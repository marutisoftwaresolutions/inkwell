-- DBScripts/2026-07-15_backfill-user-slugs.sql
-- Change: Populate Users.Slug for every user so author E-E-A-T pages (/author/{slug}) activate.
-- Schema impact: NONE — the Users.Slug column already exists (see UserRepository.GetBySlugAsync).
-- Why: Author pages, author byline links, the Person JSON-LD, and author sitemap entries all key off
--      Users.Slug. Pre-existing users had Slug = NULL, so those pages 404'd and bylines stayed plain text.
--
-- How the backfill actually happens (authoritative, code-side — single source of slug logic):
--   * New users: Blog.Infrastructure.Data.Repositories.UserRepository.CreateAsync generates a unique
--     slug from DisplayName (→ Username → email local-part) via SlugHelper when Slug is empty.
--   * Existing users: Program.cs runs IUserRepository.BackfillMissingSlugsAsync() on every startup.
--     It is idempotent (only touches Slug IS NULL / '') and reuses the exact same SlugHelper logic,
--     appending -2, -3, … on collision. No manual step is required — just deploy and start the app.
--
-- Action required: None. This script exists to satisfy the mandatory Database Script Rule and to let a
-- DBA verify/repair the data directly if the application startup path is ever skipped.

SET NOCOUNT ON;

-- 1) Verification — how many users still lack a slug (expected 0 after an app startup).
SELECT COUNT(*) AS UsersMissingSlug
FROM Users
WHERE Slug IS NULL OR LTRIM(RTRIM(Slug)) = '';

-- 2) Optional manual fallback (idempotent) — only if you must backfill WITHOUT running the app.
--    NOTE: prefer the application backfill above; it produces higher-quality slugs (full slugification
--    + guaranteed uniqueness). This SQL is a conservative last resort for simple ASCII display names.
--    Uncomment to run:
--
-- ;WITH Missing AS (
--     SELECT Id,
--            LTRIM(RTRIM(LOWER(COALESCE(NULLIF(DisplayName,''), NULLIF(Username,''),
--                   LEFT(Email, NULLIF(CHARINDEX('@', Email),0) - 1), 'user')))) AS RawName
--     FROM Users
--     WHERE Slug IS NULL OR LTRIM(RTRIM(Slug)) = ''
-- ),
-- Slugged AS (
--     SELECT Id,
--            REPLACE(REPLACE(REPLACE(REPLACE(RawName, ' ', '-'), '_', '-'), '.', '-'), '--', '-') AS BaseSlug
--     FROM Missing
-- ),
-- Numbered AS (
--     SELECT Id, BaseSlug,
--            ROW_NUMBER() OVER (PARTITION BY BaseSlug ORDER BY Id) AS rn
--     FROM Slugged
-- )
-- UPDATE u
--    SET u.Slug = CASE WHEN n.rn = 1 THEN n.BaseSlug ELSE n.BaseSlug + '-' + CAST(n.rn AS varchar(10)) END,
--        u.UpdatedAt = SYSDATETIME()
--   FROM Users u
--   JOIN Numbered n ON n.Id = u.Id;

PRINT 'User slug backfill: no schema change. Slugs are generated/backfilled in application code (see header).';
