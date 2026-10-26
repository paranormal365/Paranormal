BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [UploadFileAudioConfigs] ADD [EvpScanOptionsJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [BandHighHz] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [BandLowHz] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [OptionsJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [EvpScanCandidates] ADD [Exploratory] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [IsExploratory] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    CREATE TABLE [EvpScanPresets] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [OptionsJson] nvarchar(max) NOT NULL,
        [IsDefault] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        CONSTRAINT [PK_EvpScanPresets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvpScanPresets_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EvpScanPresets_AppUserId_Name] ON [EvpScanPresets] ([AppUserId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EvpScanPresets_OneDefault] ON [EvpScanPresets] ([AppUserId]) WHERE [IsDefault] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026201802_EvpScanSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026201802_EvpScanSettings', N'10.0.11');
END;

COMMIT;
GO

