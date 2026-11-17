using System;
using System.IO;
using Microsoft.Win32;

namespace Snapline
{
    internal sealed class StartupRegistration
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Snapline";
        private readonly string keyPath;
        private readonly string command;

        internal StartupRegistration(string executablePath, string keyPath = RunKey)
        {
            this.keyPath = keyPath;
            command = "\"" + Path.GetFullPath(executablePath) + "\" --startup";
        }

        // The Windows entry is the saved setting, so external removals are respected.
        internal bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                    return key != null && !string.IsNullOrWhiteSpace(key.GetValue(ValueName) as string);
            }
        }

        internal void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (command.Length > 260) throw new NotSupportedException("程序路径过长，请将 Snapline 移到较短的固定路径后再启用开机自启。");
                using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                    key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                    if (key != null) key.DeleteValue(ValueName, false);
            }
        }
    }
}
