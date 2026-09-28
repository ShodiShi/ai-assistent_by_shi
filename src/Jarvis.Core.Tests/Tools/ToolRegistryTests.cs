using Jarvis.Core.Tools;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

public class FakeTool : ITool
{
    public string Name => "fake_tool";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
        => Task.FromResult(new ToolResult(true, "ok"));
}

public class ToolRegistryTests
{
    [Fact]
    public void Find_ReturnsRegisteredTool()
    {
        var registry = new ToolRegistry(new ITool[] { new FakeTool() });

        var tool = registry.Find("fake_tool");

        Assert.NotNull(tool);
        Assert.Equal("fake_tool", tool!.Name);
    }

    [Fact]
    public void Find_UnknownName_ReturnsNull()
    {
        var registry = new ToolRegistry(Array.Empty<ITool>());

        Assert.Null(registry.Find("does_not_exist"));
    }
}
