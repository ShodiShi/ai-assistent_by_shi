namespace Jarvis.Core.Tools;

// Spec: "Все инструменты и их аргументы описаны JSON-схемой, которую CommandPipeline
// использует для валидации параметров, извлечённых уровнями 1-2, до вызова ExecuteAsync."
public static class ArgsValidator
{
    public static bool Validate(IReadOnlyDictionary<string, ArgSpec> schema, IReadOnlyDictionary<string, object?> args, out string? error)
    {
        foreach (var (key, spec) in schema)
        {
            var present = args.TryGetValue(key, out var value) && value is not null
                && !(value is string s0 && string.IsNullOrWhiteSpace(s0));

            if (!present)
            {
                if (spec.Required)
                {
                    error = $"отсутствует обязательный параметр «{key}»";
                    return false;
                }
                continue;
            }

            var typeOk = spec.Type switch
            {
                ArgType.String => value is string,
                ArgType.Int => value is int,
                _ => false,
            };

            if (!typeOk)
            {
                error = $"параметр «{key}» имеет неверный тип";
                return false;
            }

            if (spec.Type == ArgType.String && spec.AllowedValues is not null)
            {
                var s = (string)value!;
                if (!spec.AllowedValues.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"параметр «{key}» имеет недопустимое значение «{s}»";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }
}
