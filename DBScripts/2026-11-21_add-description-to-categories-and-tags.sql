-- DBScripts/2026-11-21_add-description-to-categories-and-tags.sql
-- Change: optional description on categories and tags.
-- Why:    a category or tag page was a bare list with a generated one-line meta description
--         ("Articles about X on <site>."). An author can now write a short introduction that is shown
--         above the list on the first page and used as the page's meta description.
-- Schema: Categories.Description NVARCHAR(600) NULL, Tags.Description NVARCHAR(600) NULL.
--         NULL means no description; the page then renders exactly as before.
-- Format: plain text. A link is written [text](/path) or [text](https://host/path); a blank line
--         starts a new paragraph. No HTML is stored or accepted.
-- Also in: Blog.Infrastructure/Data/MigrationService.cs (existing installs upgrade on startup) and
--          DBScripts/init.sql (fresh installs).
-- Idempotent: safe to run more than once.

SET NOCOUNT ON;

IF COL_LENGTH('Categories', 'Description') IS NULL
BEGIN
    ALTER TABLE Categories ADD Description NVARCHAR(600) NULL;
    PRINT 'Categories.Description added.';
END
ELSE PRINT 'Categories.Description already exists; skipped.';

IF COL_LENGTH('Tags', 'Description') IS NULL
BEGIN
    ALTER TABLE Tags ADD Description NVARCHAR(600) NULL;
    PRINT 'Tags.Description added.';
END
ELSE PRINT 'Tags.Description already exists; skipped.';
