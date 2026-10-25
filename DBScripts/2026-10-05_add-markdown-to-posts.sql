-- =============================================================================
-- 2026-10-05_add-markdown-to-posts.sql
-- Change: Markdown authoring (v1.0.8, ADR-0022).
--   Posts.Markdown    NVARCHAR(MAX) NULL                   the Markdown source of a Markdown post
--   Posts.EditorMode  NVARCHAR(20) NOT NULL DEFAULT 'rich' 'rich' (visual editor) or 'markdown'
-- Html and Plaintext stay what every page, feed, search and export reads; for a Markdown post they
-- are rendered from Markdown on each save. Existing posts become 'rich' and are unchanged.
-- Idempotent: each column is added only when missing.
-- =============================================================================

IF COL_LENGTH('Posts', 'Markdown') IS NULL
BEGIN
    ALTER TABLE Posts ADD Markdown NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.Markdown added.';
END
ELSE PRINT '  [=] Posts.Markdown already exists — skipped.';

IF COL_LENGTH('Posts', 'EditorMode') IS NULL
BEGIN
    ALTER TABLE Posts ADD EditorMode NVARCHAR(20) NOT NULL CONSTRAINT DF_Posts_EditorMode DEFAULT 'rich' WITH VALUES;
    PRINT '  [+] Posts.EditorMode added.';
END
ELSE PRINT '  [=] Posts.EditorMode already exists — skipped.';
