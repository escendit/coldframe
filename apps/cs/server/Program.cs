using Coldframe.Server.Hosting;
using Escendit.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddServiceDefaults()
    .AddSilo()
    .AddHealthCheckDefaults(checks => checks.AddCheck<SiloHealthCheck>(SiloHealthCheck.Name, tags: ["ready"]));

var app = builder.Build();

app.UseExceptionHandling();
app.UseHealthCheckDefaults();

await app.RunAsync().ConfigureAwait(false);
