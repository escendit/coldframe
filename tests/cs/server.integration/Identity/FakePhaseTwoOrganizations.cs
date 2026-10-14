using Coldframe.Server.Identity;

namespace Coldframe.Server.IntegrationTests.Identity;

/// <summary>
/// The Phase Two writes only the Site grain makes.
/// </summary>
public enum SiteWrite
{
    EnsureRole,
    AddMember,
    GrantRole,
    UpdateDisplayName,
    Invite,
}

/// <summary>
/// An invitation the fake holds as Phase Two would (Story 9.1).
/// </summary>
public sealed record FakeInvitation(string OrganizationId, string Email, string Role, Uri? RedirectUri);

/// <summary>
/// An in-memory Phase Two with switches that make it unavailable, so the TestCluster suite can check
/// idempotency and recovery without Keycloak. Its console methods change Organizations the way an
/// administrator does in the Keycloak admin console; they count as no write of the Server.
/// </summary>
public sealed class FakePhaseTwoOrganizations : IPhaseTwoOrganizations
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, PhaseTwoOrganization> _organizations = new(StringComparer.Ordinal);
    private readonly HashSet<(string Organization, string Role)> _roles = [];
    private readonly HashSet<(string Organization, string User)> _members = [];
    private readonly HashSet<(string Organization, string Role, string User)> _grants = [];
    private readonly List<FakeInvitation> _invitations = [];
    private readonly Dictionary<string, PhaseTwoMemberProfile> _profiles = new(StringComparer.Ordinal);
    private bool _failOnceAfterCreate;
    private SiteWrite? _failOnceOn;
    private bool _stallOnce;
    private SiteWrite? _stallOnceOn;
    private TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writes;
    private int _rosterReads;

    /// <summary>
    /// While set, every call fails as if Keycloak were down.
    /// </summary>
    public bool Unavailable { get; set; }

    /// <summary>
    /// How many writes (creations, roles, members, grants) reached the fake, including repeated ones.
    /// </summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>
    /// How many roster reads reached the fake, including failed ones.
    /// </summary>
    public int RosterReads => Volatile.Read(ref _rosterReads);

    /// <summary>
    /// The next creation stores the Organization, then fails as if the answer was lost.
    /// </summary>
    public void FailOnceAfterCreate()
    {
        lock (_lock)
        {
            _failOnceAfterCreate = true;
        }
    }

    /// <summary>
    /// The next <paramref name="write"/> fails as if Keycloak were down, before it changes anything.
    /// </summary>
    public void FailOnceOn(SiteWrite write)
    {
        lock (_lock)
        {
            _failOnceOn = write;
        }
    }

    /// <summary>
    /// The next tag lookup (<paramref name="write"/> <see langword="null"/>) or <paramref name="write"/> hangs
    /// until its caller cancels, like a Keycloak that accepts the connection but never answers. The returned
    /// task completes once the call hangs.
    /// </summary>
    public Task StallOnce(SiteWrite? write = null)
    {
        lock (_lock)
        {
            _stallOnce = true;
            _stallOnceOn = write;
            _stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _stalled.Task;
        }
    }

    public IReadOnlyList<PhaseTwoOrganization> Tagged(string tag)
    {
        lock (_lock)
        {
            return [.. _organizations.Values.Where(organization => HasTag(organization, tag))];
        }
    }

    public PhaseTwoOrganization? Find(string id)
    {
        lock (_lock)
        {
            return _organizations.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// The invitations of an Organization, in the order they were made.
    /// </summary>
    public IReadOnlyList<FakeInvitation> Invitations(string organizationId)
    {
        lock (_lock)
        {
            return [.. _invitations.Where(invitation => invitation.OrganizationId == organizationId)];
        }
    }

    /// <summary>
    /// Sets the name and email Phase Two shows for a User.
    /// </summary>
    public void SetProfile(string userId, string name, string? email)
    {
        lock (_lock)
        {
            _profiles[userId] = new PhaseTwoMemberProfile(userId, name, email);
        }
    }

    /// <summary>
    /// The invitee accepts on the Phase Two page: the invitation goes, the User becomes a member with its roles.
    /// </summary>
    public void AcceptInvitation(string organizationId, string email, string userId)
    {
        lock (_lock)
        {
            var invitation = _invitations.Single(candidate => candidate.OrganizationId == organizationId && candidate.Email == email);
            _invitations.Remove(invitation);
            _members.Add((organizationId, userId));
            _roles.Add((organizationId, invitation.Role));
            _grants.Add((organizationId, invitation.Role, userId));
        }
    }

    public bool IsMember(string organizationId, string userId)
    {
        lock (_lock)
        {
            return _members.Contains((organizationId, userId));
        }
    }

    public bool HasRole(string organizationId, string role)
    {
        lock (_lock)
        {
            return _roles.Contains((organizationId, role));
        }
    }

    public bool HasGrant(string organizationId, string role, string userId)
    {
        lock (_lock)
        {
            return _grants.Contains((organizationId, role, userId));
        }
    }

    /// <summary>
    /// Console: makes a User a member with the given Organization roles, creating missing roles.
    /// </summary>
    public void ConsoleAddMember(string organizationId, string userId, params string[] roles)
    {
        lock (_lock)
        {
            RequireOrganization(organizationId);
            _members.Add((organizationId, userId));

            foreach (var role in roles)
            {
                _roles.Add((organizationId, role));
                _grants.Add((organizationId, role, userId));
            }
        }
    }

    /// <summary>
    /// Console: grants a member an Organization role, creating it if missing.
    /// </summary>
    public void ConsoleGrantRole(string organizationId, string role, string userId)
    {
        lock (_lock)
        {
            RequireOrganization(organizationId);
            _roles.Add((organizationId, role));
            _grants.Add((organizationId, role, userId));
        }
    }

    /// <summary>
    /// Console: revokes an Organization role from a User.
    /// </summary>
    public void ConsoleRevokeRole(string organizationId, string role, string userId)
    {
        lock (_lock)
        {
            _grants.Remove((organizationId, role, userId));
        }
    }

    /// <summary>
    /// Console: removes a member, and with it the member's Organization roles.
    /// </summary>
    public void ConsoleRemoveMember(string organizationId, string userId)
    {
        lock (_lock)
        {
            _members.Remove((organizationId, userId));
            _grants.RemoveWhere(grant => grant.Organization == organizationId && grant.User == userId);
        }
    }

    /// <summary>
    /// Console: changes the Organization's display name.
    /// </summary>
    public void ConsoleRename(string organizationId, string displayName)
    {
        lock (_lock)
        {
            RequireOrganization(organizationId);
            _organizations[organizationId] = _organizations[organizationId] with { DisplayName = displayName };
        }
    }

    /// <summary>
    /// Console: deletes the Organization with its members, roles and grants.
    /// </summary>
    public void ConsoleDelete(string organizationId)
    {
        lock (_lock)
        {
            _organizations.Remove(organizationId);
            _members.RemoveWhere(member => member.Organization == organizationId);
            _roles.RemoveWhere(role => role.Organization == organizationId);
            _grants.RemoveWhere(grant => grant.Organization == organizationId);
        }
    }

    public Task<PhaseTwoRoster?> GetRosterAsync(string organizationId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _rosterReads);
        ThrowIfUnavailable();

        lock (_lock)
        {
            if (!_organizations.TryGetValue(organizationId, out var organization))
            {
                return Task.FromResult<PhaseTwoRoster?>(null);
            }

            var members = _members
                .Where(member => member.Organization == organizationId)
                .Select(member => member.User)
                .ToHashSet(StringComparer.Ordinal);

            var holders = SiteGrain.OrganizationRoles.Values.ToDictionary(
                role => role,
                role => (IReadOnlySet<string>)_grants
                    .Where(grant => grant.Organization == organizationId && grant.Role == role)
                    .Select(grant => grant.User)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

            return Task.FromResult<PhaseTwoRoster?>(new PhaseTwoRoster(organization.DisplayName, members, holders));
        }
    }

    public async Task<IReadOnlyList<PhaseTwoOrganization>> FindByAttributeAsync(string attribute, string value, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(null, cancellationToken);

        lock (_lock)
        {
            return
            [
                .. _organizations.Values.Where(organization =>
                    organization.Attributes.TryGetValue(attribute, out var values) && values.Contains(value)),
            ];
        }
    }

    public Task<PhaseTwoOrganization?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        return Task.FromResult(Find(id));
    }

    public Task<bool> CreateAsync(PhaseTwoOrganization organization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ThrowIfUnavailable();
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            if (_organizations.ContainsKey(organization.Id)
                || _organizations.Values.Any(existing => existing.Name == organization.Name))
            {
                return Task.FromResult(false);
            }

            _organizations.Add(organization.Id, organization);

            if (_failOnceAfterCreate)
            {
                _failOnceAfterCreate = false;
                throw new IdentityProviderUnavailableException("The fake lost the answer to the creation.");
            }
        }

        return Task.FromResult(true);
    }

    public async Task EnsureRoleAsync(string organizationId, string role, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(SiteWrite.EnsureRole, cancellationToken);
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            ThrowIfFailingOnce(SiteWrite.EnsureRole);
            RequireOrganization(organizationId);
            _roles.Add((organizationId, role));
        }
    }

    public async Task AddMemberAsync(string organizationId, string userId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(SiteWrite.AddMember, cancellationToken);
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            ThrowIfFailingOnce(SiteWrite.AddMember);
            RequireOrganization(organizationId);
            _members.Add((organizationId, userId));
        }
    }

    public async Task GrantRoleAsync(string organizationId, string role, string userId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(SiteWrite.GrantRole, cancellationToken);
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            ThrowIfFailingOnce(SiteWrite.GrantRole);
            RequireOrganization(organizationId);

            if (!_roles.Contains((organizationId, role)) || !_members.Contains((organizationId, userId)))
            {
                throw new InvalidOperationException($"Phase Two refuses role {role} for {userId}: role or membership missing.");
            }

            _grants.Add((organizationId, role, userId));
        }
    }

    public async Task UpdateDisplayNameAsync(string organizationId, string displayName, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(SiteWrite.UpdateDisplayName, cancellationToken);
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            ThrowIfFailingOnce(SiteWrite.UpdateDisplayName);
            RequireOrganization(organizationId);
            _organizations[organizationId] = _organizations[organizationId] with { DisplayName = displayName };
        }
    }

    public async Task<PhaseTwoInviteOutcome> InviteAsync(
        string organizationId,
        string email,
        string role,
        Uri? redirectUri,
        CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        await StallIfDueAsync(SiteWrite.Invite, cancellationToken);
        Interlocked.Increment(ref _writes);

        lock (_lock)
        {
            ThrowIfFailingOnce(SiteWrite.Invite);

            if (!_organizations.ContainsKey(organizationId))
            {
                return PhaseTwoInviteOutcome.NotFound;
            }

            // As Phase Two: an open invitation for the email, or a member with that email, is a conflict.
            if (_invitations.Any(invitation => invitation.OrganizationId == organizationId
                    && string.Equals(invitation.Email, email, StringComparison.OrdinalIgnoreCase))
                || _members.Any(member => member.Organization == organizationId
                    && _profiles.TryGetValue(member.User, out var profile)
                    && string.Equals(profile.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return PhaseTwoInviteOutcome.Conflict;
            }

            _invitations.Add(new FakeInvitation(organizationId, email, role, redirectUri));
            return PhaseTwoInviteOutcome.Sent;
        }
    }

    public Task<IReadOnlyList<PhaseTwoMemberProfile>?> GetMemberProfilesAsync(string organizationId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();

        lock (_lock)
        {
            if (!_organizations.ContainsKey(organizationId))
            {
                return Task.FromResult<IReadOnlyList<PhaseTwoMemberProfile>?>(null);
            }

            return Task.FromResult<IReadOnlyList<PhaseTwoMemberProfile>?>(
            [
                .. _members
                    .Where(member => member.Organization == organizationId)
                    .Select(member => _profiles.GetValueOrDefault(member.User) ?? new PhaseTwoMemberProfile(member.User, member.User, null)),
            ]);
        }
    }

    private static bool HasTag(PhaseTwoOrganization organization, string tag) =>
        organization.Attributes.TryGetValue(IPhaseTwoOrganizations.IdempotencyKeyAttribute, out var values) && values.Contains(tag);

    private void RequireOrganization(string organizationId)
    {
        if (!_organizations.ContainsKey(organizationId))
        {
            throw new InvalidOperationException($"Phase Two has no Organization {organizationId}.");
        }
    }

    private async Task StallIfDueAsync(SiteWrite? call, CancellationToken cancellationToken)
    {
        TaskCompletionSource stalled;

        lock (_lock)
        {
            if (!_stallOnce || _stallOnceOn != call)
            {
                return;
            }

            _stallOnce = false;
            stalled = _stalled;
        }

        stalled.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private void ThrowIfFailingOnce(SiteWrite write)
    {
        if (_failOnceOn == write)
        {
            _failOnceOn = null;
            throw new IdentityProviderUnavailableException($"The fake failed {write} once.");
        }
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new IdentityProviderUnavailableException("The fake is down.");
        }
    }
}
