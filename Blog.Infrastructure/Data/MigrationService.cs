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
                END";
            
            await Dapper.SqlMapper.ExecuteAsync(connection, sql);
            _logger.LogInformation("MigrationService: Schema updates applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MigrationService: Error applying schema updates.");
        }
    }
}
