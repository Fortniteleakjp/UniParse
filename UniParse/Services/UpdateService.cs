using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UniParse.Services;

/// <summary>Information about an available newer release.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string HtmlUrl, string Notes, string? ZipUrl);

/// <summary>
/// Checks GitHub Releases for a newer build and (optionally) downloads it and
/// swaps the application files via a small PowerShell helper, then relaunches.
/// </summary>
public sealed class UpdateService
{
    public const string Owner = "Fortniteleakjp";
    public const string Repo = "Unity-analysis";

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UniParse-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>The version of the currently running build (from the assembly's informational version).</summary>
    public static Version CurrentVersion
    {
        get
        {
            Assembly? asm = Assembly.GetEntryAssembly();
            string? text = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                           ?? asm?.GetName().Version?.ToString();
            return ParseVersion(text) ?? new Version(0, 0, 0);
        }
    }

    /// <summary>Returns update info if the latest release is newer than the current build; otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using HttpResponseMessage response = await Http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode)
            return null; // no releases yet, rate-limited, offline, etc.

        await using Stream stream = await response.Content.ReadAsStreamAsync(token);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        JsonElement root = document.RootElement;

        string tag = GetString(root, "tag_name");
        Version? latest = ParseVersion(tag);
        if (latest is null || latest <= CurrentVersion)
            return null;

        string htmlUrl = GetString(root, "html_url");
        string notes = GetString(root, "body");
        string? zipUrl = FindWindowsZip(root);

        return new UpdateInfo(latest, tag, htmlUrl, notes, zipUrl);
    }

    /// <summary>Downloads the release zip to a temp file, reporting progress (0..1).</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(info.ZipUrl))
            throw new InvalidOperationException("このリリースにダウンロード可能な zip アセットがありません。");

        string tempZip = Path.Combine(Path.GetTempPath(), $"UniParse_update_{info.Version}.zip");

        using HttpResponseMessage response = await Http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        await using FileStream destination = File.Create(tempZip);

        byte[] buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            readTotal += read;
            if (total is > 0)
                progress?.Report((double)readTotal / total.Value);
        }
        return tempZip;
    }

    /// <summary>
    /// Writes and launches a detached PowerShell helper that waits for this process to exit,
    /// overwrites the app folder with the downloaded zip, and relaunches the app.
    /// The caller must shut the application down immediately afterwards.
    /// </summary>
    public void StartUpdaterAndExit(string zipPath)
    {
        string appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string exePath = Environment.ProcessPath ?? Path.Combine(appDir, "UniParse.exe");

        string scriptPath = Path.Combine(Path.GetTempPath(), $"UniParse_update_{Guid.NewGuid():N}.ps1");
        string script = $$"""
$log = Join-Path $env:TEMP 'UniParse_update.log'
function Log($m) { try { Add-Content -Path $log -Value ((Get-Date).ToString('HH:mm:ss') + ' ' + $m) } catch {} }
Log '--- updater started ---'
$exe  = '{{Escape(exePath)}}'
$zip  = '{{Escape(zipPath)}}'
$dest = '{{Escape(appDir)}}'

# Wait until the app has fully exited and released its files (max 60s).
for ($i = 0; $i -lt 60; $i++) {
    try { $fs = [System.IO.File]::Open($exe, 'Open', 'ReadWrite', 'None'); $fs.Close(); Log 'app released files'; break }
    catch { Start-Sleep -Milliseconds 1000 }
}

$extract = Join-Path $env:TEMP ('UniParse_ext_' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $items = Get-ChildItem -Path $extract
    if ($items.Count -eq 1 -and $items[0].PSIsContainer) { $src = $items[0].FullName } else { $src = $extract }
    Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force
    Log 'files updated'
}
catch {
    Log ('ERROR applying update: ' + $_.Exception.Message)
}

try { Remove-Item -Path $extract -Recurse -Force } catch {}
try { Remove-Item -Path $zip -Force } catch {}

Log 'relaunching app'
try { Start-Process -FilePath $exe -WorkingDirectory $dest } catch { Log ('relaunch error: ' + $_.Exception.Message) }
try { Remove-Item -Path $PSCommandPath -Force } catch {}
""";
        File.WriteAllText(scriptPath, script);

        ProcessStartInfo psi = new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            // ShellExecute launches the helper detached from this process, so it survives our exit.
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    private static string Escape(string path) => path.Replace("'", "''");

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? FindWindowsZip(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = GetString(asset, "name");
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                name.Contains("win", StringComparison.OrdinalIgnoreCase))
            {
                string url = GetString(asset, "browser_download_url");
                if (!string.IsNullOrEmpty(url))
                    return url;
            }
        }
        return null;
    }

    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string trimmed = text.TrimStart('v', 'V');
        Match match = Regex.Match(trimmed, @"^\d+(\.\d+){0,3}");
        if (!match.Success)
            return null;
        string value = match.Value.Contains('.') ? match.Value : match.Value + ".0";
        return Version.TryParse(value, out Version? version) ? version : null;
    }
}
