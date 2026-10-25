using Blog.Core.Interfaces;

namespace Blog.Web.Services.Jobs;

/// <summary>
/// Makes the responsive copies (480, 800 and 1200 px WebP) of images already in the uploads folder (ADR-0032), a batch
/// per run so a large library is worked through over several runs without holding the scheduler. New uploads get
/// their copies at upload time; this job covers everything else, including files placed on disk by a deploy.
/// </summary>
public sealed class ImageVariantJob : IScheduledJob
{
    public const int ImagesPerRun = 40;
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(40);
    private readonly ResponsiveImageStore _store;
    private readonly IConfiguration _config;

    public ImageVariantJob(ResponsiveImageStore store, IConfiguration config)
    {
        _store = store;
        _config = config;
    }

    public string Name => "image-variants";
    public TimeSpan Interval => TimeSpan.FromMinutes(10);

    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // Off only where a host must not do the work (the test suite); uploads still get their copies.
        if (!_config.GetValue("ResponsiveImages:Backfill", true)) return "Backfill is switched off (ResponsiveImages:Backfill).";
        var started = DateTime.UtcNow;
        int images = 0, copies = 0;
        var more = false;
        foreach (var path in _store.SourceImages())
        {
            // A file already finished (or given up on) costs no header read; the rest are counted only until the
            // batch is full, so a large library is not scanned in full every run.
            if (_store.IsSettled(path)) continue;
            if (_store.MissingWidths(path).Count == 0) { _store.MarkSettled(path); continue; }
            if (images >= ImagesPerRun || DateTime.UtcNow - started >= TimeBudget || cancellationToken.IsCancellationRequested) { more = true; break; }
            copies += await _store.EnsureVariantsAsync(path, cancellationToken);
            images++;
            // Done, or a file that cannot be decoded: either way it is not tried again until it changes. A temporary
            // failure (a locked file, an I/O error) is left unsettled and tried again next run.
            if (_store.MissingWidths(path, fresh: true).Count == 0 || _store.CannotDecode(path)) _store.MarkSettled(path);
        }
        if (images == 0) return "Every image has its responsive copies.";
        return $"Made {copies} copies for {images} image(s)" + (more ? "; more remain for the next run." : ".");
    }
}
