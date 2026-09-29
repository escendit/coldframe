namespace Coldframe.Contracts.Sites;

/// <summary>
/// The lifecycle of a Site. Outside <see cref="Active"/>, Site-scoped calls answer 404.
/// </summary>
[GenerateSerializer]
[Alias("coldframe.site-lifecycle")]
public enum SiteLifecycle
{
    /// <summary>
    /// The Site grain has not been initialized.
    /// </summary>
    Uncreated = 0,

    /// <summary>
    /// The Site exists.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The Site was deleted. Its ID stays resolvable; nothing is hard-deleted.
    /// </summary>
    Deleted = 2,
}
