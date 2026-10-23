using System;
using System.IO;

namespace HermesInstaller.Core
{
    public static class HermesPaths
    {
        public static string HermesHome
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("HERMES_HOME");
                if (!string.IsNullOrWhiteSpace(env))
                {
                    return env;
                }
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "hermes");
            }
        }

        public static string InstallDir { get { return Path.Combine(HermesHome, "hermes-agent"); } }
        public static string BinDir { get { return Path.Combine(HermesHome, "bin"); } }
        public static string HermesCmd { get { return Path.Combine(BinDir, "hermes.cmd"); } }
        public static string LogsDir { get { return Path.Combine(HermesHome, "logs"); } }
        public static string InstallLog { get { return Path.Combine(LogsDir, "install.log"); } }
        public static string BootstrapLog { get { return Path.Combine(LogsDir, "bootstrap-installer.log"); } }
        public static string InstallStamp { get { return Path.Combine(InstallDir, "install-stamp.json"); } }
        public static string ConfigFile { get { return Path.Combine(HermesHome, "config.yaml"); } }

        public static string DesktopExe
        {
            get
            {
                return Path.Combine(InstallDir, "apps", "desktop", "release", "win-unpacked", "Hermes.exe");
            }
        }

        public static string DesktopReleaseDir
        {
            get { return Path.Combine(InstallDir, "apps", "desktop", "release"); }
        }

        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HermesInstaller");
            }
        }

        public static string CacheDir { get { return Path.Combine(AppDataDir, "cache"); } }
        public static string CachedInstallerScript { get { return Path.Combine(CacheDir, "install.ps1"); } }
        public static string SettingsPath { get { return Path.Combine(AppDataDir, "settings.json"); } }
        public static string LogDirectory { get { return Path.Combine(AppDataDir, "logs"); } }

        public static bool InstallLooksPresent
        {
            get { return Directory.Exists(InstallDir) && File.Exists(Path.Combine(InstallDir, ".git")); }
        }

        public static bool CliLauncherPresent
        {
            get { return File.Exists(HermesCmd); }
        }

        public static bool DesktopBuilt
        {
            get { return File.Exists(DesktopExe); }
        }

        public static string LocalAppDataRoot
        {
            get
            {
                return Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            }
        }

        public static void EnsureAppData()
        {
            Directory.CreateDirectory(AppDataDir);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(LogDirectory);
        }
    }
}
