using System.Text.Json.Serialization;

namespace Coldframe.Server.Identity.Reconciliation;

/// <summary>
/// A Keycloak admin event as <c>keycloak-temporal-extensions</c> passes it to the <c>IdentityAdminEvent</c>
/// workflow: Jackson camelCase, with nulls. Only <see cref="RealmId"/>, <see cref="ResourceType"/>,
/// <see cref="OperationType"/>, <see cref="ResourcePath"/> and <see cref="Error"/> are read; the
/// representation and the auth details never drive state.
/// </summary>
public sealed record KeycloakAdminEvent
{
    /// <summary>
    /// The event ID, a fresh UUID per dispatch.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// When the event happened, in milliseconds since the Unix epoch.
    /// </summary>
    [JsonPropertyName("time")]
    public long Time { get; init; }

    /// <summary>
    /// The ID (not the name) of the realm the event happened in.
    /// </summary>
    [JsonPropertyName("realmId")]
    public string? RealmId { get; init; }

    /// <summary>
    /// Who caused the event.
    /// </summary>
    [JsonPropertyName("authDetails")]
    public KeycloakAuthDetails? AuthDetails { get; init; }

    /// <summary>
    /// The resource type, such as <c>ORGANIZATION_MEMBERSHIP</c>.
    /// </summary>
    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; init; }

    /// <summary>
    /// <c>CREATE</c>, <c>UPDATE</c>, <c>DELETE</c> or <c>ACTION</c>.
    /// </summary>
    [JsonPropertyName("operationType")]
    public string? OperationType { get; init; }

    /// <summary>
    /// The resource path, relative to <c>/realms/{realm}/</c>.
    /// </summary>
    [JsonPropertyName("resourcePath")]
    public string? ResourcePath { get; init; }

    /// <summary>
    /// The resource representation as a JSON string, or <see langword="null"/>. Never read.
    /// </summary>
    [JsonPropertyName("representation")]
    public string? Representation { get; init; }

    /// <summary>
    /// The error, when the operation failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// Who caused a Keycloak admin event.
/// </summary>
public sealed record KeycloakAuthDetails
{
    /// <summary>
    /// The realm ID of the caller.
    /// </summary>
    [JsonPropertyName("realmId")]
    public string? RealmId { get; init; }

    /// <summary>
    /// The client the caller used.
    /// </summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }

    /// <summary>
    /// The calling User.
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>
    /// The caller's address.
    /// </summary>
    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; init; }
}

/// <summary>
/// A Keycloak user event as <c>keycloak-temporal-extensions</c> passes it to the <c>IdentityUserEvent</c>
/// workflow. The Server only completes these workflows.
/// </summary>
public sealed record KeycloakUserEvent
{
    /// <summary>
    /// The event ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// When the event happened, in milliseconds since the Unix epoch.
    /// </summary>
    [JsonPropertyName("time")]
    public long Time { get; init; }

    /// <summary>
    /// The event type, such as <c>LOGIN</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The realm ID.
    /// </summary>
    [JsonPropertyName("realmId")]
    public string? RealmId { get; init; }

    /// <summary>
    /// The client.
    /// </summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }

    /// <summary>
    /// The User.
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>
    /// The session.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>
    /// The caller's address.
    /// </summary>
    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; init; }

    /// <summary>
    /// The error, when the action failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Event-specific details.
    /// </summary>
    [JsonPropertyName("details")]
    public IReadOnlyDictionary<string, string?>? Details { get; init; }
}
