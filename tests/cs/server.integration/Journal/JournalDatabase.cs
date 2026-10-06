using Aspire.Hosting.Testing;
using Coldframe.Migrations;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coldframe.Server.IntegrationTests.Journal;

/// <summary>
/// A fresh database on the AppHost's PostgreSQL for one test class, migrated by the same runner the
/// migration job uses. It is dropped when the class finishes.
/// </summary>
public sealed class JournalDatabase(AppHostFixture fixture) : IAsyncLifetime
{
    private const string PostgresResource = "postgres";
    private const string ServerDatabaseResource = "coldframe";

    private string? _connectionString;
    private string? _administrationConnectionString;
    private string? _name;
    private NpgsqlDataSource? _dataSource;

    /// <summary>
    /// The connection string of this test class's database.
    /// </summary>
    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("The database has not been created.");

    /// <summary>
    /// A data source for assertions and test setup.
    /// </summary>
    public NpgsqlDataSource DataSource =>
        _dataSource ?? throw new InvalidOperationException("The database has not been created.");

    public ValueTask InitializeAsync() => InitializeAsync(upTo: null);

    /// <summary>
    /// Creates the database and migrates it: to the end, or up to and including the migration
    /// <paramref name="upTo"/>, as a database from before the later migrations.
    /// </summary>
    public async ValueTask InitializeAsync(long? upTo)
    {
        using var timeout = new CancellationTokenSource(AppHostFixture.ResourceTimeout);

        await fixture.WaitForHealthyAsync(PostgresResource, timeout.Token);
        await fixture.WaitForHealthyAsync(ServerDatabaseResource, timeout.Token);

        var serverConnectionString = await fixture.App.GetConnectionStringAsync(ServerDatabaseResource, timeout.Token)
            ?? throw new InvalidOperationException($"The AppHost gives '{ServerDatabaseResource}' no connection string.");

        _name = $"journal_{Guid.NewGuid():N}";
        _administrationConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;
        _connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = _name }.ConnectionString;

        await using (var administration = new NpgsqlConnection(_administrationConnectionString))
        {
            await administration.OpenAsync(timeout.Token);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_name}\"", administration);
            await create.ExecuteNonQueryAsync(timeout.Token);
        }

        Migrate(_connectionString, upTo);

        _dataSource = NpgsqlDataSource.Create(_connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_administrationConnectionString is null || _name is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using var administration = new NpgsqlConnection(_administrationConnectionString);
        await administration.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", administration);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Runs the migration set against a database, exactly as the migration job does.
    /// </summary>
    /// <returns>Whether any migration was pending before the run.</returns>
    public static bool Migrate(string connectionString) => Migrate(connectionString, upTo: null);

    /// <summary>
    /// Runs the migration set against a database up to and including the migration <paramref name="upTo"/>,
    /// or to the end when it is <see langword="null"/>.
    /// </summary>
    /// <returns>Whether any migration was pending before the run.</returns>
    public static bool Migrate(string connectionString, long? upTo)
    {
        using var services = new ServiceCollection()
            .AddColdframeMigrations(connectionString)
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        var pending = runner.HasMigrationsToApplyUp();

        if (upTo is { } version)
        {
            runner.MigrateUp(version);
        }
        else
        {
            runner.MigrateUp();
        }

        return pending;
    }

    /// <summary>
    /// Runs one SQL statement and returns its first column of the first row.
    /// </summary>
    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is null or DBNull ? default : (T)result;
    }

    /// <summary>
    /// Runs one SQL statement.
    /// </summary>
    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
