using Microsoft.Extensions.Logging;

namespace Blog.Infrastructure.Data;

/// <summary>
/// MigrationService is disabled — the SQL Server LocalDB 'blog' database
/// already has all beacon_ tables created manually via SSMS.
/// </summary>
public class MigrationService
{
    private readonly DapperContext _context;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(DapperContext context, ILogger<MigrationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("MigrationService: Checking for schema updates...");
        try 
        {
            using var connection = _context.CreateConnection();
            // Ensure Color columns exist
            var sql = @"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Categories') AND name = 'Color')
                BEGIN
                    ALTER TABLE Categories ADD Color NVARCHAR(50) NULL DEFAULT '#e8f5e9' WITH VALUES;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tags') AND name = 'Color')
                BEGIN
                    ALTER TABLE Tags ADD Color NVARCHAR(50) NULL DEFAULT '#e3f2fd' WITH VALUES;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Media') AND name = 'OriginalFileName')
                BEGIN
                    ALTER TABLE Media ADD OriginalFileName NVARCHAR(1000) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'FaqJson')
                BEGIN
                    ALTER TABLE Posts ADD FaqJson NVARCHAR(MAX) NULL;
                END

                -- Verdict roundup posts (DBScripts/2026-07-14_add-roundupjson-to-posts.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'RoundupJson')
                BEGIN
                    ALTER TABLE Posts ADD RoundupJson NVARCHAR(MAX) NULL;
                END

                -- Key Facts / At a glance (DBScripts/2026-07-16_add-keyfactsjson-to-posts.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'KeyFactsJson')
                BEGIN
                    ALTER TABLE Posts ADD KeyFactsJson NVARCHAR(MAX) NULL;
                END

                -- HowTo step-by-step guides (DBScripts/2026-07-16_add-howtojson-to-posts.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'HowToJson')
                BEGIN
                    ALTER TABLE Posts ADD HowToJson NVARCHAR(MAX) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Credentials')
                BEGIN
                    ALTER TABLE Users ADD Credentials NVARCHAR(200) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Specialty')
                BEGIN
                    ALTER TABLE Users ADD Specialty NVARCHAR(200) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'LicenseNumber')
                BEGIN
                    ALTER TABLE Users ADD LicenseNumber NVARCHAR(100) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ErrorLogs')
                BEGIN
                    CREATE TABLE ErrorLogs (
                        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                        Fingerprint NVARCHAR(64) NOT NULL,
                        StatusCode INT NOT NULL,
                        Method NVARCHAR(10) NOT NULL DEFAULT 'GET',
                        Path NVARCHAR(1024) NOT NULL,
                        ExceptionType NVARCHAR(256) NULL,
                        Message NVARCHAR(2048) NULL,
                        StackTrace NVARCHAR(MAX) NULL,
                        UserAgent NVARCHAR(512) NULL,
                        Referer NVARCHAR(1024) NULL,
                        OccurrenceCount INT NOT NULL DEFAULT 1,
                        FirstSeenAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        LastSeenAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                    CREATE UNIQUE INDEX UX_ErrorLogs_Fingerprint ON ErrorLogs(Fingerprint);
                    CREATE INDEX IX_ErrorLogs_LastSeenAt ON ErrorLogs(LastSeenAt DESC);
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CustomThemeSettings')
                BEGIN
                    CREATE TABLE CustomThemeSettings (
                        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        UserId UNIQUEIDENTIFIER NOT NULL,
                        SettingGroup NVARCHAR(100) NOT NULL DEFAULT 'general',
                        SettingKey NVARCHAR(200) NOT NULL,
                        SettingType NVARCHAR(50) NOT NULL DEFAULT 'text',
                        SettingValue NVARCHAR(MAX) NULL,
                        DefaultValue NVARCHAR(MAX) NULL,
                        Label NVARCHAR(200) NULL,
                        Description NVARCHAR(500) NULL
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Members')
                BEGIN
                    CREATE TABLE Members (
                        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        Uuid UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
                        Email NVARCHAR(320) NOT NULL,
                        Name NVARCHAR(200) NULL,
                        Note NVARCHAR(2000) NULL,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'pending',
                        Subscribed BIT NOT NULL DEFAULT 0,
                        ConfirmToken NVARCHAR(100) NOT NULL,
                        UnsubscribeToken NVARCHAR(100) NOT NULL,
                        ConfirmedAt DATETIME2 NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        DeletedAt DATETIME2 NULL,
                        CONSTRAINT UQ_Members_Email UNIQUE (Email),
                        CONSTRAINT UQ_Members_ConfirmToken UNIQUE (ConfirmToken),
                        CONSTRAINT UQ_Members_UnsubscribeToken UNIQUE (UnsubscribeToken)
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Newsletters')
                BEGIN
                    CREATE TABLE Newsletters (
                        Id            UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        Name          NVARCHAR(200) NOT NULL,
                        Slug          NVARCHAR(200) NOT NULL,
                        Description   NVARCHAR(2000) NULL,
                        SenderName    NVARCHAR(200) NULL,
                        SenderEmail   NVARCHAR(320) NULL,
                        SenderReplyTo NVARCHAR(320) NULL,
                        Status        NVARCHAR(50) NULL DEFAULT 'active',
                        CreatedAt     DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT UQ_Newsletters_Slug UNIQUE (Slug)
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Labels')
                BEGIN
                    CREATE TABLE Labels (
                        Id   UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        Name NVARCHAR(200) NOT NULL,
                        Slug NVARCHAR(200) NOT NULL,
                        CONSTRAINT UQ_Labels_Slug UNIQUE (Slug)
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MemberLabels')
                BEGIN
                    CREATE TABLE MemberLabels (
                        MemberId UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id) ON DELETE CASCADE,
                        LabelId  UNIQUEIDENTIFIER NOT NULL REFERENCES Labels(Id)  ON DELETE CASCADE,
                        CONSTRAINT PK_MemberLabels PRIMARY KEY (MemberId, LabelId)
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MemberNewsletters')
                BEGIN
                    CREATE TABLE MemberNewsletters (
                        MemberId     UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id)     ON DELETE CASCADE,
                        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE CASCADE,
                        Subscribed   BIT NOT NULL DEFAULT 1,
                        CONSTRAINT PK_MemberNewsletters PRIMARY KEY (MemberId, NewsletterId)
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Emails')
                BEGIN
                    CREATE TABLE Emails (
                        Id           UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        PostId       UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)       ON DELETE CASCADE,
                        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE NO ACTION,
                        Subject      NVARCHAR(500) NOT NULL,
                        Status       NVARCHAR(50) NULL DEFAULT 'pending',
                        OpensCount   INT NOT NULL DEFAULT 0,
                        ClicksCount  INT NOT NULL DEFAULT 0,
                        SentCount    INT NOT NULL DEFAULT 0,
                        CreatedAt    DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Redirects')
                BEGIN
                    CREATE TABLE Redirects (
                        Id        UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        [From]    NVARCHAR(850)  NOT NULL,
                        [To]      NVARCHAR(2000) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        CONSTRAINT UQ_Redirects_From UNIQUE ([From])
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Snippets')
                BEGIN
                    CREATE TABLE Snippets (
                        Id        UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        Name      NVARCHAR(200) NOT NULL,
                        Lexical   NVARCHAR(MAX) NULL,
                        CreatedBy UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE NO ACTION,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PageViews')
                BEGIN
                    CREATE TABLE PageViews (
                        Id        BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        OwnerId   UNIQUEIDENTIFIER     NOT NULL,
                        Path      NVARCHAR(500)        NOT NULL,
                        Referrer  NVARCHAR(1000)       NULL,
                        Source    NVARCHAR(100)        NULL,
                        Medium    NVARCHAR(100)        NULL,
                        Campaign  NVARCHAR(200)        NULL,
                        Term      NVARCHAR(200)        NULL,
                        Content   NVARCHAR(200)        NULL,
                        IpHash    NVARCHAR(64)         NULL,
                        UserAgent NVARCHAR(500)        NULL,
                        Country   NVARCHAR(2)          NULL,
                        Region    NVARCHAR(200)        NULL,
                        CreatedAt DATETIME2            NOT NULL DEFAULT GETUTCDATE()
                    );
                    CREATE INDEX IX_PageViews_OwnerId_CreatedAt ON PageViews (OwnerId, CreatedAt DESC);
                    CREATE INDEX IX_PageViews_OwnerId_Path      ON PageViews (OwnerId, Path);
                    CREATE INDEX IX_PageViews_OwnerId_Source    ON PageViews (OwnerId, Source);
                    CREATE INDEX IX_PageViews_OwnerId_Country   ON PageViews (OwnerId, Country);
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'LastVerifiedAt')
                BEGIN
                    ALTER TABLE Posts ADD LastVerifiedAt DATETIME2 NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'NextReviewAt')
                BEGIN
                    ALTER TABLE Posts ADD NextReviewAt DATETIME2 NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PageViews') AND name = 'Country')
                BEGIN
                    ALTER TABLE PageViews ADD Country NVARCHAR(2) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PageViews') AND name = 'Region')
                BEGIN
                    ALTER TABLE PageViews ADD Region NVARCHAR(200) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('PageViews') AND name = 'IX_PageViews_OwnerId_Country')
                BEGIN
                    CREATE INDEX IX_PageViews_OwnerId_Country ON PageViews (OwnerId, Country);
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
                BEGIN
                    CREATE TABLE AuditLogs (
                        Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        OwnerId    UNIQUEIDENTIFIER     NOT NULL,
                        UserId     UNIQUEIDENTIFIER     NOT NULL,
                        UserName   NVARCHAR(200)        NOT NULL,
                        Action     NVARCHAR(100)        NOT NULL,
                        EntityType NVARCHAR(100)        NOT NULL,
                        EntityId   NVARCHAR(100)        NULL,
                        EntityName NVARCHAR(500)        NULL,
                        OldValues  NVARCHAR(MAX)        NULL,
                        NewValues  NVARCHAR(MAX)        NULL,
                        IpAddress  NVARCHAR(45)         NULL,
                        UserAgent  NVARCHAR(500)        NULL,
                        CreatedAt  DATETIME2            NOT NULL DEFAULT GETUTCDATE()
                    );
                    CREATE INDEX IX_AuditLogs_OwnerId_CreatedAt ON AuditLogs (OwnerId, CreatedAt DESC);
                    CREATE INDEX IX_AuditLogs_EntityType        ON AuditLogs (OwnerId, EntityType, CreatedAt DESC);
                    CREATE INDEX IX_AuditLogs_UserId            ON AuditLogs (OwnerId, UserId, CreatedAt DESC);
                END

                -- Series / collections (DBScripts/2026-07-31_create-series-tables.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Series')
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
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SeriesPosts')
                BEGIN
                    CREATE TABLE SeriesPosts (
                        SeriesId  UNIQUEIDENTIFIER NOT NULL REFERENCES Series(Id) ON DELETE CASCADE,
                        PostId    UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)  ON DELETE CASCADE,
                        SortOrder INT              NOT NULL DEFAULT 0,
                        CONSTRAINT PK_SeriesPosts PRIMARY KEY (SeriesId, PostId)
                    );
                    CREATE INDEX IX_SeriesPosts_SeriesId_SortOrder ON SeriesPosts (SeriesId, SortOrder);
                    CREATE INDEX IX_SeriesPosts_PostId             ON SeriesPosts (PostId);
                END

                -- Content importer (DBScripts/2026-07-31_create-import-tables.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ImportJobs')
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
                END

                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ImportItems')
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
                END

                -- IP firewall rules (DBScripts/2026-08-15_create-ip-firewall-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IpFirewallRules')
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
                END

                -- Retire legacy URLs with 301/302/410 (DBScripts/2026-08-15_add-statuscode-to-redirects.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Redirects') AND name = 'StatusCode')
                BEGIN
                    ALTER TABLE Redirects ADD StatusCode INT NOT NULL CONSTRAINT DF_Redirects_StatusCode DEFAULT 301 WITH VALUES;
                END
                -- AI/search crawler visibility (DBScripts/2026-09-05_create-crawler-visits-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CrawlerVisits')
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
                END

                -- Scheduled-job ledger (DBScripts/2026-09-15_create-jobs-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Jobs')
                BEGIN
                    CREATE TABLE Jobs (
                        Name           NVARCHAR(100)  NOT NULL PRIMARY KEY,
                        LastStartedAt  DATETIME2      NULL,
                        LastFinishedAt DATETIME2      NULL,
                        LastSucceeded  BIT            NOT NULL CONSTRAINT DF_Jobs_LastSucceeded DEFAULT 0,
                        LastMessage    NVARCHAR(1000) NULL,
                        RunCount       INT            NOT NULL CONSTRAINT DF_Jobs_RunCount DEFAULT 0
                    );
                END

                -- Self-service password reset (DBScripts/2026-09-15_create-password-reset-tokens-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PasswordResetTokens')
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
                END

                -- Search Console daily rows (DBScripts/2026-09-15_create-search-performance-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SearchPerformance')
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
                END

                -- Post/page revision history (DBScripts/2026-09-15_create-revisions-table.sql)
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Revisions')
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
                END

                -- Revision numbers unique per entity (DBScripts/2026-09-19_add-unique-index-revisions-number.sql).
                -- Any duplicates a pre-index install produced are renumbered first, only for the entities that
                -- carry one, in a deterministic order.
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Revisions') AND name = 'UX_Revisions_Entity_Number')
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
                    END
                    CREATE UNIQUE INDEX UX_Revisions_Entity_Number ON Revisions (EntityType, EntityId, Number);
                END

                -- Comment spam-window lookups seek CreatedAt (DBScripts/2026-09-19_add-index-comments-createdat.sql)
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Comments') AND name = 'IX_Comments_CreatedAt')
                BEGIN
                    CREATE INDEX IX_Comments_CreatedAt ON Comments (CreatedAt) INCLUDE (AuthorIp);
                END

                -- Desk replies record who wrote them (DBScripts/2026-09-19_ensure-comments-memberid-column.sql);
                -- present since 001_CMS_Schema.sql, topped up here for a database built from an older script.
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Comments') AND name = 'MemberId')
                BEGIN
                    ALTER TABLE Comments ADD MemberId UNIQUEIDENTIFIER NULL;
                END

                -- Answer capsule (DBScripts/2026-09-05_add-answercapsule-to-posts.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'AnswerCapsule')
                BEGIN
                    ALTER TABLE Posts ADD AnswerCapsule NVARCHAR(1000) NULL;
                END

                -- Offender IP on error signatures (DBScripts/2026-08-15_add-lastipaddress-to-errorlogs.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ErrorLogs') AND name = 'LastIpAddress')
                BEGIN
                    ALTER TABLE ErrorLogs ADD LastIpAddress NVARCHAR(45) NULL;
                END

                -- Crawler identity check against the operator's published network (DBScripts/2026-09-27_add-verified-to-crawlervisits.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('CrawlerVisits') AND name = 'Verified')
                BEGIN
                    ALTER TABLE CrawlerVisits ADD Verified BIT NULL;
                END

                -- Query string of crawler visits (DBScripts/2026-09-27_add-query-to-crawlervisits.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('CrawlerVisits') AND name = 'Query')
                BEGIN
                    ALTER TABLE CrawlerVisits ADD Query NVARCHAR(512) NULL;
                END

                -- Outbound webhooks and their delivery log (DBScripts/2026-09-27_create-webhooks-tables.sql)
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
                END
                
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
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('WebhookDeliveries') AND name = 'UX_WebhookDeliveries_Webhook_EventKey')
                    EXEC (N'SET QUOTED_IDENTIFIER ON; CREATE UNIQUE INDEX UX_WebhookDeliveries_Webhook_EventKey ON WebhookDeliveries (WebhookId, EventKey) WHERE EventKey IS NOT NULL;');

                -- Header and footer navigation menus (DBScripts/2026-09-27_create-menu-items-table.sql)
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
                END

                -- Editorial workflow review note and request time (DBScripts/2026-09-27_add-review-workflow-to-posts.sql)
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'ReviewNote')
                BEGIN
                    ALTER TABLE Posts ADD ReviewNote NVARCHAR(1000) NULL;
                END
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'ReviewRequestedAt')
                BEGIN
                    ALTER TABLE Posts ADD ReviewRequestedAt DATETIME2 NULL;
                END

                -- Two-factor sign-in (DBScripts/2026-10-05_add-two-factor-to-users.sql, ADR-0021)
                IF COL_LENGTH('Users', 'TwoFactorSecretProtected') IS NULL ALTER TABLE Users ADD TwoFactorSecretProtected NVARCHAR(1000) NULL;
                IF COL_LENGTH('Users', 'TwoFactorEnabledAt') IS NULL ALTER TABLE Users ADD TwoFactorEnabledAt DATETIME2 NULL;
                IF COL_LENGTH('Users', 'TwoFactorRecoveryHashes') IS NULL ALTER TABLE Users ADD TwoFactorRecoveryHashes NVARCHAR(MAX) NULL;
                IF COL_LENGTH('Users', 'TwoFactorLastStep') IS NULL ALTER TABLE Users ADD TwoFactorLastStep BIGINT NULL;

                -- Markdown authoring (DBScripts/2026-10-05_add-markdown-to-posts.sql, ADR-0022)
                IF COL_LENGTH('Posts', 'Markdown') IS NULL ALTER TABLE Posts ADD Markdown NVARCHAR(MAX) NULL;
                IF COL_LENGTH('Posts', 'EditorMode') IS NULL ALTER TABLE Posts ADD EditorMode NVARCHAR(20) NOT NULL CONSTRAINT DF_Posts_EditorMode DEFAULT 'rich' WITH VALUES;

                -- Custom fields (DBScripts/2026-10-05_add-custom-fields-to-posts-and-pages.sql, ADR-0025)
                IF COL_LENGTH('Posts', 'CustomFieldsJson') IS NULL ALTER TABLE Posts ADD CustomFieldsJson NVARCHAR(MAX) NULL;
                IF COL_LENGTH('Pages', 'CustomFieldsJson') IS NULL ALTER TABLE Pages ADD CustomFieldsJson NVARCHAR(MAX) NULL;

                -- Newsletter sending (DBScripts/2026-10-05_create-newsletter-issues-and-deliveries.sql, ADR-0028)
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
                END

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
                END";
            
            await Dapper.SqlMapper.ExecuteAsync(connection, sql);
            _logger.LogInformation("MigrationService: Schema updates applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MigrationService: Error applying schema updates.");
        }
        await SeedReferenceDataAsync();
    }

    /// <summary>
    /// Countries, time zones and date locales (ADR-0027): runs the embedded dated script when a table is missing
    /// or holds fewer rows than the script carries (a fresh database, or a newer tzdata/CLDR release). The
    /// script is an idempotent MERGE, so running it is always safe; skipping it when nothing changed saves
    /// startup time. Fails open: without the catalogs Settings falls back to the runtime's own lists.
    /// </summary>
    private async Task SeedReferenceDataAsync()
    {
        try
        {
            await using var stream = typeof(MigrationService).Assembly.GetManifestResourceStream("Blog.Infrastructure.ReferenceData.sql");
            if (stream is null) { _logger.LogWarning("MigrationService: reference data script is not embedded."); return; }
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var script = await reader.ReadToEndAsync();
            var rows = System.Text.RegularExpressions.Regex.Match(script, @"Rows: (\d+) countries, (\d+) time zones, (\d+) date locales");
            using var connection = _context.CreateConnection();
            var have = await Dapper.SqlMapper.QuerySingleAsync<(int Countries, int Zones, int Locales)>(connection, @"
                SELECT CASE WHEN OBJECT_ID('Countries', 'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM Countries) END,
                       CASE WHEN OBJECT_ID('TimeZones', 'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM TimeZones) END,
                       CASE WHEN OBJECT_ID('DateLocales', 'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM DateLocales) END");
            if (rows.Success && have.Countries >= int.Parse(rows.Groups[1].Value) && have.Zones >= int.Parse(rows.Groups[2].Value)
                && have.Locales >= int.Parse(rows.Groups[3].Value)) return;
            await Dapper.SqlMapper.ExecuteAsync(connection, script, commandTimeout: 180);
            _logger.LogInformation("MigrationService: countries, time zones and date locales seeded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MigrationService: Error seeding reference data (Settings falls back to the runtime's lists).");
        }
    }
}
