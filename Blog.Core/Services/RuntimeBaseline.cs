namespace Blog.Core.Services;

/// <summary>
/// The .NET runtime patch level this release of Inkwell was built and tested on. Inkwell runs on the
/// runtime the operator installs, so a security fix in ASP.NET Core reaches a site when the operator
/// updates that runtime, not when they update Inkwell. This lets the Desk say so in plain words when
/// the server is behind, without calling out to the network. Raise <see cref="TestedOn"/> in every
/// release (it is also the figure the release notes quote). Pure.
/// </summary>
public static class RuntimeBaseline
{
    /// <summary>The runtime this release was tested on; 10.0.14 carries the October 2026 security fixes.</summary>
    public static readonly Version TestedOn = new(10, 0, 14);

    /// <summary>
    /// What to tell the operator, or null when the running runtime is at or past the tested level. A
    /// newer major or minor line is not judged: this release knows nothing about it.
    /// </summary>
    public static string? Problem(Version running)
    {
        if (running.Major != TestedOn.Major || running.Minor != TestedOn.Minor) return null;
        if (running.Build >= TestedOn.Build) return null;
        return $"This server runs .NET {Short(running)}. This release of Inkwell was tested on .NET {Short(TestedOn)}, which includes security fixes the older runtime lacks. " +
               "Update the .NET runtime on the server (the ASP.NET Core Runtime, or the Docker base image); Inkwell itself does not need to change.";
    }

    public static string Short(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
}
