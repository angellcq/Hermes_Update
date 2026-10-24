using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>fingerprint.json 的 schema（DD §3.1）：指纹三元组 + 模式版本。</summary>
    [DataContract]
    public sealed class FingerprintRecord
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "gitHead")] public string GitHead { get; set; }
        [DataMember(Name = "lockSha256")] public string LockSha256 { get; set; }
        [DataMember(Name = "cliVersion")] public string CliVersion { get; set; }
        [DataMember(Name = "capturedAt")] public string CapturedAt { get; set; }
    }

    /// <summary>
    /// 版本指纹（DD §2.2 G3，T-HU-DEV-4）。
    /// 口径红线（DD 偏差 D-1）：IsSame 只用于「同一次操作内的防重复」（RunUpgradeAsync 里
    /// fpBefore/fpAfter 的本轮对比），绝不用于「跨会话判断是否已是最新」；跨会话的
    /// 「已是最新」由官方 hermes update --yes 自身幂等（Already up to date!）保证。
    /// LoadLast/SaveLast 只把最近一次指纹落盘到 state\fingerprint.json 供排障与后续版本使用，
    /// 调用方不得据 LoadLast 的结果提前 return——否则远端有新版本时用户重跑也永远升不了级。
    /// 任一子项取不到时该字段为 null，CaptureAsync 整体不返回 null；IsSame 对任一 null 恒为 false
    /// （指纹信息不完整 → 照常执行，绝不因采集失败而误判「无变化」）。
    /// </summary>
    public sealed class VersionFingerprint
    {
        private readonly Logger _logger;
        private readonly ProcessRunner _runner;
        private readonly InstallerEngine _engine;

        public VersionFingerprint(Logger logger, ProcessRunner runner, InstallerEngine engine)
        {
            _logger = logger;
            _runner = runner;
            _engine = engine;
        }

        /// <summary>采集当前指纹；子项失败时该字段为 null + 记 Warn，整体不为 null。</summary>
        public async Task<FingerprintRecord> CaptureAsync(CancellationToken cancellationToken)
        {
            var record = new FingerprintRecord();
            record.SchemaVersion = 1;
            record.GitHead = await CaptureGitHeadAsync(cancellationToken).ConfigureAwait(false);
            record.LockSha256 = CaptureLockSha256();
            record.CliVersion = _engine != null
                ? await _engine.GetVersionAsync(cancellationToken).ConfigureAwait(false)
                : null;
            record.CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _logger.Info("[FP] 指纹采集完成: gitHead=" + OrEmpty(record.GitHead)
                + ", lockSha256=" + OrEmpty(record.LockSha256)
                + ", cliVersion=" + OrEmpty(record.CliVersion));
            return record;
        }

        /// <summary>
        /// 指纹判等（DD §2.2 G3）：三元组（gitHead/lockSha256/cliVersion）非空且全等才算相同；
        /// 任一参数为 null、或任一字段为空 → false（宁多跑一遍，不可漏升级）。
        /// 只许在同一次操作内调用，用于防重复；跨会话跳过属禁用口径（DD 偏差 D-1）。
        /// </summary>
        public static bool IsSame(FingerprintRecord a, FingerprintRecord b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            return SameText(a.GitHead, b.GitHead)
                && SameText(a.LockSha256, b.LockSha256)
                && SameText(a.CliVersion, b.CliVersion);
        }

        /// <summary>读 state\fingerprint.json（仅供排障对比；不得据此跳过 update，见 DD 偏差 D-1）。</summary>
        public FingerprintRecord LoadLast()
        {
            try
            {
                if (!File.Exists(HermesPaths.FingerprintPath))
                {
                    return null;
                }
                using (var stream = File.OpenRead(HermesPaths.FingerprintPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(FingerprintRecord));
                    var record = serializer.ReadObject(stream) as FingerprintRecord;
                    if (record == null)
                    {
                        return null;
                    }
                    if (record.SchemaVersion > 1)
                    {
                        _logger.Warn("[FP] fingerprint.json schemaVersion=" + record.SchemaVersion
                            + " 高于当前理解（1），忽略既有指纹。");
                        return null;
                    }
                    return record;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("[FP] 读取 fingerprint.json 失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>先写 .tmp 再替换（照抄 Settings.Save 模式）；失败只记 Warn，绝不影响主流程。</summary>
        public void SaveLast(FingerprintRecord record)
        {
            if (record == null)
            {
                return;
            }
            try
            {
                HermesPaths.EnsureAppData();
                string temp = HermesPaths.FingerprintPath + ".tmp";
                using (var stream = File.Create(temp))
                {
                    var serializer = new DataContractJsonSerializer(typeof(FingerprintRecord));
                    serializer.WriteObject(stream, record);
                }
                if (File.Exists(HermesPaths.FingerprintPath))
                {
                    File.Delete(HermesPaths.FingerprintPath);
                }
                File.Move(temp, HermesPaths.FingerprintPath);
            }
            catch (Exception ex)
            {
                _logger.Warn("[FP] 写入 fingerprint.json 失败: " + ex.Message);
            }
        }

        /// <summary>git -C &lt;InstallDir&gt; rev-parse HEAD（DD 冻结：Generic 短化实例，总预算 15s）。
        /// 不以 InstallLooksPresent 作前置闸门：直接执行 git，只有在 git 真失败（退出码非 0 /
        /// 无输出 / 异常）时才置空——避免目录形态误判导致 gitHead 恒为 null。</summary>
        private async Task<string> CaptureGitHeadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var options = new ProcessRunOptions
                {
                    Class = OperationClass.Generic,
                    NoProgressTimeout = null,
                    TotalTimeout = TimeSpan.FromSeconds(15),
                    PollIntervalMs = 500
                };
                string arguments = "-C " + ProcessRunner.QuoteArg(HermesPaths.InstallDir) + " rev-parse HEAD";
                ProcessResult result = await _runner.RunProcessAsync(
                    "git.exe", arguments, HermesPaths.HermesHome, null, null,
                    cancellationToken, null, options).ConfigureAwait(false);
                if (result.Cancelled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                string head = FirstLine(result.StandardOutput);
                if (result.ExitCode != 0 || head == null)
                {
                    _logger.Warn("[FP] git rev-parse HEAD 失败（退出码 " + result.ExitCode + "），gitHead 置空。");
                    return null;
                }
                return head;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn("[FP] git rev-parse HEAD 异常: " + ex.Message);
                return null;
            }
        }

        /// <summary>读 &lt;InstallDir&gt;\pm\lock.json 全字节 → SHA256 小写 hex；文件缺失 → null + Warn。</summary>
        private string CaptureLockSha256()
        {
            string path = Path.Combine(HermesPaths.InstallDir, "pm", "lock.json");
            try
            {
                if (!File.Exists(path))
                {
                    _logger.Warn("[FP] lock.json 不存在（" + path + "），lockSha256 置空。");
                    return null;
                }
                byte[] bytes = File.ReadAllBytes(path);
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("[FP] 读取 lock.json 失败: " + ex.Message);
                return null;
            }
        }

        private static bool SameText(string x, string y)
        {
            return !string.IsNullOrEmpty(x)
                && !string.IsNullOrEmpty(y)
                && string.Equals(x, y, StringComparison.Ordinal);
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : null;
        }

        private static string OrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }
    }
}
