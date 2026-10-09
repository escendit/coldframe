using System.Globalization;
using System.Net;
using System.Text.Json;
using Coldframe.Contracts.Alerts;
using Coldframe.Contracts.Sites;
using Coldframe.Server.Alerts;
using Coldframe.Server.IntegrationTests.Devices;

namespace Coldframe.Server.IntegrationTests.Edge;

/// <summary>
/// The Alerts read model at the REST surface (Story 6.2; AD-21): one test per Server row of the story's matrix,
/// from "Open and closed" to "No Alerts", and the rebuild of the read model from position 0.
/// </summary>
/// <remarks>
/// Alert events are seeded on fresh streams exactly as the Alert grain journals them. The Server of the AppHost
/// reads the system clock, so "closed 8 days ago" is a close time 8 days before now.
/// </remarks>
[Collection(IngestSuites.Name)]
public sealed class AlertsListTests(EdgeApiFixture edge) : IClassFixture<EdgeApiFixture>
{
    private const string Projector = AlertsProjector.ProjectorName;

    private const string Node = "3f2a9c0d1e4b5a67";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenAlertsComeNewestFirstThenTheOnesClosedInTheLastSevenDays()
    {
        var (siteId, member) = await SeedSiteAsync();
        var tomatoes = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var herbs = await edge.SeedLotAsync(siteId, "Herbs", Ct);
        var now = Now();

        var older = await OpenAsync(siteId, tomatoes, ThresholdSide.Low, "soil_moisture", now.AddHours(-3));
        var newer = await OpenAsync(siteId, herbs, ThresholdSide.High, "air_temperature", now.AddHours(-1));
        var recent = await OpenAsync(siteId, tomatoes, ThresholdSide.Low, "soil_moisture", now.AddDays(-3));
        await CloseAsync(recent, AlertCloseReason.Recovered, now.AddDays(-2));
        var old = await OpenAsync(siteId, tomatoes, ThresholdSide.Low, "soil_moisture", now.AddDays(-9));
        await CloseAsync(old, AlertCloseReason.Recovered, now.AddDays(-8));

        using var page = await GetAsync(member, siteId);
        var alerts = Alerts(page);

        Assert.Equal(["alerts", "openCount"], Names(page.RootElement));
        Assert.Equal([newer, older, recent], alerts.Select(Id));
        Assert.Equal(2, page.RootElement.GetProperty("openCount").GetInt32());

        Assert.Equal(["deviceId", "id", "kind", "lotId", "lotName", "openedAt", "quantity", "side"], Names(alerts[0]));
        Assert.Equal("threshold", alerts[0].GetProperty("kind").GetString());
        Assert.Equal("high", alerts[0].GetProperty("side").GetString());
        Assert.Equal("air_temperature", alerts[0].GetProperty("quantity").GetString());
        Assert.Equal(herbs, alerts[0].GetProperty("lotId").GetString());
        Assert.Equal("Herbs", alerts[0].GetProperty("lotName").GetString());
        Assert.Equal(Node, alerts[0].GetProperty("deviceId").GetString());
        Assert.Equal(Format(now.AddHours(-1)), alerts[0].GetProperty("openedAt").GetString());
        Assert.Equal("low", alerts[1].GetProperty("side").GetString());
        Assert.Equal("Tomatoes", alerts[1].GetProperty("lotName").GetString());
    }

    [Fact]
    public async Task ClosedAlertsComeNewestCloseFirstWhateverTheirOpenTime()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();

        // Opened first, closed last.
        var first = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddDays(-5));
        var second = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddDays(-4));
        await CloseAsync(second, AlertCloseReason.Recovered, now.AddDays(-3));
        await CloseAsync(first, AlertCloseReason.Recovered, now.AddDays(-1));

        using var page = await GetAsync(member, siteId);

        Assert.Equal([first, second], Alerts(page).Select(Id));
        Assert.Equal(0, page.RootElement.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task AnotherSitesAlertsAreNeverServed()
    {
        var (siteA, memberA) = await SeedSiteAsync();
        var (siteB, memberB) = await SeedSiteAsync();
        var lotA = await edge.SeedLotAsync(siteA, "Tomatoes", Ct);
        var lotB = await edge.SeedLotAsync(siteB, "Peppers", Ct);
        var mine = await OpenAsync(siteA, lotA, ThresholdSide.Low, "soil_moisture", Now().AddHours(-1));
        var theirs = await OpenAsync(siteB, lotB, ThresholdSide.Low, "soil_moisture", Now().AddHours(-1));

        using var pageA = await GetAsync(memberA, siteA);
        using var pageB = await GetAsync(memberB, siteB);

        Assert.Equal([mine], Alerts(pageA).Select(Id));
        Assert.Equal(1, pageA.RootElement.GetProperty("openCount").GetInt32());
        Assert.Equal([theirs], Alerts(pageB).Select(Id));

        // Not a Member of the other Site: refused as the matrix says.
        using var server = edge.CreateServerClient(memberA.AccessToken);
        using var refused = await server.GetAsync(new Uri($"/sites/{siteB}/alerts", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task PagesFollowTheCursorWithoutServingAnAlertTwice()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();

        // Two open at the same instant (the tie goes by ID), and a closed one: the second page crosses into Closed.
        var tied = new[]
        {
            await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddHours(-2)),
            await OpenAsync(siteId, lot, ThresholdSide.High, "relative_humidity", now.AddHours(-2)),
        }.Order(StringComparer.Ordinal).ToList();
        var closed = await OpenAsync(siteId, lot, ThresholdSide.Low, "air_temperature", now.AddDays(-2));
        await CloseAsync(closed, AlertCloseReason.Recovered, now.AddDays(-1));

        using var first = await GetAsync(member, siteId, "?limit=2");
        var cursor = first.RootElement.GetProperty("nextCursor").GetString()!;
        using var second = await GetAsync(member, siteId, $"?limit=2&cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(tied, Alerts(first).Select(Id));
        Assert.Equal([closed], Alerts(second).Select(Id));
        Assert.False(second.RootElement.TryGetProperty("nextCursor", out _));

        // The count is the Site's on every page.
        Assert.Equal(2, first.RootElement.GetProperty("openCount").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("openCount").GetInt32());

        // One Alert per page reaches every Alert once, and a full last page has no cursor.
        var seen = new List<string>();
        string? next = null;

        do
        {
            using var page = await GetAsync(member, siteId, next is null ? "?limit=1" : $"?limit=1&cursor={Uri.EscapeDataString(next)}");
            seen.AddRange(Alerts(page).Select(Id));
            next = page.RootElement.TryGetProperty("nextCursor", out var more) ? more.GetString() : null;
        }
        while (next is not null);

        Assert.Equal([.. tied, closed], seen);

        using var exact = await GetAsync(member, siteId, "?limit=3");
        Assert.Equal(3, Alerts(exact).Count);
        Assert.False(exact.RootElement.TryGetProperty("nextCursor", out _));
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=201")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=ten")]
    [InlineData("?limit=")]
    [InlineData("?limit=2&limit=3")]
    [InlineData("?cursor=")]
    [InlineData("?cursor=!!!")]
    [InlineData("?cursor=ZDE6MjAyNi0wMi0xMA")]
    [InlineData("?cursor=YTE6bzoxOm5vYm9keQ&cursor=YTE6bzoxOm5vYm9keQ")]
    public async Task ABadLimitOrCursorIsAValidationProblem(string query)
    {
        var (siteId, member) = await SeedSiteAsync();
        using var server = edge.CreateServerClient(member.AccessToken);

        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/alerts{query}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await EdgeApiFixture.ReadJsonAsync(response, Ct);
        Assert.Equal("urn:coldframe:problem:validation", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task TheLargestLimitIsAccepted()
    {
        var (siteId, member) = await SeedSiteAsync();

        using var page = await GetAsync(member, siteId, "?limit=200");

        Assert.Empty(Alerts(page));
    }

    [Fact]
    public async Task AClosedAlertCarriesItsCloseAndLeavesTheOpenCount()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();
        var alertId = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddHours(-2));

        using (var open = await GetAsync(member, siteId))
        {
            Assert.Equal(1, open.RootElement.GetProperty("openCount").GetInt32());
            Assert.False(Assert.Single(Alerts(open)).TryGetProperty("closedAt", out _));
        }

        await CloseAsync(alertId, AlertCloseReason.Recovered, now.AddHours(-1));

        using var page = await GetAsync(member, siteId);
        var alert = Assert.Single(Alerts(page));

        Assert.Equal(["closedAt", "deviceId", "id", "kind", "lotId", "lotName", "openedAt", "quantity", "reason", "side"], Names(alert));
        Assert.Equal(Format(now.AddHours(-1)), alert.GetProperty("closedAt").GetString());
        Assert.Equal("recovered", alert.GetProperty("reason").GetString());
        Assert.Equal(Format(now.AddHours(-2)), alert.GetProperty("openedAt").GetString());
        Assert.Equal(0, page.RootElement.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task AnEventAppliedTwiceLeavesOneUnchangedRow()
    {
        var (siteId, _) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();
        var open = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddHours(-2));
        var closed = await OpenAsync(siteId, lot, ThresholdSide.High, "soil_moisture", now.AddHours(-3));
        await CloseAsync(closed, AlertCloseReason.Recovered, now.AddHours(-1));
        var before = await RowsAsync(siteId);
        Assert.Equal(2, before.Count);

        // Every event once more through the projector itself, the open after its close included.
        await using (var connection = await edge.Database.OpenConnectionAsync(Ct))
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            var projector = new AlertsProjector();

            foreach (var alertId in new[] { open, closed })
            {
                foreach (var journalEvent in await edge.ReadStreamAsync($"alert/{alertId}", Ct))
                {
                    await projector.ApplyAsync(journalEvent, transaction, Ct);
                }
            }

            await transaction.CommitAsync(Ct);
        }

        Assert.Equal(before, await RowsAsync(siteId));
    }

    [Fact]
    public async Task ASiteWithoutAlertsAnswersAnEmptyListAndZero()
    {
        var (siteId, member) = await SeedSiteAsync();

        using var page = await GetAsync(member, siteId);

        Assert.Equal(["alerts", "openCount"], Names(page.RootElement));
        Assert.Empty(Alerts(page));
        Assert.Equal(0, page.RootElement.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task ARenamedLotNamesItsAlertsWithTheNewName()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct, new Coldframe.Contracts.Lots.LotRenamed("Cherry tomatoes"));
        await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", Now().AddHours(-1));

        using var page = await GetAsync(member, siteId);

        Assert.Equal("Cherry tomatoes", Assert.Single(Alerts(page)).GetProperty("lotName").GetString());
    }

    [Fact]
    public async Task AnAlertClosedExactlyAtTheCutOffIsListedAndOneClosedJustBeforeItIsNot()
    {
        var (siteId, _) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();
        var cutOff = now - AlertsReadModel.ClosedRetention;

        var atCutOff = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", cutOff.AddHours(-1));
        await CloseAsync(atCutOff, AlertCloseReason.Recovered, cutOff);
        var justBefore = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", cutOff.AddHours(-2));

        // PostgreSQL keeps microseconds: one microsecond is the smallest step before the cut-off.
        await CloseAsync(justBefore, AlertCloseReason.Recovered, cutOff.AddTicks(-10));

        // The read model itself, with the cut-off the handler would derive from a clock that reads "now".
        var page = await new AlertsReadModel(edge.Database).ListAlertsAsync(siteId, cutOff, null, 50, Ct);

        Assert.Equal([atCutOff], page.Alerts.Select(alert => alert.AlertId.ToString()));
    }

    [Fact]
    public async Task TheSitesAcknowledgementLeavesTheRowUnchanged()
    {
        var (siteId, _) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();
        var alertId = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddHours(-2));
        var before = await RowsAsync(siteId);
        Assert.Single(before);

        var last = await edge.AppendAsync($"alert/{alertId}", 1, [new AlertSiteNotified(AlertLifecycle.Open, now.AddHours(-1))], Ct);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        Assert.Equal(before, await RowsAsync(siteId));
    }

    [Fact]
    public async Task ARemovedLotStillNamesItsAlertAndCountsAsOpen()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct, new Coldframe.Contracts.Lots.LotRemoved());
        var alertId = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", Now().AddHours(-1));

        using var page = await GetAsync(member, siteId);
        var alert = Assert.Single(Alerts(page));

        Assert.Equal(alertId, Id(alert));
        Assert.Equal("Tomatoes", alert.GetProperty("lotName").GetString());
        Assert.Equal(1, page.RootElement.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task AnAlertWhoseLotHasNoRowIsLeftOutOfTheListAndTheCount()
    {
        var (siteId, member) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var listed = await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", Now().AddHours(-1));

        // A Lot the lots read model does not hold, as while it is being rebuilt: one open, one closed.
        var missing = Guid.CreateVersion7().ToString();
        await OpenAsync(siteId, missing, ThresholdSide.Low, "soil_moisture", Now().AddHours(-2));
        var closed = await OpenAsync(siteId, missing, ThresholdSide.High, "soil_moisture", Now().AddHours(-4));
        await CloseAsync(closed, AlertCloseReason.Recovered, Now().AddHours(-3));
        Assert.Equal(3, (await RowsAsync(siteId)).Count);

        using var page = await GetAsync(member, siteId);

        Assert.Equal([listed], Alerts(page).Select(Id));
        Assert.Equal(1, page.RootElement.GetProperty("openCount").GetInt32());
    }

    [Fact]
    public async Task RebuildingTheAlertsReadModelFromPositionZeroGivesTheSameRows()
    {
        var (siteId, _) = await SeedSiteAsync();
        var lot = await edge.SeedLotAsync(siteId, "Tomatoes", Ct);
        var now = Now();
        await OpenAsync(siteId, lot, ThresholdSide.Low, "soil_moisture", now.AddHours(-2));
        var closed = await OpenAsync(siteId, lot, ThresholdSide.High, "air_temperature", now.AddHours(-3));
        var last = await CloseAsync(closed, AlertCloseReason.Recovered, now.AddHours(-1));
        var before = await RowsAsync(siteId);
        Assert.Equal(2, before.Count);

        // The documented rebuild: the rows of the projector's table and its checkpoint, then from position 0.
        await using (var connection = await edge.Database.OpenConnectionAsync(Ct))
        await using (var transaction = await connection.BeginTransactionAsync(Ct))
        {
            await using var command = new Npgsql.NpgsqlCommand(
                "DELETE FROM projection_checkpoints WHERE projector = 'alerts'; DELETE FROM alerts;", connection, transaction);
            await command.ExecuteNonQueryAsync(Ct);
            await transaction.CommitAsync(Ct);
        }

        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        Assert.Equal(before, await RowsAsync(siteId));
    }

    private static DateTimeOffset Now()
    {
        // PostgreSQL keeps microseconds; whole milliseconds read back as they were written.
        var now = TimeProvider.System.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static string Format(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static List<string> Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static List<JsonElement> Alerts(JsonDocument page) => [.. page.RootElement.GetProperty("alerts").EnumerateArray()];

    private static string Id(JsonElement alert) => alert.GetProperty("id").GetString()!;

    private async Task<JsonDocument> GetAsync(TestUser user, string siteId, string query = "")
    {
        using var server = edge.CreateServerClient(user.AccessToken);
        using var response = await server.GetAsync(new Uri($"/sites/{siteId}/alerts{query}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await EdgeApiFixture.ReadJsonAsync(response, Ct);
    }

    // An open Threshold Alert, as the Alert grain journals it.
    private async Task<string> OpenAsync(string siteId, string lotId, ThresholdSide side, string quantity, DateTimeOffset openedAt)
    {
        var alertId = Guid.NewGuid().ToString();
        var last = await edge.AppendAsync(
            $"alert/{alertId}",
            [new AlertOpened(AlertKind.Threshold, side, siteId, lotId, Guid.NewGuid(), Node, quantity, 1, openedAt)],
            Ct);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        return alertId;
    }

    private async Task<long> CloseAsync(string alertId, AlertCloseReason reason, DateTimeOffset closedAt)
    {
        var last = await edge.AppendAsync($"alert/{alertId}", 1, [new AlertClosed(reason, closedAt)], Ct);
        await edge.WaitForProjectionCheckpointAsync(Projector, last);

        return last;
    }

    // The Site's rows of the read model, whole.
    private async Task<List<string>> RowsAsync(string siteId)
    {
        var rows = new List<string>();
        await using var command = edge.Database.CreateCommand(
            "SELECT to_jsonb(a)::text FROM alerts a WHERE a.site_id = @site_id ORDER BY a.alert_id");
        command.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    // A Site with a Member, seeded as events.
    private async Task<(string SiteId, TestUser Member)> SeedSiteAsync()
    {
        var owner = await edge.CreateUserAsync("alerts-owner", Ct);
        var member = await edge.CreateUserAsync("alerts-member", Ct);
        var siteId = Guid.CreateVersion7().ToString();

        var last = await edge.AppendAsync(
            $"site/{siteId}",
            [
                new SiteCreated("Alerts", owner.UserId),
                new MembershipGranted(owner.UserId, SiteRole.Owner),
                new MembershipGranted(member.UserId, SiteRole.Member),
            ],
            Ct);
        await edge.WaitForIdentityCheckpointAsync(last);

        return (siteId, member);
    }
}
