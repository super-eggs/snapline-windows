using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Snapline
{
    internal sealed class ReleaseUpdates
    {
        internal const string AppMutex = @"Local\Snapline.Windows";
        private const string Repository = "super-eggs/snapline-windows";
        private const string Releases = "https://github.com/" + Repository + "/releases/";
        private const string LatestRelease = "https://api.github.com/repos/" + Repository + "/releases/latest";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{BBFEDE84-A60E-4F11-9CC5-1EE29BA57425}_is1";
        private const int MaxInstallerBytes = 100 * 1024 * 1024;

        [DataContract]
        internal sealed class Release
        {
            [DataMember(Name = "tag_name")] internal string Tag { get; set; }
            [DataMember(Name = "draft")] internal bool Draft { get; set; }
            [DataMember(Name = "prerelease")] internal bool Prerelease { get; set; }
            [DataMember(Name = "body")] internal string Body { get; set; }
            [DataMember(Name = "assets")] internal Asset[] Assets { get; set; }

            internal Asset Package(bool installed)
            {
                string version = Tag.Substring(1);
                string name = installed ? "Snapline-Setup-" + version + "-x64.exe" : "Snapline-Portable-" + version + "-x64.zip";
                string expectedUrl = Releases + "download/" + Tag + "/" + name;
                return Assets == null ? null : Assets.FirstOrDefault(asset => asset != null && asset.Name == name
                    && asset.State == "uploaded" && asset.Url == expectedUrl);
            }

            internal string Notes()
            {
                var lines = (Body ?? "").Split('\n').Select(line => line.Trim());
                return string.Join("\n", lines.SkipWhile(line => line != "## 中文更新").Skip(1)
                    .TakeWhile(line => !line.StartsWith("#", StringComparison.Ordinal))
                    .Where(line => line.StartsWith("- ", StringComparison.Ordinal)).Take(6)
                    .Select(line => "• " + line.Substring(2, Math.Min(120, line.Length - 2))));
            }
        }

        [DataContract]
        internal sealed class Asset
        {
            [DataMember(Name = "name")] internal string Name { get; set; }
            [DataMember(Name = "state")] internal string State { get; set; }
            [DataMember(Name = "browser_download_url")] internal string Url { get; set; }
            [DataMember(Name = "size")] internal long Size { get; set; }
            [DataMember(Name = "digest")] internal string Digest { get; set; }
        }

        internal static string InstalledDirectory(string runningExe)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var path = key == null ? null : key.GetValue("Inno Setup: App Path") as string;
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return null;
                string registeredExe = Path.GetFullPath(Path.Combine(path, "Snapline.exe"));
                return string.Equals(registeredExe, runningExe, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(registeredExe) : null;
            }
        }

        internal static async Task<Release> CheckAsync(Version current)
        {
            using (var client = CreateClient())
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.MaxResponseContentBufferSize = 1024 * 1024;
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await client.GetAsync(LatestRelease).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode == 403)
                        throw new InvalidDataException("GitHub 暂时限制了更新查询，请稍后重试。");
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(stream);
                        return NewerRelease(release, current);
                    }
                }
            }
        }

        internal static Release NewerRelease(Release release, Version current)
        {
            if (release == null || release.Draft || release.Prerelease || release.Tag == null
                || !Regex.IsMatch(release.Tag, @"\Av(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z"))
                throw new InvalidDataException("GitHub 返回的正式版本信息无效，请稍后重试。");
            Version version;
            if (!Version.TryParse(release.Tag.Substring(1), out version))
                throw new InvalidDataException("GitHub 返回的版本号无效，请稍后重试。");
            return version > new Version(current.Major, current.Minor, current.Build) ? release : null;
        }

        internal static async Task<VerifiedInstaller> DownloadAsync(Asset asset)
        {
            if (asset.Digest == null || !Regex.IsMatch(asset.Digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("这个安装包缺少有效的 SHA-256 校验值，请稍后重试。");
            if (asset.Size <= 0 || asset.Size > MaxInstallerBytes)
                throw new InvalidDataException("安装包大小异常，请稍后重试。");

            using (var client = CreateClient())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            using (var response = await GetAssetAsync(client, new Uri(asset.Url), timeout.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string directory = Path.Combine(Path.GetTempPath(), "Snapline");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "Snapline-Update.exe");
                FileStream file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 81920, true);
                try
                {
                    using (file)
                    using (var download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var buffer = new byte[81920];
                        int read;
                        // Disposing the body interrupts a stalled network read on .NET Framework.
                        using (timeout.Token.Register(download.Dispose))
                            while ((read = await download.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                            {
                                if (file.Position + read > asset.Size)
                                    throw new InvalidDataException("安装包大小与发布记录不符。");
                                await file.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                            }
                        await file.FlushAsync(timeout.Token).ConfigureAwait(false);
                    }
                    file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    VerifyInstaller(file, asset.Size, asset.Digest);
                    return new VerifiedInstaller(file);
                }
                catch
                {
                    file.Dispose();
                    TryDelete(path);
                    throw;
                }
            }
        }

        internal static void VerifyInstaller(Stream file, long size, string digest)
        {
            file.Position = 0;
            using (var sha256 = SHA256.Create())
            {
                string hash = "sha256:" + BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", "");
                if (file.Length != size || !string.Equals(hash, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包校验失败，请重新检查更新。");
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Snapline/" + typeof(App).Assembly.GetName().Version.ToString(3));
            return client;
        }

        private static async Task<HttpResponseMessage> GetAssetAsync(HttpClient client, Uri uri, CancellationToken cancellation)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                    || (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com"))
                    throw new InvalidDataException("安装包下载地址异常。");
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) return response;
                using (response)
                {
                    if (response.Headers.Location == null) break;
                    uri = new Uri(uri, response.Headers.Location);
                }
            }
            throw new InvalidDataException("安装包下载地址暂不可用。");
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal sealed class VerifiedInstaller : IDisposable
        {
            private readonly FileStream file;
            private bool started;

            // Keep the verified file open without sharing writes through the installer handoff.
            internal VerifiedInstaller(FileStream file) { this.file = file; }

            internal void Start(string installedDirectory)
            {
                string directory = installedDirectory.EndsWith("\\", StringComparison.Ordinal) ? installedDirectory + "\\" : installedDirectory;
                string arguments = "/SNAPLINEUPDATE=1 /SP- /NOCLOSEAPPLICATIONS /NORESTART /DIR=\"" + directory + "\"";
                using (var process = Process.Start(new ProcessStartInfo(file.Name, arguments) { UseShellExecute = true }))
                {
                    if (process == null) throw new IOException("安装向导未启动。");
                    started = true;
                }
            }

            public void Dispose()
            {
                string path = file.Name;
                file.Dispose();
                if (!started) TryDelete(path);
            }
        }
    }
}
