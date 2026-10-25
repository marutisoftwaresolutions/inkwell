using System.Runtime.CompilerServices;

namespace Blog.Tests;

/// <summary>Settings every test host shares, applied before the first host is built.</summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        // The image-copy backfill (ADR-0032) re-encodes every upload; a test host that lives past the scheduler's
        // start-up delay must not spend minutes on it or write files into the dev uploads folder.
        Environment.SetEnvironmentVariable("ResponsiveImages__Backfill", "false");
    }
}
