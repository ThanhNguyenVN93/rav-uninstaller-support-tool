using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RavUninstallerTool.Services;

public record UpdateInfo(bool Success, bool IsNewer, string LatestVersion, string Url,
    string ZipUrl = "", string ZipName = "", string SumsUrl = "")
{
    public bool CanAutoUpdate => ZipUrl.Length > 0 && SumsUrl.Length > 0 && ZipName.Length > 0;
}

public static class UpdateService
{
    public const string Repository = "ThanhNguyenVN93/rav-uninstaller-support-tool";
    public const string AllowedAssetPrefix = "https://github.com/ThanhNguyenVN93/rav-uninstaller-support-tool/releases/download/";
    public const string ExeName = "RavUninstallerTool.exe";
    private const long MaxEntrySize = 200L * 1024 * 1024;

    /// <summary>Tests only: when true, http://127.0.0.1 and http://localhost asset URLs are accepted.</summary>
    public static bool AllowLocalhostForTests { get; set; }

    /// <summary>Tests only: overrides the API base (default https://api.github.com).</summary>
    public static string? ApiBaseOverride { get; set; }

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    private static readonly Regex ZipNameRx = new(@"^RavUninstallerTool-.*-win-x64\.zip$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out var v) ? v : null;
    }

    public static bool IsNewer(string latestTag, string currentVersion)
    {
        var l = ParseVersion(latestTag);
        var c = ParseVersion(currentVersion);
        return l != null && c != null && l > c;
    }

    private static bool IsAllowedUrl(string url)
    {
        if (url.StartsWith(AllowedAssetPrefix, StringComparison.Ordinal)) return true;
        return AllowLocalhostForTests &&
               (url.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses a GitHub release JSON. Success=false for draft/prerelease/invalid.</summary>
    public static UpdateInfo ParseRelease(string json, string currentVersion)
    {
        var fail = new UpdateInfo(false, false, currentVersion, "");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return fail;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return fail;
            if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return fail;
            if (!root.TryGetProperty("tag_name", out var t) || t.GetString() is not { Length: > 0 } tag) return fail;

            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            bool newer = IsNewer(tag, currentVersion);

            string zipUrl = "", zipName = "", sumsUrl = "";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                string exact = $"RavUninstallerTool-{(tag.StartsWith('v') || tag.StartsWith('V') ? tag : "v" + tag)}-win-x64.zip";
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string dl = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() ?? "" : "";
                    if (!IsAllowedUrl(dl)) continue;
                    if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = dl;
                    else if (name.Equals(exact, StringComparison.OrdinalIgnoreCase) ||
                             (zipName.Length == 0 && ZipNameRx.IsMatch(name)))
                    {
                        zipUrl = dl; zipName = name;
                    }
                }
            }
            if (zipUrl.Length == 0 || sumsUrl.Length == 0) { zipUrl = ""; zipName = ""; sumsUrl = ""; }
            return new UpdateInfo(true, newer, tag, url, zipUrl, zipName, sumsUrl);
        }
        catch
        {
            return fail;
        }
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(8));
            string api = ApiBaseOverride ?? "https://api.github.com";
            using var resp = await http.GetAsync($"{api}/repos/{Repository}/releases/latest", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new UpdateInfo(false, false, CurrentVersion, "");
            return ParseRelease(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), CurrentVersion);
        }
        catch
        {
            return new UpdateInfo(false, false, CurrentVersion, "");
        }
    }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RavUninstallerTool/" + CurrentVersion);
        return http;
    }

    /// <summary>Finds the hash for <paramref name="assetFileName"/> in a sha256sum-style file.</summary>
    public static string? ParseSha256(string sumsContent, string assetFileName)
    {
        foreach (var raw in sumsContent.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66) continue;
            string hash = line[..64];
            if (!Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$")) continue;
            string name = line[64..].Trim().TrimStart('*').Trim();
            if (name.Equals(assetFileName, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
        }
        return null;
    }

    public static bool VerifySha256(string filePath, string expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || !File.Exists(filePath)) return false;
        using var fs = File.OpenRead(filePath);
        string actual = Convert.ToHexString(SHA256.HashData(fs));
        return actual.Equals(expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts RavUninstallerTool.exe from the zip into destDir (Zip Slip + size guarded).</summary>
    public static string ExtractExe(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string? result = null;

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            if (Path.IsPathRooted(e.FullName) || name.StartsWith('/') || name.Split('/').Contains(".."))
                throw new InvalidDataException("Unsafe zip entry: " + e.FullName);
            string full = Path.GetFullPath(Path.Combine(destDir, name));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Zip entry escapes destination: " + e.FullName);
            if (e.Length > MaxEntrySize)
                throw new InvalidDataException("Zip entry too large: " + e.FullName);

            if (!name.Equals(ExeName, StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith("/" + ExeName, StringComparison.OrdinalIgnoreCase)) continue;

            string target = Path.Combine(root, ExeName);
            using (var src = e.Open())
            using (var dst = File.Create(target))
            {
                var buf = new byte[81920];
                long total = 0; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > MaxEntrySize) throw new InvalidDataException("Zip entry too large: " + e.FullName);
                    dst.Write(buf, 0, n);
                }
            }
            result = target;
        }
        return result ?? throw new FileNotFoundException(ExeName + " not found in archive.");
    }

    public static async Task<bool> DownloadAndApplyUpdateAsync(UpdateInfo info, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!info.CanAutoUpdate) return false;
        string tmp = Path.Combine(Path.GetTempPath(), "RavUninstallerTool-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var http = NewClient(TimeSpan.FromMinutes(5));
            progress?.Report(LocalizationService.T("update_downloading"));
            string sums = await http.GetStringAsync(info.SumsUrl, ct).ConfigureAwait(false);
            string zipPath = Path.Combine(tmp, Path.GetFileName(info.ZipName));
            using (var resp = await http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipPath);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            progress?.Report(LocalizationService.T("update_verifying"));
            string? expected = ParseSha256(sums, info.ZipName);
            if (expected == null || !VerifySha256(zipPath, expected))
                throw new InvalidDataException("SHA-256 mismatch.");
            string newExe = ExtractExe(zipPath, Path.Combine(tmp, "x"));

            progress?.Report(LocalizationService.T("update_applying"));
            return ApplyStagedUpdate(newExe);
        }
        catch (Exception ex)
        {
            progress?.Report(LocalizationService.T("update_apply_failed", ex.Message));
            return false;
        }
    }

    public static bool ApplyStagedUpdate(string newExePath)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(newExePath)) return false;
        string old = currentExe + ".old";
        File.Move(currentExe, old, true);
        try
        {
            File.Copy(newExePath, currentExe, true);
            Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(currentExe)! });
            return true;
        }
        catch
        {
            try { File.Move(old, currentExe, true); } catch { /* best effort rollback */ }
            throw;
        }
    }

    public static void CleanupOldExecutable()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            string old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch { /* ignore */ }
    }
}
