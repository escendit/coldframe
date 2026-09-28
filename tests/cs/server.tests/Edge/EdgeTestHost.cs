using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Coldframe.Server.Tests.Edge;

/// <summary>
/// A throwaway host with exactly the Edge API services the Server registers. Nothing connects: the
/// bearer handler fetches no metadata until a request arrives.
/// </summary>
internal static class EdgeTestHost
{
    public const string Authority = "https://id.test.invalid/realms/coldframe";

    public static WebApplication Build()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Identity:Authority"] = Authority,
        });

        builder.AddEdgeApi();

        return builder.Build();
    }
}
