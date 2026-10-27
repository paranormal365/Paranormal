BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    ALTER TABLE [EvpScanCandidates] ADD [SpectralFlatness] real NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    ALTER TABLE [EvpScanCandidates] ADD [VoicedFrameFraction] real NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    ALTER TABLE [EvpRulings] ADD [Blind] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [EvpBlindReview] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    CREATE TABLE [EvpMisses] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [EvpScanId] uniqueidentifier NOT NULL,
        [AudioMarkerId] uniqueidentifier NOT NULL,
        [StartSeconds] float NOT NULL,
        [EndSeconds] float NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_EvpMisses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvpMisses_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    CREATE INDEX [IX_EvpMisses_DateCreated] ON [EvpMisses] ([DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    CREATE INDEX [IX_EvpMisses_UploadFileId] ON [EvpMisses] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027124126_EvpLearningReads'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027124126_EvpLearningReads', N'10.0.11');
END;

COMMIT;
GO

