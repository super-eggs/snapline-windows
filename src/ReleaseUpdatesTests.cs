using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snapline
{
    internal static class ReleaseUpdatesTests
    {
        internal static void Run(Action<bool, string> check)
        {
            const string downloads = "https://github.com/super-eggs/snapline-windows/releases/download/v1.0.10/";
            const string setup = "Snapline-Setup-1.0.10-x64.exe";
            const string zip = "Snapline-Portable-1.0.10-x64.zip";
            var json = "{\"tag_name\":\"v1.0.10\",\"body\":\"## 中文更新\\n- 手动检查更新\\n## English updates\\n- Manual checks\",\"assets\":["
                + "{\"name\":\"" + setup + "\",\"state\":\"uploaded\",\"browser_download_url\":\"" + downloads + setup + "\"},"
                + "{\"name\":\"" + zip + "\",\"state\":\"uploaded\",\"browser_download_url\":\"" + downloads + zip + "\"}]}";
            ReleaseUpdates.Release release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                release = (ReleaseUpdates.Release)new DataContractJsonSerializer(typeof(ReleaseUpdates.Release)).ReadObject(stream);
            check(ReleaseUpdates.NewerRelease(release, new Version("1.0.9.0")) == release
                && ReleaseUpdates.NewerRelease(release, new Version("1.0.10.0")) == null
                && ReleaseUpdates.NewerRelease(release, new Version("1.1.0.0")) == null,
                "update compares numeric versions and offers neither an equal version nor a downgrade");
            check(release.Notes() == "• 手动检查更新" && release.Package(true).Url == downloads + setup
                && release.Package(false).Url == downloads + zip, "update reads Chinese release notes and selects the correct published x64 asset");

            release.Assets[0].Url = "https://example.com/" + setup;
            release.Assets[1].State = "open";
            check(release.Package(true) == null && release.Package(false) == null, "update rejects a foreign URL and a pending asset");
            release.Prerelease = true;
            bool rejected = false;
            try { ReleaseUpdates.NewerRelease(release, new Version("1.0.9.0")); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "update rejects a prerelease from the stable endpoint");

            const string digest = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            using (var file = new MemoryStream(Encoding.ASCII.GetBytes("abc"))) ReleaseUpdates.VerifyInstaller(file, 3, digest);
            rejected = false;
            try
            {
                using (var file = new MemoryStream(Encoding.ASCII.GetBytes("abd"))) ReleaseUpdates.VerifyInstaller(file, 3, digest);
            }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "installer verification accepts matching SHA-256 and rejects a same-size modified payload");

            VerifyRateLimitedCheck(check);
        }

        private static void VerifyRateLimitedCheck(Action<bool, string> check)
        {
            const string api = "https://api.github.com/repos/super-eggs/snapline-windows/releases/latest";
            const string releases = "https://github.com/super-eggs/snapline-windows/releases/";
            const string downloads = releases + "download/v1.0.12/";
            const string latest = releases + "latest/download/SHA256SUMS.txt";
            const string manifestUrl = downloads + "SHA256SUMS.txt";
            const string cdn = "https://release-assets.githubusercontent.com/test/checksums";
            const string setup = "Snapline-Setup-1.0.12-x64.exe";
            const string zip = "Snapline-Portable-1.0.12-x64.zip";
            string setupHash = new string('a', 64), zipHash = new string('b', 64);
            string checksums = setupHash + "  " + setup + "\r\n" + zipHash + "  " + zip + "\r\n";
            string target = manifestUrl;
            string manifest = checksums;
            int status = 403;
            var calls = new List<string>();
            using (var client = new HttpClient(new UpdateTransport(delegate(string url)
            {
                calls.Add(url);
                if (url == api) return new HttpResponseMessage((HttpStatusCode)status);
                if (url == latest || url == manifestUrl)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new Uri(url == latest ? target : cdn);
                    return response;
                }
                if (url == cdn) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) };
                throw new InvalidOperationException("Unexpected updater request " + url);
            })))
            {
                var current = new Version("1.0.10.0");
                var release = ReleaseUpdates.CheckLatestAsync(client, current).GetAwaiter().GetResult();
                var installer = release.Package(true);
                var portable = release.Package(false);
                check(release.Tag == "v1.0.12" && installer.Url == downloads + setup
                    && installer.FromChecksumManifest && installer.Digest == "sha256:" + setupHash
                    && portable.Url == downloads + zip && portable.Digest == "sha256:" + zipHash
                    && calls.Count == 4, "a GitHub 403 falls back to the stable release and its official versioned checksums");

                status = 429;
                calls.Clear();
                var latestInstalled = ReleaseUpdates.CheckLatestAsync(client, new Version("1.0.12.0")).GetAwaiter().GetResult();
                check(latestInstalled == null && calls.Count == 2, "a GitHub 429 recognizes an already current version without downloading checksums");

                target = "https://example.com/super-eggs/snapline-windows/releases/download/v1.0.12/SHA256SUMS.txt";
                calls.Clear();
                bool rejected = false;
                try { ReleaseUpdates.CheckLatestAsync(client, current).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && calls.Count == 2, "the updater refuses a latest-release redirect outside the official repository");

                target = manifestUrl;
                manifest = checksums + setupHash + "  " + setup + "\r\n";
                rejected = false;
                try { ReleaseUpdates.CheckLatestAsync(client, current).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected, "the updater refuses duplicate package entries in the checksum manifest");
            }
        }

        private sealed class UpdateTransport : HttpMessageHandler
        {
            private readonly Func<string, HttpResponseMessage> respond;
            internal UpdateTransport(Func<string, HttpResponseMessage> respond) { this.respond = respond; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(respond(request.RequestUri.AbsoluteUri));
            }
        }
    }
}
