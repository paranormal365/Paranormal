BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [SourceClipPath] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [SourceFieldSessionUploadId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    ALTER TABLE [EvpScanJobs] ADD [FieldSessionUploadId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    CREATE TABLE [FieldSessionAnalysisCopies] (
        [Id] uniqueidentifier NOT NULL,
        [FieldSessionUploadId] uniqueidentifier NOT NULL,
        [FieldSessionUploadFileId] uniqueidentifier NOT NULL,
        [CopyUploadFileId] uniqueidentifier NOT NULL,
        [Scope] int NOT NULL,
        [ScopeAppUserId] uniqueidentifier NULL,
        [ScopeOrganizationId] uniqueidentifier NULL,
        [SourceSha256] nvarchar(64) NULL,
        [SourceRelativePath] nvarchar(500) NOT NULL,
        [MadeByAppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_FieldSessionAnalysisCopies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_FieldSessionAnalysisCopies_Scope] CHECK (([Scope] = 0 AND [ScopeAppUserId] IS NOT NULL AND [ScopeOrganizationId] IS NULL) OR ([Scope] = 1 AND [ScopeOrganizationId] IS NOT NULL AND [ScopeAppUserId] IS NULL)),
        CONSTRAINT [FK_FieldSessionAnalysisCopies_UploadFiles_CopyUploadFileId] FOREIGN KEY ([CopyUploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_UploadFiles_SourceFieldSessionUploadId] ON [UploadFiles] ([SourceFieldSessionUploadId]) WHERE [SourceFieldSessionUploadId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_EvpScanJobs_FieldSessionUploadId] ON [EvpScanJobs] ([FieldSessionUploadId]) WHERE [FieldSessionUploadId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FieldSessionAnalysisCopies_CopyUploadFileId] ON [FieldSessionAnalysisCopies] ([CopyUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    CREATE INDEX [IX_FieldSessionAnalysisCopies_FieldSessionUploadId] ON [FieldSessionAnalysisCopies] ([FieldSessionUploadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FieldSessionAnalysisCopies_OnePerGroup] ON [FieldSessionAnalysisCopies] ([FieldSessionUploadFileId], [ScopeOrganizationId]) WHERE [ScopeOrganizationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_FieldSessionAnalysisCopies_OnePerPerson] ON [FieldSessionAnalysisCopies] ([FieldSessionUploadFileId], [ScopeAppUserId]) WHERE [ScopeAppUserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027110211_FieldSessionAnalysisCopies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027110211_FieldSessionAnalysisCopies', N'10.0.11');
END;

COMMIT;
GO

