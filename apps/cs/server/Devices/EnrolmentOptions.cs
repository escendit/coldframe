namespace Coldframe.Server.Devices;

/// <summary>
/// The Server's Device enrolment keys (AD-12), both from fixed Secrets (<c>deploy/SECRETS.md</c>). Both are
/// required, have no fallback and are checked at start.
/// </summary>
public sealed class EnrolmentOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "Enrolment";

    /// <summary>
    /// The shortest key-encryption secret, in characters.
    /// </summary>
    public const int MinDeviceKeyEncryptionKeyLength = 32;

    /// <summary>
    /// The X25519 enrolment private key as PKCS#8 PEM (<c>openssl genpkey -algorithm X25519</c>), from the
    /// Secret <c>coldframe-enrolment-key</c>, key <c>private-key.pem</c>.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// The secret the key-encryption key of every stored <c>K_dev</c> derives from, at least 32 characters,
    /// from the Secret <c>coldframe-device-kek</c>, key <c>kek</c>. It is separate from the enrolment key, so
    /// rotating the enrolment key leaves every stored <c>K_dev</c> readable.
    /// </summary>
    public string? DeviceKeyEncryptionKey { get; set; }
}
