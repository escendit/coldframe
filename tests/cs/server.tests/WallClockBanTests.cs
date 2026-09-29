using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Coldframe.Server.Tests;

/// <summary>
/// Scans the compiled Server assemblies for wall-clock reads (AD-6). The analyzer (RS0030) catches
/// them at compile time; this catches whatever reaches the IL by another route.
/// </summary>
public sealed class WallClockBanTests
{
    private static readonly HashSet<string> BannedGetters = new(StringComparer.Ordinal)
    {
        "System.DateTime.get_Now",
        "System.DateTime.get_UtcNow",
        "System.DateTime.get_Today",
        "System.DateTimeOffset.get_Now",
        "System.DateTimeOffset.get_UtcNow",
    };

    public static TheoryData<string> ScannedAssemblies =>
    [
        typeof(Coldframe.Server.Journal.JournalStore).Assembly.GetName().Name!,
        typeof(Coldframe.Contracts.Events.EventTypeAttribute).Assembly.GetName().Name!,
        typeof(Coldframe.Migrations.MigrationRunnerRegistration).Assembly.GetName().Name!,
    ];

    [Theory]
    [MemberData(nameof(ScannedAssemblies))]
    public void TheAssemblyReferencesNoWallClockGetter(string assemblyName)
    {
        var found = FindBannedGetters(Assembly.Load(assemblyName));

        Assert.True(found.Count == 0, $"{assemblyName} reads the wall clock through {string.Join(", ", found)}.");
    }

    [Fact]
    public void TheScanFindsAWallClockRead()
    {
        // This test assembly reads DateTime.UtcNow in WallClockReader, so the scan must report it.
        Assert.Contains("System.DateTime.get_UtcNow", FindBannedGetters(typeof(WallClockReader).Assembly));
    }

    private static List<string> FindBannedGetters(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        var found = new List<string>();

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var name = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}.{metadata.GetString(member.Name)}";

            if (BannedGetters.Contains(name))
            {
                found.Add(name);
            }
        }

        return found;
    }

    private static class WallClockReader
    {
        public static DateTime Read() => DateTime.UtcNow;
    }
}
