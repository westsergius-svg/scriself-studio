namespace ScriSelfStudio.UpdateManifest;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScriSelfStudio.Core.Updates;

/// <summary>Подписывает выпуск на любой ОС тем же контрактом, которым его проверяет приложение.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? temporary = null;
        try
        {
            var options = Parse(args);
            var installer = Path.GetFullPath(Required(options, "--file"));
            var output = Path.GetFullPath(Required(options, "--output"));
            var keyPath = Path.GetFullPath(Required(options, "--signing-key"));
            var version = Required(options, "--version");
            var parts = UpdateFeedParser.ParseVersion(version);
            if (parts.Count < 2) { throw new ArgumentException("Укажите числовую версию выпуска через точку."); }
            if (string.Equals(output, installer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(output, keyPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Манифест должен записываться в отдельный файл.");
            }
            var runtime = options.GetValueOrDefault("--runtime", "win-x64");
            if (UpdateFeedParser.ParseManifest(JsonSerializer.Serialize(new { version, file = Path.GetFileName(installer) }),
                ScriboriumUpdateService.DefaultChannelUrl, runtime) is null)
            {
                throw new ArgumentException("Имя файла не соответствует платформе выпуска.");
            }
            string digest;
            await using (var source = File.OpenRead(installer))
            {
                if (source.Length == 0) { throw new ArgumentException("Файл выпуска пуст."); }
                digest = Convert.ToHexString(await SHA256.HashDataAsync(source)).ToLowerInvariant();
            }
            var info = new UpdateInfo(parts, version, Path.GetFileName(installer),
                UpdateFeedParser.JoinUrl(ScriboriumUpdateService.DefaultChannelUrl, Path.GetFileName(installer)),
                options.GetValueOrDefault("--notes", string.Empty), "manifest") { Sha256 = digest };
            using var rsa = RSA.Create();
            // Ключ остаётся в памяти процесса; в результат и вывод попадает только публичная подпись.
            rsa.ImportFromPem(await File.ReadAllTextAsync(keyPath));
            info = info with { Signature = Convert.ToBase64String(rsa.SignData(SignedUpdateManifest.Payload(info),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) };
            SignedUpdateManifest.Verify(info, SignedUpdateManifest.PublicKey);
            var manifest = new Dictionary<string, string>
            {
                ["version"] = version, ["file"] = info.FileName, ["runtime"] = runtime,
                ["notes"] = info.Notes, ["sha256"] = digest, ["signature"] = info.Signature,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            File.Move(temporary, output, overwrite: true);
            temporary = null;
            Console.WriteLine("Манифест создан, SHA-256 и подпись проверены.");
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or CryptographicException or InvalidOperationException)
        {
            Console.Error.WriteLine("Манифест не создан: " + error.Message);
            return 1;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Отсутствует обязательный параметр " + name);

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || args[index] is not ("--file" or "--output" or "--signing-key" or "--version" or "--runtime" or "--notes")
                || !result.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Параметры: --file --output --signing-key --version [--runtime] [--notes].");
            }
        }
        return result;
    }
}
