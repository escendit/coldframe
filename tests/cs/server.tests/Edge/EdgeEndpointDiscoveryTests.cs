using System.Text.Json;
using Coldframe.Server.Edge;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// Every Edge API endpoint declares exactly one access rule, and the mapped endpoints are exactly the
/// operations of the contract, with the same rules (AD-4, AD-10, AD-24). An operation marked
/// <c>x-coldframe-planned</c> is in the contract ahead of the Server and must not be mapped yet; the story
/// that serves it removes the mark.
/// </summary>
public sealed class EdgeEndpointDiscoveryTests
{
    private const string MinimumRoleExtension = "x-coldframe-minimum-role";

    private const string PlannedExtension = "x-coldframe-planned";

    private static readonly string ContractPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "coldframe.openapi.json");

    private static readonly string[] OperationMethods = ["get", "put", "post", "delete", "patch", "head", "options", "trace"];

    [Fact]
    public void TheEdgeApiMapsEndpoints()
    {
        Assert.NotEmpty(EdgeEndpointCatalog.Describe());
    }

    [Fact]
    public void EveryEndpointDeclaresExactlyOneAccessRule()
    {
        var offenders = EdgeEndpointCatalog.Describe()
            .Where(operation => operation.Rules.Count != 1)
            .Select(operation => $"{operation.Key} ({operation.Rules.Count} rules)")
            .ToList();

        Assert.True(offenders.Count == 0, $"Endpoints without exactly one access rule: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void EverySiteScopedEndpointNamesItsSiteInTheRoute()
    {
        var offenders = EdgeEndpointCatalog.Describe()
            .Where(operation => operation.Rule.MinimumRole is not null
                && !operation.Route.Contains($"{{{SiteAccessHandler.SiteIdRouteValue}}}", StringComparison.Ordinal))
            .Select(operation => operation.Key)
            .ToList();

        Assert.True(offenders.Count == 0, $"Site-scoped endpoints without {{siteId}}: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void TheMappedEndpointsAndTheirRulesAreExactlyTheContractOperations()
    {
        var mapped = EdgeEndpointCatalog.Describe()
            .Select(operation => $"{operation.Key} {operation.Rule}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ReadContractOperations(planned: false), mapped);
    }

    [Fact]
    public void TheHeartbeatIsMappedWithTheDeviceRule()
    {
        var heartbeat = Assert.Single(EdgeEndpointCatalog.Describe(), operation => operation.Key == "POST /device/heartbeat");

        Assert.Equal(EdgeAccessRule.DeviceName, heartbeat.Rule.ToString());
        Assert.Contains("POST /device/heartbeat Device", ReadContractOperations(planned: false));
    }

    [Fact]
    public void NoPlannedOperationIsMappedYet()
    {
        var planned = ReadContractOperations(planned: true);
        Assert.NotEmpty(planned);

        var mapped = EdgeEndpointCatalog.Describe().Select(operation => operation.Key).ToHashSet(StringComparer.Ordinal);
        var early = planned.Select(operation => string.Join(' ', operation.Split(' ').Take(2))).Where(mapped.Contains).ToList();

        Assert.True(early.Count == 0, $"Mapped before the contract drops {PlannedExtension}: {string.Join(", ", early)}.");
    }

    private static List<string> ReadContractOperations(bool planned)
    {
        using var stream = File.OpenRead(ContractPath);
        using var contract = JsonDocument.Parse(stream);

        var operations = new List<string>();

        foreach (var path in contract.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(item => OperationMethods.Contains(item.Name)))
            {
                Assert.True(
                    operation.Value.TryGetProperty(MinimumRoleExtension, out var rule),
                    $"{operation.Name.ToUpperInvariant()} {path.Name} in the contract has no {MinimumRoleExtension}.");

                if (operation.Value.TryGetProperty(PlannedExtension, out _) == planned)
                {
                    operations.Add($"{operation.Name.ToUpperInvariant()} {path.Name} {rule.GetString()}");
                }
            }
        }

        operations.Sort(StringComparer.Ordinal);
        return operations;
    }
}
