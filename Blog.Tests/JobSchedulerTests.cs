using Blog.Core.Services;
using Blog.Web.Services.Jobs;
using Xunit;

namespace Blog.Tests;

public class JobScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_job_that_never_ran_is_due()
    {
        Assert.True(JobSchedule.IsDue(null, TimeSpan.FromHours(24), Now));
    }

    [Fact]
    public void A_job_is_due_once_the_interval_has_passed_since_it_last_started()
    {
        Assert.False(JobSchedule.IsDue(Now.AddHours(-23), TimeSpan.FromHours(24), Now));
        Assert.True(JobSchedule.IsDue(Now.AddHours(-24), TimeSpan.FromHours(24), Now));
        Assert.True(JobSchedule.IsDue(Now.AddDays(-3), TimeSpan.FromHours(24), Now));
    }

    [Fact]
    public void Cadence_is_measured_from_the_start_so_a_slow_job_does_not_drift()
    {
        // Started 24h ago, took an hour: still due now, not an hour from now.
        Assert.True(JobSchedule.IsDue(Now.AddHours(-24), TimeSpan.FromHours(24), Now));
    }

    [Fact]
    public void A_non_positive_interval_means_always_due()
    {
        Assert.True(JobSchedule.IsDue(Now, TimeSpan.Zero, Now));
    }

    [Theory]
    [InlineData(0, 90)]      // unset → default
    [InlineData(-5, 90)]
    [InlineData(3, 7)]       // below floor
    [InlineData(90, 90)]
    [InlineData(99999, 3650)] // above ceiling
    public void Crawler_retention_days_are_clamped_to_a_sane_range(int configured, int expected)
    {
        Assert.Equal(expected, CrawlerVisitRetentionJob.ClampDays(configured));
    }
}

/// <summary>
/// The scheduler's tick against a fake ledger: the claim is the only "is it due" decision, a refused
/// claim means the job does not run, a claimed job runs and is recorded, a throwing job is recorded as
/// failed without stopping the tick, and a ledger that cannot be reached warns once and never throws.
/// </summary>
public class JobSchedulerClaimTests
{
    private sealed class FakeLedger : Blog.Core.Interfaces.IJobRunRepository
    {
        public Func<string, bool> Claim = _ => true;
        public readonly List<string> Claimed = new();
        public readonly List<(string Name, bool Ok, string? Message)> Recorded = new();

        public Task<Blog.Core.Domain.JobRun?> GetAsync(string name) => Task.FromResult<Blog.Core.Domain.JobRun?>(null);
        public Task<IReadOnlyList<Blog.Core.Domain.JobRun>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Blog.Core.Domain.JobRun>>(Array.Empty<Blog.Core.Domain.JobRun>());
        public Task<bool> TryClaimAsync(string name, DateTime nowUtc, TimeSpan interval)
        {
            Claimed.Add(name);
            return Task.FromResult(Claim(name));
        }
        public Task RecordAsync(string name, DateTime startedAtUtc, DateTime finishedAtUtc, bool succeeded, string? message)
        {
            Recorded.Add((name, succeeded, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJob : Blog.Core.Interfaces.IScheduledJob
    {
        private readonly Func<Task<string>> _run;
        public int Runs;
        public FakeJob(string name, Func<Task<string>>? run = null)
        {
            Name = name;
            _run = run ?? (() => Task.FromResult("done"));
        }
        public string Name { get; }
        public TimeSpan Interval => TimeSpan.FromHours(24);
        public Task<string> RunAsync(CancellationToken cancellationToken) { Runs++; return _run(); }
    }

    private sealed class CountingLogger : Microsoft.Extensions.Logging.ILogger<JobScheduler>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) Warnings++;
        }
    }

    private static (JobScheduler Scheduler, CountingLogger Log) Build(Blog.Core.Interfaces.IJobRunRepository ledger, params Blog.Core.Interfaces.IScheduledJob[] jobs)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => ledger);
        foreach (var job in jobs)
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => job);
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var log = new CountingLogger();
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider);
        return (new JobScheduler(scopes, log), log);
    }

    [Fact]
    public async Task A_job_whose_claim_is_refused_does_not_run()
    {
        var ledger = new FakeLedger { Claim = _ => false };
        var job = new FakeJob("nightly");
        var (scheduler, _) = Build(ledger, job);

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(new[] { "nightly" }, ledger.Claimed);
        Assert.Equal(0, job.Runs);
        Assert.Empty(ledger.Recorded);
    }

    [Fact]
    public async Task A_claimed_job_runs_once_and_its_outcome_is_recorded()
    {
        var ledger = new FakeLedger();
        var job = new FakeJob("nightly");
        var (scheduler, log) = Build(ledger, job);

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(1, job.Runs);
        var run = Assert.Single(ledger.Recorded);
        Assert.Equal("nightly", run.Name);
        Assert.True(run.Ok);
        Assert.Equal("done", run.Message);
        Assert.Equal(0, log.Warnings);
    }

    [Fact]
    public async Task A_throwing_job_is_recorded_as_failed_and_the_tick_continues_to_the_next_job()
    {
        var ledger = new FakeLedger();
        var broken = new FakeJob("broken", () => throw new InvalidOperationException("boom"));
        var healthy = new FakeJob("healthy");
        var (scheduler, log) = Build(ledger, broken, healthy);

        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(1, broken.Runs);
        Assert.Equal(1, healthy.Runs);
        Assert.Equal(2, ledger.Recorded.Count);
        Assert.False(ledger.Recorded[0].Ok);
        Assert.Contains("InvalidOperationException", ledger.Recorded[0].Message);
        Assert.Contains("boom", ledger.Recorded[0].Message);
        Assert.True(ledger.Recorded[1].Ok);
        Assert.Equal(1, log.Warnings); // the failure itself is warned; nothing else is
    }

    [Fact]
    public async Task An_unreachable_ledger_skips_the_job_warns_once_per_process_and_never_escapes()
    {
        var ledger = new FakeLedger { Claim = _ => throw new InvalidOperationException("Invalid object name 'Jobs'.") };
        var job = new FakeJob("nightly");
        var (scheduler, log) = Build(ledger, job);

        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);
        await scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(0, job.Runs);
        Assert.Empty(ledger.Recorded);
        Assert.Equal(1, log.Warnings);
    }

    [Fact]
    public async Task Once_the_ledger_is_back_a_later_outage_warns_again()
    {
        var down = false;
        var ledger = new FakeLedger { Claim = _ => down ? throw new InvalidOperationException("down") : true };
        var job = new FakeJob("nightly");
        var (scheduler, log) = Build(ledger, job);

        down = true;  await scheduler.TickAsync(CancellationToken.None);   // warn
        down = false; await scheduler.TickAsync(CancellationToken.None);   // runs, resets the warning
        down = true;  await scheduler.TickAsync(CancellationToken.None);   // warn again

        Assert.Equal(1, job.Runs);
        Assert.Equal(2, log.Warnings);
    }
}
