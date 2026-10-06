# ADR-0018: Editorial workflow states, with every post status decided on the server

- **Status:** Accepted (2026-09-27)
- **Date:** 2026-09-27
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Editorial workflow states"; CHANGELOG `[Unreleased]`

## Context

Inkwell has Author, Editor and Admin roles, but no handoff between them. An author without the
publish permission who pressed Publish got a silent draft and a red message; nothing told an
editor that anything was waiting. Drupal, WordPress ("Pending review") and Joomla all treat review
as core.

Reading the save path for this work turned up a hole. The status came from the button only for
"publish" and "draft"; any other value left the model-bound `Status` in place. So a request with
`Status=Published` and some other `submitAction` published a post for a user who lacked the
publish permission.

## Options considered

1. **A separate `Reviews` table** (state, reviewer, note per round) — gives a full history. It is a
   second source of truth for "is this post live?", and revisions already record each save.
2. **Two new `PostStatus` values plus two columns on `Posts`** (`ReviewNote`,
   `ReviewRequestedAt`) — status stays the one source of truth. Public queries already list
   Published / Scheduled explicitly, so the new values are invisible to readers by construction.
3. **A flag beside the existing statuses** (`Draft` + `IsInReview`) — every query would have to
   learn about the flag, and the invisible-by-construction property would be lost.

## Decision

Option 2.

**Statuses.** `PostStatus` gains `InReview` and `NeedsChanges`, appended so stored values keep
their numbers.

**Saves.** `EditorialWorkflow.DecideOnSave(current, action, canPublish, scheduledAt, now)` decides
every editor save; a posted Status is never used:

| Action | Result |
|---|---|
| `publish`, with the permission | Published, or Scheduled if the time is ahead |
| `publish`, without the permission | InReview, with a notice (it used to silently become a draft) |
| `review` | InReview |
| `draft` | Draft; stays NeedsChanges so the author keeps the note while working; stays InReview when an editor saves edits to a post in review (saving is not a decision) |
| anything else | the current status (a new post starts as Draft) |

Taking a live post off the site (review, or a draft save) always comes with a notice saying so.

For an author, saving changes to a live post submits them for review. That takes the post off the
site until an editor approves, and the editor's confirmation says so before it happens.

**Reviewing.** Two separate POST actions for users with `posts.publish`, on a post that is
`InReview`:
- **Approve** publishes it (or schedules it), audits `Post.Approved` and the publish, records a
  revision, fires the `post.published` webhook and pings IndexNow.
- **Request changes** requires a note, sets `NeedsChanges` and audits `Post.ChangesRequested` with
  an excerpt of the note.

Resubmitting stamps `ReviewRequestedAt` and clears the note. Leaving the workflow clears both.

**Desk.**
- A review panel sits above the editor. It is a partial of its own, because its action forms are
  not editors.
- The main button says "Submit for review" for authors.
- The Posts list gains "In review" and "Needs changes" filters and badges.
- The dashboard shows a "Waiting for review" queue (oldest first) to publishers, and "Sent back to
  you" to authors.

**Readers.** `EditorialWorkflow.IsLive` is the one rule for "readers can see this post". Two public
paths loaded a post by slug without it: comment submission and the social-card image. They now
answer 404 for anything not live, which also stops them serving drafts, as they did before.

## Consequences

- Easier: authors hand work to editors inside the Desk, and editors have a queue. The status
  tampering hole is closed for every role.
- Harder: an author editing a live post takes it offline until approved; there is no "pending
  revision of a live post". That would need a draft copy beside the live one, a separate decision.
  There is no email notification yet. The queue is on the dashboard and the Posts list.
- Now maintained: `EditorialWorkflowTests`:
  - the save matrix, including the tampering case and "anything else keeps the status";
  - review permissions and the note rule;
  - the live rule;
  - a source scan that no repository shows posts by excluding drafts;
  - end to end on dev, for each new state: the post page, its share image, the home page, the feed,
    both sitemaps, `llms.txt`, `llms-full.txt` and search never link to it or show its title or
    text.

## Proof

- Code: `Blog.Core/Services/EditorialWorkflow.cs`, `Blog.Core/Domain/Post.cs`,
  `PostRepository.SetWorkflowAsync` / `GetReviewQueueAsync`,
  `Blog.Web/Controllers/PostsController.cs` (Create, Edit, Approve, RequestChanges, Bulk),
  `BlogController.AddComment`, `OgImageController`, `DashboardController`,
  `Blog.Web/Views/Posts/{_ReviewPanel,Edit,Create,Index}.cshtml`, `Views/Dashboard/Index.cshtml`
- Schema: `DBScripts/2026-09-27_add-review-workflow-to-posts.sql`, `MigrationService.cs`, `init.sql`
  (column top-ups; empty-database diff clean, 371 columns)
- Tests: `Blog.Tests/EditorialWorkflowTests.cs`
