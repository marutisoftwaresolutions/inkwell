-- =============================================================================
-- 2026-10-14_add-markdown-to-pages.sql
-- Change: Markdown authoring for pages (v1.0.9, ADR-0022 follow-up).
--   Pages.Markdown    NVARCHAR(MAX) NULL                   the Markdown source of a Markdown page
--   Pages.EditorMode  NVARCHAR(20) NOT NULL DEFAULT 'rich' 'rich' (visual editor) or 'markdown'
-- Content stays what the page, search and export read; for a Markdown page it is
-- rendered from Markdown on each save. Existing pages become 'rich' and are unchanged.
-- Idempotent: each column is added only when missing.
-- =============================================================================

IF COL_LENGTH('Pages', 'Markdown') IS NULL
BEGIN
    ALTER TABLE Pages ADD Markdown NVARCHAR(MAX) NULL;
    PRINT '  [+] Pages.Markdown added.';
END
ELSE PRINT '  [=] Pages.Markdown already exists — skipped.';

IF COL_LENGTH('Pages', 'EditorMode') IS NULL
BEGIN
    ALTER TABLE Pages ADD EditorMode NVARCHAR(20) NOT NULL CONSTRAINT DF_Pages_EditorMode DEFAULT 'rich' WITH VALUES;
    PRINT '  [+] Pages.EditorMode added.';
END
ELSE PRINT '  [=] Pages.EditorMode already exists — skipped.';
