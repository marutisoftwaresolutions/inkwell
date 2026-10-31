# Security Policy

Inkwell runs on public servers and is a target for automated attacks, so we take reports seriously and act on them quickly.

## Supported versions

Only the latest release receives security fixes. Please upgrade before reporting.

| Version | Supported |
|---|---|
| Latest release ([Releases](https://github.com/marutisoftwaresolutions/inkwell/releases)) | Yes |
| Older releases | No |

## Reporting a vulnerability

**Please do not open a public issue for a security problem.**

Email **hello@useinkwell.app** with the subject `Inkwell security report`. Include:

- the Inkwell version and how it is deployed (Docker Compose, `dotnet run`, IIS);
- steps to reproduce, or a proof of concept;
- the impact as you understand it;
- whether you want to be credited in the release notes.

You will get an acknowledgement within 3 working days. We aim to confirm the issue and agree on a timeline within 10 days, and to ship a fix in a new release as soon as it is ready. We will keep you informed and ask that you keep the report private until the fix is released.

## Scope

In scope: anything in this repository, including the Desk (admin), the public site, the REST API, the IP firewall, the importers and exporters, the Docker configuration and the SQL scripts.

Out of scope: vulnerabilities in third-party software you host alongside Inkwell (SQL Server, Caddy, the operating system), and findings that require an already-compromised admin account.

## Good to know

- Admin actions require authentication, an anti-forgery token and an audit entry.
- Secrets (preview links, form tokens, stored credentials) use ASP.NET Core Data Protection; passwords are hashed.
- Proxy headers such as `X-Forwarded-For` are ignored unless the operator explicitly enables them.
