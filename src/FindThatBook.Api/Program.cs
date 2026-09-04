var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: service discovery, resilience, health checks, and the
// OpenTelemetry logger/meter/tracer providers. The OTLP exporter configured here is
// what forwards ILogger<T> output to the Aspire dashboard's structured logs.
builder.AddServiceDefaults();

builder.Services.AddControllers();

// Minimal OpenAPI document at /openapi/v1.json (Microsoft.AspNetCore.OpenApi).
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Find That Book API";
        document.Info.Description =
            "Resolves messy plain-text book queries into ranked Open Library candidates.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swashbuckle's UI only — the document itself comes from AddOpenApi/MapOpenApi above.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Find That Book API v1");
        options.DocumentTitle = "Find That Book API";
    });
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
