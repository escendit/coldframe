using Coldframe.Contracts.Events;

namespace Coldframe.Contracts.Sites;

/// <summary>
/// The Site was created by <paramref name="CreatedBy"/>, a User ID (the OIDC <c>sub</c>).
/// </summary>
/// <param name="Name">The Site name.</param>
/// <param name="CreatedBy">The User who created the Site.</param>
[EventType("site.created")]
[GenerateSerializer]
[Alias("coldframe.site-created")]
public sealed record SiteCreated([property: Id(0)] string Name, [property: Id(1)] string CreatedBy);

/// <summary>
/// A User was given a Role on the Site.
/// </summary>
/// <param name="UserId">The User ID (the OIDC <c>sub</c>).</param>
/// <param name="Role">The Role the User holds from now on.</param>
[EventType("site.membership-granted")]
[GenerateSerializer]
[Alias("coldframe.site-membership-granted")]
public sealed record MembershipGranted([property: Id(0)] string UserId, [property: Id(1)] SiteRole Role);
