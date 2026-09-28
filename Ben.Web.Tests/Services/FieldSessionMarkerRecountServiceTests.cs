using System.Collections.Concurrent;
using System.Text;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Web.Tests.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Sessions stored while photographs, videos and sound were counted as marks get their true count
/// back from their own documents (2026-09-27).
/// </summary>
public sealed class FieldSessionMarkerRecountServiceTests
{
    private sealed class Storage : IFileStorageService
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new();
        public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        { using var b = new MemoryStream(); await data.CopyToAsync(b, ct); Files[relativePath] = b.ToArray(); }
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Files[relativePath], writable: false));
        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default) => Task.CompletedTask;
        public bool Exists(string relativePath) => Files.ContainsKey(relativePath);
        public IReadOnlyList<string> ListFiles(string relativeDirectory) => [];
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private static byte[] Document(params string[] markers)
    {
        var marked = string.Concat(markers.Select(m =>
            ",{\"at\":\"2026-09-27T21:00:01Z\",\"measurements\":{\"marker\":{\"value\":\"" + m + "\"}}}"));
        return Encoding.UTF8.GetBytes(
            "{\"format_version\":\"1.0.0\",\"device\":{\"model\":\"iPhone16,2\"},"
          + "\"session\":{\"started_at\":\"2026-09-27T21:00:00Z\"},"
          + "\"readings\":[{\"at\":\"2026-09-27T21:00:00Z\"}" + marked + "]}");
    }

    [Fact]
    public async Task Old_counts_are_put_right_from_each_sessions_own_document()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var storage = new Storage();
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var threeMarksAndFiveCaptures = new[] { "scene_motion", "scene_motion", "manual_marker", "photo", "photo", "photo", "video", "audio" };

        // One inside a .ben, one sent as a loose document, one already right, one unreadable.
        storage.Files["orgs/x/bundle.ben"] = BenBundleTests.StoredZip.Write(
            ("data.json", Document(threeMarksAndFiveCaptures)), ("media/photo-001.jpg", [1, 2, 3])).ToArray();
        storage.Files["orgs/x/loose.json"] = Document(threeMarksAndFiveCaptures);
        storage.Files["orgs/x/right.json"] = Document("manual_marker", "photo");

        var typeId = Guid.NewGuid();
        Guid Session(string path, bool bundle, int stored)
        {
            var file = new UploadFile { Id = Guid.NewGuid(), FileName = "f", ContentType = "application/json",
                                        StoragePath = path, DateCreated = now, CreatedByAppUserId = owner,
                                        UploadFileTypeId = typeId };
            var id = Guid.NewGuid();
            using var db = sqlite.NewContextAsync().GetAwaiter().GetResult();
            db.UploadFiles.Add(file);
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = id, DeviceSessionId = Guid.NewGuid(), DeviceModel = "iPhone16,2", IsBundle = bundle,
                StartedAt = now, SubmittedByAppUserId = owner, DocumentUploadFileId = file.Id,
                MarkerCount = stored, ReadingCount = 9, DateCreated = now, CreatedByAppUserId = owner,
            });
            db.SaveChanges();
            return id;
        }
        await using (var db = await sqlite.NewContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = owner, DisplayName = "Recorder", DateCreated = now });
            db.UploadFileTypes.Add(new UploadFileType { Id = typeId, Name = "Field session", DateCreated = now, CreatedByAppUserId = owner });
            await db.SaveChangesAsync();
        }
        var inBundle = Session("orgs/x/bundle.ben", bundle: true, stored: 8);
        var loose = Session("orgs/x/loose.json", bundle: false, stored: 8);
        var right = Session("orgs/x/right.json", bundle: false, stored: 1);
        var missing = Session("orgs/x/gone.json", bundle: false, stored: 8);

        var service = new FieldSessionMarkerRecountService(
            sqlite.Factory, storage, TestBundles.Store(storage), NullLogger<FieldSessionMarkerRecountService>.Instance);

        Assert.Equal(2, await service.RecountAsync(CancellationToken.None));

        await using (var db = await sqlite.NewContextAsync())
        {
            int Count(Guid id) => db.FieldSessionUploads.Single(s => s.Id == id).MarkerCount;
            Assert.Equal(3, Count(inBundle));
            Assert.Equal(3, Count(loose));
            Assert.Equal(1, Count(right));
            // Unreadable: left exactly as it was, never guessed at.
            Assert.Equal(8, Count(missing));
        }

        // A second run finds nothing to do.
        Assert.Equal(0, await service.RecountAsync(CancellationToken.None));
    }
}
