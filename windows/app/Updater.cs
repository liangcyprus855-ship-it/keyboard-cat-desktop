using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal sealed class GitHubAsset
{
    public string Name;
    public string DownloadUrl;
    public long Size;
}

internal sealed class GitHubRelease
{
    public string TagName;
    public string Name;
    public string Body;
    public string HtmlUrl;
    public bool Draft;
    public bool Prerelease;
    public readonly List<GitHubAsset> Assets = new List<GitHubAsset>();
}

internal static class Updater
{
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/" + KeycatCore.KC_GITHUB_REPO + "/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/" + KeycatCore.KC_GITHUB_REPO + "/releases/latest";
    private const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeycatDesktop.B48D8B";

    internal static void EnableModernTls()
    {
        try
        {
            // .NET 4.8 on Windows 7 SP1 may not enable TLS 1.2 without explicit flags.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        }
        catch (NotSupportedException) { }
        try
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; // TLS 1.3 when available
        }
        catch (NotSupportedException) { }
    }

    public static bool TryParseVersion(string text, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        if (String.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(1);
        int dash = trimmed.IndexOfAny(new char[] { '-', '+' });
        if (dash >= 0) trimmed = trimmed.Substring(0, dash);
        string[] parts = trimmed.Split('.');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
        if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
        return true;
    }

    public static bool IsNewerVersion(string remoteTag, string currentVersion)
    {
        int rMaj, rMin, rPat, cMaj, cMin, cPat;
        if (!TryParseVersion(remoteTag, out rMaj, out rMin, out rPat)) return false;
        if (!TryParseVersion(currentVersion, out cMaj, out cMin, out cPat)) return false;
        return KeycatCore.kc_compare_version_triples(rMaj, rMin, rPat, cMaj, cMin, cPat) > 0;
    }

    public static GitHubRelease ParseReleaseJson(string json)
    {
        if (String.IsNullOrWhiteSpace(json)) return null;
        var serializer = new JavaScriptSerializer();
        var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
        if (root == null) return null;
        var release = new GitHubRelease
        {
            TagName = StringField(root, "tag_name"),
            Name = StringField(root, "name"),
            Body = StringField(root, "body"),
            HtmlUrl = StringField(root, "html_url"),
            Draft = BoolField(root, "draft"),
            Prerelease = BoolField(root, "prerelease")
        };
        object assetsObj;
        if (root.TryGetValue("assets", out assetsObj) && assetsObj is object[])
        {
            foreach (object itemObj in (object[])assetsObj)
            {
                var item = itemObj as Dictionary<string, object>;
                if (item == null) continue;
                string name = StringField(item, "name");
                string url = StringField(item, "browser_download_url");
                if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(url)) continue;
                long size = 0;
                object sizeObj;
                if (item.TryGetValue("size", out sizeObj) && sizeObj != null)
                    long.TryParse(Convert.ToString(sizeObj, CultureInfo.InvariantCulture), out size);
                release.Assets.Add(new GitHubAsset { Name = name, DownloadUrl = url, Size = size });
            }
        }
        return release;
    }

    private static string StringField(Dictionary<string, object> dict, string key)
    {
        object value;
        return dict.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private static bool BoolField(Dictionary<string, object> dict, string key)
    {
        object value;
        return dict.TryGetValue(key, out value) && value is bool && (bool)value;
    }

    public static bool IsInstalledCopy(string executablePath)
    {
        if (String.IsNullOrEmpty(executablePath)) return false;
        string dir = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (File.Exists(Path.Combine(dir, "卸载键盘小黑猫.exe"))) return true;
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallRegKey, false))
            {
                string installDir = key != null ? key.GetValue("InstallLocation") as string : null;
                return AutoStart.SamePath(installDir, dir);
            }
        }
        catch { return false; }
    }

    public static GitHubAsset SelectWindowsAsset(GitHubRelease release, bool isInstalled)
    {
        if (release == null || release.Assets.Count == 0) return null;
        GitHubAsset installer = null;
        GitHubAsset portable = null;
        foreach (GitHubAsset asset in release.Assets)
        {
            if (!asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            bool looksLikeInstaller = asset.Name.IndexOf("安装包", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      asset.Name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
            if (looksLikeInstaller && installer == null) installer = asset;
            else if (!looksLikeInstaller && portable == null) portable = asset;
        }
        return isInstalled ? (installer ?? portable) : (portable ?? installer);
    }

    public static GitHubAsset SelectChecksumAsset(GitHubRelease release)
    {
        if (release == null) return null;
        foreach (GitHubAsset asset in release.Assets)
        {
            if (asset.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                return asset;
        }
        return null;
    }

    public static bool VerifySha256(string filePath, string checksumManifest, string expectedFileName)
    {
        if (String.IsNullOrWhiteSpace(checksumManifest) || String.IsNullOrWhiteSpace(expectedFileName)) return true;
        string expectedHash = null;
        foreach (string rawLine in checksumManifest.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 66) continue;
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string name = parts[parts.Length - 1].TrimStart('*');
                if (name.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(name).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    expectedHash = parts[0].Trim().ToLowerInvariant();
                    break;
                }
            }
        }
        if (expectedHash == null) return true;
        using (var sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(filePath))
        {
            byte[] digest = sha.ComputeHash(stream);
            var sb = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return String.Equals(sb.ToString(), expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static GitHubRelease FetchLatestRelease()
    {
        EnableModernTls();
        var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
        request.Method = "GET";
        request.UserAgent = "KeycatDesktop/" + KeycatCore.KC_APP_VERSION;
        request.Accept = "application/vnd.github+json";
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.Timeout = 8000;
        request.ReadWriteTimeout = 8000;
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return ParseReleaseJson(reader.ReadToEnd());
            }
        }
        catch (WebException ex)
        {
            var http = ex.Response as HttpWebResponse;
            if (http != null)
                throw new InvalidOperationException("GitHub 返回 HTTP " + (int)http.StatusCode + "（" + http.StatusDescription + "）。", ex);
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public static string DownloadString(string url)
    {
        EnableModernTls();
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.UserAgent = "KeycatDesktop/" + KeycatCore.KC_APP_VERSION;
        request.Timeout = 10000;
        using (var response = (HttpWebResponse)request.GetResponse())
        using (Stream stream = response.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    public static string DownloadAssetToTemp(GitHubAsset asset, GitHubAsset checksumAsset)
    {
        EnableModernTls();
        string safeName = Path.GetFileName(asset.Name);
        if (String.IsNullOrEmpty(safeName)) safeName = "KeycatUpdate.exe";
        string stage = Path.Combine(Path.GetTempPath(), "keycat-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string dest = Path.Combine(stage, safeName);
        using (var client = new WebClient())
        {
            client.Headers.Add(HttpRequestHeader.UserAgent, "KeycatDesktop/" + KeycatCore.KC_APP_VERSION);
            client.DownloadFile(asset.DownloadUrl, dest);
        }
        var info = new FileInfo(dest);
        if (!info.Exists || info.Length < 1024)
            throw new InvalidOperationException("下载的安装包文件不完整。");
        if (checksumAsset != null)
        {
            string sums = DownloadString(checksumAsset.DownloadUrl);
            if (!VerifySha256(dest, sums, asset.Name))
                throw new InvalidOperationException("安装包校验失败（SHA-256 不匹配）。");
        }
        return dest;
    }

    public static void LaunchDownloadedUpdate(string downloadedExe, bool isInstallerAsset, string currentExePath)
    {
        if (isInstallerAsset)
        {
            Process.Start(new ProcessStartInfo(downloadedExe) { UseShellExecute = true });
            return;
        }
        int pid = Process.GetCurrentProcess().Id;
        string scriptPath = Path.Combine(Path.GetDirectoryName(downloadedExe), "apply-update.ps1");
        string script =
            "$ErrorActionPreference = 'Stop'\n" +
            "$pidToWait = " + pid.ToString(CultureInfo.InvariantCulture) + "\n" +
            "for ($i = 0; $i -lt 80; $i++) {\n" +
            "    if (-not (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue)) { break }\n" +
            "    Start-Sleep -Milliseconds 150\n" +
            "}\n" +
            "$src = '" + downloadedExe.Replace("'", "''") + "'\n" +
            "$dst = '" + currentExePath.Replace("'", "''") + "'\n" +
            "$tmp = $dst + '.new'\n" +
            "Copy-Item -LiteralPath $src -Destination $tmp -Force\n" +
            "Move-Item -LiteralPath $tmp -Destination $dst -Force\n" +
            "Start-Process -FilePath $dst\n";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + scriptPath + "\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
