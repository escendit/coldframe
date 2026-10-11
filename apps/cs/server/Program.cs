using Coldframe.Server.Alerts;
using Coldframe.Server.Devices;
using Coldframe.Server.Edge;
using Coldframe.Server.Hosting;
using Coldframe.Server.Identity;
using Coldframe.Server.Live;
using Coldframe.Server.Lots;
using Coldframe.Server.Notifications;
using Coldframe.Server.Notifications.Push;
using Coldframe.Server.Testing;
using Escendit.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// The manual clock comes first, so that it is the TimeProvider everything else finds. It is registered only
// in a Development host that sets Testing:ManualClock; any other host runs on the system clock.
builder
    .AddTestingClock()
    .AddServiceDefaults()
    .AddJournal()
    .AddSiteIdentity()
    .AddLots()
    .AddDevices()
    .AddAlerts()
    .AddNotifications()
    .AddPushChannels()
    .AddLive()
    .AddKeycloakEventPipeline()
    .AddSilo()
    .AddEdgeApi()
    .AddHealthCheckDefaults(checks => checks.AddCheck<SiloHealthCheck>(SiloHealthCheck.Name, tags: ["ready"]));

var app = builder.Build();

app.UseExceptionHandling();
app.UseEdgeApi();
app.UseHealthCheckDefaults();
app.MapEdgeApi();
app.MapLiveHub();
app.MapTestingClock();

await app.RunAsync().ConfigureAwait(false);
