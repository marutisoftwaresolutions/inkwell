-- =============================================================================
-- 2026-10-15_default-post-viewcount-to-zero.sql
-- Change: Posts.ViewCount gets a default of 0, and existing NULL counts become 0.
-- Why:    the column had no default and the app's post insert does not set it, so every post created in the
--         Desk, through the API or by import started at NULL. "ViewCount = ViewCount + n" leaves NULL as NULL, so
--         those posts never counted a view and never appeared in "Most read". The code now adds with ISNULL as
--         well; this script repairs the data and stops new NULLs.
-- Idempotent: the update touches only NULL rows; the constraint is added only when the column has no default.
-- =============================================================================

UPDATE Posts SET ViewCount = 0 WHERE ViewCount IS NULL;
PRINT CONCAT('  [=] Posts with no view count set to 0: ', @@ROWCOUNT);

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc
               JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
               WHERE dc.parent_object_id = OBJECT_ID('Posts') AND c.name = 'ViewCount')
BEGIN
    ALTER TABLE Posts ADD CONSTRAINT DF_Posts_ViewCount DEFAULT 0 FOR ViewCount;
    PRINT '  [+] Posts.ViewCount now defaults to 0.';
END
ELSE PRINT '  [=] Posts.ViewCount already has a default — skipped.';
