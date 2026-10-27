BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115336_EvpVoiceLikelihood'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [VoiceModel] nvarchar(64) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115336_EvpVoiceLikelihood'
)
BEGIN
    ALTER TABLE [EvpScanCandidates] ADD [VoiceLikelihood] real NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115336_EvpVoiceLikelihood'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [VoiceLikelihood] real NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027115336_EvpVoiceLikelihood'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027115336_EvpVoiceLikelihood', N'10.0.11');
END;

COMMIT;
GO

