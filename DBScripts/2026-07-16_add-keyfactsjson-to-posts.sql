-- DBScripts/2026-07-16_add-keyfactsjson-to-posts.sql
-- Change: Add Posts.KeyFactsJson (NVARCHAR(MAX), nullable) to store a post's "Key Facts / At a glance"
--         list — a JSON array of { "label": "...", "value": "..." } pairs, mirroring FaqJson/RoundupJson.
-- Why: Rendered as a semantic definition list on the post page; highly extractable by AI answer engines
--      (ChatGPT/Claude/Perplexity/Gemini) and useful for readers. Authored via the post editor.
-- Schema impact: adds one nullable column to Posts. Idempotent (guarded by sys.columns check).
-- Note: MigrationService also applies this on startup with the same guard; this dated script documents
--       the change and lets a DBA apply it directly. Run with:  sqlcmd -S <server> -d <db> -f 65001 -i <file>

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'KeyFactsJson')
BEGIN
    ALTER TABLE Posts ADD KeyFactsJson NVARCHAR(MAX) NULL;
    PRINT 'Added Posts.KeyFactsJson.';
END
ELSE
BEGIN
    PRINT 'Posts.KeyFactsJson already exists — no change.';
END
