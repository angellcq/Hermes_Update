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

        // M2（D-12）：分级网络策略开关；DataContract 对旧配置缺失字段用构造函数默认值，
        // 老配置 Load() 后二者恒为 true。
        [DataMember(Name = "preferDirectL0")] public bool PreferDirectL0 { get; set; }
        [DataMember(Name = "enableL1")] public bool EnableL1 { get; set; }

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
            PreferDirectL0 = true;
            EnableL1 = true;
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
