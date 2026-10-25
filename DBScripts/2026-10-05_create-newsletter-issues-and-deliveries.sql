-- =============================================================================
-- 2026-10-05_create-newsletter-issues-and-deliveries.sql
-- Change: Newsletter sending (v1.0.8, ADR-0028).
--   NewsletterIssues      one newsletter: subject, preheader, intro (Markdown), the posts it features, and,
--                         once queued, the rendered HTML and text every recipient gets (snapshot) plus counts.
--   NewsletterDeliveries  one row per recipient of a queued issue; the newsletter job sends Pending rows in
--                         batches, retries failures, and records Sent / Failed / Skipped (unsubscribed meanwhile).
-- The older Newsletters / MemberNewsletters / Emails tables are not used: Emails needs a PostId and has no
-- per-recipient rows. They are left as they are.
-- Idempotent: each table and index is created only when missing.
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
