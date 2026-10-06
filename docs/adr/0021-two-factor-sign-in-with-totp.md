# ADR-0021: Two-factor sign-in with authenticator-app codes, optional by default

- **Status:** Accepted (2026-10-05)
- **Date:** 2026-10-05
- **Supersedes:** —
- **Related:** `.gsd/ROADMAP.md` v1.0.8 core "Two-factor authentication"; Security rule; ADR-0002 (key ring), ADR-0005 (hashed reset tokens), ADR-0019 (backups)

## Context

A public install is probed for passwords continuously, and an Admin was protected by a password
alone. Joomla and Drupal ship two-factor in core. The owner's decision (2026-10-05): optional for
everyone by default, with Admins able to require it per role.

## Options considered

1. **TOTP from an authenticator app (RFC 6238)**: works with every common app, needs no outbound
   service, and works offline.
2. **Email or SMS codes:** they need SMTP or an SMS provider in the sign-in path. SMS is phishable and
   costs money. Email is only as strong as the mailbox.
3. **WebAuthn / passkeys:** the strongest option, but a larger change (browser ceremony, credential
   storage, HTTPS-only). It can come later beside TOTP.

## Decision

Option 1.

**Pure Core** (`Blog.Core/Services/TwoFactor.cs`):
- RFC 6238 with HMAC-SHA1, 6 digits and 30-second steps, which are the defaults of every major app;
- RFC 4648 Base32;
- a ±1-step drift window, and refusing any step at or before the last accepted one, so a code can
  never be replayed;
- ten recovery codes (`xxxxx-xxxxx`, no look-alike characters) stored as SHA-256 hashes and usable
  once each;
- `IsRequiredFor(role, roles)`;
- the `otpauth://` URI in Google's Key Uri Format.

**Storage.** Four nullable `Users` columns (schema trio, dated script `2026-10-05_add-two-factor-to-users.sql`):
- `TwoFactorSecretProtected`, Data-Protection-encrypted;
- `TwoFactorEnabledAt`;
- `TwoFactorRecoveryHashes`, a JSON array;
- `TwoFactorLastStep`, advanced by a conditional `UPDATE … WHERE LastStep < @step` so two simultaneous
  requests cannot both use one code.

The secret is read only through `IUserRepository.GetTwoFactorAsync`, never as part of `User`.

**Setting up.** A new secret lives only in a 15-minute time-limited protected token, in the setup
form, until the user proves it with a code. The QR code is rendered server-side as SVG by QRCoder
(new dependency, MIT, no System.Drawing). Recovery codes are shown once.

**Signing in.** After a correct password, a user with two-factor gets no sign-in cookie, only a
five-minute protected ticket, path-scoped to `/account/two-factor`.
- **Codes:** the code step accepts an app code or a recovery code.
- **Wrong codes:** each is audited (`Auth.TwoFactorFailed`) and reported to the IP firewall like a
  wrong password. Five end the ticket.
- **Cookie:** issued only by `UserSignInService`. A source scan keeps it the only issuer.

**API.** `POST /api/v1/auth/login` takes `code`. With two-factor on, a missing or invalid app code
is 401. Recovery codes are not accepted there. Failed API sign-ins are now reported to the firewall,
which they were not before.

**Requiring it.** `UserSettings.TwoFactorRequiredRoles` is empty by default and editable by Admins
only. `TwoFactorEnforcementMiddleware` runs right after authorization. A signed-in user whose role
requires two-factor and who has not set it up is sent to setup before any `/admin` page. That one
rule covers password sign-in, new registrations, and sessions already open when the requirement was
switched on. It reads a one-minute cache. A user cannot turn two-factor off while their role requires
it.

**Recovery.**
- **The user:** recovery codes.
- **An Admin:** "Reset 2FA" on the Users screen, audited.
- **After a restore onto a new server:** the old key ring is not there, so app codes cannot be
  checked. The code step says so and accepts recovery codes, which are plain hashes and checked
  before the secret is decrypted.

## Consequences

- Easier: a stolen password alone no longer reaches the Desk or the API for anyone who turns this on.
- Harder:
  - another schema change;
  - one new package;
  - a lost phone without recovery codes needs an Admin;
  - a backup restored elsewhere needs recovery codes or an Admin reset for each two-factor user.
- Now maintained: `TwoFactorTests`:
  - the RFC 6238 and RFC 4648 vectors;
  - drift and replay;
  - recovery codes;
  - the policy and the URI format;
  - source scans: one cookie issuer, the login order, the API order, anti-forgery and audit on every
    POST, the middleware position, recovery before decryption;
  - an end-to-end sign-in on the dev database through the real app and API.

## Proof

- Code: `Blog.Core/Services/TwoFactor.cs`, `Blog.Core/Domain/UserTwoFactor.cs`,
  `UserRepository` (two-factor section), `Blog.Web/Services/TwoFactorService.cs`,
  `Blog.Web/Services/UserSignInService.cs`, `Blog.Web/Middleware/TwoFactorEnforcementMiddleware.cs`,
  `Blog.Web/Controllers/TwoFactorController.cs`, `AccountController.Login`,
  `Api/AuthApiController.Login`, `Views/TwoFactor/*`, `Views/Shared/_AuthLayout.cshtml`
- Schema: `DBScripts/2026-10-05_add-two-factor-to-users.sql`, `MigrationService.cs`, `init.sql`
  (empty-database diff clean both ways, 375 columns);
  `DBScripts/2026-10-05_add-two-factor-required-roles-to-settings.sql` (settings field, no schema change)
- Tests: `Blog.Tests/TwoFactorTests.cs`
