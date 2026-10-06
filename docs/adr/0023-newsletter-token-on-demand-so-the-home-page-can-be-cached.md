# ADR-0023: The newsletter form fetches its anti-forgery token on demand, so the home page can be cached

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 stretch "Home page served from a CDN cache"; ADR-0012 (traffic recording); the 2026-09 homepage request flood

## Context

The home page shows the newsletter sign-up form, and the form rendered `@Html.AntiForgeryToken()`. Generating
a token makes ASP.NET set an anti-forgery cookie and send `Cache-Control: no-cache, no-store`. The form also
read `TempData`, which sent a second `Set-Cookie`. So `/` could not be cached by a CDN. Forcing an edge TTL
would strip the cookie and break the form for every reader who got the cached copy. During the 2026-09 flood
every request for `/` was a full render on the origin. Caching `/` at the edge is the cheapest defence.

## Options considered

1. **Drop anti-forgery from subscribe.** The action has no signed-in state to protect, double opt-in limits
   abuse, and reCAPTCHA covers bots. But it removes a check the codebase applies to every form post, and
   the next reviewer would have to rediscover why.
2. **A token signed with Data Protection in the markup**, like the comment form. It is still unique per
   render, so a cached copy would carry a stale or shared token.
3. **Fetch the token when the reader presses Subscribe.** The page carries an empty slot. The form's script
   calls `GET /newsletter/token` (`no-store`, `X-Robots-Tag: noindex`), fills the slot and posts.

## Decision

Option 3.

- `_NewsletterSignup` renders no token and reads no `TempData`. Its script fetches the token before
  posting.
- `NewsletterController.Subscribe` checks the token itself (`IAntiforgery.IsRequestValidAsync`) instead of
  through `[ValidateAntiForgeryToken]`, and writes nothing without a valid one:
  - **from the script:** it answers 400 with "The sign-up form has expired. Reload the page and try again.";
  - **without JavaScript:** it renders `Views/Newsletter/Subscribe` (`noindex`), a confirm page with its own
    token and the address the reader typed, so the reader finishes with one more press.
- Results and errors for the no-JavaScript path are shown on that page, not passed through `TempData` and
  a redirect. That path previously lost its errors.

## Consequences

- An anonymous `GET /` sets no cookie and is not `no-store`. An operator can put an edge cache rule on it.
- The anti-forgery guarantee for subscribe is unchanged: no write without a valid token. The check moved;
  it did not go away (`HomePageCacheTests` scans for it).
- Readers without JavaScript press Subscribe twice.
- **Post pages are not cacheable yet.** The comment form still renders a token. Making it on-demand needs
  the same treatment for the reply forms and the comment-draft `TempData`, and is a separate change.
