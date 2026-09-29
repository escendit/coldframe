using Coldframe.Server.Journal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coldframe.Server.Hosting;

/// <summary>
/// The Server's journal and time registrations.
/// </summary>
public static class JournalHostingExtensions
{
    /// <summary>
    /// The connection string of the Coldframe database, set by the AppHost.
    /// </summary>
    public const string ConnectionStringName = "coldframe";

    /// <summary>
    /// Registers <see cref="TimeProvider.System"/> unless a host registered another provider first,
    /// and the event journal on the <c>coldframe</c> database. Nothing here runs DDL; the migration
    /// job owns the schema (AD-22).
    /// </summary>
    public static TBuilder AddJournal<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddJournal(GetConnectionString(builder.Configuration));

        return builder;
    }

    /// <summary>
    /// Returns the connection string of the Coldframe database, or fails when it is missing.
    /// </summary>
    public static string GetConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException($"No connection string '{ConnectionStringName}' is configured.")
            : connectionString;
    }
}
