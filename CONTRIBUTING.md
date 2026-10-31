# Contributing to Inkwell

Thanks for your interest. Bug reports, fixes, documentation improvements and well-scoped features are welcome.

## Before you start

- **Bugs:** open an issue using the bug report template. Include the Inkwell version, how you run it (Docker Compose, `dotnet run`, IIS) and the exact steps.
- **Features:** open an issue first so we can agree on scope before you write code. Small, focused changes are merged fastest.
- **Questions:** use [Discussions](https://github.com/marutisoftwaresolutions/inkwell/discussions), not Issues.
- **Security problems:** do not open an issue. See [SECURITY.md](SECURITY.md).

## Development setup

Prerequisites: the .NET 10 SDK and SQL Server 2019+ (Express, Developer or LocalDB), or Docker.

```bash
git clone https://github.com/marutisoftwaresolutions/inkwell.git
cd inkwell
cp Blog.Web/appsettings.example.json Blog.Web/appsettings.json   # set your connection string
dotnet build
dotnet test Blog.Tests/Blog.Tests.csproj
dotnet run --project Blog.Web
```

Integration tests that render pages need a reachable database and skip themselves when there is none.

## Project layout

| Project | Purpose |
|---|---|
| `Blog.Core` | Domain types, interfaces and pure services. No ASP.NET, no Dapper. |
| `Blog.Infrastructure` | Dapper repositories, `MigrationService`, dependency registration. |
| `Blog.Web` | Controllers, views, middleware, background jobs. |
| `Blog.Tests` | xUnit unit, source-scanning and render tests. |

Dependencies point one way: Web → Infrastructure → Core.

## Rules a pull request must follow

1. **Tests.** A fix ships with the test that would have caught it; a feature ships with tests for its logic. `dotnet test` must pass.
2. **Database changes** land in three places in the same PR: a dated, idempotent script in `DBScripts/YYYY-MM-DD_description.sql`, the matching step in `Blog.Infrastructure/Data/MigrationService.cs`, and `DBScripts/init.sql` so a fresh install is complete.
3. **Docs follow code.** If behaviour, setup or configuration changes, update `README.md` and add a line under `## [Unreleased]` in `CHANGELOG.md`.
4. **Admin write actions** are protected (`[Authorize]`, `[ValidateAntiForgeryToken]`) and recorded through `AuditService`.
5. **Time is UTC** in code and SQL. **Secrets** go through ASP.NET Data Protection. **Optional subsystems fail open**: the firewall, analytics, email or a background job must never turn a page into a 500.
6. **No new runtime dependencies** without discussing it in the issue first. Inkwell self-hosts every asset and calls no third-party script.
7. **Nothing tenant-specific** is hard-coded: site name, domain, topics and copy come from settings.

## Pull request checklist

- [ ] One change per PR, with a title that says what changed for the user.
- [ ] `dotnet build` and `dotnet test Blog.Tests/Blog.Tests.csproj` pass locally.
- [ ] Schema change: dated script + `MigrationService` + `init.sql`.
- [ ] `README.md` and `CHANGELOG.md` `[Unreleased]` updated where behaviour changed.
- [ ] No credentials, uploads or local settings files are included.

## Code style

Follow the surrounding code. Four-space indentation, explicit SQL with parameters, short methods, and comments that say why rather than what. Run `dotnet format` on the files you touched if in doubt.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
