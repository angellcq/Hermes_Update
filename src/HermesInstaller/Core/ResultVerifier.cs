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
                Summary = string.Join("、", failed.ToArray());
            }
            else if (warned.Count > 0)
            {
                Outcome = OperationOutcome.Degraded;
                Summary = string.Join("、", warned.ToArray());
            }
            else if (skipped.Count > 0)
            {
                // E9-2（2026-10-25 11:15 GUI 冒烟）：Summary 不再自嵌「降级完成：」。
                // ConcludeResult 会用 OutcomeTitle(Degraded) 作标题再拼本串，此前两层
                // 都写了前缀，产生「降级完成：降级完成：2 项无法断言」重复文案。
                // 前缀语义统一收口在 OutcomeTitle（可带操作名）。
                Outcome = OperationOutcome.Degraded;
                Summary = skipped.Count + " 项无法断言（"
                    + string.Join("、", skipped.ToArray()) + "）";
            }
            else
            {
                Outcome = OperationOutcome.Success;
                // E9-2：不再硬编码「升级完成」——操作名由 OutcomeTitle 统一提供
                // （安装操作原先会拼出「安装完成：升级完成，…」）。
                Summary = string.Format("{0} 项自检全部通过", _items.Count);
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

        /// <summary>E10-②：预检锁定的 interpreter（PreCheckDependenciesAsync 选中后供操作后 A2 复用）。</summary>
        private string _lockedPython;

        /// <summary>
        /// 操作前的依赖预检（A2 的「前值」，流程 ① 之前由引擎调用）：true = import openai 成功，
        /// false/null = 不可导入或无法判定（操作后断言将按冻结口径记 Skipped，绝不冒充 Fail）。
        /// E10-②（2026-10-25）：级联探测 + 解释器锁定。
        /// 考古实锤：tools 全部 python（3.14.9+20261021、3.14.7+20260901）都装不上 openai
        /// （exit 1），hermes 实际跑在 installs\&lt;id&gt;\environments\&lt;env&gt;\venv\Scripts\python.exe
        /// （openai 2.54.0 导入成功）——旧口径只扫 tools 一个目录，永远 Skipped。
        /// 锁定：预检选中的 interpreter 存 _lockedPython，操作后 A2 用同一个复检，避免
        /// update 中途换装 tools python 造成"前后探测不同体"的假阳/假阴。
        /// </summary>
        public async Task<bool?> PreCheckDependenciesAsync(CancellationToken ct)
        {
            try
            {
                _lockedPython = null;
                foreach (string python in EnumeratePythonCandidates())
                {
                    try
                    {
                        ProcessResult run = await RunImportCheckAsync(python, ct).ConfigureAwait(false);
                        if (run.ExitCode == 0)
                        {
                            _lockedPython = python;
                            _logger.Info("[A2-PRE] import openai 成功，锁定解释器: " + python);
                            return true;
                        }
                        _logger.Info("[A2-PRE] import openai 失败（跳过下一个候选）: " + python + "（exit " + run.ExitCode + "）");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Info("[A2-PRE] 探测候选异常（跳过）: " + python + " — " + ex.Message);
                    }
                }
                return _lockedPython != null ? (bool?)true : (bool?)null;
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
            bool? preCheckOk = null,
            System.Collections.Generic.IReadOnlyList<string> owedMarkers = null)
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

            // E10-①（2026-10-25 三轮升级事故沉淀）：A5 升级欠账。
            // hermes update 有 follow-up 欠账机制：desktop build 等收尾在 update 进程内失败时
            // 会被记为 owed，待下次 update 自动补做；但 update 本身仍是 exit 0，GUI 上毫无
            // 感知（本轮 11:20 那 36 秒 fetch failed 完全隐形）。这就是为什么把 A2/A4 全
            // 判 Skip、需要 A5 这个显式断言可见化。无欠账 = 通过；有欠账 = Warn（通常是
            // "下次升级自动补做"，不会影响本轮 CLI/网关结果）；非 update 路径 = Skipped。
            Record(report, AssertOwedMarkers(owedMarkers));
            return report;
        }

        /// <summary>A5：update 输出中的 follow-up 欠账标记断言。</summary>
        private AssertionItem AssertOwedMarkers(System.Collections.Generic.IReadOnlyList<string> owedMarkers)
        {
            var item = new AssertionItem
            {
                Name = "升级欠账",
                Expected = "update 输出无 follow-up 欠账标记（Update follow-up '…' did not finish）"
            };

            if (owedMarkers == null)
            {
                item.Status = AssertionStatus.Skipped;
                item.Detail = "非 CLI 升级路径（安装/修复无 update 输出流），不做欠账断言";
                return item;
            }

            if (owedMarkers.Count == 0)
            {
                item.Status = AssertionStatus.Pass;
                item.Actual = "无欠账";
                return item;
            }

            string first = owedMarkers[0].Trim();
            item.Status = AssertionStatus.Warn;
            item.Actual = "有欠账 ×" + owedMarkers.Count;
            item.Detail = "hermes 记有 follow-up 欠账（首个标记: "
                + Truncate(first, 160)
                + "）。hermes 将在下次升级时自动补做；本轮 CLI 与网关状态不受影响，无须人工干预。";
            return item;
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
                Expected = "hermes --version 非空且含 v?\\d+\\.\\d+ 语义版本段"
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

            // 2026-10-25 10:35 事故（E9-1）：hermes 新版将 --version 首行改为
            // "Hermes Agent v0.22.5+767.gb02fa68 (2026.9.24) · upstream … (+96 carried
            // commits)" 冗长格式，不再以 v 开头，旧断言 ^v?\d+\.\d+ 误判升级失败
            // （本轮实际已 Up to date + 网关冷启动成功）。改为非锚定语义版本段搜索。
            if (!Regex.IsMatch(version, "v?\\d+\\.\\d+"))
            {
                item.Status = AssertionStatus.Fail;
                item.Actual = version;
                item.Detail = "版本串不含 v?\\d+.\\d+ 语义版本段";
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
                Expected = "python -c \"import openai\" 退出码 0（操作前后同一解释器，级联 tools → installs venv）"
            };

            // E10-②：优先用预检锁定的解释器（同一探测体），丢失时独立重解析兜底。
            string python = _lockedPython;
            if (string.IsNullOrEmpty(python))
            {
                python = FindToolsPython();
            }
            if (string.IsNullOrEmpty(python))
            {
                item.Status = AssertionStatus.Skipped;
                item.Detail = "未找到任何可探测解释器（tools\\python-*\\python.exe 与 installs\\*\\environments\\*\\venv\\Scripts\\python.exe 均无）";
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
            if (label == "Scripts")
            {
                label = "installs venv";
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

        /// <summary>
        /// E10-②：python 候选级联枚举（探测顺序固定，保证操作前后同一解释器体）：
        /// ① tools\python-*\python.exe（最高版本优先，FindToolsPython 同款推导）；
        /// ② installs\&lt;installId&gt;\environments\&lt;envId&gt;\venv\Scripts\python.exe（完整路径排序）。
        /// 本机实况：①们都没有 openai（exit 1），②的 venv 有 openai 2.54.0（import 成功）。
        /// </summary>
        private IEnumerable<string> EnumeratePythonCandidates()
        {
            string tools = FindToolsPython();
            if (!string.IsNullOrEmpty(tools))
            {
                yield return tools;
            }

            // 注意：迭代器不能在含 catch 的 try 块体内 yield（CS1626），先收集后产出。
            foreach (string venv in CollectVenvPythons())
            {
                yield return venv;
            }
        }

        private static List<string> CollectVenvPythons()
        {
            var venvs = new List<string>();
            try
            {
                string installsDir = Path.Combine(HermesPaths.HermesHome, "installs");
                if (!Directory.Exists(installsDir))
                {
                    return venvs;
                }
                foreach (string installDir in Directory.GetDirectories(installsDir))
                {
                    string envRoot = Path.Combine(installDir, "environments");
                    if (!Directory.Exists(envRoot))
                    {
                        continue;
                    }
                    foreach (string envDir in Directory.GetDirectories(envRoot))
                    {
                        string venv = Path.Combine(envDir, "venv", "Scripts", "python.exe");
                        if (File.Exists(venv))
                        {
                            venvs.Add(venv);
                        }
                    }
                }
                venvs.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // best-effort：候选枚举失败静默为空集（PreCheck 外层已有兜底日志和 Skipped 收敛）。
            }
            return venvs;
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
