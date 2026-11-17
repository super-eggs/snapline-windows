using System;
using System.IO;
using Microsoft.Win32;

namespace Snapline
{
    internal static class StartupRegistrationTests
    {
        internal static void Run(string root, Action<bool, string> check)
        {
            // Exercise the real registry API outside Windows' executable startup locations.
            string keyPath = @"Software\Snapline-tests-" + Guid.NewGuid().ToString("N");
            string executable = Path.Combine(root, "中文 portable folder", "Snapline.exe");
            var registration = new StartupRegistration(executable, keyPath);
            try
            {
                check(!registration.Enabled, "startup defaults off without creating an entry");
                registration.SetEnabled(false);
                check(!registration.Enabled, "disabling absent startup registration is safe");
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("OtherApp", "preserved");
                registration.SetEnabled(true);
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    check((string)key.GetValue("Snapline") == "\"" + executable + "\" --startup" && key.GetValueKind("Snapline") == RegistryValueKind.String,
                        "startup command quotes spaces and Chinese paths and includes only the startup flag");
                check(new StartupRegistration(executable, keyPath).Enabled, "startup preference persists across instances");
                string movedExecutable = Path.Combine(root, "moved", "Snapline.exe");
                new StartupRegistration(movedExecutable, keyPath).SetEnabled(true);
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    check((string)key.GetValue("Snapline") == "\"" + movedExecutable + "\" --startup", "explicit enable updates executable location");
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true)) key.DeleteValue("Snapline");
                check(!registration.Enabled, "startup preference reflects external removal without re-enabling itself");
                registration.SetEnabled(true); registration.SetEnabled(false); registration.SetEnabled(false);
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    check(key.GetValue("Snapline") == null && (string)key.GetValue("OtherApp") == "preserved", "disable removes only Snapline and can be repeated");
                string longExecutable = Path.GetPathRoot(root) + new string('a', 246) + ".exe";
                bool rejected = false;
                try { new StartupRegistration(longExecutable, keyPath).SetEnabled(true); }
                catch (NotSupportedException) { rejected = true; }
                check(rejected && !registration.Enabled, "overlong Windows startup commands fail without recording enabled state");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
        }
    }
}
