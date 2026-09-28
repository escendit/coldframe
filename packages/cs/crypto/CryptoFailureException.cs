namespace Coldframe.Crypto;

/// <summary>
/// Why a cryptographic operation failed.
/// </summary>
public enum CryptoFailure
{
    /// <summary>A ciphertext, tag or associated data did not authenticate.</summary>
    AuthenticationFailed,

    /// <summary>The first message of a setup session did not open: the PoP codes differ.</summary>
    WrongSetupCode,

    /// <summary>A counter was already seen, is not above the last one, or fell out of the window.</summary>
    Replay,

    /// <summary>An X25519 exchange produced the all-zero value (a small-order public key).</summary>
    InvalidPublicKey,

    /// <summary>A PoP code is empty, longer than 64 characters or not ASCII.</summary>
    InvalidSetupCode,

    /// <summary>An input has the wrong length.</summary>
    InvalidLength,
}

/// <summary>
/// A cryptographic operation failed. The message never carries key material or payload.
/// </summary>
public sealed class CryptoFailureException : Exception
{
    /// <summary>
    /// Creates the exception for <paramref name="failure"/>.
    /// </summary>
    public CryptoFailureException(CryptoFailure failure)
        : base(Describe(failure))
    {
        Failure = failure;
    }

    /// <summary>
    /// Creates an <see cref="CryptoFailure.AuthenticationFailed"/> exception.
    /// </summary>
    public CryptoFailureException()
        : this(CryptoFailure.AuthenticationFailed)
    {
    }

    /// <summary>
    /// Creates an <see cref="CryptoFailure.AuthenticationFailed"/> exception with a message.
    /// </summary>
    public CryptoFailureException(string message)
        : base(message)
    {
        Failure = CryptoFailure.AuthenticationFailed;
    }

    /// <summary>
    /// Creates an <see cref="CryptoFailure.AuthenticationFailed"/> exception with a message and cause.
    /// </summary>
    public CryptoFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = CryptoFailure.AuthenticationFailed;
    }

    /// <summary>
    /// Why the operation failed.
    /// </summary>
    public CryptoFailure Failure { get; }

    private static string Describe(CryptoFailure failure) => failure switch
    {
        CryptoFailure.AuthenticationFailed => "The message did not authenticate.",
        CryptoFailure.WrongSetupCode => "The setup code is wrong.",
        CryptoFailure.Replay => "The counter was replayed or is outside the window.",
        CryptoFailure.InvalidPublicKey => "The X25519 public key is invalid.",
        CryptoFailure.InvalidSetupCode => "The setup code is empty, too long or not ASCII.",
        CryptoFailure.InvalidLength => "An input has the wrong length.",
        _ => "A cryptographic operation failed.",
    };
}
