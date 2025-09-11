using Serilog;
using OpenTelemetry.Metrics;
using System.Diagnostics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter();
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
