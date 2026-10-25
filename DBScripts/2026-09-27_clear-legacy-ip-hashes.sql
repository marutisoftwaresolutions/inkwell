-- DBScripts/2026-09-27_clear-legacy-ip-hashes.sql
-- Purpose: clear PageViews.IpHash values written before the per-install keyed hash (ADR-0015).
--          Those values are SHA-256(ip + "pv-salt"), and the salt was published in the source code,
--          so anyone holding a copy of the table can recover every reader's IPv4 address in minutes.
--          Nothing in Inkwell reads IpHash (no report, no unique-visitor count), so clearing it
--          loses no feature. It is NOT reversible — the point is that the old values stop existing.
--          Also clears the same column in PageViews_Archive_Flood_2026_09 if that archive exists.
--
-- HOW TO RUN
--   1. Deploy the build that contains IpHasher first. Note the UTC time the new build went live.
--   2. Set @Before to that time and leave @Apply = 0: the script reports how many rows it would clear.
--   3. Set @Apply = 1 and run again. Batched; safe to stop and re-run; idempotent.
--   sqlcmd -S <server> -d <database> -E -f 65001 -i "DBScripts\2026-09-27_clear-legacy-ip-hashes.sql"
--   With @Before left NULL the script only reports and changes nothing.

SET NOCOUNT ON;
DECLARE @Apply  BIT       = 0;      -- 0 = report only; 1 = clear
DECLARE @Before DATETIME2 = NULL;   -- UTC go-live time of the IpHasher build, e.g. '2026-09-28T10:00:00'

DECLARE @Batch INT = 50000, @Done INT;

SELECT COUNT(*) AS LegacyHashesInPageViews
  FROM PageViews
 WHERE IpHash IS NOT NULL AND (@Before IS NULL OR CreatedAt < @Before);

IF OBJECT_ID('PageViews_Archive_Flood_2026_09') IS NOT NULL
    EXEC sp_executesql N'SELECT COUNT(*) AS LegacyHashesInFloodArchive FROM PageViews_Archive_Flood_2026_09 WHERE IpHash IS NOT NULL';

IF @Apply = 0 OR @Before IS NULL
BEGIN
    PRINT 'Report only (@Apply = 0 or @Before not set). Nothing was changed.';
    RETURN;
END

SET @Done = 1;
WHILE @Done > 0
BEGIN
    UPDATE TOP (@Batch) PageViews SET IpHash = NULL
     WHERE IpHash IS NOT NULL AND CreatedAt < @Before;
    SET @Done = @@ROWCOUNT;
    IF @Done > 0 PRINT CONCAT('PageViews hashes cleared: ', @Done);
END

IF OBJECT_ID('PageViews_Archive_Flood_2026_09') IS NOT NULL
    EXEC sp_executesql N'
        DECLARE @n INT = 1;
        WHILE @n > 0
        BEGIN
            UPDATE TOP (50000) PageViews_Archive_Flood_2026_09 SET IpHash = NULL WHERE IpHash IS NOT NULL;
            SET @n = @@ROWCOUNT;
        END';

PRINT 'Done. Legacy IP hashes before the cutover are cleared.';
