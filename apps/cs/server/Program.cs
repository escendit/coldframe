using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.Edge;
using Coldframe.Server.Hosting;
using Coldframe.Server.Identity;
using Coldframe.Server.Lots;
using Escendit.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddServiceDefaults()
    .AddJournal()
    .AddSiteIdentity()
    .AddLots()
    .AddDevices()
    .AddAlerts()
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
