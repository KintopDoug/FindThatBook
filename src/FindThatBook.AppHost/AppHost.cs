var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.FindThatBook_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Swagger UI";
        url.Url = "/swagger";
    });

// Forward the Gemini key if it was configured on the AppHost, which is where an Aspire
// solution's configuration naturally lives -- and where Visual Studio's "Manage User Secrets"
// puts it when the AppHost is the startup project. The API also reads its own user secrets,
// so either location works and neither is required: with no key anywhere, both AI stages fall
// back to deterministic rules and say so.
//
// Deliberately not builder.AddParameter(secret: true), which would prompt or fail when the
// key is absent and cost the app its run-without-a-key behaviour.
var geminiApiKey = builder.Configuration["Gemini:ApiKey"];

if (!string.IsNullOrWhiteSpace(geminiApiKey))
{
    api.WithEnvironment("Gemini__ApiKey", geminiApiKey);
}

// The Vite dev server. WithReference injects the API's discovered endpoints as
// services__api__{scheme}__0 environment variables, which vite.config.ts reads to point its
// /api proxy at the right host and port. Nothing about the API's address is hard-coded in
// the web app, and the browser stays same-origin so no CORS policy is needed.
builder.AddNpmApp("web", "../FindThatBook.Web", "dev")
    // Installs dependencies before starting, so a fresh clone runs with one command. npm
    // skips the work when node_modules is already up to date, making this cheap on rerun.
    .WithNpmPackageInstallation()
    .WithReference(api)
    .WaitFor(api)
    // Aspire allocates the port and passes it as PORT; Vite binds to it.
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
