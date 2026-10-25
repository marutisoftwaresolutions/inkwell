-- =============================================================================
-- 2026-10-05_add-custom-fields-to-posts-and-pages.sql
-- Change: Custom fields (v1.0.8, ADR-0025).
--   Posts.CustomFieldsJson  NVARCHAR(MAX) NULL   a post's custom field values, { "key": "value" }
--   Pages.CustomFieldsJson  NVARCHAR(MAX) NULL   a page's custom field values, same shape
-- The field definitions live in the Settings JSON blob (see the companion no-op script
-- 2026-10-05_add-custom-fields-to-settings.sql). Existing rows stay NULL: no values, nothing shown.
-- Idempotent: each column is added only when missing.
-- =============================================================================

IF COL_LENGTH('Posts', 'CustomFieldsJson') IS NULL
BEGIN
    ALTER TABLE Posts ADD CustomFieldsJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.CustomFieldsJson added.';
END
ELSE PRINT '  [=] Posts.CustomFieldsJson already exists — skipped.';

IF COL_LENGTH('Pages', 'CustomFieldsJson') IS NULL
BEGIN
    ALTER TABLE Pages ADD CustomFieldsJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Pages.CustomFieldsJson added.';
END
ELSE PRINT '  [=] Pages.CustomFieldsJson already exists — skipped.';
