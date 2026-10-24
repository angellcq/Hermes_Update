using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>单条断言的判定状态（DD §2.2 G2）。</summary>
    public enum AssertionStatus
    {
        Pass,
        Warn,
        Fail,
        Skipped
    }

    /// <summary>单条断言结果，风格对齐 ProbeItem（Models.cs）。</summary>
    public sealed class AssertionItem
    {
        public string Name { get; set; }
        public string Expected { get; set; }
        public string Actual { get; set; }
        public AssertionStatus Status { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// 四项断言的汇总。Outcome/Summary 由 Recompute() 按冻结判定刷新（DD §2.2 G2）：
    /// 有 Fail → Failed；有 Warn → Degraded；有 Skipped（无 Warn/Fail）→ Degraded；全 Pass → Success。
    /// </summary>
    public sealed class VerificationReport
    {
        private readonly List<AssertionItem> _items = new List<AssertionItem>();

        public List<AssertionItem> Items
        {
            get { return _items; }
        }

        public OperationOutcome Outcome { get; internal set; }
        public string Summary { get; internal set; }

        public void Add(AssertionItem item)
        {
            if (item == null)
            {
                return;
            }
            _items.Add(item);
            Recompute();
        }

        private void Recompute()
        {
            var failed = new List<string>();
            var warned = new List<string>();
            var skipped = new List<string>();
            foreach (AssertionItem item in _items)
            {
                if (item == null)
                {
                    continue;
                }
                switch (item.Status)
                {
                    case AssertionStatus.Fail: failed.Add(item.Name); break;
                    case AssertionStatus.Warn: warned.Add(item.Name); break;
                    case AssertionStatus.Skipped: skipped.Add(item.Name); break;
                    default: break;
                }
            }

            if (failed.Count > 0)
            {
                Outcome = OperationOutcome.Failed;
                Summary = "失败：" + string.Join("、", failed.ToArray());
            }
            else if (warned.Count > 0)
            {
                Outcome = OperationOutcome.Degraded;
                Summary = "降级完成：" + string.Join("、", warned.ToArray());
            }
            else if (skipped.Count > 0)
            {
                Outcome = OperationOutcome.Degraded;
                Summary = "降级完成：" + skipped.Count + " 项无法断言（"
                    + string.Join("、", skipped.ToArray()) + "）";
            }
            else
            {
                Outcome = OperationOutcome.Success;
                Summary = string.Format("升级完成，{0} 项自检全部通过", _items.Count);
            }
        }
    }

    // ======================================================================
    // A4 服务状态的数据结构（DD §2.2 G5）。本卡只定义结构与合成逻辑；
    // 生产方（快照/复原）由 T-HU-DEV-3 的 ServiceStateGuard 提供。
    // T-HU-DEV-3 勿在新文件里重复定义这三个类。
    // 序列化采用 public 字段（DataContractJsonSerializer 兼容），成员名与 DD 冻结一致。
    // ======================================================================

    public sealed class ServiceEntry
    {
        public string HomePath;
        public string ProcessName;
        public int Pid;
        public string ExecutablePath;
    }

    public sealed class ServiceSnapshot
    {
        public DateTime CapturedAt;
        public List<ServiceEntry> Entries = new List<ServiceEntry>();
        public bool HadGateway;      // gateway status 解析出 PID
        public int GatewayPid;
        public bool HadDesktop;      // 桌面端进程存在
        public string StatusRaw;     // gateway status 原文（进日志，便于排障）
    }

    public sealed class ServiceRestoreResult
    {
        public bool Restored;
        public string Detail;
        public int PidBefore;
        public int PidAfter;
    }

    /// <summary>
    /// 结果断言器（DD §2.2 G2）：对一次安装/升级做 A1-A4 断言并合成三态结论。
    /// 断言不抛异常，全部折算进 VerificationReport（DD §2.3「断言未过」行）。
    /// </summary>
    public sealed class ResultVerifier
    {
        private readonly Logger _logger;
        private readonly InstallerEngine _engine;
        private readonly ProcessRunner _runner;

        public ResultVerifier(Logger logger, InstallerEngine engine)
        {
            _logger = logger;
            _engine = engine;
            _runner = new ProcessRunner(logger);
        }

        /// <summary>
        /// 操作前的依赖预检（A2 的「前值」，流程 ① 之前由引擎调用）：true = import openai 成功，
        /// false/null = 不可导入或无法判定（操作后断言将按冻结口径记 Skipped，绝不冒充 Fail）。
        /// </summary>
        public async Task<bool?> PreCheckDependenciesAsync(CancellationToken ct)
        {
            try
            {
                string python = FindToolsPython();
                if (string.IsNullOrEmpty(python))
                {
                    return null;
                }
                ProcessResult run = await RunImportCheckAsync(python, ct).ConfigureAwait(false);
                return run.ExitCode == 0;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 复原之后调用（流程 ⑨ → ⑩）。baseline/restore 为 null 时 A4 记 Skipped（T-HU-DEV-3 接线前不得伪造 Pass）。
        /// </summary>
        public async Task<VerificationReport> VerifyAsync(
            bool includeDesktop,
            ServiceSnapshot baseline,
            ServiceRestoreResult restore,
            DateTime operationStartedAt,
            CancellationToken ct,
            bool? preCheckOk = null)
        {
            var report = new VerificationReport();

            // A1 CLI 版本：必须有，无 Skipped 分支。
            AssertionItem a1;
            try
            {
                a1 = await AssertCliVersionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                a1 = new AssertionItem
                {
                    Name = "CLI 版本",
                    Status = AssertionStatus.Fail,
                    Detail = "断言执行异常: " + ex.Message
                };
            }
            Record(report, a1);

            // A2 关键依赖：preCheckOk 由引擎在操作前通过 PreCheckDependenciesAsync 捕获后回传。
            // false（操作前不可导入）或 null（无法判定）→ 按冻结口径记 Skipped，不是 Fail。
            AssertionItem a2;
            try
            {
                a2 = await AssertDependenciesAsync(preCheckOk == true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                a2 = new AssertionItem
                {
                    Name = "关键依赖",
                    Status = AssertionStatus.Skipped,
                    Detail = "断言执行异常: " + ex.Message
                };
            }
            Record(report, a2);

            // A3 桌面端产物：mtime 与本次操作开始时间比较（对 InstallerEngine.cs:312 旧判据的直接修复）。
            AssertionItem a3 = AssertDesktopArtifactAsync(includeDesktop, operationStartedAt);
            Record(report, a3);

            // A4 服务状态：baseline/restore 为 null → Skipped。
            Record(report, AssertServiceState(baseline, restore));

            return report;
        }

        /// <summary>入报表 + 落日志（[通过]/[警告]/[失败]/[跳过]，经 Logger→Redactor 统一出口脱敏）。</summary>
        private void Record(VerificationReport report, AssertionItem item)
        {
            report.Add(item);
            _logger.Write(UiTextBuilder.LineLevel(item.Status), UiTextBuilder.VerificationLine(item));
        }

        // ------------------------------------------------------------- A1

        private async Task<AssertionItem> AssertCliVersionAsync(CancellationToken ct)
        {
            var item = new AssertionItem
            {
                Name = "CLI 版本",
                Expected = "hermes --version 非空且匹配 ^v?\\d+\\.\\d+"
            };

            string version = null;
            try
            {
                version = await _engine.GetVersionAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                item.Status = AssertionStatus.Fail;
                item.Actual = "(异常)";
                item.Detail = "查询版本失败: " + ex.Message;
                return item;
            }

            if (string.IsNullOrEmpty(version))
            {
                item.Status = AssertionStatus.Fail;
                item.Actual = "(空)";
                item.Detail = "hermes --version 无输出";
                return item;
            }

            if (!Regex.IsMatch(version, "^v?\\d+\\.\\d+"))
            {
                item.Status = AssertionStatus.Fail;
                item.Actual = version;
                item.Detail = "版本串不符合 v?\\d+.\\d+ 形式";
                return item;
            }

            item.Status = AssertionStatus.Pass;
            item.Actual = version;
            return item;
        }

        // ------------------------------------------------------------- A2

        private async Task<AssertionItem> AssertDependenciesAsync(bool preCheckOk, CancellationToken ct)
        {
            var item = new AssertionItem
            {
                Name = "关键依赖",
                Expected = "python -c \"import openai\" 退出码 0"
            };

            string python = FindToolsPython();
            if (string.IsNullOrEmpty(python))
            {
                item.Status = AssertionStatus.Skipped;
                item.Detail = "未找到 <HERMES_HOME>\\tools\\python-*\\python.exe，无法导入探测";
                return item;
            }

            if (!preCheckOk)
            {
                // 冻结口径（DD 偏差 D-4 / 任务卡 §二 A2）：操作前不可导入（或无法判定）→ Skipped（不是 Fail）。
                item.Status = AssertionStatus.Skipped;
                item.Detail = "操作前的依赖预检不可导入/不可判定，操作后不做对比断言";
                return item;
            }

            string label = python;
            int dirSep = label.LastIndexOf(Path.DirectorySeparatorChar);
            if (dirSep > 0)
            {
                label = Path.GetFileName(Path.GetDirectoryName(python));
            }
            item.Actual = label;

            ProcessResult run = await RunImportCheckAsync(python, ct).ConfigureAwait(false);
            if (run.ExitCode == 0)
            {
                item.Status = AssertionStatus.Pass;
                item.Detail = null;
                return item;
            }

            item.Status = AssertionStatus.Fail;
            string err = (run.StandardError ?? string.Empty).Trim();
            item.Detail = "操作后 import openai 失败: " + (err.Length == 0 ? ("退出码 " + run.ExitCode) : Truncate(err, 200));
            return item;
        }

        /// <summary>执行 import openai。复用 Generic 类并将总超时短化为 60s（辅助命令的「短化实例」，DD §2.2 G3 同款用法）。</summary>
        private async Task<ProcessResult> RunImportCheckAsync(string pythonPath, CancellationToken ct)
        {
            var options = new ProcessRunOptions
            {
                Class = OperationClass.Generic,
                NoProgressTimeout = null,
                TotalTimeout = TimeSpan.FromSeconds(60),
                PollIntervalMs = 500
            };
            string invocation = "& " + ProcessRunner.Quote(pythonPath) + " -c \"import openai\"";
            return await _runner.RunPowerShellAsync(
                invocation, HermesPaths.HermesHome, null, null, ct, options).ConfigureAwait(false);
        }

        /// <summary>
        /// 本轮唯一允许的路径推导（DD §2.2 G2 显式例外）：
        /// 扫 &lt;HERMES_HOME&gt;\tools\python-*\python.exe，取版本号最高者。
        /// </summary>
        private string FindToolsPython()
        {
            try
            {
                string toolsDir = Path.Combine(HermesPaths.HermesHome, "tools");
                if (!Directory.Exists(toolsDir))
                {
                    return null;
                }

                string[] dirs = Directory.GetDirectories(toolsDir, "python-*");
                string bestPath = null;
                Version bestVersion = null;
                foreach (string dir in dirs)
                {
                    string candidate = Path.Combine(dir, "python.exe");
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    string label = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
                    string versionText = label;
                    if (versionText.StartsWith("python-", StringComparison.OrdinalIgnoreCase))
                    {
                        versionText = versionText.Substring("python-".Length);
                    }
                    int plus = versionText.IndexOf('+');
                    if (plus >= 0)
                    {
                        versionText = versionText.Substring(0, plus);
                    }

                    Version number;
                    if (!Version.TryParse(versionText, out number))
                    {
                        number = null;
                    }

                    bool better;
                    if (number == null)
                    {
                        better = (bestPath == null);
                    }
                    else if (bestVersion == null)
                    {
                        better = true;
                    }
                    else
                    {
                        better = number > bestVersion;
                    }

                    if (better)
                    {
                        bestPath = candidate;
                        bestVersion = number;
                    }
                }
                return bestPath;
            }
            catch (Exception ex)
            {
                _logger.Warn("扫描 tools\\python-* 失败: " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------- A3

        /// <summary>DD 冻结名（原声明为 Task 返回；无 await，落地为同步签名）。</summary>
        private AssertionItem AssertDesktopArtifactAsync(bool includeDesktop, DateTime startedAt)
        {
            var item = new AssertionItem { Name = "桌面端产物" };

            if (!includeDesktop)
            {
                item.Status = AssertionStatus.Skipped;
                item.Detail = "非桌面端操作，不做产物断言";
                return item;
            }

            item.Expected = "Hermes.exe 存在且 LastWriteTime 晚于本次操作开始时间";

            bool exists = HermesPaths.DesktopBuilt;
            DateTime mtime = DateTime.MinValue;
            if (exists)
            {
                try
                {
                    mtime = File.GetLastWriteTime(HermesPaths.DesktopExe);
                    if (mtime.Year <= 1601)
                    {
                        exists = false;
                    }
                }
                catch
                {
                    exists = false;
                }
            }

            if (!exists)
            {
                item.Status = AssertionStatus.Fail;
                item.Actual = "未找到 Hermes.exe";
                item.Detail = "路径: " + HermesPaths.DesktopExe;
                return item;
            }

            item.Actual = string.Format("产物 {0:yyyy-MM-dd HH:mm:ss} / 操作开始 {1:yyyy-MM-dd HH:mm:ss}",
                mtime, startedAt);

            if (mtime <= startedAt)
            {
                item.Status = AssertionStatus.Fail;
                item.Detail = "产物未在本轮操作中更新（疑似旧产物）。如需重建请执行桌面端「修复」";
                return item;
            }

            item.Status = AssertionStatus.Pass;
            item.Detail = null;
            return item;
        }

        // ------------------------------------------------------------- A4

        private AssertionItem AssertServiceState(ServiceSnapshot baseline, ServiceRestoreResult restore)
        {
            var item = new AssertionItem
            {
                Name = "服务状态",
                Expected = "升级前网关未运行（无需恢复），或升级后网关已恢复运行"
            };

            // 本卡约束：ServiceRestoreResult 生产方为 T-HU-DEV-3；传 null 记 Skipped，严禁伪造 Pass。
            if (baseline == null || restore == null)
            {
                item.Status = AssertionStatus.Skipped;
                item.Detail = "服务快照/复原结果未接入（T-HU-DEV-3 提供），暂记跳过";
                return item;
            }

            if (!baseline.HadGateway)
            {
                item.Status = AssertionStatus.Pass;
                item.Actual = "升级前网关未运行（PID 0），无需恢复";
                return item;
            }

            if (restore.Restored)
            {
                item.Status = AssertionStatus.Pass;
                item.Actual = "网关运行中（PID " + restore.PidAfter + "）";
                return item;
            }

            item.Status = AssertionStatus.Fail;
            item.Actual = "网关未能恢复";
            item.Detail = string.IsNullOrEmpty(restore.Detail) ? ("升级前网关运行中（PID " + baseline.GatewayPid + "）") : restore.Detail;
            return item;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
