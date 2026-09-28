using Jarvis.Core.Config;
using Jarvis.Core.Tools;
using Jarvis.Core.Tools.OsActions;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeProcessLauncher : IProcessLauncher
{
    public AppEntry? LaunchedEntry;
    public void Launch(AppEntry entry) => LaunchedEntry = entry;
}

public class OpenAppToolTests
{
    private static AppsCatalog BuildCatalog() => new(new List<AppEntry>
    {
        new() { Name = "Google Chrome", Aliases = new() { "хром", "chrome" }, Path = "chrome.exe" }
    });

    [Fact]
    public async Task ExecuteAsync_KnownAlias_LaunchesApp()
    {
        var fake = new FakeProcessLauncher();
        var tool = new OpenAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "хром" }, new ToolContext());

        Assert.True(result.Success);
        Assert.NotNull(fake.LaunchedEntry);
        Assert.Equal("Google Chrome", fake.LaunchedEntry!.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownApp_ReturnsFailureWithoutLaunching()
    {
        var fake = new FakeProcessLauncher();
        var tool = new OpenAppTool(BuildCatalog(), fake);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "несуществующее" }, new ToolContext());

        Assert.False(result.Success);
        Assert.Null(fake.LaunchedEntry);
    }
}
