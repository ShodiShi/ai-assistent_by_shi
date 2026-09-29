using Jarvis.Core.Config;
using Jarvis.Core.Nlu;
using Jarvis.Core.Tools;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class StubResolver : ICommandResolver
{
    private readonly ResolveResult _result;
    private readonly string? _expectedUtterance;

    // expectedUtterance is optional and defaults to null ("always match", the original
    // behavior every other test here relies on). Tests that need a resolver which behaves
    // like a real, input-aware resolver — i.e. only matches a specific trigger phrase and
    // returns Unresolved for anything else — pass it explicitly.
    public StubResolver(int level, ResolveResult result, string? expectedUtterance = null)
    {
        Level = level;
        _result = result;
        _expectedUtterance = expectedUtterance;
    }

    public int Level { get; }
    public bool IsAvailable => true;

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        if (_expectedUtterance is not null && !string.Equals(utterance, _expectedUtterance, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ResolveResult.Unresolved());

        return Task.FromResult(_result);
    }
}

public class RecordingTool : ITool
{
    public List<(IReadOnlyDictionary<string, object?> Args, bool Confirmed)> Calls = new();
    public string Name => "system_control";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        Calls.Add((args, context.IsConfirmed));
        if (!context.IsConfirmed)
        {
            var confirmation = new PendingConfirmation(Name, args, "Точно? (да/отмена)");
            return Task.FromResult(new ToolResult(false, confirmation.Prompt, confirmation));
        }
        return Task.FromResult(new ToolResult(true, "Сделано."));
    }
}

// A second, independent recording tool for a "safe" tool that never needs confirmation
// (mirrors the real OpenAppTool: always succeeds on the first call). Used to prove that a
// genuinely different command typed right after a dropped confirmation actually executes,
// not just that the pipeline avoids crashing on it.
public class RecordingOpenAppTool : ITool
{
    public List<(IReadOnlyDictionary<string, object?> Args, bool Confirmed)> Calls = new();
    public string Name => "open_app";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context)
    {
        Calls.Add((args, context.IsConfirmed));
        return Task.FromResult(new ToolResult(true, "Открываю."));
    }
}

public class CommandPipelineTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    [Fact]
    public async Task ProcessAsync_UnresolvedByAllLevels_ReturnsNotUnderstood()
    {
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.Unresolved()),
            new StubResolver(2, ResolveResult.Unresolved()),
        };
        var registry = new ToolRegistry(Array.Empty<ITool>());
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var outcome = await pipeline.ProcessAsync("расскажи анекдот");

        Assert.False(outcome.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_DangerousTool_AsksThenExecutesOnDa()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var first = await pipeline.ProcessAsync("выключи ноут");
        Assert.Contains("Точно", first.Message);
        Assert.Single(recordingTool.Calls);
        Assert.False(recordingTool.Calls[0].Confirmed);

        var second = await pipeline.ProcessAsync("да");
        Assert.Equal(2, recordingTool.Calls.Count);
        Assert.True(recordingTool.Calls[1].Confirmed);
        Assert.True(second.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_UnrelatedInputWhilePending_DropsPendingAndProcessesNormally()
    {
        var systemControlTool = new RecordingTool();
        var openAppTool = new RecordingOpenAppTool();
        var resolvers = new ICommandResolver[]
        {
            // Input-aware: each stub only matches its own trigger phrase, like real resolvers
            // (RuleBasedResolver/EmbeddingResolver) actually would. This lets the test prove
            // both halves of its own name: that the pending confirmation is genuinely dropped,
            // AND that the new input is genuinely processed as a real command — not just that
            // nothing crashes and the same stub happens to fire twice (the bug this test
            // previously let slip through, see task-12-report.md).
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
            new StubResolver(1, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 1.0, 1), expectedUtterance: "открой хром"),
        };
        var registry = new ToolRegistry(new ITool[] { systemControlTool, openAppTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        await pipeline.ProcessAsync("выключи ноут");
        var second = await pipeline.ProcessAsync("открой хром");

        // "...ProcessesNormally" half: a genuinely different, valid command typed right after
        // a dropped confirmation must actually resolve and execute.
        Assert.Single(openAppTool.Calls);
        Assert.Equal("open_app", second.ToolName);
        Assert.True(second.Resolved);

        // "DropsPending" half: the earlier pending shutdown must be genuinely gone, not
        // silently still armed. If _pending had survived, this "да" would hit the confirmation
        // branch and execute system_control with Confirmed=true.
        await pipeline.ProcessAsync("да");
        Assert.DoesNotContain(systemControlTool.Calls, c => c.Confirmed);
    }

    [Fact]
    public async Task ProcessAsync_Otmena_ClearsPendingWithoutExecuting()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            // Input-aware, same reasoning as above: "да" sent after "отмена" must not
            // spuriously re-match this stub (which would muddy the Calls count) — a real
            // resolver would correctly return Unresolved for it too.
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        await pipeline.ProcessAsync("выключи ноут");
        await pipeline.ProcessAsync("отмена");

        Assert.Single(recordingTool.Calls); // всё ещё только исходный запрос — подтверждённого выполнения не было

        // Prove "отмена" genuinely cleared _pending rather than just returning a message while
        // pending state silently survived: if it hadn't, this "да" would hit the confirmation
        // branch and execute system_control with Confirmed=true.
        await pipeline.ProcessAsync("да");
        Assert.DoesNotContain(recordingTool.Calls, c => c.Confirmed);
    }
}

// Program.cs handles "отмена" itself (so the user always gets «Отменено.», even when nothing is
// pending in the pipeline any more because the shutdown was already confirmed) and never passes
// it on to ProcessAsync. These cover the two small pipeline members that REPL path relies on.
public class CommandPipelineCancelTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    [Theory]
    [InlineData("отмена")]
    [InlineData("Отмена")]
    [InlineData("  ОТМЕНА  ")]
    [InlineData("отмена.")]
    public void IsCancelCommand_RecognizesCancelWord(string input)
    {
        Assert.True(CommandPipeline.IsCancelCommand(input));
    }

    [Theory]
    [InlineData("да")]
    [InlineData("отменить")]
    [InlineData("отмена выключения")]
    [InlineData("")]
    public void IsCancelCommand_RejectsOtherInput(string input)
    {
        Assert.False(CommandPipeline.IsCancelCommand(input));
    }

    // Since Program.cs no longer routes "отмена" through ProcessAsync, it must clear a pending
    // confirmation some other way — otherwise "выключи ноут" → "отмена" → "да" would still shut
    // the computer down.
    [Fact]
    public async Task ClearPending_DropsPendingConfirmation_SoLaterDaDoesNotExecute()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        await pipeline.ProcessAsync("выключи ноут");
        pipeline.ClearPending();
        var afterDa = await pipeline.ProcessAsync("да");

        Assert.Single(recordingTool.Calls);
        Assert.DoesNotContain(recordingTool.Calls, c => c.Confirmed);
        Assert.False(afterDa.Resolved);
    }

    [Fact]
    public void ClearPending_WithNothingPending_IsNoOp()
    {
        var pipeline = new CommandPipeline(Array.Empty<ICommandResolver>(), new ToolRegistry(Array.Empty<ITool>()), BuildContext());

        pipeline.ClearPending();
    }
}

public class ThrowingTool : ITool
{
    public string Name => "open_app";
    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, ToolContext context) =>
        throw new InvalidOperationException("boom");
}

// A resolver that fails (e.g. EmbeddingResolver's ONNX inference on malformed input once a real
// model is present). Throws either synchronously from ResolveAsync or via a faulted Task, since
// both reach the pipeline differently.
public class ThrowingResolver : ICommandResolver
{
    private readonly bool _faultedTask;

    public ThrowingResolver(int level, bool faultedTask = false)
    {
        Level = level;
        _faultedTask = faultedTask;
    }

    public int Level { get; }
    public bool IsAvailable => true;

    public Task<ResolveResult> ResolveAsync(string utterance, NluContext context)
    {
        var ex = new InvalidOperationException("resolver boom");
        if (_faultedTask)
            return Task.FromException<ResolveResult>(ex);
        throw ex;
    }
}

public class CollectingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();
    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}

public class CommandPipelineErrorHandlingTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    private static (Serilog.ILogger Logger, CollectingSink Sink) BuildLogger()
    {
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        return (logger, sink);
    }

    [Fact]
    public async Task ProcessAsync_ToolThrows_ReturnsFriendlyMessageWithoutCrashing()
    {
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { new ThrowingTool() });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext());

        var outcome = await pipeline.ProcessAsync("открой хром");

        Assert.False(outcome.Resolved);
        Assert.Equal("Не получилось выполнить команду.", outcome.Message);
    }

    // Spec: "Инструмент бросает исключение → перехватывается в CommandPipeline, логируется".
    [Fact]
    public async Task ProcessAsync_ToolThrows_LogsExceptionWithToolName()
    {
        var (logger, sink) = BuildLogger();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 1.0, 1)),
        };
        var registry = new ToolRegistry(new ITool[] { new ThrowingTool() });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), logger);

        var outcome = await pipeline.ProcessAsync("открой хром");

        Assert.Equal("Не получилось выполнить команду.", outcome.Message);
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        Assert.IsType<InvalidOperationException>(logEvent.Exception);
        Assert.Equal("\"open_app\"", logEvent.Properties["ToolName"].ToString());
    }

    // Spec: "деградация, а не падение процесса" — one broken resolver must not crash the REPL
    // nor stop the pipeline from falling back to the next working resolver.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_ResolverThrows_SkipsItLogsAndFallsBackToNextResolver(bool faultedTask)
    {
        var (logger, sink) = BuildLogger();
        var openAppTool = new RecordingOpenAppTool();
        var resolvers = new ICommandResolver[]
        {
            new ThrowingResolver(1, faultedTask),
            new StubResolver(2, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 0.9, 2)),
        };
        var registry = new ToolRegistry(new ITool[] { openAppTool });
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), logger);

        var outcome = await pipeline.ProcessAsync("открой хром");

        Assert.True(outcome.Resolved);
        Assert.Equal("open_app", outcome.ToolName);
        Assert.Equal(2, outcome.Level);
        Assert.Single(openAppTool.Calls);
        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, logEvent.Level);
        Assert.IsType<InvalidOperationException>(logEvent.Exception);
    }

    [Fact]
    public async Task ProcessAsync_OnlyResolverThrows_WithoutLogger_ReturnsNotUnderstood()
    {
        var resolvers = new ICommandResolver[] { new ThrowingResolver(1) };
        var pipeline = new CommandPipeline(resolvers, new ToolRegistry(Array.Empty<ITool>()), BuildContext());

        var outcome = await pipeline.ProcessAsync("что угодно");

        Assert.False(outcome.Resolved);
        Assert.Equal("Не понял команду.", outcome.Message);
    }
}

// Deterministic clock for testing the confirmation timeout without real delays.
public class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

// Spec: "тайм-аут подтверждения (например, 15 c условного «времени») сбрасывает ожидание" — a
// pending confirmation that sits too long must expire, so a stray/late "да" can never execute a
// dangerous action the user is no longer actively confirming.
public class CommandPipelineTimeoutTests
{
    private static NluContext BuildContext() => new(new AppsCatalog(new List<AppEntry>()));

    [Fact]
    public async Task ProcessAsync_DaWithinTimeout_StillExecutes()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var clock = new ManualTimeProvider();
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), timeProvider: clock, confirmationTimeout: TimeSpan.FromSeconds(15));

        await pipeline.ProcessAsync("выключи ноут");
        clock.Advance(TimeSpan.FromSeconds(10));
        var outcome = await pipeline.ProcessAsync("да");

        Assert.True(recordingTool.Calls[1].Confirmed);
        Assert.True(outcome.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_DaAfterTimeout_DoesNotExecuteAndDropsPending()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var clock = new ManualTimeProvider();
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), timeProvider: clock, confirmationTimeout: TimeSpan.FromSeconds(15));

        await pipeline.ProcessAsync("выключи ноут");
        clock.Advance(TimeSpan.FromSeconds(16));
        var outcome = await pipeline.ProcessAsync("да");

        Assert.DoesNotContain(recordingTool.Calls, c => c.Confirmed);
        Assert.False(outcome.Resolved);
    }

    [Fact]
    public async Task ProcessAsync_ExpiredPending_ThenGenuinelyDifferentCommand_ProcessesIt()
    {
        var systemControlTool = new RecordingTool();
        var openAppTool = new RecordingOpenAppTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
            new StubResolver(1, ResolveResult.For("open_app", new Dictionary<string, object?> { ["name"] = "хром" }, 1.0, 1), expectedUtterance: "открой хром"),
        };
        var registry = new ToolRegistry(new ITool[] { systemControlTool, openAppTool });
        var clock = new ManualTimeProvider();
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), timeProvider: clock, confirmationTimeout: TimeSpan.FromSeconds(15));

        await pipeline.ProcessAsync("выключи ноут");
        clock.Advance(TimeSpan.FromSeconds(20));
        var outcome = await pipeline.ProcessAsync("открой хром");

        Assert.Single(openAppTool.Calls);
        Assert.True(outcome.Resolved);
        Assert.Equal("open_app", outcome.ToolName);
    }

    [Fact]
    public async Task ProcessAsync_NoTimeoutConfigured_DefaultsTo15Seconds()
    {
        var recordingTool = new RecordingTool();
        var resolvers = new ICommandResolver[]
        {
            new StubResolver(1, ResolveResult.For("system_control", new Dictionary<string, object?> { ["action"] = "shutdown" }, 1.0, 1), expectedUtterance: "выключи ноут"),
        };
        var registry = new ToolRegistry(new ITool[] { recordingTool });
        var clock = new ManualTimeProvider();
        var pipeline = new CommandPipeline(resolvers, registry, BuildContext(), timeProvider: clock);

        await pipeline.ProcessAsync("выключи ноут");
        clock.Advance(TimeSpan.FromSeconds(16));
        await pipeline.ProcessAsync("да");

        Assert.DoesNotContain(recordingTool.Calls, c => c.Confirmed);
    }
}
