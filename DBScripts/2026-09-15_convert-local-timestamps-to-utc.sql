-- DBScripts/2026-09-15_convert-local-timestamps-to-utc.sql
-- Change: The platform now stamps and compares every publish/visibility timestamp in UTC
--         (PostService → DateTime.UtcNow, PostRepository/RedirectRepository → GETUTCDATE()).
--         Before this, Posts.PublishedAt / ScheduledAt / CreatedAt / UpdatedAt, Pages.CreatedAt /
--         UpdatedAt, Comments.CreatedAt, Media.CreatedAt and Users.CreatedAt / UpdatedAt were
--         written in the HOST'S LOCAL TIME while everything else was UTC. On a host behind UTC a
--         freshly published post answered 200 at its URL yet was missing from the homepage, feed
--         and sitemap until the local clock caught up.
--
-- Why a parameter: the correction depends on the offset the host was running at when the rows
--         were written, which this script cannot know. Set @OffsetMinutes to that offset
--         (e.g. 330 for UTC+5:30, -300 for UTC-5). Leave it at 0 for a host that ran in UTC —
--         the script is then a no-op. Run ONCE; a marker row in Jobs prevents a second pass.
--
-- Schema impact: NONE (data conversion only). Idempotent via the Jobs marker.
-- Rollback: subtract the same offset back with the sign flipped and delete the marker row.

SET NOCOUNT ON;
DECLARE @OffsetMinutes INT = 0;   -- <<< set to the host's local offset from UTC, in minutes

IF @OffsetMinutes = 0
BEGIN
    PRINT 'Offset is 0 — nothing to convert (host ran in UTC, or offset not supplied). No-op.';
    RETURN;
END

IF EXISTS (SELECT 1 FROM Jobs WHERE Name = N'utc-conversion-applied')
BEGIN
    PRINT 'UTC conversion already applied on this database — skipped.';
    RETURN;
END

BEGIN TRANSACTION;

UPDATE Posts SET
    PublishedAt = DATEADD(minute, -@OffsetMinutes, PublishedAt),
    ScheduledAt = DATEADD(minute, -@OffsetMinutes, ScheduledAt),
    CreatedAt   = DATEADD(minute, -@OffsetMinutes, CreatedAt),
    UpdatedAt   = DATEADD(minute, -@OffsetMinutes, UpdatedAt);
PRINT '  [~] Posts: ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows shifted.';

UPDATE Pages SET
    CreatedAt = DATEADD(minute, -@OffsetMinutes, CreatedAt),
    UpdatedAt = DATEADD(minute, -@OffsetMinutes, UpdatedAt);
PRINT '  [~] Pages: ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows shifted.';

UPDATE Comments SET CreatedAt = DATEADD(minute, -@OffsetMinutes, CreatedAt);
PRINT '  [~] Comments: ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows shifted.';

UPDATE Media SET CreatedAt = DATEADD(minute, -@OffsetMinutes, CreatedAt);
PRINT '  [~] Media: ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows shifted.';

UPDATE Users SET
    CreatedAt = DATEADD(minute, -@OffsetMinutes, CreatedAt),
    UpdatedAt = DATEADD(minute, -@OffsetMinutes, UpdatedAt);
PRINT '  [~] Users: ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows shifted.';

INSERT INTO Jobs (Name, LastStartedAt, LastFinishedAt, LastSucceeded, LastMessage, RunCount)
VALUES (N'utc-conversion-applied', SYSUTCDATETIME(), SYSUTCDATETIME(), 1,
        N'Local timestamps shifted to UTC by ' + CAST(@OffsetMinutes AS NVARCHAR) + N' minutes (2026-09-15 script).', 1);

COMMIT TRANSACTION;
PRINT 'UTC conversion applied with offset ' + CAST(@OffsetMinutes AS NVARCHAR) + ' minutes.';
