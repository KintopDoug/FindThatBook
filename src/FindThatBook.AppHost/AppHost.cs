var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.FindThatBook_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("https", url =>
    {
        url.DisplayText = "Swagger UI";
        url.Url = "/swagger";
    });

builder.Build().Run();
