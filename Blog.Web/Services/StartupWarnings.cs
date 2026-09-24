namespace Blog.Web.Services;

/// <summary>
/// Conditions found at startup that an operator should see and nobody would otherwise notice —
/// a static file shadowing a route, an ephemeral Data Protection key ring. Collected once, shown
/// on Admin → Dashboard. Singleton, append-only, read-only from the UI.
/// </summary>
public sealed class StartupWarnings
{
    private readonly List<string> _items = new();
    private readonly object _gate = new();

    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_gate) _items.Add(message);
    }

    public IReadOnlyList<string> All
    {
        get { lock (_gate) return _items.ToList(); }
    }
}
