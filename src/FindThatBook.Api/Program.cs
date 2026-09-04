using FindThatBook.Api.Configuration;
using FindThatBook.Api.Infrastructure;
using FindThatBook.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: service discovery, resilience, health checks, and the
// OpenTelemetry logger/meter/tracer providers. The OTLP exporter configured here is
// what forwards ILogger<T> output to the Aspire dashboard's structured logs.
builder.AddServiceDefaults();

// Bind the "Search" section and fail fast at startup on a bad value rather than on the
// first request that happens to hit the validation path.
builder.Services.AddOptions<SearchOptions>()
    .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Turns unhandled exceptions into ProblemDetails. AddProblemDetails supplies the writer
// that GlobalExceptionHandler uses, and also gives non-exception responses (404, 415, ...)
// a JSON body instead of an empty one.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Bind the "Gemini" section. The API key is intentionally not required: without one the
// app still runs and query extraction uses the deterministic parser.
builder.Services.AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection(GeminiOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

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

builder.Services.AddScoped<IBookSearchService, BookSearchService>();
builder.Services.AddTransient<IQueryValidationService, QueryValidationService>();
builder.Services.AddTransient<IFallbackQueryExtractor, FallbackQueryExtractor>();
builder.Services.AddScoped<IQueryExtractionService, QueryExtractionService>();

// Typed client. AddServiceDefaults already applies the standard resilience handler to every
// HttpClient, so retries and per-attempt timeouts come along for free.
builder.Services.AddHttpClient<ILlmQueryExtractor, GeminiQueryExtractor>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

var app = builder.Build();

// First in the pipeline, so it also catches failures thrown by later middleware.
app.UseExceptionHandler();

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
