-- =============================================================================
-- init.sql  —  Blogfront / Inkwell Complete Database Schema
-- Version : 1.0.6 (2026-09-24)
-- Target  : SQL Server 2019+ / Azure SQL
-- Usage   : Run once on a fresh database. Every block is idempotent (IF NOT
--           EXISTS) so it is safe to re-run against an existing database.
-- Note    : MigrationService.cs also applies this schema on startup, so a
--           manual run is optional for self-hosted installs.
-- =============================================================================

PRINT '==========================================================';
PRINT 'Blogfront / Inkwell — Full Schema Init v1.0.6-dev';
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

PRINT '';
PRINT '==========================================================';
PRINT 'Schema init complete: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT 'All tables verified. Safe to run multiple times.';
PRINT '==========================================================';
