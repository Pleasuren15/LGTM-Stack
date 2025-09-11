using Serilog;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
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
app.UseHttpsRedirection();

// Map Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// Add some sample endpoints to generate logs and metrics
app.MapGet("/", () => 
{
    Log.Information("Root endpoint accessed");
    return "Hello World! Check your Grafana for logs and metrics.";
});

app.MapGet("/health", () => 
{
    Log.Information("Health check requested");
    return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
});

app.MapGet("/error", () => 
{
    Log.Error("Error endpoint accessed - simulating an error");
    throw new InvalidOperationException("This is a test error for logging");
});

await app.RunAsync();
