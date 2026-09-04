IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [AppUsers] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] nvarchar(max) NULL,
        [DisplayName] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        CONSTRAINT [PK_AppUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationAddressTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAddressTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAddressTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddressTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationEmailTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationEmailTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationEmailTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationEmailTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationLinkTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationLinkTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationLinkTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLinkTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationNoteTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationNoteTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationNoteTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationNoteTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationPhoneTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationPhoneTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationPhoneTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationPhoneTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [Organizations] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [UrlName] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Organizations_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Organizations_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserAddressTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserAddressTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserAddressTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserAddressTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserEmailTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserEmailTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserEmailTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserEmailTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserLinkTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserLinkTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserLinkTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserLinkTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserMessageTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserMessageTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserMessageTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserMessageTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserNoteTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserNoteTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNoteTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserNoteTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserPhoneTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserPhoneTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserPhoneTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserPhoneTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationAddressTypeId] uniqueidentifier NOT NULL,
        [StreetAddress1] nvarchar(max) NULL,
        [StreetAddress2] nvarchar(max) NULL,
        [ZipCode] nvarchar(max) NULL,
        [City] nvarchar(max) NULL,
        [State] nvarchar(max) NULL,
        [Country] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [Latitude] decimal(18,2) NULL,
        [Longitude] decimal(18,2) NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAddresses_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddresses_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddresses_OrganizationAddressTypes_OrganizationAddressTypeId] FOREIGN KEY ([OrganizationAddressTypeId]) REFERENCES [OrganizationAddressTypes] ([Id]),
        CONSTRAINT [FK_OrganizationAddresses_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationEmails] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationEmailTypeId] uniqueidentifier NOT NULL,
        [DisplayText] nvarchar(max) NULL,
        [EmailAddress] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [IsHidden] bit NOT NULL,
        [IsPrimary] bit NOT NULL,
        [DateValidated] datetime2 NULL,
        [ValidationToken] nvarchar(max) NULL,
        [IsValidated] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationEmails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationEmails_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationEmails_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationEmails_OrganizationEmailTypes_OrganizationEmailTypeId] FOREIGN KEY ([OrganizationEmailTypeId]) REFERENCES [OrganizationEmailTypes] ([Id]),
        CONSTRAINT [FK_OrganizationEmails_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationLinks] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationLinkTypeId] uniqueidentifier NOT NULL,
        [DisplayText] nvarchar(max) NULL,
        [LinkUrl] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [IsVerifiedApproved] bit NOT NULL,
        [DateVerifiedApproved] datetime2 NULL,
        [VerifiedApprovedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationLinks_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLinks_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLinks_AppUsers_VerifiedApprovedByAppUserId] FOREIGN KEY ([VerifiedApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLinks_OrganizationLinkTypes_OrganizationLinkTypeId] FOREIGN KEY ([OrganizationLinkTypeId]) REFERENCES [OrganizationLinkTypes] ([Id]),
        CONSTRAINT [FK_OrganizationLinks_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationNotes] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationNoteTypeId] uniqueidentifier NOT NULL,
        [ParentNoteId] uniqueidentifier NULL,
        [TableName] nvarchar(max) NULL,
        [NoteBody] nvarchar(max) NULL,
        [NoteSubject] nvarchar(max) NULL,
        [ItemRecordId] uniqueidentifier NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationNotes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationNotes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationNotes_OrganizationNoteTypes_OrganizationNoteTypeId] FOREIGN KEY ([OrganizationNoteTypeId]) REFERENCES [OrganizationNoteTypes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationNotes_OrganizationNotes_ParentNoteId] FOREIGN KEY ([ParentNoteId]) REFERENCES [OrganizationNotes] ([Id]),
        CONSTRAINT [FK_OrganizationNotes_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationPages] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [IsHome] bit NOT NULL,
        [PageTitle] nvarchar(max) NULL,
        [UrlName] nvarchar(max) NULL,
        [PageHtml] nvarchar(max) NULL,
        [IsPublished] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationPages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationPages_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationPages_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationPages_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [OrganizationPhones] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationPhoneTypeId] uniqueidentifier NOT NULL,
        [IsValidated] bit NOT NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [ValidationToken] nvarchar(max) NULL,
        [DateValidated] datetime2 NULL,
        [PhoneCountry] nvarchar(max) NULL,
        [IsPrimary] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [IsCellular] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationPhones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationPhones_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationPhones_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationPhones_OrganizationPhoneTypes_OrganizationPhoneTypeId] FOREIGN KEY ([OrganizationPhoneTypeId]) REFERENCES [OrganizationPhoneTypes] ([Id]),
        CONSTRAINT [FK_OrganizationPhones_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [UserAddressTypeId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [StreetAddress1] nvarchar(max) NULL,
        [StreetAddress2] nvarchar(max) NULL,
        [City] nvarchar(max) NULL,
        [State] nvarchar(max) NULL,
        [ZipCode] nvarchar(max) NULL,
        [Country] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Longitude] decimal(18,2) NULL,
        [Latitude] decimal(18,2) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserAddresses_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserAddresses_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserAddresses_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserAddresses_UserAddressTypes_UserAddressTypeId] FOREIGN KEY ([UserAddressTypeId]) REFERENCES [UserAddressTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserEmails] (
        [Id] uniqueidentifier NOT NULL,
        [UserEmailTypeId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [EmailAddress] nvarchar(max) NULL,
        [IsHidden] bit NOT NULL,
        [IsPrimary] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [IsValidated] bit NOT NULL,
        [ValidationToken] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [DateValidated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserEmails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserEmails_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserEmails_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserEmails_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserEmails_UserEmailTypes_UserEmailTypeId] FOREIGN KEY ([UserEmailTypeId]) REFERENCES [UserEmailTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserLinks] (
        [Id] uniqueidentifier NOT NULL,
        [UserLinkTypeId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [DisplayText] nvarchar(max) NULL,
        [LinkUrl] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [IsVerifiedApproved] bit NOT NULL,
        [VerifiedApprovedByAppUserId] uniqueidentifier NULL,
        [DateVerifiedApproved] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserLinks_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserLinks_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserLinks_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserLinks_AppUsers_VerifiedApprovedByAppUserId] FOREIGN KEY ([VerifiedApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserLinks_UserLinkTypes_UserLinkTypeId] FOREIGN KEY ([UserLinkTypeId]) REFERENCES [UserLinkTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserMessages] (
        [Id] uniqueidentifier NOT NULL,
        [UserMessageTypeId] uniqueidentifier NOT NULL,
        [MessageSubject] nvarchar(max) NULL,
        [MessageBody] nvarchar(max) NULL,
        [ParentMessageId] uniqueidentifier NULL,
        [DateArchived] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserMessages_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserMessages_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserMessages_UserMessageTypes_UserMessageTypeId] FOREIGN KEY ([UserMessageTypeId]) REFERENCES [UserMessageTypes] ([Id]),
        CONSTRAINT [FK_UserMessages_UserMessages_ParentMessageId] FOREIGN KEY ([ParentMessageId]) REFERENCES [UserMessages] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserNotes] (
        [Id] uniqueidentifier NOT NULL,
        [UserNoteTypeId] uniqueidentifier NOT NULL,
        [NoteSubject] nvarchar(max) NULL,
        [NoteBody] nvarchar(max) NULL,
        [ParentNoteId] uniqueidentifier NULL,
        [ItemRecordId] uniqueidentifier NULL,
        [TableName] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNotes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserNotes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserNotes_UserNoteTypes_UserNoteTypeId] FOREIGN KEY ([UserNoteTypeId]) REFERENCES [UserNoteTypes] ([Id]),
        CONSTRAINT [FK_UserNotes_UserNotes_ParentNoteId] FOREIGN KEY ([ParentNoteId]) REFERENCES [UserNotes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserPhones] (
        [Id] uniqueidentifier NOT NULL,
        [UserPhoneTypeId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [PhoneCountry] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [IsPrimary] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [IsCellular] bit NOT NULL,
        [IsValidated] bit NOT NULL,
        [ValidationToken] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [DateValidated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserPhones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserPhones_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserPhones_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserPhones_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserPhones_UserPhoneTypes_UserPhoneTypeId] FOREIGN KEY ([UserPhoneTypeId]) REFERENCES [UserPhoneTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE TABLE [UserMessageTos] (
        [Id] uniqueidentifier NOT NULL,
        [MessageId] uniqueidentifier NOT NULL,
        [ToAppUserId] uniqueidentifier NOT NULL,
        [DateLastRead] datetime2 NULL,
        [LastReadCount] int NOT NULL,
        CONSTRAINT [PK_UserMessageTos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserMessageTos_AppUsers_ToAppUserId] FOREIGN KEY ([ToAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserMessageTos_UserMessages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [UserMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddresses_CreatedByAppUserId] ON [OrganizationAddresses] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddresses_OrganizationAddressTypeId] ON [OrganizationAddresses] ([OrganizationAddressTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddresses_OrganizationId] ON [OrganizationAddresses] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddresses_UpdatedByAppUserId] ON [OrganizationAddresses] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressTypes_CreatedByAppUserId] ON [OrganizationAddressTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressTypes_UpdatedByAppUserId] ON [OrganizationAddressTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmails_CreatedByAppUserId] ON [OrganizationEmails] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmails_OrganizationEmailTypeId] ON [OrganizationEmails] ([OrganizationEmailTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmails_OrganizationId] ON [OrganizationEmails] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmails_UpdatedByAppUserId] ON [OrganizationEmails] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmailTypes_CreatedByAppUserId] ON [OrganizationEmailTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationEmailTypes_UpdatedByAppUserId] ON [OrganizationEmailTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinks_CreatedByAppUserId] ON [OrganizationLinks] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinks_OrganizationId] ON [OrganizationLinks] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinks_OrganizationLinkTypeId] ON [OrganizationLinks] ([OrganizationLinkTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinks_UpdatedByAppUserId] ON [OrganizationLinks] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinks_VerifiedApprovedByAppUserId] ON [OrganizationLinks] ([VerifiedApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinkTypes_CreatedByAppUserId] ON [OrganizationLinkTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationLinkTypes_UpdatedByAppUserId] ON [OrganizationLinkTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNotes_CreatedByAppUserId] ON [OrganizationNotes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNotes_OrganizationId] ON [OrganizationNotes] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNotes_OrganizationNoteTypeId] ON [OrganizationNotes] ([OrganizationNoteTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNotes_ParentNoteId] ON [OrganizationNotes] ([ParentNoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNotes_UpdatedByAppUserId] ON [OrganizationNotes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNoteTypes_CreatedByAppUserId] ON [OrganizationNoteTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationNoteTypes_UpdatedByAppUserId] ON [OrganizationNoteTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPages_CreatedByAppUserId] ON [OrganizationPages] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPages_OrganizationId] ON [OrganizationPages] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPages_UpdatedByAppUserId] ON [OrganizationPages] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhones_CreatedByAppUserId] ON [OrganizationPhones] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhones_OrganizationId] ON [OrganizationPhones] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhones_OrganizationPhoneTypeId] ON [OrganizationPhones] ([OrganizationPhoneTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhones_UpdatedByAppUserId] ON [OrganizationPhones] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhoneTypes_CreatedByAppUserId] ON [OrganizationPhoneTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrganizationPhoneTypes_UpdatedByAppUserId] ON [OrganizationPhoneTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Organizations_CreatedByAppUserId] ON [Organizations] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Organizations_UpdatedByAppUserId] ON [Organizations] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddresses_AppUserId] ON [UserAddresses] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddresses_CreatedByAppUserId] ON [UserAddresses] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddresses_UpdatedByAppUserId] ON [UserAddresses] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddresses_UserAddressTypeId] ON [UserAddresses] ([UserAddressTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddressTypes_CreatedByAppUserId] ON [UserAddressTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserAddressTypes_UpdatedByAppUserId] ON [UserAddressTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmails_AppUserId] ON [UserEmails] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmails_CreatedByAppUserId] ON [UserEmails] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmails_UpdatedByAppUserId] ON [UserEmails] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmails_UserEmailTypeId] ON [UserEmails] ([UserEmailTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmailTypes_CreatedByAppUserId] ON [UserEmailTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserEmailTypes_UpdatedByAppUserId] ON [UserEmailTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinks_AppUserId] ON [UserLinks] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinks_CreatedByAppUserId] ON [UserLinks] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinks_UpdatedByAppUserId] ON [UserLinks] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinks_UserLinkTypeId] ON [UserLinks] ([UserLinkTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinks_VerifiedApprovedByAppUserId] ON [UserLinks] ([VerifiedApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinkTypes_CreatedByAppUserId] ON [UserLinkTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserLinkTypes_UpdatedByAppUserId] ON [UserLinkTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessages_CreatedByAppUserId] ON [UserMessages] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessages_ParentMessageId] ON [UserMessages] ([ParentMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessages_UpdatedByAppUserId] ON [UserMessages] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessages_UserMessageTypeId] ON [UserMessages] ([UserMessageTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessageTos_MessageId] ON [UserMessageTos] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessageTos_ToAppUserId] ON [UserMessageTos] ([ToAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessageTypes_CreatedByAppUserId] ON [UserMessageTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserMessageTypes_UpdatedByAppUserId] ON [UserMessageTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNotes_CreatedByAppUserId] ON [UserNotes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNotes_ParentNoteId] ON [UserNotes] ([ParentNoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNotes_UpdatedByAppUserId] ON [UserNotes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNotes_UserNoteTypeId] ON [UserNotes] ([UserNoteTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNoteTypes_CreatedByAppUserId] ON [UserNoteTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserNoteTypes_UpdatedByAppUserId] ON [UserNoteTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhones_AppUserId] ON [UserPhones] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhones_CreatedByAppUserId] ON [UserPhones] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhones_UpdatedByAppUserId] ON [UserPhones] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhones_UserPhoneTypeId] ON [UserPhones] ([UserPhoneTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhoneTypes_CreatedByAppUserId] ON [UserPhoneTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserPhoneTypes_UpdatedByAppUserId] ON [UserPhoneTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709155716_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709155716_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    EXEC sp_rename N'[AppUsers].[UserId]', N'SecurityStamp', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [AccessFailedCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [ConcurrencyStamp] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [Email] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [EmailConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [LockoutEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [LockoutEnd] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [NormalizedEmail] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [NormalizedUserName] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [PasswordHash] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [PhoneNumber] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [PhoneNumberConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [TwoFactorEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [UserName] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] uniqueidentifier NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AppUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AppUsers] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AppUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709163203_AddIdentitySchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709163203_AddIdentitySchema', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711131156_AddGeocodingMetadataToAddresses'
)
BEGIN
    ALTER TABLE [UserAddresses] ADD [GeocodingResponseJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711131156_AddGeocodingMetadataToAddresses'
)
BEGIN
    ALTER TABLE [UserAddresses] ADD [GeocodingResultType] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711131156_AddGeocodingMetadataToAddresses'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [GeocodingResponseJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711131156_AddGeocodingMetadataToAddresses'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [GeocodingResultType] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711131156_AddGeocodingMetadataToAddresses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260711131156_AddGeocodingMetadataToAddresses', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE TABLE [OrganizationAccessGrants] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [TableName] int NOT NULL,
        [ActionName] int NOT NULL,
        [IsAllowed] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAccessGrants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAccessGrants_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationAccessGrants_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAccessGrants_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAccessGrants_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE TABLE [OrganizationUserMemberships] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [IsOrganizationAdmin] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationUserMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationUserMemberships_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationUserMemberships_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationUserMemberships_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationUserMemberships_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationAccessGrants_AppUserId] ON [OrganizationAccessGrants] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationAccessGrants_CreatedByAppUserId] ON [OrganizationAccessGrants] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName_ActionName] ON [OrganizationAccessGrants] ([OrganizationId], [AppUserId], [TableName], [ActionName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationAccessGrants_UpdatedByAppUserId] ON [OrganizationAccessGrants] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationUserMemberships_AppUserId] ON [OrganizationUserMemberships] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationUserMemberships_CreatedByAppUserId] ON [OrganizationUserMemberships] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationUserMemberships_OrganizationId_AppUserId] ON [OrganizationUserMemberships] ([OrganizationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    CREATE INDEX [IX_OrganizationUserMemberships_UpdatedByAppUserId] ON [OrganizationUserMemberships] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711133326_AddOrganizationSecurityModel'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260711133326_AddOrganizationSecurityModel', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE TABLE [UploadFileTypes] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE TABLE [UploadFiles] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileTypeId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(max) NULL,
        [StoredFileName] nvarchar(max) NULL,
        [ContentType] nvarchar(max) NULL,
        [FileSize] bigint NOT NULL,
        [FileData] varbinary(max) NULL,
        [Description] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFiles_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UploadFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFiles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFiles_UploadFileTypes_UploadFileTypeId] FOREIGN KEY ([UploadFileTypeId]) REFERENCES [UploadFileTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_AppUserId] ON [UploadFiles] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_CreatedByAppUserId] ON [UploadFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_UpdatedByAppUserId] ON [UploadFiles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_UploadFileTypeId] ON [UploadFiles] ([UploadFileTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFileTypes_CreatedByAppUserId] ON [UploadFileTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    CREATE INDEX [IX_UploadFileTypes_UpdatedByAppUserId] ON [UploadFileTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713150000_AddUploadFileEntities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713150000_AddUploadFileEntities', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE TABLE [UploadFileOrganizationShares] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [SharedByAppUserId] uniqueidentifier NOT NULL,
        [Visibility] int NOT NULL,
        [IsActive] bit NOT NULL,
        [RemovedByAppUserId] uniqueidentifier NULL,
        [RemovalDate] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileOrganizationShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileOrganizationShares_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileOrganizationShares_AppUsers_RemovedByAppUserId] FOREIGN KEY ([RemovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileOrganizationShares_AppUsers_SharedByAppUserId] FOREIGN KEY ([SharedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileOrganizationShares_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileOrganizationShares_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UploadFileOrganizationShares_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE TABLE [UploadFilePermissionRequests] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NULL,
        [RequestedByAppUserId] uniqueidentifier NOT NULL,
        [PermissionType] int NOT NULL,
        [RequestStatus] int NOT NULL,
        [RequestNotes] nvarchar(max) NULL,
        [ReviewNotes] nvarchar(max) NULL,
        [ReviewedByAppUserId] uniqueidentifier NULL,
        [DateReviewed] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFilePermissionRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_AppUsers_RequestedByAppUserId] FOREIGN KEY ([RequestedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_AppUsers_ReviewedByAppUserId] FOREIGN KEY ([ReviewedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]),
        CONSTRAINT [FK_UploadFilePermissionRequests_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFileOrganizationShares_CreatedByAppUserId] ON [UploadFileOrganizationShares] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFileOrganizationShares_OrganizationId] ON [UploadFileOrganizationShares] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFileOrganizationShares_RemovedByAppUserId] ON [UploadFileOrganizationShares] ([RemovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFileOrganizationShares_SharedByAppUserId] ON [UploadFileOrganizationShares] ([SharedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFileOrganizationShares_UpdatedByAppUserId] ON [UploadFileOrganizationShares] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UploadFileOrganizationShares_UploadFileId_OrganizationId] ON [UploadFileOrganizationShares] ([UploadFileId], [OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_CreatedByAppUserId] ON [UploadFilePermissionRequests] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_OrganizationId] ON [UploadFilePermissionRequests] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_RequestedByAppUserId] ON [UploadFilePermissionRequests] ([RequestedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_ReviewedByAppUserId] ON [UploadFilePermissionRequests] ([ReviewedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_UpdatedByAppUserId] ON [UploadFilePermissionRequests] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    CREATE INDEX [IX_UploadFilePermissionRequests_UploadFileId] ON [UploadFilePermissionRequests] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713151155_AddUploadFileSharing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713151155_AddUploadFileSharing', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714130129_AddUploadFileTypeExtensions'
)
BEGIN
    ALTER TABLE [UploadFileTypes] ADD [AllowAllExtensions] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714130129_AddUploadFileTypeExtensions'
)
BEGIN
    CREATE TABLE [UploadFileTypeExtensions] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileTypeId] uniqueidentifier NOT NULL,
        [Pattern] nvarchar(450) NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UploadFileTypeExtensions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileTypeExtensions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileTypeExtensions_UploadFileTypes_UploadFileTypeId] FOREIGN KEY ([UploadFileTypeId]) REFERENCES [UploadFileTypes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714130129_AddUploadFileTypeExtensions'
)
BEGIN
    CREATE INDEX [IX_UploadFileTypeExtensions_CreatedByAppUserId] ON [UploadFileTypeExtensions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714130129_AddUploadFileTypeExtensions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_UploadFileTypeExtensions_UploadFileTypeId_Pattern] ON [UploadFileTypeExtensions] ([UploadFileTypeId], [Pattern]) WHERE [Pattern] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714130129_AddUploadFileTypeExtensions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714130129_AddUploadFileTypeExtensions', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714154316_ReplaceIsOrganizationAdminWithRole'
)
BEGIN
    ALTER TABLE [OrganizationUserMemberships] ADD [Role] int NOT NULL DEFAULT 4;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714154316_ReplaceIsOrganizationAdminWithRole'
)
BEGIN
    UPDATE OrganizationUserMemberships SET Role = CASE WHEN IsOrganizationAdmin = 1 THEN 1 ELSE 4 END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714154316_ReplaceIsOrganizationAdminWithRole'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrganizationUserMemberships]') AND [c].[name] = N'IsOrganizationAdmin');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [OrganizationUserMemberships] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [OrganizationUserMemberships] DROP COLUMN [IsOrganizationAdmin];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714154316_ReplaceIsOrganizationAdminWithRole'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714154316_ReplaceIsOrganizationAdminWithRole', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714160800_AddAuditLogs'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Action] int NOT NULL,
        [EntityType] nvarchar(128) NOT NULL,
        [EntityId] uniqueidentifier NOT NULL,
        [Source] nvarchar(64) NOT NULL,
        [OccurredAt] datetime2 NOT NULL,
        [ChangesJson] nvarchar(max) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714160800_AddAuditLogs'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714160800_AddAuditLogs'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_OccurredAt] ON [AuditLogs] ([OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714160800_AddAuditLogs'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714160800_AddAuditLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714160800_AddAuditLogs', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714184021_ReplaceActionNameWithActionsBitmask'
)
BEGIN
    DROP INDEX [IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName_ActionName] ON [OrganizationAccessGrants];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714184021_ReplaceActionNameWithActionsBitmask'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrganizationAccessGrants]') AND [c].[name] = N'IsAllowed');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OrganizationAccessGrants] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [OrganizationAccessGrants] DROP COLUMN [IsAllowed];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714184021_ReplaceActionNameWithActionsBitmask'
)
BEGIN
    EXEC sp_rename N'[OrganizationAccessGrants].[ActionName]', N'Actions', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714184021_ReplaceActionNameWithActionsBitmask'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName] ON [OrganizationAccessGrants] ([OrganizationId], [AppUserId], [TableName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714184021_ReplaceActionNameWithActionsBitmask'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714184021_ReplaceActionNameWithActionsBitmask', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD [IsPublic] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD [ParentPageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE TABLE [CmsSections] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationPageId] uniqueidentifier NOT NULL,
        [SectionType] int NOT NULL,
        [Title] nvarchar(max) NULL,
        [ContentJson] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CmsSections] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CmsSections_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CmsSections_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CmsSections_OrganizationPages_OrganizationPageId] FOREIGN KEY ([OrganizationPageId]) REFERENCES [OrganizationPages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE TABLE [OrganizationLogos] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [AltText] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationLogos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationLogos_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLogos_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationLogos_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationLogos_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE TABLE [OrgMemberGroups] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgMemberGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMemberGroups_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMemberGroups_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMemberGroups_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE TABLE [CmsPagePermissions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationPageId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NULL,
        [OrgMemberGroupId] uniqueidentifier NULL,
        [Actions] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CmsPagePermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CmsPagePermissions_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CmsPagePermissions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CmsPagePermissions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CmsPagePermissions_OrgMemberGroups_OrgMemberGroupId] FOREIGN KEY ([OrgMemberGroupId]) REFERENCES [OrgMemberGroups] ([Id]),
        CONSTRAINT [FK_CmsPagePermissions_OrganizationPages_OrganizationPageId] FOREIGN KEY ([OrganizationPageId]) REFERENCES [OrganizationPages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE TABLE [OrgMemberGroupMemberships] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMemberGroupId] uniqueidentifier NOT NULL,
        [OrganizationUserMembershipId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrgMemberGroupMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMemberGroupMemberships_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMemberGroupMemberships_OrgMemberGroups_OrgMemberGroupId] FOREIGN KEY ([OrgMemberGroupId]) REFERENCES [OrgMemberGroups] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrgMemberGroupMemberships_OrganizationUserMemberships_OrganizationUserMembershipId] FOREIGN KEY ([OrganizationUserMembershipId]) REFERENCES [OrganizationUserMemberships] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrganizationPages_ParentPageId] ON [OrganizationPages] ([ParentPageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsPagePermissions_AppUserId] ON [CmsPagePermissions] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsPagePermissions_CreatedByAppUserId] ON [CmsPagePermissions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsPagePermissions_OrganizationPageId] ON [CmsPagePermissions] ([OrganizationPageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsPagePermissions_OrgMemberGroupId] ON [CmsPagePermissions] ([OrgMemberGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsPagePermissions_UpdatedByAppUserId] ON [CmsPagePermissions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsSections_CreatedByAppUserId] ON [CmsSections] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsSections_OrganizationPageId] ON [CmsSections] ([OrganizationPageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_CmsSections_UpdatedByAppUserId] ON [CmsSections] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrganizationLogos_CreatedByAppUserId] ON [OrganizationLogos] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrganizationLogos_OrganizationId] ON [OrganizationLogos] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrganizationLogos_UpdatedByAppUserId] ON [OrganizationLogos] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrganizationLogos_UploadFileId] ON [OrganizationLogos] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrgMemberGroupMemberships_CreatedByAppUserId] ON [OrgMemberGroupMemberships] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrgMemberGroupMemberships_OrganizationUserMembershipId] ON [OrgMemberGroupMemberships] ([OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgMemberGroupMemberships_OrgMemberGroupId_OrganizationUserMembershipId] ON [OrgMemberGroupMemberships] ([OrgMemberGroupId], [OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrgMemberGroups_CreatedByAppUserId] ON [OrgMemberGroups] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrgMemberGroups_OrganizationId] ON [OrgMemberGroups] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    CREATE INDEX [IX_OrgMemberGroups_UpdatedByAppUserId] ON [OrgMemberGroups] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD CONSTRAINT [FK_OrganizationPages_OrganizationPages_ParentPageId] FOREIGN KEY ([ParentPageId]) REFERENCES [OrganizationPages] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718122428_AddCmsEntities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718122428_AddCmsEntities', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718193225_AddUploadFileAudioConfig'
)
BEGIN
    CREATE TABLE [UploadFileAudioConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [WaveColor] nvarchar(max) NULL,
        [ProgressColor] nvarchar(max) NULL,
        [CursorColor] nvarchar(max) NULL,
        [CursorWidth] int NULL,
        [Height] int NULL,
        [BarWidth] int NULL,
        [BarGap] int NULL,
        [BarRadius] int NULL,
        [BarHeight] float NULL,
        [BarAlign] nvarchar(max) NULL,
        [Normalize] bit NOT NULL,
        [DragToSeek] bit NOT NULL,
        [HideScrollbar] bit NOT NULL,
        [AudioRate] float NULL,
        [EnableHover] bit NOT NULL,
        [EnableTimeline] bit NOT NULL,
        [EnableZoom] bit NOT NULL,
        [EnableMinimap] bit NOT NULL,
        [EnableSpectrogram] bit NOT NULL,
        [EnableSpectrogramWindowed] bit NOT NULL,
        [EnableEnvelope] bit NOT NULL,
        [EnableRegions] bit NOT NULL,
        [HoverOptionsJson] nvarchar(max) NULL,
        [TimelineOptionsJson] nvarchar(max) NULL,
        [ZoomOptionsJson] nvarchar(max) NULL,
        [MinimapOptionsJson] nvarchar(max) NULL,
        [SpectrogramOptionsJson] nvarchar(max) NULL,
        [SpectrogramWindowedOptionsJson] nvarchar(max) NULL,
        [EnvelopeOptionsJson] nvarchar(max) NULL,
        [InitialHeight] nvarchar(max) NULL,
        [MinHeight] nvarchar(max) NULL,
        [MaxHeight] nvarchar(max) NULL,
        [ShowControls] bit NOT NULL,
        [MinZoom] float NOT NULL,
        [MaxZoom] float NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileAudioConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileAudioConfigs_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileAudioConfigs_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileAudioConfigs_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718193225_AddUploadFileAudioConfig'
)
BEGIN
    CREATE INDEX [IX_UploadFileAudioConfigs_CreatedByAppUserId] ON [UploadFileAudioConfigs] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718193225_AddUploadFileAudioConfig'
)
BEGIN
    CREATE INDEX [IX_UploadFileAudioConfigs_UpdatedByAppUserId] ON [UploadFileAudioConfigs] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718193225_AddUploadFileAudioConfig'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UploadFileAudioConfigs_UploadFileId] ON [UploadFileAudioConfigs] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718193225_AddUploadFileAudioConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718193225_AddUploadFileAudioConfig', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [ParentFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [RegionEnd] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [RegionStart] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    CREATE TABLE [UploadFileRegionNotes] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [RegionStart] float NOT NULL,
        [RegionEnd] float NOT NULL,
        [RegionLabel] nvarchar(max) NULL,
        [TimeOffset] float NULL,
        [NoteHtml] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileRegionNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileRegionNotes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileRegionNotes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileRegionNotes_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_ParentFileId] ON [UploadFiles] ([ParentFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    CREATE INDEX [IX_UploadFileRegionNotes_CreatedByAppUserId] ON [UploadFileRegionNotes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    CREATE INDEX [IX_UploadFileRegionNotes_UpdatedByAppUserId] ON [UploadFileRegionNotes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    CREATE INDEX [IX_UploadFileRegionNotes_UploadFileId] ON [UploadFileRegionNotes] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD CONSTRAINT [FK_UploadFiles_UploadFiles_ParentFileId] FOREIGN KEY ([ParentFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719142238_AddUploadFileRegionNotesAndParentClip'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719142238_AddUploadFileRegionNotesAndParentClip', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719163758_AddUploadFileVotes'
)
BEGIN
    CREATE TABLE [UploadFileVotes] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [Score] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        CONSTRAINT [PK_UploadFileVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileVotes_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileVotes_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719163758_AddUploadFileVotes'
)
BEGIN
    CREATE INDEX [IX_UploadFileVotes_AppUserId] ON [UploadFileVotes] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719163758_AddUploadFileVotes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UploadFileVotes_UploadFileId_AppUserId] ON [UploadFileVotes] ([UploadFileId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719163758_AddUploadFileVotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719163758_AddUploadFileVotes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721133025_AddFileStoragePath'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [StoragePath] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721133025_AddFileStoragePath'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721133025_AddFileStoragePath', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsAcceptingApplications] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE TABLE [OrganizationFiles] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UploadFileTypeId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(max) NULL,
        [StoredFileName] nvarchar(max) NULL,
        [ContentType] nvarchar(max) NULL,
        [FileSize] bigint NOT NULL,
        [StoragePath] nvarchar(500) NULL,
        [FileData] varbinary(max) NULL,
        [Description] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [SourceUploadFileId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationFiles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationFiles_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationFiles_UploadFileTypes_UploadFileTypeId] FOREIGN KEY ([UploadFileTypeId]) REFERENCES [UploadFileTypes] ([Id]),
        CONSTRAINT [FK_OrganizationFiles_UploadFiles_SourceUploadFileId] FOREIGN KEY ([SourceUploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE TABLE [OrganizationMembershipRequests] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [RequestMessage] nvarchar(2000) NULL,
        [Status] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationMembershipRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationMembershipRequests_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationMembershipRequests_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipRequests_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipRequests_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_CreatedByAppUserId] ON [OrganizationFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_OrganizationId] ON [OrganizationFiles] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_SourceUploadFileId] ON [OrganizationFiles] ([SourceUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_UpdatedByAppUserId] ON [OrganizationFiles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_UploadFileTypeId] ON [OrganizationFiles] ([UploadFileTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipRequests_AppUserId] ON [OrganizationMembershipRequests] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipRequests_CreatedByAppUserId] ON [OrganizationMembershipRequests] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipRequests_OrganizationId_AppUserId] ON [OrganizationMembershipRequests] ([OrganizationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipRequests_UpdatedByAppUserId] ON [OrganizationMembershipRequests] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722135037_AddOrganizationMembershipRequestsAndFiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722135037_AddOrganizationMembershipRequestsAndFiles', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    ALTER TABLE [OrganizationFiles] ADD [DatePublished] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    ALTER TABLE [OrganizationFiles] ADD [PublishedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    CREATE TABLE [OrganizationFileDeleteLogs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [OrganizationName] nvarchar(256) NOT NULL,
        [OriginalFileId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(512) NOT NULL,
        [ContentType] nvarchar(128) NOT NULL,
        [FileSize] bigint NOT NULL,
        [StoragePath] nvarchar(500) NULL,
        [SourceUploadFileId] uniqueidentifier NULL,
        [WasPublic] bit NOT NULL,
        [WasPublishedByAppUserId] uniqueidentifier NULL,
        [WasPublishedByDisplayName] nvarchar(256) NULL,
        [WasDatePublished] datetime2 NULL,
        [DeletedByAppUserId] uniqueidentifier NOT NULL,
        [DeletedByDisplayName] nvarchar(256) NOT NULL,
        [DateDeleted] datetime2 NOT NULL,
        CONSTRAINT [PK_OrganizationFileDeleteLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    CREATE INDEX [IX_OrganizationFiles_PublishedByAppUserId] ON [OrganizationFiles] ([PublishedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    CREATE INDEX [IX_OrganizationFileDeleteLogs_DeletedByAppUserId] ON [OrganizationFileDeleteLogs] ([DeletedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    CREATE INDEX [IX_OrganizationFileDeleteLogs_OrganizationId] ON [OrganizationFileDeleteLogs] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    ALTER TABLE [OrganizationFiles] ADD CONSTRAINT [FK_OrganizationFiles_AppUsers_PublishedByAppUserId] FOREIGN KEY ([PublishedByAppUserId]) REFERENCES [AppUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140331_AddOrgFilePublishingAndDeleteLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722140331_AddOrgFilePublishingAndDeleteLog', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722150352_AddOrganizationAddressMapConfig'
)
BEGIN
    CREATE TABLE [OrganizationAddressMapConfigs] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationAddressId] uniqueidentifier NOT NULL,
        [IsOnMap] bit NOT NULL,
        [ShowMarker] bit NOT NULL,
        [ShowRegion] bit NOT NULL,
        [RegionRadiusMiles] float NOT NULL,
        [MarkerColor] nvarchar(50) NOT NULL,
        [MarkerIconKey] nvarchar(64) NULL,
        [RegionFillColor] nvarchar(50) NOT NULL,
        [RegionFillOpacity] float NOT NULL,
        [RegionStrokeColor] nvarchar(50) NOT NULL,
        [RegionStrokeOpacity] float NOT NULL,
        [RegionStrokeWidth] float NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAddressMapConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAddressMapConfigs_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddressMapConfigs_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddressMapConfigs_OrganizationAddresses_OrganizationAddressId] FOREIGN KEY ([OrganizationAddressId]) REFERENCES [OrganizationAddresses] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722150352_AddOrganizationAddressMapConfig'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressMapConfigs_CreatedByAppUserId] ON [OrganizationAddressMapConfigs] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722150352_AddOrganizationAddressMapConfig'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationAddressMapConfigs_OrganizationAddressId] ON [OrganizationAddressMapConfigs] ([OrganizationAddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722150352_AddOrganizationAddressMapConfig'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressMapConfigs_UpdatedByAppUserId] ON [OrganizationAddressMapConfigs] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722150352_AddOrganizationAddressMapConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722150352_AddOrganizationAddressMapConfig', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE TABLE [OrganizationRoles] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationRoles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationRoles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationRoles_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE TABLE [OrganizationRoleMemberships] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationRoleId] uniqueidentifier NOT NULL,
        [OrganizationUserMembershipId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrganizationRoleMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationRoleMemberships_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationRoleMemberships_OrganizationRoles_OrganizationRoleId] FOREIGN KEY ([OrganizationRoleId]) REFERENCES [OrganizationRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationRoleMemberships_OrganizationUserMemberships_OrganizationUserMembershipId] FOREIGN KEY ([OrganizationUserMembershipId]) REFERENCES [OrganizationUserMemberships] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE TABLE [OrganizationRolePermissions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationRoleId] uniqueidentifier NOT NULL,
        [TableName] int NOT NULL,
        [Actions] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrganizationRolePermissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationRolePermissions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationRolePermissions_OrganizationRoles_OrganizationRoleId] FOREIGN KEY ([OrganizationRoleId]) REFERENCES [OrganizationRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRoleMemberships_CreatedByAppUserId] ON [OrganizationRoleMemberships] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationRoleMemberships_OrganizationRoleId_OrganizationUserMembershipId] ON [OrganizationRoleMemberships] ([OrganizationRoleId], [OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRoleMemberships_OrganizationUserMembershipId] ON [OrganizationRoleMemberships] ([OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRolePermissions_CreatedByAppUserId] ON [OrganizationRolePermissions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationRolePermissions_OrganizationRoleId_TableName] ON [OrganizationRolePermissions] ([OrganizationRoleId], [TableName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRoles_CreatedByAppUserId] ON [OrganizationRoles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRoles_OrganizationId] ON [OrganizationRoles] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationRoles_UpdatedByAppUserId] ON [OrganizationRoles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723132324_AddOrganizationNamedRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723132324_AddOrganizationNamedRoles', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723160816_FixLatLonPrecision'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserAddresses]') AND [c].[name] = N'Longitude');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [UserAddresses] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [UserAddresses] ALTER COLUMN [Longitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723160816_FixLatLonPrecision'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserAddresses]') AND [c].[name] = N'Latitude');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [UserAddresses] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [UserAddresses] ALTER COLUMN [Latitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723160816_FixLatLonPrecision'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrganizationAddresses]') AND [c].[name] = N'Longitude');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [OrganizationAddresses] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [OrganizationAddresses] ALTER COLUMN [Longitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723160816_FixLatLonPrecision'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrganizationAddresses]') AND [c].[name] = N'Latitude');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [OrganizationAddresses] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [OrganizationAddresses] ALTER COLUMN [Latitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723160816_FixLatLonPrecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723160816_FixLatLonPrecision', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [Organizations] ADD [ShowAddressDirections] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [Organizations] ADD [ShowAddressMap] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [IsSearchable] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [MemberDisplayMode] int NOT NULL DEFAULT 2;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [PublicDisplayMode] int NOT NULL DEFAULT 4;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [SearchRadiusMiles] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [SearchVisibility] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    ALTER TABLE [OrganizationAddresses] ADD [Visibility] int NOT NULL DEFAULT 3;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    UPDATE OrganizationAddresses SET Visibility = CASE WHEN IsPublic = 1 THEN 0 ELSE 3 END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    UPDATE OrganizationAddresses SET PublicDisplayMode = CASE WHEN IsPublic = 1 THEN 0 ELSE 4 END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OrganizationAddresses]') AND [c].[name] = N'IsPublic');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [OrganizationAddresses] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [OrganizationAddresses] DROP COLUMN [IsPublic];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    CREATE TABLE [OrganizationAddressMemberAccesses] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationAddressId] uniqueidentifier NOT NULL,
        [OrganizationUserMembershipId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrganizationAddressMemberAccesses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAddressMemberAccesses_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAddressMemberAccesses_OrganizationAddresses_OrganizationAddressId] FOREIGN KEY ([OrganizationAddressId]) REFERENCES [OrganizationAddresses] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationAddressMemberAccesses_OrganizationUserMemberships_OrganizationUserMembershipId] FOREIGN KEY ([OrganizationUserMembershipId]) REFERENCES [OrganizationUserMemberships] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressMemberAccesses_CreatedByAppUserId] ON [OrganizationAddressMemberAccesses] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationAddressMemberAccesses_OrganizationAddressId_OrganizationUserMembershipId] ON [OrganizationAddressMemberAccesses] ([OrganizationAddressId], [OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    CREATE INDEX [IX_OrganizationAddressMemberAccesses_OrganizationUserMembershipId] ON [OrganizationAddressMemberAccesses] ([OrganizationUserMembershipId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723165923_AddAddressVisibilityAndOrgSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723165923_AddAddressVisibilityAndOrgSettings', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE TABLE [ExperienceCategories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [ColorClass] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsApproved] bit NOT NULL,
        [ProposedByOrganizationId] uniqueidentifier NULL,
        [ApprovedByAppUserId] uniqueidentifier NULL,
        [DateApproved] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ExperienceCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExperienceCategories_AppUsers_ApprovedByAppUserId] FOREIGN KEY ([ApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceCategories_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceCategories_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceCategories_Organizations_ProposedByOrganizationId] FOREIGN KEY ([ProposedByOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE TABLE [ExperienceTypes] (
        [Id] uniqueidentifier NOT NULL,
        [ExperienceCategoryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsApproved] bit NOT NULL,
        [ProposedByOrganizationId] uniqueidentifier NULL,
        [ApprovedByAppUserId] uniqueidentifier NULL,
        [DateApproved] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ExperienceTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExperienceTypes_AppUsers_ApprovedByAppUserId] FOREIGN KEY ([ApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ExperienceTypes_ExperienceCategories_ExperienceCategoryId] FOREIGN KEY ([ExperienceCategoryId]) REFERENCES [ExperienceCategories] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ExperienceTypes_Organizations_ProposedByOrganizationId] FOREIGN KEY ([ProposedByOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceCategories_ApprovedByAppUserId] ON [ExperienceCategories] ([ApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceCategories_CreatedByAppUserId] ON [ExperienceCategories] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceCategories_ProposedByOrganizationId] ON [ExperienceCategories] ([ProposedByOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceCategories_UpdatedByAppUserId] ON [ExperienceCategories] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceTypes_ApprovedByAppUserId] ON [ExperienceTypes] ([ApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceTypes_CreatedByAppUserId] ON [ExperienceTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceTypes_ExperienceCategoryId] ON [ExperienceTypes] ([ExperienceCategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceTypes_ProposedByOrganizationId] ON [ExperienceTypes] ([ProposedByOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    CREATE INDEX [IX_ExperienceTypes_UpdatedByAppUserId] ON [ExperienceTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724170601_AddExperienceTaxonomy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724170601_AddExperienceTaxonomy', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    ALTER TABLE [Organizations] ADD [AcceptsClientsOutsideRange] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsAcceptingClients] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    CREATE TABLE [OrganizationAreaOfOperations] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [RadiusMiles] decimal(10,2) NOT NULL,
        [CenterLatitude] decimal(18,10) NOT NULL,
        [CenterLongitude] decimal(18,10) NOT NULL,
        [DisplayLabel] nvarchar(256) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAreaOfOperations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAreaOfOperations_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAreaOfOperations_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAreaOfOperations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    CREATE INDEX [IX_OrganizationAreaOfOperations_CreatedByAppUserId] ON [OrganizationAreaOfOperations] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationAreaOfOperations_OrganizationId] ON [OrganizationAreaOfOperations] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    CREATE INDEX [IX_OrganizationAreaOfOperations_UpdatedByAppUserId] ON [OrganizationAreaOfOperations] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724172112_AddOrgClientAcceptanceAndAreaOfOperation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE TABLE [ClientRequests] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [StreetAddress1] nvarchar(256) NULL,
        [StreetAddress2] nvarchar(max) NULL,
        [City] nvarchar(128) NULL,
        [State] nvarchar(64) NULL,
        [ZipCode] nvarchar(20) NULL,
        [Country] nvarchar(64) NULL,
        [Latitude] decimal(18,10) NULL,
        [Longitude] decimal(18,10) NULL,
        [Gender] int NOT NULL,
        [BirthYear] int NULL,
        [Description] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ClientRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClientRequests_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequests_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequests_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE TABLE [ClientRequestFiles] (
        [Id] uniqueidentifier NOT NULL,
        [ClientRequestId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [DateUpdated] datetime2 NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ClientRequestFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClientRequestFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestFiles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestFiles_ClientRequests_ClientRequestId] FOREIGN KEY ([ClientRequestId]) REFERENCES [ClientRequests] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClientRequestFiles_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE TABLE [ClientRequestOrganizations] (
        [Id] uniqueidentifier NOT NULL,
        [ClientRequestId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [DateApplied] datetime2 NOT NULL,
        [DateResponded] datetime2 NULL,
        [RespondedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ClientRequestOrganizations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClientRequestOrganizations_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestOrganizations_AppUsers_RespondedByAppUserId] FOREIGN KEY ([RespondedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestOrganizations_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestOrganizations_ClientRequests_ClientRequestId] FOREIGN KEY ([ClientRequestId]) REFERENCES [ClientRequests] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ClientRequestOrganizations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClientRequestFiles_ClientRequestId_UploadFileId] ON [ClientRequestFiles] ([ClientRequestId], [UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestFiles_CreatedByAppUserId] ON [ClientRequestFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestFiles_UpdatedByAppUserId] ON [ClientRequestFiles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestFiles_UploadFileId] ON [ClientRequestFiles] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClientRequestOrganizations_ClientRequestId_OrganizationId] ON [ClientRequestOrganizations] ([ClientRequestId], [OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestOrganizations_CreatedByAppUserId] ON [ClientRequestOrganizations] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestOrganizations_OrganizationId] ON [ClientRequestOrganizations] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestOrganizations_RespondedByAppUserId] ON [ClientRequestOrganizations] ([RespondedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequestOrganizations_UpdatedByAppUserId] ON [ClientRequestOrganizations] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequests_AppUserId] ON [ClientRequests] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequests_CreatedByAppUserId] ON [ClientRequests] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    CREATE INDEX [IX_ClientRequests_UpdatedByAppUserId] ON [ClientRequests] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724173642_AddClientRequest'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724173642_AddClientRequest', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD [CaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE TABLE [Cases] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [ClientRequestId] uniqueidentifier NULL,
        [CaseManagerAppUserId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [Title] nvarchar(256) NULL,
        [Description] nvarchar(max) NULL,
        [StreetAddress1] nvarchar(256) NULL,
        [StreetAddress2] nvarchar(max) NULL,
        [City] nvarchar(128) NULL,
        [State] nvarchar(64) NULL,
        [ZipCode] nvarchar(20) NULL,
        [Country] nvarchar(max) NULL,
        [Latitude] decimal(18,10) NULL,
        [Longitude] decimal(18,10) NULL,
        [PublicPseudonym] nvarchar(128) NULL,
        [IsPublic] bit NOT NULL,
        [DateCaseOpened] datetime2 NOT NULL,
        [DateCaseClosed] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Cases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cases_AppUsers_CaseManagerAppUserId] FOREIGN KEY ([CaseManagerAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Cases_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Cases_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Cases_ClientRequests_ClientRequestId] FOREIGN KEY ([ClientRequestId]) REFERENCES [ClientRequests] ([Id]),
        CONSTRAINT [FK_Cases_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE TABLE [CaseTimelineEntries] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [EntryType] int NOT NULL,
        [EventDateTime] datetime2 NULL,
        [Title] nvarchar(256) NULL,
        [Body] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseTimelineEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseTimelineEntries_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTimelineEntries_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTimelineEntries_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTimelineEntries_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE TABLE [CaseTimelineEntryExperienceTypes] (
        [CaseTimelineEntryId] uniqueidentifier NOT NULL,
        [ExperienceTypeId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_CaseTimelineEntryExperienceTypes] PRIMARY KEY ([CaseTimelineEntryId], [ExperienceTypeId]),
        CONSTRAINT [FK_CaseTimelineEntryExperienceTypes_CaseTimelineEntries_CaseTimelineEntryId] FOREIGN KEY ([CaseTimelineEntryId]) REFERENCES [CaseTimelineEntries] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CaseTimelineEntryExperienceTypes_ExperienceTypes_ExperienceTypeId] FOREIGN KEY ([ExperienceTypeId]) REFERENCES [ExperienceTypes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE TABLE [CaseTimelineEntryFiles] (
        [Id] uniqueidentifier NOT NULL,
        [CaseTimelineEntryId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_CaseTimelineEntryFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseTimelineEntryFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTimelineEntryFiles_CaseTimelineEntries_CaseTimelineEntryId] FOREIGN KEY ([CaseTimelineEntryId]) REFERENCES [CaseTimelineEntries] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CaseTimelineEntryFiles_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_OrganizationPages_CaseId] ON [OrganizationPages] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_Cases_CaseManagerAppUserId] ON [Cases] ([CaseManagerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_Cases_ClientRequestId] ON [Cases] ([ClientRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_Cases_CreatedByAppUserId] ON [Cases] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_Cases_OrganizationId] ON [Cases] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_Cases_UpdatedByAppUserId] ON [Cases] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntries_AuthorAppUserId] ON [CaseTimelineEntries] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntries_CaseId] ON [CaseTimelineEntries] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntries_CreatedByAppUserId] ON [CaseTimelineEntries] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntries_UpdatedByAppUserId] ON [CaseTimelineEntries] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntryExperienceTypes_ExperienceTypeId] ON [CaseTimelineEntryExperienceTypes] ([ExperienceTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseTimelineEntryFiles_CaseTimelineEntryId_UploadFileId] ON [CaseTimelineEntryFiles] ([CaseTimelineEntryId], [UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntryFiles_CreatedByAppUserId] ON [CaseTimelineEntryFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntryFiles_UploadFileId] ON [CaseTimelineEntryFiles] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD CONSTRAINT [FK_OrganizationPages_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724175402_AddCaseManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724175402_AddCaseManagement', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180014_AddCaseNumberAndYear'
)
BEGIN
    DROP INDEX [IX_Cases_OrganizationId] ON [Cases];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180014_AddCaseNumberAndYear'
)
BEGIN
    ALTER TABLE [Cases] ADD [CaseYear] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180014_AddCaseNumberAndYear'
)
BEGIN
    ALTER TABLE [Cases] ADD [OrgCaseNumber] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180014_AddCaseNumberAndYear'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cases_OrganizationId_CaseYear_OrgCaseNumber] ON [Cases] ([OrganizationId], [CaseYear], [OrgCaseNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180014_AddCaseNumberAndYear'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724180014_AddCaseNumberAndYear', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    ALTER TABLE [OrganizationMembershipRequests] ADD [CanReapply] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    ALTER TABLE [OrganizationMembershipRequests] ADD [DenialReason] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    ALTER TABLE [OrganizationMembershipRequests] ADD [IsUnderReview] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    ALTER TABLE [OrganizationMembershipRequests] ADD [VoteDeadline] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE TABLE [MembershipReviewVotes] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationMembershipRequestId] uniqueidentifier NOT NULL,
        [VoterAppUserId] uniqueidentifier NOT NULL,
        [VoteType] int NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [DateVoted] datetime2 NOT NULL,
        CONSTRAINT [PK_MembershipReviewVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MembershipReviewVotes_AppUsers_VoterAppUserId] FOREIGN KEY ([VoterAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_MembershipReviewVotes_OrganizationMembershipRequests_OrganizationMembershipRequestId] FOREIGN KEY ([OrganizationMembershipRequestId]) REFERENCES [OrganizationMembershipRequests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE TABLE [OrganizationMembershipQuestions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [QuestionText] nvarchar(1000) NOT NULL,
        [IsRequired] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationMembershipQuestions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationMembershipQuestions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipQuestions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipQuestions_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE TABLE [OrganizationMembershipAnswers] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationMembershipRequestId] uniqueidentifier NOT NULL,
        [OrganizationMembershipQuestionId] uniqueidentifier NOT NULL,
        [AnswerText] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationMembershipAnswers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationMembershipAnswers_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipAnswers_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipAnswers_OrganizationMembershipQuestions_OrganizationMembershipQuestionId] FOREIGN KEY ([OrganizationMembershipQuestionId]) REFERENCES [OrganizationMembershipQuestions] ([Id]),
        CONSTRAINT [FK_OrganizationMembershipAnswers_OrganizationMembershipRequests_OrganizationMembershipRequestId] FOREIGN KEY ([OrganizationMembershipRequestId]) REFERENCES [OrganizationMembershipRequests] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MembershipReviewVotes_OrganizationMembershipRequestId_VoterAppUserId] ON [MembershipReviewVotes] ([OrganizationMembershipRequestId], [VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_MembershipReviewVotes_VoterAppUserId] ON [MembershipReviewVotes] ([VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipAnswers_CreatedByAppUserId] ON [OrganizationMembershipAnswers] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipAnswers_OrganizationMembershipQuestionId] ON [OrganizationMembershipAnswers] ([OrganizationMembershipQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationMembershipAnswers_OrganizationMembershipRequestId_OrganizationMembershipQuestionId] ON [OrganizationMembershipAnswers] ([OrganizationMembershipRequestId], [OrganizationMembershipQuestionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipAnswers_UpdatedByAppUserId] ON [OrganizationMembershipAnswers] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipQuestions_CreatedByAppUserId] ON [OrganizationMembershipQuestions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipQuestions_OrganizationId] ON [OrganizationMembershipQuestions] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembershipQuestions_UpdatedByAppUserId] ON [OrganizationMembershipQuestions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724180808_AddMembershipPhase3'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724180808_AddMembershipPhase3', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgCalendarEventTypes] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [ColorClass] nvarchar(max) NULL,
        [IconClass] nvarchar(max) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgCalendarEventTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgCalendarEventTypes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEventTypes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEventTypes_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgMessages] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [ParentMessageId] uniqueidentifier NULL,
        [ChannelType] int NOT NULL,
        [Subject] nvarchar(256) NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsEncrypted] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [CaseId] uniqueidentifier NULL,
        [ViewCount] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMessages_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessages_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessages_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessages_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_OrgMessages_OrgMessages_ParentMessageId] FOREIGN KEY ([ParentMessageId]) REFERENCES [OrgMessages] ([Id]),
        CONSTRAINT [FK_OrgMessages_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgCalendarEvents] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [EventTypeId] uniqueidentifier NULL,
        [CaseId] uniqueidentifier NULL,
        [Title] nvarchar(256) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Location] nvarchar(512) NULL,
        [StartDateTime] datetime2 NOT NULL,
        [EndDateTime] datetime2 NOT NULL,
        [IsAllDay] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [RecurrenceRule] nvarchar(512) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrgCalendarEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgCalendarEvents_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEvents_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEvents_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_OrgCalendarEvents_OrgCalendarEventTypes_EventTypeId] FOREIGN KEY ([EventTypeId]) REFERENCES [OrgCalendarEventTypes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_OrgCalendarEvents_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgMessageRecipients] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NOT NULL,
        [RecipientAppUserId] uniqueidentifier NOT NULL,
        [DateRead] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageRecipients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMessageRecipients_AppUsers_RecipientAppUserId] FOREIGN KEY ([RecipientAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageRecipients_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgMessageViews] (
        [OrgMessageId] uniqueidentifier NOT NULL,
        [ViewerAppUserId] uniqueidentifier NOT NULL,
        [DateViewed] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageViews] PRIMARY KEY ([OrgMessageId], [ViewerAppUserId]),
        CONSTRAINT [FK_OrgMessageViews_AppUsers_ViewerAppUserId] FOREIGN KEY ([ViewerAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageViews_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE TABLE [OrgCalendarEventAttendees] (
        [Id] uniqueidentifier NOT NULL,
        [OrgCalendarEventId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [RsvpStatus] int NOT NULL,
        [AssignedTask] nvarchar(512) NULL,
        [DateRsvp] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrgCalendarEventAttendees] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgCalendarEventAttendees_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEventAttendees_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgCalendarEventAttendees_OrgCalendarEvents_OrgCalendarEventId] FOREIGN KEY ([OrgCalendarEventId]) REFERENCES [OrgCalendarEvents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEventAttendees_AppUserId] ON [OrgCalendarEventAttendees] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEventAttendees_CreatedByAppUserId] ON [OrgCalendarEventAttendees] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgCalendarEventAttendees_OrgCalendarEventId_AppUserId] ON [OrgCalendarEventAttendees] ([OrgCalendarEventId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_CaseId] ON [OrgCalendarEvents] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_CreatedByAppUserId] ON [OrgCalendarEvents] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_EventTypeId] ON [OrgCalendarEvents] ([EventTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_OrganizationId] ON [OrgCalendarEvents] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_UpdatedByAppUserId] ON [OrgCalendarEvents] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEventTypes_CreatedByAppUserId] ON [OrgCalendarEventTypes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEventTypes_OrganizationId] ON [OrgCalendarEventTypes] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEventTypes_UpdatedByAppUserId] ON [OrgCalendarEventTypes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgMessageRecipients_OrgMessageId_RecipientAppUserId] ON [OrgMessageRecipients] ([OrgMessageId], [RecipientAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessageRecipients_RecipientAppUserId] ON [OrgMessageRecipients] ([RecipientAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_AuthorAppUserId] ON [OrgMessages] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_CaseId] ON [OrgMessages] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_CreatedByAppUserId] ON [OrgMessages] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_OrganizationId] ON [OrgMessages] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_ParentMessageId] ON [OrgMessages] ([ParentMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_UpdatedByAppUserId] ON [OrgMessages] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    CREATE INDEX [IX_OrgMessageViews_ViewerAppUserId] ON [OrgMessageViews] ([ViewerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181804_AddMessagingAndCalendar'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724181804_AddMessagingAndCalendar', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE TABLE [EvidenceVotes] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [VoterAppUserId] uniqueidentifier NOT NULL,
        [VoterOrganizationId] uniqueidentifier NULL,
        [VoteType] int NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [IsPublicVoter] bit NOT NULL,
        [DateVoted] datetime2 NOT NULL,
        CONSTRAINT [PK_EvidenceVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EvidenceVotes_AppUsers_VoterAppUserId] FOREIGN KEY ([VoterAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EvidenceVotes_Organizations_VoterOrganizationId] FOREIGN KEY ([VoterOrganizationId]) REFERENCES [Organizations] ([Id]),
        CONSTRAINT [FK_EvidenceVotes_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE TABLE [Investigations] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [OrgCalendarEventId] uniqueidentifier NULL,
        [Title] nvarchar(256) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Location] nvarchar(512) NULL,
        [ScheduledDateTime] datetime2 NOT NULL,
        [EndDateTime] datetime2 NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Investigations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Investigations_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Investigations_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Investigations_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Investigations_OrgCalendarEvents_OrgCalendarEventId] FOREIGN KEY ([OrgCalendarEventId]) REFERENCES [OrgCalendarEvents] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE TABLE [InvestigationAttendees] (
        [Id] uniqueidentifier NOT NULL,
        [InvestigationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [AssignedRole] nvarchar(128) NULL,
        [DidAttend] bit NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_InvestigationAttendees] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvestigationAttendees_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationAttendees_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationAttendees_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EvidenceVotes_UploadFileId_VoterAppUserId] ON [EvidenceVotes] ([UploadFileId], [VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_EvidenceVotes_VoterAppUserId] ON [EvidenceVotes] ([VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_EvidenceVotes_VoterOrganizationId] ON [EvidenceVotes] ([VoterOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_InvestigationAttendees_AppUserId] ON [InvestigationAttendees] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_InvestigationAttendees_CreatedByAppUserId] ON [InvestigationAttendees] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvestigationAttendees_InvestigationId_AppUserId] ON [InvestigationAttendees] ([InvestigationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_Investigations_CaseId] ON [Investigations] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_Investigations_CreatedByAppUserId] ON [Investigations] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_Investigations_OrgCalendarEventId] ON [Investigations] ([OrgCalendarEventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    CREATE INDEX [IX_Investigations_UpdatedByAppUserId] ON [Investigations] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724183339_AddInvestigationAndEvidenceVoting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724183339_AddInvestigationAndEvidenceVoting', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE TABLE [CaseTransferLogs] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [FromOrganizationId] uniqueidentifier NOT NULL,
        [ToOrganizationId] uniqueidentifier NOT NULL,
        [ProposedByAppUserId] uniqueidentifier NOT NULL,
        [RespondedByAppUserId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [TransferReason] nvarchar(1000) NULL,
        [RejectionReason] nvarchar(1000) NULL,
        [DateProposed] datetime2 NOT NULL,
        [DateResponded] datetime2 NULL,
        CONSTRAINT [PK_CaseTransferLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseTransferLogs_AppUsers_ProposedByAppUserId] FOREIGN KEY ([ProposedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTransferLogs_AppUsers_RespondedByAppUserId] FOREIGN KEY ([RespondedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseTransferLogs_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_CaseTransferLogs_Organizations_FromOrganizationId] FOREIGN KEY ([FromOrganizationId]) REFERENCES [Organizations] ([Id]),
        CONSTRAINT [FK_CaseTransferLogs_Organizations_ToOrganizationId] FOREIGN KEY ([ToOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE INDEX [IX_CaseTransferLogs_CaseId] ON [CaseTransferLogs] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE INDEX [IX_CaseTransferLogs_FromOrganizationId] ON [CaseTransferLogs] ([FromOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE INDEX [IX_CaseTransferLogs_ProposedByAppUserId] ON [CaseTransferLogs] ([ProposedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE INDEX [IX_CaseTransferLogs_RespondedByAppUserId] ON [CaseTransferLogs] ([RespondedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    CREATE INDEX [IX_CaseTransferLogs_ToOrganizationId] ON [CaseTransferLogs] ([ToOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184002_AddCaseTransferAndPublicDiscovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724184002_AddCaseTransferAndPublicDiscovery', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802160018_AddCaseVotes'
)
BEGIN
    CREATE TABLE [CaseVotes] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [VoterAppUserId] uniqueidentifier NOT NULL,
        [VoteType] int NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [DateVoted] datetime2 NOT NULL,
        CONSTRAINT [PK_CaseVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseVotes_AppUsers_VoterAppUserId] FOREIGN KEY ([VoterAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseVotes_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802160018_AddCaseVotes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseVotes_CaseId_VoterAppUserId] ON [CaseVotes] ([CaseId], [VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802160018_AddCaseVotes'
)
BEGIN
    CREATE INDEX [IX_CaseVotes_VoterAppUserId] ON [CaseVotes] ([VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802160018_AddCaseVotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802160018_AddCaseVotes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184655_AddInvestigationWorkflowFields'
)
BEGIN
    ALTER TABLE [Investigations] ADD [EvidenceDueDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184655_AddInvestigationWorkflowFields'
)
BEGIN
    ALTER TABLE [InvestigationAttendees] ADD [Rsvp] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184655_AddInvestigationWorkflowFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802184655_AddInvestigationWorkflowFields', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    CREATE TABLE [CaseMessages] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [SenderSide] int NOT NULL,
        [IsReadByClient] bit NOT NULL,
        [IsReadByOrg] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseMessages_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseMessages_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseMessages_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseMessages_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    CREATE INDEX [IX_CaseMessages_AuthorAppUserId] ON [CaseMessages] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    CREATE INDEX [IX_CaseMessages_CaseId_DateCreated] ON [CaseMessages] ([CaseId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    CREATE INDEX [IX_CaseMessages_CreatedByAppUserId] ON [CaseMessages] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    CREATE INDEX [IX_CaseMessages_UpdatedByAppUserId] ON [CaseMessages] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802190422_AddCaseMessageBoard'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802190422_AddCaseMessageBoard', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802193004_AddFileMetadataAndCaseStorage'
)
BEGIN
    ALTER TABLE [CaseTimelineEntries] ADD [IpAddress] nvarchar(45) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802193004_AddFileMetadataAndCaseStorage'
)
BEGIN
    CREATE TABLE [UploadFileMetadata] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [MediaKind] nvarchar(20) NOT NULL,
        [DurationSeconds] float NULL,
        [SampleRateHz] int NULL,
        [BitRateKbps] int NULL,
        [Channels] int NULL,
        [AudioCodec] nvarchar(50) NULL,
        [WidthPixels] int NULL,
        [HeightPixels] int NULL,
        [CapturedAtUtc] datetime2 NULL,
        [GpsLatitude] float NULL,
        [GpsLongitude] float NULL,
        [GpsAltitudeMeters] float NULL,
        [CameraManufacturer] nvarchar(100) NULL,
        [CameraModel] nvarchar(100) NULL,
        [RawMetadataJson] nvarchar(max) NULL,
        [ExtractedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UploadFileMetadata] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileMetadata_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802193004_AddFileMetadataAndCaseStorage'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UploadFileMetadata_UploadFileId] ON [UploadFileMetadata] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802193004_AddFileMetadataAndCaseStorage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802193004_AddFileMetadataAndCaseStorage', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE TABLE [CaseReports] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Summary] nvarchar(max) NULL,
        [Conclusion] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [ExpectedDeliveryDate] datetime2 NULL,
        [PublishedAt] datetime2 NULL,
        [PublishedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseReports_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseReports_AppUsers_PublishedByAppUserId] FOREIGN KEY ([PublishedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseReports_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseReports_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE TABLE [CaseReportSections] (
        [Id] uniqueidentifier NOT NULL,
        [CaseReportId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Body] nvarchar(max) NULL,
        [SectionType] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_CaseReportSections] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseReportSections_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseReportSections_CaseReports_CaseReportId] FOREIGN KEY ([CaseReportId]) REFERENCES [CaseReports] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE TABLE [CaseReportSectionFiles] (
        [Id] uniqueidentifier NOT NULL,
        [CaseReportSectionId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [Caption] nvarchar(500) NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_CaseReportSectionFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseReportSectionFiles_CaseReportSections_CaseReportSectionId] FOREIGN KEY ([CaseReportSectionId]) REFERENCES [CaseReportSections] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CaseReportSectionFiles_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReports_CaseId] ON [CaseReports] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReports_CreatedByAppUserId] ON [CaseReports] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReports_PublishedByAppUserId] ON [CaseReports] ([PublishedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReports_UpdatedByAppUserId] ON [CaseReports] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReportSectionFiles_CaseReportSectionId] ON [CaseReportSectionFiles] ([CaseReportSectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReportSectionFiles_UploadFileId] ON [CaseReportSectionFiles] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReportSections_CaseReportId] ON [CaseReportSections] ([CaseReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    CREATE INDEX [IX_CaseReportSections_CreatedByAppUserId] ON [CaseReportSections] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802195818_AddCaseReportBuilder'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802195818_AddCaseReportBuilder', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    CREATE TABLE [CaseResearchEntries] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [ResearchType] int NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Body] nvarchar(max) NULL,
        [Url] nvarchar(2000) NULL,
        [UploadFileId] uniqueidentifier NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseResearchEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseResearchEntries_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseResearchEntries_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseResearchEntries_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_CaseResearchEntries_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    CREATE INDEX [IX_CaseResearchEntries_CaseId_SortOrder] ON [CaseResearchEntries] ([CaseId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    CREATE INDEX [IX_CaseResearchEntries_CreatedByAppUserId] ON [CaseResearchEntries] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    CREATE INDEX [IX_CaseResearchEntries_UpdatedByAppUserId] ON [CaseResearchEntries] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    CREATE INDEX [IX_CaseResearchEntries_UploadFileId] ON [CaseResearchEntries] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802202448_AddCaseResearch'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802202448_AddCaseResearch', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE TABLE [InvestigationScheduleProposals] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [AcceptedSlotId] uniqueidentifier NULL,
        [ClientCounterDateTime] datetime2 NULL,
        [ClientResponseNotes] nvarchar(1000) NULL,
        [ClientRespondedAt] datetime2 NULL,
        [InvestigationId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_InvestigationScheduleProposals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvestigationScheduleProposals_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationScheduleProposals_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationScheduleProposals_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_InvestigationScheduleProposals_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE TABLE [ScheduleProposalSlots] (
        [Id] uniqueidentifier NOT NULL,
        [ProposalId] uniqueidentifier NOT NULL,
        [StartDateTime] datetime2 NOT NULL,
        [EndDateTime] datetime2 NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ScheduleProposalSlots] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ScheduleProposalSlots_InvestigationScheduleProposals_ProposalId] FOREIGN KEY ([ProposalId]) REFERENCES [InvestigationScheduleProposals] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE INDEX [IX_InvestigationScheduleProposals_CaseId_Status] ON [InvestigationScheduleProposals] ([CaseId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE INDEX [IX_InvestigationScheduleProposals_CreatedByAppUserId] ON [InvestigationScheduleProposals] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE INDEX [IX_InvestigationScheduleProposals_InvestigationId] ON [InvestigationScheduleProposals] ([InvestigationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE INDEX [IX_InvestigationScheduleProposals_UpdatedByAppUserId] ON [InvestigationScheduleProposals] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    CREATE INDEX [IX_ScheduleProposalSlots_ProposalId] ON [ScheduleProposalSlots] ([ProposalId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802203110_AddInvestigationScheduling'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802203110_AddInvestigationScheduling', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD [CaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD [IsOriginalUploader] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD [IsVoterCaseClient] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD [IsVoterCaseOrgMember] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD [VoterOrganizationName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    CREATE INDEX [IX_EvidenceVotes_CaseId] ON [EvidenceVotes] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    ALTER TABLE [EvidenceVotes] ADD CONSTRAINT [FK_EvidenceVotes_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802212142_AddEvidenceVoteContext'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802212142_AddEvidenceVoteContext', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    CREATE TABLE [CaseClientAccesses] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseClientAccesses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseClientAccesses_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientAccesses_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientAccesses_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientAccesses_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    CREATE INDEX [IX_CaseClientAccesses_AppUserId] ON [CaseClientAccesses] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseClientAccesses_CaseId_AppUserId] ON [CaseClientAccesses] ([CaseId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    CREATE INDEX [IX_CaseClientAccesses_CreatedByAppUserId] ON [CaseClientAccesses] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    CREATE INDEX [IX_CaseClientAccesses_UpdatedByAppUserId] ON [CaseClientAccesses] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802213147_AddCaseClientAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802213147_AddCaseClientAccess', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    CREATE TABLE [CaseNotes] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [Title] nvarchar(300) NULL,
        [Body] nvarchar(max) NOT NULL,
        [IsPinned] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseNotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseNotes_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseNotes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseNotes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseNotes_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    CREATE INDEX [IX_CaseNotes_AuthorAppUserId] ON [CaseNotes] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    CREATE INDEX [IX_CaseNotes_CaseId] ON [CaseNotes] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    CREATE INDEX [IX_CaseNotes_CreatedByAppUserId] ON [CaseNotes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    CREATE INDEX [IX_CaseNotes_UpdatedByAppUserId] ON [CaseNotes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002023_AddCaseNotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805002023_AddCaseNotes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002948_AddOrganizationPublicContact'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PublicEmail] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002948_AddOrganizationPublicContact'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PublicPhone] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002948_AddOrganizationPublicContact'
)
BEGIN
    ALTER TABLE [Organizations] ADD [PublicWebsite] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805002948_AddOrganizationPublicContact'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805002948_AddOrganizationPublicContact', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805142534_AddVideoProjects'
)
BEGIN
    CREATE TABLE [VideoProjects] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [ProjectJson] nvarchar(max) NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VideoProjects] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VideoProjects_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_VideoProjects_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_VideoProjects_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805142534_AddVideoProjects'
)
BEGIN
    CREATE INDEX [IX_VideoProjects_CaseId] ON [VideoProjects] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805142534_AddVideoProjects'
)
BEGIN
    CREATE INDEX [IX_VideoProjects_CreatedByAppUserId] ON [VideoProjects] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805142534_AddVideoProjects'
)
BEGIN
    CREATE INDEX [IX_VideoProjects_UpdatedByAppUserId] ON [VideoProjects] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805142534_AddVideoProjects'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805142534_AddVideoProjects', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    ALTER TABLE [VideoProjects] DROP CONSTRAINT [FK_VideoProjects_Cases_CaseId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[VideoProjects]') AND [c].[name] = N'CaseId');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [VideoProjects] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [VideoProjects] ALTER COLUMN [CaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    ALTER TABLE [VideoProjects] ADD [PublishedUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    CREATE INDEX [IX_VideoProjects_PublishedUploadFileId] ON [VideoProjects] ([PublishedUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    ALTER TABLE [VideoProjects] ADD CONSTRAINT [FK_VideoProjects_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    ALTER TABLE [VideoProjects] ADD CONSTRAINT [FK_VideoProjects_UploadFiles_PublishedUploadFileId] FOREIGN KEY ([PublishedUploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805145420_UpdateVideoProjectsForUserOwnership'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805145420_UpdateVideoProjectsForUserOwnership', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805153814_AddImageEditorMetadata'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [EditStateJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805153814_AddImageEditorMetadata'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [IsEditedVersion] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805153814_AddImageEditorMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805153814_AddImageEditorMetadata', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805174714_AddAudioMarkers'
)
BEGIN
    CREATE TABLE [AudioMarkers] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [TimeSeconds] float NOT NULL,
        [Label] nvarchar(200) NULL,
        [ConfidenceLevel] int NOT NULL,
        [Note] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_AudioMarkers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AudioMarkers_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_AudioMarkers_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_AudioMarkers_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805174714_AddAudioMarkers'
)
BEGIN
    CREATE INDEX [IX_AudioMarkers_CreatedByAppUserId] ON [AudioMarkers] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805174714_AddAudioMarkers'
)
BEGIN
    CREATE INDEX [IX_AudioMarkers_UpdatedByAppUserId] ON [AudioMarkers] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805174714_AddAudioMarkers'
)
BEGIN
    CREATE INDEX [IX_AudioMarkers_UploadFileId] ON [AudioMarkers] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805174714_AddAudioMarkers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805174714_AddAudioMarkers', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    CREATE TABLE [CaseFiles] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [Description] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseFiles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseFiles_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CaseFiles_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    CREATE INDEX [IX_CaseFiles_CaseId] ON [CaseFiles] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    CREATE INDEX [IX_CaseFiles_CreatedByAppUserId] ON [CaseFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    CREATE INDEX [IX_CaseFiles_UpdatedByAppUserId] ON [CaseFiles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    CREATE INDEX [IX_CaseFiles_UploadFileId] ON [CaseFiles] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805233431_AddCaseFiles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805233431_AddCaseFiles', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806005320_AddCaseRelatedPeople'
)
BEGIN
    CREATE TABLE [CaseRelatedPeople] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Age] int NULL,
        [Relationship] nvarchar(100) NULL,
        [LivesAtProperty] bit NOT NULL,
        [Notes] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseRelatedPeople] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseRelatedPeople_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseRelatedPeople_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseRelatedPeople_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806005320_AddCaseRelatedPeople'
)
BEGIN
    CREATE INDEX [IX_CaseRelatedPeople_CaseId] ON [CaseRelatedPeople] ([CaseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806005320_AddCaseRelatedPeople'
)
BEGIN
    CREATE INDEX [IX_CaseRelatedPeople_CreatedByAppUserId] ON [CaseRelatedPeople] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806005320_AddCaseRelatedPeople'
)
BEGIN
    CREATE INDEX [IX_CaseRelatedPeople_UpdatedByAppUserId] ON [CaseRelatedPeople] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806005320_AddCaseRelatedPeople'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806005320_AddCaseRelatedPeople', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE TABLE [UploadFileShares] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [TargetType] int NOT NULL,
        [TargetAppUserId] uniqueidentifier NULL,
        [TargetInvestigationId] uniqueidentifier NULL,
        [TargetOrganizationId] uniqueidentifier NULL,
        [SharedByAppUserId] uniqueidentifier NOT NULL,
        [IsActive] bit NOT NULL,
        [RemovedByAppUserId] uniqueidentifier NULL,
        [RemovalDate] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileShares_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileShares_AppUsers_RemovedByAppUserId] FOREIGN KEY ([RemovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileShares_AppUsers_SharedByAppUserId] FOREIGN KEY ([SharedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileShares_AppUsers_TargetAppUserId] FOREIGN KEY ([TargetAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileShares_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileShares_Investigations_TargetInvestigationId] FOREIGN KEY ([TargetInvestigationId]) REFERENCES [Investigations] ([Id]),
        CONSTRAINT [FK_UploadFileShares_Organizations_TargetOrganizationId] FOREIGN KEY ([TargetOrganizationId]) REFERENCES [Organizations] ([Id]),
        CONSTRAINT [FK_UploadFileShares_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_CreatedByAppUserId] ON [UploadFileShares] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_RemovedByAppUserId] ON [UploadFileShares] ([RemovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_SharedByAppUserId] ON [UploadFileShares] ([SharedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_TargetAppUserId] ON [UploadFileShares] ([TargetAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_TargetInvestigationId] ON [UploadFileShares] ([TargetInvestigationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_TargetOrganizationId] ON [UploadFileShares] ([TargetOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_UpdatedByAppUserId] ON [UploadFileShares] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    CREATE INDEX [IX_UploadFileShares_UploadFileId_TargetType_IsActive] ON [UploadFileShares] ([UploadFileId], [TargetType], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806105800_AddUploadFileShare'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806105800_AddUploadFileShare', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [AllowClientComments] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [AllowInvestigationTeamComments] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [AllowOrganizationComments] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [AllowPublicComments] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [CaseCopyOfUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE TABLE [UploadFileComments] (
        [Id] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [IsOwner] bit NOT NULL,
        [IsInvestigationTeamMember] bit NOT NULL,
        [IsClient] bit NOT NULL,
        [IsOrganizationMember] bit NOT NULL,
        [IsPublicCommenter] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UploadFileComments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UploadFileComments_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileComments_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileComments_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UploadFileComments_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_CaseCopyOfUploadFileId] ON [UploadFiles] ([CaseCopyOfUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE INDEX [IX_UploadFileComments_AuthorAppUserId] ON [UploadFileComments] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE INDEX [IX_UploadFileComments_CreatedByAppUserId] ON [UploadFileComments] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE INDEX [IX_UploadFileComments_UpdatedByAppUserId] ON [UploadFileComments] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    CREATE INDEX [IX_UploadFileComments_UploadFileId_DateCreated] ON [UploadFileComments] ([UploadFileId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD CONSTRAINT [FK_UploadFiles_UploadFiles_CaseCopyOfUploadFileId] FOREIGN KEY ([CaseCopyOfUploadFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811133320_AddUploadFileComments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811133320_AddUploadFileComments', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811145717_AddUploadFileArchivedVersion'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [ArchivedFromUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811145717_AddUploadFileArchivedVersion'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_ArchivedFromUploadFileId] ON [UploadFiles] ([ArchivedFromUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811145717_AddUploadFileArchivedVersion'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD CONSTRAINT [FK_UploadFiles_UploadFiles_ArchivedFromUploadFileId] FOREIGN KEY ([ArchivedFromUploadFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811145717_AddUploadFileArchivedVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811145717_AddUploadFileArchivedVersion', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE TABLE [CaseClientInvites] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [Token] nvarchar(64) NOT NULL,
        [DateExpires] datetime2 NOT NULL,
        [DateAccepted] datetime2 NULL,
        [DateRevoked] datetime2 NULL,
        [AcceptedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseClientInvites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseClientInvites_AppUsers_AcceptedByAppUserId] FOREIGN KEY ([AcceptedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientInvites_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientInvites_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseClientInvites_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE INDEX [IX_CaseClientInvites_AcceptedByAppUserId] ON [CaseClientInvites] ([AcceptedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE INDEX [IX_CaseClientInvites_CaseId_Email] ON [CaseClientInvites] ([CaseId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE INDEX [IX_CaseClientInvites_CreatedByAppUserId] ON [CaseClientInvites] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseClientInvites_Token] ON [CaseClientInvites] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    CREATE INDEX [IX_CaseClientInvites_UpdatedByAppUserId] ON [CaseClientInvites] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811154454_AddCaseClientInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811154454_AddCaseClientInvites', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811181326_AddRaceConditionUniqueIndexes'
)
BEGIN
    DROP INDEX [IX_OrganizationMembershipRequests_OrganizationId_AppUserId] ON [OrganizationMembershipRequests];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811181326_AddRaceConditionUniqueIndexes'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserMessageTypes]') AND [c].[name] = N'Name');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [UserMessageTypes] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [UserMessageTypes] ALTER COLUMN [Name] nvarchar(450) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811181326_AddRaceConditionUniqueIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_UserMessageTypes_Name] ON [UserMessageTypes] ([Name]) WHERE [Name] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811181326_AddRaceConditionUniqueIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrganizationMembershipRequests_OrganizationId_AppUserId] ON [OrganizationMembershipRequests] ([OrganizationId], [AppUserId]) WHERE [Status] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260811181326_AddRaceConditionUniqueIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260811181326_AddRaceConditionUniqueIndexes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [DetectionScore] real NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [EndSeconds] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [IsAutoDetected] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [LinkedClipUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD [ReviewStatus] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    CREATE INDEX [IX_AudioMarkers_LinkedClipUploadFileId] ON [AudioMarkers] ([LinkedClipUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    CREATE INDEX [IX_AudioMarkers_UploadFileId_ReviewStatus] ON [AudioMarkers] ([UploadFileId], [ReviewStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    ALTER TABLE [AudioMarkers] ADD CONSTRAINT [FK_AudioMarkers_UploadFiles_LinkedClipUploadFileId] FOREIGN KEY ([LinkedClipUploadFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814172747_AddAudioMarkerSpansAndCandidates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814172747_AddAudioMarkerSpansAndCandidates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814191415_AddCaseTimelineVisibility'
)
BEGIN
    ALTER TABLE [CaseTimelineEntries] ADD [Visibility] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814191415_AddCaseTimelineVisibility'
)
BEGIN
    UPDATE [CaseTimelineEntries] SET [Visibility] = 2 WHERE [IsPublic] = 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814191415_AddCaseTimelineVisibility'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CaseTimelineEntries]') AND [c].[name] = N'IsPublic');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [CaseTimelineEntries] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [CaseTimelineEntries] DROP COLUMN [IsPublic];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814191415_AddCaseTimelineVisibility'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814191415_AddCaseTimelineVisibility', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814192750_AddTimelineInvestigationLink'
)
BEGIN
    ALTER TABLE [CaseTimelineEntries] ADD [InvestigationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814192750_AddTimelineInvestigationLink'
)
BEGIN
    CREATE INDEX [IX_CaseTimelineEntries_InvestigationId] ON [CaseTimelineEntries] ([InvestigationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814192750_AddTimelineInvestigationLink'
)
BEGIN
    ALTER TABLE [CaseTimelineEntries] ADD CONSTRAINT [FK_CaseTimelineEntries_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814192750_AddTimelineInvestigationLink'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814192750_AddTimelineInvestigationLink', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    CREATE TABLE [AppUserPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [AltText] nvarchar(max) NULL,
        [IsPublic] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_AppUserPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AppUserPhotos_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AppUserPhotos_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_AppUserPhotos_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_AppUserPhotos_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppUserPhotos_AppUserId_IsPublic] ON [AppUserPhotos] ([AppUserId], [IsPublic]) WHERE [IsActive] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    CREATE INDEX [IX_AppUserPhotos_CreatedByAppUserId] ON [AppUserPhotos] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    CREATE INDEX [IX_AppUserPhotos_UpdatedByAppUserId] ON [AppUserPhotos] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    CREATE INDEX [IX_AppUserPhotos_UploadFileId] ON [AppUserPhotos] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814200303_AddAppUserPhoto'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814200303_AddAppUserPhoto', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815002108_AddPrivatePhotoConsentFlags'
)
BEGIN
    ALTER TABLE [Organizations] ADD [AllowMemberPrivatePhotosToClients] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815002108_AddPrivatePhotoConsentFlags'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [SharePrivatePhotoWithClients] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815002108_AddPrivatePhotoConsentFlags'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815002108_AddPrivatePhotoConsentFlags', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815004607_AddCaseRelatedPersonPhoto'
)
BEGIN
    ALTER TABLE [CaseRelatedPeople] ADD [UploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815004607_AddCaseRelatedPersonPhoto'
)
BEGIN
    CREATE INDEX [IX_CaseRelatedPeople_UploadFileId] ON [CaseRelatedPeople] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815004607_AddCaseRelatedPersonPhoto'
)
BEGIN
    ALTER TABLE [CaseRelatedPeople] ADD CONSTRAINT [FK_CaseRelatedPeople_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815004607_AddCaseRelatedPersonPhoto'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815004607_AddCaseRelatedPersonPhoto', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815011825_AddClientDisplayAlias'
)
BEGIN
    ALTER TABLE [Cases] ADD [ClientDisplayAlias] nvarchar(128) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815011825_AddClientDisplayAlias'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815011825_AddClientDisplayAlias', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815110315_AddSiteSettings'
)
BEGIN
    CREATE TABLE [SiteSettings] (
        [Id] uniqueidentifier NOT NULL,
        [Key] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        [Description] nvarchar(512) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SiteSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SiteSettings_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SiteSettings_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815110315_AddSiteSettings'
)
BEGIN
    CREATE INDEX [IX_SiteSettings_CreatedByAppUserId] ON [SiteSettings] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815110315_AddSiteSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SiteSettings_Key] ON [SiteSettings] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815110315_AddSiteSettings'
)
BEGIN
    CREATE INDEX [IX_SiteSettings_UpdatedByAppUserId] ON [SiteSettings] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815110315_AddSiteSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815110315_AddSiteSettings', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE TABLE [VideoAssets] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Category] nvarchar(100) NULL,
        [Tags] nvarchar(500) NULL,
        [Type] int NOT NULL,
        [Format] int NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [ThumbnailUploadFileId] uniqueidentifier NULL,
        [ContentHash] nvarchar(64) NOT NULL,
        [FileSizeBytes] bigint NOT NULL,
        [NativeWidth] int NULL,
        [NativeHeight] int NULL,
        [AllowRecolor] bit NOT NULL,
        [AllowResize] bit NOT NULL,
        [AllowOpacity] bit NOT NULL,
        [AllowRotation] bit NOT NULL,
        [AllowEffects] bit NOT NULL,
        [AllowEasing] bit NOT NULL,
        [AllowMotion] bit NOT NULL,
        [AllowControlPoints] bit NOT NULL,
        [PresetColors] nvarchar(500) NULL,
        [MinScale] float NULL,
        [MaxScale] float NULL,
        [FlattenOnExport] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_VideoAssets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VideoAssets_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_VideoAssets_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_VideoAssets_UploadFiles_ThumbnailUploadFileId] FOREIGN KEY ([ThumbnailUploadFileId]) REFERENCES [UploadFiles] ([Id]),
        CONSTRAINT [FK_VideoAssets_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_CreatedByAppUserId] ON [VideoAssets] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_IsActive_SortOrder] ON [VideoAssets] ([IsActive], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_ThumbnailUploadFileId] ON [VideoAssets] ([ThumbnailUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_UpdatedByAppUserId] ON [VideoAssets] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    CREATE INDEX [IX_VideoAssets_UploadFileId] ON [VideoAssets] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815111705_AddVideoAssetCatalog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815111705_AddVideoAssetCatalog', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE TABLE [SupportTickets] (
        [Id] uniqueidentifier NOT NULL,
        [Reference] nvarchar(450) NULL,
        [AccessToken] uniqueidentifier NOT NULL,
        [FromName] nvarchar(max) NULL,
        [FromEmail] nvarchar(450) NULL,
        [Topic] int NOT NULL,
        [Subject] nvarchar(max) NULL,
        [Body] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [AppUserId] uniqueidentifier NULL,
        [AssignedToAppUserId] uniqueidentifier NULL,
        [SourceIpHash] nvarchar(450) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [DateClosed] datetime2 NULL,
        CONSTRAINT [PK_SupportTickets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportTickets_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SupportTickets_AppUsers_AssignedToAppUserId] FOREIGN KEY ([AssignedToAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE TABLE [SupportTicketReplies] (
        [Id] uniqueidentifier NOT NULL,
        [SupportTicketId] uniqueidentifier NOT NULL,
        [Body] nvarchar(max) NULL,
        [AuthorAppUserId] uniqueidentifier NULL,
        [IsFromStaff] bit NOT NULL,
        [IsInternalNote] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportTicketReplies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportTicketReplies_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SupportTicketReplies_SupportTickets_SupportTicketId] FOREIGN KEY ([SupportTicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTicketReplies_AuthorAppUserId] ON [SupportTicketReplies] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTicketReplies_SupportTicketId] ON [SupportTicketReplies] ([SupportTicketId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SupportTickets_AccessToken] ON [SupportTickets] ([AccessToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_AppUserId] ON [SupportTickets] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_AssignedToAppUserId] ON [SupportTickets] ([AssignedToAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_FromEmail_DateCreated] ON [SupportTickets] ([FromEmail], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SupportTickets_Reference] ON [SupportTickets] ([Reference]) WHERE [Reference] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_SourceIpHash_DateCreated] ON [SupportTickets] ([SourceIpHash], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_Status_DateCreated] ON [SupportTickets] ([Status], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815131443_AddSupportTickets'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815131443_AddSupportTickets', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815143421_AddCalendarEventAddressAndMeetingUrl'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [MeetingUrl] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815143421_AddCalendarEventAddressAndMeetingUrl'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [OrganizationAddressId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815143421_AddCalendarEventAddressAndMeetingUrl'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_OrganizationAddressId] ON [OrgCalendarEvents] ([OrganizationAddressId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815143421_AddCalendarEventAddressAndMeetingUrl'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD CONSTRAINT [FK_OrgCalendarEvents_OrganizationAddresses_OrganizationAddressId] FOREIGN KEY ([OrganizationAddressId]) REFERENCES [OrganizationAddresses] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815143421_AddCalendarEventAddressAndMeetingUrl'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815143421_AddCalendarEventAddressAndMeetingUrl', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815144933_AddInvestigationCoordinates'
)
BEGIN
    ALTER TABLE [Investigations] ADD [DateGeocoded] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815144933_AddInvestigationCoordinates'
)
BEGIN
    ALTER TABLE [Investigations] ADD [GeocodeNote] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815144933_AddInvestigationCoordinates'
)
BEGIN
    ALTER TABLE [Investigations] ADD [Latitude] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815144933_AddInvestigationCoordinates'
)
BEGIN
    ALTER TABLE [Investigations] ADD [Longitude] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815144933_AddInvestigationCoordinates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815144933_AddInvestigationCoordinates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815153732_AddUserEmailValidationSentDate'
)
BEGIN
    ALTER TABLE [UserEmails] ADD [DateValidationSent] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815153732_AddUserEmailValidationSentDate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815153732_AddUserEmailValidationSentDate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    ALTER TABLE [Investigations] ADD [PlaceId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    ALTER TABLE [Cases] ADD [PlaceId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE TABLE [Places] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(256) NULL,
        [StreetAddress1] nvarchar(256) NULL,
        [StreetAddress2] nvarchar(256) NULL,
        [City] nvarchar(128) NULL,
        [State] nvarchar(64) NULL,
        [ZipCode] nvarchar(20) NULL,
        [Country] nvarchar(64) NULL,
        [Latitude] decimal(18,10) NULL,
        [Longitude] decimal(18,10) NULL,
        [GeocodeNote] nvarchar(512) NULL,
        [DateGeocoded] datetime2 NULL,
        [Kind] int NOT NULL,
        [IsApproved] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Places] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Places_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Places_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE INDEX [IX_Investigations_PlaceId] ON [Investigations] ([PlaceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE INDEX [IX_Cases_PlaceId] ON [Cases] ([PlaceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE INDEX [IX_Places_CreatedByAppUserId] ON [Places] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE INDEX [IX_Places_Latitude_Longitude] ON [Places] ([Latitude], [Longitude]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    CREATE INDEX [IX_Places_UpdatedByAppUserId] ON [Places] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    ALTER TABLE [Cases] ADD CONSTRAINT [FK_Cases_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    ALTER TABLE [Investigations] ADD CONSTRAINT [FK_Investigations_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162759_AddPlaceEntity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815162759_AddPlaceEntity', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162818_BackfillPlacesFromCases'
)
BEGIN
    INSERT INTO Places (
        Id, Name, StreetAddress1, StreetAddress2, City, State, ZipCode, Country,
        Latitude, Longitude, GeocodeNote, DateGeocoded, Kind, IsApproved,
        DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId)
    SELECT
        NEWID(), NULL, c.StreetAddress1, c.StreetAddress2, c.City, c.State, c.ZipCode,
        c.Country, c.Latitude, c.Longitude, NULL,
        CASE WHEN c.Latitude IS NOT NULL AND c.Longitude IS NOT NULL
             THEN c.DateCreated ELSE NULL END,
        1, 0,
        c.DateCreated, NULL, c.CreatedByAppUserId, NULL
    FROM Cases c
    WHERE c.PlaceId IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162818_BackfillPlacesFromCases'
)
BEGIN
    UPDATE c
    SET PlaceId = p.Id
    FROM Cases c
    INNER JOIN Places p
        ON  p.DateCreated        = c.DateCreated
        AND p.CreatedByAppUserId = c.CreatedByAppUserId
        AND ISNULL(p.StreetAddress1, '') = ISNULL(c.StreetAddress1, '')
        AND ISNULL(p.City,           '') = ISNULL(c.City,           '')
        AND ISNULL(p.State,          '') = ISNULL(c.State,          '')
        AND ISNULL(p.ZipCode,        '') = ISNULL(c.ZipCode,        '')
    WHERE c.PlaceId IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162818_BackfillPlacesFromCases'
)
BEGIN
    UPDATE i
    SET PlaceId = c.PlaceId
    FROM Investigations i
    INNER JOIN Cases c ON c.Id = i.CaseId
    WHERE i.PlaceId IS NULL AND c.PlaceId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815162818_BackfillPlacesFromCases'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815162818_BackfillPlacesFromCases', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    ALTER TABLE [Investigations] ADD [OrganizationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    UPDATE i
    SET OrganizationId = c.OrganizationId
    FROM Investigations i
    INNER JOIN Cases c ON c.Id = i.CaseId
    WHERE i.OrganizationId IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    ALTER TABLE Investigations ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Investigations]') AND [c].[name] = N'CaseId');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [Investigations] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [Investigations] ALTER COLUMN [CaseId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    CREATE INDEX [IX_Investigations_OrganizationId] ON [Investigations] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    ALTER TABLE [Investigations] ADD CONSTRAINT [FK_Investigations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815164340_MakeInvestigationCaseOptional'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815164340_MakeInvestigationCaseOptional', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815170243_AddInvestigationAttendeeLead'
)
BEGIN
    ALTER TABLE [InvestigationAttendees] ADD [IsLead] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815170243_AddInvestigationAttendeeLead'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815170243_AddInvestigationAttendeeLead', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815172144_FixInvestigationCoordinatePrecision'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Investigations]') AND [c].[name] = N'Longitude');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [Investigations] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [Investigations] ALTER COLUMN [Longitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815172144_FixInvestigationCoordinatePrecision'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Investigations]') AND [c].[name] = N'Latitude');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [Investigations] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [Investigations] ALTER COLUMN [Latitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815172144_FixInvestigationCoordinatePrecision'
)
BEGIN
    UPDATE i
    SET Latitude  = p.Latitude,
        Longitude = p.Longitude
    FROM Investigations i
    INNER JOIN Places p ON p.Id = i.PlaceId
    WHERE p.Latitude IS NOT NULL
      AND p.Longitude IS NOT NULL
      AND (i.Latitude <> p.Latitude OR i.Longitude <> p.Longitude);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815172144_FixInvestigationCoordinatePrecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815172144_FixInvestigationCoordinatePrecision', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173303_AddInvestigationAttendeeArrival'
)
BEGIN
    ALTER TABLE [InvestigationAttendees] ADD [AttendanceRecordedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173303_AddInvestigationAttendeeArrival'
)
BEGIN
    ALTER TABLE [InvestigationAttendees] ADD [DateArrived] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173303_AddInvestigationAttendeeArrival'
)
BEGIN
    CREATE INDEX [IX_InvestigationAttendees_AttendanceRecordedByAppUserId] ON [InvestigationAttendees] ([AttendanceRecordedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173303_AddInvestigationAttendeeArrival'
)
BEGIN
    ALTER TABLE [InvestigationAttendees] ADD CONSTRAINT [FK_InvestigationAttendees_AppUsers_AttendanceRecordedByAppUserId] FOREIGN KEY ([AttendanceRecordedByAppUserId]) REFERENCES [AppUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173303_AddInvestigationAttendeeArrival'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815173303_AddInvestigationAttendeeArrival', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173921_AddInvestigationVisibility'
)
BEGIN
    ALTER TABLE [Investigations] ADD [Visibility] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815173921_AddInvestigationVisibility'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815173921_AddInvestigationVisibility', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815192324_AddInvestigationFindings'
)
BEGIN
    CREATE TABLE [InvestigationFindings] (
        [Id] uniqueidentifier NOT NULL,
        [InvestigationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [Narrative] nvarchar(max) NOT NULL,
        [DateUpdated] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_InvestigationFindings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvestigationFindings_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationFindings_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationFindings_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815192324_AddInvestigationFindings'
)
BEGIN
    CREATE INDEX [IX_InvestigationFindings_AppUserId] ON [InvestigationFindings] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815192324_AddInvestigationFindings'
)
BEGIN
    CREATE INDEX [IX_InvestigationFindings_CreatedByAppUserId] ON [InvestigationFindings] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815192324_AddInvestigationFindings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvestigationFindings_InvestigationId_AppUserId] ON [InvestigationFindings] ([InvestigationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815192324_AddInvestigationFindings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815192324_AddInvestigationFindings', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var13 nvarchar(max);
    SELECT @var13 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UploadFiles]') AND [c].[name] = N'StoredFileName');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [UploadFiles] DROP CONSTRAINT ' + @var13 + ';');
    ALTER TABLE [UploadFiles] ALTER COLUMN [StoredFileName] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var14 nvarchar(max);
    SELECT @var14 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UploadFiles]') AND [c].[name] = N'FileName');
    IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [UploadFiles] DROP CONSTRAINT ' + @var14 + ';');
    ALTER TABLE [UploadFiles] ALTER COLUMN [FileName] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var15 nvarchar(max);
    SELECT @var15 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UploadFiles]') AND [c].[name] = N'Description');
    IF @var15 IS NOT NULL EXEC(N'ALTER TABLE [UploadFiles] DROP CONSTRAINT ' + @var15 + ';');
    ALTER TABLE [UploadFiles] ALTER COLUMN [Description] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var16 nvarchar(max);
    SELECT @var16 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UploadFiles]') AND [c].[name] = N'ContentType');
    IF @var16 IS NOT NULL EXEC(N'ALTER TABLE [UploadFiles] DROP CONSTRAINT ' + @var16 + ';');
    ALTER TABLE [UploadFiles] ALTER COLUMN [ContentType] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var17 nvarchar(max);
    SELECT @var17 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'UrlName');
    IF @var17 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT ' + @var17 + ';');
    ALTER TABLE [Organizations] ALTER COLUMN [UrlName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var18 nvarchar(max);
    SELECT @var18 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'PublicWebsite');
    IF @var18 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT ' + @var18 + ';');
    ALTER TABLE [Organizations] ALTER COLUMN [PublicWebsite] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var19 nvarchar(max);
    SELECT @var19 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'PublicPhone');
    IF @var19 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT ' + @var19 + ';');
    ALTER TABLE [Organizations] ALTER COLUMN [PublicPhone] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var20 nvarchar(max);
    SELECT @var20 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'PublicEmail');
    IF @var20 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT ' + @var20 + ';');
    ALTER TABLE [Organizations] ALTER COLUMN [PublicEmail] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var21 nvarchar(max);
    SELECT @var21 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Organizations]') AND [c].[name] = N'Name');
    IF @var21 IS NOT NULL EXEC(N'ALTER TABLE [Organizations] DROP CONSTRAINT ' + @var21 + ';');
    ALTER TABLE [Organizations] ALTER COLUMN [Name] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var22 nvarchar(max);
    SELECT @var22 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Cases]') AND [c].[name] = N'StreetAddress2');
    IF @var22 IS NOT NULL EXEC(N'ALTER TABLE [Cases] DROP CONSTRAINT ' + @var22 + ';');
    ALTER TABLE [Cases] ALTER COLUMN [StreetAddress2] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var23 nvarchar(max);
    SELECT @var23 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Cases]') AND [c].[name] = N'Country');
    IF @var23 IS NOT NULL EXEC(N'ALTER TABLE [Cases] DROP CONSTRAINT ' + @var23 + ';');
    ALTER TABLE [Cases] ALTER COLUMN [Country] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    DECLARE @var24 nvarchar(max);
    SELECT @var24 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AppUsers]') AND [c].[name] = N'DisplayName');
    IF @var24 IS NOT NULL EXEC(N'ALTER TABLE [AppUsers] DROP CONSTRAINT ' + @var24 + ';');
    ALTER TABLE [AppUsers] ALTER COLUMN [DisplayName] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180022_BoundUnboundedStringColumns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816180022_BoundUnboundedStringColumns', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180959_AddSidecarInstallLog'
)
BEGIN
    CREATE TABLE [SidecarInstallLogs] (
        [Id] uniqueidentifier NOT NULL,
        [InstallId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(20) NOT NULL,
        [Version] nvarchar(50) NULL,
        [Platform] nvarchar(50) NULL,
        [AppUserId] uniqueidentifier NULL,
        [IpAddress] nvarchar(45) NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_SidecarInstallLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SidecarInstallLogs_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180959_AddSidecarInstallLog'
)
BEGIN
    CREATE INDEX [IX_SidecarInstallLogs_AppUserId] ON [SidecarInstallLogs] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180959_AddSidecarInstallLog'
)
BEGIN
    CREATE INDEX [IX_SidecarInstallLogs_DateCreated] ON [SidecarInstallLogs] ([DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180959_AddSidecarInstallLog'
)
BEGIN
    CREATE INDEX [IX_SidecarInstallLogs_InstallId] ON [SidecarInstallLogs] ([InstallId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816180959_AddSidecarInstallLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816180959_AddSidecarInstallLog', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE TABLE [EquipmentBrands] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsApproved] bit NOT NULL,
        [ProposedByOrganizationId] uniqueidentifier NULL,
        [ProposedByAppUserId] uniqueidentifier NULL,
        [ApprovedByAppUserId] uniqueidentifier NULL,
        [DateApproved] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentBrands] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentBrands_AppUsers_ApprovedByAppUserId] FOREIGN KEY ([ApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentBrands_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentBrands_AppUsers_ProposedByAppUserId] FOREIGN KEY ([ProposedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentBrands_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentBrands_Organizations_ProposedByOrganizationId] FOREIGN KEY ([ProposedByOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE TABLE [EquipmentCategories] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IconClass] nvarchar(100) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentCategories_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCategories_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE TABLE [EquipmentModels] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentBrandId] uniqueidentifier NOT NULL,
        [EquipmentCategoryId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ModelNumber] nvarchar(100) NULL,
        [Description] nvarchar(1000) NULL,
        [IsApproved] bit NOT NULL,
        [ProposedByOrganizationId] uniqueidentifier NULL,
        [ProposedByAppUserId] uniqueidentifier NULL,
        [ApprovedByAppUserId] uniqueidentifier NULL,
        [DateApproved] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentModels] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentModels_AppUsers_ApprovedByAppUserId] FOREIGN KEY ([ApprovedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentModels_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentModels_AppUsers_ProposedByAppUserId] FOREIGN KEY ([ProposedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentModels_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentModels_EquipmentBrands_EquipmentBrandId] FOREIGN KEY ([EquipmentBrandId]) REFERENCES [EquipmentBrands] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EquipmentModels_EquipmentCategories_EquipmentCategoryId] FOREIGN KEY ([EquipmentCategoryId]) REFERENCES [EquipmentCategories] ([Id]),
        CONSTRAINT [FK_EquipmentModels_Organizations_ProposedByOrganizationId] FOREIGN KEY ([ProposedByOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE TABLE [EquipmentItems] (
        [Id] uniqueidentifier NOT NULL,
        [OwnerAppUserId] uniqueidentifier NULL,
        [OwningOrganizationId] uniqueidentifier NULL,
        [EquipmentModelId] uniqueidentifier NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [SerialNumber] nvarchar(100) NULL,
        [AcquisitionDate] datetime2 NULL,
        [Notes] nvarchar(2000) NULL,
        [IsRetired] bit NOT NULL,
        [CurrentHolderAppUserId] uniqueidentifier NULL,
        [LastServicedDate] datetime2 NULL,
        [DefectNotes] nvarchar(2000) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentItems_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItems_AppUsers_CurrentHolderAppUserId] FOREIGN KEY ([CurrentHolderAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItems_AppUsers_OwnerAppUserId] FOREIGN KEY ([OwnerAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItems_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItems_EquipmentModels_EquipmentModelId] FOREIGN KEY ([EquipmentModelId]) REFERENCES [EquipmentModels] ([Id]),
        CONSTRAINT [FK_EquipmentItems_Organizations_OwningOrganizationId] FOREIGN KEY ([OwningOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE TABLE [EquipmentItemPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [IsPrimary] bit NOT NULL,
        [Caption] nvarchar(200) NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentItemPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentItemPhotos_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemPhotos_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemPhotos_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EquipmentItemPhotos_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentBrands_ApprovedByAppUserId] ON [EquipmentBrands] ([ApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentBrands_CreatedByAppUserId] ON [EquipmentBrands] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentBrands_Name] ON [EquipmentBrands] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentBrands_ProposedByAppUserId] ON [EquipmentBrands] ([ProposedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentBrands_ProposedByOrganizationId] ON [EquipmentBrands] ([ProposedByOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentBrands_UpdatedByAppUserId] ON [EquipmentBrands] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentCategories_CreatedByAppUserId] ON [EquipmentCategories] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentCategories_Name] ON [EquipmentCategories] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentCategories_UpdatedByAppUserId] ON [EquipmentCategories] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemPhotos_CreatedByAppUserId] ON [EquipmentItemPhotos] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentItemPhotos_EquipmentItemId_UploadFileId] ON [EquipmentItemPhotos] ([EquipmentItemId], [UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemPhotos_UpdatedByAppUserId] ON [EquipmentItemPhotos] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemPhotos_UploadFileId] ON [EquipmentItemPhotos] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_CreatedByAppUserId] ON [EquipmentItems] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_CurrentHolderAppUserId] ON [EquipmentItems] ([CurrentHolderAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_EquipmentModelId] ON [EquipmentItems] ([EquipmentModelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_OwnerAppUserId] ON [EquipmentItems] ([OwnerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_OwningOrganizationId] ON [EquipmentItems] ([OwningOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentItems_UpdatedByAppUserId] ON [EquipmentItems] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_ApprovedByAppUserId] ON [EquipmentModels] ([ApprovedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_CreatedByAppUserId] ON [EquipmentModels] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentModels_EquipmentBrandId_Name] ON [EquipmentModels] ([EquipmentBrandId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_EquipmentCategoryId] ON [EquipmentModels] ([EquipmentCategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_ProposedByAppUserId] ON [EquipmentModels] ([ProposedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_ProposedByOrganizationId] ON [EquipmentModels] ([ProposedByOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    CREATE INDEX [IX_EquipmentModels_UpdatedByAppUserId] ON [EquipmentModels] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816190437_AddEquipmentCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816190437_AddEquipmentCore', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816192802_AddEquipmentVisibilityAndLoanAudience'
)
BEGIN
    ALTER TABLE [EquipmentItems] ADD [IncludeInGlobalCatalog] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816192802_AddEquipmentVisibilityAndLoanAudience'
)
BEGIN
    ALTER TABLE [EquipmentItems] ADD [LoanAudience] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816192802_AddEquipmentVisibilityAndLoanAudience'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816192802_AddEquipmentVisibilityAndLoanAudience', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    CREATE TABLE [EquipmentItemShares] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentItemShares] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentItemShares_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemShares_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemShares_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EquipmentItemShares_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemShares_CreatedByAppUserId] ON [EquipmentItemShares] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentItemShares_EquipmentItemId_OrganizationId] ON [EquipmentItemShares] ([EquipmentItemId], [OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemShares_OrganizationId] ON [EquipmentItemShares] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemShares_UpdatedByAppUserId] ON [EquipmentItemShares] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816193617_AddEquipmentItemShare'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816193617_AddEquipmentItemShare', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    CREATE TABLE [EquipmentServiceLogs] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [EntryType] int NOT NULL,
        [EntryDate] datetime2 NOT NULL,
        [Notes] nvarchar(2000) NOT NULL,
        [PerformedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentServiceLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentServiceLogs_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentServiceLogs_AppUsers_PerformedByAppUserId] FOREIGN KEY ([PerformedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentServiceLogs_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentServiceLogs_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    CREATE INDEX [IX_EquipmentServiceLogs_CreatedByAppUserId] ON [EquipmentServiceLogs] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    CREATE INDEX [IX_EquipmentServiceLogs_EquipmentItemId_EntryDate] ON [EquipmentServiceLogs] ([EquipmentItemId], [EntryDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    CREATE INDEX [IX_EquipmentServiceLogs_PerformedByAppUserId] ON [EquipmentServiceLogs] ([PerformedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    CREATE INDEX [IX_EquipmentServiceLogs_UpdatedByAppUserId] ON [EquipmentServiceLogs] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816215502_AddEquipmentServiceLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816215502_AddEquipmentServiceLog', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE TABLE [EquipmentCheckouts] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [BorrowerAppUserId] uniqueidentifier NOT NULL,
        [BorrowedForOrganizationId] uniqueidentifier NULL,
        [InvestigationId] uniqueidentifier NULL,
        [Status] int NOT NULL,
        [RequestNotes] nvarchar(1000) NULL,
        [ReviewNotes] nvarchar(1000) NULL,
        [ReviewedByAppUserId] uniqueidentifier NULL,
        [DateReviewed] datetime2 NULL,
        [DateNeededFrom] datetime2 NULL,
        [DateDue] datetime2 NULL,
        [DateCheckedOut] datetime2 NULL,
        [CheckedOutConfirmedByAppUserId] uniqueidentifier NULL,
        [DateReturned] datetime2 NULL,
        [ReturnedReceivedByAppUserId] uniqueidentifier NULL,
        [ReturnConditionNotes] nvarchar(2000) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentCheckouts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_AppUsers_BorrowerAppUserId] FOREIGN KEY ([BorrowerAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_AppUsers_ReviewedByAppUserId] FOREIGN KEY ([ReviewedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]),
        CONSTRAINT [FK_EquipmentCheckouts_Organizations_BorrowedForOrganizationId] FOREIGN KEY ([BorrowedForOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_BorrowedForOrganizationId_Status] ON [EquipmentCheckouts] ([BorrowedForOrganizationId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_BorrowerAppUserId] ON [EquipmentCheckouts] ([BorrowerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_CreatedByAppUserId] ON [EquipmentCheckouts] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_EquipmentItemId_Status] ON [EquipmentCheckouts] ([EquipmentItemId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_InvestigationId] ON [EquipmentCheckouts] ([InvestigationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_ReviewedByAppUserId] ON [EquipmentCheckouts] ([ReviewedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckouts_UpdatedByAppUserId] ON [EquipmentCheckouts] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817110128_AddEquipmentCheckout'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817110128_AddEquipmentCheckout', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE TABLE [EquipmentCheckoutPhotos] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentCheckoutId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [Stage] int NOT NULL,
        [Caption] nvarchar(200) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentCheckoutPhotos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutPhotos_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutPhotos_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutPhotos_EquipmentCheckouts_EquipmentCheckoutId] FOREIGN KEY ([EquipmentCheckoutId]) REFERENCES [EquipmentCheckouts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EquipmentCheckoutPhotos_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE TABLE [EquipmentCheckoutRenewals] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentCheckoutId] uniqueidentifier NOT NULL,
        [RequestedDateDue] datetime2 NOT NULL,
        [Status] int NOT NULL,
        [RequestNotes] nvarchar(1000) NULL,
        [ReviewNotes] nvarchar(1000) NULL,
        [ReviewedByAppUserId] uniqueidentifier NULL,
        [DateReviewed] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentCheckoutRenewals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutRenewals_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutRenewals_AppUsers_ReviewedByAppUserId] FOREIGN KEY ([ReviewedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutRenewals_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentCheckoutRenewals_EquipmentCheckouts_EquipmentCheckoutId] FOREIGN KEY ([EquipmentCheckoutId]) REFERENCES [EquipmentCheckouts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutPhotos_CreatedByAppUserId] ON [EquipmentCheckoutPhotos] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutPhotos_EquipmentCheckoutId_Stage] ON [EquipmentCheckoutPhotos] ([EquipmentCheckoutId], [Stage]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutPhotos_UpdatedByAppUserId] ON [EquipmentCheckoutPhotos] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutPhotos_UploadFileId] ON [EquipmentCheckoutPhotos] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutRenewals_CreatedByAppUserId] ON [EquipmentCheckoutRenewals] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutRenewals_EquipmentCheckoutId_Status] ON [EquipmentCheckoutRenewals] ([EquipmentCheckoutId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutRenewals_ReviewedByAppUserId] ON [EquipmentCheckoutRenewals] ([ReviewedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    CREATE INDEX [IX_EquipmentCheckoutRenewals_UpdatedByAppUserId] ON [EquipmentCheckoutRenewals] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817114147_AddEquipmentCheckoutPhotosAndRenewals', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817125238_AddUserMessageHideSenderIdentity'
)
BEGIN
    ALTER TABLE [UserMessages] ADD [HideSenderIdentity] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817125238_AddUserMessageHideSenderIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817125238_AddUserMessageHideSenderIdentity', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817130554_AddEquipmentModelPageFields'
)
BEGIN
    ALTER TABLE [EquipmentItems] ADD [LinkClickCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817130554_AddEquipmentModelPageFields'
)
BEGIN
    ALTER TABLE [EquipmentItems] ADD [ViewCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817130554_AddEquipmentModelPageFields'
)
BEGIN
    ALTER TABLE [EquipmentItems] ADD [WebsiteUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817130554_AddEquipmentModelPageFields'
)
BEGIN
    ALTER TABLE [EquipmentItemPhotos] ADD [ExcludeFromCatalog] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817130554_AddEquipmentModelPageFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817130554_AddEquipmentModelPageFields', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE TABLE [EquipmentItemFaqs] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [Question] nvarchar(500) NOT NULL,
        [Answer] nvarchar(4000) NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentItemFaqs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentItemFaqs_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemFaqs_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentItemFaqs_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE TABLE [EquipmentQuestions] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentItemId] uniqueidentifier NOT NULL,
        [AskedByAppUserId] uniqueidentifier NOT NULL,
        [QuestionText] nvarchar(2000) NOT NULL,
        [AnswerText] nvarchar(4000) NULL,
        [Status] int NOT NULL,
        [AnsweredByAppUserId] uniqueidentifier NULL,
        [AnsweredDate] datetime2 NULL,
        [PromotedToFaqId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentQuestions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentQuestions_AppUsers_AnsweredByAppUserId] FOREIGN KEY ([AnsweredByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentQuestions_AppUsers_AskedByAppUserId] FOREIGN KEY ([AskedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentQuestions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentQuestions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentQuestions_EquipmentItems_EquipmentItemId] FOREIGN KEY ([EquipmentItemId]) REFERENCES [EquipmentItems] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemFaqs_CreatedByAppUserId] ON [EquipmentItemFaqs] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemFaqs_EquipmentItemId_SortOrder] ON [EquipmentItemFaqs] ([EquipmentItemId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentItemFaqs_UpdatedByAppUserId] ON [EquipmentItemFaqs] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentQuestions_AnsweredByAppUserId] ON [EquipmentQuestions] ([AnsweredByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentQuestions_AskedByAppUserId] ON [EquipmentQuestions] ([AskedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentQuestions_CreatedByAppUserId] ON [EquipmentQuestions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentQuestions_EquipmentItemId_Status] ON [EquipmentQuestions] ([EquipmentItemId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    CREATE INDEX [IX_EquipmentQuestions_UpdatedByAppUserId] ON [EquipmentQuestions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817135757_AddEquipmentFaqAndQuestions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817135757_AddEquipmentFaqAndQuestions', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE TABLE [EquipmentLoanFeedbacks] (
        [Id] uniqueidentifier NOT NULL,
        [EquipmentCheckoutId] uniqueidentifier NOT NULL,
        [AuthorAppUserId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [CounterpartyComment] nvarchar(2000) NULL,
        [Rating] int NULL,
        [ProductComment] nvarchar(2000) NULL,
        [SubjectAppUserId] uniqueidentifier NULL,
        [SubjectOrganizationId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EquipmentLoanFeedbacks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EquipmentLoanFeedbacks_AppUsers_AuthorAppUserId] FOREIGN KEY ([AuthorAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentLoanFeedbacks_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentLoanFeedbacks_AppUsers_SubjectAppUserId] FOREIGN KEY ([SubjectAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentLoanFeedbacks_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EquipmentLoanFeedbacks_EquipmentCheckouts_EquipmentCheckoutId] FOREIGN KEY ([EquipmentCheckoutId]) REFERENCES [EquipmentCheckouts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EquipmentLoanFeedbacks_Organizations_SubjectOrganizationId] FOREIGN KEY ([SubjectOrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE INDEX [IX_EquipmentLoanFeedbacks_AuthorAppUserId] ON [EquipmentLoanFeedbacks] ([AuthorAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE INDEX [IX_EquipmentLoanFeedbacks_CreatedByAppUserId] ON [EquipmentLoanFeedbacks] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EquipmentLoanFeedbacks_EquipmentCheckoutId_Role] ON [EquipmentLoanFeedbacks] ([EquipmentCheckoutId], [Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE INDEX [IX_EquipmentLoanFeedbacks_SubjectAppUserId_Role] ON [EquipmentLoanFeedbacks] ([SubjectAppUserId], [Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE INDEX [IX_EquipmentLoanFeedbacks_SubjectOrganizationId_Role] ON [EquipmentLoanFeedbacks] ([SubjectOrganizationId], [Role]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    CREATE INDEX [IX_EquipmentLoanFeedbacks_UpdatedByAppUserId] ON [EquipmentLoanFeedbacks] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817140747_AddEquipmentLoanFeedback'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817140747_AddEquipmentLoanFeedback', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817150953_AddCmsPageDrafts'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD [DraftOfOrganizationPageId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817150953_AddCmsPageDrafts'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrganizationPages_DraftOfOrganizationPageId] ON [OrganizationPages] ([DraftOfOrganizationPageId]) WHERE [DraftOfOrganizationPageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817150953_AddCmsPageDrafts'
)
BEGIN
    ALTER TABLE [OrganizationPages] ADD CONSTRAINT [FK_OrganizationPages_OrganizationPages_DraftOfOrganizationPageId] FOREIGN KEY ([DraftOfOrganizationPageId]) REFERENCES [OrganizationPages] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817150953_AddCmsPageDrafts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817150953_AddCmsPageDrafts', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817152743_AddOrganizationCmsTemplates'
)
BEGIN
    CREATE TABLE [OrganizationCmsTemplates] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Scope] int NOT NULL,
        [SectionType] int NOT NULL,
        [ContentJson] nvarchar(max) NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationCmsTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationCmsTemplates_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationCmsTemplates_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationCmsTemplates_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817152743_AddOrganizationCmsTemplates'
)
BEGIN
    CREATE INDEX [IX_OrganizationCmsTemplates_CreatedByAppUserId] ON [OrganizationCmsTemplates] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817152743_AddOrganizationCmsTemplates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationCmsTemplates_OrganizationId_Scope_Name] ON [OrganizationCmsTemplates] ([OrganizationId], [Scope], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817152743_AddOrganizationCmsTemplates'
)
BEGIN
    CREATE INDEX [IX_OrganizationCmsTemplates_UpdatedByAppUserId] ON [OrganizationCmsTemplates] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817152743_AddOrganizationCmsTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817152743_AddOrganizationCmsTemplates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    DROP INDEX [IX_OrgCalendarEvents_OrganizationId] ON [OrgCalendarEvents];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [AttendeeCapacity] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [HideExactLocation] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [PlaceId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [RsvpClosesAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_OrganizationId_IsPublic_StartDateTime] ON [OrgCalendarEvents] ([OrganizationId], [IsPublic], [StartDateTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    CREATE INDEX [IX_OrgCalendarEvents_PlaceId] ON [OrgCalendarEvents] ([PlaceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD CONSTRAINT [FK_OrgCalendarEvents_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162528_AddPublicEventFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817162528_AddPublicEventFields', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162918_AddPublicEventSlug'
)
BEGIN
    ALTER TABLE [OrgCalendarEvents] ADD [UrlName] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162918_AddPublicEventSlug'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OrgCalendarEvents_OrganizationId_UrlName] ON [OrgCalendarEvents] ([OrganizationId], [UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817162918_AddPublicEventSlug'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817162918_AddPublicEventSlug', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817171953_AddEventAttendanceInvites'
)
BEGIN
    CREATE TABLE [EventAttendanceInvites] (
        [Id] uniqueidentifier NOT NULL,
        [OrgCalendarEventId] uniqueidentifier NOT NULL,
        [Email] nvarchar(320) NOT NULL,
        [DisplayName] nvarchar(200) NULL,
        [Token] nvarchar(128) NULL,
        [DateExpires] datetime2 NOT NULL,
        [DateConfirmed] datetime2 NULL,
        [ConfirmedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EventAttendanceInvites] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventAttendanceInvites_AppUsers_ConfirmedByAppUserId] FOREIGN KEY ([ConfirmedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventAttendanceInvites_OrgCalendarEvents_OrgCalendarEventId] FOREIGN KEY ([OrgCalendarEventId]) REFERENCES [OrgCalendarEvents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817171953_AddEventAttendanceInvites'
)
BEGIN
    CREATE INDEX [IX_EventAttendanceInvites_ConfirmedByAppUserId] ON [EventAttendanceInvites] ([ConfirmedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817171953_AddEventAttendanceInvites'
)
BEGIN
    CREATE INDEX [IX_EventAttendanceInvites_OrgCalendarEventId_Email] ON [EventAttendanceInvites] ([OrgCalendarEventId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817171953_AddEventAttendanceInvites'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EventAttendanceInvites_Token] ON [EventAttendanceInvites] ([Token]) WHERE [Token] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817171953_AddEventAttendanceInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817171953_AddEventAttendanceInvites', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817182911_AddCaseSlug'
)
BEGIN
    ALTER TABLE [Cases] ADD [UrlName] nvarchar(120) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817182911_AddCaseSlug'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Cases_OrganizationId_UrlName] ON [Cases] ([OrganizationId], [UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817182911_AddCaseSlug'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817182911_AddCaseSlug', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817183946_AddInvestigationSlug'
)
BEGIN
    ALTER TABLE [Investigations] ADD [UrlName] nvarchar(140) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817183946_AddInvestigationSlug'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Investigations_OrganizationId_UrlName] ON [Investigations] ([OrganizationId], [UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817183946_AddInvestigationSlug'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817183946_AddInvestigationSlug', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    WITH Survivors AS (
        SELECT Id,
               ExperienceCategoryId,
               LOWER(LTRIM(RTRIM(Name))) AS NormalizedName,
               ROW_NUMBER() OVER (
                   PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                   ORDER BY DateCreated, Id) AS Rn
        FROM ExperienceTypes
        WHERE Name IS NOT NULL
    )
    UPDATE j
    SET j.ExperienceTypeId = keep.Id
    FROM CaseTimelineEntryExperienceTypes j
    INNER JOIN Survivors dup  ON dup.Id = j.ExperienceTypeId AND dup.Rn > 1
    INNER JOIN Survivors keep ON keep.ExperienceCategoryId = dup.ExperienceCategoryId
                             AND keep.NormalizedName = dup.NormalizedName
                             AND keep.Rn = 1
    -- An entry already tagged with the survivor would collide on the join's composite
    -- key, so those rows are dropped by the delete below instead of being moved.
    WHERE NOT EXISTS (
        SELECT 1 FROM CaseTimelineEntryExperienceTypes existing
        WHERE existing.CaseTimelineEntryId = j.CaseTimelineEntryId
          AND existing.ExperienceTypeId = keep.Id);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    WITH Duplicates AS (
        SELECT Id,
               ROW_NUMBER() OVER (
                   PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                   ORDER BY DateCreated, Id) AS Rn
        FROM ExperienceTypes
        WHERE Name IS NOT NULL
    )
    DELETE FROM CaseTimelineEntryExperienceTypes
    WHERE ExperienceTypeId IN (SELECT Id FROM Duplicates WHERE Rn > 1);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    WITH Duplicates AS (
        SELECT Id,
               ROW_NUMBER() OVER (
                   PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                   ORDER BY DateCreated, Id) AS Rn
        FROM ExperienceTypes
        WHERE Name IS NOT NULL
    )
    DELETE FROM ExperienceTypes
    WHERE Id IN (SELECT Id FROM Duplicates WHERE Rn > 1);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    DROP INDEX [IX_ExperienceTypes_ExperienceCategoryId] ON [ExperienceTypes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    DECLARE @var25 nvarchar(max);
    SELECT @var25 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ExperienceTypes]') AND [c].[name] = N'Name');
    IF @var25 IS NOT NULL EXEC(N'ALTER TABLE [ExperienceTypes] DROP CONSTRAINT ' + @var25 + ';');
    ALTER TABLE [ExperienceTypes] ALTER COLUMN [Name] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ExperienceTypes_ExperienceCategoryId_Name] ON [ExperienceTypes] ([ExperienceCategoryId], [Name]) WHERE [Name] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817201721_AddExperienceTypeNameUniqueIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817201721_AddExperienceTypeNameUniqueIndex', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    UPDATE Organizations SET UrlName = LOWER(LTRIM(RTRIM(UrlName))) WHERE UrlName IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    WITH Duplicates AS (
        SELECT Id, UrlName,
               ROW_NUMBER() OVER (PARTITION BY UrlName ORDER BY DateCreated, Id) AS Rn
        FROM Organizations
        WHERE UrlName IS NOT NULL
    )
    UPDATE d
    SET d.UrlName = LEFT(dup.UrlName, 96) + '-' + CAST(dup.Rn AS nvarchar(3))
    FROM Organizations d
    INNER JOIN Duplicates dup ON dup.Id = d.Id
    WHERE dup.Rn > 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    CREATE TABLE [OrganizationUrlNameAliases] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [UrlName] nvarchar(100) NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationUrlNameAliases] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationUrlNameAliases_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Organizations_UrlName] ON [Organizations] ([UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    CREATE INDEX [IX_OrganizationUrlNameAliases_OrganizationId] ON [OrganizationUrlNameAliases] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationUrlNameAliases_UrlName] ON [OrganizationUrlNameAliases] ([UrlName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817212140_AddOrganizationUrlNameUniquenessAndAliases', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212937_AddEquipmentCatalogSlugs'
)
BEGIN
    ALTER TABLE [EquipmentModels] ADD [UrlName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212937_AddEquipmentCatalogSlugs'
)
BEGIN
    ALTER TABLE [EquipmentBrands] ADD [UrlName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212937_AddEquipmentCatalogSlugs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EquipmentModels_EquipmentBrandId_UrlName] ON [EquipmentModels] ([EquipmentBrandId], [UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212937_AddEquipmentCatalogSlugs'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_EquipmentBrands_UrlName] ON [EquipmentBrands] ([UrlName]) WHERE [UrlName] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260817212937_AddEquipmentCatalogSlugs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260817212937_AddEquipmentCatalogSlugs', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820121159_AddSignInEvents'
)
BEGIN
    CREATE TABLE [SignInEvents] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NULL,
        [Utc] datetime2 NOT NULL,
        [Succeeded] bit NOT NULL,
        [Method] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_SignInEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SignInEvents_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820121159_AddSignInEvents'
)
BEGIN
    CREATE INDEX [IX_SignInEvents_AppUserId_Utc] ON [SignInEvents] ([AppUserId], [Utc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820121159_AddSignInEvents'
)
BEGIN
    CREATE INDEX [IX_SignInEvents_Utc_Succeeded] ON [SignInEvents] ([Utc], [Succeeded]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820121159_AddSignInEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820121159_AddSignInEvents', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820133541_AddEventReminderSent'
)
BEGIN
    CREATE TABLE [EventReminderSents] (
        [Id] uniqueidentifier NOT NULL,
        [OrgCalendarEventId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [SentUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_EventReminderSents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventReminderSents_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventReminderSents_OrgCalendarEvents_OrgCalendarEventId] FOREIGN KEY ([OrgCalendarEventId]) REFERENCES [OrgCalendarEvents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820133541_AddEventReminderSent'
)
BEGIN
    CREATE INDEX [IX_EventReminderSents_AppUserId] ON [EventReminderSents] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820133541_AddEventReminderSent'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EventReminderSents_OrgCalendarEventId_AppUserId] ON [EventReminderSents] ([OrgCalendarEventId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820133541_AddEventReminderSent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820133541_AddEventReminderSent', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [HiddenByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [HiddenUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE TABLE [OrgMessageHashtags] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NOT NULL,
        [Tag] nvarchar(64) NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageHashtags] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMessageHashtags_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE TABLE [OrgMessageMentions] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NOT NULL,
        [MentionedAppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageMentions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMessageMentions_AppUsers_MentionedAppUserId] FOREIGN KEY ([MentionedAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageMentions_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE TABLE [OrgMessageReports] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NOT NULL,
        [ReportedByAppUserId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(1000) NULL,
        [Outcome] int NOT NULL,
        [ResolvedUtc] datetime2 NULL,
        [ResolvedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgMessageReports_AppUsers_ReportedByAppUserId] FOREIGN KEY ([ReportedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageReports_AppUsers_ResolvedByAppUserId] FOREIGN KEY ([ResolvedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageReports_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE TABLE [UserFollows] (
        [Id] uniqueidentifier NOT NULL,
        [FollowerAppUserId] uniqueidentifier NOT NULL,
        [FollowedAppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_UserFollows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserFollows_AppUsers_FollowedAppUserId] FOREIGN KEY ([FollowedAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserFollows_AppUsers_FollowerAppUserId] FOREIGN KEY ([FollowerAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_ChannelType_HiddenUtc_DateCreated] ON [OrgMessages] ([ChannelType], [HiddenUtc], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_HiddenByAppUserId] ON [OrgMessages] ([HiddenByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgMessageHashtags_OrgMessageId_Tag] ON [OrgMessageHashtags] ([OrgMessageId], [Tag]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessageHashtags_Tag_DateCreated] ON [OrgMessageHashtags] ([Tag], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessageMentions_MentionedAppUserId_DateCreated] ON [OrgMessageMentions] ([MentionedAppUserId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgMessageMentions_OrgMessageId_MentionedAppUserId] ON [OrgMessageMentions] ([OrgMessageId], [MentionedAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId] ON [OrgMessageReports] ([OrgMessageId], [ReportedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessageReports_Outcome_DateCreated] ON [OrgMessageReports] ([Outcome], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessageReports_ReportedByAppUserId] ON [OrgMessageReports] ([ReportedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_OrgMessageReports_ResolvedByAppUserId] ON [OrgMessageReports] ([ResolvedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE INDEX [IX_UserFollows_FollowedAppUserId] ON [UserFollows] ([FollowedAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserFollows_FollowerAppUserId_FollowedAppUserId] ON [UserFollows] ([FollowerAppUserId], [FollowedAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD CONSTRAINT [FK_OrgMessages_AppUsers_HiddenByAppUserId] FOREIGN KEY ([HiddenByAppUserId]) REFERENCES [AppUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820140519_AddPublicFeed'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820140519_AddPublicFeed', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820141225_AddUserHandle'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [Handle] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820141225_AddUserHandle'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AppUsers_Handle] ON [AppUsers] ([Handle]) WHERE [Handle] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820141225_AddUserHandle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820141225_AddUserHandle', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE TABLE [Publications] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [UrlName] nvarchar(120) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IsPublic] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Publications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Publications_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Publications_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Publications_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE TABLE [PublicationPosts] (
        [Id] uniqueidentifier NOT NULL,
        [PublicationId] uniqueidentifier NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [UrlName] nvarchar(160) NOT NULL,
        [Excerpt] nvarchar(1000) NULL,
        [BodyHtml] nvarchar(max) NOT NULL,
        [PublishedUtc] datetime2 NULL,
        [RequiredTier] int NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PublicationPosts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PublicationPosts_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_PublicationPosts_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_PublicationPosts_Publications_PublicationId] FOREIGN KEY ([PublicationId]) REFERENCES [Publications] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE TABLE [PublicationSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [PublicationId] uniqueidentifier NOT NULL,
        [SubscriberAppUserId] uniqueidentifier NOT NULL,
        [Tier] int NULL,
        [CancelledUtc] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_PublicationSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PublicationSubscriptions_AppUsers_SubscriberAppUserId] FOREIGN KEY ([SubscriberAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_PublicationSubscriptions_Publications_PublicationId] FOREIGN KEY ([PublicationId]) REFERENCES [Publications] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_PublicationPosts_CreatedByAppUserId] ON [PublicationPosts] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_PublicationPosts_PublicationId_PublishedUtc] ON [PublicationPosts] ([PublicationId], [PublishedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PublicationPosts_PublicationId_UrlName] ON [PublicationPosts] ([PublicationId], [UrlName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_PublicationPosts_UpdatedByAppUserId] ON [PublicationPosts] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_Publications_CreatedByAppUserId] ON [Publications] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_Publications_OrganizationId] ON [Publications] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_Publications_UpdatedByAppUserId] ON [Publications] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Publications_UrlName] ON [Publications] ([UrlName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PublicationSubscriptions_PublicationId_SubscriberAppUserId] ON [PublicationSubscriptions] ([PublicationId], [SubscriberAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    CREATE INDEX [IX_PublicationSubscriptions_SubscriberAppUserId_CancelledUtc] ON [PublicationSubscriptions] ([SubscriberAppUserId], [CancelledUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260820172611_AddPublications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260820172611_AddPublications', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821183541_AddUserLegalNameGenderAndBirthYear'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [BirthYear] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821183541_AddUserLegalNameGenderAndBirthYear'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [FirstName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821183541_AddUserLegalNameGenderAndBirthYear'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [Gender] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821183541_AddUserLegalNameGenderAndBirthYear'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [LastName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821183541_AddUserLegalNameGenderAndBirthYear'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260821183541_AddUserLegalNameGenderAndBirthYear', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [Coupons] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Kind] int NOT NULL,
        [PercentOff] int NULL,
        [AmountOff] decimal(18,2) NULL,
        [Duration] int NOT NULL,
        [DurationPeriods] int NULL,
        [MaxRedemptions] int NULL,
        [RedemptionCount] int NOT NULL,
        [ValidFromUtc] datetime2 NULL,
        [RedeemByUtc] datetime2 NULL,
        [AppliesToInterval] int NULL,
        [AppliesTo] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Coupons_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_Coupons_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [OrganizationBillingContacts] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationBillingContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationBillingContacts_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationBillingContacts_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationBillingContacts_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationBillingContacts_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [SubscriptionTiers] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [MinMembers] int NOT NULL,
        [MaxMembers] int NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionTiers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionTiers_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTiers_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [CouponCodes] (
        [Id] uniqueidentifier NOT NULL,
        [CouponId] uniqueidentifier NOT NULL,
        [Code] nvarchar(64) NOT NULL,
        [MaxRedemptions] int NULL,
        [RedemptionCount] int NOT NULL,
        [IssuedTo] nvarchar(256) NULL,
        [RestrictedToAppUserId] uniqueidentifier NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CouponCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CouponCodes_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CouponCodes_AppUsers_RestrictedToAppUserId] FOREIGN KEY ([RestrictedToAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CouponCodes_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CouponCodes_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [OrganizationSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [SubscriptionTierId] uniqueidentifier NULL,
        [MemberCountAtPeriodStart] int NOT NULL,
        [Interval] int NOT NULL,
        [PriceAtPeriodStart] decimal(18,2) NOT NULL,
        [CurrentPeriodStart] datetime2 NULL,
        [CurrentPeriodEnd] datetime2 NULL,
        [CancelAtPeriodEnd] bit NOT NULL,
        [LapsedAtUtc] datetime2 NULL,
        [FirstPaidPeriodStartUtc] datetime2 NULL,
        [ProviderSubscriptionRef] nvarchar(max) NULL,
        [ProviderName] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationSubscriptions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationSubscriptions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationSubscriptions_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationSubscriptions_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [SubscriptionTierPrices] (
        [Id] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [Interval] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionTierPrices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionTierPrices_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierPrices_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierPrices_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE TABLE [CouponRedemptions] (
        [Id] uniqueidentifier NOT NULL,
        [CouponId] uniqueidentifier NOT NULL,
        [CouponCodeId] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PeriodsRemaining] int NULL,
        [RedeemedAtUtc] datetime2 NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CouponRedemptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CouponRedemptions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CouponRedemptions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CouponRedemptions_CouponCodes_CouponCodeId] FOREIGN KEY ([CouponCodeId]) REFERENCES [CouponCodes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CouponRedemptions_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CouponRedemptions_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CouponCodes_Code] ON [CouponCodes] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponCodes_CouponId] ON [CouponCodes] ([CouponId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponCodes_CreatedByAppUserId] ON [CouponCodes] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponCodes_RestrictedToAppUserId] ON [CouponCodes] ([RestrictedToAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponCodes_UpdatedByAppUserId] ON [CouponCodes] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_CouponCodeId] ON [CouponRedemptions] ([CouponCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CouponRedemptions_CouponId_OrganizationId] ON [CouponRedemptions] ([CouponId], [OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_CreatedByAppUserId] ON [CouponRedemptions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_OrganizationId] ON [CouponRedemptions] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_CouponRedemptions_UpdatedByAppUserId] ON [CouponRedemptions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_Coupons_CreatedByAppUserId] ON [Coupons] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_Coupons_UpdatedByAppUserId] ON [Coupons] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationBillingContacts_AppUserId] ON [OrganizationBillingContacts] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationBillingContacts_CreatedByAppUserId] ON [OrganizationBillingContacts] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationBillingContacts_OrganizationId_AppUserId] ON [OrganizationBillingContacts] ([OrganizationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationBillingContacts_UpdatedByAppUserId] ON [OrganizationBillingContacts] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationSubscriptions_CreatedByAppUserId] ON [OrganizationSubscriptions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationSubscriptions_OrganizationId] ON [OrganizationSubscriptions] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationSubscriptions_Status_CurrentPeriodEnd] ON [OrganizationSubscriptions] ([Status], [CurrentPeriodEnd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationSubscriptions_SubscriptionTierId] ON [OrganizationSubscriptions] ([SubscriptionTierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_OrganizationSubscriptions_UpdatedByAppUserId] ON [OrganizationSubscriptions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierPrices_CreatedByAppUserId] ON [SubscriptionTierPrices] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionTierPrices_SubscriptionTierId_Interval] ON [SubscriptionTierPrices] ([SubscriptionTierId], [Interval]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierPrices_UpdatedByAppUserId] ON [SubscriptionTierPrices] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTiers_CreatedByAppUserId] ON [SubscriptionTiers] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTiers_IsActive_MinMembers] ON [SubscriptionTiers] ([IsActive], [MinMembers]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTiers_UpdatedByAppUserId] ON [SubscriptionTiers] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822162106_AddSubscriptionsCouponsAndBillingIntervals', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822163033_AddSubscriptionTierLimits'
)
BEGIN
    CREATE TABLE [SubscriptionTierLimits] (
        [Id] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [Limit] int NOT NULL,
        [MaxValue] int NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionTierLimits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionTierLimits_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierLimits_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierLimits_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822163033_AddSubscriptionTierLimits'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierLimits_CreatedByAppUserId] ON [SubscriptionTierLimits] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822163033_AddSubscriptionTierLimits'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionTierLimits_SubscriptionTierId_Limit] ON [SubscriptionTierLimits] ([SubscriptionTierId], [Limit]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822163033_AddSubscriptionTierLimits'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierLimits_UpdatedByAppUserId] ON [SubscriptionTierLimits] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822163033_AddSubscriptionTierLimits'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822163033_AddSubscriptionTierLimits', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    CREATE TABLE [SubscriptionContractTerms] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationSubscriptionId] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [TierName] nvarchar(100) NOT NULL,
        [Interval] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [LimitsJson] nvarchar(max) NOT NULL,
        [PeriodStartUtc] datetime2 NOT NULL,
        [PeriodEndUtc] datetime2 NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionContractTerms] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionContractTerms_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionContractTerms_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionContractTerms_OrganizationSubscriptions_OrganizationSubscriptionId] FOREIGN KEY ([OrganizationSubscriptionId]) REFERENCES [OrganizationSubscriptions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SubscriptionContractTerms_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    CREATE INDEX [IX_SubscriptionContractTerms_CreatedByAppUserId] ON [SubscriptionContractTerms] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    CREATE INDEX [IX_SubscriptionContractTerms_OrganizationSubscriptionId_PeriodStartUtc] ON [SubscriptionContractTerms] ([OrganizationSubscriptionId], [PeriodStartUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    CREATE INDEX [IX_SubscriptionContractTerms_SubscriptionTierId] ON [SubscriptionContractTerms] ([SubscriptionTierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    CREATE INDEX [IX_SubscriptionContractTerms_UpdatedByAppUserId] ON [SubscriptionContractTerms] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822164243_AddSubscriptionContractTerms'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822164243_AddSubscriptionContractTerms', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE TABLE [TierChangeNotices] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [Sentences] nvarchar(4000) NOT NULL,
        [EffectiveAtUtc] datetime2 NOT NULL,
        [DeliverAtUtc] datetime2 NOT NULL,
        [DeliveredAtUtc] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TierChangeNotices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TierChangeNotices_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_TierChangeNotices_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_TierChangeNotices_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TierChangeNotices_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE INDEX [IX_TierChangeNotices_CreatedByAppUserId] ON [TierChangeNotices] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE INDEX [IX_TierChangeNotices_DeliveredAtUtc_DeliverAtUtc] ON [TierChangeNotices] ([DeliveredAtUtc], [DeliverAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE INDEX [IX_TierChangeNotices_OrganizationId] ON [TierChangeNotices] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE INDEX [IX_TierChangeNotices_SubscriptionTierId] ON [TierChangeNotices] ([SubscriptionTierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    CREATE INDEX [IX_TierChangeNotices_UpdatedByAppUserId] ON [TierChangeNotices] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822171306_AddTierChangeNotices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822171306_AddTierChangeNotices', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175002_AddRedemptionAmounts'
)
BEGIN
    ALTER TABLE [CouponRedemptions] ADD [Discount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175002_AddRedemptionAmounts'
)
BEGIN
    ALTER TABLE [CouponRedemptions] ADD [ListPrice] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175002_AddRedemptionAmounts'
)
BEGIN
    ALTER TABLE [CouponRedemptions] ADD [Payable] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175002_AddRedemptionAmounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822175002_AddRedemptionAmounts', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175656_AddCasePauseAndLapseNotices'
)
BEGIN
    ALTER TABLE [OrganizationSubscriptions] ADD [OneWeekNoticeSentForPeriodEnd] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175656_AddCasePauseAndLapseNotices'
)
BEGIN
    ALTER TABLE [OrganizationSubscriptions] ADD [TwoWeekNoticeSentForPeriodEnd] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175656_AddCasePauseAndLapseNotices'
)
BEGIN
    ALTER TABLE [Cases] ADD [StatusBeforePause] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822175656_AddCasePauseAndLapseNotices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822175656_AddCasePauseAndLapseNotices', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822180817_AddClientProposedTransfers'
)
BEGIN
    ALTER TABLE [CaseTransferLogs] ADD [ProposedByClient] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822180817_AddClientProposedTransfers'
)
BEGIN
    ALTER TABLE [CaseTransferLogs] ADD [ShareHistory] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822180817_AddClientProposedTransfers'
)
BEGIN
    ALTER TABLE [CaseTransferLogs] ADD [ShareInvestigations] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822180817_AddClientProposedTransfers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822180817_AddClientProposedTransfers', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE TABLE [EventEvidenceSubmissions] (
        [Id] uniqueidentifier NOT NULL,
        [OrgCalendarEventId] uniqueidentifier NOT NULL,
        [SubmittedByAppUserId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [Note] nvarchar(2000) NULL,
        [Status] int NOT NULL,
        [ReviewedByAppUserId] uniqueidentifier NULL,
        [DateReviewed] datetime2 NULL,
        [RejectionReason] nvarchar(1000) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_EventEvidenceSubmissions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EventEvidenceSubmissions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventEvidenceSubmissions_AppUsers_ReviewedByAppUserId] FOREIGN KEY ([ReviewedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventEvidenceSubmissions_AppUsers_SubmittedByAppUserId] FOREIGN KEY ([SubmittedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventEvidenceSubmissions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_EventEvidenceSubmissions_OrgCalendarEvents_OrgCalendarEventId] FOREIGN KEY ([OrgCalendarEventId]) REFERENCES [OrgCalendarEvents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EventEvidenceSubmissions_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_CreatedByAppUserId] ON [EventEvidenceSubmissions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_OrgCalendarEventId_Status] ON [EventEvidenceSubmissions] ([OrgCalendarEventId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_ReviewedByAppUserId] ON [EventEvidenceSubmissions] ([ReviewedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_SubmittedByAppUserId] ON [EventEvidenceSubmissions] ([SubmittedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_UpdatedByAppUserId] ON [EventEvidenceSubmissions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    CREATE INDEX [IX_EventEvidenceSubmissions_UploadFileId] ON [EventEvidenceSubmissions] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822184026_AddEventEvidenceSubmissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822184026_AddEventEvidenceSubmissions', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    ALTER TABLE [OrganizationUserMemberships] ADD [MemberLevelId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    CREATE TABLE [OrganizationMemberLevels] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationMemberLevels] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationMemberLevels_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMemberLevels_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMemberLevels_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    CREATE INDEX [IX_OrganizationUserMemberships_MemberLevelId] ON [OrganizationUserMemberships] ([MemberLevelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    CREATE INDEX [IX_OrganizationMemberLevels_CreatedByAppUserId] ON [OrganizationMemberLevels] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    CREATE INDEX [IX_OrganizationMemberLevels_OrganizationId_SortOrder] ON [OrganizationMemberLevels] ([OrganizationId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    CREATE INDEX [IX_OrganizationMemberLevels_UpdatedByAppUserId] ON [OrganizationMemberLevels] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    ALTER TABLE [OrganizationUserMemberships] ADD CONSTRAINT [FK_OrganizationUserMemberships_OrganizationMemberLevels_MemberLevelId] FOREIGN KEY ([MemberLevelId]) REFERENCES [OrganizationMemberLevels] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823122540_AddOrganizationMemberLevels'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823122540_AddOrganizationMemberLevels', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE TABLE [CaseContacts] (
        [Id] uniqueidentifier NOT NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_CaseContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseContacts_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseContacts_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseContacts_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_CaseContacts_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE TABLE [InvestigationDuties] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsSingleHolder] bit NOT NULL,
        [MinimumMemberLevelId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_InvestigationDuties] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvestigationDuties_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationDuties_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationDuties_OrganizationMemberLevels_MinimumMemberLevelId] FOREIGN KEY ([MinimumMemberLevelId]) REFERENCES [OrganizationMemberLevels] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_InvestigationDuties_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE TABLE [InvestigationDutyAssignments] (
        [Id] uniqueidentifier NOT NULL,
        [InvestigationAttendeeId] uniqueidentifier NOT NULL,
        [InvestigationDutyId] uniqueidentifier NOT NULL,
        [EligibilityOverridden] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_InvestigationDutyAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvestigationDutyAssignments_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationDutyAssignments_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_InvestigationDutyAssignments_InvestigationAttendees_InvestigationAttendeeId] FOREIGN KEY ([InvestigationAttendeeId]) REFERENCES [InvestigationAttendees] ([Id]),
        CONSTRAINT [FK_InvestigationDutyAssignments_InvestigationDuties_InvestigationDutyId] FOREIGN KEY ([InvestigationDutyId]) REFERENCES [InvestigationDuties] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_CaseContacts_AppUserId] ON [CaseContacts] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseContacts_CaseId_AppUserId] ON [CaseContacts] ([CaseId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_CaseContacts_CreatedByAppUserId] ON [CaseContacts] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_CaseContacts_UpdatedByAppUserId] ON [CaseContacts] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDuties_CreatedByAppUserId] ON [InvestigationDuties] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDuties_MinimumMemberLevelId] ON [InvestigationDuties] ([MinimumMemberLevelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDuties_OrganizationId_SortOrder] ON [InvestigationDuties] ([OrganizationId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDuties_UpdatedByAppUserId] ON [InvestigationDuties] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDutyAssignments_CreatedByAppUserId] ON [InvestigationDutyAssignments] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InvestigationDutyAssignments_InvestigationAttendeeId_InvestigationDutyId] ON [InvestigationDutyAssignments] ([InvestigationAttendeeId], [InvestigationDutyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDutyAssignments_InvestigationDutyId] ON [InvestigationDutyAssignments] ([InvestigationDutyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    CREATE INDEX [IX_InvestigationDutyAssignments_UpdatedByAppUserId] ON [InvestigationDutyAssignments] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823132348_AddInvestigationDutiesAndCaseContacts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823132348_AddInvestigationDutiesAndCaseContacts', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823152748_AddSubscriptionTierPermissionAreas'
)
BEGIN
    CREATE TABLE [SubscriptionTierPermissionAreas] (
        [Id] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [Area] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionTierPermissionAreas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionTierPermissionAreas_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierPermissionAreas_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierPermissionAreas_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823152748_AddSubscriptionTierPermissionAreas'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierPermissionAreas_CreatedByAppUserId] ON [SubscriptionTierPermissionAreas] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823152748_AddSubscriptionTierPermissionAreas'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionTierPermissionAreas_SubscriptionTierId_Area] ON [SubscriptionTierPermissionAreas] ([SubscriptionTierId], [Area]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823152748_AddSubscriptionTierPermissionAreas'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierPermissionAreas_UpdatedByAppUserId] ON [SubscriptionTierPermissionAreas] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823152748_AddSubscriptionTierPermissionAreas'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823152748_AddSubscriptionTierPermissionAreas', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823181722_AddSubscriptionTierExcludedCapabilities'
)
BEGIN
    CREATE TABLE [SubscriptionTierExcludedCapabilities] (
        [Id] uniqueidentifier NOT NULL,
        [SubscriptionTierId] uniqueidentifier NOT NULL,
        [Capability] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_SubscriptionTierExcludedCapabilities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubscriptionTierExcludedCapabilities_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierExcludedCapabilities_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_SubscriptionTierExcludedCapabilities_SubscriptionTiers_SubscriptionTierId] FOREIGN KEY ([SubscriptionTierId]) REFERENCES [SubscriptionTiers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823181722_AddSubscriptionTierExcludedCapabilities'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierExcludedCapabilities_CreatedByAppUserId] ON [SubscriptionTierExcludedCapabilities] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823181722_AddSubscriptionTierExcludedCapabilities'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionTierExcludedCapabilities_SubscriptionTierId_Capability] ON [SubscriptionTierExcludedCapabilities] ([SubscriptionTierId], [Capability]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823181722_AddSubscriptionTierExcludedCapabilities'
)
BEGIN
    CREATE INDEX [IX_SubscriptionTierExcludedCapabilities_UpdatedByAppUserId] ON [SubscriptionTierExcludedCapabilities] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823181722_AddSubscriptionTierExcludedCapabilities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823181722_AddSubscriptionTierExcludedCapabilities', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183804_AddUserTourStates'
)
BEGIN
    CREATE TABLE [UserTourStates] (
        [Id] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [TourName] nvarchar(64) NOT NULL,
        [Completed] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_UserTourStates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserTourStates_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserTourStates_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserTourStates_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183804_AddUserTourStates'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserTourStates_AppUserId_TourName] ON [UserTourStates] ([AppUserId], [TourName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183804_AddUserTourStates'
)
BEGIN
    CREATE INDEX [IX_UserTourStates_CreatedByAppUserId] ON [UserTourStates] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183804_AddUserTourStates'
)
BEGIN
    CREATE INDEX [IX_UserTourStates_UpdatedByAppUserId] ON [UserTourStates] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823183804_AddUserTourStates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823183804_AddUserTourStates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823185009_AddAppUserDateOnboarded'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [DateOnboarded] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823185009_AddAppUserDateOnboarded'
)
BEGIN
    UPDATE AppUsers SET DateOnboarded = GETUTCDATE() WHERE DateOnboarded IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823185009_AddAppUserDateOnboarded'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823185009_AddAppUserDateOnboarded', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE TABLE [OrganizationAds] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [Headline] nvarchar(80) NOT NULL,
        [Body] nvarchar(300) NOT NULL,
        [ImageUploadFileId] uniqueidentifier NULL,
        [TargetKind] nvarchar(16) NOT NULL,
        [Status] int NOT NULL,
        [RejectionReason] nvarchar(max) NULL,
        [DateSubmitted] datetime2 NULL,
        [DateReviewed] datetime2 NULL,
        [ReviewedByAppUserId] uniqueidentifier NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_OrganizationAds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationAds_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAds_AppUsers_ReviewedByAppUserId] FOREIGN KEY ([ReviewedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAds_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationAds_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationAds_UploadFiles_ImageUploadFileId] FOREIGN KEY ([ImageUploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE INDEX [IX_OrganizationAds_CreatedByAppUserId] ON [OrganizationAds] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE INDEX [IX_OrganizationAds_ImageUploadFileId] ON [OrganizationAds] ([ImageUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE INDEX [IX_OrganizationAds_OrganizationId] ON [OrganizationAds] ([OrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE INDEX [IX_OrganizationAds_ReviewedByAppUserId] ON [OrganizationAds] ([ReviewedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    CREATE INDEX [IX_OrganizationAds_UpdatedByAppUserId] ON [OrganizationAds] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823194643_AddOrganizationAds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823194643_AddOrganizationAds', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    ALTER TABLE [Coupons] ADD [ReferrerAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE TABLE [BillingLedgerEntries] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] int NOT NULL,
        [OrganizationId] uniqueidentifier NULL,
        [ReferrerAppUserId] uniqueidentifier NULL,
        [Amount] decimal(18,2) NOT NULL,
        [AdjustmentIsCredit] bit NOT NULL,
        [TaxRatePercent] decimal(5,2) NOT NULL,
        [TaxAmount] decimal(18,2) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [PaymentReference] nvarchar(128) NULL,
        [ReceiptNumber] int NULL,
        [PeriodStart] datetime2 NULL,
        [PeriodEnd] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_BillingLedgerEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingLedgerEntries_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_BillingLedgerEntries_AppUsers_ReferrerAppUserId] FOREIGN KEY ([ReferrerAppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BillingLedgerEntries_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_BillingLedgerEntries_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE TABLE [TaxRateRules] (
        [Id] uniqueidentifier NOT NULL,
        [State] nvarchar(2) NOT NULL,
        [RatePercent] decimal(5,2) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_TaxRateRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaxRateRules_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_TaxRateRules_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_Coupons_ReferrerAppUserId] ON [Coupons] ([ReferrerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_BillingLedgerEntries_CreatedByAppUserId] ON [BillingLedgerEntries] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_BillingLedgerEntries_OrganizationId_DateCreated] ON [BillingLedgerEntries] ([OrganizationId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BillingLedgerEntries_ReceiptNumber] ON [BillingLedgerEntries] ([ReceiptNumber]) WHERE [ReceiptNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_BillingLedgerEntries_ReferrerAppUserId] ON [BillingLedgerEntries] ([ReferrerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_BillingLedgerEntries_UpdatedByAppUserId] ON [BillingLedgerEntries] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_TaxRateRules_CreatedByAppUserId] ON [TaxRateRules] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TaxRateRules_State] ON [TaxRateRules] ([State]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    CREATE INDEX [IX_TaxRateRules_UpdatedByAppUserId] ON [TaxRateRules] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    ALTER TABLE [Coupons] ADD CONSTRAINT [FK_Coupons_AppUsers_ReferrerAppUserId] FOREIGN KEY ([ReferrerAppUserId]) REFERENCES [AppUsers] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824012710_AddBillingLedgerAndTax'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824012710_AddBillingLedgerAndTax', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    ALTER TABLE [SubscriptionTierPrices] ADD [PricePerExtraMember] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    ALTER TABLE [Coupons] ADD [ReferralCommissionPercent] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    CREATE TABLE [MemberSeatSubscriptions] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [AppUserId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [Interval] int NOT NULL,
        [PriceAtStart] decimal(18,2) NOT NULL,
        [CurrentPeriodStart] datetime2 NULL,
        [CurrentPeriodEnd] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_MemberSeatSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MemberSeatSubscriptions_AppUsers_AppUserId] FOREIGN KEY ([AppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_MemberSeatSubscriptions_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_MemberSeatSubscriptions_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_MemberSeatSubscriptions_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    CREATE INDEX [IX_MemberSeatSubscriptions_AppUserId] ON [MemberSeatSubscriptions] ([AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    CREATE INDEX [IX_MemberSeatSubscriptions_CreatedByAppUserId] ON [MemberSeatSubscriptions] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MemberSeatSubscriptions_OrganizationId_AppUserId] ON [MemberSeatSubscriptions] ([OrganizationId], [AppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    CREATE INDEX [IX_MemberSeatSubscriptions_UpdatedByAppUserId] ON [MemberSeatSubscriptions] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824104620_AddOverflowSeatsAndReferralCommission'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824104620_AddOverflowSeatsAndReferralCommission', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824120701_AddInheritedMetadataSource'
)
BEGIN
    ALTER TABLE [UploadFileMetadata] ADD [InheritedFromUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824120701_AddInheritedMetadataSource'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824120701_AddInheritedMetadataSource', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824125434_AddOrgMediaStrippingSetting'
)
BEGIN
    ALTER TABLE [Organizations] ADD [StripMediaMetadata] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824125434_AddOrgMediaStrippingSetting'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824125434_AddOrgMediaStrippingSetting', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824135542_AddPrivateEngagementCases'
)
BEGIN
    ALTER TABLE [Cases] ADD [IsPrivateEngagement] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824135542_AddPrivateEngagementCases'
)
BEGIN
    ALTER TABLE [Cases] ADD [WasPublicBeforeLapse] bit NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824135542_AddPrivateEngagementCases'
)
BEGIN
    ALTER TABLE [CaseRelatedPeople] ADD [PublicLabel] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824135542_AddPrivateEngagementCases'
)
BEGIN
    UPDATE Cases SET IsPrivateEngagement = 1 WHERE ClientRequestId IS NOT NULL;

    UPDATE c SET c.IsPrivateEngagement = 1
    FROM Cases c JOIN Places p ON p.Id = c.PlaceId
    WHERE p.Kind = 1;

    UPDATE c SET c.IsPrivateEngagement = 1
    FROM Cases c
    WHERE EXISTS (
        SELECT 1 FROM Investigations i
        JOIN Places p ON p.Id = i.PlaceId
        WHERE i.CaseId = c.Id AND p.Kind = 1);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824135542_AddPrivateEngagementCases'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824135542_AddPrivateEngagementCases', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824145932_AddLapseUnpublishMemory'
)
BEGIN
    ALTER TABLE [OrganizationSubscriptions] ADD [StrandedClientNoticeSentAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824145932_AddLapseUnpublishMemory'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824145932_AddLapseUnpublishMemory', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824163140_AddFeedLikes'
)
BEGIN
    CREATE TABLE [OrgMessageLikes] (
        [OrgMessageId] uniqueidentifier NOT NULL,
        [LikerAppUserId] uniqueidentifier NOT NULL,
        [DateLiked] datetime2 NOT NULL,
        CONSTRAINT [PK_OrgMessageLikes] PRIMARY KEY ([OrgMessageId], [LikerAppUserId]),
        CONSTRAINT [FK_OrgMessageLikes_AppUsers_LikerAppUserId] FOREIGN KEY ([LikerAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrgMessageLikes_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824163140_AddFeedLikes'
)
BEGIN
    CREATE INDEX [IX_OrgMessageLikes_LikerAppUserId] ON [OrgMessageLikes] ([LikerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824163140_AddFeedLikes'
)
BEGIN
    CREATE INDEX [IX_OrgMessageLikes_OrgMessageId_DateLiked] ON [OrgMessageLikes] ([OrgMessageId], [DateLiked]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824163140_AddFeedLikes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824163140_AddFeedLikes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaReviewState] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaUploadFileId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_MediaReviewState_DateCreated] ON [OrgMessages] ([MediaReviewState], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_MediaUploadFileId] ON [OrgMessages] ([MediaUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD CONSTRAINT [FK_OrgMessages_UploadFiles_MediaUploadFileId] FOREIGN KEY ([MediaUploadFileId]) REFERENCES [UploadFiles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824165447_AddFeedPostMedia'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824165447_AddFeedPostMedia', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824172739_AddFeedMediaReviewAudit'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaReviewNote] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824172739_AddFeedMediaReviewAudit'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaReviewedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824172739_AddFeedMediaReviewAudit'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaReviewedUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824172739_AddFeedMediaReviewAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824172739_AddFeedMediaReviewAudit', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [CategoryMatchScore] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [FeedExperienceTypeId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE TABLE [FeedLabelledExamples] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NULL,
        [ExperienceTypeId] uniqueidentifier NOT NULL,
        [Label] int NOT NULL,
        [Source] int NOT NULL,
        [FeaturesJson] nvarchar(max) NULL,
        [DecidedByAppUserId] uniqueidentifier NOT NULL,
        [DecidedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FeedLabelledExamples] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeedLabelledExamples_AppUsers_DecidedByAppUserId] FOREIGN KEY ([DecidedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FeedLabelledExamples_ExperienceTypes_ExperienceTypeId] FOREIGN KEY ([ExperienceTypeId]) REFERENCES [ExperienceTypes] ([Id]),
        CONSTRAINT [FK_FeedLabelledExamples_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE TABLE [FeedMediaFeatureSets] (
        [OrgMessageId] uniqueidentifier NOT NULL,
        [IsVideo] bit NOT NULL,
        [DurationSeconds] float NULL,
        [HasAudio] bit NULL,
        [WidthPixels] int NULL,
        [HeightPixels] int NULL,
        [MeanLuma] float NULL,
        [LumaStdDev] float NULL,
        [CapturedHourLocal] int NULL,
        [CameraManufacturer] nvarchar(128) NULL,
        [AudioAnomalyScore] float NULL,
        [EvpHitCount] int NULL,
        [MotionScore] float NULL,
        [ExtraJson] nvarchar(max) NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_FeedMediaFeatureSets] PRIMARY KEY ([OrgMessageId]),
        CONSTRAINT [FK_FeedMediaFeatureSets_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE TABLE [FeedTypeWeightSets] (
        [Id] uniqueidentifier NOT NULL,
        [ExperienceTypeId] uniqueidentifier NOT NULL,
        [FitVersion] int NOT NULL,
        [FitUtc] datetime2 NOT NULL,
        [ExampleCount] int NOT NULL,
        [WeightsJson] nvarchar(max) NOT NULL,
        [HoldoutAccuracy] float NULL,
        CONSTRAINT [PK_FeedTypeWeightSets] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeedTypeWeightSets_ExperienceTypes_ExperienceTypeId] FOREIGN KEY ([ExperienceTypeId]) REFERENCES [ExperienceTypes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_FeedExperienceTypeId_DateCreated] ON [OrgMessages] ([FeedExperienceTypeId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE INDEX [IX_FeedLabelledExamples_DecidedByAppUserId] ON [FeedLabelledExamples] ([DecidedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE INDEX [IX_FeedLabelledExamples_ExperienceTypeId_DecidedUtc] ON [FeedLabelledExamples] ([ExperienceTypeId], [DecidedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE INDEX [IX_FeedLabelledExamples_OrgMessageId] ON [FeedLabelledExamples] ([OrgMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FeedTypeWeightSets_ExperienceTypeId_FitVersion] ON [FeedTypeWeightSets] ([ExperienceTypeId], [FitVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD CONSTRAINT [FK_OrgMessages_ExperienceTypes_FeedExperienceTypeId] FOREIGN KEY ([FeedExperienceTypeId]) REFERENCES [ExperienceTypes] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824182654_AddFeedCategoriesAndLearning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824182654_AddFeedCategoriesAndLearning', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [AttributedOrganizationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [AttributionDecidedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [AttributionDecidedUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [AttributionState] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    CREATE TABLE [FeedPostConsents] (
        [Id] uniqueidentifier NOT NULL,
        [OrgMessageId] uniqueidentifier NULL,
        [CaseId] uniqueidentifier NOT NULL,
        [AgreedByAppUserId] uniqueidentifier NOT NULL,
        [AgreedUtc] datetime2 NOT NULL,
        [WordingVersion] int NOT NULL,
        CONSTRAINT [PK_FeedPostConsents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FeedPostConsents_AppUsers_AgreedByAppUserId] FOREIGN KEY ([AgreedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FeedPostConsents_Cases_CaseId] FOREIGN KEY ([CaseId]) REFERENCES [Cases] ([Id]),
        CONSTRAINT [FK_FeedPostConsents_OrgMessages_OrgMessageId] FOREIGN KEY ([OrgMessageId]) REFERENCES [OrgMessages] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    CREATE INDEX [IX_OrgMessages_AttributedOrganizationId_AttributionState_DateCreated] ON [OrgMessages] ([AttributedOrganizationId], [AttributionState], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    CREATE INDEX [IX_FeedPostConsents_AgreedByAppUserId] ON [FeedPostConsents] ([AgreedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    CREATE INDEX [IX_FeedPostConsents_CaseId_AgreedUtc] ON [FeedPostConsents] ([CaseId], [AgreedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    CREATE INDEX [IX_FeedPostConsents_OrgMessageId] ON [FeedPostConsents] ([OrgMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD CONSTRAINT [FK_OrgMessages_Organizations_AttributedOrganizationId] FOREIGN KEY ([AttributedOrganizationId]) REFERENCES [Organizations] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824184811_AddFeedAttributionAndConsent'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824184811_AddFeedAttributionAndConsent', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824190644_AddOrganizationAdCounters'
)
BEGIN
    ALTER TABLE [OrganizationAds] ADD [Clicks] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824190644_AddOrganizationAdCounters'
)
BEGIN
    ALTER TABLE [OrganizationAds] ADD [Impressions] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824190644_AddOrganizationAdCounters'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824190644_AddOrganizationAdCounters', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824202454_AddOrganizationKindAndTours'
)
BEGIN
    ALTER TABLE [Organizations] ADD [Kind] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824202454_AddOrganizationKindAndTours'
)
BEGIN
    ALTER TABLE [Organizations] ADD [RunsPublicTours] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824202454_AddOrganizationKindAndTours'
)
BEGIN
    CREATE INDEX [IX_Organizations_RunsPublicTours] ON [Organizations] ([RunsPublicTours]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824202454_AddOrganizationKindAndTours'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824202454_AddOrganizationKindAndTours', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE TABLE [FieldSessionUploads] (
        [Id] uniqueidentifier NOT NULL,
        [InvestigationId] uniqueidentifier NULL,
        [SubmittedByAppUserId] uniqueidentifier NOT NULL,
        [RecordedByAppUserId] uniqueidentifier NULL,
        [RecordedByName] nvarchar(200) NULL,
        [DeviceSessionId] uniqueidentifier NOT NULL,
        [DocumentUploadFileId] uniqueidentifier NOT NULL,
        [DeviceModel] nvarchar(100) NOT NULL,
        [LocationLabel] nvarchar(400) NULL,
        [StartedAt] datetime2 NOT NULL,
        [EndedAt] datetime2 NULL,
        [ReadingCount] int NOT NULL,
        [MarkerCount] int NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_FieldSessionUploads] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FieldSessionUploads_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploads_AppUsers_RecordedByAppUserId] FOREIGN KEY ([RecordedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploads_AppUsers_SubmittedByAppUserId] FOREIGN KEY ([SubmittedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploads_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploads_Investigations_InvestigationId] FOREIGN KEY ([InvestigationId]) REFERENCES [Investigations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FieldSessionUploads_UploadFiles_DocumentUploadFileId] FOREIGN KEY ([DocumentUploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE TABLE [FieldSessionUploadFiles] (
        [Id] uniqueidentifier NOT NULL,
        [FieldSessionUploadId] uniqueidentifier NOT NULL,
        [UploadFileId] uniqueidentifier NOT NULL,
        [RelativePath] nvarchar(500) NOT NULL,
        [Sha256] nvarchar(64) NULL,
        [DigestMatched] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_FieldSessionUploadFiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FieldSessionUploadFiles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploadFiles_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionUploadFiles_FieldSessionUploads_FieldSessionUploadId] FOREIGN KEY ([FieldSessionUploadId]) REFERENCES [FieldSessionUploads] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FieldSessionUploadFiles_UploadFiles_UploadFileId] FOREIGN KEY ([UploadFileId]) REFERENCES [UploadFiles] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploadFiles_CreatedByAppUserId] ON [FieldSessionUploadFiles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FieldSessionUploadFiles_FieldSessionUploadId_RelativePath] ON [FieldSessionUploadFiles] ([FieldSessionUploadId], [RelativePath]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploadFiles_UpdatedByAppUserId] ON [FieldSessionUploadFiles] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploadFiles_UploadFileId] ON [FieldSessionUploadFiles] ([UploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_CreatedByAppUserId] ON [FieldSessionUploads] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_DocumentUploadFileId] ON [FieldSessionUploads] ([DocumentUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_InvestigationId_StartedAt] ON [FieldSessionUploads] ([InvestigationId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_RecordedByAppUserId] ON [FieldSessionUploads] ([RecordedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FieldSessionUploads_SubmittedByAppUserId_DeviceSessionId] ON [FieldSessionUploads] ([SubmittedByAppUserId], [DeviceSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_SubmittedByAppUserId_StartedAt] ON [FieldSessionUploads] ([SubmittedByAppUserId], [StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_UpdatedByAppUserId] ON [FieldSessionUploads] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825155328_AddFieldSessionUploads'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825155328_AddFieldSessionUploads', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825172652_AddCaseReportFieldSessionCitations'
)
BEGIN
    CREATE TABLE [CaseReportSectionFieldSessions] (
        [Id] uniqueidentifier NOT NULL,
        [CaseReportSectionId] uniqueidentifier NOT NULL,
        [FieldSessionUploadId] uniqueidentifier NOT NULL,
        [Caption] nvarchar(500) NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_CaseReportSectionFieldSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CaseReportSectionFieldSessions_CaseReportSections_CaseReportSectionId] FOREIGN KEY ([CaseReportSectionId]) REFERENCES [CaseReportSections] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CaseReportSectionFieldSessions_FieldSessionUploads_FieldSessionUploadId] FOREIGN KEY ([FieldSessionUploadId]) REFERENCES [FieldSessionUploads] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825172652_AddCaseReportFieldSessionCitations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CaseReportSectionFieldSessions_CaseReportSectionId_FieldSessionUploadId] ON [CaseReportSectionFieldSessions] ([CaseReportSectionId], [FieldSessionUploadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825172652_AddCaseReportFieldSessionCitations'
)
BEGIN
    CREATE INDEX [IX_CaseReportSectionFieldSessions_FieldSessionUploadId] ON [CaseReportSectionFieldSessions] ([FieldSessionUploadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825172652_AddCaseReportFieldSessionCitations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825172652_AddCaseReportFieldSessionCitations', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826150041_RevokeGrandfatheredInvestigatorRole'
)
BEGIN

                    DELETE FROM [OrganizationRoleMemberships]
                    WHERE [OrganizationRoleId] IN (
                        SELECT [Id] FROM [OrganizationRoles]
                        WHERE [Name] = N'Investigator Role'
                          AND [Description] = N'Reads the group''s cases and investigations. Assigned to everyone who was already a member when role-based case access arrived, so the change took nothing from anyone; hand it to new members as they earn it.'
                    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826150041_RevokeGrandfatheredInvestigatorRole'
)
BEGIN

                    UPDATE [OrganizationRoles]
                    SET [Description] = N'Reads the group''s cases and investigations. Assign it to the members who should see them.'
                    WHERE [Name] = N'Investigator Role'
                      AND [Description] = N'Reads the group''s cases and investigations. Assigned to everyone who was already a member when role-based case access arrived, so the change took nothing from anyone; hand it to new members as they earn it.';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826150041_RevokeGrandfatheredInvestigatorRole'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826150041_RevokeGrandfatheredInvestigatorRole', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826181006_AddMemberLevelSuggestedRoles'
)
BEGIN
    CREATE TABLE [OrganizationMemberLevelRoles] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationMemberLevelId] uniqueidentifier NOT NULL,
        [OrganizationRoleId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_OrganizationMemberLevelRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrganizationMemberLevelRoles_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_OrganizationMemberLevelRoles_OrganizationMemberLevels_OrganizationMemberLevelId] FOREIGN KEY ([OrganizationMemberLevelId]) REFERENCES [OrganizationMemberLevels] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrganizationMemberLevelRoles_OrganizationRoles_OrganizationRoleId] FOREIGN KEY ([OrganizationRoleId]) REFERENCES [OrganizationRoles] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826181006_AddMemberLevelSuggestedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMemberLevelRoles_CreatedByAppUserId] ON [OrganizationMemberLevelRoles] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826181006_AddMemberLevelSuggestedRoles'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationMemberLevelRoles_OrganizationMemberLevelId_OrganizationRoleId] ON [OrganizationMemberLevelRoles] ([OrganizationMemberLevelId], [OrganizationRoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826181006_AddMemberLevelSuggestedRoles'
)
BEGIN
    CREATE INDEX [IX_OrganizationMemberLevelRoles_OrganizationRoleId] ON [OrganizationMemberLevelRoles] ([OrganizationRoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826181006_AddMemberLevelSuggestedRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826181006_AddMemberLevelSuggestedRoles', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826193008_AddClientRequestReviewVotes'
)
BEGIN
    CREATE TABLE [ClientRequestReviewVotes] (
        [Id] uniqueidentifier NOT NULL,
        [ClientRequestOrganizationId] uniqueidentifier NOT NULL,
        [VoterAppUserId] uniqueidentifier NOT NULL,
        [InFavor] bit NOT NULL,
        [Comment] nvarchar(1000) NULL,
        [DateVoted] datetime2 NOT NULL,
        CONSTRAINT [PK_ClientRequestReviewVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ClientRequestReviewVotes_AppUsers_VoterAppUserId] FOREIGN KEY ([VoterAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_ClientRequestReviewVotes_ClientRequestOrganizations_ClientRequestOrganizationId] FOREIGN KEY ([ClientRequestOrganizationId]) REFERENCES [ClientRequestOrganizations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826193008_AddClientRequestReviewVotes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_ClientRequestOrganizations_OneAcceptedPerRequest] ON [ClientRequestOrganizations] ([ClientRequestId]) WHERE [Status] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826193008_AddClientRequestReviewVotes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClientRequestReviewVotes_ClientRequestOrganizationId_VoterAppUserId] ON [ClientRequestReviewVotes] ([ClientRequestOrganizationId], [VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826193008_AddClientRequestReviewVotes'
)
BEGIN
    CREATE INDEX [IX_ClientRequestReviewVotes_VoterAppUserId] ON [ClientRequestReviewVotes] ([VoterAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826193008_AddClientRequestReviewVotes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826193008_AddClientRequestReviewVotes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827034513_AddUnbandedBusinessTiers'
)
BEGIN
    ALTER TABLE [SubscriptionTiers] ADD [IsBandedByMembers] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827034513_AddUnbandedBusinessTiers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827034513_AddUnbandedBusinessTiers', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827041613_AddRateLimitRefusalTracking'
)
BEGIN
    CREATE TABLE [RateLimitRefusals] (
        [Id] uniqueidentifier NOT NULL,
        [PolicyName] nvarchar(64) NOT NULL,
        [Refusals] bigint NOT NULL,
        [DistinctCallers] int NOT NULL,
        [PeakDistinctCallers] int NOT NULL,
        [DateFirstSeen] datetime2 NOT NULL,
        [DateLastSeen] datetime2 NOT NULL,
        [DateNotified] datetime2 NULL,
        CONSTRAINT [PK_RateLimitRefusals] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827041613_AddRateLimitRefusalTracking'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RateLimitRefusals_PolicyName] ON [RateLimitRefusals] ([PolicyName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827041613_AddRateLimitRefusalTracking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827041613_AddRateLimitRefusalTracking', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827051110_AddOrganiserSentAttendanceInvites'
)
BEGIN
    ALTER TABLE [EventAttendanceInvites] ADD [InvitedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827051110_AddOrganiserSentAttendanceInvites'
)
BEGIN
    CREATE INDEX [IX_EventAttendanceInvites_InvitedByAppUserId] ON [EventAttendanceInvites] ([InvitedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827051110_AddOrganiserSentAttendanceInvites'
)
BEGIN
    ALTER TABLE [EventAttendanceInvites] ADD CONSTRAINT [FK_EventAttendanceInvites_AppUsers_InvitedByAppUserId] FOREIGN KEY ([InvitedByAppUserId]) REFERENCES [AppUsers] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827051110_AddOrganiserSentAttendanceInvites'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827051110_AddOrganiserSentAttendanceInvites', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    CREATE TABLE [PlaceRooms] (
        [Id] uniqueidentifier NOT NULL,
        [OrganizationId] uniqueidentifier NOT NULL,
        [PlaceId] uniqueidentifier NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Floor] nvarchar(60) NULL,
        [Description] nvarchar(1000) NULL,
        [IsPublic] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_PlaceRooms] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlaceRooms_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_PlaceRooms_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_PlaceRooms_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PlaceRooms_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    CREATE INDEX [IX_PlaceRooms_CreatedByAppUserId] ON [PlaceRooms] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlaceRooms_OrganizationId_PlaceId_Name] ON [PlaceRooms] ([OrganizationId], [PlaceId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    CREATE INDEX [IX_PlaceRooms_PlaceId] ON [PlaceRooms] ([PlaceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    CREATE INDEX [IX_PlaceRooms_UpdatedByAppUserId] ON [PlaceRooms] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260827184032_AddPlaceRooms'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260827184032_AddPlaceRooms', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828164309_AddAppUserDateClosed'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [DateClosed] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828164309_AddAppUserDateClosed'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260828164309_AddAppUserDateClosed', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828172652_AddUserBlocks'
)
BEGIN
    CREATE TABLE [UserBlocks] (
        [Id] uniqueidentifier NOT NULL,
        [BlockerAppUserId] uniqueidentifier NOT NULL,
        [BlockedAppUserId] uniqueidentifier NOT NULL,
        [DateCreated] datetime2 NOT NULL,
        CONSTRAINT [PK_UserBlocks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserBlocks_AppUsers_BlockedAppUserId] FOREIGN KEY ([BlockedAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_UserBlocks_AppUsers_BlockerAppUserId] FOREIGN KEY ([BlockerAppUserId]) REFERENCES [AppUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828172652_AddUserBlocks'
)
BEGIN
    CREATE INDEX [IX_UserBlocks_BlockedAppUserId] ON [UserBlocks] ([BlockedAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828172652_AddUserBlocks'
)
BEGIN
    CREATE INDEX [IX_UserBlocks_BlockerAppUserId] ON [UserBlocks] ([BlockerAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828172652_AddUserBlocks'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserBlocks_BlockerAppUserId_BlockedAppUserId] ON [UserBlocks] ([BlockerAppUserId], [BlockedAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260828172652_AddUserBlocks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260828172652_AddUserBlocks', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830181821_AddStripeProviderRefs'
)
BEGIN
    ALTER TABLE [OrganizationSubscriptions] ADD [ProviderCustomerRef] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830181821_AddStripeProviderRefs'
)
BEGIN
    ALTER TABLE [OrganizationSubscriptions] ADD [ProviderPaymentMethodRef] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830181821_AddStripeProviderRefs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830181821_AddStripeProviderRefs', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830185111_AddMemberSeatProviderRefs'
)
BEGIN
    ALTER TABLE [MemberSeatSubscriptions] ADD [ProviderCustomerRef] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830185111_AddMemberSeatProviderRefs'
)
BEGIN
    ALTER TABLE [MemberSeatSubscriptions] ADD [ProviderName] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830185111_AddMemberSeatProviderRefs'
)
BEGIN
    ALTER TABLE [MemberSeatSubscriptions] ADD [ProviderPaymentMethodRef] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830185111_AddMemberSeatProviderRefs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830185111_AddMemberSeatProviderRefs', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830200818_AddFieldSessionPlaceAndPublication'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [PlaceId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830200818_AddFieldSessionPlaceAndPublication'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [PublishedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830200818_AddFieldSessionPlaceAndPublication'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_PlaceId_PublishedAtUtc] ON [FieldSessionUploads] ([PlaceId], [PublishedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830200818_AddFieldSessionPlaceAndPublication'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD CONSTRAINT [FK_FieldSessionUploads_Places_PlaceId] FOREIGN KEY ([PlaceId]) REFERENCES [Places] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830200818_AddFieldSessionPlaceAndPublication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830200818_AddFieldSessionPlaceAndPublication', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [MediaReviewNote] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [MediaReviewState] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [MediaReviewedByAppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [MediaReviewedUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_MediaReviewState_PublishedAtUtc] ON [FieldSessionUploads] ([MediaReviewState], [PublishedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830202945_AddFieldSessionMediaReview'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830202945_AddFieldSessionMediaReview', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831141212_AddOrganizationIsPersonal'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsPersonal] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831141212_AddOrganizationIsPersonal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831141212_AddOrganizationIsPersonal', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831144302_AddEventEvidencePlacePublication'
)
BEGIN
    ALTER TABLE [EventEvidenceSubmissions] ADD [ArchiveReviewNote] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831144302_AddEventEvidencePlacePublication'
)
BEGIN
    ALTER TABLE [EventEvidenceSubmissions] ADD [ArchiveReviewState] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831144302_AddEventEvidencePlacePublication'
)
BEGIN
    ALTER TABLE [EventEvidenceSubmissions] ADD [PublishedToPlaceAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831144302_AddEventEvidencePlacePublication'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831144302_AddEventEvidencePlacePublication', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831170856_AddOrganizationIsUnlisted'
)
BEGIN
    ALTER TABLE [Organizations] ADD [IsUnlisted] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831170856_AddOrganizationIsUnlisted'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831170856_AddOrganizationIsUnlisted', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831212819_AddUserConfirmationTimestamps'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [DateConfirmationSent] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831212819_AddUserConfirmationTimestamps'
)
BEGIN
    ALTER TABLE [AppUsers] ADD [DateEmailConfirmed] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831212819_AddUserConfirmationTimestamps'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831212819_AddUserConfirmationTimestamps', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902202426_AddFieldSessionFirstFix'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [Latitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902202426_AddFieldSessionFirstFix'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [Longitude] decimal(18,10) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902202426_AddFieldSessionFirstFix'
)
BEGIN
    ALTER TABLE [FieldSessionUploads] ADD [PositionResolved] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902202426_AddFieldSessionFirstFix'
)
BEGIN
    CREATE INDEX [IX_FieldSessionUploads_SubmittedByAppUserId_PositionResolved_Latitude_Longitude] ON [FieldSessionUploads] ([SubmittedByAppUserId], [PositionResolved], [Latitude], [Longitude]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902202426_AddFieldSessionFirstFix'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902202426_AddFieldSessionFirstFix', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE TABLE [FieldSessionShareLinks] (
        [Id] uniqueidentifier NOT NULL,
        [Token] nvarchar(64) NOT NULL,
        [FieldSessionUploadId] uniqueidentifier NOT NULL,
        [FieldSessionUploadFileId] uniqueidentifier NULL,
        [CreatedByAppUserId] uniqueidentifier NOT NULL,
        [Note] nvarchar(200) NULL,
        [ExpiresUtc] datetime2 NOT NULL,
        [RevokedUtc] datetime2 NULL,
        [RevokedByAppUserId] uniqueidentifier NULL,
        [IncludePositions] bit NOT NULL,
        [ViewCount] int NOT NULL,
        [LastViewedUtc] datetime2 NULL,
        [DateCreated] datetime2 NOT NULL,
        [DateUpdated] datetime2 NULL,
        [UpdatedByAppUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_FieldSessionShareLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinks_AppUsers_CreatedByAppUserId] FOREIGN KEY ([CreatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinks_AppUsers_RevokedByAppUserId] FOREIGN KEY ([RevokedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinks_AppUsers_UpdatedByAppUserId] FOREIGN KEY ([UpdatedByAppUserId]) REFERENCES [AppUsers] ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinks_FieldSessionUploadFiles_FieldSessionUploadFileId] FOREIGN KEY ([FieldSessionUploadFileId]) REFERENCES [FieldSessionUploadFiles] ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinks_FieldSessionUploads_FieldSessionUploadId] FOREIGN KEY ([FieldSessionUploadId]) REFERENCES [FieldSessionUploads] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE TABLE [FieldSessionShareLinkViews] (
        [Id] uniqueidentifier NOT NULL,
        [FieldSessionShareLinkId] uniqueidentifier NOT NULL,
        [ViewedUtc] datetime2 NOT NULL,
        [ViewerHash] nvarchar(64) NULL,
        [UserAgent] nvarchar(300) NULL,
        [FieldSessionUploadFileId] uniqueidentifier NULL,
        CONSTRAINT [PK_FieldSessionShareLinkViews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FieldSessionShareLinkViews_FieldSessionShareLinks_FieldSessionShareLinkId] FOREIGN KEY ([FieldSessionShareLinkId]) REFERENCES [FieldSessionShareLinks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinks_CreatedByAppUserId] ON [FieldSessionShareLinks] ([CreatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinks_FieldSessionUploadFileId] ON [FieldSessionShareLinks] ([FieldSessionUploadFileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinks_FieldSessionUploadId_DateCreated] ON [FieldSessionShareLinks] ([FieldSessionUploadId], [DateCreated]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinks_RevokedByAppUserId] ON [FieldSessionShareLinks] ([RevokedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE UNIQUE INDEX [IX_FieldSessionShareLinks_Token] ON [FieldSessionShareLinks] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinks_UpdatedByAppUserId] ON [FieldSessionShareLinks] ([UpdatedByAppUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    CREATE INDEX [IX_FieldSessionShareLinkViews_FieldSessionShareLinkId_ViewedUtc] ON [FieldSessionShareLinkViews] ([FieldSessionShareLinkId], [ViewedUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904115028_AddFieldSessionShareLinks'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904115028_AddFieldSessionShareLinks', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904154444_AddOrgMessageScreenerScore'
)
BEGIN
    ALTER TABLE [OrgMessages] ADD [MediaScreenerScore] float NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904154444_AddOrgMessageScreenerScore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904154444_AddOrgMessageScreenerScore', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904155733_AddUploadFileOwnerOrganization'
)
BEGIN
    DECLARE @var26 nvarchar(max);
    SELECT @var26 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UploadFiles]') AND [c].[name] = N'AppUserId');
    IF @var26 IS NOT NULL EXEC(N'ALTER TABLE [UploadFiles] DROP CONSTRAINT ' + @var26 + ';');
    ALTER TABLE [UploadFiles] ALTER COLUMN [AppUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904155733_AddUploadFileOwnerOrganization'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD [OwnerOrganizationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904155733_AddUploadFileOwnerOrganization'
)
BEGIN
    CREATE INDEX [IX_UploadFiles_OwnerOrganizationId] ON [UploadFiles] ([OwnerOrganizationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904155733_AddUploadFileOwnerOrganization'
)
BEGIN
    ALTER TABLE [UploadFiles] ADD CONSTRAINT [FK_UploadFiles_Organizations_OwnerOrganizationId] FOREIGN KEY ([OwnerOrganizationId]) REFERENCES [Organizations] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260904155733_AddUploadFileOwnerOrganization'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260904155733_AddUploadFileOwnerOrganization', N'10.0.11');
END;

COMMIT;
GO

