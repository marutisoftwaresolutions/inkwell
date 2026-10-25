-- DBScripts/2026-08-15_create-ip-firewall-table.sql
-- Change: Creates the IpFirewallRules table backing the new IP Firewall feature
--         (Admin -> Security). One row per IP address, holding either a Block rule
--         (auto-created by the threat scorer, or added manually) or an Allow rule
--         (never auto-blocked).
-- Why:    The site is under continuous automated probing (WordPress/PHP scanners,
--         path-traversal and admin brute-force attempts). Blocks must survive an app
--         restart, so they live in the database rather than only in memory.
-- Schema impact: New table IpFirewallRules + 2 indexes. No existing table changed.
-- Idempotent: safe to run repeatedly.

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IpFirewallRules')
BEGIN
    CREATE TABLE IpFirewallRules (
        Id            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IpAddress     NVARCHAR(45)   NOT NULL,          -- IPv4 or IPv6, normalized
        Kind          NVARCHAR(10)   NOT NULL DEFAULT 'Block',  -- Block | Allow
        Source        NVARCHAR(10)   NOT NULL DEFAULT 'Auto',   -- Auto  | Manual
        Reason        NVARCHAR(500)  NULL,
        Score         INT            NOT NULL DEFAULT 0,        -- threat score at block time
        OffenseCount  INT            NOT NULL DEFAULT 1,        -- drives block-duration escalation
        HitCount      INT            NOT NULL DEFAULT 0,        -- requests denied since the block
        LastPath      NVARCHAR(1024) NULL,
        LastUserAgent NVARCHAR(512)  NULL,
        CreatedBy     NVARCHAR(200)  NULL,                      -- 'system' for auto blocks
        CreatedAt     DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt     DATETIME2      NULL,                      -- NULL = permanent
        LastHitAt     DATETIME2      NULL,
        CONSTRAINT UQ_IpFirewallRules_IpAddress UNIQUE (IpAddress)
    );

    CREATE INDEX IX_IpFirewallRules_Kind_ExpiresAt ON IpFirewallRules (Kind, ExpiresAt);
    CREATE INDEX IX_IpFirewallRules_CreatedAt      ON IpFirewallRules (CreatedAt DESC);

    PRINT 'Created table IpFirewallRules.';
END
ELSE
BEGIN
    PRINT 'IpFirewallRules already exists - no action taken.';
END
GO
