using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChillWithYouSpanishInstaller;

internal sealed record ModUpdateInfo(
    string Version,
    string InstallerUrl,
    string Sha256,
    string? Notes);

internal static class UpdateService
{
    public const string ManifestUrl =
        "https://raw.githubusercontent.com/Lirienne16/ChillWithYou-ESLatam/main/update.json";

    public static async Task<ModUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(ManifestUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var update = await JsonSerializer.DeserializeAsync<ModUpdateInfo>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        if (update is null || !System.Version.TryParse(update.Version, out var remote) ||
            !System.Version.TryParse(InstallerCore.ModVersion, out var local) || remote <= local)
            return null;
        if (!Uri.TryCreate(update.InstallerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("El manifiesto contiene una dirección de descarga inválida.");
        if (update.Sha256.Length != 64 || update.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("El manifiesto contiene un SHA-256 inválido.");
        return update;
    }

    public static async Task<string> DownloadAsync(
        ModUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string destination = Path.Combine(Path.GetTempPath(),
            $"ChillWithYou_ESLatam_Instalador_{update.Version}.exe");
        string temporary = destination + $".tmp.{Guid.NewGuid():N}";
        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(update.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    if (total is > 0)
                        progress?.Report((int)Math.Clamp(written * 100 / total.Value, 0, 100));
                }
                await output.FlushAsync(cancellationToken);
            }

            string hash;
            await using (var downloaded = File.OpenRead(temporary))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La descarga no coincide con el SHA-256 publicado.");

            File.Move(temporary, destination, overwrite: true);
            progress?.Report(100);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static void Launch(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ChillWithYou-ESLatam", InstallerCore.ModVersion));
        return client;
    }
}
