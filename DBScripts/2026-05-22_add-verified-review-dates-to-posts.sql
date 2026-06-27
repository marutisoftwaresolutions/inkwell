-- DBScripts/2026-05-22_add-verified-review-dates-to-posts.sql
-- Change: Add LastVerifiedAt and NextReviewAt columns to the Posts table.
-- Purpose: Track content freshness — when the article was last fact-checked and when it is due for review.
--          These dates are displayed on the public blog post page and can trigger automatic year updates in the slug.
-- Schema impact: Two nullable DATETIME2 columns added to Posts.
-- Action required: Run once. MigrationService also applies this on startup.

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'LastVerifiedAt')
BEGIN
    ALTER TABLE Posts ADD LastVerifiedAt DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Posts') AND name = 'NextReviewAt')
BEGIN
    ALTER TABLE Posts ADD NextReviewAt DATETIME2 NULL;
END
