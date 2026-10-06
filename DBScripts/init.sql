-- =============================================================================
-- init.sql  —  Inkwell Complete Database Schema
-- Version : 1.0.8-dev (2026-10-05d)
-- Target  : SQL Server 2019+ / Azure SQL
-- Usage   : Run once on a fresh database. Every block is idempotent (IF NOT
--           EXISTS) so it is safe to re-run against an existing database.
-- Note    : MigrationService.cs also applies this schema on startup, so a
--           manual run is optional for self-hosted installs.
-- =============================================================================

PRINT '==========================================================';
PRINT 'Inkwell — Full Schema Init (version: see the header of this file)';
PRINT 'Started: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT '==========================================================';

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Users
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Users')
BEGIN
    CREATE TABLE Users (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Uuid            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Username        NVARCHAR(100)    NULL,
        Email           NVARCHAR(255)    NOT NULL,
        PasswordHash    NVARCHAR(MAX)    NOT NULL,
        DisplayName     NVARCHAR(255)    NULL,
        Bio             NVARCHAR(500)    NULL,
        AvatarUrl       NVARCHAR(500)    NULL,
        Website         NVARCHAR(500)    NULL,
        Role            NVARCHAR(50)     NULL DEFAULT 'Member',
        IsActive        BIT              NOT NULL DEFAULT 1,
        CreatedAt       DATETIME         NULL,
        UpdatedAt       DATETIME2(7)     NOT NULL,
        Slug            NVARCHAR(255)    NULL,
        Status          NVARCHAR(50)     NULL,
        ProfileImage    NVARCHAR(1000)   NULL,
        CoverImage      NVARCHAR(1000)   NULL,
        Twitter         NVARCHAR(255)    NULL,
        Facebook        NVARCHAR(255)    NULL,
        LastLogin       DATETIME2(7)     NULL,
        MetaTitle       NVARCHAR(255)    NULL,
        MetaDescription NVARCHAR(MAX)    NULL,
        CreatedByUserId UNIQUEIDENTIFIER NULL,
        -- Professional author credentials (v1.0.1)
        Credentials     NVARCHAR(200)    NULL,
        Specialty       NVARCHAR(200)    NULL,
        LicenseNumber   NVARCHAR(100)    NULL,

        CONSTRAINT UQ_Users_Email UNIQUE (Email)
    );
    PRINT '  [+] Users table created.';
END
ELSE
BEGIN
    -- Ensure v1.0.1 columns exist on upgrade
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'Credentials')
        ALTER TABLE Users ADD Credentials NVARCHAR(200) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'Specialty')
        ALTER TABLE Users ADD Specialty NVARCHAR(200) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'LicenseNumber')
        ALTER TABLE Users ADD LicenseNumber NVARCHAR(100) NULL;
    PRINT '  [=] Users already exists — upgrade columns checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Categories
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Categories')
BEGIN
    CREATE TABLE Categories (
        Id       UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Name     NVARCHAR(255)    NOT NULL,
        Slug     NVARCHAR(255)    NOT NULL,
        AuthorId UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        Color    NVARCHAR(50)     NULL DEFAULT '#e8f5e9',

        CONSTRAINT UQ_Categories_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Categories table created.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Categories') AND name = N'Color')
        ALTER TABLE Categories ADD Color NVARCHAR(50) NULL DEFAULT '#e8f5e9' WITH VALUES;
    PRINT '  [=] Categories already exists — upgrade columns checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Tags
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Tags')
BEGIN
    CREATE TABLE Tags (
        Id       UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Name     NVARCHAR(255)    NOT NULL,
        Slug     NVARCHAR(255)    NOT NULL,
        AuthorId UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        Color    NVARCHAR(50)     NULL DEFAULT '#e3f2fd',

        CONSTRAINT UQ_Tags_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Tags table created.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Tags') AND name = N'Color')
        ALTER TABLE Tags ADD Color NVARCHAR(50) NULL DEFAULT '#e3f2fd' WITH VALUES;
    PRINT '  [=] Tags already exists — upgrade columns checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. Posts
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Posts')
BEGIN
    CREATE TABLE Posts (
        Id                   UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Uuid                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Title                NVARCHAR(255)    NOT NULL,
        Slug                 NVARCHAR(255)    NOT NULL,
        Type                 NVARCHAR(50)     NULL,
        Status               NVARCHAR(50)     NULL,
        Visibility           NVARCHAR(50)     NULL,
        Html                 NVARCHAR(MAX)    NULL,
        Plaintext            NVARCHAR(MAX)    NULL,
        FeatureImage         NVARCHAR(1000)   NULL,
        AllowComments        BIT              NOT NULL DEFAULT 1,
        ViewCount            INT              NULL,
        ScheduledAt          DATETIME2(7)     NULL,
        PublishedAt          DATETIME         NULL,
        CreatedAt            DATETIME         NULL,
        UpdatedAt            DATETIME         NULL,
        AuthorId             UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        MetaTitle            NVARCHAR(255)    NULL,
        MetaDescription      NVARCHAR(MAX)    NULL,
        CanonicalUrl         NVARCHAR(1000)   NULL,
        OgImage              NVARCHAR(1000)   NULL,
        OgTitle              NVARCHAR(255)    NULL,
        OgDescription        NVARCHAR(MAX)    NULL,
        TwitterImage         NVARCHAR(1000)   NULL,
        TwitterTitle         NVARCHAR(255)    NULL,
        TwitterDescription   NVARCHAR(MAX)    NULL,
        -- v1.0.1 columns
        FaqJson              NVARCHAR(MAX)    NULL,
        LastVerifiedAt       DATETIME2        NULL,
        NextReviewAt         DATETIME2        NULL,
        -- Verdict roundup posts
        RoundupJson          NVARCHAR(MAX)    NULL,

        CONSTRAINT UQ_Posts_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Posts table created.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'FaqJson')
        ALTER TABLE Posts ADD FaqJson NVARCHAR(MAX) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'LastVerifiedAt')
        ALTER TABLE Posts ADD LastVerifiedAt DATETIME2 NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'NextReviewAt')
        ALTER TABLE Posts ADD NextReviewAt DATETIME2 NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'RoundupJson')
        ALTER TABLE Posts ADD RoundupJson NVARCHAR(MAX) NULL;
    PRINT '  [=] Posts already exists — upgrade columns checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. Comments
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Comments')
BEGIN
    CREATE TABLE Comments (
        Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        PostId      UNIQUEIDENTIFIER NULL REFERENCES Posts(Id)    ON DELETE CASCADE,
        ParentId    UNIQUEIDENTIFIER NULL REFERENCES Comments(Id) ON DELETE NO ACTION,
        MemberId    UNIQUEIDENTIFIER NULL REFERENCES Users(Id)    ON DELETE NO ACTION,
        AuthorName  NVARCHAR(200)    NOT NULL,
        AuthorEmail NVARCHAR(200)    NOT NULL,
        AuthorUrl   NVARCHAR(500)    NULL,
        AuthorIp    NVARCHAR(50)     NULL,
        Content     NVARCHAR(MAX)    NOT NULL,
        Html        NVARCHAR(MAX)    NULL,
        Status      NVARCHAR(50)     NOT NULL,
        CreatedAt   DATETIME2(7)     NOT NULL
    );
    PRINT '  [+] Comments table created.';
END
ELSE
    PRINT '  [=] Comments already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 6. Media
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Media')
BEGIN
    CREATE TABLE Media (
        Id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        FileName         NVARCHAR(255)    NULL,
        OriginalFileName NVARCHAR(255)    NULL,
        FilePath         NVARCHAR(MAX)    NULL,
        Url              NVARCHAR(MAX)    NOT NULL,
        ContentType      NVARCHAR(100)    NULL,
        FileSize         BIGINT           NULL,
        Width            INT              NULL,
        Height           INT              NULL,
        AltText          NVARCHAR(MAX)    NULL,
        Caption          NVARCHAR(MAX)    NULL,
        UploadedBy       UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        CreatedAt        DATETIME         NULL
    );
    PRINT '  [+] Media table created.';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'Media') AND name = N'OriginalFileName')
        ALTER TABLE Media ADD OriginalFileName NVARCHAR(255) NULL;
    PRINT '  [=] Media already exists — upgrade columns checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 7. Pages
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Pages')
BEGIN
    CREATE TABLE Pages (
        Id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ParentId         UNIQUEIDENTIFIER NULL REFERENCES Pages(Id)  ON DELETE NO ACTION,
        AuthorId         UNIQUEIDENTIFIER NULL REFERENCES Users(Id)  ON DELETE SET NULL,
        FeaturedImageId  UNIQUEIDENTIFIER NULL REFERENCES Media(Id)  ON DELETE SET NULL,
        Title            NVARCHAR(255)    NOT NULL,
        Slug             NVARCHAR(255)    NOT NULL,
        Content          NVARCHAR(MAX)    NULL,
        IsInNav          BIT              NULL,
        SortOrder        INT              NULL,
        IsPublished      BIT              NOT NULL DEFAULT 0,
        PublishedAt      DATETIME2(7)     NULL,
        CreatedAt        DATETIME2(7)     NOT NULL,
        UpdatedAt        DATETIME2(7)     NOT NULL,
        MetaTitle        NVARCHAR(MAX)    NULL,
        MetaDescription  NVARCHAR(MAX)    NULL,
        CanonicalUrl     NVARCHAR(MAX)    NULL,
        OgImage          NVARCHAR(MAX)    NULL,
        OgTitle          NVARCHAR(MAX)    NULL,
        OgDescription    NVARCHAR(MAX)    NULL,
        TwitterImage     NVARCHAR(MAX)    NULL,
        TwitterTitle     NVARCHAR(MAX)    NULL,
        TwitterDescription NVARCHAR(MAX)  NULL,

        CONSTRAINT UQ_Pages_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Pages table created.';
END
ELSE
    PRINT '  [=] Pages already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 8. Settings
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Settings')
BEGIN
    CREATE TABLE Settings (
        Id          INT              IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId      UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE CASCADE,
        JsonPayload NVARCHAR(MAX)    NOT NULL,
        UpdatedAt   DATETIME2(7)    NOT NULL
    );
    PRINT '  [+] Settings table created.';
END
ELSE
    PRINT '  [=] Settings already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 9. CustomThemeSettings
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CustomThemeSettings')
BEGIN
    CREATE TABLE CustomThemeSettings (
        Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        UserId       UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
        SettingGroup NVARCHAR(100)    NOT NULL DEFAULT 'general',
        SettingKey   NVARCHAR(200)    NOT NULL,
        SettingType  NVARCHAR(50)     NOT NULL DEFAULT 'text',
        SettingValue NVARCHAR(MAX)    NULL,
        DefaultValue NVARCHAR(MAX)    NULL,
        Label        NVARCHAR(200)    NULL,
        Description  NVARCHAR(500)    NULL
    );
    PRINT '  [+] CustomThemeSettings table created.';
END
ELSE
    PRINT '  [=] CustomThemeSettings already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 10. Roles / Permissions / RBAC junction tables
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Roles')
BEGIN
    CREATE TABLE Roles (
        Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Uuid        UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Name        NVARCHAR(255)    NOT NULL,
        Description NVARCHAR(MAX)    NULL
    );
    PRINT '  [+] Roles table created.';
END
ELSE
    PRINT '  [=] Roles already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Permissions')
BEGIN
    CREATE TABLE Permissions (
        Id         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Uuid       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Name       NVARCHAR(255)    NOT NULL,
        ActionType NVARCHAR(50)     NOT NULL,
        ObjectType NVARCHAR(50)     NOT NULL
    );
    PRINT '  [+] Permissions table created.';
END
ELSE
    PRINT '  [=] Permissions already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PermissionsRoles')
BEGIN
    CREATE TABLE PermissionsRoles (
        PermissionId UNIQUEIDENTIFIER NOT NULL REFERENCES Permissions(Id) ON DELETE CASCADE,
        RoleId       UNIQUEIDENTIFIER NOT NULL REFERENCES Roles(Id)       ON DELETE CASCADE,
        CONSTRAINT PK_PermissionsRoles PRIMARY KEY (PermissionId, RoleId)
    );
    PRINT '  [+] PermissionsRoles table created.';
END
ELSE
    PRINT '  [=] PermissionsRoles already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'RolesUsers')
BEGIN
    CREATE TABLE RolesUsers (
        RoleId UNIQUEIDENTIFIER NOT NULL REFERENCES Roles(Id) ON DELETE CASCADE,
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
        CONSTRAINT PK_RolesUsers PRIMARY KEY (RoleId, UserId)
    );
    PRINT '  [+] RolesUsers table created.';
END
ELSE
    PRINT '  [=] RolesUsers already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 11. Post junction tables
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PostCategories')
BEGIN
    CREATE TABLE PostCategories (
        PostId     UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)      ON DELETE CASCADE,
        CategoryId UNIQUEIDENTIFIER NOT NULL REFERENCES Categories(Id) ON DELETE CASCADE,
        CONSTRAINT PK_PostCategories PRIMARY KEY (PostId, CategoryId)
    );
    PRINT '  [+] PostCategories table created.';
END
ELSE
    PRINT '  [=] PostCategories already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PostTags')
BEGIN
    CREATE TABLE PostTags (
        PostId    UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id) ON DELETE CASCADE,
        TagId     UNIQUEIDENTIFIER NOT NULL REFERENCES Tags(Id)  ON DELETE CASCADE,
        SortOrder INT              NULL,
        CONSTRAINT PK_PostTags PRIMARY KEY (PostId, TagId)
    );
    PRINT '  [+] PostTags table created.';
END
ELSE
    PRINT '  [=] PostTags already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 11b. Series (ordered post collections / multi-part guides) + SeriesPosts join
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Series')
BEGIN
    CREATE TABLE Series (
        Id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Title       NVARCHAR(255)    NOT NULL,
        Slug        NVARCHAR(255)    NOT NULL,
        Description NVARCHAR(MAX)    NULL,
        AuthorId    UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
        CreatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_Series_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Series table created.';
END
ELSE
    PRINT '  [=] Series already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SeriesPosts')
BEGIN
    CREATE TABLE SeriesPosts (
        SeriesId  UNIQUEIDENTIFIER NOT NULL REFERENCES Series(Id) ON DELETE CASCADE,
        PostId    UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)  ON DELETE CASCADE,
        SortOrder INT              NOT NULL DEFAULT 0,
        CONSTRAINT PK_SeriesPosts PRIMARY KEY (SeriesId, PostId)
    );
    CREATE INDEX IX_SeriesPosts_SeriesId_SortOrder ON SeriesPosts (SeriesId, SortOrder);
    CREATE INDEX IX_SeriesPosts_PostId             ON SeriesPosts (PostId);
    PRINT '  [+] SeriesPosts table created.';
END
ELSE
    PRINT '  [=] SeriesPosts already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 11c. Content importer (WordPress / Ghost migration jobs + per-item tracking)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ImportJobs')
BEGIN
    CREATE TABLE ImportJobs (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        OwnerId       UNIQUEIDENTIFIER NOT NULL,
        Source        NVARCHAR(20)     NOT NULL,
        FileName      NVARCHAR(500)    NULL,
        FilePath      NVARCHAR(1000)   NULL,
        Status        NVARCHAR(20)     NOT NULL DEFAULT 'Draft',
        OptionsJson   NVARCHAR(MAX)    NULL,
        TotalItems    INT NOT NULL DEFAULT 0,
        ImportedItems INT NOT NULL DEFAULT 0,
        FailedItems   INT NOT NULL DEFAULT 0,
        SkippedItems  INT NOT NULL DEFAULT 0,
        CreatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CompletedAt   DATETIME2 NULL
    );
    CREATE INDEX IX_ImportJobs_OwnerId_CreatedAt ON ImportJobs (OwnerId, CreatedAt DESC);
    PRINT '  [+] ImportJobs table created.';
END
ELSE
    PRINT '  [=] ImportJobs already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ImportItems')
BEGIN
    CREATE TABLE ImportItems (
        Id       BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        JobId    UNIQUEIDENTIFIER NOT NULL REFERENCES ImportJobs(Id) ON DELETE CASCADE,
        ItemType NVARCHAR(20)   NOT NULL,
        SourceId NVARCHAR(450)  NOT NULL,
        Title    NVARCHAR(1000) NULL,
        Status   NVARCHAR(20)   NOT NULL DEFAULT 'Pending',
        TargetId UNIQUEIDENTIFIER NULL,
        Error    NVARCHAR(MAX)  NULL,
        Ordinal  INT NOT NULL DEFAULT 0,
        DataJson NVARCHAR(MAX)  NULL
    );
    CREATE INDEX IX_ImportItems_Job_Status_Ordinal ON ImportItems (JobId, Status, Ordinal, Id);
    CREATE INDEX IX_ImportItems_Job_Type_Source    ON ImportItems (JobId, ItemType, SourceId);
    PRINT '  [+] ImportItems table created.';
END
ELSE
    PRINT '  [=] ImportItems already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 12. Members  (newsletter subscribers, double opt-in)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Members')
BEGIN
    CREATE TABLE Members (
        Id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Uuid             UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Email            NVARCHAR(320)    NOT NULL,
        Name             NVARCHAR(200)    NULL,
        Note             NVARCHAR(2000)   NULL,
        Status           NVARCHAR(50)     NOT NULL DEFAULT 'pending',
        Subscribed       BIT              NOT NULL DEFAULT 0,
        ConfirmToken     NVARCHAR(100)    NOT NULL,
        UnsubscribeToken NVARCHAR(100)    NOT NULL,
        ConfirmedAt      DATETIME2        NULL,
        CreatedAt        DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        DeletedAt        DATETIME2        NULL,

        CONSTRAINT UQ_Members_Email            UNIQUE (Email),
        CONSTRAINT UQ_Members_ConfirmToken     UNIQUE (ConfirmToken),
        CONSTRAINT UQ_Members_UnsubscribeToken UNIQUE (UnsubscribeToken)
    );
    PRINT '  [+] Members table created.';
END
ELSE
    PRINT '  [=] Members already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 13. Labels  (member segmentation tags)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Labels')
BEGIN
    CREATE TABLE Labels (
        Id   UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name NVARCHAR(200)    NOT NULL,
        Slug NVARCHAR(200)    NOT NULL,

        CONSTRAINT UQ_Labels_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Labels table created.';
END
ELSE
    PRINT '  [=] Labels already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 14. MemberLabels
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MemberLabels')
BEGIN
    CREATE TABLE MemberLabels (
        MemberId UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id) ON DELETE CASCADE,
        LabelId  UNIQUEIDENTIFIER NOT NULL REFERENCES Labels(Id)  ON DELETE CASCADE,
        CONSTRAINT PK_MemberLabels PRIMARY KEY (MemberId, LabelId)
    );
    PRINT '  [+] MemberLabels table created.';
END
ELSE
    PRINT '  [=] MemberLabels already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 15. Newsletters
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Newsletters')
BEGIN
    CREATE TABLE Newsletters (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name          NVARCHAR(200)    NOT NULL,
        Slug          NVARCHAR(200)    NOT NULL,
        Description   NVARCHAR(2000)   NULL,
        SenderName    NVARCHAR(200)    NULL,
        SenderEmail   NVARCHAR(320)    NULL,
        SenderReplyTo NVARCHAR(320)    NULL,
        Status        NVARCHAR(50)     NULL DEFAULT 'active',
        CreatedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_Newsletters_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Newsletters table created.';
END
ELSE
    PRINT '  [=] Newsletters already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 16. MemberNewsletters
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'MemberNewsletters')
BEGIN
    CREATE TABLE MemberNewsletters (
        MemberId     UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id)     ON DELETE CASCADE,
        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE CASCADE,
        Subscribed   BIT              NOT NULL DEFAULT 1,
        CONSTRAINT PK_MemberNewsletters PRIMARY KEY (MemberId, NewsletterId)
    );
    PRINT '  [+] MemberNewsletters table created.';
END
ELSE
    PRINT '  [=] MemberNewsletters already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 17. Emails  (newsletter send log per post)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Emails')
BEGIN
    CREATE TABLE Emails (
        Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        PostId       UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)       ON DELETE CASCADE,
        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE NO ACTION,
        Subject      NVARCHAR(500)    NOT NULL,
        Status       NVARCHAR(50)     NULL DEFAULT 'pending',
        OpensCount   INT              NOT NULL DEFAULT 0,
        ClicksCount  INT              NOT NULL DEFAULT 0,
        SentCount    INT              NOT NULL DEFAULT 0,
        CreatedAt    DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  [+] Emails table created.';
END
ELSE
    PRINT '  [=] Emails already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 18. Redirects
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Redirects')
BEGIN
    CREATE TABLE Redirects (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [From]    NVARCHAR(850)    NOT NULL,
        [To]      NVARCHAR(2000)   NOT NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_Redirects_From UNIQUE ([From])
    );
    PRINT '  [+] Redirects table created.';
END
ELSE
    PRINT '  [=] Redirects already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 19. Snippets
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Snippets')
BEGIN
    CREATE TABLE Snippets (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name      NVARCHAR(200)    NOT NULL,
        Lexical   NVARCHAR(MAX)    NULL,
        CreatedBy UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE NO ACTION,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  [+] Snippets table created.';
END
ELSE
    PRINT '  [=] Snippets already exists — skipped.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 20. PageViews  (analytics — BIGINT IDENTITY so TRUNCATE resets cleanly)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PageViews')
BEGIN
    CREATE TABLE PageViews (
        Id        BIGINT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId   UNIQUEIDENTIFIER NOT NULL,
        Path      NVARCHAR(500)    NOT NULL,
        Referrer  NVARCHAR(1000)   NULL,
        Source    NVARCHAR(100)    NULL,
        Medium    NVARCHAR(100)    NULL,
        Campaign  NVARCHAR(200)    NULL,
        Term      NVARCHAR(200)    NULL,
        Content   NVARCHAR(200)    NULL,
        IpHash    NVARCHAR(64)     NULL,
        UserAgent NVARCHAR(500)    NULL,
        Country   NVARCHAR(2)      NULL,
        Region    NVARCHAR(200)    NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_PageViews_OwnerId_CreatedAt ON PageViews (OwnerId, CreatedAt DESC);
    CREATE INDEX IX_PageViews_OwnerId_Path      ON PageViews (OwnerId, Path);
    CREATE INDEX IX_PageViews_OwnerId_Source    ON PageViews (OwnerId, Source);
    CREATE INDEX IX_PageViews_OwnerId_Country   ON PageViews (OwnerId, Country);
    PRINT '  [+] PageViews table + indexes created.';
END
ELSE
BEGIN
    -- Ensure Country/Region columns exist (added in v1.0.1)
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'Country')
        ALTER TABLE PageViews ADD Country NVARCHAR(2) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'Region')
        ALTER TABLE PageViews ADD Region NVARCHAR(200) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'PageViews') AND name = N'IX_PageViews_OwnerId_Country')
        CREATE INDEX IX_PageViews_OwnerId_Country ON PageViews (OwnerId, Country);
    PRINT '  [=] PageViews already exists — upgrade columns/indexes checked.';
END

-- ─────────────────────────────────────────────────────────────────────────────
-- 21. AuditLogs  (immutable admin action log — read-only from UI)
-- ─────────────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLogs')
BEGIN
    CREATE TABLE AuditLogs (
        Id         BIGINT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId    UNIQUEIDENTIFIER NOT NULL,
        UserId     UNIQUEIDENTIFIER NOT NULL,
        UserName   NVARCHAR(200)    NOT NULL,
        Action     NVARCHAR(100)    NOT NULL,
        EntityType NVARCHAR(100)    NOT NULL,
        EntityId   NVARCHAR(100)    NULL,
        EntityName NVARCHAR(500)    NULL,
        OldValues  NVARCHAR(MAX)    NULL,
        NewValues  NVARCHAR(MAX)    NULL,
        IpAddress  NVARCHAR(45)     NULL,
        UserAgent  NVARCHAR(500)    NULL,
        CreatedAt  DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    CREATE INDEX IX_AuditLogs_OwnerId_CreatedAt ON AuditLogs (OwnerId, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_EntityType        ON AuditLogs (OwnerId, EntityType, CreatedAt DESC);
    CREATE INDEX IX_AuditLogs_UserId            ON AuditLogs (OwnerId, UserId,     CreatedAt DESC);
    PRINT '  [+] AuditLogs table + indexes created.';
END
ELSE
    PRINT '  [=] AuditLogs already exists — skipped.';


-- =============================================================================
-- 30. ErrorLogs  —  grouped 4xx/5xx error signatures behind Admin → Error Monitor
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'ErrorLogs')
BEGIN
    CREATE TABLE ErrorLogs (
        Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
        Fingerprint     NVARCHAR(64)   NOT NULL,
        StatusCode      INT            NOT NULL,
        Method          NVARCHAR(10)   NOT NULL DEFAULT 'GET',
        Path            NVARCHAR(1024) NOT NULL,
        ExceptionType   NVARCHAR(256)  NULL,
        Message         NVARCHAR(2048) NULL,
        StackTrace      NVARCHAR(MAX)  NULL,
        UserAgent       NVARCHAR(512)  NULL,
        Referer         NVARCHAR(1024) NULL,
        LastIpAddress   NVARCHAR(45)   NULL,
        OccurrenceCount INT            NOT NULL DEFAULT 1,
        FirstSeenAt     DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        LastSeenAt      DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_ErrorLogs_Fingerprint ON ErrorLogs (Fingerprint);
    CREATE INDEX        IX_ErrorLogs_LastSeenAt  ON ErrorLogs (LastSeenAt DESC);
    PRINT '  [+] ErrorLogs table + indexes created.';
END
ELSE
BEGIN
    -- LastIpAddress arrived with the one-click "block this address" action.
    IF COL_LENGTH('ErrorLogs', 'LastIpAddress') IS NULL
    BEGIN
        ALTER TABLE ErrorLogs ADD LastIpAddress NVARCHAR(45) NULL;
        PRINT '  [+] ErrorLogs.LastIpAddress added.';
    END
    PRINT '  [=] ErrorLogs already exists — skipped.';
END

-- =============================================================================
-- 31. IpFirewallRules  —  blocked / allowlisted addresses (Admin → Security)
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'IpFirewallRules')
BEGIN
    CREATE TABLE IpFirewallRules (
        Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IpAddress     NVARCHAR(45)   NOT NULL,
        Kind          NVARCHAR(10)   NOT NULL DEFAULT 'Block',
        Source        NVARCHAR(10)   NOT NULL DEFAULT 'Auto',
        Reason        NVARCHAR(500)  NULL,
        Score         INT            NOT NULL DEFAULT 0,
        OffenseCount  INT            NOT NULL DEFAULT 1,
        HitCount      INT            NOT NULL DEFAULT 0,
        LastPath      NVARCHAR(1024) NULL,
        LastUserAgent NVARCHAR(512)  NULL,
        CreatedBy     NVARCHAR(200)  NULL,
        CreatedAt     DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt     DATETIME2      NULL,
        LastHitAt     DATETIME2      NULL,
        CONSTRAINT UQ_IpFirewallRules_IpAddress UNIQUE (IpAddress)
    );
    CREATE INDEX IX_IpFirewallRules_Kind_ExpiresAt ON IpFirewallRules (Kind, ExpiresAt);
    CREATE INDEX IX_IpFirewallRules_CreatedAt      ON IpFirewallRules (CreatedAt DESC);
    PRINT '  [+] IpFirewallRules table + indexes created.';
END
ELSE
    PRINT '  [=] IpFirewallRules already exists — skipped.';

-- =============================================================================
-- 32. CrawlerVisits  —  identified AI / search crawler requests (Admin → AI Crawlers)
--     Kept apart from PageViews, which is human analytics: mixing crawler hits
--     into it would silently inflate every visitor number.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'CrawlerVisits')
BEGIN
    CREATE TABLE CrawlerVisits (
        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId    UNIQUEIDENTIFIER NOT NULL,
        Crawler    NVARCHAR(60)     NOT NULL,
        Operator   NVARCHAR(60)     NOT NULL,
        IsAi       BIT              NOT NULL,
        Path       NVARCHAR(1024)   NOT NULL,
        StatusCode INT              NOT NULL,
        UserAgent  NVARCHAR(512)    NULL,
        VisitedAt  DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_CrawlerVisits_Owner_VisitedAt ON CrawlerVisits (OwnerId, VisitedAt DESC);
    CREATE INDEX IX_CrawlerVisits_Owner_Crawler   ON CrawlerVisits (OwnerId, Crawler, VisitedAt DESC);
    CREATE INDEX IX_CrawlerVisits_Owner_Path      ON CrawlerVisits (OwnerId, Path);
    PRINT '  [+] CrawlerVisits table + indexes created.';
END
ELSE
    PRINT '  [=] CrawlerVisits already exists — skipped.';

-- =============================================================================
-- 33. Jobs  —  ledger for the in-process scheduled-job runner (Admin → Dashboard)
--     One row per job name: last start/finish, outcome, one-line report.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Jobs')
BEGIN
    CREATE TABLE Jobs (
        Name           NVARCHAR(100)  NOT NULL PRIMARY KEY,
        LastStartedAt  DATETIME2      NULL,
        LastFinishedAt DATETIME2      NULL,
        LastSucceeded  BIT            NOT NULL CONSTRAINT DF_Jobs_LastSucceeded DEFAULT 0,
        LastMessage    NVARCHAR(1000) NULL,
        RunCount       INT            NOT NULL CONSTRAINT DF_Jobs_RunCount DEFAULT 0
    );
    PRINT '  [+] Jobs table created.';
END
ELSE
    PRINT '  [=] Jobs already exists — skipped.';

-- =============================================================================
-- 34. PasswordResetTokens  —  self-service password reset links (hash only,
--     single-use, 30-minute expiry; expired rows pruned nightly)
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PasswordResetTokens')
BEGIN
    CREATE TABLE PasswordResetTokens (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        UserId    UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
        TokenHash NVARCHAR(128)    NOT NULL,
        ExpiresAt DATETIME2        NOT NULL,
        UsedAt    DATETIME2        NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
        RequestIp NVARCHAR(45)     NULL
    );
    CREATE UNIQUE INDEX UX_PasswordResetTokens_TokenHash ON PasswordResetTokens (TokenHash);
    CREATE INDEX IX_PasswordResetTokens_User_CreatedAt ON PasswordResetTokens (UserId, CreatedAt DESC);
    PRINT '  [+] PasswordResetTokens table + indexes created.';
END
ELSE
    PRINT '  [=] PasswordResetTokens already exists — skipped.';

-- =============================================================================
-- 35. SearchPerformance  —  daily Search Console rows (date × page × query),
--     pulled nightly per connected owner; feeds the striking-distance panel,
--     the Search filters in Content Health and the dashboard tile.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'SearchPerformance')
BEGIN
    CREATE TABLE SearchPerformance (
        Id          BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OwnerId     UNIQUEIDENTIFIER NOT NULL,
        [Date]      DATE             NOT NULL,
        Page        NVARCHAR(1024)   NOT NULL,
        Query       NVARCHAR(512)    NOT NULL,
        Clicks      INT              NOT NULL,
        Impressions INT              NOT NULL,
        Ctr         FLOAT            NOT NULL,
        Position    FLOAT            NOT NULL
    );
    CREATE INDEX IX_SearchPerformance_Owner_Date      ON SearchPerformance (OwnerId, [Date]);
    CREATE INDEX IX_SearchPerformance_Owner_Page_Date ON SearchPerformance (OwnerId, Page, [Date]);
    PRINT '  [+] SearchPerformance table + indexes created.';
END
ELSE
    PRINT '  [=] SearchPerformance already exists — skipped.';

-- =============================================================================
-- 36. Revisions  —  content snapshots of posts and pages, written on every
--     saved change; compared and restored from the editor sidebar; pruned to
--     the tenant's RevisionsPerItem.
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Revisions')
BEGIN
    CREATE TABLE Revisions (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        EntityType      NVARCHAR(20)     NOT NULL,
        EntityId        UNIQUEIDENTIFIER NOT NULL,
        Number          INT              NOT NULL,
        Title           NVARCHAR(500)    NOT NULL,
        Slug            NVARCHAR(500)    NOT NULL,
        Html            NVARCHAR(MAX)    NOT NULL,
        MetaTitle       NVARCHAR(500)    NULL,
        MetaDescription NVARCHAR(1000)   NULL,
        StructuredJson  NVARCHAR(MAX)    NULL,
        Status          NVARCHAR(50)     NOT NULL,
        Reason          NVARCHAR(100)    NOT NULL,
        AuthorId        UNIQUEIDENTIFIER NOT NULL,
        AuthorName      NVARCHAR(200)    NULL,
        CreatedAt       DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Revisions_Entity_CreatedAt ON Revisions (EntityType, EntityId, CreatedAt DESC);
    PRINT '  [+] Revisions table + index created.';
END
ELSE
    PRINT '  [=] Revisions already exists — skipped.';

-- =============================================================================
-- 36a. Webhooks + WebhookDeliveries  —  outbound signed events (Admin → Webhooks)
--      and their per-attempt delivery log (DBScripts/2026-09-27_create-webhooks-tables.sql).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Webhooks')
BEGIN
    CREATE TABLE Webhooks (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        OwnerId         UNIQUEIDENTIFIER NOT NULL,
        Name            NVARCHAR(100)    NOT NULL,
        Url             NVARCHAR(2048)   NOT NULL,
        SecretProtected NVARCHAR(MAX)    NOT NULL,
        Events          NVARCHAR(400)    NOT NULL,
        IsEnabled       BIT              NOT NULL CONSTRAINT DF_Webhooks_IsEnabled DEFAULT 1,
        SiteBaseUrl     NVARCHAR(300)    NULL,
        CreatedAt       DATETIME2        NOT NULL CONSTRAINT DF_Webhooks_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt       DATETIME2        NOT NULL CONSTRAINT DF_Webhooks_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Webhooks_OwnerId ON Webhooks (OwnerId);
    PRINT '  [+] Webhooks table + index created.';
END
ELSE
    PRINT '  [=] Webhooks already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WebhookDeliveries')
BEGIN
    CREATE TABLE WebhookDeliveries (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        WebhookId       UNIQUEIDENTIFIER NOT NULL
                        CONSTRAINT FK_WebhookDeliveries_Webhooks REFERENCES Webhooks(Id) ON DELETE CASCADE,
        OwnerId         UNIQUEIDENTIFIER NOT NULL,
        EventId         UNIQUEIDENTIFIER NOT NULL,
        EventType       NVARCHAR(50)     NOT NULL,
        EventKey        NVARCHAR(200)    NULL,
        PayloadJson     NVARCHAR(MAX)    NOT NULL,
        Status          NVARCHAR(20)     NOT NULL,
        Attempts        INT              NOT NULL CONSTRAINT DF_WebhookDeliveries_Attempts DEFAULT 0,
        NextAttemptAt   DATETIME2        NOT NULL,
        LastAttemptAt   DATETIME2        NULL,
        ResponseCode    INT              NULL,
        ResponseSnippet NVARCHAR(500)    NULL,
        Error           NVARCHAR(500)    NULL,
        CreatedAt       DATETIME2        NOT NULL CONSTRAINT DF_WebhookDeliveries_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_WebhookDeliveries_Due ON WebhookDeliveries (Status, NextAttemptAt);
    CREATE INDEX IX_WebhookDeliveries_Webhook_CreatedAt ON WebhookDeliveries (WebhookId, CreatedAt DESC);
    PRINT '  [+] WebhookDeliveries table + indexes created.';
END
ELSE
    PRINT '  [=] WebhookDeliveries already exists — skipped.';

-- One queued event per webhook per key (e.g. a scheduled post's publish), enforced by a filtered
-- unique index. Filtered indexes need QUOTED_IDENTIFIER ON, which sqlcmd leaves off, so it is set in
-- the batch that creates the index. Checked on its own so a partial earlier run heals.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('WebhookDeliveries') AND name = 'UX_WebhookDeliveries_Webhook_EventKey')
BEGIN
    EXEC (N'SET QUOTED_IDENTIFIER ON; CREATE UNIQUE INDEX UX_WebhookDeliveries_Webhook_EventKey ON WebhookDeliveries (WebhookId, EventKey) WHERE EventKey IS NOT NULL;');
    PRINT '  [+] UX_WebhookDeliveries_Webhook_EventKey created.';
END
ELSE
    PRINT '  [=] UX_WebhookDeliveries_Webhook_EventKey already exists — skipped.';

-- =============================================================================
-- 36b. MenuItems  —  header and footer navigation menus (Admin → Menus)
--      (DBScripts/2026-09-27_create-menu-items-table.sql).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MenuItems')
BEGIN
    CREATE TABLE MenuItems (
        Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        OwnerId      UNIQUEIDENTIFIER NOT NULL,
        Location     NVARCHAR(20)     NOT NULL,
        ParentId     UNIQUEIDENTIFIER NULL,
        SortOrder    INT              NOT NULL CONSTRAINT DF_MenuItems_SortOrder DEFAULT 0,
        Label        NVARCHAR(100)    NULL,
        LinkType     NVARCHAR(20)     NOT NULL,
        TargetId     UNIQUEIDENTIFIER NULL,
        Url          NVARCHAR(2048)   NULL,
        OpenInNewTab BIT              NOT NULL CONSTRAINT DF_MenuItems_OpenInNewTab DEFAULT 0,
        CreatedAt    DATETIME2        NOT NULL CONSTRAINT DF_MenuItems_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt    DATETIME2        NOT NULL CONSTRAINT DF_MenuItems_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_MenuItems_Owner_Location ON MenuItems (OwnerId, Location, SortOrder);
    PRINT '  [+] MenuItems table + index created.';
END
ELSE
    PRINT '  [=] MenuItems already exists — skipped.';

-- =============================================================================
-- 36b. NewsletterIssues + NewsletterDeliveries  —  newsletter sending (Admin → Newsletter)
--      (DBScripts/2026-10-05_create-newsletter-issues-and-deliveries.sql, ADR-0028).
-- =============================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NewsletterIssues')
BEGIN
    CREATE TABLE NewsletterIssues (
        Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        OwnerId         UNIQUEIDENTIFIER NOT NULL,
        Subject         NVARCHAR(200)    NOT NULL,
        Preheader       NVARCHAR(200)    NULL,
        IntroMarkdown   NVARCHAR(MAX)    NULL,
        PostIds         NVARCHAR(1000)   NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_NewsletterIssues_Status DEFAULT 'Draft',
        SiteBaseUrl     NVARCHAR(300)    NULL,
        RenderedHtml    NVARCHAR(MAX)    NULL,
        RenderedText    NVARCHAR(MAX)    NULL,
        RecipientCount  INT              NOT NULL CONSTRAINT DF_NewsletterIssues_RecipientCount DEFAULT 0,
        SentCount       INT              NOT NULL CONSTRAINT DF_NewsletterIssues_SentCount DEFAULT 0,
        FailedCount     INT              NOT NULL CONSTRAINT DF_NewsletterIssues_FailedCount DEFAULT 0,
        CreatedBy       UNIQUEIDENTIFIER NOT NULL,
        QueuedBy        UNIQUEIDENTIFIER NULL,
        QueuedByName    NVARCHAR(200)    NULL,
        CreatedAt       DATETIME2        NOT NULL CONSTRAINT DF_NewsletterIssues_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt       DATETIME2        NOT NULL CONSTRAINT DF_NewsletterIssues_UpdatedAt DEFAULT SYSUTCDATETIME(),
        QueuedAt        DATETIME2        NULL,
        FinishedAt      DATETIME2        NULL
    );
    CREATE INDEX IX_NewsletterIssues_Owner_Status ON NewsletterIssues (OwnerId, Status);
    PRINT '  [+] NewsletterIssues table + index created.';
END
ELSE
    PRINT '  [=] NewsletterIssues already exists — skipped.';

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'NewsletterDeliveries')
BEGIN
    CREATE TABLE NewsletterDeliveries (
        Id              BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IssueId         UNIQUEIDENTIFIER NOT NULL
                        CONSTRAINT FK_NewsletterDeliveries_Issues REFERENCES NewsletterIssues(Id) ON DELETE CASCADE,
        MemberId        UNIQUEIDENTIFIER NOT NULL,
        Email           NVARCHAR(255)    NOT NULL,
        Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_NewsletterDeliveries_Status DEFAULT 'Pending',
        Attempts        INT              NOT NULL CONSTRAINT DF_NewsletterDeliveries_Attempts DEFAULT 0,
        NextAttemptAt   DATETIME2        NOT NULL CONSTRAINT DF_NewsletterDeliveries_NextAttemptAt DEFAULT SYSUTCDATETIME(),
        SentAt          DATETIME2        NULL,
        Error           NVARCHAR(500)    NULL,
        CONSTRAINT UX_NewsletterDeliveries_Issue_Member UNIQUE (IssueId, MemberId)
    );
    CREATE INDEX IX_NewsletterDeliveries_Due ON NewsletterDeliveries (IssueId, Status, NextAttemptAt);
    PRINT '  [+] NewsletterDeliveries table + indexes created.';
END
ELSE
    PRINT '  [=] NewsletterDeliveries already exists — skipped.';

-- =============================================================================
-- 37. Column top-ups on tables created above
--     These arrived after their table did, so a database created by an older
--     copy of this file needs them added rather than the table recreated.
-- =============================================================================

-- Posts: AI-extractable content blocks.
IF COL_LENGTH('Posts', 'KeyFactsJson') IS NULL
BEGIN
    ALTER TABLE Posts ADD KeyFactsJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.KeyFactsJson added.';
END
ELSE
    PRINT '  [=] Posts.KeyFactsJson already exists — skipped.';

IF COL_LENGTH('Posts', 'HowToJson') IS NULL
BEGIN
    ALTER TABLE Posts ADD HowToJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.HowToJson added.';
END
ELSE
    PRINT '  [=] Posts.HowToJson already exists — skipped.';

-- Answer capsule: the short direct answer rendered above the article and emitted
-- as schema.org "abstract".
IF COL_LENGTH('Posts', 'AnswerCapsule') IS NULL
BEGIN
    ALTER TABLE Posts ADD AnswerCapsule NVARCHAR(1000) NULL;
    PRINT '  [+] Posts.AnswerCapsule added.';
END
ELSE
    PRINT '  [=] Posts.AnswerCapsule already exists — skipped.';

-- Redirects: 301 (default) / 302 / 410 Gone.
IF COL_LENGTH('Redirects', 'StatusCode') IS NULL
BEGIN
    ALTER TABLE Redirects ADD StatusCode INT NOT NULL CONSTRAINT DF_Redirects_StatusCode DEFAULT 301;
    PRINT '  [+] Redirects.StatusCode added (default 301).';
END
ELSE
    PRINT '  [=] Redirects.StatusCode already exists — skipped.';

-- Comments.MemberId: the signed-in user behind a Desk reply. Section 5 creates it; this
-- top-up covers a database built from a copy that predates it
-- (DBScripts/2026-09-19_ensure-comments-memberid-column.sql).
IF COL_LENGTH('Comments', 'MemberId') IS NULL
BEGIN
    ALTER TABLE Comments ADD MemberId UNIQUEIDENTIFIER NULL;
    PRINT '  [+] Comments.MemberId added.';
END
ELSE
    PRINT '  [=] Comments.MemberId already exists — skipped.';

-- CrawlerVisits.Verified: whether the request came from the network the crawler's operator
-- publishes (1), failed that check (0), or could not be checked (NULL)
-- (DBScripts/2026-09-27_add-verified-to-crawlervisits.sql).
IF COL_LENGTH('CrawlerVisits', 'Verified') IS NULL
BEGIN
    ALTER TABLE CrawlerVisits ADD Verified BIT NULL;
    PRINT '  [+] CrawlerVisits.Verified added.';
END
ELSE
    PRINT '  [=] CrawlerVisits.Verified already exists — skipped.';

-- CrawlerVisits.Query: the request's query string, so the AI Crawlers report can show what crawlers
-- actually request, not just the path (DBScripts/2026-09-27_add-query-to-crawlervisits.sql).
IF COL_LENGTH('CrawlerVisits', 'Query') IS NULL
BEGIN
    ALTER TABLE CrawlerVisits ADD Query NVARCHAR(512) NULL;
    PRINT '  [+] CrawlerVisits.Query added.';
END
ELSE
    PRINT '  [=] CrawlerVisits.Query already exists — skipped.';

-- Posts.ReviewNote / ReviewRequestedAt: editorial workflow (an editor's note when a post is returned;
-- when it was submitted) (DBScripts/2026-09-27_add-review-workflow-to-posts.sql).
IF COL_LENGTH('Posts', 'ReviewNote') IS NULL
BEGIN
    ALTER TABLE Posts ADD ReviewNote NVARCHAR(1000) NULL;
    PRINT '  [+] Posts.ReviewNote added.';
END
ELSE
    PRINT '  [=] Posts.ReviewNote already exists — skipped.';

IF COL_LENGTH('Posts', 'ReviewRequestedAt') IS NULL
BEGIN
    ALTER TABLE Posts ADD ReviewRequestedAt DATETIME2 NULL;
    PRINT '  [+] Posts.ReviewRequestedAt added.';
END
ELSE
    PRINT '  [=] Posts.ReviewRequestedAt already exists — skipped.';

-- Users two-factor sign-in (DBScripts/2026-10-05_add-two-factor-to-users.sql, ADR-0021): the TOTP secret
-- (Data-Protection-encrypted), when it was turned on, hashed recovery codes and the last accepted step.
IF COL_LENGTH('Users', 'TwoFactorSecretProtected') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorSecretProtected NVARCHAR(1000) NULL;
    PRINT '  [+] Users.TwoFactorSecretProtected added.';
END
IF COL_LENGTH('Users', 'TwoFactorEnabledAt') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorEnabledAt DATETIME2 NULL;
    PRINT '  [+] Users.TwoFactorEnabledAt added.';
END
IF COL_LENGTH('Users', 'TwoFactorRecoveryHashes') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorRecoveryHashes NVARCHAR(MAX) NULL;
    PRINT '  [+] Users.TwoFactorRecoveryHashes added.';
END
IF COL_LENGTH('Users', 'TwoFactorLastStep') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorLastStep BIGINT NULL;
    PRINT '  [+] Users.TwoFactorLastStep added.';
END

-- Posts Markdown authoring (DBScripts/2026-10-05_add-markdown-to-posts.sql, ADR-0022).
IF COL_LENGTH('Posts', 'Markdown') IS NULL
BEGIN
    ALTER TABLE Posts ADD Markdown NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.Markdown added.';
END
IF COL_LENGTH('Posts', 'EditorMode') IS NULL
BEGIN
    ALTER TABLE Posts ADD EditorMode NVARCHAR(20) NOT NULL CONSTRAINT DF_Posts_EditorMode DEFAULT 'rich' WITH VALUES;
    PRINT '  [+] Posts.EditorMode added.';
END

-- Custom fields (DBScripts/2026-10-05_add-custom-fields-to-posts-and-pages.sql, ADR-0025).
IF COL_LENGTH('Posts', 'CustomFieldsJson') IS NULL
BEGIN
    ALTER TABLE Posts ADD CustomFieldsJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.CustomFieldsJson added.';
END
IF COL_LENGTH('Pages', 'CustomFieldsJson') IS NULL
BEGIN
    ALTER TABLE Pages ADD CustomFieldsJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Pages.CustomFieldsJson added.';
END

-- =============================================================================
-- 38. Index top-ups on tables created above
--     Same reason as section 37: the CREATE TABLE blocks never run again on a
--     database that already exists, so an index that arrived later is added here.
-- =============================================================================

-- Revisions: numbers are unique per entity; the repository computes the next
-- number inside the INSERT and relies on this index as the hard guarantee
-- (DBScripts/2026-09-19_add-unique-index-revisions-number.sql). Duplicates a
-- pre-index install produced are renumbered first, only where they exist.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Revisions') AND name = 'UX_Revisions_Entity_Number')
BEGIN
    IF EXISTS (SELECT 1 FROM Revisions GROUP BY EntityType, EntityId, Number HAVING COUNT(*) > 1)
    BEGIN
        ;WITH d AS (
            SELECT r.Number,
                   NewNumber = ROW_NUMBER() OVER (PARTITION BY r.EntityType, r.EntityId ORDER BY r.Number, r.CreatedAt, r.Id)
            FROM Revisions r
            WHERE EXISTS (SELECT 1 FROM Revisions x
                          WHERE x.EntityType = r.EntityType AND x.EntityId = r.EntityId
                          GROUP BY x.Number HAVING COUNT(*) > 1)
        )
        UPDATE d SET Number = NewNumber WHERE Number <> NewNumber;
        PRINT '  [~] Revisions: duplicate numbers renumbered.';
    END
    CREATE UNIQUE INDEX UX_Revisions_Entity_Number ON Revisions (EntityType, EntityId, Number);
    PRINT '  [+] UX_Revisions_Entity_Number created.';
END
ELSE
    PRINT '  [=] UX_Revisions_Entity_Number already exists — skipped.';

-- Comments: the spam gate filters every submission on CreatedAt (and AuthorIp)
-- (DBScripts/2026-09-19_add-index-comments-createdat.sql).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Comments') AND name = 'IX_Comments_CreatedAt')
BEGIN
    CREATE INDEX IX_Comments_CreatedAt ON Comments (CreatedAt) INCLUDE (AuthorIp);
    PRINT '  [+] IX_Comments_CreatedAt created.';
END
ELSE
    PRINT '  [=] IX_Comments_CreatedAt already exists — skipped.';
-- =============================================================================
-- 39. Reference catalogs: countries, time zones and date locales (ADR-0027)
--     Verbatim from DBScripts/2026-10-05_create-and-seed-countries-timezones-date-locales.sql (IANA tzdata
--     + Unicode CLDR; see that file's header for sources and versions). A test keeps the two identical.
-- =============================================================================
SET NOCOUNT ON;

IF OBJECT_ID('Countries', 'U') IS NULL
BEGIN
    CREATE TABLE Countries (
        Code            CHAR(2)        NOT NULL CONSTRAINT PK_Countries PRIMARY KEY,
        Name            NVARCHAR(100)  NOT NULL,
        LikelyLocale    NVARCHAR(20)   NULL,
        DefaultTimeZone NVARCHAR(64)   NULL
    );
    PRINT '  [+] Countries created.';
END

IF OBJECT_ID('TimeZones', 'U') IS NULL
BEGIN
    CREATE TABLE TimeZones (
        Id                    NVARCHAR(64)   NOT NULL CONSTRAINT PK_TimeZones PRIMARY KEY,
        CountryCode           CHAR(2)        NULL,
        WindowsId             NVARCHAR(64)   NULL,
        StandardOffsetMinutes INT            NULL,
        City                  NVARCHAR(100)  NOT NULL,
        Comment               NVARCHAR(200)  NULL,
        DisplayName           NVARCHAR(200)  NOT NULL
    );
    CREATE INDEX IX_TimeZones_CountryCode ON TimeZones (CountryCode);
    PRINT '  [+] TimeZones created.';
END

IF OBJECT_ID('DateLocales', 'U') IS NULL
BEGIN
    CREATE TABLE DateLocales (
        Code             NVARCHAR(20)   NOT NULL CONSTRAINT PK_DateLocales PRIMARY KEY,
        CountryCode      CHAR(2)        NOT NULL,
        EnglishName      NVARCHAR(150)  NOT NULL,
        NativeName       NVARCHAR(150)  NOT NULL,
        ShortDatePattern NVARCHAR(60)   NOT NULL,
        LongDatePattern  NVARCHAR(100)  NOT NULL,
        ShortTimePattern NVARCHAR(40)   NOT NULL,
        FirstDayOfWeek   TINYINT        NOT NULL,
        SampleLong       NVARCHAR(100)  NOT NULL,
        SampleShort      NVARCHAR(60)   NOT NULL
    );
    CREATE INDEX IX_DateLocales_CountryCode ON DateLocales (CountryCode);
    PRINT '  [+] DateLocales created.';
END
MERGE Countries AS t
USING (VALUES
    (N'AD', N'Andorra', N'ca-AD', N'Europe/Andorra'),
    (N'AE', N'United Arab Emirates', N'ar-AE', N'Asia/Dubai'),
    (N'AF', N'Afghanistan', N'fa-AF', N'Asia/Kabul'),
    (N'AG', N'Antigua & Barbuda', N'en-AG', N'America/Antigua'),
    (N'AI', N'Anguilla', N'en-AI', N'America/Anguilla'),
    (N'AL', N'Albania', N'sq-AL', N'Europe/Tirane'),
    (N'AM', N'Armenia', N'hy-AM', N'Asia/Yerevan'),
    (N'AO', N'Angola', N'pt-AO', N'Africa/Luanda'),
    (N'AQ', N'Antarctica', NULL, N'Antarctica/Casey'),
    (N'AR', N'Argentina', N'es-AR', N'America/Argentina/Buenos_Aires'),
    (N'AS', N'Samoa (American)', N'en-AS', N'Pacific/Pago_Pago'),
    (N'AT', N'Austria', N'de-AT', N'Europe/Vienna'),
    (N'AU', N'Australia', N'en-AU', N'Australia/Sydney'),
    (N'AW', N'Aruba', N'nl-AW', N'America/Aruba'),
    (N'AX', N'Åland Islands', N'sv-AX', N'Europe/Mariehamn'),
    (N'AZ', N'Azerbaijan', N'az-Latn-AZ', N'Asia/Baku'),
    (N'BA', N'Bosnia & Herzegovina', N'bs-Latn-BA', N'Europe/Sarajevo'),
    (N'BB', N'Barbados', N'en-BB', N'America/Barbados'),
    (N'BD', N'Bangladesh', N'bn-BD', N'Asia/Dhaka'),
    (N'BE', N'Belgium', N'en-BE', N'Europe/Brussels'),
    (N'BF', N'Burkina Faso', N'fr-BF', N'Africa/Ouagadougou'),
    (N'BG', N'Bulgaria', N'bg-BG', N'Europe/Sofia'),
    (N'BH', N'Bahrain', N'ar-BH', N'Asia/Bahrain'),
    (N'BI', N'Burundi', N'rn-BI', N'Africa/Bujumbura'),
    (N'BJ', N'Benin', N'fr-BJ', N'Africa/Porto-Novo'),
    (N'BL', N'St Barthelemy', N'fr-BL', N'America/St_Barthelemy'),
    (N'BM', N'Bermuda', N'en-BM', N'Atlantic/Bermuda'),
    (N'BN', N'Brunei', N'ms-BN', N'Asia/Brunei'),
    (N'BO', N'Bolivia', N'es-BO', N'America/La_Paz'),
    (N'BQ', N'Caribbean NL', N'nl-BQ', N'America/Kralendijk'),
    (N'BR', N'Brazil', N'pt-BR', N'America/Sao_Paulo'),
    (N'BS', N'Bahamas', N'en-BS', N'America/Nassau'),
    (N'BT', N'Bhutan', N'dz-BT', N'Asia/Thimphu'),
    (N'BV', N'Bouvet Island', NULL, NULL),
    (N'BW', N'Botswana', N'en-BW', N'Africa/Gaborone'),
    (N'BY', N'Belarus', N'ru-BY', N'Europe/Minsk'),
    (N'BZ', N'Belize', N'en-BZ', N'America/Belize'),
    (N'CA', N'Canada', N'en-CA', N'America/Toronto'),
    (N'CC', N'Cocos (Keeling) Islands', N'en-CC', N'Indian/Cocos'),
    (N'CD', N'Congo (Dem. Rep.)', N'fr-CD', N'Africa/Kinshasa'),
    (N'CF', N'Central African Rep.', N'sg-CF', N'Africa/Bangui'),
    (N'CG', N'Congo (Rep.)', N'fr-CG', N'Africa/Brazzaville'),
    (N'CH', N'Switzerland', N'de-CH', N'Europe/Zurich'),
    (N'CI', N'Côte d’Ivoire', N'fr-CI', N'Africa/Abidjan'),
    (N'CK', N'Cook Islands', N'en-CK', N'Pacific/Rarotonga'),
    (N'CL', N'Chile', N'es-CL', N'America/Santiago'),
    (N'CM', N'Cameroon', N'fr-CM', N'Africa/Douala'),
    (N'CN', N'China', N'zh-Hans-CN', N'Asia/Shanghai'),
    (N'CO', N'Colombia', N'es-CO', N'America/Bogota'),
    (N'CR', N'Costa Rica', N'es-CR', N'America/Costa_Rica'),
    (N'CU', N'Cuba', N'es-CU', N'America/Havana'),
    (N'CV', N'Cape Verde', N'kea-CV', N'Atlantic/Cape_Verde'),
    (N'CW', N'Curaçao', N'nl-CW', N'America/Curacao'),
    (N'CX', N'Christmas Island', N'en-CX', N'Indian/Christmas'),
    (N'CY', N'Cyprus', N'el-CY', N'Asia/Nicosia'),
    (N'CZ', N'Czech Republic', N'cs-CZ', N'Europe/Prague'),
    (N'DE', N'Germany', N'de-DE', N'Europe/Berlin'),
    (N'DJ', N'Djibouti', N'fr-DJ', N'Africa/Djibouti'),
    (N'DK', N'Denmark', N'da-DK', N'Europe/Copenhagen'),
    (N'DM', N'Dominica', N'en-DM', N'America/Dominica'),
    (N'DO', N'Dominican Republic', N'es-DO', N'America/Santo_Domingo'),
    (N'DZ', N'Algeria', N'ar-DZ', N'Africa/Algiers'),
    (N'EC', N'Ecuador', N'es-EC', N'America/Guayaquil'),
    (N'EE', N'Estonia', N'et-EE', N'Europe/Tallinn'),
    (N'EG', N'Egypt', N'ar-EG', N'Africa/Cairo'),
    (N'EH', N'Western Sahara', NULL, N'Africa/El_Aaiun'),
    (N'ER', N'Eritrea', N'ti-ER', N'Africa/Asmara'),
    (N'ES', N'Spain', N'es-ES', N'Europe/Madrid'),
    (N'ET', N'Ethiopia', N'am-ET', N'Africa/Addis_Ababa'),
    (N'FI', N'Finland', N'fi-FI', N'Europe/Helsinki'),
    (N'FJ', N'Fiji', N'en-FJ', N'Pacific/Fiji'),
    (N'FK', N'Falkland Islands', N'en-FK', N'Atlantic/Stanley'),
    (N'FM', N'Micronesia', N'en-FM', N'Pacific/Pohnpei'),
    (N'FO', N'Faroe Islands', N'fo-FO', N'Atlantic/Faroe'),
    (N'FR', N'France', N'fr-FR', N'Europe/Paris'),
    (N'GA', N'Gabon', N'fr-GA', N'Africa/Libreville'),
    (N'GB', N'Britain (UK)', N'en-GB', N'Europe/London'),
    (N'GD', N'Grenada', N'en-GD', N'America/Grenada'),
    (N'GE', N'Georgia', N'ka-GE', N'Asia/Tbilisi'),
    (N'GF', N'French Guiana', N'fr-GF', N'America/Cayenne'),
    (N'GG', N'Guernsey', N'en-GG', N'Europe/Guernsey'),
    (N'GH', N'Ghana', N'ak-GH', N'Africa/Accra'),
    (N'GI', N'Gibraltar', N'en-GI', N'Europe/Gibraltar'),
    (N'GL', N'Greenland', N'kl-GL', N'America/Nuuk'),
    (N'GM', N'Gambia', N'en-GM', N'Africa/Banjul'),
    (N'GN', N'Guinea', N'fr-GN', N'Africa/Conakry'),
    (N'GP', N'Guadeloupe', N'fr-GP', N'America/Guadeloupe'),
    (N'GQ', N'Equatorial Guinea', N'es-GQ', N'Africa/Malabo'),
    (N'GR', N'Greece', N'el-GR', N'Europe/Athens'),
    (N'GS', N'South Georgia & the South Sandwich Islands', NULL, N'Atlantic/South_Georgia'),
    (N'GT', N'Guatemala', N'es-GT', N'America/Guatemala'),
    (N'GU', N'Guam', N'en-GU', N'Pacific/Guam'),
    (N'GW', N'Guinea-Bissau', N'pt-GW', N'Africa/Bissau'),
    (N'GY', N'Guyana', N'en-GY', N'America/Guyana'),
    (N'HK', N'Hong Kong', N'zh-Hant-HK', N'Asia/Hong_Kong'),
    (N'HM', N'Heard Island & McDonald Islands', NULL, NULL),
    (N'HN', N'Honduras', N'es-HN', N'America/Tegucigalpa'),
    (N'HR', N'Croatia', N'hr-HR', N'Europe/Zagreb'),
    (N'HT', N'Haiti', N'fr-HT', N'America/Port-au-Prince'),
    (N'HU', N'Hungary', N'hu-HU', N'Europe/Budapest'),
    (N'ID', N'Indonesia', N'id-ID', N'Asia/Jakarta'),
    (N'IE', N'Ireland', N'en-IE', N'Europe/Dublin'),
    (N'IL', N'Israel', N'he-IL', N'Asia/Jerusalem'),
    (N'IM', N'Isle of Man', N'en-IM', N'Europe/Isle_of_Man'),
    (N'IN', N'India', N'hi-IN', N'Asia/Kolkata'),
    (N'IO', N'British Indian Ocean Territory', N'en-IO', N'Indian/Chagos'),
    (N'IQ', N'Iraq', N'ar-IQ', N'Asia/Baghdad'),
    (N'IR', N'Iran', N'fa-IR', N'Asia/Tehran'),
    (N'IS', N'Iceland', N'is-IS', N'Atlantic/Reykjavik'),
    (N'IT', N'Italy', N'it-IT', N'Europe/Rome'),
    (N'JE', N'Jersey', N'en-JE', N'Europe/Jersey'),
    (N'JM', N'Jamaica', N'en-JM', N'America/Jamaica'),
    (N'JO', N'Jordan', N'ar-JO', N'Asia/Amman'),
    (N'JP', N'Japan', N'ja-JP', N'Asia/Tokyo'),
    (N'KE', N'Kenya', N'sw-KE', N'Africa/Nairobi'),
    (N'KG', N'Kyrgyzstan', N'ky-KG', N'Asia/Bishkek'),
    (N'KH', N'Cambodia', N'km-KH', N'Asia/Phnom_Penh'),
    (N'KI', N'Kiribati', N'en-KI', N'Pacific/Tarawa'),
    (N'KM', N'Comoros', N'ar-KM', N'Indian/Comoro'),
    (N'KN', N'St Kitts & Nevis', N'en-KN', N'America/St_Kitts'),
    (N'KP', N'Korea (North)', N'ko-KP', N'Asia/Pyongyang'),
    (N'KR', N'Korea (South)', N'ko-KR', N'Asia/Seoul'),
    (N'KW', N'Kuwait', N'ar-KW', N'Asia/Kuwait'),
    (N'KY', N'Cayman Islands', N'en-KY', N'America/Cayman'),
    (N'KZ', N'Kazakhstan', N'ru-KZ', N'Asia/Almaty'),
    (N'LA', N'Laos', N'lo-LA', N'Asia/Vientiane'),
    (N'LB', N'Lebanon', N'ar-LB', N'Asia/Beirut'),
    (N'LC', N'St Lucia', N'en-LC', N'America/St_Lucia'),
    (N'LI', N'Liechtenstein', N'de-LI', N'Europe/Vaduz'),
    (N'LK', N'Sri Lanka', N'si-LK', N'Asia/Colombo'),
    (N'LR', N'Liberia', N'en-LR', N'Africa/Monrovia'),
    (N'LS', N'Lesotho', N'st-LS', N'Africa/Maseru'),
    (N'LT', N'Lithuania', N'lt-LT', N'Europe/Vilnius'),
    (N'LU', N'Luxembourg', N'fr-LU', N'Europe/Luxembourg'),
    (N'LV', N'Latvia', N'lv-LV', N'Europe/Riga'),
    (N'LY', N'Libya', N'ar-LY', N'Africa/Tripoli'),
    (N'MA', N'Morocco', N'ar-MA', N'Africa/Casablanca'),
    (N'MC', N'Monaco', N'fr-MC', N'Europe/Monaco'),
    (N'MD', N'Moldova', N'ro-MD', N'Europe/Chisinau'),
    (N'ME', N'Montenegro', N'sr-Latn-ME', N'Europe/Podgorica'),
    (N'MF', N'St Martin (French)', N'fr-MF', N'America/Marigot'),
    (N'MG', N'Madagascar', N'mg-MG', N'Indian/Antananarivo'),
    (N'MH', N'Marshall Islands', N'en-MH', N'Pacific/Kwajalein'),
    (N'MK', N'North Macedonia', N'mk-MK', N'Europe/Skopje'),
    (N'ML', N'Mali', N'bm-ML', N'Africa/Bamako'),
    (N'MM', N'Myanmar (Burma)', N'my-MM', N'Asia/Yangon'),
    (N'MN', N'Mongolia', N'mn-MN', N'Asia/Ulaanbaatar'),
    (N'MO', N'Macau', N'zh-Hant-MO', N'Asia/Macau'),
    (N'MP', N'Northern Mariana Islands', N'en-MP', N'Pacific/Saipan'),
    (N'MQ', N'Martinique', N'fr-MQ', N'America/Martinique'),
    (N'MR', N'Mauritania', N'ar-MR', N'Africa/Nouakchott'),
    (N'MS', N'Montserrat', N'en-MS', N'America/Montserrat'),
    (N'MT', N'Malta', N'mt-MT', N'Europe/Malta'),
    (N'MU', N'Mauritius', N'mfe-MU', N'Indian/Mauritius'),
    (N'MV', N'Maldives', N'dv-MV', N'Indian/Maldives'),
    (N'MW', N'Malawi', N'en-MW', N'Africa/Blantyre'),
    (N'MX', N'Mexico', N'es-MX', N'America/Mexico_City'),
    (N'MY', N'Malaysia', N'ms-MY', N'Asia/Kuching'),
    (N'MZ', N'Mozambique', N'pt-MZ', N'Africa/Maputo'),
    (N'NA', N'Namibia', N'af-NA', N'Africa/Windhoek'),
    (N'NC', N'New Caledonia', N'fr-NC', N'Pacific/Noumea'),
    (N'NE', N'Niger', N'ha-NE', N'Africa/Niamey'),
    (N'NF', N'Norfolk Island', N'en-NF', N'Pacific/Norfolk'),
    (N'NG', N'Nigeria', N'en-NG', N'Africa/Lagos'),
    (N'NI', N'Nicaragua', N'es-NI', N'America/Managua'),
    (N'NL', N'Netherlands', N'nl-NL', N'Europe/Amsterdam'),
    (N'NO', N'Norway', N'nb-NO', N'Europe/Oslo'),
    (N'NP', N'Nepal', N'ne-NP', N'Asia/Kathmandu'),
    (N'NR', N'Nauru', N'en-NR', N'Pacific/Nauru'),
    (N'NU', N'Niue', N'en-NU', N'Pacific/Niue'),
    (N'NZ', N'New Zealand', N'en-NZ', N'Pacific/Auckland'),
    (N'OM', N'Oman', N'ar-OM', N'Asia/Muscat'),
    (N'PA', N'Panama', N'es-PA', N'America/Panama'),
    (N'PE', N'Peru', N'es-PE', N'America/Lima'),
    (N'PF', N'French Polynesia', N'fr-PF', N'Pacific/Tahiti'),
    (N'PG', N'Papua New Guinea', N'en-PG', N'Pacific/Port_Moresby'),
    (N'PH', N'Philippines', N'en-PH', N'Asia/Manila'),
    (N'PK', N'Pakistan', N'ur-PK', N'Asia/Karachi'),
    (N'PL', N'Poland', N'pl-PL', N'Europe/Warsaw'),
    (N'PM', N'St Pierre & Miquelon', N'fr-PM', N'America/Miquelon'),
    (N'PN', N'Pitcairn', N'en-PN', N'Pacific/Pitcairn'),
    (N'PR', N'Puerto Rico', N'es-PR', N'America/Puerto_Rico'),
    (N'PS', N'Palestine', N'ar-PS', N'Asia/Gaza'),
    (N'PT', N'Portugal', N'pt-PT', N'Europe/Lisbon'),
    (N'PW', N'Palau', N'en-PW', N'Pacific/Palau'),
    (N'PY', N'Paraguay', N'gn-PY', N'America/Asuncion'),
    (N'QA', N'Qatar', N'ar-QA', N'Asia/Qatar'),
    (N'RE', N'Réunion', N'fr-RE', N'Indian/Reunion'),
    (N'RO', N'Romania', N'ro-RO', N'Europe/Bucharest'),
    (N'RS', N'Serbia', N'sr-Cyrl-RS', N'Europe/Belgrade'),
    (N'RU', N'Russia', N'ru-RU', N'Europe/Moscow'),
    (N'RW', N'Rwanda', N'rw-RW', N'Africa/Kigali'),
    (N'SA', N'Saudi Arabia', N'ar-SA', N'Asia/Riyadh'),
    (N'SB', N'Solomon Islands', N'en-SB', N'Pacific/Guadalcanal'),
    (N'SC', N'Seychelles', N'fr-SC', N'Indian/Mahe'),
    (N'SD', N'Sudan', N'ar-SD', N'Africa/Khartoum'),
    (N'SE', N'Sweden', N'sv-SE', N'Europe/Stockholm'),
    (N'SG', N'Singapore', N'en-SG', N'Asia/Singapore'),
    (N'SH', N'St Helena', N'en-SH', N'Atlantic/St_Helena'),
    (N'SI', N'Slovenia', N'sl-SI', N'Europe/Ljubljana')
) AS s (Code, Name, LikelyLocale, DefaultTimeZone)
ON t.Code = s.Code
WHEN MATCHED AND (ISNULL(CAST(t.Name AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.Name AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.LikelyLocale AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.LikelyLocale AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.DefaultTimeZone AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.DefaultTimeZone AS NVARCHAR(200)), N''))
    THEN UPDATE SET Name = s.Name, LikelyLocale = s.LikelyLocale, DefaultTimeZone = s.DefaultTimeZone
WHEN NOT MATCHED THEN INSERT (Code, Name, LikelyLocale, DefaultTimeZone) VALUES (s.Code, s.Name, s.LikelyLocale, s.DefaultTimeZone);
PRINT '  [=] Countries: rows 1–200 merged.';

MERGE Countries AS t
USING (VALUES
    (N'SJ', N'Svalbard & Jan Mayen', N'nb-SJ', N'Arctic/Longyearbyen'),
    (N'SK', N'Slovakia', N'sk-SK', N'Europe/Bratislava'),
    (N'SL', N'Sierra Leone', N'en-SL', N'Africa/Freetown'),
    (N'SM', N'San Marino', N'it-SM', N'Europe/San_Marino'),
    (N'SN', N'Senegal', N'wo-SN', N'Africa/Dakar'),
    (N'SO', N'Somalia', N'so-SO', N'Africa/Mogadishu'),
    (N'SR', N'Suriname', N'nl-SR', N'America/Paramaribo'),
    (N'SS', N'South Sudan', N'ar-SS', N'Africa/Juba'),
    (N'ST', N'Sao Tome & Principe', N'pt-ST', N'Africa/Sao_Tome'),
    (N'SV', N'El Salvador', N'es-SV', N'America/El_Salvador'),
    (N'SX', N'St Maarten (Dutch)', N'en-SX', N'America/Lower_Princes'),
    (N'SY', N'Syria', N'ar-SY', N'Asia/Damascus'),
    (N'SZ', N'Eswatini (Swaziland)', N'en-SZ', N'Africa/Mbabane'),
    (N'TC', N'Turks & Caicos Is', N'en-TC', N'America/Grand_Turk'),
    (N'TD', N'Chad', N'ar-TD', N'Africa/Ndjamena'),
    (N'TF', N'French S. Terr.', NULL, N'Indian/Kerguelen'),
    (N'TG', N'Togo', N'fr-TG', N'Africa/Lome'),
    (N'TH', N'Thailand', N'th-TH', N'Asia/Bangkok'),
    (N'TJ', N'Tajikistan', N'tg-TJ', N'Asia/Dushanbe'),
    (N'TK', N'Tokelau', N'en-TK', N'Pacific/Fakaofo'),
    (N'TL', N'East Timor', N'pt-TL', N'Asia/Dili'),
    (N'TM', N'Turkmenistan', N'tk-TM', N'Asia/Ashgabat'),
    (N'TN', N'Tunisia', N'ar-TN', N'Africa/Tunis'),
    (N'TO', N'Tonga', N'to-TO', N'Pacific/Tongatapu'),
    (N'TR', N'Turkey', N'tr-TR', N'Europe/Istanbul'),
    (N'TT', N'Trinidad & Tobago', N'en-TT', N'America/Port_of_Spain'),
    (N'TV', N'Tuvalu', N'en-TV', N'Pacific/Funafuti'),
    (N'TW', N'Taiwan', N'zh-Hant-TW', N'Asia/Taipei'),
    (N'TZ', N'Tanzania', N'sw-TZ', N'Africa/Dar_es_Salaam'),
    (N'UA', N'Ukraine', N'uk-UA', N'Europe/Kyiv'),
    (N'UG', N'Uganda', N'sw-UG', N'Africa/Kampala'),
    (N'UM', N'US minor outlying islands', N'en-UM', N'Pacific/Midway'),
    (N'US', N'United States', N'en-US', N'America/New_York'),
    (N'UY', N'Uruguay', N'es-UY', N'America/Montevideo'),
    (N'UZ', N'Uzbekistan', N'uz-Latn-UZ', N'Asia/Tashkent'),
    (N'VA', N'Vatican City', N'it-VA', N'Europe/Vatican'),
    (N'VC', N'St Vincent', N'en-VC', N'America/St_Vincent'),
    (N'VE', N'Venezuela', N'es-VE', N'America/Caracas'),
    (N'VG', N'Virgin Islands (UK)', N'en-VG', N'America/Tortola'),
    (N'VI', N'Virgin Islands (US)', N'en-VI', N'America/St_Thomas'),
    (N'VN', N'Vietnam', N'vi-VN', N'Asia/Ho_Chi_Minh'),
    (N'VU', N'Vanuatu', N'en-VU', N'Pacific/Efate'),
    (N'WF', N'Wallis & Futuna', N'fr-WF', N'Pacific/Wallis'),
    (N'WS', N'Samoa (western)', N'en-WS', N'Pacific/Apia'),
    (N'YE', N'Yemen', N'ar-YE', N'Asia/Aden'),
    (N'YT', N'Mayotte', N'fr-YT', N'Indian/Mayotte'),
    (N'ZA', N'South Africa', N'en-ZA', N'Africa/Johannesburg'),
    (N'ZM', N'Zambia', N'bem-ZM', N'Africa/Lusaka'),
    (N'ZW', N'Zimbabwe', N'sn-ZW', N'Africa/Harare')
) AS s (Code, Name, LikelyLocale, DefaultTimeZone)
ON t.Code = s.Code
WHEN MATCHED AND (ISNULL(CAST(t.Name AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.Name AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.LikelyLocale AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.LikelyLocale AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.DefaultTimeZone AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.DefaultTimeZone AS NVARCHAR(200)), N''))
    THEN UPDATE SET Name = s.Name, LikelyLocale = s.LikelyLocale, DefaultTimeZone = s.DefaultTimeZone
WHEN NOT MATCHED THEN INSERT (Code, Name, LikelyLocale, DefaultTimeZone) VALUES (s.Code, s.Name, s.LikelyLocale, s.DefaultTimeZone);
PRINT '  [=] Countries: rows 201–249 merged.';

MERGE TimeZones AS t
USING (VALUES
    (N'Europe/Andorra', N'AD', N'W. Europe Standard Time', 60, N'Andorra', NULL, N'(UTC+01:00) Andorra — Andorra'),
    (N'Asia/Dubai', N'AE', N'Arabian Standard Time', 240, N'Dubai', NULL, N'(UTC+04:00) United Arab Emirates — Dubai'),
    (N'Asia/Kabul', N'AF', N'Afghanistan Standard Time', 270, N'Kabul', NULL, N'(UTC+04:30) Afghanistan — Kabul'),
    (N'America/Antigua', N'AG', N'SA Western Standard Time', -240, N'Antigua', NULL, N'(UTC−04:00) Antigua & Barbuda — Antigua'),
    (N'America/Anguilla', N'AI', N'SA Western Standard Time', -240, N'Anguilla', NULL, N'(UTC−04:00) Anguilla — Anguilla'),
    (N'Europe/Tirane', N'AL', N'Central Europe Standard Time', 60, N'Tirane', NULL, N'(UTC+01:00) Albania — Tirane'),
    (N'Asia/Yerevan', N'AM', N'Caucasus Standard Time', 240, N'Yerevan', NULL, N'(UTC+04:00) Armenia — Yerevan'),
    (N'Africa/Luanda', N'AO', N'W. Central Africa Standard Time', 60, N'Luanda', NULL, N'(UTC+01:00) Angola — Luanda'),
    (N'Antarctica/McMurdo', N'AQ', N'New Zealand Standard Time', 720, N'McMurdo', N'New Zealand time - McMurdo, South Pole', N'(UTC+12:00) Antarctica — McMurdo (New Zealand time - McMurdo, South Pole)'),
    (N'Antarctica/Casey', N'AQ', N'Central Pacific Standard Time', 660, N'Casey', N'Casey', N'(UTC+11:00) Antarctica — Casey (Casey)'),
    (N'Antarctica/Davis', N'AQ', N'SE Asia Standard Time', 420, N'Davis', N'Davis', N'(UTC+07:00) Antarctica — Davis (Davis)'),
    (N'Antarctica/DumontDUrville', N'AQ', N'West Pacific Standard Time', 600, N'DumontDUrville', N'Dumont-d''Urville', N'(UTC+10:00) Antarctica — DumontDUrville (Dumont-d''Urville)'),
    (N'Antarctica/Mawson', N'AQ', N'West Asia Standard Time', 300, N'Mawson', N'Mawson', N'(UTC+05:00) Antarctica — Mawson (Mawson)'),
    (N'Antarctica/Palmer', N'AQ', N'SA Eastern Standard Time', -180, N'Palmer', N'Palmer', N'(UTC−03:00) Antarctica — Palmer (Palmer)'),
    (N'Antarctica/Rothera', N'AQ', N'SA Eastern Standard Time', -180, N'Rothera', N'Rothera', N'(UTC−03:00) Antarctica — Rothera (Rothera)'),
    (N'Antarctica/Syowa', N'AQ', N'E. Africa Standard Time', 180, N'Syowa', N'Syowa', N'(UTC+03:00) Antarctica — Syowa (Syowa)'),
    (N'Antarctica/Troll', N'AQ', NULL, NULL, N'Troll', N'Troll', N'Antarctica — Troll (Troll)'),
    (N'Antarctica/Vostok', N'AQ', N'Central Asia Standard Time', 360, N'Vostok', N'Vostok', N'(UTC+06:00) Antarctica — Vostok (Vostok)'),
    (N'America/Argentina/Buenos_Aires', N'AR', N'Argentina Standard Time', -180, N'Buenos Aires', N'Buenos Aires (BA, CF)', N'(UTC−03:00) Argentina — Buenos Aires (Buenos Aires (BA, CF))'),
    (N'America/Argentina/Cordoba', N'AR', N'Argentina Standard Time', -180, N'Cordoba', N'Argentina (most areas: CB, CC, CN, ER, FM, MN, SE, SF)', N'(UTC−03:00) Argentina — Cordoba (Argentina (most areas: CB, CC, CN, ER, FM, MN, SE, SF))'),
    (N'America/Argentina/Salta', N'AR', N'Argentina Standard Time', -180, N'Salta', N'Salta (SA, LP, NQ, RN)', N'(UTC−03:00) Argentina — Salta (Salta (SA, LP, NQ, RN))'),
    (N'America/Argentina/Jujuy', N'AR', N'Argentina Standard Time', -180, N'Jujuy', N'Jujuy (JY)', N'(UTC−03:00) Argentina — Jujuy (Jujuy (JY))'),
    (N'America/Argentina/Tucuman', N'AR', N'Argentina Standard Time', -180, N'Tucuman', N'Tucuman (TM)', N'(UTC−03:00) Argentina — Tucuman (Tucuman (TM))'),
    (N'America/Argentina/Catamarca', N'AR', N'Argentina Standard Time', -180, N'Catamarca', N'Catamarca (CT), Chubut (CH)', N'(UTC−03:00) Argentina — Catamarca (Catamarca (CT), Chubut (CH))'),
    (N'America/Argentina/La_Rioja', N'AR', N'Argentina Standard Time', -180, N'La Rioja', N'La Rioja (LR)', N'(UTC−03:00) Argentina — La Rioja (La Rioja (LR))'),
    (N'America/Argentina/San_Juan', N'AR', N'Argentina Standard Time', -180, N'San Juan', N'San Juan (SJ)', N'(UTC−03:00) Argentina — San Juan (San Juan (SJ))'),
    (N'America/Argentina/Mendoza', N'AR', N'Argentina Standard Time', -180, N'Mendoza', N'Mendoza (MZ)', N'(UTC−03:00) Argentina — Mendoza (Mendoza (MZ))'),
    (N'America/Argentina/San_Luis', N'AR', N'Argentina Standard Time', -180, N'San Luis', N'San Luis (SL)', N'(UTC−03:00) Argentina — San Luis (San Luis (SL))'),
    (N'America/Argentina/Rio_Gallegos', N'AR', N'Argentina Standard Time', -180, N'Rio Gallegos', N'Santa Cruz (SC)', N'(UTC−03:00) Argentina — Rio Gallegos (Santa Cruz (SC))'),
    (N'America/Argentina/Ushuaia', N'AR', N'Argentina Standard Time', -180, N'Ushuaia', N'Tierra del Fuego (TF)', N'(UTC−03:00) Argentina — Ushuaia (Tierra del Fuego (TF))'),
    (N'Pacific/Pago_Pago', N'AS', N'UTC-11', -660, N'Pago Pago', NULL, N'(UTC−11:00) Samoa (American) — Pago Pago'),
    (N'Europe/Vienna', N'AT', N'W. Europe Standard Time', 60, N'Vienna', NULL, N'(UTC+01:00) Austria — Vienna'),
    (N'Australia/Lord_Howe', N'AU', N'Lord Howe Standard Time', 630, N'Lord Howe', N'Lord Howe Island', N'(UTC+10:30) Australia — Lord Howe (Lord Howe Island)'),
    (N'Antarctica/Macquarie', N'AU', N'Tasmania Standard Time', 600, N'Macquarie', N'Macquarie Island', N'(UTC+10:00) Australia — Macquarie (Macquarie Island)'),
    (N'Australia/Hobart', N'AU', N'Tasmania Standard Time', 600, N'Hobart', N'Tasmania', N'(UTC+10:00) Australia — Hobart (Tasmania)'),
    (N'Australia/Melbourne', N'AU', N'AUS Eastern Standard Time', 600, N'Melbourne', N'Victoria', N'(UTC+10:00) Australia — Melbourne (Victoria)'),
    (N'Australia/Sydney', N'AU', N'AUS Eastern Standard Time', 600, N'Sydney', N'New South Wales (most areas)', N'(UTC+10:00) Australia — Sydney (New South Wales (most areas))'),
    (N'Australia/Broken_Hill', N'AU', N'Cen. Australia Standard Time', 570, N'Broken Hill', N'New South Wales (Yancowinna)', N'(UTC+09:30) Australia — Broken Hill (New South Wales (Yancowinna))'),
    (N'Australia/Brisbane', N'AU', N'E. Australia Standard Time', 600, N'Brisbane', N'Queensland (most areas)', N'(UTC+10:00) Australia — Brisbane (Queensland (most areas))'),
    (N'Australia/Lindeman', N'AU', N'E. Australia Standard Time', 600, N'Lindeman', N'Queensland (Whitsunday Islands)', N'(UTC+10:00) Australia — Lindeman (Queensland (Whitsunday Islands))'),
    (N'Australia/Adelaide', N'AU', N'Cen. Australia Standard Time', 570, N'Adelaide', N'South Australia', N'(UTC+09:30) Australia — Adelaide (South Australia)'),
    (N'Australia/Darwin', N'AU', N'AUS Central Standard Time', 570, N'Darwin', N'Northern Territory', N'(UTC+09:30) Australia — Darwin (Northern Territory)'),
    (N'Australia/Perth', N'AU', N'W. Australia Standard Time', 480, N'Perth', N'Western Australia (most areas)', N'(UTC+08:00) Australia — Perth (Western Australia (most areas))'),
    (N'Australia/Eucla', N'AU', N'Aus Central W. Standard Time', 525, N'Eucla', N'Western Australia (Eucla)', N'(UTC+08:45) Australia — Eucla (Western Australia (Eucla))'),
    (N'America/Aruba', N'AW', N'SA Western Standard Time', -240, N'Aruba', NULL, N'(UTC−04:00) Aruba — Aruba'),
    (N'Europe/Mariehamn', N'AX', N'FLE Standard Time', 120, N'Mariehamn', NULL, N'(UTC+02:00) Åland Islands — Mariehamn'),
    (N'Asia/Baku', N'AZ', N'Azerbaijan Standard Time', 240, N'Baku', NULL, N'(UTC+04:00) Azerbaijan — Baku'),
    (N'Europe/Sarajevo', N'BA', N'Central European Standard Time', 60, N'Sarajevo', NULL, N'(UTC+01:00) Bosnia & Herzegovina — Sarajevo'),
    (N'America/Barbados', N'BB', N'SA Western Standard Time', -240, N'Barbados', NULL, N'(UTC−04:00) Barbados — Barbados'),
    (N'Asia/Dhaka', N'BD', N'Bangladesh Standard Time', 360, N'Dhaka', NULL, N'(UTC+06:00) Bangladesh — Dhaka'),
    (N'Europe/Brussels', N'BE', N'Romance Standard Time', 60, N'Brussels', NULL, N'(UTC+01:00) Belgium — Brussels'),
    (N'Africa/Ouagadougou', N'BF', N'Greenwich Standard Time', 0, N'Ouagadougou', NULL, N'(UTC+00:00) Burkina Faso — Ouagadougou'),
    (N'Europe/Sofia', N'BG', N'FLE Standard Time', 120, N'Sofia', NULL, N'(UTC+02:00) Bulgaria — Sofia'),
    (N'Asia/Bahrain', N'BH', N'Arab Standard Time', 180, N'Bahrain', NULL, N'(UTC+03:00) Bahrain — Bahrain'),
    (N'Africa/Bujumbura', N'BI', N'South Africa Standard Time', 120, N'Bujumbura', NULL, N'(UTC+02:00) Burundi — Bujumbura'),
    (N'Africa/Porto-Novo', N'BJ', N'W. Central Africa Standard Time', 60, N'Porto-Novo', NULL, N'(UTC+01:00) Benin — Porto-Novo'),
    (N'America/St_Barthelemy', N'BL', N'SA Western Standard Time', -240, N'St Barthelemy', NULL, N'(UTC−04:00) St Barthelemy — St Barthelemy'),
    (N'Atlantic/Bermuda', N'BM', N'Atlantic Standard Time', -240, N'Bermuda', NULL, N'(UTC−04:00) Bermuda — Bermuda'),
    (N'Asia/Brunei', N'BN', N'Singapore Standard Time', 480, N'Brunei', NULL, N'(UTC+08:00) Brunei — Brunei'),
    (N'America/La_Paz', N'BO', N'SA Western Standard Time', -240, N'La Paz', NULL, N'(UTC−04:00) Bolivia — La Paz'),
    (N'America/Kralendijk', N'BQ', N'SA Western Standard Time', -240, N'Kralendijk', NULL, N'(UTC−04:00) Caribbean NL — Kralendijk'),
    (N'America/Noronha', N'BR', N'UTC-02', -120, N'Noronha', N'Atlantic islands', N'(UTC−02:00) Brazil — Noronha (Atlantic islands)'),
    (N'America/Belem', N'BR', N'SA Eastern Standard Time', -180, N'Belem', N'Para (east), Amapa', N'(UTC−03:00) Brazil — Belem (Para (east), Amapa)'),
    (N'America/Fortaleza', N'BR', N'SA Eastern Standard Time', -180, N'Fortaleza', N'Brazil (northeast: MA, PI, CE, RN, PB)', N'(UTC−03:00) Brazil — Fortaleza (Brazil (northeast: MA, PI, CE, RN, PB))'),
    (N'America/Recife', N'BR', N'SA Eastern Standard Time', -180, N'Recife', N'Pernambuco', N'(UTC−03:00) Brazil — Recife (Pernambuco)'),
    (N'America/Araguaina', N'BR', N'Tocantins Standard Time', -180, N'Araguaina', N'Tocantins', N'(UTC−03:00) Brazil — Araguaina (Tocantins)'),
    (N'America/Maceio', N'BR', N'SA Eastern Standard Time', -180, N'Maceio', N'Alagoas, Sergipe', N'(UTC−03:00) Brazil — Maceio (Alagoas, Sergipe)'),
    (N'America/Bahia', N'BR', N'Bahia Standard Time', -180, N'Bahia', N'Bahia', N'(UTC−03:00) Brazil — Bahia (Bahia)'),
    (N'America/Sao_Paulo', N'BR', N'E. South America Standard Time', -180, N'Sao Paulo', N'Brazil (southeast: GO, DF, MG, ES, RJ, SP, PR, SC, RS)', N'(UTC−03:00) Brazil — Sao Paulo (Brazil (southeast: GO, DF, MG, ES, RJ, SP, PR, SC, RS))'),
    (N'America/Campo_Grande', N'BR', N'Central Brazilian Standard Time', -240, N'Campo Grande', N'Mato Grosso do Sul', N'(UTC−04:00) Brazil — Campo Grande (Mato Grosso do Sul)'),
    (N'America/Cuiaba', N'BR', N'Central Brazilian Standard Time', -240, N'Cuiaba', N'Mato Grosso', N'(UTC−04:00) Brazil — Cuiaba (Mato Grosso)'),
    (N'America/Santarem', N'BR', N'SA Eastern Standard Time', -180, N'Santarem', N'Para (west)', N'(UTC−03:00) Brazil — Santarem (Para (west))'),
    (N'America/Porto_Velho', N'BR', N'SA Western Standard Time', -240, N'Porto Velho', N'Rondonia', N'(UTC−04:00) Brazil — Porto Velho (Rondonia)'),
    (N'America/Boa_Vista', N'BR', N'SA Western Standard Time', -240, N'Boa Vista', N'Roraima', N'(UTC−04:00) Brazil — Boa Vista (Roraima)'),
    (N'America/Manaus', N'BR', N'SA Western Standard Time', -240, N'Manaus', N'Amazonas (east)', N'(UTC−04:00) Brazil — Manaus (Amazonas (east))'),
    (N'America/Eirunepe', N'BR', N'SA Pacific Standard Time', -300, N'Eirunepe', N'Amazonas (west)', N'(UTC−05:00) Brazil — Eirunepe (Amazonas (west))'),
    (N'America/Rio_Branco', N'BR', N'SA Pacific Standard Time', -300, N'Rio Branco', N'Acre', N'(UTC−05:00) Brazil — Rio Branco (Acre)'),
    (N'America/Nassau', N'BS', N'Eastern Standard Time', -300, N'Nassau', NULL, N'(UTC−05:00) Bahamas — Nassau'),
    (N'Asia/Thimphu', N'BT', N'Bangladesh Standard Time', 360, N'Thimphu', NULL, N'(UTC+06:00) Bhutan — Thimphu'),
    (N'Africa/Gaborone', N'BW', N'South Africa Standard Time', 120, N'Gaborone', NULL, N'(UTC+02:00) Botswana — Gaborone'),
    (N'Europe/Minsk', N'BY', N'Belarus Standard Time', 180, N'Minsk', NULL, N'(UTC+03:00) Belarus — Minsk'),
    (N'America/Belize', N'BZ', N'Central America Standard Time', -360, N'Belize', NULL, N'(UTC−06:00) Belize — Belize'),
    (N'America/St_Johns', N'CA', N'Newfoundland Standard Time', -210, N'St Johns', N'Newfoundland, Labrador (SE)', N'(UTC−03:30) Canada — St Johns (Newfoundland, Labrador (SE))'),
    (N'America/Halifax', N'CA', N'Atlantic Standard Time', -240, N'Halifax', N'Atlantic - NS (most areas), PE', N'(UTC−04:00) Canada — Halifax (Atlantic - NS (most areas), PE)'),
    (N'America/Glace_Bay', N'CA', N'Atlantic Standard Time', -240, N'Glace Bay', N'Atlantic - NS (Cape Breton)', N'(UTC−04:00) Canada — Glace Bay (Atlantic - NS (Cape Breton))'),
    (N'America/Moncton', N'CA', N'Atlantic Standard Time', -240, N'Moncton', N'Atlantic - New Brunswick', N'(UTC−04:00) Canada — Moncton (Atlantic - New Brunswick)'),
    (N'America/Goose_Bay', N'CA', N'Atlantic Standard Time', -240, N'Goose Bay', N'Atlantic - Labrador (most areas)', N'(UTC−04:00) Canada — Goose Bay (Atlantic - Labrador (most areas))'),
    (N'America/Blanc-Sablon', N'CA', N'SA Western Standard Time', -240, N'Blanc-Sablon', N'AST - QC (Lower North Shore)', N'(UTC−04:00) Canada — Blanc-Sablon (AST - QC (Lower North Shore))'),
    (N'America/Toronto', N'CA', N'Eastern Standard Time', -300, N'Toronto', N'Eastern - ON & QC (most areas)', N'(UTC−05:00) Canada — Toronto (Eastern - ON & QC (most areas))'),
    (N'America/Iqaluit', N'CA', N'Eastern Standard Time', -300, N'Iqaluit', N'Eastern - NU (most areas)', N'(UTC−05:00) Canada — Iqaluit (Eastern - NU (most areas))'),
    (N'America/Atikokan', N'CA', N'SA Pacific Standard Time', -300, N'Atikokan', N'EST - ON (Atikokan), NU (Coral H)', N'(UTC−05:00) Canada — Atikokan (EST - ON (Atikokan), NU (Coral H))'),
    (N'America/Winnipeg', N'CA', N'Central Standard Time', -360, N'Winnipeg', N'EST - Manitoba, ON (northwest)', N'(UTC−06:00) Canada — Winnipeg (EST - Manitoba, ON (northwest))'),
    (N'America/Resolute', N'CA', N'Central Standard Time', -360, N'Resolute', N'Central - NU (Resolute)', N'(UTC−06:00) Canada — Resolute (Central - NU (Resolute))'),
    (N'America/Rankin_Inlet', N'CA', N'Central Standard Time', -360, N'Rankin Inlet', N'Central - NU (central)', N'(UTC−06:00) Canada — Rankin Inlet (Central - NU (central))'),
    (N'America/Regina', N'CA', N'Canada Central Standard Time', -360, N'Regina', N'CST - SK (most areas)', N'(UTC−06:00) Canada — Regina (CST - SK (most areas))'),
    (N'America/Swift_Current', N'CA', N'Canada Central Standard Time', -360, N'Swift Current', N'CST - SK (midwest)', N'(UTC−06:00) Canada — Swift Current (CST - SK (midwest))'),
    (N'America/Edmonton', N'CA', N'Mountain Standard Time', -420, N'Edmonton', N'CST - AB, BC(E), NT(E), SK(W)', N'(UTC−07:00) Canada — Edmonton (CST - AB, BC(E), NT(E), SK(W))'),
    (N'America/Cambridge_Bay', N'CA', N'Mountain Standard Time', -420, N'Cambridge Bay', N'Mountain - NU (west)', N'(UTC−07:00) Canada — Cambridge Bay (Mountain - NU (west))'),
    (N'America/Inuvik', N'CA', N'Mountain Standard Time', -420, N'Inuvik', N'CST - NT (west)', N'(UTC−07:00) Canada — Inuvik (CST - NT (west))'),
    (N'America/Vancouver', N'CA', N'Pacific Standard Time', -480, N'Vancouver', N'MST - BC (most areas)', N'(UTC−08:00) Canada — Vancouver (MST - BC (most areas))'),
    (N'America/Creston', N'CA', N'US Mountain Standard Time', -420, N'Creston', N'MST - BC (Creston)', N'(UTC−07:00) Canada — Creston (MST - BC (Creston))'),
    (N'America/Dawson_Creek', N'CA', N'US Mountain Standard Time', -420, N'Dawson Creek', N'MST - BC (Dawson Cr, Ft St John)', N'(UTC−07:00) Canada — Dawson Creek (MST - BC (Dawson Cr, Ft St John))'),
    (N'America/Fort_Nelson', N'CA', N'US Mountain Standard Time', -420, N'Fort Nelson', N'MST - BC (Ft Nelson)', N'(UTC−07:00) Canada — Fort Nelson (MST - BC (Ft Nelson))'),
    (N'America/Whitehorse', N'CA', N'Yukon Standard Time', -420, N'Whitehorse', N'MST - Yukon (east)', N'(UTC−07:00) Canada — Whitehorse (MST - Yukon (east))'),
    (N'America/Dawson', N'CA', N'Yukon Standard Time', -420, N'Dawson', N'MST - Yukon (west)', N'(UTC−07:00) Canada — Dawson (MST - Yukon (west))'),
    (N'Indian/Cocos', N'CC', N'Myanmar Standard Time', 390, N'Cocos', NULL, N'(UTC+06:30) Cocos (Keeling) Islands — Cocos'),
    (N'Africa/Kinshasa', N'CD', N'W. Central Africa Standard Time', 60, N'Kinshasa', N'Dem. Rep. of Congo (west)', N'(UTC+01:00) Congo (Dem. Rep.) — Kinshasa (Dem. Rep. of Congo (west))'),
    (N'Africa/Lubumbashi', N'CD', N'South Africa Standard Time', 120, N'Lubumbashi', N'Dem. Rep. of Congo (east)', N'(UTC+02:00) Congo (Dem. Rep.) — Lubumbashi (Dem. Rep. of Congo (east))'),
    (N'Africa/Bangui', N'CF', N'W. Central Africa Standard Time', 60, N'Bangui', NULL, N'(UTC+01:00) Central African Rep. — Bangui'),
    (N'Africa/Brazzaville', N'CG', N'W. Central Africa Standard Time', 60, N'Brazzaville', NULL, N'(UTC+01:00) Congo (Rep.) — Brazzaville'),
    (N'Europe/Zurich', N'CH', N'W. Europe Standard Time', 60, N'Zurich', NULL, N'(UTC+01:00) Switzerland — Zurich'),
    (N'Africa/Abidjan', N'CI', N'Greenwich Standard Time', 0, N'Abidjan', NULL, N'(UTC+00:00) Côte d’Ivoire — Abidjan'),
    (N'Pacific/Rarotonga', N'CK', N'Hawaiian Standard Time', -600, N'Rarotonga', NULL, N'(UTC−10:00) Cook Islands — Rarotonga'),
    (N'America/Santiago', N'CL', N'Pacific SA Standard Time', -240, N'Santiago', N'most of Chile', N'(UTC−04:00) Chile — Santiago (most of Chile)'),
    (N'America/Coyhaique', N'CL', NULL, NULL, N'Coyhaique', N'Aysen Region', N'Chile — Coyhaique (Aysen Region)'),
    (N'America/Punta_Arenas', N'CL', N'Magallanes Standard Time', -180, N'Punta Arenas', N'Magallanes Region', N'(UTC−03:00) Chile — Punta Arenas (Magallanes Region)'),
    (N'Pacific/Easter', N'CL', N'Easter Island Standard Time', -360, N'Easter', N'Easter Island', N'(UTC−06:00) Chile — Easter (Easter Island)'),
    (N'Africa/Douala', N'CM', N'W. Central Africa Standard Time', 60, N'Douala', NULL, N'(UTC+01:00) Cameroon — Douala'),
    (N'Asia/Shanghai', N'CN', N'China Standard Time', 480, N'Shanghai', N'Beijing Time', N'(UTC+08:00) China — Shanghai (Beijing Time)'),
    (N'Asia/Urumqi', N'CN', NULL, NULL, N'Urumqi', N'Xinjiang Time', N'China — Urumqi (Xinjiang Time)'),
    (N'America/Bogota', N'CO', N'SA Pacific Standard Time', -300, N'Bogota', NULL, N'(UTC−05:00) Colombia — Bogota'),
    (N'America/Costa_Rica', N'CR', N'Central America Standard Time', -360, N'Costa Rica', NULL, N'(UTC−06:00) Costa Rica — Costa Rica'),
    (N'America/Havana', N'CU', N'Cuba Standard Time', -300, N'Havana', NULL, N'(UTC−05:00) Cuba — Havana'),
    (N'Atlantic/Cape_Verde', N'CV', N'Cape Verde Standard Time', -60, N'Cape Verde', NULL, N'(UTC−01:00) Cape Verde — Cape Verde'),
    (N'America/Curacao', N'CW', N'SA Western Standard Time', -240, N'Curacao', NULL, N'(UTC−04:00) Curaçao — Curacao'),
    (N'Indian/Christmas', N'CX', N'SE Asia Standard Time', 420, N'Christmas', NULL, N'(UTC+07:00) Christmas Island — Christmas'),
    (N'Asia/Nicosia', N'CY', N'GTB Standard Time', 120, N'Nicosia', N'most of Cyprus', N'(UTC+02:00) Cyprus — Nicosia (most of Cyprus)'),
    (N'Asia/Famagusta', N'CY', N'GTB Standard Time', 120, N'Famagusta', N'Northern Cyprus', N'(UTC+02:00) Cyprus — Famagusta (Northern Cyprus)'),
    (N'Europe/Prague', N'CZ', N'Central Europe Standard Time', 60, N'Prague', NULL, N'(UTC+01:00) Czech Republic — Prague'),
    (N'Europe/Berlin', N'DE', N'W. Europe Standard Time', 60, N'Berlin', N'most of Germany', N'(UTC+01:00) Germany — Berlin (most of Germany)'),
    (N'Europe/Busingen', N'DE', N'W. Europe Standard Time', 60, N'Busingen', N'Busingen', N'(UTC+01:00) Germany — Busingen (Busingen)'),
    (N'Africa/Djibouti', N'DJ', N'E. Africa Standard Time', 180, N'Djibouti', NULL, N'(UTC+03:00) Djibouti — Djibouti'),
    (N'Europe/Copenhagen', N'DK', N'Romance Standard Time', 60, N'Copenhagen', NULL, N'(UTC+01:00) Denmark — Copenhagen'),
    (N'America/Dominica', N'DM', N'SA Western Standard Time', -240, N'Dominica', NULL, N'(UTC−04:00) Dominica — Dominica'),
    (N'America/Santo_Domingo', N'DO', N'SA Western Standard Time', -240, N'Santo Domingo', NULL, N'(UTC−04:00) Dominican Republic — Santo Domingo'),
    (N'Africa/Algiers', N'DZ', N'W. Central Africa Standard Time', 60, N'Algiers', NULL, N'(UTC+01:00) Algeria — Algiers'),
    (N'America/Guayaquil', N'EC', N'SA Pacific Standard Time', -300, N'Guayaquil', N'Ecuador (mainland)', N'(UTC−05:00) Ecuador — Guayaquil (Ecuador (mainland))'),
    (N'Pacific/Galapagos', N'EC', N'Central America Standard Time', -360, N'Galapagos', N'Galapagos Islands', N'(UTC−06:00) Ecuador — Galapagos (Galapagos Islands)'),
    (N'Europe/Tallinn', N'EE', N'FLE Standard Time', 120, N'Tallinn', NULL, N'(UTC+02:00) Estonia — Tallinn'),
    (N'Africa/Cairo', N'EG', N'Egypt Standard Time', 120, N'Cairo', NULL, N'(UTC+02:00) Egypt — Cairo'),
    (N'Africa/El_Aaiun', N'EH', N'Morocco Standard Time', 0, N'El Aaiun', NULL, N'(UTC+00:00) Western Sahara — El Aaiun'),
    (N'Africa/Asmara', N'ER', N'E. Africa Standard Time', 180, N'Asmara', NULL, N'(UTC+03:00) Eritrea — Asmara'),
    (N'Europe/Madrid', N'ES', N'Romance Standard Time', 60, N'Madrid', N'Spain (mainland)', N'(UTC+01:00) Spain — Madrid (Spain (mainland))'),
    (N'Africa/Ceuta', N'ES', N'Romance Standard Time', 60, N'Ceuta', N'Ceuta, Melilla', N'(UTC+01:00) Spain — Ceuta (Ceuta, Melilla)'),
    (N'Atlantic/Canary', N'ES', N'GMT Standard Time', 0, N'Canary', N'Canary Islands', N'(UTC+00:00) Spain — Canary (Canary Islands)'),
    (N'Africa/Addis_Ababa', N'ET', N'E. Africa Standard Time', 180, N'Addis Ababa', NULL, N'(UTC+03:00) Ethiopia — Addis Ababa'),
    (N'Europe/Helsinki', N'FI', N'FLE Standard Time', 120, N'Helsinki', NULL, N'(UTC+02:00) Finland — Helsinki'),
    (N'Pacific/Fiji', N'FJ', N'Fiji Standard Time', 720, N'Fiji', NULL, N'(UTC+12:00) Fiji — Fiji'),
    (N'Atlantic/Stanley', N'FK', N'SA Eastern Standard Time', -180, N'Stanley', NULL, N'(UTC−03:00) Falkland Islands — Stanley'),
    (N'Pacific/Chuuk', N'FM', N'West Pacific Standard Time', 600, N'Chuuk', N'Chuuk/Truk, Yap', N'(UTC+10:00) Micronesia — Chuuk (Chuuk/Truk, Yap)'),
    (N'Pacific/Pohnpei', N'FM', N'Central Pacific Standard Time', 660, N'Pohnpei', N'Pohnpei/Ponape', N'(UTC+11:00) Micronesia — Pohnpei (Pohnpei/Ponape)'),
    (N'Pacific/Kosrae', N'FM', N'Central Pacific Standard Time', 660, N'Kosrae', N'Kosrae', N'(UTC+11:00) Micronesia — Kosrae (Kosrae)'),
    (N'Atlantic/Faroe', N'FO', N'GMT Standard Time', 0, N'Faroe', NULL, N'(UTC+00:00) Faroe Islands — Faroe'),
    (N'Europe/Paris', N'FR', N'Romance Standard Time', 60, N'Paris', NULL, N'(UTC+01:00) France — Paris'),
    (N'Africa/Libreville', N'GA', N'W. Central Africa Standard Time', 60, N'Libreville', NULL, N'(UTC+01:00) Gabon — Libreville'),
    (N'Europe/London', N'GB', N'GMT Standard Time', 0, N'London', NULL, N'(UTC+00:00) Britain (UK) — London'),
    (N'America/Grenada', N'GD', N'SA Western Standard Time', -240, N'Grenada', NULL, N'(UTC−04:00) Grenada — Grenada'),
    (N'Asia/Tbilisi', N'GE', N'Georgian Standard Time', 240, N'Tbilisi', NULL, N'(UTC+04:00) Georgia — Tbilisi'),
    (N'America/Cayenne', N'GF', N'SA Eastern Standard Time', -180, N'Cayenne', NULL, N'(UTC−03:00) French Guiana — Cayenne'),
    (N'Europe/Guernsey', N'GG', N'GMT Standard Time', 0, N'Guernsey', NULL, N'(UTC+00:00) Guernsey — Guernsey'),
    (N'Africa/Accra', N'GH', N'Greenwich Standard Time', 0, N'Accra', NULL, N'(UTC+00:00) Ghana — Accra'),
    (N'Europe/Gibraltar', N'GI', N'W. Europe Standard Time', 60, N'Gibraltar', NULL, N'(UTC+01:00) Gibraltar — Gibraltar'),
    (N'America/Nuuk', N'GL', N'Greenland Standard Time', -120, N'Nuuk', N'most of Greenland', N'(UTC−02:00) Greenland — Nuuk (most of Greenland)'),
    (N'America/Danmarkshavn', N'GL', N'Greenwich Standard Time', 0, N'Danmarkshavn', N'National Park (east coast)', N'(UTC+00:00) Greenland — Danmarkshavn (National Park (east coast))'),
    (N'America/Scoresbysund', N'GL', N'Azores Standard Time', -60, N'Scoresbysund', N'Scoresbysund/Ittoqqortoormiit', N'(UTC−01:00) Greenland — Scoresbysund (Scoresbysund/Ittoqqortoormiit)'),
    (N'America/Thule', N'GL', N'Atlantic Standard Time', -240, N'Thule', N'Thule/Pituffik', N'(UTC−04:00) Greenland — Thule (Thule/Pituffik)'),
    (N'Africa/Banjul', N'GM', N'Greenwich Standard Time', 0, N'Banjul', NULL, N'(UTC+00:00) Gambia — Banjul'),
    (N'Africa/Conakry', N'GN', N'Greenwich Standard Time', 0, N'Conakry', NULL, N'(UTC+00:00) Guinea — Conakry'),
    (N'America/Guadeloupe', N'GP', N'SA Western Standard Time', -240, N'Guadeloupe', NULL, N'(UTC−04:00) Guadeloupe — Guadeloupe'),
    (N'Africa/Malabo', N'GQ', N'W. Central Africa Standard Time', 60, N'Malabo', NULL, N'(UTC+01:00) Equatorial Guinea — Malabo'),
    (N'Europe/Athens', N'GR', N'GTB Standard Time', 120, N'Athens', NULL, N'(UTC+02:00) Greece — Athens'),
    (N'Atlantic/South_Georgia', N'GS', N'UTC-02', -120, N'South Georgia', NULL, N'(UTC−02:00) South Georgia & the South Sandwich Islands — South Georgia'),
    (N'America/Guatemala', N'GT', N'Central America Standard Time', -360, N'Guatemala', NULL, N'(UTC−06:00) Guatemala — Guatemala'),
    (N'Pacific/Guam', N'GU', N'West Pacific Standard Time', 600, N'Guam', NULL, N'(UTC+10:00) Guam — Guam'),
    (N'Africa/Bissau', N'GW', N'Greenwich Standard Time', 0, N'Bissau', NULL, N'(UTC+00:00) Guinea-Bissau — Bissau'),
    (N'America/Guyana', N'GY', N'SA Western Standard Time', -240, N'Guyana', NULL, N'(UTC−04:00) Guyana — Guyana'),
    (N'Asia/Hong_Kong', N'HK', N'China Standard Time', 480, N'Hong Kong', NULL, N'(UTC+08:00) Hong Kong — Hong Kong'),
    (N'America/Tegucigalpa', N'HN', N'Central America Standard Time', -360, N'Tegucigalpa', NULL, N'(UTC−06:00) Honduras — Tegucigalpa'),
    (N'Europe/Zagreb', N'HR', N'Central European Standard Time', 60, N'Zagreb', NULL, N'(UTC+01:00) Croatia — Zagreb'),
    (N'America/Port-au-Prince', N'HT', N'Haiti Standard Time', -300, N'Port-au-Prince', NULL, N'(UTC−05:00) Haiti — Port-au-Prince'),
    (N'Europe/Budapest', N'HU', N'Central Europe Standard Time', 60, N'Budapest', NULL, N'(UTC+01:00) Hungary — Budapest'),
    (N'Asia/Jakarta', N'ID', N'SE Asia Standard Time', 420, N'Jakarta', N'Java, Sumatra', N'(UTC+07:00) Indonesia — Jakarta (Java, Sumatra)'),
    (N'Asia/Pontianak', N'ID', N'SE Asia Standard Time', 420, N'Pontianak', N'Borneo (west, central)', N'(UTC+07:00) Indonesia — Pontianak (Borneo (west, central))'),
    (N'Asia/Makassar', N'ID', N'Singapore Standard Time', 480, N'Makassar', N'Borneo (east, south), Sulawesi/Celebes, Bali, Nusa Tengarra, Timor (west)', N'(UTC+08:00) Indonesia — Makassar (Borneo (east, south), Sulawesi/Celebes, Bali, Nusa Tengarra, Timor (west))'),
    (N'Asia/Jayapura', N'ID', N'Tokyo Standard Time', 540, N'Jayapura', N'New Guinea (West Papua / Irian Jaya), Malukus/Moluccas', N'(UTC+09:00) Indonesia — Jayapura (New Guinea (West Papua / Irian Jaya), Malukus/Moluccas)'),
    (N'Europe/Dublin', N'IE', N'GMT Standard Time', 0, N'Dublin', NULL, N'(UTC+00:00) Ireland — Dublin'),
    (N'Asia/Jerusalem', N'IL', N'Israel Standard Time', 120, N'Jerusalem', NULL, N'(UTC+02:00) Israel — Jerusalem'),
    (N'Europe/Isle_of_Man', N'IM', N'GMT Standard Time', 0, N'Isle of Man', NULL, N'(UTC+00:00) Isle of Man — Isle of Man'),
    (N'Asia/Kolkata', N'IN', N'India Standard Time', 330, N'Kolkata', NULL, N'(UTC+05:30) India — Kolkata'),
    (N'Indian/Chagos', N'IO', N'Central Asia Standard Time', 360, N'Chagos', NULL, N'(UTC+06:00) British Indian Ocean Territory — Chagos'),
    (N'Asia/Baghdad', N'IQ', N'Arabic Standard Time', 180, N'Baghdad', NULL, N'(UTC+03:00) Iraq — Baghdad'),
    (N'Asia/Tehran', N'IR', N'Iran Standard Time', 210, N'Tehran', NULL, N'(UTC+03:30) Iran — Tehran'),
    (N'Atlantic/Reykjavik', N'IS', N'Greenwich Standard Time', 0, N'Reykjavik', NULL, N'(UTC+00:00) Iceland — Reykjavik'),
    (N'Europe/Rome', N'IT', N'W. Europe Standard Time', 60, N'Rome', NULL, N'(UTC+01:00) Italy — Rome'),
    (N'Europe/Jersey', N'JE', N'GMT Standard Time', 0, N'Jersey', NULL, N'(UTC+00:00) Jersey — Jersey'),
    (N'America/Jamaica', N'JM', N'SA Pacific Standard Time', -300, N'Jamaica', NULL, N'(UTC−05:00) Jamaica — Jamaica'),
    (N'Asia/Amman', N'JO', N'Jordan Standard Time', 180, N'Amman', NULL, N'(UTC+03:00) Jordan — Amman'),
    (N'Asia/Tokyo', N'JP', N'Tokyo Standard Time', 540, N'Tokyo', NULL, N'(UTC+09:00) Japan — Tokyo'),
    (N'Africa/Nairobi', N'KE', N'E. Africa Standard Time', 180, N'Nairobi', NULL, N'(UTC+03:00) Kenya — Nairobi'),
    (N'Asia/Bishkek', N'KG', N'Central Asia Standard Time', 360, N'Bishkek', NULL, N'(UTC+06:00) Kyrgyzstan — Bishkek')
) AS s (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName)
ON t.Id = s.Id
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.WindowsId AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.WindowsId AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.StandardOffsetMinutes AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.StandardOffsetMinutes AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.City AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.City AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.Comment AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.Comment AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.DisplayName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.DisplayName AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, WindowsId = s.WindowsId, StandardOffsetMinutes = s.StandardOffsetMinutes, City = s.City, Comment = s.Comment, DisplayName = s.DisplayName
WHEN NOT MATCHED THEN INSERT (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName) VALUES (s.Id, s.CountryCode, s.WindowsId, s.StandardOffsetMinutes, s.City, s.Comment, s.DisplayName);
PRINT '  [=] TimeZones: rows 1–200 merged.';

MERGE TimeZones AS t
USING (VALUES
    (N'Asia/Phnom_Penh', N'KH', N'SE Asia Standard Time', 420, N'Phnom Penh', NULL, N'(UTC+07:00) Cambodia — Phnom Penh'),
    (N'Pacific/Tarawa', N'KI', N'UTC+12', 720, N'Tarawa', N'Gilbert Islands', N'(UTC+12:00) Kiribati — Tarawa (Gilbert Islands)'),
    (N'Pacific/Kanton', N'KI', NULL, 780, N'Kanton', N'Phoenix Islands', N'(UTC+13:00) Kiribati — Kanton (Phoenix Islands)'),
    (N'Pacific/Kiritimati', N'KI', N'Line Islands Standard Time', 840, N'Kiritimati', N'Line Islands', N'(UTC+14:00) Kiribati — Kiritimati (Line Islands)'),
    (N'Indian/Comoro', N'KM', N'E. Africa Standard Time', 180, N'Comoro', NULL, N'(UTC+03:00) Comoros — Comoro'),
    (N'America/St_Kitts', N'KN', N'SA Western Standard Time', -240, N'St Kitts', NULL, N'(UTC−04:00) St Kitts & Nevis — St Kitts'),
    (N'Asia/Pyongyang', N'KP', N'North Korea Standard Time', 540, N'Pyongyang', NULL, N'(UTC+09:00) Korea (North) — Pyongyang'),
    (N'Asia/Seoul', N'KR', N'Korea Standard Time', 540, N'Seoul', NULL, N'(UTC+09:00) Korea (South) — Seoul'),
    (N'Asia/Kuwait', N'KW', N'Arab Standard Time', 180, N'Kuwait', NULL, N'(UTC+03:00) Kuwait — Kuwait'),
    (N'America/Cayman', N'KY', N'SA Pacific Standard Time', -300, N'Cayman', NULL, N'(UTC−05:00) Cayman Islands — Cayman'),
    (N'Asia/Almaty', N'KZ', N'Central Asia Standard Time', 360, N'Almaty', N'most of Kazakhstan', N'(UTC+06:00) Kazakhstan — Almaty (most of Kazakhstan)'),
    (N'Asia/Qyzylorda', N'KZ', N'Qyzylorda Standard Time', 300, N'Qyzylorda', N'Qyzylorda/Kyzylorda/Kzyl-Orda', N'(UTC+05:00) Kazakhstan — Qyzylorda (Qyzylorda/Kyzylorda/Kzyl-Orda)'),
    (N'Asia/Qostanay', N'KZ', N'Central Asia Standard Time', 360, N'Qostanay', N'Qostanay/Kostanay/Kustanay', N'(UTC+06:00) Kazakhstan — Qostanay (Qostanay/Kostanay/Kustanay)'),
    (N'Asia/Aqtobe', N'KZ', N'West Asia Standard Time', 300, N'Aqtobe', N'Aqtobe/Aktobe', N'(UTC+05:00) Kazakhstan — Aqtobe (Aqtobe/Aktobe)'),
    (N'Asia/Aqtau', N'KZ', N'West Asia Standard Time', 300, N'Aqtau', N'Mangghystau/Mankistau', N'(UTC+05:00) Kazakhstan — Aqtau (Mangghystau/Mankistau)'),
    (N'Asia/Atyrau', N'KZ', N'West Asia Standard Time', 300, N'Atyrau', N'Atyrau/Atirau/Gur''yev', N'(UTC+05:00) Kazakhstan — Atyrau (Atyrau/Atirau/Gur''yev)'),
    (N'Asia/Oral', N'KZ', N'West Asia Standard Time', 300, N'Oral', N'West Kazakhstan', N'(UTC+05:00) Kazakhstan — Oral (West Kazakhstan)'),
    (N'Asia/Vientiane', N'LA', N'SE Asia Standard Time', 420, N'Vientiane', NULL, N'(UTC+07:00) Laos — Vientiane'),
    (N'Asia/Beirut', N'LB', N'Middle East Standard Time', 120, N'Beirut', NULL, N'(UTC+02:00) Lebanon — Beirut'),
    (N'America/St_Lucia', N'LC', N'SA Western Standard Time', -240, N'St Lucia', NULL, N'(UTC−04:00) St Lucia — St Lucia'),
    (N'Europe/Vaduz', N'LI', N'W. Europe Standard Time', 60, N'Vaduz', NULL, N'(UTC+01:00) Liechtenstein — Vaduz'),
    (N'Asia/Colombo', N'LK', N'Sri Lanka Standard Time', 330, N'Colombo', NULL, N'(UTC+05:30) Sri Lanka — Colombo'),
    (N'Africa/Monrovia', N'LR', N'Greenwich Standard Time', 0, N'Monrovia', NULL, N'(UTC+00:00) Liberia — Monrovia'),
    (N'Africa/Maseru', N'LS', N'South Africa Standard Time', 120, N'Maseru', NULL, N'(UTC+02:00) Lesotho — Maseru'),
    (N'Europe/Vilnius', N'LT', N'FLE Standard Time', 120, N'Vilnius', NULL, N'(UTC+02:00) Lithuania — Vilnius'),
    (N'Europe/Luxembourg', N'LU', N'W. Europe Standard Time', 60, N'Luxembourg', NULL, N'(UTC+01:00) Luxembourg — Luxembourg'),
    (N'Europe/Riga', N'LV', N'FLE Standard Time', 120, N'Riga', NULL, N'(UTC+02:00) Latvia — Riga'),
    (N'Africa/Tripoli', N'LY', N'Libya Standard Time', 120, N'Tripoli', NULL, N'(UTC+02:00) Libya — Tripoli'),
    (N'Africa/Casablanca', N'MA', N'Morocco Standard Time', 0, N'Casablanca', NULL, N'(UTC+00:00) Morocco — Casablanca'),
    (N'Europe/Monaco', N'MC', N'W. Europe Standard Time', 60, N'Monaco', NULL, N'(UTC+01:00) Monaco — Monaco'),
    (N'Europe/Chisinau', N'MD', N'E. Europe Standard Time', 120, N'Chisinau', NULL, N'(UTC+02:00) Moldova — Chisinau'),
    (N'Europe/Podgorica', N'ME', N'Central Europe Standard Time', 60, N'Podgorica', NULL, N'(UTC+01:00) Montenegro — Podgorica'),
    (N'America/Marigot', N'MF', N'SA Western Standard Time', -240, N'Marigot', NULL, N'(UTC−04:00) St Martin (French) — Marigot'),
    (N'Indian/Antananarivo', N'MG', N'E. Africa Standard Time', 180, N'Antananarivo', NULL, N'(UTC+03:00) Madagascar — Antananarivo'),
    (N'Pacific/Majuro', N'MH', N'UTC+12', 720, N'Majuro', N'most of Marshall Islands', N'(UTC+12:00) Marshall Islands — Majuro (most of Marshall Islands)'),
    (N'Pacific/Kwajalein', N'MH', N'UTC+12', 720, N'Kwajalein', N'Kwajalein', N'(UTC+12:00) Marshall Islands — Kwajalein (Kwajalein)'),
    (N'Europe/Skopje', N'MK', N'Central European Standard Time', 60, N'Skopje', NULL, N'(UTC+01:00) North Macedonia — Skopje'),
    (N'Africa/Bamako', N'ML', N'Greenwich Standard Time', 0, N'Bamako', NULL, N'(UTC+00:00) Mali — Bamako'),
    (N'Asia/Yangon', N'MM', N'Myanmar Standard Time', 390, N'Yangon', NULL, N'(UTC+06:30) Myanmar (Burma) — Yangon'),
    (N'Asia/Ulaanbaatar', N'MN', N'Ulaanbaatar Standard Time', 480, N'Ulaanbaatar', N'most of Mongolia', N'(UTC+08:00) Mongolia — Ulaanbaatar (most of Mongolia)'),
    (N'Asia/Hovd', N'MN', N'W. Mongolia Standard Time', 420, N'Hovd', N'Bayan-Olgii, Hovd, Uvs', N'(UTC+07:00) Mongolia — Hovd (Bayan-Olgii, Hovd, Uvs)'),
    (N'Asia/Macau', N'MO', N'China Standard Time', 480, N'Macau', NULL, N'(UTC+08:00) Macau — Macau'),
    (N'Pacific/Saipan', N'MP', N'West Pacific Standard Time', 600, N'Saipan', NULL, N'(UTC+10:00) Northern Mariana Islands — Saipan'),
    (N'America/Martinique', N'MQ', N'SA Western Standard Time', -240, N'Martinique', NULL, N'(UTC−04:00) Martinique — Martinique'),
    (N'Africa/Nouakchott', N'MR', N'Greenwich Standard Time', 0, N'Nouakchott', NULL, N'(UTC+00:00) Mauritania — Nouakchott'),
    (N'America/Montserrat', N'MS', N'SA Western Standard Time', -240, N'Montserrat', NULL, N'(UTC−04:00) Montserrat — Montserrat'),
    (N'Europe/Malta', N'MT', N'W. Europe Standard Time', 60, N'Malta', NULL, N'(UTC+01:00) Malta — Malta'),
    (N'Indian/Mauritius', N'MU', N'Mauritius Standard Time', 240, N'Mauritius', NULL, N'(UTC+04:00) Mauritius — Mauritius'),
    (N'Indian/Maldives', N'MV', N'West Asia Standard Time', 300, N'Maldives', NULL, N'(UTC+05:00) Maldives — Maldives'),
    (N'Africa/Blantyre', N'MW', N'South Africa Standard Time', 120, N'Blantyre', NULL, N'(UTC+02:00) Malawi — Blantyre'),
    (N'America/Mexico_City', N'MX', N'Central Standard Time (Mexico)', -360, N'Mexico City', N'Central Mexico', N'(UTC−06:00) Mexico — Mexico City (Central Mexico)'),
    (N'America/Cancun', N'MX', N'Eastern Standard Time (Mexico)', -300, N'Cancun', N'Quintana Roo', N'(UTC−05:00) Mexico — Cancun (Quintana Roo)'),
    (N'America/Merida', N'MX', N'Central Standard Time (Mexico)', -360, N'Merida', N'Campeche, Yucatan', N'(UTC−06:00) Mexico — Merida (Campeche, Yucatan)'),
    (N'America/Monterrey', N'MX', N'Central Standard Time (Mexico)', -360, N'Monterrey', N'Durango; Coahuila, Nuevo Leon, Tamaulipas (most areas)', N'(UTC−06:00) Mexico — Monterrey (Durango; Coahuila, Nuevo Leon, Tamaulipas (most areas))'),
    (N'America/Matamoros', N'MX', N'Central Standard Time', -360, N'Matamoros', N'Coahuila, Nuevo Leon, Tamaulipas (US border)', N'(UTC−06:00) Mexico — Matamoros (Coahuila, Nuevo Leon, Tamaulipas (US border))'),
    (N'America/Chihuahua', N'MX', N'Mountain Standard Time (Mexico)', -420, N'Chihuahua', N'Chihuahua (most areas)', N'(UTC−07:00) Mexico — Chihuahua (Chihuahua (most areas))'),
    (N'America/Ciudad_Juarez', N'MX', NULL, NULL, N'Ciudad Juarez', N'Chihuahua (US border - west)', N'Mexico — Ciudad Juarez (Chihuahua (US border - west))'),
    (N'America/Ojinaga', N'MX', N'Mountain Standard Time', -420, N'Ojinaga', N'Chihuahua (US border - east)', N'(UTC−07:00) Mexico — Ojinaga (Chihuahua (US border - east))'),
    (N'America/Mazatlan', N'MX', N'Mountain Standard Time (Mexico)', -420, N'Mazatlan', N'Baja California Sur, Nayarit (most areas), Sinaloa', N'(UTC−07:00) Mexico — Mazatlan (Baja California Sur, Nayarit (most areas), Sinaloa)'),
    (N'America/Bahia_Banderas', N'MX', N'Central Standard Time (Mexico)', -360, N'Bahia Banderas', N'Bahia de Banderas', N'(UTC−06:00) Mexico — Bahia Banderas (Bahia de Banderas)'),
    (N'America/Hermosillo', N'MX', N'US Mountain Standard Time', -420, N'Hermosillo', N'Sonora', N'(UTC−07:00) Mexico — Hermosillo (Sonora)'),
    (N'America/Tijuana', N'MX', N'Pacific Standard Time (Mexico)', -480, N'Tijuana', N'Baja California', N'(UTC−08:00) Mexico — Tijuana (Baja California)'),
    (N'Asia/Kuala_Lumpur', N'MY', N'Singapore Standard Time', 480, N'Kuala Lumpur', N'Malaysia (peninsula)', N'(UTC+08:00) Malaysia — Kuala Lumpur (Malaysia (peninsula))'),
    (N'Asia/Kuching', N'MY', N'Singapore Standard Time', 480, N'Kuching', N'Sabah, Sarawak', N'(UTC+08:00) Malaysia — Kuching (Sabah, Sarawak)'),
    (N'Africa/Maputo', N'MZ', N'South Africa Standard Time', 120, N'Maputo', NULL, N'(UTC+02:00) Mozambique — Maputo'),
    (N'Africa/Windhoek', N'NA', N'Namibia Standard Time', 120, N'Windhoek', NULL, N'(UTC+02:00) Namibia — Windhoek'),
    (N'Pacific/Noumea', N'NC', N'Central Pacific Standard Time', 660, N'Noumea', NULL, N'(UTC+11:00) New Caledonia — Noumea'),
    (N'Africa/Niamey', N'NE', N'W. Central Africa Standard Time', 60, N'Niamey', NULL, N'(UTC+01:00) Niger — Niamey'),
    (N'Pacific/Norfolk', N'NF', N'Norfolk Standard Time', 660, N'Norfolk', NULL, N'(UTC+11:00) Norfolk Island — Norfolk'),
    (N'Africa/Lagos', N'NG', N'W. Central Africa Standard Time', 60, N'Lagos', NULL, N'(UTC+01:00) Nigeria — Lagos'),
    (N'America/Managua', N'NI', N'Central America Standard Time', -360, N'Managua', NULL, N'(UTC−06:00) Nicaragua — Managua'),
    (N'Europe/Amsterdam', N'NL', N'W. Europe Standard Time', 60, N'Amsterdam', NULL, N'(UTC+01:00) Netherlands — Amsterdam'),
    (N'Europe/Oslo', N'NO', N'W. Europe Standard Time', 60, N'Oslo', NULL, N'(UTC+01:00) Norway — Oslo'),
    (N'Asia/Kathmandu', N'NP', N'Nepal Standard Time', 345, N'Kathmandu', NULL, N'(UTC+05:45) Nepal — Kathmandu'),
    (N'Pacific/Nauru', N'NR', N'UTC+12', 720, N'Nauru', NULL, N'(UTC+12:00) Nauru — Nauru'),
    (N'Pacific/Niue', N'NU', N'UTC-11', -660, N'Niue', NULL, N'(UTC−11:00) Niue — Niue'),
    (N'Pacific/Auckland', N'NZ', N'New Zealand Standard Time', 720, N'Auckland', N'most of New Zealand', N'(UTC+12:00) New Zealand — Auckland (most of New Zealand)'),
    (N'Pacific/Chatham', N'NZ', N'Chatham Islands Standard Time', 765, N'Chatham', N'Chatham Islands', N'(UTC+12:45) New Zealand — Chatham (Chatham Islands)'),
    (N'Asia/Muscat', N'OM', N'Arabian Standard Time', 240, N'Muscat', NULL, N'(UTC+04:00) Oman — Muscat'),
    (N'America/Panama', N'PA', N'SA Pacific Standard Time', -300, N'Panama', NULL, N'(UTC−05:00) Panama — Panama'),
    (N'America/Lima', N'PE', N'SA Pacific Standard Time', -300, N'Lima', NULL, N'(UTC−05:00) Peru — Lima'),
    (N'Pacific/Tahiti', N'PF', N'Hawaiian Standard Time', -600, N'Tahiti', N'Society Islands', N'(UTC−10:00) French Polynesia — Tahiti (Society Islands)'),
    (N'Pacific/Marquesas', N'PF', N'Marquesas Standard Time', -570, N'Marquesas', N'Marquesas Islands', N'(UTC−09:30) French Polynesia — Marquesas (Marquesas Islands)'),
    (N'Pacific/Gambier', N'PF', N'UTC-09', -540, N'Gambier', N'Gambier Islands', N'(UTC−09:00) French Polynesia — Gambier (Gambier Islands)'),
    (N'Pacific/Port_Moresby', N'PG', N'West Pacific Standard Time', 600, N'Port Moresby', N'most of Papua New Guinea', N'(UTC+10:00) Papua New Guinea — Port Moresby (most of Papua New Guinea)'),
    (N'Pacific/Bougainville', N'PG', N'Bougainville Standard Time', 660, N'Bougainville', N'Bougainville', N'(UTC+11:00) Papua New Guinea — Bougainville (Bougainville)'),
    (N'Asia/Manila', N'PH', N'Singapore Standard Time', 480, N'Manila', NULL, N'(UTC+08:00) Philippines — Manila'),
    (N'Asia/Karachi', N'PK', N'Pakistan Standard Time', 300, N'Karachi', NULL, N'(UTC+05:00) Pakistan — Karachi'),
    (N'Europe/Warsaw', N'PL', N'Central European Standard Time', 60, N'Warsaw', NULL, N'(UTC+01:00) Poland — Warsaw'),
    (N'America/Miquelon', N'PM', N'Saint Pierre Standard Time', -180, N'Miquelon', NULL, N'(UTC−03:00) St Pierre & Miquelon — Miquelon'),
    (N'Pacific/Pitcairn', N'PN', N'UTC-08', -480, N'Pitcairn', NULL, N'(UTC−08:00) Pitcairn — Pitcairn'),
    (N'America/Puerto_Rico', N'PR', N'SA Western Standard Time', -240, N'Puerto Rico', NULL, N'(UTC−04:00) Puerto Rico — Puerto Rico'),
    (N'Asia/Gaza', N'PS', N'West Bank Standard Time', 120, N'Gaza', N'Gaza Strip', N'(UTC+02:00) Palestine — Gaza (Gaza Strip)'),
    (N'Asia/Hebron', N'PS', N'West Bank Standard Time', 120, N'Hebron', N'West Bank', N'(UTC+02:00) Palestine — Hebron (West Bank)'),
    (N'Europe/Lisbon', N'PT', N'GMT Standard Time', 0, N'Lisbon', N'Portugal (mainland)', N'(UTC+00:00) Portugal — Lisbon (Portugal (mainland))'),
    (N'Atlantic/Madeira', N'PT', N'GMT Standard Time', 0, N'Madeira', N'Madeira Islands', N'(UTC+00:00) Portugal — Madeira (Madeira Islands)'),
    (N'Atlantic/Azores', N'PT', N'Azores Standard Time', -60, N'Azores', N'Azores', N'(UTC−01:00) Portugal — Azores (Azores)'),
    (N'Pacific/Palau', N'PW', N'Tokyo Standard Time', 540, N'Palau', NULL, N'(UTC+09:00) Palau — Palau'),
    (N'America/Asuncion', N'PY', N'Paraguay Standard Time', -180, N'Asuncion', NULL, N'(UTC−03:00) Paraguay — Asuncion'),
    (N'Asia/Qatar', N'QA', N'Arab Standard Time', 180, N'Qatar', NULL, N'(UTC+03:00) Qatar — Qatar'),
    (N'Indian/Reunion', N'RE', N'Mauritius Standard Time', 240, N'Reunion', NULL, N'(UTC+04:00) Réunion — Reunion'),
    (N'Europe/Bucharest', N'RO', N'GTB Standard Time', 120, N'Bucharest', NULL, N'(UTC+02:00) Romania — Bucharest'),
    (N'Europe/Belgrade', N'RS', N'Central Europe Standard Time', 60, N'Belgrade', NULL, N'(UTC+01:00) Serbia — Belgrade'),
    (N'Europe/Kaliningrad', N'RU', N'Kaliningrad Standard Time', 120, N'Kaliningrad', N'MSK-01 - Kaliningrad', N'(UTC+02:00) Russia — Kaliningrad (MSK-01 - Kaliningrad)'),
    (N'Europe/Moscow', N'RU', N'Russian Standard Time', 180, N'Moscow', N'MSK+00 - Moscow area', N'(UTC+03:00) Russia — Moscow (MSK+00 - Moscow area)'),
    (N'Europe/Simferopol', N'UA', N'Russian Standard Time', 180, N'Simferopol', N'Crimea', N'(UTC+03:00) Ukraine — Simferopol (Crimea)'),
    (N'Europe/Kirov', N'RU', N'Russian Standard Time', 180, N'Kirov', N'MSK+00 - Kirov', N'(UTC+03:00) Russia — Kirov (MSK+00 - Kirov)'),
    (N'Europe/Volgograd', N'RU', N'Volgograd Standard Time', 180, N'Volgograd', N'MSK+00 - Volgograd', N'(UTC+03:00) Russia — Volgograd (MSK+00 - Volgograd)'),
    (N'Europe/Astrakhan', N'RU', N'Astrakhan Standard Time', 240, N'Astrakhan', N'MSK+01 - Astrakhan', N'(UTC+04:00) Russia — Astrakhan (MSK+01 - Astrakhan)'),
    (N'Europe/Saratov', N'RU', N'Saratov Standard Time', 240, N'Saratov', N'MSK+01 - Saratov', N'(UTC+04:00) Russia — Saratov (MSK+01 - Saratov)'),
    (N'Europe/Ulyanovsk', N'RU', N'Astrakhan Standard Time', 240, N'Ulyanovsk', N'MSK+01 - Ulyanovsk', N'(UTC+04:00) Russia — Ulyanovsk (MSK+01 - Ulyanovsk)'),
    (N'Europe/Samara', N'RU', N'Russia Time Zone 3', 240, N'Samara', N'MSK+01 - Samara, Udmurtia', N'(UTC+04:00) Russia — Samara (MSK+01 - Samara, Udmurtia)'),
    (N'Asia/Yekaterinburg', N'RU', N'Ekaterinburg Standard Time', 300, N'Yekaterinburg', N'MSK+02 - Urals', N'(UTC+05:00) Russia — Yekaterinburg (MSK+02 - Urals)'),
    (N'Asia/Omsk', N'RU', N'Omsk Standard Time', 360, N'Omsk', N'MSK+03 - Omsk', N'(UTC+06:00) Russia — Omsk (MSK+03 - Omsk)'),
    (N'Asia/Novosibirsk', N'RU', N'N. Central Asia Standard Time', 420, N'Novosibirsk', N'MSK+04 - Novosibirsk', N'(UTC+07:00) Russia — Novosibirsk (MSK+04 - Novosibirsk)'),
    (N'Asia/Barnaul', N'RU', N'Altai Standard Time', 420, N'Barnaul', N'MSK+04 - Altai', N'(UTC+07:00) Russia — Barnaul (MSK+04 - Altai)'),
    (N'Asia/Tomsk', N'RU', N'Tomsk Standard Time', 420, N'Tomsk', N'MSK+04 - Tomsk', N'(UTC+07:00) Russia — Tomsk (MSK+04 - Tomsk)'),
    (N'Asia/Novokuznetsk', N'RU', N'North Asia Standard Time', 420, N'Novokuznetsk', N'MSK+04 - Kemerovo', N'(UTC+07:00) Russia — Novokuznetsk (MSK+04 - Kemerovo)'),
    (N'Asia/Krasnoyarsk', N'RU', N'North Asia Standard Time', 420, N'Krasnoyarsk', N'MSK+04 - Krasnoyarsk area', N'(UTC+07:00) Russia — Krasnoyarsk (MSK+04 - Krasnoyarsk area)'),
    (N'Asia/Irkutsk', N'RU', N'North Asia East Standard Time', 480, N'Irkutsk', N'MSK+05 - Irkutsk, Buryatia', N'(UTC+08:00) Russia — Irkutsk (MSK+05 - Irkutsk, Buryatia)'),
    (N'Asia/Chita', N'RU', N'Transbaikal Standard Time', 540, N'Chita', N'MSK+06 - Zabaykalsky', N'(UTC+09:00) Russia — Chita (MSK+06 - Zabaykalsky)'),
    (N'Asia/Yakutsk', N'RU', N'Yakutsk Standard Time', 540, N'Yakutsk', N'MSK+06 - Lena River', N'(UTC+09:00) Russia — Yakutsk (MSK+06 - Lena River)'),
    (N'Asia/Khandyga', N'RU', N'Yakutsk Standard Time', 540, N'Khandyga', N'MSK+06 - Tomponsky, Ust-Maysky', N'(UTC+09:00) Russia — Khandyga (MSK+06 - Tomponsky, Ust-Maysky)'),
    (N'Asia/Vladivostok', N'RU', N'Vladivostok Standard Time', 600, N'Vladivostok', N'MSK+07 - Amur River', N'(UTC+10:00) Russia — Vladivostok (MSK+07 - Amur River)'),
    (N'Asia/Ust-Nera', N'RU', N'Vladivostok Standard Time', 600, N'Ust-Nera', N'MSK+07 - Oymyakonsky', N'(UTC+10:00) Russia — Ust-Nera (MSK+07 - Oymyakonsky)'),
    (N'Asia/Magadan', N'RU', N'Magadan Standard Time', 660, N'Magadan', N'MSK+08 - Magadan', N'(UTC+11:00) Russia — Magadan (MSK+08 - Magadan)'),
    (N'Asia/Sakhalin', N'RU', N'Sakhalin Standard Time', 660, N'Sakhalin', N'MSK+08 - Sakhalin Island', N'(UTC+11:00) Russia — Sakhalin (MSK+08 - Sakhalin Island)'),
    (N'Asia/Srednekolymsk', N'RU', N'Russia Time Zone 10', 660, N'Srednekolymsk', N'MSK+08 - Sakha (E), N Kuril Is', N'(UTC+11:00) Russia — Srednekolymsk (MSK+08 - Sakha (E), N Kuril Is)'),
    (N'Asia/Kamchatka', N'RU', N'Russia Time Zone 11', 720, N'Kamchatka', N'MSK+09 - Kamchatka', N'(UTC+12:00) Russia — Kamchatka (MSK+09 - Kamchatka)'),
    (N'Asia/Anadyr', N'RU', N'Russia Time Zone 11', 720, N'Anadyr', N'MSK+09 - Bering Sea', N'(UTC+12:00) Russia — Anadyr (MSK+09 - Bering Sea)'),
    (N'Africa/Kigali', N'RW', N'South Africa Standard Time', 120, N'Kigali', NULL, N'(UTC+02:00) Rwanda — Kigali'),
    (N'Asia/Riyadh', N'SA', N'Arab Standard Time', 180, N'Riyadh', NULL, N'(UTC+03:00) Saudi Arabia — Riyadh'),
    (N'Pacific/Guadalcanal', N'SB', N'Central Pacific Standard Time', 660, N'Guadalcanal', NULL, N'(UTC+11:00) Solomon Islands — Guadalcanal'),
    (N'Indian/Mahe', N'SC', N'Mauritius Standard Time', 240, N'Mahe', NULL, N'(UTC+04:00) Seychelles — Mahe'),
    (N'Africa/Khartoum', N'SD', N'Sudan Standard Time', 120, N'Khartoum', NULL, N'(UTC+02:00) Sudan — Khartoum'),
    (N'Europe/Stockholm', N'SE', N'W. Europe Standard Time', 60, N'Stockholm', NULL, N'(UTC+01:00) Sweden — Stockholm'),
    (N'Asia/Singapore', N'SG', N'Singapore Standard Time', 480, N'Singapore', NULL, N'(UTC+08:00) Singapore — Singapore'),
    (N'Atlantic/St_Helena', N'SH', N'Greenwich Standard Time', 0, N'St Helena', NULL, N'(UTC+00:00) St Helena — St Helena'),
    (N'Europe/Ljubljana', N'SI', N'Central Europe Standard Time', 60, N'Ljubljana', NULL, N'(UTC+01:00) Slovenia — Ljubljana'),
    (N'Arctic/Longyearbyen', N'SJ', N'W. Europe Standard Time', 60, N'Longyearbyen', NULL, N'(UTC+01:00) Svalbard & Jan Mayen — Longyearbyen'),
    (N'Europe/Bratislava', N'SK', N'Central Europe Standard Time', 60, N'Bratislava', NULL, N'(UTC+01:00) Slovakia — Bratislava'),
    (N'Africa/Freetown', N'SL', N'Greenwich Standard Time', 0, N'Freetown', NULL, N'(UTC+00:00) Sierra Leone — Freetown'),
    (N'Europe/San_Marino', N'SM', N'W. Europe Standard Time', 60, N'San Marino', NULL, N'(UTC+01:00) San Marino — San Marino'),
    (N'Africa/Dakar', N'SN', N'Greenwich Standard Time', 0, N'Dakar', NULL, N'(UTC+00:00) Senegal — Dakar'),
    (N'Africa/Mogadishu', N'SO', N'E. Africa Standard Time', 180, N'Mogadishu', NULL, N'(UTC+03:00) Somalia — Mogadishu'),
    (N'America/Paramaribo', N'SR', N'SA Eastern Standard Time', -180, N'Paramaribo', NULL, N'(UTC−03:00) Suriname — Paramaribo'),
    (N'Africa/Juba', N'SS', N'South Africa Standard Time', 120, N'Juba', NULL, N'(UTC+02:00) South Sudan — Juba'),
    (N'Africa/Sao_Tome', N'ST', N'Sao Tome Standard Time', 0, N'Sao Tome', NULL, N'(UTC+00:00) Sao Tome & Principe — Sao Tome'),
    (N'America/El_Salvador', N'SV', N'Central America Standard Time', -360, N'El Salvador', NULL, N'(UTC−06:00) El Salvador — El Salvador'),
    (N'America/Lower_Princes', N'SX', N'SA Western Standard Time', -240, N'Lower Princes', NULL, N'(UTC−04:00) St Maarten (Dutch) — Lower Princes'),
    (N'Asia/Damascus', N'SY', N'Syria Standard Time', 180, N'Damascus', NULL, N'(UTC+03:00) Syria — Damascus'),
    (N'Africa/Mbabane', N'SZ', N'South Africa Standard Time', 120, N'Mbabane', NULL, N'(UTC+02:00) Eswatini (Swaziland) — Mbabane'),
    (N'America/Grand_Turk', N'TC', N'Turks And Caicos Standard Time', -300, N'Grand Turk', NULL, N'(UTC−05:00) Turks & Caicos Is — Grand Turk'),
    (N'Africa/Ndjamena', N'TD', N'W. Central Africa Standard Time', 60, N'Ndjamena', NULL, N'(UTC+01:00) Chad — Ndjamena'),
    (N'Indian/Kerguelen', N'TF', N'West Asia Standard Time', 300, N'Kerguelen', NULL, N'(UTC+05:00) French S. Terr. — Kerguelen'),
    (N'Africa/Lome', N'TG', N'Greenwich Standard Time', 0, N'Lome', NULL, N'(UTC+00:00) Togo — Lome'),
    (N'Asia/Bangkok', N'TH', N'SE Asia Standard Time', 420, N'Bangkok', NULL, N'(UTC+07:00) Thailand — Bangkok'),
    (N'Asia/Dushanbe', N'TJ', N'West Asia Standard Time', 300, N'Dushanbe', NULL, N'(UTC+05:00) Tajikistan — Dushanbe'),
    (N'Pacific/Fakaofo', N'TK', N'UTC+13', 780, N'Fakaofo', NULL, N'(UTC+13:00) Tokelau — Fakaofo'),
    (N'Asia/Dili', N'TL', N'Tokyo Standard Time', 540, N'Dili', NULL, N'(UTC+09:00) East Timor — Dili'),
    (N'Asia/Ashgabat', N'TM', N'West Asia Standard Time', 300, N'Ashgabat', NULL, N'(UTC+05:00) Turkmenistan — Ashgabat'),
    (N'Africa/Tunis', N'TN', N'W. Central Africa Standard Time', 60, N'Tunis', NULL, N'(UTC+01:00) Tunisia — Tunis'),
    (N'Pacific/Tongatapu', N'TO', N'Tonga Standard Time', 780, N'Tongatapu', NULL, N'(UTC+13:00) Tonga — Tongatapu'),
    (N'Europe/Istanbul', N'TR', N'Turkey Standard Time', 180, N'Istanbul', NULL, N'(UTC+03:00) Turkey — Istanbul'),
    (N'America/Port_of_Spain', N'TT', N'SA Western Standard Time', -240, N'Port of Spain', NULL, N'(UTC−04:00) Trinidad & Tobago — Port of Spain'),
    (N'Pacific/Funafuti', N'TV', N'UTC+12', 720, N'Funafuti', NULL, N'(UTC+12:00) Tuvalu — Funafuti'),
    (N'Asia/Taipei', N'TW', N'Taipei Standard Time', 480, N'Taipei', NULL, N'(UTC+08:00) Taiwan — Taipei'),
    (N'Africa/Dar_es_Salaam', N'TZ', N'E. Africa Standard Time', 180, N'Dar es Salaam', NULL, N'(UTC+03:00) Tanzania — Dar es Salaam'),
    (N'Europe/Kyiv', N'UA', NULL, 120, N'Kyiv', N'most of Ukraine', N'(UTC+02:00) Ukraine — Kyiv (most of Ukraine)'),
    (N'Africa/Kampala', N'UG', N'E. Africa Standard Time', 180, N'Kampala', NULL, N'(UTC+03:00) Uganda — Kampala'),
    (N'Pacific/Midway', N'UM', N'UTC-11', -660, N'Midway', N'Midway Islands', N'(UTC−11:00) US minor outlying islands — Midway (Midway Islands)'),
    (N'Pacific/Wake', N'UM', N'UTC+12', 720, N'Wake', N'Wake Island', N'(UTC+12:00) US minor outlying islands — Wake (Wake Island)'),
    (N'America/New_York', N'US', N'Eastern Standard Time', -300, N'New York', N'Eastern (most areas)', N'(UTC−05:00) United States — New York (Eastern (most areas))'),
    (N'America/Detroit', N'US', N'Eastern Standard Time', -300, N'Detroit', N'Eastern - MI (most areas)', N'(UTC−05:00) United States — Detroit (Eastern - MI (most areas))'),
    (N'America/Kentucky/Louisville', N'US', N'Eastern Standard Time', -300, N'Louisville', N'Eastern - KY (Louisville area)', N'(UTC−05:00) United States — Louisville (Eastern - KY (Louisville area))'),
    (N'America/Kentucky/Monticello', N'US', N'Eastern Standard Time', -300, N'Monticello', N'Eastern - KY (Wayne)', N'(UTC−05:00) United States — Monticello (Eastern - KY (Wayne))'),
    (N'America/Indiana/Indianapolis', N'US', N'US Eastern Standard Time', -300, N'Indianapolis', N'Eastern - IN (most areas)', N'(UTC−05:00) United States — Indianapolis (Eastern - IN (most areas))'),
    (N'America/Indiana/Vincennes', N'US', N'Eastern Standard Time', -300, N'Vincennes', N'Eastern - IN (Da, Du, K, Mn)', N'(UTC−05:00) United States — Vincennes (Eastern - IN (Da, Du, K, Mn))'),
    (N'America/Indiana/Winamac', N'US', N'Eastern Standard Time', -300, N'Winamac', N'Eastern - IN (Pulaski)', N'(UTC−05:00) United States — Winamac (Eastern - IN (Pulaski))'),
    (N'America/Indiana/Marengo', N'US', N'US Eastern Standard Time', -300, N'Marengo', N'Eastern - IN (Crawford)', N'(UTC−05:00) United States — Marengo (Eastern - IN (Crawford))'),
    (N'America/Indiana/Petersburg', N'US', N'Eastern Standard Time', -300, N'Petersburg', N'Eastern - IN (Pike)', N'(UTC−05:00) United States — Petersburg (Eastern - IN (Pike))'),
    (N'America/Indiana/Vevay', N'US', N'US Eastern Standard Time', -300, N'Vevay', N'Eastern - IN (Switzerland)', N'(UTC−05:00) United States — Vevay (Eastern - IN (Switzerland))'),
    (N'America/Chicago', N'US', N'Central Standard Time', -360, N'Chicago', N'Central (most areas)', N'(UTC−06:00) United States — Chicago (Central (most areas))'),
    (N'America/Indiana/Tell_City', N'US', N'Central Standard Time', -360, N'Tell City', N'Central - IN (Perry)', N'(UTC−06:00) United States — Tell City (Central - IN (Perry))'),
    (N'America/Indiana/Knox', N'US', N'Central Standard Time', -360, N'Knox', N'Central - IN (Starke)', N'(UTC−06:00) United States — Knox (Central - IN (Starke))'),
    (N'America/Menominee', N'US', N'Central Standard Time', -360, N'Menominee', N'Central - MI (Wisconsin border)', N'(UTC−06:00) United States — Menominee (Central - MI (Wisconsin border))'),
    (N'America/North_Dakota/Center', N'US', N'Central Standard Time', -360, N'Center', N'Central - ND (Oliver)', N'(UTC−06:00) United States — Center (Central - ND (Oliver))'),
    (N'America/North_Dakota/New_Salem', N'US', N'Central Standard Time', -360, N'New Salem', N'Central - ND (Morton rural)', N'(UTC−06:00) United States — New Salem (Central - ND (Morton rural))'),
    (N'America/North_Dakota/Beulah', N'US', N'Central Standard Time', -360, N'Beulah', N'Central - ND (Mercer)', N'(UTC−06:00) United States — Beulah (Central - ND (Mercer))'),
    (N'America/Denver', N'US', N'Mountain Standard Time', -420, N'Denver', N'Mountain (most areas)', N'(UTC−07:00) United States — Denver (Mountain (most areas))'),
    (N'America/Boise', N'US', N'Mountain Standard Time', -420, N'Boise', N'Mountain - ID (south), OR (east)', N'(UTC−07:00) United States — Boise (Mountain - ID (south), OR (east))'),
    (N'America/Phoenix', N'US', N'US Mountain Standard Time', -420, N'Phoenix', N'MST - AZ (except Navajo)', N'(UTC−07:00) United States — Phoenix (MST - AZ (except Navajo))'),
    (N'America/Los_Angeles', N'US', N'Pacific Standard Time', -480, N'Los Angeles', N'Pacific', N'(UTC−08:00) United States — Los Angeles (Pacific)'),
    (N'America/Anchorage', N'US', N'Alaskan Standard Time', -540, N'Anchorage', N'Alaska (most areas)', N'(UTC−09:00) United States — Anchorage (Alaska (most areas))'),
    (N'America/Juneau', N'US', N'Alaskan Standard Time', -540, N'Juneau', N'Alaska - Juneau area', N'(UTC−09:00) United States — Juneau (Alaska - Juneau area)'),
    (N'America/Sitka', N'US', N'Alaskan Standard Time', -540, N'Sitka', N'Alaska - Sitka area', N'(UTC−09:00) United States — Sitka (Alaska - Sitka area)'),
    (N'America/Metlakatla', N'US', N'Alaskan Standard Time', -540, N'Metlakatla', N'Alaska - Annette Island', N'(UTC−09:00) United States — Metlakatla (Alaska - Annette Island)'),
    (N'America/Yakutat', N'US', N'Alaskan Standard Time', -540, N'Yakutat', N'Alaska - Yakutat', N'(UTC−09:00) United States — Yakutat (Alaska - Yakutat)'),
    (N'America/Nome', N'US', N'Alaskan Standard Time', -540, N'Nome', N'Alaska (west)', N'(UTC−09:00) United States — Nome (Alaska (west))'),
    (N'America/Adak', N'US', N'Aleutian Standard Time', -600, N'Adak', N'Alaska - western Aleutians', N'(UTC−10:00) United States — Adak (Alaska - western Aleutians)')
) AS s (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName)
ON t.Id = s.Id
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.WindowsId AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.WindowsId AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.StandardOffsetMinutes AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.StandardOffsetMinutes AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.City AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.City AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.Comment AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.Comment AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.DisplayName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.DisplayName AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, WindowsId = s.WindowsId, StandardOffsetMinutes = s.StandardOffsetMinutes, City = s.City, Comment = s.Comment, DisplayName = s.DisplayName
WHEN NOT MATCHED THEN INSERT (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName) VALUES (s.Id, s.CountryCode, s.WindowsId, s.StandardOffsetMinutes, s.City, s.Comment, s.DisplayName);
PRINT '  [=] TimeZones: rows 201–400 merged.';

MERGE TimeZones AS t
USING (VALUES
    (N'Pacific/Honolulu', N'US', N'Hawaiian Standard Time', -600, N'Honolulu', N'Hawaii', N'(UTC−10:00) United States — Honolulu (Hawaii)'),
    (N'America/Montevideo', N'UY', N'Montevideo Standard Time', -180, N'Montevideo', NULL, N'(UTC−03:00) Uruguay — Montevideo'),
    (N'Asia/Samarkand', N'UZ', N'West Asia Standard Time', 300, N'Samarkand', N'Uzbekistan (west)', N'(UTC+05:00) Uzbekistan — Samarkand (Uzbekistan (west))'),
    (N'Asia/Tashkent', N'UZ', N'West Asia Standard Time', 300, N'Tashkent', N'Uzbekistan (east)', N'(UTC+05:00) Uzbekistan — Tashkent (Uzbekistan (east))'),
    (N'Europe/Vatican', N'VA', N'W. Europe Standard Time', 60, N'Vatican', NULL, N'(UTC+01:00) Vatican City — Vatican'),
    (N'America/St_Vincent', N'VC', N'SA Western Standard Time', -240, N'St Vincent', NULL, N'(UTC−04:00) St Vincent — St Vincent'),
    (N'America/Caracas', N'VE', N'Venezuela Standard Time', -240, N'Caracas', NULL, N'(UTC−04:00) Venezuela — Caracas'),
    (N'America/Tortola', N'VG', N'SA Western Standard Time', -240, N'Tortola', NULL, N'(UTC−04:00) Virgin Islands (UK) — Tortola'),
    (N'America/St_Thomas', N'VI', N'SA Western Standard Time', -240, N'St Thomas', NULL, N'(UTC−04:00) Virgin Islands (US) — St Thomas'),
    (N'Asia/Ho_Chi_Minh', N'VN', N'SE Asia Standard Time', 420, N'Ho Chi Minh', NULL, N'(UTC+07:00) Vietnam — Ho Chi Minh'),
    (N'Pacific/Efate', N'VU', N'Central Pacific Standard Time', 660, N'Efate', NULL, N'(UTC+11:00) Vanuatu — Efate'),
    (N'Pacific/Wallis', N'WF', N'UTC+12', 720, N'Wallis', NULL, N'(UTC+12:00) Wallis & Futuna — Wallis'),
    (N'Pacific/Apia', N'WS', N'Samoa Standard Time', 780, N'Apia', NULL, N'(UTC+13:00) Samoa (western) — Apia'),
    (N'Asia/Aden', N'YE', N'Arab Standard Time', 180, N'Aden', NULL, N'(UTC+03:00) Yemen — Aden'),
    (N'Indian/Mayotte', N'YT', N'E. Africa Standard Time', 180, N'Mayotte', NULL, N'(UTC+03:00) Mayotte — Mayotte'),
    (N'Africa/Johannesburg', N'ZA', N'South Africa Standard Time', 120, N'Johannesburg', NULL, N'(UTC+02:00) South Africa — Johannesburg'),
    (N'Africa/Lusaka', N'ZM', N'South Africa Standard Time', 120, N'Lusaka', NULL, N'(UTC+02:00) Zambia — Lusaka'),
    (N'Africa/Harare', N'ZW', N'South Africa Standard Time', 120, N'Harare', NULL, N'(UTC+02:00) Zimbabwe — Harare'),
    (N'Etc/UTC', NULL, N'UTC', 0, N'UTC', N'Coordinated Universal Time', N'(UTC+00:00) Coordinated Universal Time')
) AS s (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName)
ON t.Id = s.Id
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.WindowsId AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.WindowsId AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.StandardOffsetMinutes AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.StandardOffsetMinutes AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.City AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.City AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.Comment AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.Comment AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.DisplayName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.DisplayName AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, WindowsId = s.WindowsId, StandardOffsetMinutes = s.StandardOffsetMinutes, City = s.City, Comment = s.Comment, DisplayName = s.DisplayName
WHEN NOT MATCHED THEN INSERT (Id, CountryCode, WindowsId, StandardOffsetMinutes, City, Comment, DisplayName) VALUES (s.Id, s.CountryCode, s.WindowsId, s.StandardOffsetMinutes, s.City, s.Comment, s.DisplayName);
PRINT '  [=] TimeZones: rows 401–419 merged.';

MERGE DateLocales AS t
USING (VALUES
    (N'aa-DJ', N'DJ', N'Afar (Djibouti)', N'Afar (Djibouti)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 6, N'2026 M10 5', N'2026-10-05'),
    (N'aa-ER', N'ER', N'Afar (Eritrea)', N'Afar (Eritrea)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 1, N'2026 M10 5', N'2026-10-05'),
    (N'aa-ET', N'ET', N'Afar (Ethiopia)', N'Afar (Ethiopia)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 0, N'2026 M10 5', N'2026-10-05'),
    (N'af-NA', N'NA', N'Afrikaans (Namibia)', N'Afrikaans (Namibië)', N'yyyy-MM-dd', N'dd MMMM yyyy', N'h:mm tt', 1, N'05 Oktober 2026', N'2026-10-05'),
    (N'af-ZA', N'ZA', N'Afrikaans (South Africa)', N'Afrikaans (Suid-Afrika)', N'yyyy-MM-dd', N'dd MMMM yyyy', N'HH:mm', 0, N'05 Oktober 2026', N'2026-10-05'),
    (N'agq-CM', N'CM', N'Aghem (Cameroon)', N'Aghem (Kàmàlûŋ)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ndzɔ̀ŋɔ̀ghǔuwelɔ̀m 2026', N'5/10/2026'),
    (N'ak-GH', N'GH', N'Akan (Ghana)', N'Akan (Gaana)', N'yyyy/MM/dd', N'yyyy MMMM dd', N'h:mm tt', 1, N'2026 Ɔbɛsɛ-Ahinime 05', N'2026/10/05'),
    (N'am-ET', N'ET', N'Amharic (Ethiopia)', N'አማርኛ (ኢትዮጵያ)', N'dd/MM/yyyy', N'yyyy MMMM d', N'h:mm tt', 0, N'2026 ኦክቶበር 5', N'05/10/2026'),
    (N'ar-AE', N'AE', N'Arabic (United Arab Emirates)', N'العربية (الإمارات العربية المتحدة)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-BH', N'BH', N'Arabic (Bahrain)', N'العربية (البحرين)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-DJ', N'DJ', N'Arabic (Djibouti)', N'العربية (جيبوتي)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-DZ', N'DZ', N'Arabic (Algeria)', N'العربية (الجزائر)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-EG', N'EG', N'Arabic (Egypt)', N'العربية (مصر)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-ER', N'ER', N'Arabic (Eritrea)', N'العربية (إريتريا)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-IL', N'IL', N'Arabic (Israel)', N'العربية (إسرائيل)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'H:mm', 0, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-IQ', N'IQ', N'Arabic (Iraq)', N'العربية (العراق)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 تشرين الأول 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-JO', N'JO', N'Arabic (Jordan)', N'العربية (الأردن)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 تشرين الأول 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-KM', N'KM', N'Arabic (Comoros)', N'العربية (جزر القمر)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-KW', N'KW', N'Arabic (Kuwait)', N'العربية (الكويت)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-LB', N'LB', N'Arabic (Lebanon)', N'العربية (لبنان)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 تشرين الأول 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-LY', N'LY', N'Arabic (Libya)', N'العربية (ليبيا)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-MA', N'MA', N'Arabic (Morocco)', N'العربية (المغرب)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-MR', N'MR', N'Arabic (Mauritania)', N'العربية (موريتانيا)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-OM', N'OM', N'Arabic (Oman)', N'العربية (عُمان)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-PS', N'PS', N'Arabic (Palestinian Authority)', N'العربية (السلطة الفلسطينية)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 تشرين الأول 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-QA', N'QA', N'Arabic (Qatar)', N'العربية (قطر)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-SA', N'SA', N'Arabic (Saudi Arabia)', N'العربية (المملكة العربية السعودية)', N'd‏/M‏/yyyy g', N'd MMMM yyyy g', N'h:mm tt', 0, N'5 أكتوبر 2026 م', N'5‏‏/10‏‏/2026 م'),
    (N'ar-SD', N'SD', N'Arabic (Sudan)', N'العربية (السودان)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-SO', N'SO', N'Arabic (Somalia)', N'العربية (الصومال)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-SS', N'SS', N'Arabic (South Sudan)', N'العربية (جنوب السودان)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-SY', N'SY', N'Arabic (Syria)', N'العربية (سوريا)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 تشرين الأول 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-TD', N'TD', N'Arabic (Chad)', N'العربية (تشاد)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-TN', N'TN', N'Arabic (Tunisia)', N'العربية (تونس)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'ar-YE', N'YE', N'Arabic (Yemen)', N'العربية (اليمن)', N'd‏/M‏/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 أكتوبر 2026', N'5‏‏/10‏‏/2026'),
    (N'arn-CL', N'CL', N'Mapuche (Chile)', N'Mapuche (Chile)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'as-IN', N'IN', N'Assamese (India)', N'অসমীয়া (ভাৰত)', N'd-M-yyyy', N'd MMMM, yyyy', N'tt h.mm', 0, N'5 অক্টোবৰ, 2026', N'5-10-2026'),
    (N'asa-TZ', N'TZ', N'Asu (Tanzania)', N'Kipare (Tadhania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'ast-ES', N'ES', N'Asturian (Spain)', N'asturianu (España)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'HH:mm', 1, N'5 d’ochobre de 2026', N'5/10/2026'),
    (N'az-Cyrl-AZ', N'AZ', N'Azerbaijani (Cyrillic, Azerbaijan)', N'азәрбајҹан (Кирил, Азәрбајҹан)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 октјабр 2026', N'05.10.2026'),
    (N'az-Latn-AZ', N'AZ', N'Azerbaijani (Latin, Azerbaijan)', N'azərbaycan (latın, Azərbaycan)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktyabr 2026', N'05.10.2026'),
    (N'ba-RU', N'RU', N'Bashkir (Russia)', N'Bashkir (Russia)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'bas-CM', N'CM', N'Basaa (Cameroon)', N'Ɓàsàa (Kàmɛ̀rûn)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Bìòôm 2026', N'5/10/2026'),
    (N'be-BY', N'BY', N'Belarusian (Belarus)', N'беларуская (Беларусь)', N'd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 кастрычніка 2026 г.', N'5.10.2026'),
    (N'bem-ZM', N'ZM', N'Bemba (Zambia)', N'Ichibemba (Zambia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'bez-TZ', N'TZ', N'Bena (Tanzania)', N'Hibena (Hutanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 pa mwedzi gwa kumi 2026', N'05/10/2026'),
    (N'bg-BG', N'BG', N'Bulgarian (Bulgaria)', N'български (България)', N'd.MM.yyyy ''г''.', N'd MMMM yyyy ''г''.', N'H:mm ''ч''.', 1, N'5 октомври 2026 г.', N'5.10.2026 г.'),
    (N'bm-ML', N'ML', N'Bamanankan (Mali)', N'bamanakan (Mali)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ɔkutɔburu 2026', N'5/10/2026'),
    (N'bn-BD', N'BD', N'Bangla (Bangladesh)', N'বাংলা (বাংলাদেশ)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 অক্টোবর, 2026', N'5/10/2026'),
    (N'bn-IN', N'IN', N'Bangla (India)', N'বাংলা (ভারত)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 অক্টোবর, 2026', N'5/10/2026'),
    (N'bo-CN', N'CN', N'Tibetan (China)', N'བོད་སྐད་ (རྒྱ་ནག)', N'yyyy-MM-dd', N'yyyy MMMMའི་ཚེས་d', N'h:mm tt', 0, N'2026 ཟླ་བ་བཅུ་པའི་ཚེས་5', N'2026-10-05'),
    (N'bo-IN', N'IN', N'Tibetan (India)', N'བོད་སྐད་ (རྒྱ་གར་)', N'yyyy-MM-dd', N'yyyy MMMMའི་ཚེས་d', N'h:mm tt', 0, N'2026 ཟླ་བ་བཅུ་པའི་ཚེས་5', N'2026-10-05'),
    (N'br-FR', N'FR', N'Breton (France)', N'brezhoneg (Frañs)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Here 2026', N'05/10/2026'),
    (N'brx-IN', N'IN', N'Bodo (India)', N'बड़ो (भारत)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'अखथबर 5, 2026', N'10/5/2026'),
    (N'bs-Cyrl-BA', N'BA', N'Bosnian (Cyrillic, Bosnia & Herzegovina)', N'босански (ћирилица, Босна и Херцеговина)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. октобар 2026.', N'5.10.2026.'),
    (N'bs-Latn-BA', N'BA', N'Bosnian (Latin, Bosnia & Herzegovina)', N'bosanski (latinica, Bosna i Hercegovina)', N'd. M. yyyy.', N'd. MMMM yyyy.', N'HH:mm', 1, N'5. oktobar 2026.', N'5. 10. 2026.'),
    (N'byn-ER', N'ER', N'Blin (Eritrea)', N'Blin (Eritrea)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'ca-AD', N'AD', N'Catalan (Andorra)', N'català (Andorra)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'H:mm', 1, N'5 d’octubre de 2026', N'5/10/2026'),
    (N'ca-ES', N'ES', N'Catalan (Spain)', N'català (Espanya)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'H:mm', 1, N'5 d’octubre de 2026', N'5/10/2026'),
    (N'ca-ES-VALENCIA', N'ES', N'Catalan (Spain, Valencian)', N'valencià (Espanya, valencià)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'H:mm', 1, N'5 d’octubre de 2026', N'5/10/2026'),
    (N'ca-FR', N'FR', N'Catalan (France)', N'català (França)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'H:mm', 1, N'5 d’octubre de 2026', N'5/10/2026'),
    (N'ca-IT', N'IT', N'Catalan (Italy)', N'català (Itàlia)', N'd/M/yyyy', N'd MMMM ''de'' yyyy', N'H:mm', 1, N'5 d’octubre de 2026', N'5/10/2026'),
    (N'ccp-BD', N'BD', N'Chakma (Bangladesh)', N'𑄌𑄋𑄴𑄟𑄳𑄦 (𑄝𑄁𑄣𑄘𑄬𑄌𑄴)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 𑄃𑄧𑄇𑄴𑄑𑄬𑄝𑄧𑄢𑄴, 2026', N'5/10/2026'),
    (N'ccp-IN', N'IN', N'Chakma (India)', N'𑄌𑄋𑄴𑄟𑄳𑄦 (𑄞𑄢𑄧𑄖𑄴)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 𑄃𑄧𑄇𑄴𑄑𑄬𑄝𑄧𑄢𑄴, 2026', N'5/10/2026'),
    (N'ce-RU', N'RU', N'Chechen (Russia)', N'нохчийн (Росси)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 октябрь 5', N'2026-10-05'),
    (N'ceb-PH', N'PH', N'Cebuano (Philippines)', N'Cebuano (Pilipinas)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'Oktubre 5, 2026', N'10/5/2026'),
    (N'cgg-UG', N'UG', N'Chiga (Uganda)', N'Rukiga (Uganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Okwaikumi 2026', N'05/10/2026'),
    (N'chr-US', N'US', N'Cherokee (United States)', N'ᏣᎳᎩ (ᏌᏊ ᎢᏳᎾᎵᏍᏔᏅ ᏍᎦᏚᎩ)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'ᏚᏂᏅᏗ 5, 2026', N'10/5/2026'),
    (N'ckb-IQ', N'IQ', N'Central Kurdish (Iraq)', N'کوردیی ناوەندی (عێراق)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 6, N'2026 تشرینی یەکەم 5', N'2026-10-05'),
    (N'ckb-IR', N'IR', N'Central Kurdish (Iran)', N'کوردیی ناوەندی (ئێران)', N'g yyyy-MM-dd', N'g yyyy MMMM d', N'HH:mm', 6, N'زایینی 2026 تشرینی یەکەم 5', N'زایینی 2026-10-05'),
    (N'co-FR', N'FR', N'Corsican (France)', N'Corsican (France)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'cs-CZ', N'CZ', N'Czech (Czechia)', N'čeština (Česko)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'H:mm', 1, N'5. října 2026', N'05.10.2026'),
    (N'cu-RU', N'RU', N'Church Slavic (Russia)', N'Church Slavic (Russia)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'cy-GB', N'GB', N'Welsh (United Kingdom)', N'Cymraeg (Y Deyrnas Unedig)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Hydref 2026', N'05/10/2026'),
    (N'da-DK', N'DK', N'Danish (Denmark)', N'dansk (Danmark)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH.mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'da-GL', N'GL', N'Danish (Greenland)', N'dansk (Grønland)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH.mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'dav-KE', N'KE', N'Taita (Kenya)', N'Kitaita (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mori ghwa ikumi 2026', N'05/10/2026'),
    (N'de-AT', N'AT', N'German (Austria)', N'Deutsch (Österreich)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-BE', N'BE', N'German (Belgium)', N'Deutsch (Belgien)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-CH', N'CH', N'German (Switzerland)', N'Deutsch (Schweiz)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-DE', N'DE', N'German (Germany)', N'Deutsch (Deutschland)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-IT', N'IT', N'German (Italy)', N'Deutsch (Italien)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-LI', N'LI', N'German (Liechtenstein)', N'Deutsch (Liechtenstein)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'de-LU', N'LU', N'German (Luxembourg)', N'Deutsch (Luxemburg)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'dje-NE', N'NE', N'Zarma (Niger)', N'Zarmaciine (Nižer)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoobur 2026', N'5/10/2026'),
    (N'dsb-DE', N'DE', N'Lower Sorbian (Germany)', N'dolnoserbšćina (Nimska)', N'd.M.yyyy', N'd. MMMM yyyy', N'H:mm', 1, N'5. oktobra 2026', N'5.10.2026'),
    (N'dua-CM', N'CM', N'Duala (Cameroon)', N'duálá (Cameroun)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 mayésɛ́ 2026', N'5/10/2026'),
    (N'dv-MV', N'MV', N'Divehi (Maldives)', N'Divehi (Maldives)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 5, N'2026 M10 5', N'2026-10-05'),
    (N'dyo-SN', N'SN', N'Jola-Fonyi (Senegal)', N'joola (Senegal)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktobar 2026', N'5/10/2026'),
    (N'dz-BT', N'BT', N'Dzongkha (Bhutan)', N'རྫོང་ཁ། (འབྲུག།)', N'yyyy-MM-dd', N'སྤྱི་ལོ་yyyy MMMM ཚེས་dd', N'ཆུ་ཚོད་ h སྐར་མ་ mm tt', 0, N'སྤྱི་ལོ་2026 ཟླ་བཅུ་པ་ ཚེས་05', N'2026-10-05'),
    (N'ebu-KE', N'KE', N'Embu (Kenya)', N'Kĩembu (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mweri wa ikũmi 2026', N'05/10/2026'),
    (N'ee-GH', N'GH', N'Ewe (Ghana)', N'Eʋegbe (Ghana nutome)', N'M/d/yyyy', N'MMMM d ''lia'' yyyy', N'tt ''ga'' h:mm', 1, N'kele 5 lia 2026', N'10/5/2026'),
    (N'ee-TG', N'TG', N'Ewe (Togo)', N'Eʋegbe (Togo nutome)', N'M/d/yyyy', N'MMMM d ''lia'' yyyy', N'HH:mm', 1, N'kele 5 lia 2026', N'10/5/2026'),
    (N'el-CY', N'CY', N'Greek (Cyprus)', N'Ελληνικά (Κύπρος)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Οκτωβρίου 2026', N'5/10/2026'),
    (N'el-GR', N'GR', N'Greek (Greece)', N'Ελληνικά (Ελλάδα)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Οκτωβρίου 2026', N'5/10/2026'),
    (N'en-AE', N'AE', N'English (United Arab Emirates)', N'English (United Arab Emirates)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 October 2026', N'05/10/2026'),
    (N'en-AG', N'AG', N'English (Antigua & Barbuda)', N'English (Antigua & Barbuda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-AI', N'AI', N'English (Anguilla)', N'English (Anguilla)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-AS', N'AS', N'English (American Samoa)', N'English (American Samoa)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-AT', N'AT', N'English (Austria)', N'English (Austria)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-AU', N'AU', N'English (Australia)', N'English (Australia)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'5/10/2026'),
    (N'en-BB', N'BB', N'English (Barbados)', N'English (Barbados)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-BE', N'BE', N'English (Belgium)', N'English (Belgium)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-BI', N'BI', N'English (Burundi)', N'English (Burundi)', N'M/d/yyyy', N'MMMM d, yyyy', N'HH:mm', 1, N'October 5, 2026', N'10/5/2026'),
    (N'en-BM', N'BM', N'English (Bermuda)', N'English (Bermuda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-BS', N'BS', N'English (Bahamas)', N'English (Bahamas)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-BW', N'BW', N'English (Botswana)', N'English (Botswana)', N'dd/MM/yyyy', N'dd MMMM yyyy', N'HH:mm', 0, N'05 October 2026', N'05/10/2026'),
    (N'en-BZ', N'BZ', N'English (Belize)', N'English (Belize)', N'dd/MM/yyyy', N'dd MMMM yyyy', N'HH:mm', 0, N'05 October 2026', N'05/10/2026'),
    (N'en-CA', N'CA', N'English (Canada)', N'English (Canada)', N'yyyy-MM-dd', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'2026-10-05'),
    (N'en-CC', N'CC', N'English (Cocos [Keeling] Islands)', N'English (Cocos [Keeling] Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-CH', N'CH', N'English (Switzerland)', N'English (Switzerland)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-CK', N'CK', N'English (Cook Islands)', N'English (Cook Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-CM', N'CM', N'English (Cameroon)', N'English (Cameroon)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-CX', N'CX', N'English (Christmas Island)', N'English (Christmas Island)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-CY', N'CY', N'English (Cyprus)', N'English (Cyprus)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-DE', N'DE', N'English (Germany)', N'English (Germany)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-DK', N'DK', N'English (Denmark)', N'English (Denmark)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH.mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-DM', N'DM', N'English (Dominica)', N'English (Dominica)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-ER', N'ER', N'English (Eritrea)', N'English (Eritrea)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-FI', N'FI', N'English (Finland)', N'English (Finland)', N'dd/MM/yyyy', N'd MMMM yyyy', N'H.mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-FJ', N'FJ', N'English (Fiji)', N'English (Fiji)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-FK', N'FK', N'English (Falkland Islands)', N'English (Falkland Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-FM', N'FM', N'English (Micronesia)', N'English (Micronesia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GB', N'GB', N'English (United Kingdom)', N'English (United Kingdom)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GD', N'GD', N'English (Grenada)', N'English (Grenada)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GG', N'GG', N'English (Guernsey)', N'English (Guernsey)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GH', N'GH', N'English (Ghana)', N'English (Ghana)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GI', N'GI', N'English (Gibraltar)', N'English (Gibraltar)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GM', N'GM', N'English (Gambia)', N'English (Gambia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-GU', N'GU', N'English (Guam)', N'English (Guam)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-GY', N'GY', N'English (Guyana)', N'English (Guyana)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-HK', N'HK', N'English (Hong Kong SAR)', N'English (Hong Kong SAR)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'5/10/2026'),
    (N'en-IE', N'IE', N'English (Ireland)', N'English (Ireland)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-IL', N'IL', N'English (Israel)', N'English (Israel)', N'dd/MM/yyyy', N'd MMMM yyyy', N'H:mm', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-IM', N'IM', N'English (Isle of Man)', N'English (Isle of Man)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-IN', N'IN', N'English (India)', N'English (India)', N'dd/MM/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 October, 2026', N'05/10/2026'),
    (N'en-IO', N'IO', N'English (British Indian Ocean Territory)', N'English (British Indian Ocean Territory)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-JE', N'JE', N'English (Jersey)', N'English (Jersey)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-JM', N'JM', N'English (Jamaica)', N'English (Jamaica)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-KE', N'KE', N'English (Kenya)', N'English (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-KI', N'KI', N'English (Kiribati)', N'English (Kiribati)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-KN', N'KN', N'English (St. Kitts & Nevis)', N'English (St. Kitts & Nevis)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-KY', N'KY', N'English (Cayman Islands)', N'English (Cayman Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-LC', N'LC', N'English (St. Lucia)', N'English (St. Lucia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-LR', N'LR', N'English (Liberia)', N'English (Liberia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-LS', N'LS', N'English (Lesotho)', N'English (Lesotho)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-MG', N'MG', N'English (Madagascar)', N'English (Madagascar)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-MH', N'MH', N'English (Marshall Islands)', N'English (Marshall Islands)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-MO', N'MO', N'English (Macao SAR)', N'English (Macao SAR)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-MP', N'MP', N'English (Northern Mariana Islands)', N'English (Northern Mariana Islands)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 1, N'October 5, 2026', N'10/5/2026'),
    (N'en-MS', N'MS', N'English (Montserrat)', N'English (Montserrat)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-MT', N'MT', N'English (Malta)', N'English (Malta)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-MU', N'MU', N'English (Mauritius)', N'English (Mauritius)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-MW', N'MW', N'English (Malawi)', N'English (Malawi)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-MY', N'MY', N'English (Malaysia)', N'English (Malaysia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NA', N'NA', N'English (Namibia)', N'English (Namibia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NF', N'NF', N'English (Norfolk Island)', N'English (Norfolk Island)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NG', N'NG', N'English (Nigeria)', N'English (Nigeria)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NL', N'NL', N'English (Netherlands)', N'English (Netherlands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NR', N'NR', N'English (Nauru)', N'English (Nauru)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NU', N'NU', N'English (Niue)', N'English (Niue)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-NZ', N'NZ', N'English (New Zealand)', N'English (New Zealand)', N'd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'5/10/2026'),
    (N'en-PG', N'PG', N'English (Papua New Guinea)', N'English (Papua New Guinea)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-PH', N'PH', N'English (Philippines)', N'English (Philippines)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-PK', N'PK', N'English (Pakistan)', N'English (Pakistan)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-PN', N'PN', N'English (Pitcairn Islands)', N'English (Pitcairn Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-PR', N'PR', N'English (Puerto Rico)', N'English (Puerto Rico)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-PW', N'PW', N'English (Palau)', N'English (Palau)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-RW', N'RW', N'English (Rwanda)', N'English (Rwanda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SB', N'SB', N'English (Solomon Islands)', N'English (Solomon Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SC', N'SC', N'English (Seychelles)', N'English (Seychelles)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SD', N'SD', N'English (Sudan)', N'English (Sudan)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 October 2026', N'05/10/2026'),
    (N'en-SE', N'SE', N'English (Sweden)', N'English (Sweden)', N'yyyy-MM-dd', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'2026-10-05'),
    (N'en-SG', N'SG', N'English (Singapore)', N'English (Singapore)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'5/10/2026'),
    (N'en-SH', N'SH', N'English (St Helena, Ascension, Tristan da Cunha)', N'English (St Helena, Ascension, Tristan da Cunha)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SI', N'SI', N'English (Slovenia)', N'English (Slovenia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SL', N'SL', N'English (Sierra Leone)', N'English (Sierra Leone)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SS', N'SS', N'English (South Sudan)', N'English (South Sudan)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SX', N'SX', N'English (Sint Maarten)', N'English (Sint Maarten)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-SZ', N'SZ', N'English (Eswatini)', N'English (Eswatini)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-TC', N'TC', N'English (Turks & Caicos Islands)', N'English (Turks & Caicos Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-TK', N'TK', N'English (Tokelau)', N'English (Tokelau)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-TO', N'TO', N'English (Tonga)', N'English (Tonga)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-TT', N'TT', N'English (Trinidad & Tobago)', N'English (Trinidad & Tobago)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-TV', N'TV', N'English (Tuvalu)', N'English (Tuvalu)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-TZ', N'TZ', N'English (Tanzania)', N'English (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-UG', N'UG', N'English (Uganda)', N'English (Uganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-UM', N'UM', N'English (U.S. Outlying Islands)', N'English (U.S. Outlying Islands)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-US', N'US', N'English (United States)', N'English (United States)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-US-POSIX', N'US', N'English (United States, Computer)', N'English (United States, Computer)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-VC', N'VC', N'English (St. Vincent & Grenadines)', N'English (St. Vincent & Grenadines)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-VG', N'VG', N'English (British Virgin Islands)', N'English (British Virgin Islands)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-VI', N'VI', N'English (U.S. Virgin Islands)', N'English (U.S. Virgin Islands)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'October 5, 2026', N'10/5/2026'),
    (N'en-VU', N'VU', N'English (Vanuatu)', N'English (Vanuatu)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-WS', N'WS', N'English (Samoa)', N'English (Samoa)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 October 2026', N'05/10/2026'),
    (N'en-ZA', N'ZA', N'English (South Africa)', N'English (South Africa)', N'yyyy/MM/dd', N'dd MMMM yyyy', N'HH:mm', 0, N'05 October 2026', N'2026/10/05'),
    (N'en-ZM', N'ZM', N'English (Zambia)', N'English (Zambia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 October 2026', N'05/10/2026'),
    (N'en-ZW', N'ZW', N'English (Zimbabwe)', N'English (Zimbabwe)', N'd/M/yyyy', N'dd MMMM yyyy', N'HH:mm', 0, N'05 October 2026', N'5/10/2026'),
    (N'es-AR', N'AR', N'Spanish (Argentina)', N'español (Argentina)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-BO', N'BO', N'Spanish (Bolivia)', N'español (Bolivia)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-BR', N'BR', N'Spanish (Brazil)', N'español (Brasil)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026')
) AS s (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort)
ON t.Code = s.Code
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.EnglishName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.EnglishName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.NativeName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.NativeName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.LongDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.LongDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortTimePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortTimePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.FirstDayOfWeek AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.FirstDayOfWeek AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleLong AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleLong AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleShort AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleShort AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, EnglishName = s.EnglishName, NativeName = s.NativeName, ShortDatePattern = s.ShortDatePattern, LongDatePattern = s.LongDatePattern, ShortTimePattern = s.ShortTimePattern, FirstDayOfWeek = s.FirstDayOfWeek, SampleLong = s.SampleLong, SampleShort = s.SampleShort
WHEN NOT MATCHED THEN INSERT (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort) VALUES (s.Code, s.CountryCode, s.EnglishName, s.NativeName, s.ShortDatePattern, s.LongDatePattern, s.ShortTimePattern, s.FirstDayOfWeek, s.SampleLong, s.SampleShort);
PRINT '  [=] DateLocales: rows 1–200 merged.';

MERGE DateLocales AS t
USING (VALUES
    (N'es-BZ', N'BZ', N'Spanish (Belize)', N'español (Belice)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-CL', N'CL', N'Spanish (Chile)', N'español (Chile)', N'dd-MM-yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'05-10-2026'),
    (N'es-CO', N'CO', N'Spanish (Colombia)', N'español (Colombia)', N'd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-CR', N'CR', N'Spanish (Costa Rica)', N'español (Costa Rica)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-CU', N'CU', N'Spanish (Cuba)', N'español (Cuba)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-DO', N'DO', N'Spanish (Dominican Republic)', N'español (República Dominicana)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-EC', N'EC', N'Spanish (Ecuador)', N'español (Ecuador)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-ES', N'ES', N'Spanish (Spain)', N'español (España)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'H:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-GQ', N'GQ', N'Spanish (Equatorial Guinea)', N'español (Guinea Ecuatorial)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'H:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-GT', N'GT', N'Spanish (Guatemala)', N'español (Guatemala)', N'd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-HN', N'HN', N'Spanish (Honduras)', N'español (Honduras)', N'd/M/yyyy', N'dd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'05 de octubre de 2026', N'5/10/2026'),
    (N'es-MX', N'MX', N'Spanish (Mexico)', N'español (México)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'H:mm', 0, N'5 de octubre de 2026', N'05/10/2026'),
    (N'es-NI', N'NI', N'Spanish (Nicaragua)', N'español (Nicaragua)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-PA', N'PA', N'Spanish (Panama)', N'español (Panamá)', N'MM/dd/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'10/05/2026'),
    (N'es-PE', N'PE', N'Spanish (Peru)', N'español (Perú)', N'd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-PH', N'PH', N'Spanish (Philippines)', N'español (Filipinas)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-PR', N'PR', N'Spanish (Puerto Rico)', N'español (Puerto Rico)', N'MM/dd/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'10/05/2026'),
    (N'es-PY', N'PY', N'Spanish (Paraguay)', N'español (Paraguay)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-SV', N'SV', N'Spanish (El Salvador)', N'español (El Salvador)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-US', N'US', N'Spanish (United States)', N'español (Estados Unidos)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-UY', N'UY', N'Spanish (Uruguay)', N'español (Uruguay)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de octubre de 2026', N'5/10/2026'),
    (N'es-VE', N'VE', N'Spanish (Venezuela)', N'español (Venezuela)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de octubre de 2026', N'5/10/2026'),
    (N'et-EE', N'EE', N'Estonian (Estonia)', N'eesti (Eesti)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktoober 2026', N'05.10.2026'),
    (N'eu-ES', N'ES', N'Basque (Spain)', N'euskara (Espainia)', N'yyyy/M/d', N'yyyy(''e'')''ko'' MMMM''ren'' d(''a'')', N'HH:mm', 1, N'2026(e)ko urriaren 5(a)', N'2026/10/5'),
    (N'ewo-CM', N'CM', N'Ewondo (Cameroon)', N'ewondo (Kamərún)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ngɔn awóm 2026', N'5/10/2026'),
    (N'fa-AF', N'AF', N'Persian (Afghanistan)', N'فارسی (افغانستان)', N'yyyy/M/d', N'yyyy MMMM d', N'H:mm', 6, N'2026 اکتوبر 5', N'2026/10/5'),
    (N'fa-IR', N'IR', N'Persian (Iran)', N'فارسی (ایران)', N'yyyy/M/d', N'yyyy MMMM d', N'H:mm', 6, N'2026 اکتبر 5', N'2026/10/5'),
    (N'ff-Latn-BF', N'BF', N'Fulah (Latin, Burkina Faso)', N'Fulah (Latin, Burkina Faso)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-CM', N'CM', N'Fulah (Latin, Cameroon)', N'Fulah (Latin, Cameroon)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-GH', N'GH', N'Fulah (Latin, Ghana)', N'Fulah (Latin, Ghana)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-GM', N'GM', N'Fulah (Latin, Gambia)', N'Fulah (Latin, Gambia)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-GN', N'GN', N'Fulah (Latin, Guinea)', N'Fulah (Latin, Guinea)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-GW', N'GW', N'Fulah (Latin, Guinea-Bissau)', N'Fulah (Latin, Guinea-Bissau)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-LR', N'LR', N'Fulah (Latin, Liberia)', N'Fulah (Latin, Liberia)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-MR', N'MR', N'Fulah (Latin, Mauritania)', N'Fulah (Latin, Mauritania)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-NE', N'NE', N'Fulah (Latin, Niger)', N'Fulah (Latin, Niger)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-NG', N'NG', N'Fulah (Latin, Nigeria)', N'Fulah (Latin, Nigeria)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-SL', N'SL', N'Fulah (Latin, Sierra Leone)', N'Fulah (Latin, Sierra Leone)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'ff-Latn-SN', N'SN', N'Fulah (Latin, Senegal)', N'Fulah (Latin, Senegal)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 yarkomaa 2026', N'5/10/2026'),
    (N'fi-FI', N'FI', N'Finnish (Finland)', N'suomi (Suomi)', N'd.M.yyyy', N'd. MMMM yyyy', N'H.mm', 1, N'5. lokakuuta 2026', N'5.10.2026'),
    (N'fil-PH', N'PH', N'Filipino (Philippines)', N'Filipino (Pilipinas)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'Oktubre 5, 2026', N'10/5/2026'),
    (N'fo-DK', N'DK', N'Faroese (Denmark)', N'føroyskt (Danmark)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'fo-FO', N'FO', N'Faroese (Faroe Islands)', N'føroyskt (Føroyar)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'fr-BE', N'BE', N'French (Belgium)', N'français (Belgique)', N'd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'5/10/2026'),
    (N'fr-BF', N'BF', N'French (Burkina Faso)', N'français (Burkina Faso)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-BI', N'BI', N'French (Burundi)', N'français (Burundi)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-BJ', N'BJ', N'French (Benin)', N'français (Bénin)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-BL', N'BL', N'French (St. Barthélemy)', N'français (Saint-Barthélemy)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-CA', N'CA', N'French (Canada)', N'français (Canada)', N'yyyy-MM-dd', N'd MMMM yyyy', N'HH ''h'' mm', 0, N'5 octobre 2026', N'2026-10-05'),
    (N'fr-CD', N'CD', N'French (Congo [DRC])', N'français (Congo [République démocratique du])', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-CF', N'CF', N'French (Central African Republic)', N'français (République centrafricaine)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-CG', N'CG', N'French (Congo)', N'français (Congo)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-CH', N'CH', N'French (Switzerland)', N'français (Suisse)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05.10.2026'),
    (N'fr-CI', N'CI', N'French (Côte d’Ivoire)', N'français (Côte d’Ivoire)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-CM', N'CM', N'French (Cameroon)', N'français (Cameroun)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-DJ', N'DJ', N'French (Djibouti)', N'français (Djibouti)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-DZ', N'DZ', N'French (Algeria)', N'français (Algérie)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-FR', N'FR', N'French (France)', N'français (France)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-GA', N'GA', N'French (Gabon)', N'français (Gabon)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-GF', N'GF', N'French (French Guiana)', N'français (Guyane française)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-GN', N'GN', N'French (Guinea)', N'français (Guinée)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-GP', N'GP', N'French (Guadeloupe)', N'français (Guadeloupe)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-GQ', N'GQ', N'French (Equatorial Guinea)', N'français (Guinée équatoriale)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-HT', N'HT', N'French (Haiti)', N'français (Haïti)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-KM', N'KM', N'French (Comoros)', N'français (Comores)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-LU', N'LU', N'French (Luxembourg)', N'français (Luxembourg)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MA', N'MA', N'French (Morocco)', N'français (Maroc)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MC', N'MC', N'French (Monaco)', N'français (Monaco)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MF', N'MF', N'French (St. Martin)', N'français (Saint-Martin)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MG', N'MG', N'French (Madagascar)', N'français (Madagascar)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-ML', N'ML', N'French (Mali)', N'français (Mali)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MQ', N'MQ', N'French (Martinique)', N'français (Martinique)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MR', N'MR', N'French (Mauritania)', N'français (Mauritanie)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-MU', N'MU', N'French (Mauritius)', N'français (Maurice)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-NC', N'NC', N'French (New Caledonia)', N'français (Nouvelle-Calédonie)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-NE', N'NE', N'French (Niger)', N'français (Niger)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-PF', N'PF', N'French (French Polynesia)', N'français (Polynésie française)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-PM', N'PM', N'French (St. Pierre & Miquelon)', N'français (Saint-Pierre-et-Miquelon)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-RE', N'RE', N'French (Réunion)', N'français (La Réunion)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-RW', N'RW', N'French (Rwanda)', N'français (Rwanda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-SC', N'SC', N'French (Seychelles)', N'français (Seychelles)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-SN', N'SN', N'French (Senegal)', N'français (Sénégal)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-SY', N'SY', N'French (Syria)', N'français (Syrie)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-TD', N'TD', N'French (Chad)', N'français (Tchad)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-TG', N'TG', N'French (Togo)', N'français (Togo)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-TN', N'TN', N'French (Tunisia)', N'français (Tunisie)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-VU', N'VU', N'French (Vanuatu)', N'français (Vanuatu)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-WF', N'WF', N'French (Wallis & Futuna)', N'français (Wallis-et-Futuna)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fr-YT', N'YT', N'French (Mayotte)', N'français (Mayotte)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octobre 2026', N'05/10/2026'),
    (N'fur-IT', N'IT', N'Friulian (Italy)', N'furlan (Italie)', N'dd/MM/yyyy', N'd ''di'' MMMM ''dal'' yyyy', N'HH:mm', 1, N'5 di Otubar dal 2026', N'05/10/2026'),
    (N'fy-NL', N'NL', N'Western Frisian (Netherlands)', N'Frysk (Nederlân)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktober 2026', N'05-10-2026'),
    (N'ga-IE', N'IE', N'Irish (Ireland)', N'Gaeilge (Éire)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Deireadh Fómhair 2026', N'05/10/2026'),
    (N'gd-GB', N'GB', N'Scottish Gaelic (United Kingdom)', N'Gàidhlig (An Rìoghachd Aonaichte)', N'dd/MM/yyyy', N'd''mh'' MMMM yyyy', N'HH:mm', 1, N'5mh dhen Dàmhair 2026', N'05/10/2026'),
    (N'gl-ES', N'ES', N'Galician (Spain)', N'galego (España)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'gn-PY', N'PY', N'Guarani (Paraguay)', N'Guarani (Paraguay)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'gsw-CH', N'CH', N'Swiss German (Switzerland)', N'Schwiizertüütsch (Schwiiz)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktoober 2026', N'05.10.2026'),
    (N'gsw-FR', N'FR', N'Swiss German (France)', N'Elsässisch (Frànkrisch)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktoober 2026', N'05.10.2026'),
    (N'gsw-LI', N'LI', N'Swiss German (Liechtenstein)', N'Schwiizertüütsch (Liächteschtäi)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktoober 2026', N'05.10.2026'),
    (N'gu-IN', N'IN', N'Gujarati (India)', N'ગુજરાતી (ભારત)', N'd/M/yyyy', N'd MMMM, yyyy', N'hh:mm tt', 0, N'5 ઑક્ટોબર, 2026', N'5/10/2026'),
    (N'guz-KE', N'KE', N'Gusii (Kenya)', N'Ekegusii (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Okitoba 2026', N'05/10/2026'),
    (N'gv-IM', N'IM', N'Manx (Isle of Man)', N'Gaelg (Ellan Vannin)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 Jerrey-fouyir 5', N'2026-10-05'),
    (N'ha-GH', N'GH', N'Hausa (Ghana)', N'Hausa (Gana)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 1, N'5 Oktoba, 2026', N'5/10/2026'),
    (N'ha-NE', N'NE', N'Hausa (Niger)', N'Hausa (Nijar)', N'd/M/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 Oktoba, 2026', N'5/10/2026'),
    (N'ha-NG', N'NG', N'Hausa (Nigeria)', N'Hausa (Najeriya)', N'd/M/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 Oktoba, 2026', N'5/10/2026'),
    (N'haw-US', N'US', N'Hawaiian (United States)', N'ʻŌlelo Hawaiʻi (ʻAmelika Hui Pū ʻIa)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 ʻOkakopa 2026', N'5/10/2026'),
    (N'he-IL', N'IL', N'Hebrew (Israel)', N'עברית (ישראל)', N'd.M.yyyy', N'd בMMMM yyyy', N'H:mm', 0, N'5 באוקטובר 2026', N'5.10.2026'),
    (N'hi-IN', N'IN', N'Hindi (India)', N'हिन्दी (भारत)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 अक्तूबर 2026', N'5/10/2026'),
    (N'hr-BA', N'BA', N'Croatian (Bosnia & Herzegovina)', N'hrvatski (Bosna i Hercegovina)', N'd. M. yyyy.', N'd. MMMM yyyy.', N'HH:mm', 1, N'5. listopada 2026.', N'5. 10. 2026.'),
    (N'hr-HR', N'HR', N'Croatian (Croatia)', N'hrvatski (Hrvatska)', N'dd. MM. yyyy.', N'd. MMMM yyyy.', N'HH:mm', 1, N'5. listopada 2026.', N'05. 10. 2026.'),
    (N'hsb-DE', N'DE', N'Upper Sorbian (Germany)', N'hornjoserbšćina (Němska)', N'd.M.yyyy', N'd. MMMM yyyy', N'H:mm ''hodź''.', 1, N'5. oktobra 2026', N'5.10.2026'),
    (N'hu-HU', N'HU', N'Hungarian (Hungary)', N'magyar (Magyarország)', N'yyyy. MM. dd.', N'yyyy. MMMM d.', N'H:mm', 1, N'2026. október 5.', N'2026. 10. 05.'),
    (N'hy-AM', N'AM', N'Armenian (Armenia)', N'հայերեն (Հայաստան)', N'dd.MM.yyyy', N'yyyy թ. MMMM d', N'HH:mm', 1, N'2026 թ. հոկտեմբերի 5', N'05.10.2026'),
    (N'id-ID', N'ID', N'Indonesian (Indonesia)', N'Indonesia (Indonesia)', N'dd/MM/yyyy', N'dd MMMM yyyy', N'HH.mm', 0, N'05 Oktober 2026', N'05/10/2026'),
    (N'ig-NG', N'NG', N'Igbo (Nigeria)', N'Asụsụ Igbo (Naịjịrịa)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Ọktoba 2026', N'5/10/2026'),
    (N'ii-CN', N'CN', N'Yi (China)', N'ꆈꌠꉙ (ꍏꇩ)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 0, N'2026 ꊰꆪ 5', N'2026-10-05'),
    (N'is-IS', N'IS', N'Icelandic (Iceland)', N'íslenska (Ísland)', N'd.M.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. október 2026', N'5.10.2026'),
    (N'it-CH', N'CH', N'Italian (Switzerland)', N'italiano (Svizzera)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ottobre 2026', N'05.10.2026'),
    (N'it-IT', N'IT', N'Italian (Italy)', N'italiano (Italia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ottobre 2026', N'05/10/2026'),
    (N'it-SM', N'SM', N'Italian (San Marino)', N'italiano (San Marino)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ottobre 2026', N'05/10/2026'),
    (N'it-VA', N'VA', N'Italian (Vatican City)', N'italiano (Città del Vaticano)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ottobre 2026', N'05/10/2026'),
    (N'iu-CA', N'CA', N'Inuktitut (Canada)', N'Inuktitut (Canada)', N'MM/dd/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'M10 5, 2026', N'10/05/2026'),
    (N'iu-Latn-CA', N'CA', N'Inuktitut (Latin, Canada)', N'Inuktitut (Latin, Canada)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'ja-JP', N'JP', N'Japanese (Japan)', N'日本語 (日本)', N'yyyy/MM/dd', N'yyyy年M月d日', N'H:mm', 0, N'2026年10月5日', N'2026/10/05'),
    (N'jgo-CM', N'CM', N'Ngomba (Cameroon)', N'Ndaꞌa (Kamɛlûn)', N'yyyy-MM-dd', N'yyyy MMMM dd', N'HH:mm', 1, N'2026 Pɛsaŋ Nɛgɛ́m 05', N'2026-10-05'),
    (N'jmc-TZ', N'TZ', N'Machame (Tanzania)', N'Kimachame (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'jv-ID', N'ID', N'Javanese (Indonesia)', N'Jawa (Indonésia)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Oktober 2026', N'05-10-2026'),
    (N'ka-GE', N'GE', N'Georgian (Georgia)', N'ქართული (საქართველო)', N'dd.MM.yyyy', N'dd MMMM, yyyy', N'HH:mm', 1, N'05 ოქტომბერი, 2026', N'05.10.2026'),
    (N'kab-DZ', N'DZ', N'Kabyle (Algeria)', N'Taqbaylit (Lezzayer)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 6, N'5 Tubeṛ 2026', N'5/10/2026'),
    (N'kam-KE', N'KE', N'Kamba (Kenya)', N'Kikamba (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mwai wa ĩkumi 2026', N'05/10/2026'),
    (N'kde-TZ', N'TZ', N'Makonde (Tanzania)', N'Chimakonde (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Mwedi wa Nnyano na Nnyano 2026', N'05/10/2026'),
    (N'kea-CV', N'CV', N'Kabuverdianu (Cabo Verde)', N'kabuverdianu (Kabu Verdi)', N'd/M/yyyy', N'd ''di'' MMMM ''di'' yyyy', N'HH:mm', 1, N'5 di Otubru di 2026', N'5/10/2026'),
    (N'khq-ML', N'ML', N'Koyra Chiini (Mali)', N'Koyra ciini (Maali)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoobur 2026', N'5/10/2026'),
    (N'ki-KE', N'KE', N'Kikuyu (Kenya)', N'Gikuyu (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mwere wa ikũmi 2026', N'05/10/2026'),
    (N'kk-KZ', N'KZ', N'Kazakh (Kazakhstan)', N'қазақ тілі (Қазақстан)', N'dd.MM.yyyy', N'yyyy ''ж''. d MMMM', N'HH:mm', 1, N'2026 ж. 5 қазан', N'05.10.2026'),
    (N'kkj-CM', N'CM', N'Kako (Cameroon)', N'kakɔ (Kamɛrun)', N'dd/MM yyyy', N'dd MMMM yyyy', N'HH:mm', 1, N'05 nyukul 2026', N'05/10 2026'),
    (N'kl-GL', N'GL', N'Kalaallisut (Greenland)', N'kalaallisut (Kalaallit Nunaat)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH.mm', 1, N'2026 oktoberi 5', N'2026-10-05'),
    (N'kln-KE', N'KE', N'Kalenjin (Kenya)', N'Kalenjin (Emetab Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Epeeso 2026', N'05/10/2026'),
    (N'km-KH', N'KH', N'Khmer (Cambodia)', N'ខ្មែរ (កម្ពុជា)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 តុលា 2026', N'5/10/2026'),
    (N'kn-IN', N'IN', N'Kannada (India)', N'ಕನ್ನಡ (ಭಾರತ)', N'd/M/yyyy', N'MMMM d, yyyy', N'hh:mm tt', 0, N'ಅಕ್ಟೋಬರ್ 5, 2026', N'5/10/2026'),
    (N'ko-KP', N'KP', N'Korean (North Korea)', N'한국어(조선민주주의인민공화국)', N'yyyy. M. d.', N'yyyy년 M월 d일', N'tt h:mm', 1, N'2026년 10월 5일', N'2026. 10. 5.'),
    (N'ko-KR', N'KR', N'Korean (Korea)', N'한국어(대한민국)', N'yyyy. M. d.', N'yyyy년 M월 d일', N'tt h:mm', 0, N'2026년 10월 5일', N'2026. 10. 5.'),
    (N'kok-IN', N'IN', N'Konkani (India)', N'कोंकणी (भारत)', N'd-M-yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 ऑक्टोबर 2026', N'5-10-2026'),
    (N'ks-IN', N'IN', N'Kashmiri (India)', N'کٲشُر (ہِندوستان)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'اکتوٗبر 5, 2026', N'10/5/2026'),
    (N'ksb-TZ', N'TZ', N'Shambala (Tanzania)', N'Kishambaa (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'ksf-CM', N'CM', N'Bafia (Cameroon)', N'rikpa (kamɛrún)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ŋwíí akǝ ntɛk 2026', N'5/10/2026'),
    (N'ksh-DE', N'DE', N'Colognian (Germany)', N'Kölsch en Doütschland', N'd. M. yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktohber 2026', N'5. 10. 2026'),
    (N'kw-GB', N'GB', N'Cornish (United Kingdom)', N'kernewek (Rywvaneth Unys)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 mis Hedra 5', N'2026-10-05'),
    (N'ky-KG', N'KG', N'Kyrgyz (Kyrgyzstan)', N'кыргызча (Кыргызстан)', N'd/M/yyyy', N'yyyy-''ж''., d-MMMM', N'HH:mm', 1, N'2026-ж., 5-октябрь', N'5/10/2026'),
    (N'lag-TZ', N'TZ', N'Langi (Tanzania)', N'Kɨlaangi (Taansanía)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Kwiinyi 2026', N'05/10/2026'),
    (N'lb-LU', N'LU', N'Luxembourgish (Luxembourg)', N'Lëtzebuergesch (Lëtzebuerg)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Oktober 2026', N'05.10.2026'),
    (N'lg-UG', N'UG', N'Ganda (Uganda)', N'Luganda (Yuganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Okitobba 2026', N'05/10/2026'),
    (N'lkt-US', N'US', N'Lakota (United States)', N'Lakȟólʼiyapi (Mílahaŋska Tȟamákȟočhe)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'Čhaŋwápe-kasná Wí 5, 2026', N'10/5/2026'),
    (N'ln-AO', N'AO', N'Lingala (Angola)', N'lingála (Angóla)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 sánzá ya zómi 2026', N'5/10/2026'),
    (N'ln-CD', N'CD', N'Lingala (Congo [DRC])', N'lingála (Republíki ya Kongó Demokratíki)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 sánzá ya zómi 2026', N'5/10/2026'),
    (N'ln-CF', N'CF', N'Lingala (Central African Republic)', N'lingála (Repibiki ya Afríka ya Káti)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 sánzá ya zómi 2026', N'5/10/2026'),
    (N'ln-CG', N'CG', N'Lingala (Congo)', N'lingála (Kongo)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 sánzá ya zómi 2026', N'5/10/2026'),
    (N'lo-LA', N'LA', N'Lao (Laos)', N'ລາວ (ລາວ)', N'd/M/yyyy', N'd MMMM g yyyy', N'H:mm', 0, N'5 ຕຸລາ ຄ.ສ. 2026', N'5/10/2026'),
    (N'lrc-IQ', N'IQ', N'Northern Luri (Iraq)', N'Northern Luri (Iraq)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 6, N'2026 ئوکتوڤر 5', N'2026-10-05'),
    (N'lrc-IR', N'IR', N'Northern Luri (Iran)', N'Northern Luri (Iran)', N'g yyyy-MM-dd', N'g yyyy MMMM d', N'HH:mm', 6, N'CE 2026 ئوکتوڤر 5', N'CE 2026-10-05'),
    (N'lt-LT', N'LT', N'Lithuanian (Lithuania)', N'lietuvių (Lietuva)', N'yyyy-MM-dd', N'yyyy ''m''. MMMM d ''d''.', N'HH:mm', 1, N'2026 m. spalio 5 d.', N'2026-10-05'),
    (N'lu-CD', N'CD', N'Luba-Katanga (Congo [DRC])', N'Tshiluba (Ditunga wa Kongu)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Lungùdi 2026', N'5/10/2026'),
    (N'luo-KE', N'KE', N'Luo (Kenya)', N'Dholuo (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Dwe mar Apar 2026', N'05/10/2026'),
    (N'luy-KE', N'KE', N'Luyia (Kenya)', N'Luluhia (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Oktoba 2026', N'05/10/2026'),
    (N'lv-LV', N'LV', N'Latvian (Latvia)', N'latviešu (Latvija)', N'dd.MM.yyyy', N'yyyy. ''gada'' d. MMMM', N'HH:mm', 1, N'2026. gada 5. oktobris', N'05.10.2026'),
    (N'mas-KE', N'KE', N'Masai (Kenya)', N'Maa (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Olgísan 2026', N'05/10/2026'),
    (N'mas-TZ', N'TZ', N'Masai (Tanzania)', N'Maa (Tansania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Olgísan 2026', N'05/10/2026'),
    (N'mer-KE', N'KE', N'Meru (Kenya)', N'Kĩmĩrũ (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Oktũba 2026', N'05/10/2026'),
    (N'mfe-MU', N'MU', N'Morisyen (Mauritius)', N'kreol morisien (Moris)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktob 2026', N'5/10/2026'),
    (N'mg-MG', N'MG', N'Malagasy (Madagascar)', N'Malagasy (Madagasikara)', N'yyyy-MM-dd', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktobra 2026', N'2026-10-05'),
    (N'mgh-MZ', N'MZ', N'Makhuwa-Meetto (Mozambique)', N'Makua (Umozambiki)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mweri wo kumi 2026', N'05/10/2026'),
    (N'mgo-CM', N'CM', N'Metaʼ (Cameroon)', N'metaʼ (Kamalun)', N'yyyy-MM-dd', N'yyyy MMMM dd', N'HH:mm', 1, N'2026 iməg tèsiʼe 05', N'2026-10-05'),
    (N'mi-NZ', N'NZ', N'Maori (New Zealand)', N'Māori (Aotearoa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm', 1, N'2026 Whiringa-ā-nuku 5', N'2026-10-05'),
    (N'mk-MK', N'MK', N'Macedonian (North Macedonia)', N'македонски (Северна Македонија)', N'dd.M.yyyy', N'dd MMMM yyyy', N'HH:mm', 1, N'05 октомври 2026', N'05.10.2026'),
    (N'ml-IN', N'IN', N'Malayalam (India)', N'മലയാളം (ഇന്ത്യ)', N'd/M/yyyy', N'yyyy, MMMM d', N'h:mm tt', 0, N'2026, ഒക്‌ടോബർ 5', N'5/10/2026'),
    (N'mn-MN', N'MN', N'Mongolian (Mongolia)', N'монгол (Монгол)', N'yyyy.MM.dd', N'yyyy.MM.dd', N'HH:mm', 1, N'2026.10.05', N'2026.10.05'),
    (N'mn-Mong-CN', N'CN', N'Mongolian (Mongolian, China)', N'Mongolian (Mongolian, China)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'mn-Mong-MN', N'MN', N'Mongolian (Mongolian, Mongolia)', N'ᠮᠣᠩᠭᠣᠯ (ᠮᠣᠩᠭᠣᠯ ᠪᠢᠴᠢᠭ᠌, ᠮᠣᠩᠭᠣᠯ)', N'yyyy.MM.dd', N'yyyy ᠣᠨ ᠎᠎᠎ᠤ MMMM᠎᠎ᠢᠢᠨd. ᠋ᠭᠠᠷᠠᠭ', N'HH:mm', 1, N'2026 ᠣᠨ ᠎᠎᠎ᠤ ᠠᠷᠪᠠᠳᠣᠭᠠᠷ ᠰᠠᠷ᠎ᠠ᠎᠎ᠢᠢᠨ5. ᠋ᠭᠠᠷᠠᠭ', N'2026.10.05'),
    (N'moh-CA', N'CA', N'Mohawk (Canada)', N'Mohawk (Canada)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'mr-IN', N'IN', N'Marathi (India)', N'मराठी (भारत)', N'd/M/yyyy', N'd MMMM, yyyy', N'h:mm tt', 0, N'5 ऑक्टोबर, 2026', N'5/10/2026'),
    (N'ms-BN', N'BN', N'Malay (Brunei)', N'Melayu (Brunei)', N'd/MM/yyyy', N'dd MMMM yyyy', N'h:mm tt', 1, N'05 Oktober 2026', N'5/10/2026'),
    (N'ms-MY', N'MY', N'Malay (Malaysia)', N'Melayu (Malaysia)', N'd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Oktober 2026', N'5/10/2026'),
    (N'ms-SG', N'SG', N'Malay (Singapore)', N'Melayu (Singapura)', N'd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 Oktober 2026', N'5/10/2026'),
    (N'mt-MT', N'MT', N'Maltese (Malta)', N'Malti (Malta)', N'dd/MM/yyyy', N'd ''ta''’ MMMM yyyy', N'HH:mm', 0, N'5 ta’ Ottubru 2026', N'05/10/2026'),
    (N'mua-CM', N'CM', N'Mundang (Cameroon)', N'MUNDAŊ (kameruŋ)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Fĩi Mundaŋ 2026', N'5/10/2026'),
    (N'my-MM', N'MM', N'Burmese (Myanmar)', N'မြန်မာ (မြန်မာ)', N'dd-MM-yyyy', N'yyyy၊ MMMM d၊', N'tt H:mm', 0, N'2026၊ အောက်တိုဘာ 5၊', N'05-10-2026'),
    (N'mzn-IR', N'IR', N'Mazanderani (Iran)', N'مازرونی (ایران)', N'g yyyy-MM-dd', N'g yyyy MMMM d', N'HH:mm', 6, N'م. 2026 اکتبر 5', N'م. 2026-10-05'),
    (N'naq-NA', N'NA', N'Nama (Namibia)', N'Khoekhoegowab (Namibiab)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 ǂNûǁnâiseb 2026', N'05/10/2026'),
    (N'nb-NO', N'NO', N'Norwegian Bokmål (Norway)', N'norsk bokmål (Norge)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'nb-SJ', N'SJ', N'Norwegian Bokmål (Svalbard & Jan Mayen)', N'norsk bokmål (Svalbard og Jan Mayen)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'nd-ZW', N'ZW', N'North Ndebele (Zimbabwe)', N'isiNdebele (Zimbabwe)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Mfumfu 2026', N'05/10/2026'),
    (N'nds-DE', N'DE', N'Low German (Germany)', N'Low German (Germany)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'nds-NL', N'NL', N'Low German (Netherlands)', N'Low German (Netherlands)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'ne-IN', N'IN', N'Nepali (India)', N'नेपाली (भारत)', N'yyyy/M/d', N'yyyy MMMM d', N'h:mm tt', 0, N'2026 अक्टोबर 5', N'2026/10/5'),
    (N'ne-NP', N'NP', N'Nepali (Nepal)', N'नेपाली (नेपाल)', N'yyyy/M/d', N'yyyy MMMM d', N'HH:mm', 0, N'2026 अक्टोबर 5', N'2026/10/5'),
    (N'nl-AW', N'AW', N'Dutch (Aruba)', N'Nederlands (Aruba)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'nl-BE', N'BE', N'Dutch (Belgium)', N'Nederlands (België)', N'd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'5/10/2026'),
    (N'nl-BQ', N'BQ', N'Dutch (Bonaire, Sint Eustatius and Saba)', N'Nederlands (Bonaire, Sint Eustatius en Saba)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'nl-CW', N'CW', N'Dutch (Curaçao)', N'Nederlands (Curaçao)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'nl-NL', N'NL', N'Dutch (Netherlands)', N'Nederlands (Nederland)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'nl-SR', N'SR', N'Dutch (Suriname)', N'Nederlands (Suriname)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026')
) AS s (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort)
ON t.Code = s.Code
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.EnglishName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.EnglishName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.NativeName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.NativeName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.LongDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.LongDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortTimePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortTimePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.FirstDayOfWeek AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.FirstDayOfWeek AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleLong AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleLong AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleShort AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleShort AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, EnglishName = s.EnglishName, NativeName = s.NativeName, ShortDatePattern = s.ShortDatePattern, LongDatePattern = s.LongDatePattern, ShortTimePattern = s.ShortTimePattern, FirstDayOfWeek = s.FirstDayOfWeek, SampleLong = s.SampleLong, SampleShort = s.SampleShort
WHEN NOT MATCHED THEN INSERT (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort) VALUES (s.Code, s.CountryCode, s.EnglishName, s.NativeName, s.ShortDatePattern, s.LongDatePattern, s.ShortTimePattern, s.FirstDayOfWeek, s.SampleLong, s.SampleShort);
PRINT '  [=] DateLocales: rows 201–400 merged.';

MERGE DateLocales AS t
USING (VALUES
    (N'nl-SX', N'SX', N'Dutch (Sint Maarten)', N'Nederlands (Sint-Maarten)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'nmg-CM', N'CM', N'Kwasio (Cameroon)', N'Kwasio (Cameroon)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ngwɛn wum 2026', N'5/10/2026'),
    (N'nn-NO', N'NO', N'Norwegian Nynorsk (Norway)', N'nynorsk (Noreg)', N'dd.MM.yyyy', N'd. MMMM yyyy', N'HH:mm', 1, N'5. oktober 2026', N'05.10.2026'),
    (N'nnh-CM', N'CM', N'Ngiemboon (Cameroon)', N'Shwóŋò ngiembɔɔn (Kàmalûm)', N'dd/MM/yyyy', N'̌ʼ d ''na'' MMMM, yyyy', N'HH:mm', 1, N'̌ʼ 5 na saŋ tàŋa tsetsáʼ, 2026', N'05/10/2026'),
    (N'nqo-GN', N'GN', N'N’Ko (Guinea)', N'N’Ko (Guinea)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'nr-ZA', N'ZA', N'South Ndebele (South Africa)', N'South Ndebele (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'nso-ZA', N'ZA', N'Sesotho sa Leboa (South Africa)', N'Sesotho sa Leboa (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Diphalane 5', N'2026-10-05'),
    (N'nus-SS', N'SS', N'Nuer (South Sudan)', N'Nuer (South Sudan)', N'd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Laath 2026', N'5/10/2026'),
    (N'nyn-UG', N'UG', N'Nyankole (Uganda)', N'Runyankore (Uganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Okwaikumi 2026', N'05/10/2026'),
    (N'oc-FR', N'FR', N'Occitan (France)', N'Occitan (France)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'om-ET', N'ET', N'Oromo (Ethiopia)', N'Oromoo (Itoophiyaa)', N'dd/MM/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'Onkololeessa 5, 2026', N'05/10/2026'),
    (N'om-KE', N'KE', N'Oromo (Kenya)', N'Oromoo (Keeniyaa)', N'dd/MM/yyyy', N'MMMM d, yyyy', N'HH:mm', 0, N'Onkololeessa 5, 2026', N'05/10/2026'),
    (N'or-IN', N'IN', N'Odia (India)', N'ଓଡ଼ିଆ (ଭାରତ)', N'M/d/yyyy', N'MMMM d, yyyy', N'h:mm tt', 0, N'ଅକ୍ଟୋବର 5, 2026', N'10/5/2026'),
    (N'os-GE', N'GE', N'Ossetic (Georgia)', N'ирон (Гуырдзыстон)', N'dd.MM.yyyy', N'd MMMM, yyyy ''аз''', N'HH:mm', 1, N'5 октябры, 2026 аз', N'05.10.2026'),
    (N'os-RU', N'RU', N'Ossetic (Russia)', N'ирон (Уӕрӕсе)', N'dd.MM.yyyy', N'd MMMM, yyyy ''аз''', N'HH:mm', 1, N'5 октябры, 2026 аз', N'05.10.2026'),
    (N'pa-Arab-PK', N'PK', N'Punjabi (Arabic, Pakistan)', N'پنجابی (عربی, پاکستان)', N'dd/MM/yyyy', N'dd MMMM yyyy', N'h:mm tt', 0, N'05 اکتوبر 2026', N'05/10/2026'),
    (N'pa-Guru-IN', N'IN', N'Punjabi (Gurmukhi, India)', N'ਪੰਜਾਬੀ (ਗੁਰਮੁਖੀ, ਭਾਰਤ)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 0, N'5 ਅਕਤੂਬਰ 2026', N'5/10/2026'),
    (N'pl-PL', N'PL', N'Polish (Poland)', N'polski (Polska)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 października 2026', N'05.10.2026'),
    (N'ps-AF', N'AF', N'Pashto (Afghanistan)', N'پښتو (افغانستان)', N'g yyyy/M/d', N'g yyyy د MMMM d', N'H:mm', 6, N'م. 2026 د اکتوبر 5', N'م. 2026/10/5'),
    (N'ps-PK', N'PK', N'Pashto (Pakistan)', N'پښتو (پاکستان)', N'yyyy/M/d', N'yyyy د MMMM d', N'h:mm tt', 0, N'2026 د اکتوبر 5', N'2026/10/5'),
    (N'pt-AO', N'AO', N'Portuguese (Angola)', N'português (Angola)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-BR', N'BR', N'Portuguese (Brazil)', N'português (Brasil)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-CH', N'CH', N'Portuguese (Switzerland)', N'português (Suíça)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-CV', N'CV', N'Portuguese (Cabo Verde)', N'português (Cabo Verde)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-GQ', N'GQ', N'Portuguese (Equatorial Guinea)', N'português (Guiné Equatorial)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-GW', N'GW', N'Portuguese (Guinea-Bissau)', N'português (Guiné-Bissau)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-LU', N'LU', N'Portuguese (Luxembourg)', N'português (Luxemburgo)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-MO', N'MO', N'Portuguese (Macao SAR)', N'português (RAE de Macau)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'h:mm tt', 0, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-MZ', N'MZ', N'Portuguese (Mozambique)', N'português (Moçambique)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-PT', N'PT', N'Portuguese (Portugal)', N'português (Portugal)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-ST', N'ST', N'Portuguese (São Tomé & Príncipe)', N'português (São Tomé e Príncipe)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'pt-TL', N'TL', N'Portuguese (Timor-Leste)', N'português (Timor-Leste)', N'dd/MM/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 1, N'5 de outubro de 2026', N'05/10/2026'),
    (N'qu-BO', N'BO', N'Quechua (Bolivia)', N'Runasimi (Bolivia)', N'dd/MM/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 Octubre, 2026', N'05/10/2026'),
    (N'qu-EC', N'EC', N'Quechua (Ecuador)', N'Runasimi (Ecuador)', N'dd/MM/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 Octubre, 2026', N'05/10/2026'),
    (N'qu-PE', N'PE', N'Quechua (Peru)', N'Runasimi (Perú)', N'dd/MM/yyyy', N'd MMMM, yyyy', N'HH:mm', 0, N'5 Octubre, 2026', N'05/10/2026'),
    (N'quc-GT', N'GT', N'Kʼicheʼ (Guatemala)', N'Kʼicheʼ (Guatemala)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'rm-CH', N'CH', N'Romansh (Switzerland)', N'rumantsch (Svizra)', N'dd-MM-yyyy', N'd ''da'' MMMM yyyy', N'HH:mm', 1, N'5 da october 2026', N'05-10-2026'),
    (N'rn-BI', N'BI', N'Rundi (Burundi)', N'Ikirundi (Uburundi)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Gitugutu 2026', N'5/10/2026'),
    (N'ro-MD', N'MD', N'Romanian (Moldova)', N'română (Republica Moldova)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octombrie 2026', N'05.10.2026'),
    (N'ro-RO', N'RO', N'Romanian (Romania)', N'română (România)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 octombrie 2026', N'05.10.2026'),
    (N'rof-TZ', N'TZ', N'Rombo (Tanzania)', N'Kihorombo (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Mweri wa ikumi 2026', N'05/10/2026'),
    (N'ru-BY', N'BY', N'Russian (Belarus)', N'русский (Беларусь)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'ru-KG', N'KG', N'Russian (Kyrgyzstan)', N'русский (Киргизия)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'ru-KZ', N'KZ', N'Russian (Kazakhstan)', N'русский (Казахстан)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'ru-MD', N'MD', N'Russian (Moldova)', N'русский (Молдова)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'ru-RU', N'RU', N'Russian (Russia)', N'русский (Россия)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'ru-UA', N'UA', N'Russian (Ukraine)', N'русский (Украина)', N'dd.MM.yyyy', N'd MMMM yyyy ''г''.', N'HH:mm', 1, N'5 октября 2026 г.', N'05.10.2026'),
    (N'rw-RW', N'RW', N'Kinyarwanda (Rwanda)', N'Kinyarwanda (U Rwanda)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 Ukwakira 5', N'2026-10-05'),
    (N'rwk-TZ', N'TZ', N'Rwa (Tanzania)', N'Kiruwa (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'sa-IN', N'IN', N'Sanskrit (India)', N'Sanskrit (India)', N'd-MM-yyyy', N'd MMMM yyyy', N'hh:mm tt', 0, N'5 M10 2026', N'5-10-2026'),
    (N'sah-RU', N'RU', N'Sakha (Russia)', N'саха тыла (Арассыыйа)', N'yyyy/M/d', N'yyyy ''сыл'' MMMM d ''күнэ''', N'HH:mm', 1, N'2026 сыл Алтынньы 5 күнэ', N'2026/10/5'),
    (N'saq-KE', N'KE', N'Samburu (Kenya)', N'Kisampur (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Lapa le tomon 2026', N'05/10/2026'),
    (N'sbp-TZ', N'TZ', N'Sangu (Tanzania)', N'Ishisangu (Tansaniya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Mokhu 2026', N'05/10/2026'),
    (N'sd-PK', N'PK', N'Sindhi (Pakistan)', N'سنڌي (پاڪستان)', N'yyyy-MM-dd', N'yyyy MMMM d', N'h:mm tt', 0, N'2026 آڪٽوبر 5', N'2026-10-05'),
    (N'se-FI', N'FI', N'Northern Sami (Finland)', N'davvisámegiella (Suopma)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 golggotmánnu 2026', N'05.10.2026'),
    (N'se-NO', N'NO', N'Northern Sami (Norway)', N'davvisámegiella (Norga)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 golggotmánnu 5', N'2026-10-05'),
    (N'se-SE', N'SE', N'Northern Sami (Sweden)', N'davvisámegiella (Ruoŧŧa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 golggotmánnu 5', N'2026-10-05'),
    (N'seh-MZ', N'MZ', N'Sena (Mozambique)', N'sena (Moçambique)', N'd/M/yyyy', N'd ''de'' MMMM ''de'' yyyy', N'HH:mm', 0, N'5 de Otubro de 2026', N'5/10/2026'),
    (N'ses-ML', N'ML', N'Koyraboro Senni (Mali)', N'Koyraboro senni (Maali)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoobur 2026', N'5/10/2026'),
    (N'sg-CF', N'CF', N'Sango (Central African Republic)', N'Sängö (Ködörösêse tî Bêafrîka)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Ngberere 2026', N'5/10/2026'),
    (N'shi-Latn-MA', N'MA', N'Tachelhit (Latin, Morocco)', N'Tachelhit (Latin, Morocco)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ktubr 2026', N'5/10/2026'),
    (N'shi-Tfng-MA', N'MA', N'Tachelhit (Tifinagh, Morocco)', N'Tachelhit (Tifinagh, Morocco)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ⴽⵜⵓⴱⵔ 2026', N'5/10/2026'),
    (N'si-LK', N'LK', N'Sinhala (Sri Lanka)', N'සිංහල (ශ්‍රී ලංකාව)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH.mm', 1, N'2026 ඔක්තෝබර් 5', N'2026-10-05'),
    (N'sk-SK', N'SK', N'Slovak (Slovakia)', N'slovenčina (Slovensko)', N'd. M. yyyy', N'd. MMMM yyyy', N'H:mm', 1, N'5. októbra 2026', N'5. 10. 2026'),
    (N'sl-SI', N'SI', N'Slovenian (Slovenia)', N'slovenščina (Slovenija)', N'd. MM. yyyy', N'dd. MMMM yyyy', N'HH:mm', 1, N'05. oktober 2026', N'5. 10. 2026'),
    (N'sma-NO', N'NO', N'Southern Sami (Norway)', N'Southern Sami (Norway)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'sma-SE', N'SE', N'Southern Sami (Sweden)', N'Southern Sami (Sweden)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'smj-NO', N'NO', N'Lule Sami (Norway)', N'Lule Sami (Norway)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'smj-SE', N'SE', N'Lule Sami (Sweden)', N'Lule Sami (Sweden)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'smn-FI', N'FI', N'Inari Sami (Finland)', N'anarâškielâ (Suomâ)', N'd.M.yyyy', N'MMMM d. yyyy', N'H.mm', 1, N'roovvâdmáánu 5. 2026', N'5.10.2026'),
    (N'sms-FI', N'FI', N'Skolt Sami (Finland)', N'Skolt Sami (Finland)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'sn-ZW', N'ZW', N'Shona (Zimbabwe)', N'chiShona (Zimbabwe)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Gumiguru 5', N'2026-10-05'),
    (N'so-DJ', N'DJ', N'Somali (Djibouti)', N'Soomaali (Jabuuti)', N'dd/MM/yyyy', N'MMMM dd, yyyy', N'h:mm tt', 6, N'Bisha Tobnaad 05, 2026', N'05/10/2026'),
    (N'so-ET', N'ET', N'Somali (Ethiopia)', N'Soomaali (Itoobiya)', N'dd/MM/yyyy', N'MMMM dd, yyyy', N'h:mm tt', 0, N'Bisha Tobnaad 05, 2026', N'05/10/2026'),
    (N'so-KE', N'KE', N'Somali (Kenya)', N'Soomaali (Kenya)', N'dd/MM/yyyy', N'MMMM dd, yyyy', N'HH:mm', 0, N'Bisha Tobnaad 05, 2026', N'05/10/2026'),
    (N'so-SO', N'SO', N'Somali (Somalia)', N'Soomaali (Soomaaliya)', N'dd/MM/yyyy', N'MMMM dd, yyyy', N'h:mm tt', 1, N'Bisha Tobnaad 05, 2026', N'05/10/2026'),
    (N'sq-AL', N'AL', N'Albanian (Albania)', N'shqip (Shqipëri)', N'd.M.yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 tetor 2026', N'5.10.2026'),
    (N'sq-MK', N'MK', N'Albanian (North Macedonia)', N'shqip (Maqedonia e Veriut)', N'd.M.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 tetor 2026', N'5.10.2026'),
    (N'sr-Cyrl-BA', N'BA', N'Serbian (Cyrillic, Bosnia & Herzegovina)', N'српски (ћирилица, Босна и Херцеговина)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. октобар 2026.', N'5.10.2026.'),
    (N'sr-Cyrl-ME', N'ME', N'Serbian (Cyrillic, Montenegro)', N'српски (ћирилица, Црна Гора)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. октобар 2026.', N'5.10.2026.'),
    (N'sr-Cyrl-RS', N'RS', N'Serbian (Cyrillic, Serbia)', N'српски (ћирилица, Србија)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. октобар 2026.', N'5.10.2026.'),
    (N'sr-Latn-BA', N'BA', N'Serbian (Latin, Bosnia & Herzegovina)', N'srpski (latinica, Bosna i Hercegovina)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. oktobar 2026.', N'5.10.2026.'),
    (N'sr-Latn-ME', N'ME', N'Serbian (Latin, Montenegro)', N'srpski (latinica, Crna Gora)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. oktobar 2026.', N'5.10.2026.'),
    (N'sr-Latn-RS', N'RS', N'Serbian (Latin, Serbia)', N'srpski (latinica, Srbija)', N'd.M.yyyy.', N'dd. MMMM yyyy.', N'HH:mm', 1, N'05. oktobar 2026.', N'5.10.2026.'),
    (N'ss-SZ', N'SZ', N'siSwati (Eswatini)', N'siSwati (eSwatini)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'ss-ZA', N'ZA', N'siSwati (South Africa)', N'siSwati (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'ssy-ER', N'ER', N'Saho (Eritrea)', N'Saho (Eritrea)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'st-LS', N'LS', N'Sesotho (Lesotho)', N'Sesotho (Lesotho)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 Mphalane 5', N'2026-10-05'),
    (N'st-ZA', N'ZA', N'Sesotho (South Africa)', N'Sesotho (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Mphalane 5', N'2026-10-05'),
    (N'sv-AX', N'AX', N'Swedish (Åland Islands)', N'svenska (Åland)', N'yyyy-MM-dd', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'2026-10-05'),
    (N'sv-FI', N'FI', N'Swedish (Finland)', N'svenska (Finland)', N'dd-MM-yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'05-10-2026'),
    (N'sv-SE', N'SE', N'Swedish (Sweden)', N'svenska (Sverige)', N'yyyy-MM-dd', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktober 2026', N'2026-10-05'),
    (N'sw-CD', N'CD', N'Kiswahili (Congo [DRC])', N'Kiswahili (Jamhuri ya Kidemokrasia ya Kongo)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'sw-KE', N'KE', N'Kiswahili (Kenya)', N'Kiswahili (Kenya)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Oktoba 2026', N'05/10/2026'),
    (N'sw-TZ', N'TZ', N'Kiswahili (Tanzania)', N'Kiswahili (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'sw-UG', N'UG', N'Kiswahili (Uganda)', N'Kiswahili (Uganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'syr-SY', N'SY', N'Syriac (Syria)', N'Syriac (Syria)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 6, N'2026 ܏ܬܫ ܏ܐ 5', N'2026-10-05'),
    (N'ta-IN', N'IN', N'Tamil (India)', N'தமிழ் (இந்தியா)', N'd/M/yyyy', N'd MMMM, yyyy', N'tt h:mm', 0, N'5 அக்டோபர், 2026', N'5/10/2026'),
    (N'ta-LK', N'LK', N'Tamil (Sri Lanka)', N'தமிழ் (இலங்கை)', N'd/M/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 அக்டோபர், 2026', N'5/10/2026'),
    (N'ta-MY', N'MY', N'Tamil (Malaysia)', N'தமிழ் (மலேசியா)', N'd/M/yyyy', N'd MMMM, yyyy', N'tt h:mm', 1, N'5 அக்டோபர், 2026', N'5/10/2026'),
    (N'ta-SG', N'SG', N'Tamil (Singapore)', N'தமிழ் (சிங்கப்பூர்)', N'd/M/yyyy', N'd MMMM, yyyy', N'tt h:mm', 0, N'5 அக்டோபர், 2026', N'5/10/2026'),
    (N'te-IN', N'IN', N'Telugu (India)', N'తెలుగు (భారతదేశం)', N'dd-MM-yyyy', N'd, MMMM yyyy', N'h:mm tt', 0, N'5, అక్టోబర్ 2026', N'05-10-2026'),
    (N'teo-KE', N'KE', N'Teso (Kenya)', N'Kiteso (Kenia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 0, N'5 Otibar 2026', N'05/10/2026'),
    (N'teo-UG', N'UG', N'Teso (Uganda)', N'Kiteso (Uganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Otibar 2026', N'05/10/2026'),
    (N'tg-TJ', N'TJ', N'Tajik (Tajikistan)', N'тоҷикӣ (Тоҷикистон)', N'dd/MM/yyyy', N'dd MMMM yyyy', N'HH:mm', 1, N'05 Октябр 2026', N'05/10/2026'),
    (N'th-TH', N'TH', N'Thai (Thailand)', N'ไทย (ไทย)', N'd/M/yyyy', N'd MMMM g yyyy', N'HH:mm', 0, N'5 ตุลาคม ค.ศ. 2026', N'5/10/2026'),
    (N'ti-ER', N'ER', N'Tigrinya (Eritrea)', N'ትግርኛ (ኤርትራ)', N'dd/MM/yyyy', N'dd MMMM መዓልቲ yyyy g', N'h:mm tt', 1, N'05 ጥቅምቲ መዓልቲ 2026 ዓ/ም', N'05/10/2026'),
    (N'ti-ET', N'ET', N'Tigrinya (Ethiopia)', N'ትግርኛ (ኢትዮጵያ)', N'dd/MM/yyyy', N'dd MMMM መዓልቲ yyyy g', N'h:mm tt', 0, N'05 ጥቅምቲ መዓልቲ 2026 ዓ/ም', N'05/10/2026'),
    (N'tig-ER', N'ER', N'Tigre (Eritrea)', N'Tigre (Eritrea)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 1, N'2026 M10 5', N'2026-10-05'),
    (N'tk-TM', N'TM', N'Turkmen (Turkmenistan)', N'türkmen dili (Türkmenistan)', N'dd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 oktýabr 2026', N'05.10.2026'),
    (N'tn-BW', N'BW', N'Setswana (Botswana)', N'Setswana (Botswana)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Diphalane 5', N'2026-10-05'),
    (N'tn-ZA', N'ZA', N'Setswana (South Africa)', N'Setswana (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Diphalane 5', N'2026-10-05'),
    (N'to-TO', N'TO', N'Tongan (Tonga)', N'lea fakatonga (Tonga)', N'd/M/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 ʻOkatopa 2026', N'5/10/2026'),
    (N'tr-CY', N'CY', N'Turkish (Cyprus)', N'Türkçe (Kıbrıs)', N'd.MM.yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 Ekim 2026', N'5.10.2026'),
    (N'tr-TR', N'TR', N'Turkish (Turkey)', N'Türkçe (Türkiye)', N'd.MM.yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Ekim 2026', N'5.10.2026'),
    (N'ts-ZA', N'ZA', N'Xitsonga (South Africa)', N'Xitsonga (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Nhlangula 5', N'2026-10-05'),
    (N'tt-RU', N'RU', N'Tatar (Russia)', N'татар (Россия)', N'dd.MM.yyyy', N'd MMMM, yyyy ''ел''', N'H:mm', 1, N'5 октябрь, 2026 ел', N'05.10.2026'),
    (N'twq-NE', N'NE', N'Tasawaq (Niger)', N'Tasawaq senni (Nižer)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoobur 2026', N'5/10/2026'),
    (N'tzm-MA', N'MA', N'Central Atlas Tamazight (Morocco)', N'Tamaziɣt n laṭlaṣ (Meṛṛuk)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Kṭuber 2026', N'05/10/2026'),
    (N'ug-CN', N'CN', N'Uyghur (China)', N'ئۇيغۇرچە (جۇڭگو)', N'yyyy-MM-dd', N'yyyy d-MMMM', N'h:mm tt', 0, N'2026 5-ئۆكتەبىر', N'2026-10-05'),
    (N'uk-UA', N'UA', N'Ukrainian (Ukraine)', N'українська (Україна)', N'dd.MM.yyyy', N'd MMMM yyyy ''р''.', N'HH:mm', 1, N'5 жовтня 2026 р.', N'05.10.2026'),
    (N'ur-IN', N'IN', N'Urdu (India)', N'اردو (بھارت)', N'd/M/yyyy', N'd MMMM، yyyy', N'h:mm tt', 0, N'5 اکتوبر، 2026', N'5/10/2026'),
    (N'ur-PK', N'PK', N'Urdu (Pakistan)', N'اردو (پاکستان)', N'd/M/yyyy', N'd MMMM، yyyy', N'h:mm tt', 0, N'5 اکتوبر، 2026', N'5/10/2026'),
    (N'uz-Arab-AF', N'AF', N'Uzbek (Arabic, Afghanistan)', N'اوزبیک (عربی, افغانستان)', N'g yyyy-MM-dd', N'g yyyy MMMM d', N'HH:mm', 6, N'CE 2026 اکتوبر 5', N'CE 2026-10-05'),
    (N'uz-Cyrl-UZ', N'UZ', N'Uzbek (Cyrillic, Uzbekistan)', N'ўзбекча (Кирил, Ўзбекистон)', N'dd/MM/yyyy', N'dd MMMM, yyyy', N'HH:mm', 1, N'05 октябр, 2026', N'05/10/2026'),
    (N'uz-Latn-UZ', N'UZ', N'Uzbek (Latin, Uzbekistan)', N'o‘zbek (lotin, Oʻzbekiston)', N'dd/MM/yyyy', N'd-MMMM, yyyy', N'HH:mm', 1, N'5-oktabr, 2026', N'05/10/2026'),
    (N'vai-Latn-LR', N'LR', N'Vai (Latin, Liberia)', N'Vai (Latin, Liberia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 galo 2026', N'05/10/2026'),
    (N'vai-Vaii-LR', N'LR', N'Vai (Vai, Liberia)', N'Vai (Vai, Liberia)', N'dd/MM/yyyy', N'd MMMM yyyy', N'h:mm tt', 1, N'5 ꕭꖃ 2026', N'05/10/2026'),
    (N've-ZA', N'ZA', N'Venda (South Africa)', N'Venda (South Africa)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'vi-VN', N'VN', N'Vietnamese (Vietnam)', N'Tiếng Việt (Việt Nam)', N'dd/MM/yyyy', N'd MMMM, yyyy', N'HH:mm', 1, N'5 tháng 10, 2026', N'05/10/2026'),
    (N'vun-TZ', N'TZ', N'Vunjo (Tanzania)', N'Kyivunjo (Tanzania)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Oktoba 2026', N'05/10/2026'),
    (N'wae-CH', N'CH', N'Walser (Switzerland)', N'Walser (Schwiz)', N'yyyy-MM-dd', N'd. MMMM yyyy', N'HH:mm', 1, N'5. Wímánet 2026', N'2026-10-05'),
    (N'wal-ET', N'ET', N'Wolaytta (Ethiopia)', N'Wolaytta (Ethiopia)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 M10 5', N'2026-10-05'),
    (N'wo-SN', N'SN', N'Wolof (Senegal)', N'Wolof (Senegaal)', N'dd-MM-yyyy', N'd MMM, yyyy', N'HH:mm', 1, N'5 Okt, 2026', N'05-10-2026'),
    (N'xh-ZA', N'ZA', N'isiXhosa (South Africa)', N'isiXhosa (eMzantsi Afrika)', N'yyyy-MM-dd', N'yyyy MMMM d', N'HH:mm', 0, N'2026 Okthoba 5', N'2026-10-05'),
    (N'xog-UG', N'UG', N'Soga (Uganda)', N'Olusoga (Yuganda)', N'dd/MM/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 Okitobba 2026', N'05/10/2026'),
    (N'yav-CM', N'CM', N'Yangben (Cameroon)', N'nuasue (Kemelún)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 imɛŋ i putúk,oóli ú kátíɛ 2026', N'5/10/2026'),
    (N'yo-BJ', N'BJ', N'Yoruba (Benin)', N'Èdè Yorùbá (Orílɛ́ède Bɛ̀nɛ̀)', N'd/M/yyyy', N'd MMM yyyy', N'H:m', 1, N'5 Ɔ̀wà 2026', N'5/10/2026'),
    (N'yo-NG', N'NG', N'Yoruba (Nigeria)', N'Èdè Yorùbá (Orilẹ̀-èdè Nàìjíríà)', N'd/M/yyyy', N'd MMM yyyy', N'H:m', 1, N'5 Ọ̀wà 2026', N'5/10/2026'),
    (N'zgh-MA', N'MA', N'Standard Moroccan Tamazight (Morocco)', N'ⵜⴰⵎⴰⵣⵉⵖⵜ (ⵍⵎⵖⵔⵉⴱ)', N'd/M/yyyy', N'd MMMM yyyy', N'HH:mm', 1, N'5 ⴽⵜⵓⴱⵔ 2026', N'5/10/2026'),
    (N'zh-Hans-CN', N'CN', N'Chinese (Simplified, China)', N'中文（简体，中国）', N'yyyy/M/d', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'2026/10/5'),
    (N'zh-Hans-HK', N'HK', N'Chinese (Simplified, Hong Kong SAR)', N'中文（简体，香港特别行政区）', N'd/M/yyyy', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'5/10/2026'),
    (N'zh-Hans-MO', N'MO', N'Chinese (Simplified, Macao SAR)', N'中文（简体，澳门特别行政区）', N'd/M/yyyy', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'5/10/2026'),
    (N'zh-Hans-SG', N'SG', N'Chinese (Simplified, Singapore)', N'中文（简体，新加坡）', N'dd/MM/yyyy', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'05/10/2026'),
    (N'zh-Hant-HK', N'HK', N'Chinese (Traditional, Hong Kong SAR)', N'中文（繁體字，香港特別行政區）', N'd/M/yyyy', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'5/10/2026'),
    (N'zh-Hant-MO', N'MO', N'Chinese (Traditional, Macao SAR)', N'中文（繁體字，澳門特別行政區）', N'd/M/yyyy', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'5/10/2026'),
    (N'zh-Hant-TW', N'TW', N'Chinese (Traditional, Taiwan)', N'中文（繁體，台灣）', N'yyyy/M/d', N'yyyy年M月d日', N'tth:mm', 0, N'2026年10月5日', N'2026/10/5'),
    (N'zu-ZA', N'ZA', N'isiZulu (South Africa)', N'isiZulu (iNingizimu Afrika)', N'M/d/yyyy', N'MMMM d, yyyy', N'HH:mm', 0, N'Okthoba 5, 2026', N'10/5/2026')
) AS s (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort)
ON t.Code = s.Code
WHEN MATCHED AND (ISNULL(CAST(t.CountryCode AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.CountryCode AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.EnglishName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.EnglishName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.NativeName AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.NativeName AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.LongDatePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.LongDatePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.ShortTimePattern AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.ShortTimePattern AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.FirstDayOfWeek AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.FirstDayOfWeek AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleLong AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleLong AS NVARCHAR(200)), N'') OR ISNULL(CAST(t.SampleShort AS NVARCHAR(200)), N'') <> ISNULL(CAST(s.SampleShort AS NVARCHAR(200)), N''))
    THEN UPDATE SET CountryCode = s.CountryCode, EnglishName = s.EnglishName, NativeName = s.NativeName, ShortDatePattern = s.ShortDatePattern, LongDatePattern = s.LongDatePattern, ShortTimePattern = s.ShortTimePattern, FirstDayOfWeek = s.FirstDayOfWeek, SampleLong = s.SampleLong, SampleShort = s.SampleShort
WHEN NOT MATCHED THEN INSERT (Code, CountryCode, EnglishName, NativeName, ShortDatePattern, LongDatePattern, ShortTimePattern, FirstDayOfWeek, SampleLong, SampleShort) VALUES (s.Code, s.CountryCode, s.EnglishName, s.NativeName, s.ShortDatePattern, s.LongDatePattern, s.ShortTimePattern, s.FirstDayOfWeek, s.SampleLong, s.SampleShort);
PRINT '  [=] DateLocales: rows 401–548 merged.';

PRINT 'Countries, time zones and date locales are up to date.';
SET NOCOUNT OFF;

-- =============================================================================

PRINT '';
PRINT '==========================================================';
PRINT 'Schema init complete: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT 'All tables verified. Safe to run multiple times.';
PRINT '==========================================================';
