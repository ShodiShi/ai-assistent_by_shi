using Jarvis.Core.Nlu;
using Xunit;

namespace Jarvis.Core.Tests.Nlu;

public class RussianNumberParserTests
{
    [Theory]
    [InlineData("громкость на 30 процентов", 30)]
    [InlineData("прибавь звука на двадцать процентов", 20)]
    [InlineData("сделай пять процентов", 5)]
    public void TryExtractPercent_FindsValue(string text, int expected)
    {
        var ok = RussianNumberParser.TryExtractPercent(text, out var value);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryExtractPercent_NoNumber_ReturnsFalse()
    {
        Assert.False(RussianNumberParser.TryExtractPercent("сделай погромче", out _));
    }

    [Theory]
    [InlineData("поставь таймер на 5 минут", 5)]
    [InlineData("таймер на десять минут", 10)]
    public void TryExtractMinutes_FindsValue(string text, int expected)
    {
        var ok = RussianNumberParser.TryExtractMinutes(text, out var value);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }
}
