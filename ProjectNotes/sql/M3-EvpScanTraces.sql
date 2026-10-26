BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026225545_EvpScanTraces'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [TraceBlob] varbinary(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026225545_EvpScanTraces'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [TraceBucketMs] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026225545_EvpScanTraces'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026225545_EvpScanTraces', N'10.0.11');
END;

COMMIT;
GO

