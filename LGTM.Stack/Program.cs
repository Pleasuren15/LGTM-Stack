using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Create ActivitySource for custom traces and Meter for custom metrics
var activitySource = new ActivitySource("LGTM.Stack");
var meter = new Meter("LGTM.Stack");

// Custom metrics
var simulationCounter = meter.CreateCounter<int>("lgtm_simulations_total", "count", "Total number of simulations run");
var operationCounter = meter.CreateCounter<int>("lgtm_operations_total", "count", "Total number of operations performed");
var errorCounter = meter.CreateCounter<int>("lgtm_errors_total", "count", "Total number of errors encountered");
var simulationDurationHistogram = meter.CreateHistogram<double>("lgtm_simulation_duration_seconds", "seconds", "Duration of simulations");

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("LGTM.Stack") // Add our custom meter
            .AddPrometheusExporter(); // Keep for /metrics endpoint
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

// Simulation endpoint - the only one visible in Swagger
app.MapGet("/simulate", async (HttpClient httpClient) =>
{
    Log.Information("Starting LGTM simulation");
    
    var simulationStart = DateTime.UtcNow;
    var simulationResults = new List<string>();
    var random = new Random();
    
    // Increment simulation counter
    simulationCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "simulate"));
    
    try
    {
        // Simulate root endpoint
        Log.Information("Root endpoint accessed");
        simulationResults.Add("✅ Root endpoint simulated");
        operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "root"));
        
        // Simulate health check with cascading calls
        Log.Information("Health check requested");
        simulationResults.Add("✅ Health check simulated");
        operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "health"));
        
        if (random.Next(0, 2) == 1)
        {
            // Simulate test logs
            Log.Information("Test log entry - Information level");
            Log.Warning("Test log entry - Warning level"); 
            Log.Error("Test log entry - Error level");
            simulationResults.Add("✅ Test logs generated");
            operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "test-logs"));
            
            if (random.Next(0, 3) == 1)
            {
                // Simulate Loki connectivity test
                Log.Information("Testing direct Loki connectivity from application");
                using var client = new HttpClient();
                try
                {
                    var response = await client.GetAsync("http://localhost:3100/ready");
                    var status = response.IsSuccessStatusCode ? "accessible" : "not accessible";
                    Log.Information("Loki status check: {Status}", status);
                    simulationResults.Add($"✅ Loki test completed - Status: {status}");
                    operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "loki-test"));
                    
                    if (random.Next(0, 4) == 1)
                    {
                        // Simulate force logs
                        LGTM.Stack.TestLogging.TestDirectLokiLogging();
                        Log.Information("Forced log test completed at {Timestamp}", DateTime.UtcNow);
                        Log.Warning("This is a warning from the main application");
                        Log.Error("This is an error from the main application");
                        simulationResults.Add("✅ Force logs completed");
                        operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "force-logs"));
                        
                        if (random.Next(0, 3) == 1)
                        {
                            // Simulate trace test
                            using var activity = activitySource.StartActivity("SimulatedTraceTest");
                            activity?.SetTag("test.type", "simulation");
                            activity?.SetTag("endpoint", "simulate");
                            
                            Log.Information("Starting trace test with activity ID: {ActivityId}", activity?.Id);
                            
                            var traceResponse = await httpClient.GetAsync("https://api.github.com/zen");
                            var content = await traceResponse.Content.ReadAsStringAsync();
                            
                            activity?.SetTag("http.response_size", content.Length);
                            activity?.SetStatus(ActivityStatusCode.Ok);
                            
                            Log.Information("Trace test completed successfully");
                            simulationResults.Add($"✅ Trace test completed - Trace ID: {activity?.TraceId}");
                            operationCounter.Add(1, new KeyValuePair<string, object?>("operation", "trace-test"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to connect to Loki during simulation");
                    simulationResults.Add("❌ Loki test failed");
                    errorCounter.Add(1, new KeyValuePair<string, object?>("operation", "loki-test"));
                }
            }
        }
        
        // Occasionally simulate an error
        if (random.Next(0, 10) == 1)
        {
            Log.Error("Simulated error during LGTM simulation");
            simulationResults.Add("⚠️ Simulated error generated");
            errorCounter.Add(1, new KeyValuePair<string, object?>("operation", "simulation"));
        }
        
        Log.Information("LGTM simulation completed with {ResultCount} operations", simulationResults.Count);
        
        // Record simulation duration
        var simulationDuration = (DateTime.UtcNow - simulationStart).TotalSeconds;
        simulationDurationHistogram.Record(simulationDuration);
        
        return Results.Ok(new
        {
            message = "LGTM Stack simulation completed successfully",
            timestamp = DateTime.UtcNow,
            operationsPerformed = simulationResults,
            totalOperations = simulationResults.Count,
            instruction = "Check your Grafana dashboards for logs, metrics, and traces generated by this simulation"
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error during LGTM simulation");
        simulationResults.Add($"❌ Simulation error: {ex.Message}");
        errorCounter.Add(1, new KeyValuePair<string, object?>("operation", "simulation"));
        
        // Record simulation duration even on error
        var simulationDuration = (DateTime.UtcNow - simulationStart).TotalSeconds;
        simulationDurationHistogram.Record(simulationDuration);
        
        return Results.Ok(new
        {
            message = "LGTM Stack simulation completed with errors",
            timestamp = DateTime.UtcNow,
            operationsPerformed = simulationResults,
            error = ex.Message
        });
    }
})
.WithName("RunSimulation")
.WithSummary("Run LGTM Stack Simulation")
.WithDescription("Simulates various operations to generate logs, metrics, and traces for testing your LGTM monitoring stack");

// Hidden endpoints - not visible in Swagger
app.MapGet("/", () => "Hello World! Use /simulate endpoint for testing.")
    .ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .ExcludeFromDescription();

app.MapGet("/error", () => 
{
    Log.Error("Error endpoint accessed - simulating an error");
    throw new InvalidOperationException("This is a test error for logging");
})
.ExcludeFromDescription();

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
.ExcludeFromDescription();

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
.ExcludeFromDescription();

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
.ExcludeFromDescription();

app.MapGet("/trace-test", async (HttpClient httpClient) => 
{
    using var activity = activitySource.StartActivity("TraceTest");
    activity?.SetTag("test.type", "manual");
    activity?.SetTag("endpoint", "trace-test");
    
    Log.Information("Starting trace test with activity ID: {ActivityId}", activity?.Id);
    
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
.ExcludeFromDescription();

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
