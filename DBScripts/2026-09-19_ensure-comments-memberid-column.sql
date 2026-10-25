-- DBScripts/2026-09-19_ensure-comments-memberid-column.sql
-- Change: CommentRepository.CreateAsync now persists Comment.MemberId — the signed-in
--         user behind a Desk reply (CommentsController.Reply set it, the INSERT dropped it).
-- Schema impact: NONE on a current install. Comments.MemberId has existed since
--         Blog.Infrastructure/Migrations/001_CMS_Schema.sql and init.sql creates it in the
--         table itself. This script is the top-up for a database built from an older copy:
--         it adds the column only when missing (nullable, no FK so no existing row can
--         fail it), matching the MigrationService and init.sql top-ups.
-- Idempotent: safe to run more than once.
SET NOCOUNT ON;

IF OBJECT_ID('Comments') IS NULL
    PRINT 'Comments table not present — nothing to do.';
ELSE IF COL_LENGTH('Comments', 'MemberId') IS NOT NULL
    PRINT 'Comments.MemberId already exists — no schema change required.';
ELSE
BEGIN
    ALTER TABLE Comments ADD MemberId UNIQUEIDENTIFIER NULL;
    PRINT 'Comments.MemberId added.';
END
