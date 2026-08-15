// Integration tests (WebApplicationFactory) share the app's single SQL Server database. Running test
// classes in parallel let one test's transient rows (e.g. a briefly-published imported post) leak into
// another's assertions (e.g. the sitemap smoke test), causing flaky failures. The suite is small and
// fast, so serialize it for deterministic, isolation-safe runs.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
