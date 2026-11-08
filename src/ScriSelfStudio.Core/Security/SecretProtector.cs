namespace ScriSelfStudio.Core.Security;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Шифрование паролей: DPAPI в Windows, AES-GCM с ключом в Keychain в macOS.
/// В настройках хранится зашифрованная строка, а ключ принадлежит учётной записи.
/// </summary>
public static class SecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;
    private const string MacEnvelopePrefix = "mac-keychain-v1:";

    public static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var key = MacKeychain.GetOrCreateKey();
                try
                {
                    return MacEnvelopePrefix + AuthenticatedSecretEnvelope.Encrypt(value, key);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            if (!OperatingSystem.IsWindows())
            {
                return Convert.ToBase64String(bytes);
            }

            return Convert.ToBase64String(Transform(bytes, protect: true));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            if (protectedValue.StartsWith(MacEnvelopePrefix, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsMacOS())
                {
                    return null;
                }

                var key = MacKeychain.GetOrCreateKey();
                try
                {
                    return AuthenticatedSecretEnvelope.Decrypt(protectedValue[MacEnvelopePrefix.Length..], key);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }

            var bytes = Convert.FromBase64String(protectedValue);
            if (!OperatingSystem.IsWindows())
            {
                return Encoding.UTF8.GetString(bytes);
            }

            return Encoding.UTF8.GetString(Transform(bytes, protect: false));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = new DataBlob
        {
            Size = input.Length,
            Data = Marshal.AllocHGlobal(input.Length),
        };

        try
        {
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            DataBlob outputBlob;
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);

            if (!succeeded)
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
                return output;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputBlob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
