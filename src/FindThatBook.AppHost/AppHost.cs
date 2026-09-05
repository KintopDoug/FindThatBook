var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.FindThatBook_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Swagger UI";
        url.Url = "/swagger";
    });

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
