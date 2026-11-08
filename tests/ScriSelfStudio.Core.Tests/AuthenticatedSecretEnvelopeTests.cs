namespace ScriSelfStudio.Core.Tests;

using System.Security.Cryptography;
using System.Text;
using ScriSelfStudio.Core.Security;

public sealed class AuthenticatedSecretEnvelopeTests
{
    [Fact]
    public void Password_and_unicode_survive_authenticated_encryption()
    {
        const string value = "пароль приложения 🔑";
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = AuthenticatedSecretEnvelope.Encrypt(value, key);

        Assert.Equal(value, AuthenticatedSecretEnvelope.Decrypt(encrypted, key));
        Assert.DoesNotContain(value, Encoding.UTF8.GetString(Convert.FromBase64String(encrypted)), StringComparison.Ordinal);
        Assert.NotEqual(encrypted, AuthenticatedSecretEnvelope.Encrypt(value, key));
    }

    [Fact]
    public void Wrong_key_does_not_reveal_the_password()
    {
        var encrypted = AuthenticatedSecretEnvelope.Encrypt("password", RandomNumberGenerator.GetBytes(32));
        Assert.ThrowsAny<CryptographicException>(() =>
            AuthenticatedSecretEnvelope.Decrypt(encrypted, RandomNumberGenerator.GetBytes(32)));
    }

    [Fact]
    public void Modified_settings_are_rejected()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = Convert.FromBase64String(AuthenticatedSecretEnvelope.Encrypt("password", key));
        encrypted[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() =>
            AuthenticatedSecretEnvelope.Decrypt(Convert.ToBase64String(encrypted), key));
    }
}
