-- DBScripts/2026-11-13_create-goalcounts-table.sql
-- Change: GoalCounts, one row per site, day (UTC) and goal: how many times a goal was reached that day
--         (Admin -> Analytics -> Goals). Numbers only: no reader, session or address is stored.
--         The goals themselves are a list in the Settings JSON blob (UserSettings.Goals, no schema change).
-- OwnerId is the tenant in cloud mode and the empty GUID otherwise, as in PageViews.
-- Idempotent. Also in MigrationService and init.sql (section 46).

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'GoalCounts')
BEGIN
    CREATE TABLE GoalCounts (
        OwnerId UNIQUEIDENTIFIER NOT NULL,
        Day     DATE             NOT NULL,
        GoalKey NVARCHAR(40)     NOT NULL,
        Hits    INT              NOT NULL CONSTRAINT DF_GoalCounts_Hits DEFAULT 0,
        CONSTRAINT PK_GoalCounts PRIMARY KEY (OwnerId, Day, GoalKey)
    );
    PRINT '  [+] GoalCounts table created.';
END
ELSE
    PRINT '  [=] GoalCounts already exists - skipped.';
