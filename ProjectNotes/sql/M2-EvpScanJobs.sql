BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    CREATE TABLE [EvpScanJobs] (
        [Id] uniqueidentifier NOT NULL,
        [RequestedByAppUserId] uniqueidentifier NOT NULL,
        [Sensitivity] int NOT NULL,
        [OptionsJson] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [Percent] int NOT NULL,
        [Error] nvarchar(1000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [StartedUtc] datetime2 NULL,
        [FinishedUtc] datetime2 NULL,
        [CancelRequestedUtc] datetime2 NULL,
        CONSTRAINT [PK_EvpScanJobs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvpScanJobs_AppUsers_RequestedByAppUserId] FOREIGN KEY ([RequestedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    CREATE TABLE [EvpScanJobSegments] (
        [Id] uniqueidentifier NOT NULL,
        [EvpScanJobId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [Order] int NOT NULL,
        [Status] int NOT NULL,
        [Percent] int NOT NULL,
        [ProcessedSeconds] float NOT NULL,
        [SettledSeconds] float NOT NULL,
        [TotalSeconds] float NULL,
        [CandidateCount] int NULL,
        [TopScore] real NULL,
        [EvpScanId] uniqueidentifier NULL,
        [Error] nvarchar(1000) NULL,
        [StartedUtc] datetime2 NULL,
        [FinishedUtc] datetime2 NULL,
        CONSTRAINT [PK_EvpScanJobSegments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvpScanJobSegments_EvpScanJobs_EvpScanJobId] FOREIGN KEY ([EvpScanJobId]) REFERENCES [EvpScanJobs] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EvpScanJobSegments_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    CREATE INDEX [IX_EvpScanJobs_RequestedByAppUserId_FinishedUtc] ON [EvpScanJobs] ([RequestedByAppUserId], [FinishedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    CREATE INDEX [IX_EvpScanJobs_Status_CreatedUtc] ON [EvpScanJobs] ([Status], [CreatedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    CREATE INDEX [IX_EvpScanJobSegments_EvpScanJobId] ON [EvpScanJobSegments] ([EvpScanJobId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EvpScanJobSegments_OneLivePerFile] ON [EvpScanJobSegments] ([UploadFileId]) WHERE [FinishedUtc] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026210214_EvpScanJobs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026210214_EvpScanJobs', N'10.0.11');
END;

COMMIT;
GO

