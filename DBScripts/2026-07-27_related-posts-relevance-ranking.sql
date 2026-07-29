-- DBScripts/2026-07-27_related-posts-relevance-ranking.sql
-- Change: PostRepository.GetRelatedPostsAsync now ranks related posts by a RELEVANCE score
--         (shared category * 3 + shared tag * 1), recency as tie-breaker, with a recency backfill
--         so the See-Also / Related internal-linking module is always populated.
-- Schema impact: NONE. Pure query-logic change (ORDER BY). Uses only existing tables/columns
--                (Posts, PostCategories, PostTags, PublishedAt) — no new column, index, or data.
-- Action required: None. Behaviour applies as soon as the updated build is deployed.
PRINT 'No schema migration required — related-posts change is query-logic only (no schema impact).';
