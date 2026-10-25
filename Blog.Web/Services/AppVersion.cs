using System.Reflection;

namespace Blog.Web.Services;

/// <summary>
/// The version this build was stamped with (Directory.Build.props), without the source-control suffix. Shown to
/// signed-in staff in the Desk so an operator can confirm what is running after a deploy; never sent to readers,
/// because a version number in public pages only helps scanners match known vulnerabilities.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } =
        (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
}
