namespace Coldframe.Server.Identity;

/// <summary>
/// Keycloak could not be reached, timed out, or answered with a server error. Grains turn it into an
/// <c>IdentityProviderUnavailable</c> result; it never crosses a grain boundary.
/// </summary>
public sealed class IdentityProviderUnavailableException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public IdentityProviderUnavailableException()
    {
    }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    public IdentityProviderUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and its cause.
    /// </summary>
    public IdentityProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
