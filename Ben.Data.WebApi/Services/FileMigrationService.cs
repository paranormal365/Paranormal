using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// One-time startup service: for every UploadFile row that still has FileData bytes
/// but no StoragePath, writes the bytes to the configured file storage and records
/// the relative path back to the database.
///
/// The service is idempotent — rows that already have a StoragePath are skipped.
/// Once all rows are migrated, it exits immediately on subsequent startups.
/// </summary>
public sealed class FileMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<FileMigrationService> _logger;

    public FileMigrationService(
        IServiceScopeFactory scopeFactory,
        IFileStorageService fileStorage,
        ILogger<FileMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _fileStorage  = fileStorage;
        _logger       = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A hosted service that throws from StartAsync takes the whole host down with it, and this
        // one reaches for the database before the app is listening. That made a migration which is
        // explicitly best-effort — idempotent, per-file failures already tolerated, retried on the
        // next startup — able to stop the API serving anything at all, including over a transient
        // blip at boot. Logged and skipped instead; the work simply happens next time.
        try
        {
            await MigrateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FileMigrationService: migration could not run this startup — it will be retried on the next one.");
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BenDataContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Count pending rows
        var pending = await db.UploadFiles
            .Where(f => f.StoragePath == null && f.FileData != null)
            .CountAsync(cancellationToken);

        if (pending == 0)
        {
            _logger.LogInformation("FileMigrationService: no files need migrating.");
            return;
        }

        _logger.LogInformation("FileMigrationService: migrating {Count} file(s) from database to filesystem…", pending);

        var migrated = 0;
        var failed   = 0;
        const int BatchSize = 20;
        int skip = 0;

        while (true)
        {
            var batch = await db.UploadFiles
                .Where(f => f.StoragePath == null && f.FileData != null)
                .OrderBy(f => f.DateCreated)
                .Skip(skip)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) break;

            foreach (var file in batch)
            {
                try
                {
                    var relativePath = _fileStorage.UserFilePath(file.AppUserId, file.StoredFileName);

                    if (!_fileStorage.Exists(relativePath))
                    {
                        using var ms = new MemoryStream(file.FileData!);
                        await _fileStorage.WriteAsync(relativePath, ms, cancellationToken);
                    }

                    file.StoragePath = relativePath;
                    migrated++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "FileMigrationService: failed to migrate file {FileId} ({FileName})",
                        file.Id, file.FileName);
                    failed++;
                    skip++; // don't retry this row in the next batch
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            if (batch.Count < BatchSize) break;
        }

        _logger.LogInformation(
            "FileMigrationService: migration complete — {Migrated} succeeded, {Failed} failed.",
            migrated, failed);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
