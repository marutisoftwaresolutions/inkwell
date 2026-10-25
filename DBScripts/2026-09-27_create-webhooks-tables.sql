-- DBScripts/2026-09-27_create-webhooks-tables.sql
-- Change: creates Webhooks (endpoints: URL, Data-Protection-encrypted signing secret, subscribed
--         events, enabled flag) and WebhookDeliveries (one row per event per endpoint: payload,
--         status, attempts, next attempt, last response). v1.0.8 core item; see ADR-0016.
-- Why:    no outbound event existed, so static rebuilds, social posting and cache purges could not be
--         triggered by publishing.
-- Idempotent; safe to run more than once.

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
    PRINT 'Webhooks created.';
END
ELSE
    PRINT 'Webhooks already exists — skipped.';

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
    PRINT 'WebhookDeliveries created.';
END
ELSE
    PRINT 'WebhookDeliveries already exists — skipped.';

-- One queued event per webhook per key (e.g. a scheduled post's publish), enforced by a filtered
-- unique index. Filtered indexes need QUOTED_IDENTIFIER ON, which sqlcmd leaves off, so it is set in
-- the batch that creates the index. Checked on its own so a partial earlier run heals.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('WebhookDeliveries') AND name = 'UX_WebhookDeliveries_Webhook_EventKey')
BEGIN
    EXEC (N'SET QUOTED_IDENTIFIER ON; CREATE UNIQUE INDEX UX_WebhookDeliveries_Webhook_EventKey ON WebhookDeliveries (WebhookId, EventKey) WHERE EventKey IS NOT NULL;');
    PRINT 'UX_WebhookDeliveries_Webhook_EventKey created.';
END
ELSE
    PRINT 'UX_WebhookDeliveries_Webhook_EventKey already exists — skipped.';
