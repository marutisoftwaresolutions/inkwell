-- DBScripts/2026-09-27_add-review-workflow-to-posts.sql
-- Change: Posts gains ReviewNote NVARCHAR(1000) NULL (an editor's note when a post is returned for
--         changes) and ReviewRequestedAt DATETIME2 NULL (when it was last submitted; orders the review
--         queue). The two new statuses, InReview and NeedsChanges, need no schema change: Posts.Status
--         is NVARCHAR and every public query lists Published / Scheduled explicitly, so readers never
--         see them. v1.0.8 core item "Editorial workflow states"; see ADR-0018.
-- Idempotent; safe to run more than once.

IF COL_LENGTH('Posts', 'ReviewNote') IS NULL
BEGIN
    ALTER TABLE Posts ADD ReviewNote NVARCHAR(1000) NULL;
    PRINT 'Posts.ReviewNote added.';
END
ELSE
    PRINT 'Posts.ReviewNote already exists — skipped.';

IF COL_LENGTH('Posts', 'ReviewRequestedAt') IS NULL
BEGIN
    ALTER TABLE Posts ADD ReviewRequestedAt DATETIME2 NULL;
    PRINT 'Posts.ReviewRequestedAt added.';
END
ELSE
    PRINT 'Posts.ReviewRequestedAt already exists — skipped.';
