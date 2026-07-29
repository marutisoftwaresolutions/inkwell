-- DBScripts/2026-07-16_add-howtojson-to-posts.sql
-- Change: Add Posts.HowToJson (NVARCHAR(MAX), nullable) to store a post's step-by-step guide —
--         a JSON array of { "name": "...", "text": "..." } steps, mirroring FaqJson/KeyFactsJson.
-- Why: Rendered as a visible numbered list on the post page and emitted as HowTo JSON-LD so AI
--      answer engines can present the procedure. Authored via the post editor.
-- Schema impact: adds one nullable column to Posts. Idempotent (guarded by sys.columns check).
-- Note: MigrationService also applies this on startup with the same guard. Run with:
--       sqlcmd -S <server> -d <db> -f 65001 -i <file>

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'HowToJson')
BEGIN
    ALTER TABLE Posts ADD HowToJson NVARCHAR(MAX) NULL;
    PRINT 'Added Posts.HowToJson.';
END
ELSE
BEGIN
    PRINT 'Posts.HowToJson already exists — no change.';
END
