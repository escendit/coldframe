using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Coldframe.Server.Identity;

/// <summary>
/// The Phase Two Organizations API over HTTP, authenticated as the Server's service account.
/// </summary>
/// <remarks>
/// The named client <see cref="KeycloakServiceAccount.HttpClientName"/> carries the standard resilience
/// handler; its <see cref="HttpClient.Timeout"/> (<see cref="KeycloakOptions.RequestTimeout"/>) bounds each
/// request including retries.
/// </remarks>
public sealed class PhaseTwoOrganizations(
    IHttpClientFactory httpClientFactory,
    KeycloakServiceAccount serviceAccount,
    IOptions<KeycloakOptions> options) : IPhaseTwoOrganizations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PhaseTwoOrganization>> FindByAttributeAsync(
        string attribute,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        ArgumentNullException.ThrowIfNull(value);

        // An exact attribute match. The value is quoted because it contains ':'.
        var query = Uri.EscapeDataString($"{attribute}:\"{value}\"");

        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"orgs?q={query}", null, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // A value Phase Two cannot parse matches nothing; the creation by ID still converges.
            return [];
        }

        EnsureStatus(response, "search Organizations", HttpStatusCode.OK);

        var organizations = await response.Content
            .ReadFromJsonAsync<List<OrganizationRepresentation>>(Json, cancellationToken)
            .ConfigureAwait(false) ?? [];

        return [.. organizations.Select(ToOrganization)];
    }

    /// <inheritdoc />
    public async Task<PhaseTwoOrganization?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var response = await SendAuthorizedAsync(HttpMethod.Get, $"orgs/{Escape(id)}", null, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureStatus(response, $"read Organization {id}", HttpStatusCode.OK);

        var organization = await response.Content
            .ReadFromJsonAsync<OrganizationRepresentation>(Json, cancellationToken)
            .ConfigureAwait(false);

        return organization is null ? null : ToOrganization(organization);
    }

    /// <inheritdoc />
    public async Task<bool> CreateAsync(PhaseTwoOrganization organization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);

        var body = new OrganizationRepresentation(
            organization.Id,
            organization.Name,
            organization.DisplayName,
            organization.Attributes.ToDictionary(pair => pair.Key, pair => (List<string>?)[.. pair.Value], StringComparer.Ordinal));

        using var response = await SendAuthorizedAsync(HttpMethod.Post, "orgs", body, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }

        EnsureStatus(response, $"create Organization {organization.Id}", HttpStatusCode.Created);
        return true;
    }

    /// <inheritdoc />
    public async Task EnsureRoleAsync(string organizationId, string role, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        using var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"orgs/{Escape(organizationId)}/roles",
            new { name = role },
            cancellationToken).ConfigureAwait(false);

        // 409: the role exists, which is what was asked for.
        EnsureStatus(response, $"create role {role} in Organization {organizationId}", HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    /// <inheritdoc />
    public async Task AddMemberAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var response = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"orgs/{Escape(organizationId)}/members/{Escape(userId)}",
            null,
            cancellationToken).ConfigureAwait(false);

        EnsureStatus(response, $"add member {userId} to Organization {organizationId}", HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    /// <inheritdoc />
    public async Task GrantRoleAsync(string organizationId, string role, string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var response = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"orgs/{Escape(organizationId)}/roles/{Escape(role)}/users/{Escape(userId)}",
            null,
            cancellationToken).ConfigureAwait(false);

        EnsureStatus(response, $"grant role {role} to {userId} in Organization {organizationId}", HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.OK);
    }

    /// <summary>
    /// Sends a request and turns every transport failure, timeout and server error into
    /// <see cref="IdentityProviderUnavailableException"/>. Cancellation by the caller propagates as is.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new IdentityProviderUnavailableException(
                $"Keycloak did not answer {request.Method} {request.RequestUri?.AbsolutePath}.",
                exception);
        }

        if ((int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new IdentityProviderUnavailableException(
                $"Keycloak answered {request.Method} {request.RequestUri?.AbsolutePath} with {status}.");
        }

        return response;
    }

    private static string Escape(string segment) => Uri.EscapeDataString(segment);

    private static void EnsureStatus(HttpResponseMessage response, string action, params HttpStatusCode[] expected)
    {
        if (!expected.Contains(response.StatusCode))
        {
            throw new InvalidOperationException(
                $"Phase Two refused to {action}: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static PhaseTwoOrganization ToOrganization(OrganizationRepresentation organization) =>
        new(
            organization.Id ?? string.Empty,
            organization.Name ?? string.Empty,
            organization.DisplayName,
            (organization.Attributes ?? []).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)(pair.Value ?? []),
                StringComparer.Ordinal));

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(KeycloakServiceAccount.HttpClientName);
        var uri = options.Value.RealmUri(path);

        // A token can be revoked before it expires: on 401, request a new one and try once more.
        for (var attempt = 1; ; attempt++)
        {
            var token = await serviceAccount.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            }

            var response = await SendAsync(client, request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 1)
            {
                return response;
            }

            response.Dispose();
            serviceAccount.Invalidate();
        }
    }

    private sealed record OrganizationRepresentation(
        string? Id,
        string? Name,
        string? DisplayName,
        Dictionary<string, List<string>?>? Attributes);
}
