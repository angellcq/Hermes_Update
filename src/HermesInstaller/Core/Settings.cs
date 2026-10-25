using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

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

        // E14（SSH 代理）：镜像全灭时的最后一条出网通道。
        // 结构：本机 ssh.exe 起 SOCKS5（-D），再由 LocalSocksHttpProxy 桥成 HTTP 代理，
        // 供 HttpClient / git / npm / pip / electron 共用。密码用 DPAPI（当前用户）加密后落盘。
        [DataMember(Name = "sshProxyEnabled")] public bool SshProxyEnabled { get; set; }
        [DataMember(Name = "sshHost")] public string SshHost { get; set; }
        [DataMember(Name = "sshPort")] public int SshPort { get; set; }
        [DataMember(Name = "sshUser")] public string SshUser { get; set; }
        [DataMember(Name = "sshSocksPort")] public int SshSocksPort { get; set; }
        [DataMember(Name = "sshHttpPort")] public int SshHttpPort { get; set; }
        [DataMember(Name = "sshPasswordProtected")] public string SshPasswordProtected { get; set; }

        // E12：M2 留下的 preferDirectL0 / enableL1 两个开关全仓零引用（探针与分级都没读它们），
        // 已删除。旧 settings.json 里残留的同名键会被 DataContract 忽略，不影响读取。

        /// <summary>内存态明文密码（无 [DataMember]，不落盘）。</summary>
        public string SshPassword { get; set; }

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
            SshProxyEnabled = false;
            SshHost = string.Empty;
            SshPort = 22;
            SshUser = "root";
            SshSocksPort = 1080;
            SshHttpPort = 1081;
            SshPasswordProtected = string.Empty;
        }

        /// <summary>
        /// E14：写入 SSH 密码。DPAPI（CurrentUser）加密后进 settings.json，
        /// 明文只留在内存；加密失败也保留内存值（本次会话可用，重启后失效）。
        /// </summary>
        public void SetSshPassword(string plain)
        {
            SshPassword = plain ?? string.Empty;
            if (string.IsNullOrEmpty(plain))
            {
                SshPasswordProtected = string.Empty;
                return;
            }
            try
            {
                byte[] cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                SshPasswordProtected = Convert.ToBase64String(cipher);
            }
            catch
            {
                SshPasswordProtected = string.Empty;
            }
        }

        /// <summary>
        /// E14：取 SSH 密码。内存优先；否则解 DPAPI。解不开（换机器/换用户/DPAPI 受损）
        /// 返回空串，由调用方提示重填——不抛异常。
        /// </summary>
        public string GetSshPassword()
        {
            if (!string.IsNullOrEmpty(SshPassword))
            {
                return SshPassword;
            }
            if (string.IsNullOrEmpty(SshPasswordProtected))
            {
                return string.Empty;
            }
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(SshPasswordProtected), null, DataProtectionScope.CurrentUser);
                SshPassword = Encoding.UTF8.GetString(plain);
                return SshPassword;
            }
            catch
            {
                SshPassword = string.Empty;
                return string.Empty;
            }
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
