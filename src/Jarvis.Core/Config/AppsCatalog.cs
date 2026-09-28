namespace Jarvis.Core.Config;

public class AppsCatalog
{
    private readonly List<AppEntry> _apps;

    public AppsCatalog(List<AppEntry> apps) => _apps = apps;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public AppEntry? FindByNameOrAlias(string text)
    {
        var needle = text.Trim().ToLowerInvariant();
        foreach (var app in _apps)
        {
            if (string.Equals(app.Name, needle, StringComparison.OrdinalIgnoreCase))
                return app;
            if (app.Aliases.Any(a => string.Equals(a, needle, StringComparison.OrdinalIgnoreCase)))
                return app;
        }
        return null;
    }
}
