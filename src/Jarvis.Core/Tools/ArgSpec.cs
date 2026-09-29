namespace Jarvis.Core.Tools;

public enum ArgType { String, Int }

public record ArgSpec(ArgType Type, bool Required = true, IReadOnlyList<string>? AllowedValues = null);
