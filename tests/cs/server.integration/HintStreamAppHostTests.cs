using Aspire.Hosting.Testing;
using Coldframe.Server.IntegrationTests.Journal;
using Npgsql;

namespace Coldframe.Server.IntegrationTests;

/// <summary>
/// The Server's production hint wiring: its outbox dispatcher publishes to the NATS stream provider and
/// marks the outbox row dispatched (AD-5, AD-21).
/// </summary>
public sealed class HintStreamAppHostTests(AppHostFixture fixture)
{
    private const string ServerResource = "server";
    private const string DatabaseResource = "coldframe";

    [Fact]
    public async Task TheServerDispatchesAnOutboxRowThroughNats()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(ServerResource, timeout.Token);

        var connectionString = await fixture.App.GetConnectionStringAsync(DatabaseResource, timeout.Token)
            ?? throw new InvalidOperationException($"The AppHost gives '{DatabaseResource}' no connection string.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        // The Server's projectors read every row of this journal, so the probe must be an event the Server
        // can read: a registered alias on a stream that no projector projects.
        long position;
        await using (var insert = dataSource.CreateCommand(
            """
            WITH appended AS (
                INSERT INTO journal_events (stream_id, version, type_alias, schema_version, payload, recorded_at)
                VALUES (@stream_id, 1, 'site.created', 1, '{"name":"hint","createdBy":"hint-probe"}'::jsonb, now())
                RETURNING position
            )
            INSERT INTO journal_outbox (position) SELECT position FROM appended RETURNING position
            """))
        {
            insert.Parameters.AddWithValue("stream_id", $"hint-probe/{Guid.NewGuid():N}");
            position = (long)(await insert.ExecuteScalarAsync(timeout.Token))!;
        }

        // The row did not come through the Server's store, so only the dispatcher's poll finds it.
        await JournalWait.UntilAsync(
            async () =>
            {
                await using var select = dataSource.CreateCommand(
                    "SELECT dispatched_at IS NOT NULL FROM journal_outbox WHERE position = @position");
                select.Parameters.AddWithValue("position", position);
                return (bool)(await select.ExecuteScalarAsync(timeout.Token))!;
            },
            $"the Server dispatches outbox position {position}",
            TimeSpan.FromMinutes(1));
    }
}
