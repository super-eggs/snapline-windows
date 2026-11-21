using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

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
        }
    }
}
