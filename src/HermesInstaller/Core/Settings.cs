using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HermesInstaller.Core
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "channel")] public string Channel { get; set; }
        [DataMember(Name = "skipBrowser")] public bool SkipBrowser { get; set; }
        [DataMember(Name = "skipComputerUse")] public bool SkipComputerUse { get; set; }
        [DataMember(Name = "autoDetectMirrors")] public bool AutoDetectMirrors { get; set; }
        [DataMember(Name = "repoMirror")] public string RepoMirror { get; set; }
        [DataMember(Name = "electronMirror")] public string ElectronMirror { get; set; }
        [DataMember(Name = "builderMirror")] public string BuilderMirror { get; set; }
        [DataMember(Name = "npmRegistry")] public string NpmRegistry { get; set; }
        [DataMember(Name = "pipIndex")] public string PipIndex { get; set; }
        [DataMember(Name = "allowHostsFix")] public bool AllowHostsFix { get; set; }
        [DataMember(Name = "backupBeforeUpdate")] public bool BackupBeforeUpdate { get; set; }
        [DataMember(Name = "lastOperation")] public string LastOperation { get; set; }

        // E12：M2 留下的 preferDirectL0 / enableL1 两个开关全仓零引用（探针与分级都没读它们），
        // 已删除。旧 settings.json 里残留的同名键会被 DataContract 忽略，不影响读取。

        public Settings()
        {
            Channel = "stable";
            AutoDetectMirrors = true;
            RepoMirror = string.Empty;
            ElectronMirror = Mirrors.ElectronMirror;
            BuilderMirror = Mirrors.BuilderMirror;
            NpmRegistry = Mirrors.NpmRegistry;
            PipIndex = Mirrors.PipIndex;
            AllowHostsFix = false;
            BackupBeforeUpdate = false;
        }

        public static Settings Load()
        {
            try
            {
                if (!File.Exists(HermesPaths.SettingsPath))
                {
                    return new Settings();
                }
                using (var stream = File.OpenRead(HermesPaths.SettingsPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(Settings));
                    var loaded = serializer.ReadObject(stream) as Settings;
                    if (loaded == null)
                    {
                        return new Settings();
                    }
                    if (string.IsNullOrEmpty(loaded.Channel))
                    {
                        loaded.Channel = "stable";
                    }
                    return loaded;
                }
            }
            catch
            {
                return new Settings();
            }
        }

        public void Save()
        {
            try
            {
                HermesPaths.EnsureAppData();
                string temp = HermesPaths.SettingsPath + ".tmp";
                using (var stream = File.Create(temp))
                {
                    var serializer = new DataContractJsonSerializer(typeof(Settings));
                    serializer.WriteObject(stream, this);
                }
                if (File.Exists(HermesPaths.SettingsPath))
                {
                    File.Delete(HermesPaths.SettingsPath);
                }
                File.Move(temp, HermesPaths.SettingsPath);
            }
            catch
            {
                // Settings are best-effort; never block the UI.
            }
        }
    }
}
