using System.Reflection;
using Coldframe.Migrations;
using FluentMigrator;

namespace Coldframe.Server.Tests.Migrations;

/// <summary>
/// The one migration set (AD-22): forward-only, run by the migration job, never by the Server.
/// </summary>
public sealed class MigrationSetTests
{
    private static readonly Assembly MigrationsAssembly = typeof(MigrationRunnerRegistration).Assembly;

    [Fact]
    public void EveryColdframeMigrationIsForwardOnly()
    {
        var migrations = FindMigrations(MigrationsAssembly);

        Assert.NotEmpty(migrations);
        Assert.All(migrations, migration => Assert.True(
            migration.Type.IsSubclassOf(typeof(ForwardOnlyMigration)),
            $"{migration.Type.Name} does not derive from ForwardOnlyMigration."));
    }

    [Fact]
    public void ColdframeMigrationVersionsDoNotCollideWithTheClusterPackage()
    {
        var clusterAssembly = typeof(Microsoft.Extensions.DependencyInjection.OrleansVersionTableMetadata).Assembly;

        var cluster = FindMigrations(clusterAssembly).Select(migration => migration.Version).ToHashSet();
        var coldframe = FindMigrations(MigrationsAssembly).Select(migration => migration.Version).ToList();

        Assert.NotEmpty(cluster);
        Assert.Equal(coldframe.Count, coldframe.Distinct().Count());
        Assert.DoesNotContain(coldframe, cluster.Contains);
    }

    [Fact]
    public void TheServerReferencesNoFluentMigratorAssembly()
    {
        var server = typeof(Coldframe.Server.Journal.JournalStore).Assembly;

        var references = server.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name.StartsWith("FluentMigrator", StringComparison.Ordinal));
    }

    private static List<(Type Type, long Version)> FindMigrations(Assembly assembly) =>
        assembly.GetTypes()
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<MigrationAttribute>()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => (candidate.Type, candidate.Attribute!.Version))
            .ToList();
}
