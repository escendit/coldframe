using Coldframe.Server.Edge;
using Coldframe.Server.Hosting;
using Coldframe.Server.Identity;
using Escendit.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddServiceDefaults()
    .AddJournal()
    .AddSiteIdentity()
    .AddKeycloakEventPipeline()
    .AddSilo()
    .AddEdgeApi()
    .AddHealthCheckDefaults(checks => checks.AddCheck<SiloHealthCheck>(SiloHealthCheck.Name, tags: ["ready"]));

var app = builder.Build();

app.UseExceptionHandling();
app.UseEdgeApi();
app.UseHealthCheckDefaults();
app.MapEdgeApi();

await app.RunAsync().ConfigureAwait(false);
