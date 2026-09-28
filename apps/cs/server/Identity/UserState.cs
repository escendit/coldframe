using Coldframe.Contracts.Sites;

namespace Coldframe.Server.Identity;

/// <summary>
/// One Site creation a User asked for, by idempotency key.
/// </summary>
/// <param name="SiteId">The Site ID chosen for the request.</param>
/// <param name="Name">The requested Site name.</param>
/// <param name="RequestedAt">When the request was received.</param>
/// <param name="Completed">Whether the Site exists with the User as its Owner.</param>
[GenerateSerializer]
[Alias("coldframe.user-site-creation")]
public sealed record SiteCreation(
    [property: Id(0)] string SiteId,
    [property: Id(1)] string Name,
    [property: Id(2)] DateTimeOffset RequestedAt,
    [property: Id(3)] bool Completed);

/// <summary>
/// The state of the User grain: the Site creations it has seen, by idempotency key.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.user-state")]
public sealed class UserState
{
    /// <summary>
    /// How long an idempotency key is remembered after its request.
    /// </summary>
    public static readonly TimeSpan IdempotencyKeyLifetime = TimeSpan.FromHours(24);

    [Id(0)]
    private readonly Dictionary<string, SiteCreation> _siteCreations = new(StringComparer.Ordinal);

    /// <summary>
    /// The Site creations by idempotency key. A key used again after it expired holds the newer request.
    /// </summary>
    public IReadOnlyDictionary<string, SiteCreation> SiteCreations => _siteCreations;

    /// <summary>
    /// Returns the request that still holds <paramref name="idempotencyKey"/> at <paramref name="now"/>.
    /// A completed request expires 24 h after it was received; a pending one never does, because its
    /// Organization may exist and a retry must resume it.
    /// </summary>
    public SiteCreation? FindLive(string idempotencyKey, DateTimeOffset now) =>
        _siteCreations.TryGetValue(idempotencyKey, out var creation)
        && (!creation.Completed || now - creation.RequestedAt < IdempotencyKeyLifetime)
            ? creation
            : null;

    public void Apply(SiteCreationRequested @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _siteCreations[@event.IdempotencyKey] = new SiteCreation(@event.SiteId, @event.Name, @event.RequestedAt, Completed: false);
    }

    public void Apply(SiteCreationCompleted @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (_siteCreations.TryGetValue(@event.IdempotencyKey, out var creation)
            && string.Equals(creation.SiteId, @event.SiteId, StringComparison.Ordinal))
        {
            _siteCreations[@event.IdempotencyKey] = creation with { Completed = true };
        }
    }
}
