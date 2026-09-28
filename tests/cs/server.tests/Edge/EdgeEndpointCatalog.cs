using Coldframe.Server.Edge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Coldframe.Server.Tests.Edge;

// Compiled into both test projects: the unit tests check the access declarations against the contract,
// the integration tests run the authorization matrix over the same list.

/// <summary>
/// One Edge API operation: an HTTP method on a route, with the access rules it declares.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Route">The route pattern, such as <c>/sites/{siteId}</c>.</param>
/// <param name="Rules">Every <see cref="EdgeAccessRule"/> in the endpoint's metadata; exactly one is valid.</param>
public sealed record EdgeOperation(string Method, string Route, IReadOnlyList<EdgeAccessRule> Rules)
{
    /// <summary>
    /// The operation's key, such as <c>GET /sites/{siteId}</c>.
    /// </summary>
    public string Key => $"{Method} {Route}";

    /// <summary>
    /// The one declared rule. Fails when there is none or more than one.
    /// </summary>
    public EdgeAccessRule Rule => Rules.Count == 1
        ? Rules[0]
        : throw new InvalidOperationException($"{Key} declares {Rules.Count} access rules; it must declare exactly one.");
}

/// <summary>
/// Lists every endpoint <see cref="EdgeApi.MapEdgeApi"/> maps, from a throwaway <see cref="WebApplication"/>,
/// so a new endpoint is picked up without registering it anywhere else.
/// </summary>
public static class EdgeEndpointCatalog
{
    public static IReadOnlyList<EdgeOperation> Describe()
    {
        var builder = WebApplication.CreateSlimBuilder();
        using var app = builder.Build();

        app.MapEdgeApi();

        return
        [
            .. ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(endpoint =>
                {
                    var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                        ?? throw new InvalidOperationException($"{endpoint.RoutePattern.RawText} declares no HTTP method.");
                    var rules = endpoint.Metadata.GetOrderedMetadata<EdgeAccessRule>();

                    return methods.Select(method => new EdgeOperation(method, endpoint.RoutePattern.RawText ?? string.Empty, rules));
                })
                .OrderBy(operation => operation.Key, StringComparer.Ordinal),
        ];
    }
}
