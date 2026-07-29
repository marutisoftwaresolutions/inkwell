-- DBScripts/2026-07-15_add-website-to-roundup-entries.sql
-- Change: Added "website" (the product/company's real URL) to each entry object inside the
--         Posts.RoundupJson blob, powering an outbound "Visit website" reference and the
--         SoftwareApplication `url` in ItemList/Review JSON-LD.
--
-- Schema impact: NONE — "website" lives inside the existing Posts.RoundupJson NVARCHAR(MAX)
--                JSON blob (RoundupEntry.Website). No column is added or altered.
--
-- Why: The Content External-Link Rule (CLAUDE.md) now requires every roundup entry to link the
--      real vendor site. Older roundup blobs without a "website" key remain valid — the field is
--      optional at the storage layer and simply renders no outbound link until backfilled.
--
-- Action required: None for the schema. Content owners backfill the "website" key when editing a
--                  roundup; existing entries render unchanged until then.
--
-- Run with (UTF-8 code page is MANDATORY even for a no-op documentation script):
--   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\2026-07-15_add-website-to-roundup-entries.sql"

SET NOCOUNT ON;
PRINT 'No schema migration required: RoundupEntry.website is stored inside Posts.RoundupJson.';
GO
