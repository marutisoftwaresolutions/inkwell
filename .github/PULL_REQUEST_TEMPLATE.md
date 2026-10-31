<!-- Thanks for contributing. Keep one change per pull request. Read CONTRIBUTING.md first. -->

## What changed, and why

<!-- Describe the outcome for a user or operator, not the diff. Link the issue if there is one: "Fixes #123". -->

## How I tested it

<!-- Commands you ran and what you saw. Note whether a reachable database was available for the render tests. -->

```
dotnet build
dotnet test Blog.Tests/Blog.Tests.csproj
```

## Checklist

- [ ] `dotnet build` and `dotnet test Blog.Tests/Blog.Tests.csproj` pass locally.
- [ ] A bug fix includes the test that would have caught it; a feature includes tests for its logic.
- [ ] Schema change: a dated, idempotent script in `DBScripts/`, the matching `MigrationService` step, and `DBScripts/init.sql`.
- [ ] Behaviour, setup or configuration change: `README.md` updated and a line added under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] Admin write action: `[Authorize]`, `[ValidateAntiForgeryToken]` and an `AuditService` call.
- [ ] Timestamps use UTC; optional subsystems fail open; nothing tenant-specific is hard-coded.
- [ ] No credentials, uploads, local settings files or unrelated files are included.
