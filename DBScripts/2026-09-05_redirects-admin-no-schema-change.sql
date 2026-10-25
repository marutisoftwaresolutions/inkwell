-- DBScripts/2026-09-05_redirects-admin-no-schema-change.sql
-- Change: Admin → Redirects (list / create / edit / delete rules + a triaged 404 worklist).
--         `RedirectRepository` gained GetAllAsync, DeleteAsync, GetUnhandledNotFoundsAsync and
--         GetPublishedSlugsAsync; `RedirectRule` gained Id and CreatedAt.
--
-- Schema impact: NONE. Every column those queries read already exists:
--   Redirects  : Id (uniqueidentifier), [From], [To], CreatedAt (datetime2), StatusCode (int)
--   ErrorLogs  : Path, StatusCode, Referer, OccurrenceCount, LastSeenAt
--   Posts      : Slug, Status (nvarchar enum NAME, not an int), PublishedAt, ScheduledAt
--
-- Recorded per the Database Script Rule because *Repository.cs changed, even though nothing in the
-- schema does. Note for future edits: Posts.Status stores the enum *name* ('Published'), so
-- `Status = 1` fails with a conversion error rather than returning nothing — it cost a test run here.
--
-- Action required: None. The screen works against the existing schema on both dev and prod.

SET NOCOUNT ON;

-- Fail loudly if an assumption above is wrong, rather than letting the admin screen 500 later.
IF COL_LENGTH('Redirects', 'CreatedAt')  IS NULL RAISERROR('Redirects.CreatedAt is missing.', 16, 1);
IF COL_LENGTH('Redirects', 'StatusCode') IS NULL RAISERROR('Redirects.StatusCode is missing - run the v1.0.4 redirect-status script first.', 16, 1);
IF COL_LENGTH('ErrorLogs', 'Referer')    IS NULL RAISERROR('ErrorLogs.Referer is missing.', 16, 1);

PRINT 'No schema migration required for the Redirects admin screen.';
