namespace Coldframe.Contracts.Sites;

/// <summary>
/// A User's Role on one Site. Roles are ordered <see cref="Owner"/> &gt; <see cref="Administrator"/> &gt;
/// <see cref="Member"/>, so a higher Role satisfies every lower minimum. They serialize as their names.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-role")]
public enum SiteRole
{
    /// <summary>
    /// Sees the Site.
    /// </summary>
    Member = 1,

    /// <summary>
    /// Manages Lots and Nodes of the Site.
    /// </summary>
    Administrator = 2,

    /// <summary>
    /// Owns the Site: renames it and manages its Members.
    /// </summary>
    Owner = 3,
}
