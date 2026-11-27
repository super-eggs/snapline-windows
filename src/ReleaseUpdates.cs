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
        private const string ChecksumsFile = "SHA256SUMS.txt";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{BBFEDE84-A60E-4F11-9CC5-1EE29BA57425}_is1";

        [DataContract]
        internal sealed class Release
        {
            [DataMember(Name = "tag_name")] internal string Tag { get; set; }
            [DataMember(Name = "draft")] internal bool Draft { get; set; }
            [DataMember(Name = "prerelease")] internal bool Prerelease { get; set; }
            [DataMember(Name = "body")] internal string Body { get; set; }
            [DataMember(Name = "assets")] internal Asset[] Assets { get; set; }

            internal Asset Package(bool installed, bool ocr)
            {
                string name = PackageName(Tag, installed, ocr);
                string expectedUrl = AssetUrl(Tag, name);
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
            internal bool FromChecksumManifest { get; set; }
        }

        private static string PackageName(string tag, bool installed, bool ocr)
        {
            string prefix = ocr ? "Snapline-OCR-" : "Snapline-";
            string version = tag.Substring(1);
            return installed ? prefix + "Setup-" + version + "-x64.exe" : prefix + "Portable-" + version + "-x64.zip";
        }

        private static string AssetUrl(string tag, string name)
        {
            return Releases + "download/" + tag + "/" + name;
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
                return await CheckLatestAsync(client, current).ConfigureAwait(false);
            }
        }

        internal static async Task<Release> CheckLatestAsync(HttpClient client, Version current)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode != 429 && (int)response.StatusCode != 403)
                    {
                        response.EnsureSuccessStatusCode();
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            var release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(stream);
                            return NewerRelease(release, current);
                        }
                    }
                }
            }
            return await CheckPublishedDownloadsAsync(client, current).ConfigureAwait(false);
        }

        private static async Task<Release> CheckPublishedDownloadsAsync(HttpClient client, Version current)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var latest = new Uri(Releases + "latest/download/" + ChecksumsFile);
                Uri manifestUrl;
                using (var response = await client.GetAsync(latest, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!IsRedirect(response) || response.Headers.Location == null)
                        throw new InvalidDataException("暂时无法从 GitHub 确认最新版本，请前往 Snapline 发布页查看。");
                    manifestUrl = new Uri(latest, response.Headers.Location);
                }

                // The official latest-asset link redirects to the stable release's versioned manifest.
                var match = Regex.Match(manifestUrl.AbsoluteUri,
                    @"\A" + Regex.Escape(Releases + "download/") + @"([^/?#]+)/" + Regex.Escape(ChecksumsFile) + @"\z");
                if (!match.Success) throw new InvalidDataException("GitHub 返回的更新校验文件地址异常。");
                var release = NewerRelease(new Release { Tag = match.Groups[1].Value }, current);
                if (release == null) return null;

                string manifest;
                using (var response = await GetAssetAsync(client, manifestUrl, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (timeout.Token.Register(response.Dispose))
                    {
                        await response.Content.LoadIntoBufferAsync(4096).ConfigureAwait(false);
                        manifest = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                release.Assets = new[]
                {
                    ManifestPackage(release.Tag, manifest, true, false), ManifestPackage(release.Tag, manifest, false, false),
                    ManifestPackage(release.Tag, manifest, true, true), ManifestPackage(release.Tag, manifest, false, true)
                };
                return release;
            }
        }

        private static Asset ManifestPackage(string tag, string manifest, bool installed, bool ocr)
        {
            string name = PackageName(tag, installed, ocr);
            var matches = Regex.Matches(manifest, @"(?m)^([0-9a-fA-F]{64})  " + Regex.Escape(name) + @"\r?$");
            if (ocr && matches.Count == 0) return null;
            if (matches.Count != 1) throw new InvalidDataException("GitHub 发布的 SHA-256 校验文件不完整或存在冲突。");
            return new Asset
            {
                Name = name, State = "uploaded", Url = AssetUrl(tag, name),
                Digest = "sha256:" + matches[0].Groups[1].Value, FromChecksumManifest = true
            };
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

        internal static async Task<VerifiedInstaller> DownloadAsync(Asset asset, bool ocr)
        {
            int maxInstallerBytes = (ocr ? 256 : 100) * 1024 * 1024;
            if (asset.Digest == null || !Regex.IsMatch(asset.Digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("这个安装包缺少有效的 SHA-256 校验值，请稍后重试。");
            if (!asset.FromChecksumManifest && (asset.Size <= 0 || asset.Size > maxInstallerBytes))
                throw new InvalidDataException("安装包大小异常，请稍后重试。");

            using (var client = CreateClient())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(ocr ? 10 : 3)))
            using (var response = await GetAssetAsync(client, new Uri(asset.Url), timeout.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                // The published checksum file has hashes only. Use the official download's byte length.
                long size = asset.FromChecksumManifest ? response.Content.Headers.ContentLength.GetValueOrDefault() : asset.Size;
                if (size <= 0 || size > maxInstallerBytes)
                    throw new InvalidDataException("安装包大小异常，请稍后重试。");
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
                                if (file.Position + read > size)
                                    throw new InvalidDataException("安装包大小与发布记录不符。");
                                await file.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                            }
                        await file.FlushAsync(timeout.Token).ConfigureAwait(false);
                    }
                    file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    VerifyInstaller(file, size, asset.Digest);
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

        private static bool IsRedirect(HttpResponseMessage response)
        {
            int status = (int)response.StatusCode;
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        private static async Task<HttpResponseMessage> GetAssetAsync(HttpClient client, Uri uri, CancellationToken cancellation)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                    || (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com"))
                    throw new InvalidDataException("安装包下载地址异常。");
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                if (!IsRedirect(response)) return response;
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
