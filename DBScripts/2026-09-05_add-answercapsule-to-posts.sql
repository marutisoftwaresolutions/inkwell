-- DBScripts/2026-09-05_add-answercapsule-to-posts.sql
-- Change: Adds Posts.AnswerCapsule - a short direct answer shown at the top of a post.
-- Why:    AI answer engines and featured snippets quote *passages*, not pages. A post that opens
--         with context and reaches its answer three paragraphs down gives them nothing clean to
--         lift. The capsule is a deliberate 40-60 word answer placed before the article body,
--         rendered as visible copy (not hidden text) and emitted as schema.org `abstract` on the
--         BlogPosting, so the same sentence serves the reader and the machine.
-- Length: NVARCHAR(1000) is deliberate headroom, not a target. The editor and the structured-data
--         linter both steer toward 40-60 words; the column simply must not truncate a long one.
-- Schema impact: One nullable column on Posts. Existing rows stay NULL and render exactly as before,
--         so nothing changes for a post that never sets one.
-- Idempotent: safe to run repeatedly.

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('Posts') AND name = 'AnswerCapsule')
BEGIN
    ALTER TABLE Posts ADD AnswerCapsule NVARCHAR(1000) NULL;
    PRINT 'Added column Posts.AnswerCapsule.';
END
ELSE
BEGIN
    PRINT 'Posts.AnswerCapsule already exists - no action taken.';
END
GO
