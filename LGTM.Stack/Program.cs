using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

// Auto-open browser to Swagger UI
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
});

await app.RunAsync();
