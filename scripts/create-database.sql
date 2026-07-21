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
    VALUES (N'20260709155716_InitialCreate', N'10.0.9');
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
    VALUES (N'20260709163203_AddIdentitySchema', N'10.0.9');
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
    VALUES (N'20260711131156_AddGeocodingMetadataToAddresses', N'10.0.9');
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
    VALUES (N'20260711133326_AddOrganizationSecurityModel', N'10.0.9');
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
    VALUES (N'20260713150000_AddUploadFileEntities', N'10.0.9');
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
    VALUES (N'20260713151155_AddUploadFileSharing', N'10.0.9');
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
    VALUES (N'20260714130129_AddUploadFileTypeExtensions', N'10.0.9');
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
    VALUES (N'20260714154316_ReplaceIsOrganizationAdminWithRole', N'10.0.9');
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
    VALUES (N'20260714160800_AddAuditLogs', N'10.0.9');
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
    VALUES (N'20260714184021_ReplaceActionNameWithActionsBitmask', N'10.0.9');
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
    VALUES (N'20260718122428_AddCmsEntities', N'10.0.9');
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
    VALUES (N'20260718193225_AddUploadFileAudioConfig', N'10.0.9');
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
    VALUES (N'20260719142238_AddUploadFileRegionNotesAndParentClip', N'10.0.9');
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
    VALUES (N'20260719163758_AddUploadFileVotes', N'10.0.9');
END;

-- ── Migration 10: AddFileStoragePath (2026-07-21) ────────────────────────────
-- Adds StoragePath for filesystem-based file storage. FileData made nullable
-- (was already NULL in DB). Existing blobs migrated to disk by FileMigrationService
-- on next WebApi startup. FileData column will be dropped in a future migration
-- once all rows have StoragePath populated.

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
    VALUES (N'20260721133025_AddFileStoragePath', N'10.0.9');
END;

COMMIT;
GO

