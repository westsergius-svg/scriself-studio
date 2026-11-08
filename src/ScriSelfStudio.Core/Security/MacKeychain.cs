namespace ScriSelfStudio.Core.Security;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

/// <summary>Один ключ AES для настроек приложения, сохранённый в связке ключей macOS.</summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychain
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;
    private const int KeySize = 32;
    private static readonly byte[] Service = Encoding.UTF8.GetBytes("ru.scriborium.scriselfstudio");
    private static readonly byte[] Account = Encoding.UTF8.GetBytes("settings-encryption-key-v1");

    public static byte[] GetOrCreateKey()
    {
        if (TryReadKey(out var existing))
        {
            return existing;
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        var status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, (uint)key.Length, key, IntPtr.Zero);
        if (status == 0)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        if (status == DuplicateItem && TryReadKey(out existing))
        {
            return existing;
        }

        throw new CryptographicException($"Связка ключей macOS недоступна (код {status}).");
    }

    private static bool TryReadKey(out byte[] key)
    {
        key = [];
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)Account.Length, Account, out var length, out var data, IntPtr.Zero);
        if (status == ItemNotFound)
        {
            return false;
        }

        if (status != 0)
        {
            throw new CryptographicException($"Связка ключей macOS недоступна (код {status}).");
        }

        try
        {
            if (length != KeySize || data == IntPtr.Zero)
            {
                throw new CryptographicException("Повреждён ключ защищённых настроек.");
            }

            key = new byte[KeySize];
            Marshal.Copy(data, key, 0, key.Length);
            return true;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
        }
    }

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain,
        uint serviceLength, byte[] service, uint accountLength, byte[] account,
        out uint passwordLength, out IntPtr passwordData, IntPtr item);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceLength, byte[] service, uint accountLength, byte[] account,
        uint passwordLength, byte[] passwordData, IntPtr item);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
}
