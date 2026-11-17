namespace ScriSelfStudio.Core.Updates;

using System.Security.Cryptography;
using System.Text;

/// <summary>Публичный ключ выпуска закреплён в приложении, приватный хранится у автора.</summary>
public static class SignedUpdateManifest
{
    public const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAyVEby3Bn3KOTVHwWO8eo
        79UiNEp6t/i/7y9qudaexsZvNF4D8FBFzB6F7x7KpCWdkcqSWKbFeQeJKVBJmodG
        Sn2dbHZLoSr1DNCcK1rdGAlcsS97X/xyQINJzfpXQqHCnHgszADzXVsfQiQf5Ukv
        De0/Gm1TuImcxP0UuHk7kw25Vj68lgUfKXnW7BTv3iYbmdNI6w3x+Ww7Y+rJhlET
        hoQR/1GXnxcPPsy+YZhG5PLIisGoBMzV3R5+W95SS013BBKAK05EYaWeot+Vxd+n
        p4ETMJsk50e1DE0o4Nm9gjLK7xZkjXji5mhEDBDGDquvUKP2GAsX1bzU0IL2X+HO
        /llAHXjLV4/n7ZvySTy1VgNHvE0o+1P7tRlilhS52JolhSI+ah6CvjnQdzWbTuAV
        CJPDO/Kj5nU+D8DkCZI4oRJJTYSwLAAgzjOetetTecjuoH5pLorE233rp3dVqB1Y
        vf2L9rjesZUlZ6Rq1B/kt79jDmq+G1aUueS0fX9+RTu5AgMBAAE=
        -----END PUBLIC KEY-----
        """;

    public static byte[] Payload(UpdateInfo info) => Encoding.UTF8.GetBytes(
        "scriself-update-v1\n" + info.VersionText + "\n" + info.FileName + "\n"
        + info.Sha256!.ToLowerInvariant() + "\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(info.Notes)));

    public static void Verify(UpdateInfo info, string publicKey)
    {
        if (info.Sha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(info.Signature))
            throw new InvalidDataException("Не удалось проверить подлинность обновления. Скачайте новую версию с официального сайта вручную.");
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            if (!rsa.VerifyData(Payload(info), Convert.FromBase64String(info.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new InvalidDataException("Подлинность обновления не подтверждена. Установщик не запущен.");
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Подлинность обновления не подтверждена. Установщик не запущен.", error);
        }
    }
}
