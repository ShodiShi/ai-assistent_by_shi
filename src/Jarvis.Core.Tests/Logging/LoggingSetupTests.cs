using Jarvis.Core.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.TestCorrelator;
using Xunit;

namespace Jarvis.Core.Tests.Logging;

public class LoggingSetupTests
{
    [Fact]
    public void CommandLogEntry_LogsAllRequiredFields()
    {
        using (TestCorrelator.CreateContext())
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            var entry = new CommandLogEntry(
                Time: DateTimeOffset.UtcNow,
                Phrase: "открой хром",
                Level: 1,
                ToolName: "open_app",
                Result: "success");

            Log.Information("{@Entry}", entry);

            var logEvent = Assert.Single(TestCorrelator.GetLogEventsFromCurrentContext());
            var props = logEvent.Properties["Entry"].ToString();
            Assert.Contains("открой хром", props);
            Assert.Contains("open_app", props);
            Assert.Contains("success", props);
        }
    }
}
