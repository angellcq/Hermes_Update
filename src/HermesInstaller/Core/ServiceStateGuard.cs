using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// 升级前采集网关/桌面端状态快照，升级后按冻结顺序复原（DD §2.2 G5，T-HU-DEV-3）。
    /// 复原顺序固定：gateway status 查 PID → 未运行则 gateway ensure → 再查 →
    /// 仍未运行则 gateway start → 再查 → 仍未运行则 Restored=false。
    /// 双起防护（DD 风险 R-10）：仅在当前 PID == 0 时才尝试启动，网关已在跑时
    /// 绝不发起任何网关启动命令；每次 status 解析出的 PID 都进日志。
    /// 子命令均为 2026-10-24 本机实测存在的 gateway status/ensure/start；解析失败
    /// 时保留原文进日志并按未运行处理，不做正则硬猜。
    /// 命名偏差说明：DD 伪代码 QueryGatewayPidAsync(out string raw, ...) 的 out 参数
    /// 在 C# async 方法里非法，落地为返回私有 GatewayStatus（Pid + Raw）。
    /// 禁止用 Process.Start(HermesPaths.DesktopExe) 之类手段冒充网关恢复。
    /// </summary>
    public sealed class ServiceStateGuard
    {
        private readonly Logger _logger;
        private readonly ProcessRunner _runner;

        public ServiceStateGuard(Logger logger, ProcessRunner runner)
        {
            _logger = logger;
            _runner = runner;
        }

        /// <summary>升级前快照。调用方必须保证在 StopHermesProcessesAsync 之前调用。</summary>
        public async Task<ServiceSnapshot> CaptureAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            GatewayStatus status = await QueryGatewayPidAsync("快照采集", ct).ConfigureAwait(false);

            var snapshot = new ServiceSnapshot();
            snapshot.CapturedAt = DateTime.Now;
            snapshot.StatusRaw = status.Raw;
            snapshot.GatewayPid = status.Pid;
            snapshot.HadGateway = status.Pid > 0;

            if (snapshot.HadGateway)
            {
                var gatewayEntry = new ServiceEntry();
                gatewayEntry.HomePath = HermesPaths.HermesHome;
                gatewayEntry.ProcessName = "hermes-gateway";
                gatewayEntry.Pid = snapshot.GatewayPid;
                gatewayEntry.ExecutablePath = null; // status 输出不提供可执行路径
                snapshot.Entries.Add(gatewayEntry);
            }

            DetectDesktopProcesses(snapshot);

            _logger.Info("[GATEWAY][快照] 网关 PID=" + snapshot.GatewayPid
                + "，网关在跑=" + snapshot.HadGateway
                + "，桌面端进程=" + snapshot.HadDesktop
                + "，快照条目数=" + snapshot.Entries.Count);
            return snapshot;
        }

        /// <summary>升级后复原（DD §2.2 G5 伪代码）。基线由调用方在停进程之前采集。</summary>
        public async Task<ServiceRestoreResult> RestoreAsync(ServiceSnapshot baseline, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var result = new ServiceRestoreResult();
            result.Restored = false;
            result.Detail = null;
            result.PidBefore = baseline != null ? baseline.GatewayPid : 0;
            result.PidAfter = 0;

            if (baseline == null)
            {
                result.Detail = "无基线快照，无法判断网关是否需要恢复";
                _logger.Warn("[GATEWAY] 复原跳过: " + result.Detail);
                return result;
            }

            if (!baseline.HadGateway)
            {
                // 未运行时不乱启动：升级前网关未运行 → 直接放行，不执行任何 gateway 启动命令。
                result.Restored = true;
                result.Detail = "升级前网关未运行，无需恢复";
                _logger.Info("[GATEWAY] " + result.Detail + "（基线 PID 0），未发起任何恢复命令。");
                return result;
            }

            _logger.Info("[GATEWAY] 开始复原：基线 PID " + baseline.GatewayPid + "，先查询当前网关 PID…");
            GatewayStatus before = await QueryGatewayPidAsync("复原前查询", ct).ConfigureAwait(false);
            result.PidBefore = before.Pid;

            // 双起防护（R-10）：只有「当前 PID == 0」才继续尝试启动。
            if (before.Pid > 0)
            {
                result.Restored = true;
                result.PidAfter = before.Pid;
                result.Detail = "网关仍运行（PID " + before.Pid + "）";
                _logger.Info("[GATEWAY][双起防护] 复原前查询到网关已运行（PID " + before.Pid
                    + "），无需启动，未发起任何恢复命令。");
                return result;
            }

            string lastRaw = before.Raw;

            // 第一步 ensure（官方语义：不安装、不替换服务）
            await RunGatewayCommandAsync("ensure", ct).ConfigureAwait(false);
            GatewayStatus afterEnsure = await QueryGatewayPidAsync("ensure 后查询", ct).ConfigureAwait(false);
            lastRaw = afterEnsure.Raw;
            if (afterEnsure.Pid > 0)
            {
                result.Restored = true;
                result.PidAfter = afterEnsure.Pid;
                result.Detail = "gateway ensure 已恢复（PID " + afterEnsure.Pid + "）";
                _logger.Success("[GATEWAY] " + result.Detail);
                return result;
            }

            // 第二步 start（启动已注册的计划任务 Hermes_Gateway）
            await RunGatewayCommandAsync("start", ct).ConfigureAwait(false);
            GatewayStatus afterStart = await QueryGatewayPidAsync("start 后查询", ct).ConfigureAwait(false);
            lastRaw = afterStart.Raw;
            if (afterStart.Pid > 0)
            {
                result.Restored = true;
                result.PidAfter = afterStart.Pid;
                result.Detail = "gateway start 已恢复（PID " + afterStart.Pid + "）";
                _logger.Success("[GATEWAY] " + result.Detail);
                return result;
            }

            // 结论降级：两次启动尝试都失败，原文进日志与 Detail。
            result.Restored = false;
            result.PidAfter = 0;
            result.Detail = "网关未能恢复；最后一次 status 原文：" + lastRaw;
            _logger.Error("[GATEWAY] 两步复原均失败，按冻结顺序降级。" + result.Detail);
            return result;
        }

        /// <summary>
        /// gateway 子命令统一超时选项。实测 gateway status 单次可达 5.7s（2026-10-24），
        /// EndpointProbe 的 5s 总超时会把它杀掉导致状态误判为未运行，因此一律走
        /// Generic 基底再放宽：无进展 120s / 总预算 300s，不重试（DD §2.2 G5）。
        /// </summary>
        private static ProcessRunOptions BuildGatewayOptions()
        {
            return new ProcessRunOptions
            {
                Class = OperationClass.Generic,
                NoProgressTimeout = TimeSpan.FromSeconds(120),
                TotalTimeout = TimeSpan.FromSeconds(300)
            };
        }

        private async Task<ProcessResult> RunGatewayCommandAsync(string subcommand, CancellationToken ct)
        {
            string invocation = "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " gateway " + subcommand;

            ProcessRunOptions options = BuildGatewayOptions();

            _logger.Stage("[GATEWAY] 执行 gateway " + subcommand + " …");
            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                null,
                null,
                ct,
                options).ConfigureAwait(false);

            LogProcessOutput("gateway " + subcommand, result);

            if (result.Cancelled)
            {
                throw new OperationCanceledException(ct);
            }

            return result;
        }

        private async Task<GatewayStatus> QueryGatewayPidAsync(string label, CancellationToken ct)
        {
            var status = new GatewayStatus();
            status.Pid = 0;
            status.Raw = string.Empty;

            if (!HermesPaths.CliLauncherPresent)
            {
                _logger.Warn("[GATEWAY] " + label + ": 未检测到 hermes CLI（hermes.cmd 缺失），视为网关未运行。");
                return status;
            }

            string invocation = "& " + ProcessRunner.Quote(HermesPaths.HermesCmd) + " gateway status";

            ProcessResult result = await _runner.RunPowerShellAsync(
                invocation,
                HermesPaths.HermesHome,
                null,
                null,
                ct,
                BuildGatewayOptions()).ConfigureAwait(false);

            LogProcessOutput("gateway status（" + label + "）", result);

            if (result.Cancelled)
            {
                throw new OperationCanceledException(ct);
            }

            if (result.TimedOut)
            {
                _logger.Warn("[GATEWAY] " + label + ": gateway status 超时（gateway 类选项：无进展 "
                    + BuildGatewayOptions().NoProgressTimeout.Value.TotalSeconds + "s / 总预算 "
                    + BuildGatewayOptions().TotalTimeout.Value.TotalSeconds
                    + "s），按未运行处理。");
                return status;
            }

            status.Raw = result.StandardOutput ?? string.Empty;
            status.Pid = ParseGatewayPid(status.Raw);

            if (status.Pid == 0)
            {
                _logger.Warn("[GATEWAY] " + label + ": 未能解析出网关 PID（exit=" + result.ExitCode
                    + "），按未运行处理；原文已整段落日志。");
            }

            return status;
        }

        /// <summary>逐行找同时含 "Gateway process running" 与 "PID:" 的行，取 PID: 后的连续数字；失败返回 0。</summary>
        private static int ParseGatewayPid(string stdout)
        {
            if (string.IsNullOrEmpty(stdout))
            {
                return 0;
            }

            string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.IndexOf("Gateway process running", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                int idx = line.IndexOf("PID:", StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    continue;
                }
                int i = idx + 4;
                while (i < line.Length && !char.IsDigit(line[i]))
                {
                    i++;
                }
                int start = i;
                while (i < line.Length && char.IsDigit(line[i]))
                {
                    i++;
                }
                if (i > start)
                {
                    int parsed;
                    if (int.TryParse(line.Substring(start, i - start), out parsed))
                    {
                        return parsed;
                    }
                }
            }
            return 0;
        }

        private void DetectDesktopProcesses(ServiceSnapshot snapshot)
        {
            string home = HermesPaths.HermesHome.TrimEnd('\\') + "\\";
            foreach (Process process in Process.GetProcessesByName("Hermes"))
            {
                try
                {
                    string path = null;
                    try { path = process.MainModule != null ? process.MainModule.FileName : null; } catch { }
                    if (path == null || !path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    snapshot.HadDesktop = true;
                    var entry = new ServiceEntry();
                    entry.HomePath = HermesPaths.HermesHome;
                    entry.ProcessName = process.ProcessName;
                    entry.Pid = process.Id;
                    entry.ExecutablePath = path;
                    snapshot.Entries.Add(entry);
                }
                catch { }
                finally
                {
                    try { process.Dispose(); } catch { }
                }
            }
        }

        /// <summary>整段落日志（stdout 走 Info、stderr 走 Warn，R-03）；经 Logger→Redactor 统一出口脱敏。</summary>
        private void LogProcessOutput(string label, ProcessResult result)
        {
            string stdout = result == null ? string.Empty : (result.StandardOutput ?? string.Empty);
            string stderr = result == null ? string.Empty : (result.StandardError ?? string.Empty);

            if (stdout.Trim().Length == 0 && stderr.Trim().Length == 0)
            {
                _logger.Info("[GATEWAY] " + label + ": （无输出，exit=" + (result == null ? -1 : result.ExitCode) + "）");
                return;
            }

            string[] outLines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in outLines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    _logger.Info("[GATEWAY] " + label + " | " + trimmed);
                }
            }

            string[] errLines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in errLines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    _logger.Warn("[GATEWAY] " + label + " [stderr] | " + trimmed);
                }
            }
        }

        /// <summary>QueryGatewayPidAsync 的返回结构（C# async 无法带 out 参数，见类注释）。</summary>
        private sealed class GatewayStatus
        {
            public int Pid;
            public string Raw;
        }
    }
}
