using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Coldframe.Server.Identity;

/// <summary>
/// Obtains access tokens for the Server's own service account with the client-credentials grant and
/// caches each one until shortly before it expires, measured on the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class KeycloakServiceAccount(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    /// <summary>
    /// The named HTTP client both the token request and the Organizations API use.
    /// </summary>
    public const string HttpClientName = "keycloak";

    /// <summary>
    /// A token is renewed this long before it expires.
    /// </summary>
    public static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt;

    /// <summary>
    /// Returns a valid access token of the service account.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _renewAt)
            {
                return _token;
            }

            var settings = options.Value;
            var requestedAt = timeProvider.GetUtcNow();
            var client = httpClientFactory.CreateClient(HttpClientName);

            var tokenUri = settings.RealmUri("protocol/openid-connect/token");

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret ?? string.Empty,
                }),
            };

            using var response = await PhaseTwoOrganizations.SendAsync(client, request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidOperationException(
                    $"Keycloak refused the token request of '{settings.ClientId}' with {(int)response.StatusCode}.");
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

            _token = token.AccessToken;
            _renewAt = requestedAt + TimeSpan.FromSeconds(token.ExpiresIn) - RenewBeforeExpiry;

            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Forgets the cached token, so the next call requests a new one.
    /// </summary>
    public void Invalidate() => _token = null;

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
