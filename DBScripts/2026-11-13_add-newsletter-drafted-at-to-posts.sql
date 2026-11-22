-- DBScripts/2026-11-13_add-newsletter-drafted-at-to-posts.sql
-- Change: Posts.NewsletterDraftedAt DATETIME2 NULL (UTC). Set once, when a newsletter draft is prepared
--         for a post that went live (Settings -> General -> "Prepare a newsletter when a post is
--         published"). It is what makes "once per post" hold: without it, deleting the prepared draft
--         of a scheduled post brought a new one back every minute for a day.
-- Existing posts stay NULL. Idempotent. Also in MigrationService and init.sql (top-ups).

IF COL_LENGTH('Posts', 'NewsletterDraftedAt') IS NULL
BEGIN
    ALTER TABLE Posts ADD NewsletterDraftedAt DATETIME2 NULL;
    PRINT '  [+] Posts.NewsletterDraftedAt added.';
END
ELSE
    PRINT '  [=] Posts.NewsletterDraftedAt already exists - skipped.';
