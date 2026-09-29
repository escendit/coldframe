using Coldframe.Contracts.Devices;
using Coldframe.Crypto;
using Coldframe.Server.Devices;

namespace Coldframe.Server.Tests.Devices;

/// <summary>
/// <c>K_dev</c> at rest: wrapped under the KEK, bound to its Device, and refused when anything changed.
/// </summary>
public sealed class DeviceKeyVaultTests
{
    private const string Secret = "a-test-kek-of-at-least-thirty-two-characters";

    private static readonly byte[] DeviceKey = Convert.FromHexString("bb3ae0cdc341bb550b81aae28e83da42888f66af83853e370ba6d51b40d0cf4a");

    private static readonly DeviceId Device = KeyHierarchy.DeriveDeviceId(DeviceKey);

    [Fact]
    public void AWrappedKeyUnwrapsToTheSameKey()
    {
        var vault = new DeviceKeyVault(Secret);

        var wrapped = vault.Wrap(Device, DeviceKey);

        Assert.Equal(vault.KekId, wrapped.KekId);
        Assert.Equal(16, wrapped.KekId.Length);
        Assert.Equal(12, wrapped.Nonce.Length);
        Assert.Equal(48, wrapped.Sealed.Length);
        Assert.Equal(DeviceKey, vault.Unwrap(Device, wrapped));

        // Another vault with the same secret reads it: the KEK is derived, not random.
        Assert.Equal(DeviceKey, new DeviceKeyVault(Secret).Unwrap(Device, wrapped));
    }

    [Fact]
    public void EachWrapHasItsOwnNonceAndHidesTheKey()
    {
        var vault = new DeviceKeyVault(Secret);

        var first = vault.Wrap(Device, DeviceKey);
        var second = vault.Wrap(Device, DeviceKey);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Sealed, second.Sealed);
        Assert.DoesNotContain(Convert.ToHexStringLower(DeviceKey), Convert.ToHexStringLower(first.Sealed), StringComparison.Ordinal);
    }

    [Fact]
    public void ATamperedKeyOrAnotherDeviceIsRefused()
    {
        var vault = new DeviceKeyVault(Secret);
        var wrapped = vault.Wrap(Device, DeviceKey);

        var sealedBytes = (byte[])wrapped.Sealed.Clone();
        sealedBytes[0] ^= 1;
        var nonce = (byte[])wrapped.Nonce.Clone();
        nonce[^1] ^= 1;

        Assert.Throws<CryptoFailureException>(() => vault.Unwrap(Device, wrapped with { Sealed = sealedBytes }));
        Assert.Throws<CryptoFailureException>(() => vault.Unwrap(Device, wrapped with { Nonce = nonce }));
        Assert.Throws<CryptoFailureException>(() => vault.Unwrap(new DeviceId(Device.Value ^ 1), wrapped));
        Assert.Throws<CryptoFailureException>(() => vault.Unwrap(Device, wrapped with { Sealed = wrapped.Sealed.AsSpan(0, 47).ToArray() }));
        Assert.Throws<CryptoFailureException>(() => vault.Unwrap(Device, wrapped with { Nonce = [] }));
    }

    [Fact]
    public void AnotherKekIsRefused()
    {
        var wrapped = new DeviceKeyVault(Secret).Wrap(Device, DeviceKey);
        var other = new DeviceKeyVault(Secret + "!");

        Assert.NotEqual(wrapped.KekId, other.KekId);
        Assert.Throws<CryptoFailureException>(() => other.Unwrap(Device, wrapped));

        // Even under a forged KEK ID, the tag does not verify.
        Assert.Throws<CryptoFailureException>(() => other.Unwrap(Device, wrapped with { KekId = other.KekId }));
    }

    [Fact]
    public void AShortSecretOrAKeyOfTheWrongLengthIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new DeviceKeyVault(new string('k', 31)));
        Assert.Throws<ArgumentException>(() => new DeviceKeyVault((string?)null));
        Assert.Throws<CryptoFailureException>(() => new DeviceKeyVault(Secret).Wrap(Device, DeviceKey.AsSpan(0, 31)));
    }

    [Fact]
    public void ItNamesOnlyTheKekId()
    {
        var vault = new DeviceKeyVault(Secret);

        Assert.Equal($"DeviceKeyVault({vault.KekId})", vault.ToString());
        Assert.IsType<WrappedDeviceKey>(vault.Wrap(Device, DeviceKey));
    }
}
