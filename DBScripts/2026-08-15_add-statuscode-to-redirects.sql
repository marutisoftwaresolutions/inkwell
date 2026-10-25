-- DBScripts/2026-08-15_add-statuscode-to-redirects.sql
-- Change: Adds Redirects.StatusCode so a rule can express *how* a legacy URL is retired:
--         301 (moved permanently, the existing behaviour and the default),
--         302 (temporary), or
--         410 (Gone - the resource is intentionally removed and will not come back).
-- Why:    This domain previously hosted the OptoSoft product site. Old product routes such as
--         /features, /pricing, /aboutus and /contactus now 404 but are still indexed and still
--         drawing impressions (Search Console, 2026-08-15: /features 441 impressions at avg
--         position 45). Per the Production Deployment Rule an indexed legacy URL must be retired
--         deliberately - 410 or 301 - never left as an indexed 404. A 410 tells Google the page is
--         intentionally gone, which removes it from the index far faster than a soft/hard 404.
-- Schema impact: One NOT NULL INT column with DEFAULT 301 applied to existing rows, so every
--                pre-existing redirect keeps behaving exactly as before (backward compatible).
-- Note:   For a 410 rule the [To] column carries no meaning; rows store '' and the application
--         ignores it when StatusCode = 410.
-- Idempotent: safe to run repeatedly.

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('Redirects') AND name = 'StatusCode')
BEGIN
    ALTER TABLE Redirects ADD StatusCode INT NOT NULL CONSTRAINT DF_Redirects_StatusCode DEFAULT 301 WITH VALUES;
    PRINT 'Added column Redirects.StatusCode (default 301).';
END
ELSE
BEGIN
    PRINT 'Redirects.StatusCode already exists - no action taken.';
END
GO
