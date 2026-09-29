using Jarvis.Core.Tools;

namespace Jarvis.Core.Nlu;

public record PipelineOutcome(string Message, int Level, string? ToolName, bool Resolved);

public class CommandPipeline
{
    private readonly IReadOnlyList<ICommandResolver> _resolvers;
    private readonly IToolRegistry _tools;
    private readonly NluContext _context;
    private readonly Serilog.ILogger? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _confirmationTimeout;
    private PendingConfirmation? _pending;
    private DateTimeOffset _pendingSetAt;

    // logger is optional: when null, caught tool/resolver exceptions are still handled (the
    // pipeline degrades instead of crashing), they just aren't logged anywhere.
    // timeProvider/confirmationTimeout default to the real clock and 15 seconds (spec: "тайм-аут
    // подтверждения (например, 15 c условного «времени») сбрасывает ожидание") — tests inject a
    // fake TimeProvider to control elapsed time deterministically.
    public CommandPipeline(
        IReadOnlyList<ICommandResolver> resolvers,
        IToolRegistry tools,
        NluContext context,
        Serilog.ILogger? logger = null,
        TimeProvider? timeProvider = null,
        TimeSpan? confirmationTimeout = null)
    {
        _resolvers = resolvers;
        _tools = tools;
        _context = context;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _confirmationTimeout = confirmationTimeout ?? TimeSpan.FromSeconds(15);
    }

    // Single definition of the cancel word, shared with Program.cs's REPL (which handles
    // "отмена" itself so shutdown /a always gets a clear reply, even with nothing pending here).
    public static bool IsCancelCommand(string input) =>
        input.Trim().ToLowerInvariant() is "отмена" or "отмена.";

    // Drops any pending confirmation without executing it. Used by Program.cs's "отмена"
    // handler, which does not route "отмена" through ProcessAsync.
    public void ClearPending() => _pending = null;

    public async Task<PipelineOutcome> ProcessAsync(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        if (_pending is not null)
        {
            var pending = _pending;
            var expired = _timeProvider.GetUtcNow() - _pendingSetAt > _confirmationTimeout;
            _pending = null;

            if (!expired && normalized is "да" or "да.")
                return await ExecuteTool(pending!.ToolName, pending.Args, confirmed: true, level: 0);

            // _pending уже обнулено выше — если это "отмена", просто сообщаем об отмене.
            if (!expired && IsCancelCommand(input))
                return new PipelineOutcome("Отменено.", 0, null, true);

            // Либо время подтверждения истекло, либо это несвязанный ввод — в обоих случаях
            // ожидание уже сброшено выше, обрабатываем текущий ввод как новую команду ниже.
        }

        foreach (var resolver in _resolvers.Where(r => r.IsAvailable).OrderBy(r => r.Level))
        {
            ResolveResult result;
            try
            {
                result = await resolver.ResolveAsync(input, _context);
            }
            catch (Exception ex)
            {
                // Резолвер упал (например, инференс ONNX на уровне 2) — пропускаем его и пробуем
                // следующий: деградация, а не падение процесса REPL.
                _logger?.Warning(ex, "Резолвер уровня {Level} ({Resolver}) выбросил исключение, пропускаю его.",
                    resolver.Level, resolver.GetType().Name);
                continue;
            }

            if (result.Resolved && result.ToolName is not null)
                return await ExecuteTool(result.ToolName, result.Args, confirmed: false, level: result.Level);
        }

        return new PipelineOutcome("Не понял команду.", 0, null, false);
    }

    private async Task<PipelineOutcome> ExecuteTool(string toolName, IReadOnlyDictionary<string, object?> args, bool confirmed, int level)
    {
        var tool = _tools.Find(toolName);
        if (tool is null)
            return new PipelineOutcome($"Инструмент {toolName} не найден.", level, toolName, false);

        if (!ArgsValidator.Validate(tool.ArgsSchema, args, out var validationError))
        {
            _logger?.Warning("Аргументы для инструмента {ToolName} не прошли проверку схемой: {Error}", toolName, validationError);
            return new PipelineOutcome($"Не получилось выполнить команду: {validationError}.", level, toolName, false);
        }

        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(args, new ToolContext(IsConfirmed: confirmed));
        }
        catch (Exception ex)
        {
            // Инструмент упал — логируем и деградируем, а не роняем процесс REPL.
            _logger?.Warning(ex, "Инструмент {ToolName} выбросил исключение.", toolName);
            return new PipelineOutcome("Не получилось выполнить команду.", level, toolName, false);
        }

        if (result.Confirmation is not null)
        {
            _pending = result.Confirmation;
            _pendingSetAt = _timeProvider.GetUtcNow();
        }

        return new PipelineOutcome(result.Message, level, toolName, result.Success);
    }
}
