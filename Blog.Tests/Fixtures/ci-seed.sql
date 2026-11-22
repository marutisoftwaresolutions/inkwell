-- Blog.Tests/Fixtures/ci-seed.sql
-- A minimal installed site for the database-backed tests in CI: one admin, one settings row, a few
-- categories, tags and published posts. Sample data only; safe to run more than once. Never run this
-- against a real site: it is applied by the build-and-test workflow to its throwaway container.
SET NOCOUNT ON;
DECLARE @admin UNIQUEIDENTIFIER = 'C1000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = @admin)
    INSERT INTO Users (Id, Email, Username, DisplayName, PasswordHash, Role, Status, Slug, UpdatedAt)
    VALUES (@admin, N'ci-admin@example.invalid', N'admin', N'CI Admin', N'not-a-usable-hash', N'Admin', N'active', N'ci-admin', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM Settings WHERE UserId = @admin)
    INSERT INTO Settings (UserId, JsonPayload, UpdatedAt)
    VALUES (@admin, N'{"SiteName":"CI Sample Site","SiteDescription":"Sample content for automated tests.","PostsPerPage":20,"CommentsEnabled":true,"CommentsModeration":true}', SYSUTCDATETIME());

DECLARE @cats TABLE (Id UNIQUEIDENTIFIER, Name NVARCHAR(100), Slug NVARCHAR(100));
INSERT INTO @cats VALUES
 ('C2000000-0000-0000-0000-000000000001', N'Practice Management Software', N'practice-management-software'),
 ('C2000000-0000-0000-0000-000000000002', N'Billing and Revenue Cycle',    N'billing-revenue-cycle'),
 ('C2000000-0000-0000-0000-000000000003', N'Optical Retail Technology',    N'optical-retail-technology');
INSERT INTO Categories (Id, Name, Slug, AuthorId)
SELECT c.Id, c.Name, c.Slug, @admin FROM @cats c WHERE NOT EXISTS (SELECT 1 FROM Categories x WHERE x.Id = c.Id);

DECLARE @tags TABLE (Id UNIQUEIDENTIFIER, Name NVARCHAR(100), Slug NVARCHAR(100));
INSERT INTO @tags VALUES
 ('C3000000-0000-0000-0000-000000000001', N'Cloud-Based EHR', N'cloud-based-ehr'),
 ('C3000000-0000-0000-0000-000000000002', N'Revolution EHR',  N'revolution-ehr'),
 ('C3000000-0000-0000-0000-000000000003', N'Optical POS',     N'optical-pos'),
 ('C3000000-0000-0000-0000-000000000004', N'Eyefinity',       N'eyefinity'),
 ('C3000000-0000-0000-0000-000000000005', N'OptoSoft',        N'optosoft'),
 ('C3000000-0000-0000-0000-000000000006', N'AI Diagnostics',  N'ai-diagnostics');
INSERT INTO Tags (Id, Name, Slug, AuthorId)
SELECT t.Id, t.Name, t.Slug, @admin FROM @tags t WHERE NOT EXISTS (SELECT 1 FROM Tags x WHERE x.Id = t.Id);

-- Eight published posts; each carries one category and two tags so archives, filters and
-- related-post queries all have rows to work with.
DECLARE @n INT = 1;
WHILE @n <= 8
BEGIN
    DECLARE @pid UNIQUEIDENTIFIER = CAST('C4000000-0000-0000-0000-00000000000' + CAST(@n AS CHAR(1)) AS UNIQUEIDENTIFIER);
    IF NOT EXISTS (SELECT 1 FROM Posts WHERE Id = @pid)
        INSERT INTO Posts (Id, Title, Slug, Html, Plaintext, Status, Visibility, Type, AuthorId, PublishedAt, CreatedAt, UpdatedAt, MetaDescription)
        VALUES (@pid, N'Sample post ' + CAST(@n AS NVARCHAR(2)), N'sample-post-' + CAST(@n AS NVARCHAR(2)),
                N'<p>Sample body for automated tests.</p><h2>First section</h2><p>Text.</p><h2>Second section</h2><p>Text.</p><h2>Third section</h2><p>Text.</p>',
                N'Sample body for automated tests. First section Text. Second section Text. Third section Text.',
                N'Published', N'public', N'post', @admin, DATEADD(DAY, -@n, SYSUTCDATETIME()), SYSUTCDATETIME(), SYSUTCDATETIME(),
                N'A sample post used by the automated test suite; it holds no real content at all.');
    IF NOT EXISTS (SELECT 1 FROM PostCategories WHERE PostId = @pid)
        INSERT INTO PostCategories (PostId, CategoryId)
        SELECT @pid, Id FROM @cats WHERE Slug = CASE @n % 3 WHEN 0 THEN N'practice-management-software' WHEN 1 THEN N'billing-revenue-cycle' ELSE N'optical-retail-technology' END;
    IF NOT EXISTS (SELECT 1 FROM PostTags WHERE PostId = @pid)
        INSERT INTO PostTags (PostId, TagId)
        SELECT @pid, Id FROM (SELECT Id, ROW_NUMBER() OVER (ORDER BY Slug) rn FROM @tags) t WHERE t.rn IN ((@n % 6) + 1, ((@n + 2) % 6) + 1);
    SET @n += 1;
END
PRINT 'CI sample site seeded.';
