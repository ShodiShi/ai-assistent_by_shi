using Jarvis.Core.Tools;
using Xunit;

namespace Jarvis.Core.Tests.Tools;

// Spec: "Все инструменты и их аргументы описаны JSON-схемой, которую CommandPipeline
// использует для валидации параметров, извлечённых уровнями 1-2, до вызова ExecuteAsync."
public class ArgsValidatorTests
{
    private static readonly IReadOnlyDictionary<string, ArgSpec> NameSchema = new Dictionary<string, ArgSpec>
    {
        ["name"] = new ArgSpec(ArgType.String, Required: true),
    };

    private static readonly IReadOnlyDictionary<string, ArgSpec> ActionSchema = new Dictionary<string, ArgSpec>
    {
        ["action"] = new ArgSpec(ArgType.String, Required: true, AllowedValues: new[] { "up", "down" }),
        ["amount"] = new ArgSpec(ArgType.Int, Required: false),
    };

    [Fact]
    public void Validate_AllRequiredPresentWithCorrectTypes_ReturnsTrue()
    {
        var args = new Dictionary<string, object?> { ["name"] = "хром" };

        var ok = ArgsValidator.Validate(NameSchema, args, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_MissingRequiredArg_ReturnsFalse()
    {
        var args = new Dictionary<string, object?>();

        var ok = ArgsValidator.Validate(NameSchema, args, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_RequiredStringIsEmpty_ReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["name"] = "" };

        var ok = ArgsValidator.Validate(NameSchema, args, out _);

        Assert.False(ok);
    }

    [Fact]
    public void Validate_RequiredStringIsWhitespace_ReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["name"] = "   " };

        var ok = ArgsValidator.Validate(NameSchema, args, out _);

        Assert.False(ok);
    }

    [Fact]
    public void Validate_WrongType_ReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["name"] = 42 };

        var ok = ArgsValidator.Validate(NameSchema, args, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_OptionalArgMissing_StillReturnsTrue()
    {
        var args = new Dictionary<string, object?> { ["action"] = "up" };

        var ok = ArgsValidator.Validate(ActionSchema, args, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_OptionalArgWrongType_ReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["action"] = "up", ["amount"] = "thirty" };

        var ok = ArgsValidator.Validate(ActionSchema, args, out _);

        Assert.False(ok);
    }

    [Fact]
    public void Validate_AllowedValueMatches_ReturnsTrue()
    {
        var args = new Dictionary<string, object?> { ["action"] = "down" };

        var ok = ArgsValidator.Validate(ActionSchema, args, out _);

        Assert.True(ok);
    }

    [Fact]
    public void Validate_ValueNotInAllowedList_ReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["action"] = "sideways" };

        var ok = ArgsValidator.Validate(ActionSchema, args, out var error);

        Assert.False(ok);
        Assert.Contains("sideways", error);
    }

    [Fact]
    public void Validate_AllowedValueIsCaseInsensitive()
    {
        var args = new Dictionary<string, object?> { ["action"] = "UP" };

        var ok = ArgsValidator.Validate(ActionSchema, args, out _);

        Assert.True(ok);
    }

    [Fact]
    public void Validate_EmptySchema_AlwaysReturnsTrue()
    {
        var ok = ArgsValidator.Validate(new Dictionary<string, ArgSpec>(), new Dictionary<string, object?> { ["whatever"] = 123 }, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_ExtraArgsNotInSchema_AreIgnored()
    {
        var args = new Dictionary<string, object?> { ["name"] = "хром", ["extra"] = "не важно" };

        var ok = ArgsValidator.Validate(NameSchema, args, out _);

        Assert.True(ok);
    }
}
