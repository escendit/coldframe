namespace Coldframe.Server.Identity;

/// <summary>
/// How the Server reaches the Phase Two Organizations API with its own service account
/// (<c>coldframe-server</c>, roles <c>view-organizations</c> and <c>manage-organizations</c>). It holds
/// no Keycloak administrator credentials.
/// </summary>
public sealed class KeycloakOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "Keycloak";

    /// <summary>
    /// The Keycloak base address, such as <c>https://id.example.org</c>.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// The realm that holds Coldframe's Users and Organizations.
    /// </summary>
    public string Realm { get; set; } = "coldframe";

    /// <summary>
    /// The client ID of the Server's service account.
    /// </summary>
    public string ClientId { get; set; } = "coldframe-server";

    /// <summary>
    /// The client secret of the Server's service account.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The upper bound of one HTTP request to Keycloak, retries included. Kept under 20 s.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The upper bound of all Keycloak calls one Site creation makes. Kept under the Orleans call
    /// timeout (30 s), so a hanging Keycloak ends in 503, not in a timed-out grain call.
    /// </summary>
    public TimeSpan OperationBudget { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Returns an address below the realm, such as <c>{BaseUrl}/realms/{Realm}/orgs</c>.
    /// </summary>
    /// <param name="relative">The path below the realm, without a leading slash.</param>
    public Uri RealmUri(string relative)
    {
        var baseUrl = BaseUrl ?? throw new InvalidOperationException("Keycloak:BaseUrl is not configured.");
        var root = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri($"{baseUrl.AbsoluteUri}/");

        return new Uri(root, $"realms/{Uri.EscapeDataString(Realm)}/{relative}");
    }
}
