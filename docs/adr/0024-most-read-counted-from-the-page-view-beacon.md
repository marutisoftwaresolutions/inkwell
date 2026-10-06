# ADR-0024: "Most read" is counted from the page-view beacon

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** the "Not changed" consequence of ADR-0014 (`Posts.ViewCount` stayed a server-side counter)
- **Related:** ADR-0012 (batched traffic recording), ADR-0014 (page views counted from a beacon); `.gsd/ROADMAP.md` v1.0.8 stretch

## Context

Since ADR-0014, the Analytics screen counts a view only when the page's script sends a signed beacon.
`Posts.ViewCount`, which ranks "most read" on the home page and the 404 page, was still raised on the
server for every anonymous GET of a post whose User-Agent did not look like a bot. Two problems followed:

- **Two numbers for one thing.** A request flood, a scraper with a browser User-Agent, or a cache
  revalidation raised "most read" but not Analytics.
- **One database write per post request,** on the hot path the architecture rule says to keep free of
  writes. The public `GET /api/posts/{slug}` raised it too, so an API client reading posts counted as
  readers.

## Options considered

1. **Keep the server counter** and filter harder. The beacon already does the filtering (token bound to
   the path, same-site check, per-address cap). Duplicating that would give two diverging filters.
2. **Compute "most read" from `PageViews`** when the list is built. That is a scan over a large table on
   public pages, and it changes every query that sorts by `ViewCount`.
3. **Add beacon views to `ViewCount` at flush time.** `TrafficRecorder` already writes each beacon batch.
   It also tallies the batch per post (`PostViewTally`, pure, in Core), and `IPostRepository.AddViewCountsAsync`
   adds the counts with one statement per post on one connection.

## Decision

Option 3.

- `PostViewTally.Count` maps beacon paths to slugs: single-segment paths only, so not the home page or
  nested routes. It lower-cases the slug and groups by owner and slug.
- `AddViewCountsAsync` only raises posts that readers can see (published, or scheduled and past due), so a
  beacon from an anonymous preview of a draft counts nothing. In cloud mode it matches the tenant's posts.
- The increments in `BlogController.Post` and `PostsApiController.GetPost` are removed, along with
  `IncrementViewCountAsync`.
- The update fails open inside the flush, like the rest of the recorder.

## Consequences

- "Most read" and Analytics count the same thing: a reader who ran the page's script.
- No write on the post-page request path. The count lands at the next flush (≤ 30 s).
- Readers without JavaScript no longer raise "most read". This is the same trade ADR-0014 made for
  Analytics.
- Existing `ViewCount` values were counted the old way and are kept as they are. New views add on top, so
  rankings change gradually rather than resetting.
- Tests: `PageViewBeaconTests` cover the tally, and check end to end that a post GET leaves `ViewCount`
  unchanged and that one valid beacon adds exactly one.
