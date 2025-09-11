using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace LGTM.Stack;

public static class TestLogging
{
    public static void TestDirectLokiLogging()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.GrafanaLoki("http://localhost:3100", new[]
            {
                new LokiLabel { Key = "app", Value = "lgtm-stack-test" },
                new LokiLabel { Key = "test", Value = "direct" }
            })
            .CreateLogger();

        logger.Information("Direct test log message {Timestamp}", DateTime.UtcNow);
        logger.Warning("Test warning message");
        logger.Error("Test error message");
        
        Log.CloseAndFlush();
        logger.Dispose();
        
        Console.WriteLine("Test logs sent to Loki. Check Grafana!");
    }
}