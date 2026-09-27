using Coldframe.Server.Journal;
using Coldframe.Server.Tests.Samples;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// An event row and its outbox row commit in one transaction (AD-21).
/// </summary>
public sealed class OutboxAtomicityTests(JournalDatabase database) : IClassFixture<JournalDatabase>
{
    [Fact]
    public async Task WhenTheOutboxInsertFailsNeitherRowExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var streamId = SampleGrain.StreamIdOf($"atomic-{Guid.NewGuid():N}");

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddJournal(database.ConnectionString, options => options.EventAssemblies.Add(typeof(SampleCreated).Assembly))
            .BuildServiceProvider();
        var store = services.GetRequiredService<JournalStore>();

        await database.ExecuteAsync(
            """
            CREATE FUNCTION reject_outbox() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'outbox insert rejected by the test'; END $$;
            CREATE TRIGGER reject_outbox BEFORE INSERT ON journal_outbox
            FOR EACH ROW EXECUTE FUNCTION reject_outbox();
            """);

        try
        {
            await Assert.ThrowsAsync<PostgresException>(
                () => store.AppendAsync(streamId, 0, [new SampleCreated("never")], cancellationToken));
        }
        finally
        {
            await database.ExecuteAsync("DROP TRIGGER reject_outbox ON journal_outbox; DROP FUNCTION reject_outbox();");
        }

        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM journal_events WHERE stream_id = @stream_id", ("stream_id", streamId)));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM journal_outbox"));

        // Without the trigger the same append succeeds and writes both rows.
        Assert.True(await store.AppendAsync(streamId, 0, [new SampleCreated("now")], cancellationToken));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM journal_events e JOIN journal_outbox o ON o.position = e.position WHERE e.stream_id = @stream_id",
            ("stream_id", streamId)));
    }
}
