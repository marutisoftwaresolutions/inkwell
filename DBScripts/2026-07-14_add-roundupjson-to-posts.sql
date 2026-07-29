-- DBScripts/2026-07-14_add-roundupjson-to-posts.sql
-- Change: Adds Posts.RoundupJson (NVARCHAR(MAX), NULL) to support the "Verdict" roundup
--         post format — scored "Top N Best <X> Software" comparison listicles.
--
-- Why: The Verdict theme renders editor's picks, a comparison table, and per-entry
--      scored cards (rank / badge / score / best-for / standout / pros / cons / CTA)
--      from structured data rather than hand-written HTML. That same structure drives
--      ItemList + Review + AggregateRating JSON-LD, which is the SEO/AEO payoff.
--
-- Shape (mirrors the existing FaqJson blob pattern — no child tables):
--   {
--     "listType": "SoftwareApplication",
--     "weights":  { "features": 40, "ease": 30, "value": 30 },
--     "methodology": [ { "step": "Feature verification", "detail": "..." } ],
--     "entries": [ {
--        "rank": 1, "name": "OptiLight", "badge": "Editor's pick", "score": 9.4,
--        "scores": { "features": 9.6, "ease": 9.1, "value": 9.4 },
--        "bestFor": "...", "standout": "...", "body": "...",
--        "pros": ["..."], "cons": ["..."],
--        "ctaUrl": "https://...", "logoUrl": "/media/x.png", "domain": "example.com"
--     } ]
--   }
--
-- Backward compatibility: column is NULLABLE with no default. Every existing post has
-- RoundupJson = NULL and renders exactly as before. The Verdict post template only
-- activates when RoundupJson is present.
--
-- Idempotent: safe to run more than once.
--
-- Run with (UTF-8 code page is MANDATORY):
--   sqlcmd -S <server> -d <db> -U <user> -P <pass> -f 65001 -i "DBScripts\2026-07-14_add-roundupjson-to-posts.sql"

SET NOCOUNT ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Posts') AND name = N'RoundupJson'
)
BEGIN
    ALTER TABLE Posts ADD RoundupJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.RoundupJson column added.';
END
ELSE
    PRINT '  [=] Posts.RoundupJson already exists - skipped.';
GO

-- Verification
SELECT 'Schema' AS Section,
       'Posts.RoundupJson' AS Item,
       CASE WHEN EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Posts') AND name = N'RoundupJson')
            THEN 'OK' ELSE 'MISSING' END AS Status;
GO
