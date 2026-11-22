-- DBScripts/2026-11-13_add-parent-to-categories.sql
-- Change: Categories.ParentId UNIQUEIDENTIFIER NULL. A category can sit under another category
--         (Admin -> Categories). A parent's public page also lists the posts of the categories beneath it.
-- No foreign key: SQL Server refuses a cascading self-reference, so the application clears the
-- children's ParentId when a parent is deleted, and treats a ParentId that points nowhere as top level.
-- Existing categories stay NULL (top level). Idempotent. Also in MigrationService and init.sql (top-ups).

IF COL_LENGTH('Categories', 'ParentId') IS NULL
BEGIN
    ALTER TABLE Categories ADD ParentId UNIQUEIDENTIFIER NULL;
    PRINT '  [+] Categories.ParentId added.';
END
ELSE
    PRINT '  [=] Categories.ParentId already exists - skipped.';
