BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027133833_EvpScoringVersions'
)
BEGIN
    ALTER TABLE [EvpScans] ADD [ScoringVersion] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027133833_EvpScoringVersions'
)
BEGIN
    CREATE TABLE [EvpScoringVersions] (
        [Id] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [WeightProminence] float NOT NULL,
        [WeightBand] float NOT NULL,
        [WeightDuration] float NOT NULL,
        [Decisions] int NOT NULL,
        [Recordings] int NOT NULL,
        [HeldOutDecisions] int NOT NULL,
        [TrainAuc] float NOT NULL,
        [HeldOutAuc] float NOT NULL,
        [BaselineHeldOutAuc] float NOT NULL,
        [PassedGate] bit NOT NULL,
        [GateNote] nvarchar(500) NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [ActivatedUtc] datetime2 NULL,
        [ActivatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EvpScoringVersions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027133833_EvpScoringVersions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EvpScoringVersions_OneActive] ON [EvpScoringVersions] ([IsActive]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027133833_EvpScoringVersions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EvpScoringVersions_Version] ON [EvpScoringVersions] ([Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261027133833_EvpScoringVersions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261027133833_EvpScoringVersions', N'10.0.11');
END;

COMMIT;
GO

