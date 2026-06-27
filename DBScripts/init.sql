-- =============================================================================
-- init.sql  —  Blogfront / Inkwell Complete Database Schema
-- Version : 1.0.1 (2026-06-27)
-- Target  : SQL Server 2019+ / Azure SQL
-- Usage   : Run once on a fresh database. Every block is idempotent (IF NOT
--           EXISTS) so it is safe to re-run against an existing database.
-- Note    : MigrationService.cs also applies this schema on startup, so a
--           manual run is optional for self-hosted installs.
-- =============================================================================

PRINT '==========================================================';
PRINT 'Blogfront / Inkwell — Full Schema Init v1.0.1';
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

PRINT '';
PRINT '==========================================================';
PRINT 'Schema init complete: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT 'All 21 tables verified. Safe to run multiple times.';
PRINT '==========================================================';
