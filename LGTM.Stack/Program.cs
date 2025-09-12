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
app.MapGet("/", async (HttpClient httpClient) =>
{
    Log.Information("Root endpoint accessed");

    // Call health endpoint
    try
    {
        var healthResponse = await httpClient.GetAsync("http://localhost:5207/health");
        Log.Information("Root endpoint called health: {StatusCode}", healthResponse.StatusCode);
    }
    catch (Exception ex)
    {
        Log.Warning("Root endpoint failed to call health: {Error}", ex.Message);
    }

    return "Hello World! Check your Grafana for logs and metrics.";
})
.WithName("GetRoot");

app.MapGet("/health", async (HttpClient httpClient) =>
{
    Log.Information("Health check requested");

    // Call test-logs endpoint randomly
    if (Random.Shared.Next(0, 2) == 1)
    {
        try
        {
            var logsResponse = await httpClient.GetAsync("http://localhost:5207/test-logs");
            Log.Information("Health endpoint called test-logs: {StatusCode}", logsResponse.StatusCode);
        }
        catch (Exception ex)
        {
            Log.Warning("Health endpoint failed to call test-logs: {Error}", ex.Message);
        }
    }

    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
})
.WithName("GetHealth");

app.MapGet("/error", () =>
{
    Log.Error("Error endpoint accessed - simulating an error");
    throw new InvalidOperationException("This is a test error for logging");
})
.WithName("GetError");

app.MapGet("/test-logs", async (HttpClient httpClient) =>
{
    Log.Information("Test log entry - Information level");
    Log.Warning("Test log entry - Warning level");
    Log.Error("Test log entry - Error level");

    // Call loki-test endpoint sometimes
    if (Random.Shared.Next(0, 3) == 1)
    {
        try
        {
            var lokiResponse = await httpClient.GetAsync("http://localhost:5207/loki-test");
            Log.Information("Test-logs endpoint called loki-test: {StatusCode}", lokiResponse.StatusCode);
        }
        catch (Exception ex)
        {
            Log.Warning("Test-logs endpoint failed to call loki-test: {Error}", ex.Message);
        }
    }

    return Results.Ok(new
    {
        message = "Test logs generated",
        timestamp = DateTime.UtcNow,
        levels = new[] { "Information", "Warning", "Error" }
    });
})
.WithName("TestLogs");

app.MapGet("/loki-test", async (HttpClient httpClient) =>
{
    Log.Information("Testing direct Loki connectivity from application");

    using var client = new HttpClient();
    try
    {
        var response = await client.GetAsync("http://localhost:3100/ready");
        var status = response.IsSuccessStatusCode ? "accessible" : "not accessible";

        Log.Information("Loki status check: {Status}", status);

        // Call force-logs endpoint sometimes
        if (Random.Shared.Next(0, 4) == 1)
        {
            try
            {
                var forceResponse = await httpClient.GetAsync("http://localhost:5207/force-logs");
                Log.Information("Loki-test endpoint called force-logs: {StatusCode}", forceResponse.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Warning("Loki-test endpoint failed to call force-logs: {Error}", ex.Message);
            }
        }

        return Results.Ok(new
        {
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

app.MapGet("/force-logs", async (HttpClient httpClient) =>
{
    LGTM.Stack.TestLogging.TestDirectLokiLogging();

    Log.Information("Forced log test completed at {Timestamp}", DateTime.UtcNow);
    Log.Warning("This is a warning from the main application");
    Log.Error("This is an error from the main application");

    // Call trace-test endpoint sometimes
    if (Random.Shared.Next(0, 3) == 1)
    {
        try
        {
            var traceResponse = await httpClient.GetAsync("http://localhost:5207/trace-test");
            Log.Information("Force-logs endpoint called trace-test: {StatusCode}", traceResponse.StatusCode);
        }
        catch (Exception ex)
        {
            Log.Warning("Force-logs endpoint failed to call trace-test: {Error}", ex.Message);
        }
    }

    return Results.Ok(new
    {
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

        // Call root endpoint sometimes to create a cycle
        if (Random.Shared.Next(0, 5) == 1)
        {
            try
            {
                var rootResponse = await httpClient.GetAsync("http://localhost:5207/");
                Log.Information("Trace-test endpoint called root: {StatusCode}", rootResponse.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Warning("Trace-test endpoint failed to call root: {Error}", ex.Message);
            }
        }

        return Results.Ok(new
        {
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
    Process.Start(new ProcessStartInfo("cmd", $"/c start {swaggerUrl}") { CreateNoWindow = true });
});

await app.RunAsync();
