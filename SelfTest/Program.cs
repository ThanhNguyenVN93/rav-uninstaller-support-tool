using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RavUninstallerTool.Services;

int failures = 0;
void Check(bool cond, string name)
{
    if (!cond) { failures++; Console.WriteLine("FAIL: " + name); }
}

const string Pre = UpdateService.AllowedAssetPrefix;

string Release(string tag, bool draft = false, bool pre = false, bool zip = true, bool sums = true, string prefix = Pre)
{
    var assets = new List<object>();
    if (zip) assets.Add(new { name = $"RavUninstallerTool-{tag}-win-x64.zip", browser_download_url = $"{prefix}{tag}/RavUninstallerTool-{tag}-win-x64.zip" });
    if (sums) assets.Add(new { name = "SHA256SUMS.txt", browser_download_url = $"{prefix}{tag}/SHA256SUMS.txt" });
    return JsonSerializer.Serialize(new { tag_name = tag, html_url = "https://example/r", draft, prerelease = pre, assets });
}

// Version parsing
Check(UpdateService.ParseVersion("v1.2.3") == new Version(1, 2, 3), "ParseVersion v1.2.3");
Check(UpdateService.ParseVersion("1.2.3-beta") == new Version(1, 2, 3), "ParseVersion suffix");
Check(UpdateService.ParseVersion("garbage") == null, "ParseVersion garbage");
Check(UpdateService.IsNewer("v1.1.0", "1.0.0"), "IsNewer true");
Check(!UpdateService.IsNewer("v1.0.0", "1.0.0"), "IsNewer equal");
Check(!UpdateService.IsNewer("v0.9.0", "1.0.0"), "IsNewer older");
Check(!UpdateService.IsNewer("bad", "1.0.0"), "IsNewer bad");

// ParseRelease
var ok = UpdateService.ParseRelease(Release("v1.1.0"), "1.0.0");
Check(ok.Success && ok.IsNewer && ok.CanAutoUpdate, "ParseRelease valid");
Check(!UpdateService.ParseRelease(Release("v1.1.0", draft: true), "1.0.0").Success, "ParseRelease draft");
Check(!UpdateService.ParseRelease(Release("v1.1.0", pre: true), "1.0.0").Success, "ParseRelease prerelease");
Check(!UpdateService.ParseRelease(Release("v1.1.0", zip: false), "1.0.0").CanAutoUpdate, "ParseRelease missing zip");
Check(!UpdateService.ParseRelease(Release("v1.1.0", sums: false), "1.0.0").CanAutoUpdate, "ParseRelease missing sums");
Check(!UpdateService.ParseRelease(Release("v1.1.0", prefix: "https://evil.example/"), "1.0.0").CanAutoUpdate, "ParseRelease bad prefix");
Check(!UpdateService.ParseRelease(Release("v1.1.0", prefix: "http://127.0.0.1/"), "1.0.0").CanAutoUpdate, "localhost rejected by default");
UpdateService.AllowLocalhostForTests = true;
Check(UpdateService.ParseRelease(Release("v1.1.0", prefix: "http://127.0.0.1:8080/"), "1.0.0").CanAutoUpdate, "localhost allowed in test mode");
UpdateService.AllowLocalhostForTests = false;
Check(!UpdateService.ParseRelease("not json", "1.0.0").Success, "ParseRelease invalid json");

// Sha256
string tmp = Path.Combine(Path.GetTempPath(), "RavSelfTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);
try
{
    string file = Path.Combine(tmp, "a.bin");
    File.WriteAllBytes(file, Encoding.UTF8.GetBytes("hello"));
    string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    string sums = $"{hash}  RavUninstallerTool-v1.1.0-win-x64.zip\n{new string('0', 64)} *other.zip\n";
    Check(UpdateService.ParseSha256(sums, "RavUninstallerTool-v1.1.0-win-x64.zip") == hash, "ParseSha256 found");
    Check(UpdateService.ParseSha256(sums, "other.zip") == new string('0', 64), "ParseSha256 star");
    Check(UpdateService.ParseSha256(sums, "missing.zip") == null, "ParseSha256 missing");
    Check(UpdateService.VerifySha256(file, hash), "VerifySha256 ok");
    Check(UpdateService.VerifySha256(file, hash.ToUpperInvariant()), "VerifySha256 case-insens");
    Check(!UpdateService.VerifySha256(file, new string('0', 64)), "VerifySha256 mismatch");

    // ExtractExe valid
    string zipOk = Path.Combine(tmp, "ok.zip");
    using (var z = ZipFile.Open(zipOk, ZipArchiveMode.Create))
    {
        var e = z.CreateEntry("RavUninstallerTool.exe");
        using var w = new StreamWriter(e.Open()); w.Write("MZfake");
    }
    string outDir = Path.Combine(tmp, "out");
    string exe = UpdateService.ExtractExe(zipOk, outDir);
    Check(File.Exists(exe) && Path.GetFileName(exe) == "RavUninstallerTool.exe" && File.ReadAllText(exe) == "MZfake", "ExtractExe valid");

    // Zip slip
    string zipBad = Path.Combine(tmp, "bad.zip");
    using (var z = ZipFile.Open(zipBad, ZipArchiveMode.Create))
    {
        using (var w = new StreamWriter(z.CreateEntry("../evil.txt").Open())) w.Write("x");
        using (var w2 = new StreamWriter(z.CreateEntry("RavUninstallerTool.exe").Open())) w2.Write("x");
    }
    bool threw = false;
    try { UpdateService.ExtractExe(zipBad, Path.Combine(tmp, "out2")); } catch (InvalidDataException) { threw = true; }
    Check(threw, "ExtractExe zip slip rejected");
    Check(!File.Exists(Path.Combine(tmp, "evil.txt")), "zip slip no file written");

    // Missing exe
    string zipNone = Path.Combine(tmp, "none.zip");
    using (var z = ZipFile.Open(zipNone, ZipArchiveMode.Create))
    {
        var e = z.CreateEntry("readme.txt");
        using var w = new StreamWriter(e.Open()); w.Write("x");
    }
    threw = false;
    try { UpdateService.ExtractExe(zipNone, Path.Combine(tmp, "out3")); } catch (FileNotFoundException) { threw = true; }
    Check(threw, "ExtractExe missing exe");
}
finally
{
    try { Directory.Delete(tmp, true); } catch { }
}

// Localization parity
string[] keys = { "update_confirm_install", "update_downloading", "update_verifying", "update_applying", "update_apply_failed" };
var enField = typeof(LocalizationService).GetField("En", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var viField = typeof(LocalizationService).GetField("Vi", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var en = (Dictionary<string, string>)enField.GetValue(null)!;
var vi = (Dictionary<string, string>)viField.GetValue(null)!;
foreach (var k in keys) Check(en.ContainsKey(k) && vi.ContainsKey(k), "loc key " + k);
Check(en.Keys.OrderBy(x => x).SequenceEqual(vi.Keys.OrderBy(x => x)), "EN/VI key sets match");

if (failures == 0) { Console.WriteLine("PASS"); return 0; }
Console.WriteLine($"{failures} failure(s)");
return 1;
