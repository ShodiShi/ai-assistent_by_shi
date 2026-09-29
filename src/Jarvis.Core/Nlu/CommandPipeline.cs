using Jarvis.Core.Tools;

namespace Jarvis.Core.Nlu;

public record PipelineOutcome(string Message, int Level, string? ToolName, bool Resolved);

public class CommandPipeline
{
    private readonly IReadOnlyList<ICommandResolver> _resolvers;
    private readonly IToolRegistry _tools;
    private readonly NluContext _context;
    private PendingConfirmation? _pending;

    public CommandPipeline(IReadOnlyList<ICommandResolver> resolvers, IToolRegistry tools, NluContext context)
    {
        _resolvers = resolvers;
        _tools = tools;
        _context = context;
    }

    public async Task<PipelineOutcome> ProcessAsync(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        if (_pending is not null)
        {
            var pending = _pending;
            _pending = null;

            if (normalized is "да" or "да.")
                return await ExecuteTool(pending!.ToolName, pending.Args, confirmed: true, level: 0);

            // _pending уже обнулено выше — если это "отмена", просто сообщаем об отмене.
            if (normalized is "отмена" or "отмена.")
                return new PipelineOutcome("Отменено.", 0, null, true);

            // Не "да" и не "отмена" — ожидание сброшено, но этот же ввод не интерпретируется
            // как подтверждение отложенного действия. Мы не повторно резолвим тот же текст в
            // рамках этого вызова (это могло бы случайно снова напороться на то же опасное
            // действие через резолверы) — просто сообщаем, что не поняли, и ждём следующую
            // команду от пользователя.
            return new PipelineOutcome("Не понял команду.", 0, null, false);
        }

        foreach (var resolver in _resolvers.Where(r => r.IsAvailable).OrderBy(r => r.Level))
        {
            var result = await resolver.ResolveAsync(input, _context);
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

        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(args, new ToolContext(IsConfirmed: confirmed));
        }
        catch (Exception)
        {
            // Инструмент упал — деградация, а не падение процесса REPL.
            return new PipelineOutcome("Не получилось выполнить команду.", level, toolName, false);
        }

        if (result.Confirmation is not null)
            _pending = result.Confirmation;

        return new PipelineOutcome(result.Message, level, toolName, result.Success);
    }
}
