# ADR-0016: Webhooks are queued in the database, sent by the job runner, signed, and guarded against SSRF at the socket

- **Status:** Accepted (2026-09-27)
- **Date:** 2026-09-27
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Webhooks"; CHANGELOG `[Unreleased]`; ADR-0001 (in-process job scheduler); ADR-0002 (Data Protection key ring)

## Context

Inkwell emitted no outbound event of any kind. Publishing could not trigger a static-site rebuild,
a social post or a CDN purge. Webhooks are the most-used integration point in Ghost, and core in
WordPress, Drupal and Joomla. Four constraints shaped the design:

- **Publishing must never wait on a third party** (architecture rule: fail open for anything
  optional).
- **Nothing outbound may run on the request path.**
- **A URL the operator types must never let the server reach itself or its network.** On a hosted
  box that means `127.0.0.1`, private ranges and the cloud metadata address `169.254.169.254`.
- **Scheduled posts go live when first read, not through a save,** so there is no request at the
  moment they publish.

## Options considered

1. **Send inline from the controller** — simple, but a slow endpoint stalls every publish, and a
   failure is lost.
2. **In-memory queue with a background loop** — loses events on recycle, and the architecture rule
   forbids a second scheduler.
3. **Outbox table plus the existing `JobScheduler`** — durable, retryable and visible in a log. It
   costs one insert per subscribed webhook at publish time and up to a minute of latency.
4. **External queue (Hangfire, a broker)** — a dependency a self-hoster has to run. Rejected by the
   architecture rule.

## Decision

- **Tables.** `Webhooks` holds each endpoint:
  - URL;
  - events, as a comma list;
  - enabled flag;
  - signing secret, protected with Data Protection purpose `Inkwell.WebhookSecret.v1`;
  - site base URL captured at save time.

  `WebhookDeliveries` is the outbox and the log. A filtered unique index on `(WebhookId, EventKey)`
  makes a keyed event (a scheduled post's publish) queue at most once per webhook.
- **Queueing.** Controllers call `WebhookPublisher` after a committed write:
  - posts: create, edit, bulk, and the JSON API;
  - pages: create and update;
  - a reader's comment.

  The publisher decides the event with the pure `WebhookRules.EventForPostSave`, from what readers
  could see before and after: `post.published`, `post.updated` or `post.unpublished`. It inserts
  one pending delivery per subscribed, enabled webhook, and swallows its own failures.
- **Delivery.** The `webhooks` job (`IScheduledJob`, 1 minute) does three things:
  - announces scheduled posts whose time has come (24-hour look-back, de-duplicated by key, and only
    to webhooks that existed before the scheduled time);
  - sends due deliveries, at most 20 per run within a 40-second budget;
  - retries failures after 1 min, 5 min, 30 min and 2 h, then marks them failed (five attempts);
  - prunes finished deliveries after 30 days.
- **Signing.** `X-Inkwell-Signature: t=<unix>,v1=<hex HMAC-SHA256 of "t.body">`.
  `X-Inkwell-Delivery` carries an event id that a redelivery keeps, so receivers can de-duplicate.
- **SSRF.**
  - `WebhookRules.ValidateUrl` refuses non-http(s) schemes, embedded credentials, local names and
    literal internal addresses at save.
  - The sender's `SocketsHttpHandler.ConnectCallback` resolves the host itself and connects only if
    **every** resolved address is public (`WebhookRules.AreAllPublic`). This defeats names that
    resolve, or re-resolve, to internal addresses.
  - Redirects are not followed. The timeout is 10 s, and 500 characters of the response are kept.
- **Desk.** Admin → Webhooks (`AdminOnly`) has list, editor and delivery log. The secret is
  write-only: a new webhook starts with a generated one to copy, and editing leaves it blank to
  keep the current one. "Send test" and "Redeliver" attempt immediately (an explicit admin action
  may call outbound HTTP) and report the endpoint's answer.
- **Audit.** `Webhook.Created`, `Webhook.Updated`, `Webhook.Deleted`, `Webhook.Redelivered` and
  `Webhook.Tested`. The secret is never audited, only "secret replaced".

## Consequences

- Easier: integrations without code changes. A dead endpoint costs retries, never a publish. Every
  attempt is visible and can be redelivered.
- Harder: up to a minute's latency, and longer when an earlier delivery is slow. Payloads carry
  content (title, slug, URL, excerpt; a comment's author name and text, never the email or IP) to
  an address the operator chooses. That is why the screen is Admin-only.
- A rotated or lost Data Protection key ring makes stored secrets unreadable. Deliveries then fail
  with a message asking for a new secret; they never go out unsigned.
- Loopback and private targets are refused even for testing on a developer machine. Use a public
  request bin, or a tunnel.
- Now maintained:
  - `WebhookTests`: the SSRF rules including rebinding, the signature, retries, event mapping, and
    queue / de-duplicate / deliver / retry with the network replaced;
  - `AdminPagesSmokeTests`: list, editor, the existing-webhook editor and the log render;
  - the empty-database schema diff, including the filtered index.

## Proof

- Code: `Blog.Core/Domain/Webhook.cs`, `Blog.Core/Services/WebhookRules.cs`,
  `Blog.Infrastructure/Data/Repositories/WebhookRepository.cs`,
  `Blog.Web/Services/Webhooks/{WebhookPublisher,WebhookSender}.cs`,
  `Blog.Web/Services/Jobs/WebhookDeliveryJob.cs`, `Blog.Web/Controllers/WebhooksController.cs`,
  `Blog.Web/Views/Webhooks/*`
- Schema: `DBScripts/2026-09-27_create-webhooks-tables.sql`, `MigrationService.cs`, `init.sql` §36a.
  The filtered index is created through `EXEC` with `QUOTED_IDENTIFIER ON`, because `sqlcmd` leaves
  it off.
- Tests: `Blog.Tests/WebhookTests.cs`, `Blog.Tests/AdminPagesSmokeTests.cs`
