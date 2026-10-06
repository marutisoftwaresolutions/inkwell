# ADR-0028: Newsletters are frozen when queued and sent in batches by the job runner

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** the open v1.0.7/v1.0.8 owner decision "newsletter sending: build or delete" (owner chose *build*, 2026-10-05)
- **Related:** ADR-0001 (single scheduler), ADR-0016 (webhook delivery queue, the pattern reused), ADR-0023 (sign-up form)

## Context

Readers could subscribe and confirm, but nothing ever sent a newsletter. `AuditActions.NewsletterSent` had no
caller. The old `Newsletters`, `MemberNewsletters` and `Emails` tables had no code. `Emails` requires a post and has
no per-recipient rows, so it cannot log a send. The email service swallowed every SMTP error, so a sender could not
retry. Unsubscribing happened on a plain GET, so link scanners in mail systems would unsubscribe people. The
confirmation email put the subscriber's typed name into HTML unescaped. `Members` has no tenant column.

## Decision

- **Two tables:** `NewsletterIssues` and `NewsletterDeliveries` (one row per recipient, unique per issue and member).
  - The schema trio is `DBScripts/2026-10-05_create-newsletter-issues-and-deliveries.sql`.
  - The old tables are left unused.
- **Desk (Admin → Newsletter, Admin-only):**
  - A draft has a subject, preview text, a Markdown introduction (raw HTML escaped) and up to six posts shown as
    cards.
  - You can preview it in a sandboxed frame, send a test to yourself, then send it.
  - Sending is confirmed with the recipient count, and needs the typed word SEND at five or more recipients.
  - Drafts can be deleted; queued and sent issues are a record.
- **Frozen at queue time:** queuing renders the email once (`NewsletterEmail`, pure, in Core).
  - It is HTML in a single 600 px table with inline styles, plus a plain-text part, with everything encoded and every
    link absolute.
  - Every recipient gets the same issue even if posts change meanwhile.
  - The per-recipient unsubscribe link is a placeholder, filled in at send time.
- **Sending:** `NewsletterSendJob`, a scheduled job like the others, every minute.
  - It expands a queued issue to one delivery per confirmed subscriber; this is idempotent.
  - Each run sends up to `Newsletter:BatchSize` copies (default 50) within 40 seconds, so a large list goes out over
    several minutes and one slow SMTP server cannot stall the scheduler.
  - Each copy carries `List-Unsubscribe` and `List-Unsubscribe-Post` (RFC 8058 one-click).
  - A failure is retried after 5 and 30 minutes, then marked failed with the server's reason.
  - Someone who unsubscribed after the issue was queued is skipped.
  - When nothing is pending, the issue is marked sent and `Newsletter.Sent` is audited on behalf of the Admin who
    queued it.
  - **Stop sending** cancels whatever has not gone out.
  - Without SMTP configured, nothing is attempted, and the screen says so.
- **Email service:** `IEmailService.SendMessageAsync` returns a result instead of swallowing errors, and supports a
  plain-text part and headers. The old fire-and-forget method is unchanged for transactional mail.
- **Unsubscribe:**
  - GET shows a confirmation page.
  - POST unsubscribes, as does the one-click POST from mail providers. It has no anti-forgery token: the unguessable
    token in the address is the proof.
- **Fixed alongside:**
  - The confirmation email escapes the subscriber's name.
  - The welcome email no longer promises automatic mails.
  - Deleting a subscriber is audited, by id only, keeping the address out of the permanent trail.
- **Scope limit:** single-site installs only. Subscribers carry no tenant, so the screen answers 404 in cloud mode,
  like Backups and Export.

## Consequences

- An Admin can run a newsletter without a third-party service. Deliverability (SPF, DKIM, DMARC on the sending domain)
  stays the operator's job and is pointed out in the README.
- `System.Net.Mail` opens one connection per message. That is fine at the default pace; a list of tens of thousands
  would want a pooled client or a provider API, which is a later decision with its own ADR.
- No open or click tracking: no pixels, no rewritten links. That is consistent with the site's no-tracker stance.
- Backups carry issues (content) but not the send log, which, like webhook deliveries, would re-send if replayed.

## Proof

- `NewsletterTests`:
  - the renderer: encoding, absolute links, raw HTML escaped, the per-recipient link, plain text;
  - the job end to end on dev with a recording mail service: one copy per confirmed subscriber with their own link
    and one-click headers, a leaver skipped, a failing address retried then failed, the issue finished and audited,
    a re-run sending nothing;
  - unsubscribe: GET only asks, one-click unsubscribes, an unknown token is 404;
  - source rules: Admin-only, anti-forgery, audit, cloud 404.
- `AdminPagesSmokeTests.A_newsletter_draft_is_written_previewed_and_deleted_from_the_desk`.
- The empty-database schema diff is clean.
