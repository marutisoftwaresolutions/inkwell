-- DBScripts/2026-09-17_convert-remaining-local-timestamps-to-utc.sql
-- Change: Follow-up to 2026-09-15_convert-local-timestamps-to-utc.sql. That script shifted Posts,
--         Pages, Comments, Media and Users from host-local time to UTC but left two columns that the
--         same release also moved to UTC in code: Redirects.CreatedAt (RedirectRepository) and
--         Settings.UpdatedAt (SettingRepository). Both are display/ordering columns only, so nothing
--         was hidden or mis-scheduled — but "created" times on Admin → Redirects and the settings
--         audit trail are off by the host's offset until this runs.
--
-- Usage:  set @OffsetMinutes to the SAME value used for the 2026-09-15 script (e.g. 330 for UTC+5:30).
--         Leave it at 0 on a host that always ran in UTC; the script is then a no-op.
--         Run ONCE; a marker row in Jobs prevents a second pass.
--
-- Schema impact: NONE (data conversion only). Idempotent via the Jobs marker.
-- Run with: sqlcmd -S <server> -d <db> -f 65001 -i "DBScripts\2026-09-17_convert-remaining-local-timestamps-to-utc.sql"
SET NOCOUNT ON;

DECLARE @OffsetMinutes INT = 0;   -- <<< set to the host's local offset from UTC, in minutes

IF @OffsetMinutes = 0
BEGIN
    PRINT 'OffsetMinutes is 0 — nothing to convert (host was already UTC, or the value was not set).';
    RETURN;
END

IF OBJECT_ID('dbo.Jobs', 'U') IS NULL
BEGIN
    RAISERROR('Jobs table is missing — run 2026-09-15_create-jobs-table.sql first.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM Jobs WHERE Name = N'utc-conversion-2-applied')
BEGIN
    PRINT 'Already applied (marker utc-conversion-2-applied present) — skipping.';
    RETURN;
END

BEGIN TRAN;

IF COL_LENGTH('dbo.Redirects', 'CreatedAt') IS NOT NULL
    UPDATE Redirects SET CreatedAt = DATEADD(minute, -@OffsetMinutes, CreatedAt);

IF COL_LENGTH('dbo.Settings', 'UpdatedAt') IS NOT NULL
    UPDATE Settings SET UpdatedAt = DATEADD(minute, -@OffsetMinutes, UpdatedAt);

INSERT INTO Jobs (Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount)
VALUES (N'utc-conversion-2-applied', SYSUTCDATETIME(), SYSUTCDATETIME(), 1,
        N'Shifted Redirects.CreatedAt and Settings.UpdatedAt by -' + CAST(@OffsetMinutes AS NVARCHAR(10)) + N' minutes', 1);

COMMIT;
PRINT 'Converted Redirects.CreatedAt and Settings.UpdatedAt to UTC.';
