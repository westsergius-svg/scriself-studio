namespace ScriSelfStudio.Core.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>Настройки содержат шифротекст; ключ хранится отдельно в системном хранилище.</summary>
internal static class AuthenticatedSecretEnvelope
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string Encrypt(string value, byte[] key)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            var envelope = new byte[NonceSize + TagSize + plaintext.Length];
            var nonce = envelope.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, envelope.AsSpan(NonceSize + TagSize), envelope.AsSpan(NonceSize, TagSize));
            return Convert.ToBase64String(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string Decrypt(string value, byte[] key)
    {
        var envelope = Convert.FromBase64String(value);
        if (envelope.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Повреждены защищённые настройки.");
        }

        var plaintext = new byte[envelope.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(envelope.AsSpan(0, NonceSize), envelope.AsSpan(NonceSize + TagSize),
                envelope.AsSpan(NonceSize, TagSize), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
