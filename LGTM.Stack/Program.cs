using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NBomber.CSharp;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Create ActivitySource for custom traces
var activitySource = new ActivitySource("LGTM.Stack");

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("LGTM.Stack")
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri("http://localhost:4317");
                options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Map Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// Add some sample endpoints to generate logs and metrics
app.MapGet("/", () => 
{
    Log.Information("Root endpoint accessed");
    return "Hello World! Check your Grafana for logs and metrics.";
})
.WithName("GetRoot");

app.MapGet("/health", () => 
{
    Log.Information("Health check requested");
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
})
.WithName("GetHealth");

app.MapGet("/error", () => 
{
    Log.Error("Error endpoint accessed - simulating an error");
    throw new InvalidOperationException("This is a test error for logging");
})
.WithName("GetError");

app.MapGet("/test-logs", () => 
{
    Log.Information("Test log entry - Information level");
    Log.Warning("Test log entry - Warning level");
    Log.Error("Test log entry - Error level");
    
    return Results.Ok(new { 
        message = "Test logs generated", 
        timestamp = DateTime.UtcNow,
        levels = new[] { "Information", "Warning", "Error" }
    });
})
.WithName("TestLogs");

app.MapGet("/loki-test", async () => 
{
    Log.Information("Testing direct Loki connectivity from application");
    
    using var client = new HttpClient();
    try 
    {
        var response = await client.GetAsync("http://localhost:3100/ready");
        var status = response.IsSuccessStatusCode ? "accessible" : "not accessible";
        
        Log.Information("Loki status check: {Status}", status);
        
        return Results.Ok(new { 
            lokiStatus = status,
            timestamp = DateTime.UtcNow 
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to connect to Loki");
        return Results.Problem("Failed to connect to Loki");
    }
})
.WithName("LokiTest");

app.MapGet("/force-logs", () => 
{
    LGTM.Stack.TestLogging.TestDirectLokiLogging();
    
    Log.Information("Forced log test completed at {Timestamp}", DateTime.UtcNow);
    Log.Warning("This is a warning from the main application");
    Log.Error("This is an error from the main application");
    
    return Results.Ok(new { 
        message = "Direct logging test completed",
        timestamp = DateTime.UtcNow,
        instruction = "Check your Grafana Loki for logs with app=lgtm-stack-test and app=lgtm-stack labels"
    });
})
.WithName("ForceLogs");

app.MapGet("/trace-test", async (HttpClient httpClient) => 
{
    using var activity = activitySource.StartActivity("TraceTest");
    activity?.SetTag("test.type", "manual");
    activity?.SetTag("endpoint", "trace-test");
    
    Log.Information("Starting trace test with activity ID: {ActivityId}", activity?.Id);
    
    // Make an HTTP call to generate child spans
    try 
    {
        var response = await httpClient.GetAsync("https://api.github.com/zen");
        var content = await response.Content.ReadAsStringAsync();
        
        activity?.SetTag("http.response_size", content.Length);
        activity?.SetStatus(ActivityStatusCode.Ok);
        
        Log.Information("Trace test completed successfully");
        
        return Results.Ok(new { 
            message = "Trace test completed",
            traceId = activity?.TraceId.ToString(),
            spanId = activity?.SpanId.ToString(),
            timestamp = DateTime.UtcNow,
            instruction = "Check your Grafana Tempo for traces"
        });
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Log.Error(ex, "Error during trace test");
        throw;
    }
})
.WithName("TraceTest");

// Auto-open browser to Swagger UI and run load tests
var urls = app.Urls;
app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = urls.FirstOrDefault() ?? "https://localhost:5001";
    var swaggerUrl = $"{url}/swagger";
    
    Log.Information("Opening browser to Swagger UI at {SwaggerUrl}", swaggerUrl);
    
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {swaggerUrl}") { CreateNoWindow = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", swaggerUrl);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", swaggerUrl);
        }
    }
    catch (Exception ex)
    {
        Log.Warning("Could not auto-open browser: {Error}", ex.Message);
    }
    
    // Start load testing after a short delay
    Task.Run(async () =>
    {
        await Task.Delay(2000); // Wait 2 seconds for the app to fully start
        await RunLoadTests(url);
    });
});

async Task RunLoadTests(string baseUrl)
{
    Log.Information("Starting NBomber load tests against {BaseUrl}", baseUrl);
    
    // Define scenarios for each endpoint
    var scenarios = new[]
    {
        CreateEndpointScenario("root", $"{baseUrl}/", "Root endpoint load test"),
        CreateEndpointScenario("health", $"{baseUrl}/health", "Health endpoint load test"),
        CreateEndpointScenario("test-logs", $"{baseUrl}/test-logs", "Test logs endpoint load test"),
        CreateEndpointScenario("loki-test", $"{baseUrl}/loki-test", "Loki test endpoint load test"),
        CreateEndpointScenario("force-logs", $"{baseUrl}/force-logs", "Force logs endpoint load test"),
        CreateEndpointScenario("trace-test", $"{baseUrl}/trace-test", "Trace test endpoint load test")
    };
    
    try
    {
        var scenario = Scenario.Create("api_load_test", async context =>
        {
            using var httpClient = new HttpClient();
            
            // Randomly select an endpoint
            var random = new Random();
            var endpoint = scenarios[random.Next(scenarios.Length)];
            
            var response = await httpClient.GetAsync(endpoint.Url);
            
            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromSeconds(8)) // 10 requests per second for 8 seconds
        );
        
        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-reports")
            .Run();
            
        Log.Information("NBomber load tests completed successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error running NBomber load tests");
    }
}

static EndpointInfo CreateEndpointScenario(string name, string url, string description)
{
    return new EndpointInfo { Name = name, Url = url, Description = description };
}

record EndpointInfo(string Name, string Url, string Description);

await app.RunAsync();
