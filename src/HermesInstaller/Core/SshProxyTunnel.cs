using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// E14（2026-10-25）：SSH 动态端口转发（SOCKS5）隧道。
    ///
    /// 为什么需要它：本工具的所有出网路径（install.ps1 / hermes update / npm / pip / electron /
    /// git）都依赖直连或镜像；当两者同时不可用时（镜像被墙、GitHub 直连被 RST），
    /// 唯一还能用的人工办法就是「找个能出网的跳板机开 ssh -D」。这里把该办法固化下来：
    ///
    ///   1) 用本机自带 OpenSSH（C:\Windows\System32\OpenSSH\ssh.exe，Win10 1809+ 可选功能）起
    ///      <c>ssh -N -D 127.0.0.1:&lt;socksPort&gt;</c>；
    ///   2) 改用 ssh-keygen 生成的一次性 ed25519 私钥做免密登录——首次会用密码把公钥装进
    ///      ~/.ssh/authorized_keys（幂等，已有则不追加），之后每次都不再传密码；
    ///   3) 密码不落盘明文：只在本次安装公钥时写进 <see cref="HermesPaths.SshPasswordFile"/>，
    ///      由 askpass.cmd（<c>type 密码文件</c>）读给 ssh，用完立即删除。
    ///
    /// askpass 用 .cmd 是实测结论（Win32-OpenSSH 9.5 能直接执行 batch，无需额外 exe）。
    /// </summary>
    public sealed class SshProxyTunnel : IDisposable
    {
        private const int DefaultSocksPort = 1080;
        private const int MaxRestarts = 3;
        private const string KeyComment = "hermes-installer-proxy";

        private readonly Settings _settings;
        private readonly Logger _logger;
        private readonly object _sync = new object();

        private Process _process;
        private StreamWriter _stdin;
        private bool _stopRequested;
        private bool _disposed;
        private volatile bool _running;
        private int _restarts;
        private string _lastError = string.Empty;
        private int _socksPort = DefaultSocksPort;
        private string _sshExe;
        private string _keyPath;
        private bool _usePassword;

        public SshProxyTunnel(Settings settings, Logger logger)
        {
            _settings = settings ?? new Settings();
            _logger = logger;
        }

        public bool IsRunning
        {
            get
            {
                Process p = _process;
                return _running && p != null && !HasExited(p);
            }
        }

        public int SocksPort { get { return _socksPort; } }

        public string LastError { get { return _lastError; } }

        /// <summary>本机 ssh.exe 路径（PATH 优先，其次 Windows 可选功能目录）。找不到返回 null。</summary>
        public static string FindSshExe()
        {
            return FindTool("ssh.exe", "OpenSSH");
        }

        public static string FindKeygenExe()
        {
            return FindTool("ssh-keygen.exe", "OpenSSH");
        }

        private static string FindTool(string fileName, string folder)
        {
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (string dir in pathEnv.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir))
                    {
                        continue;
                    }
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), fileName);
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string candidate = Path.Combine(winDir, "System32", folder, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 起隧道（幂等）。返回 true 表示 SOCKS5 端口已就绪。
        /// 任何一步失败都只记日志、返回 false，由调用方决定是否继续直连。
        /// </summary>
        public async Task<bool> StartAsync(CancellationToken cancellationToken)
        {
            if (IsRunning)
            {
                return true;
            }

            _stopRequested = false;
            _lastError = string.Empty;
            _restarts = 0;
            _sshExe = FindSshExe();
            if (_sshExe == null)
            {
                _lastError = "未找到 ssh.exe，请启用 Windows 可选功能「OpenSSH 客户端」";
                Warn(_lastError);
                return false;
            }

            string host = (_settings.SshHost ?? string.Empty).Trim();
            string user = (_settings.SshUser ?? string.Empty).Trim();
            if (host.Length == 0 || user.Length == 0)
            {
                _lastError = "SSH 主机或用户名为空";
                Warn("[PROXY] " + _lastError);
                return false;
            }

            _socksPort = NormalizePort(_settings.SshSocksPort, DefaultSocksPort);
            int port = NormalizePort(_settings.SshPort, 22);

            // 端口已有 SOCKS 服务（上次崩溃残留的隧道）→ 直接复用，避免二次绑定。
            if (IsSocksReady("127.0.0.1", _socksPort))
            {
                _running = true;
                Info("[PROXY] 检测到 127.0.0.1:" + _socksPort + " 已有可用的 SOCKS5 隧道，直接复用。");
                return true;
            }

            HermesPaths.EnsureAppData();
            string password = ResolvePassword();
            bool keyAuth = false;

            _keyPath = await EnsureKeyPairAsync(cancellationToken).ConfigureAwait(false);
            if (_keyPath != null)
            {
                Info("[PROXY] 使用私钥: " + _keyPath);
                keyAuth = await TestKeyAuthAsync(_sshExe, _keyPath, user, host, port, cancellationToken)
                    .ConfigureAwait(false);
                if (!keyAuth)
                {
                    if (password.Length > 0)
                    {
                        Info("[PROXY] 私钥尚未授权，尝试用密码登录并安装公钥…");
                        keyAuth = await InstallKeyAsync(user, host, port, password, cancellationToken)
                            .ConfigureAwait(false);
                        if (keyAuth)
                        {
                            keyAuth = await TestKeyAuthAsync(_sshExe, _keyPath, user, host, port, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        Warn("[PROXY] 私钥未授权且没有可用密码，无法自动登录。");
                    }
                }
            }

            _usePassword = !keyAuth;
            if (_usePassword && password.Length == 0)
            {
                _lastError = "免密登录未建立且未配置密码";
                Warn("[PROXY] " + _lastError);
                return false;
            }

            if (_usePassword)
            {
                WriteAskPass(password);
                Warn("[PROXY] 免密登录失败，本轮退化为密码登录（askpass），建议检查服务器 sshd 是否允许公钥。");
            }

            if (!StartTunnelProcess(user, host, port))
            {
                CleanupAskPass();
                return false;
            }

            bool ready = await WaitForSocksAsync(cancellationToken).ConfigureAwait(false);
            if (!ready)
            {
                _lastError = "SOCKS5 端口在 20 秒内未就绪";
                Warn("[PROXY] " + _lastError + "，ssh 诊断: " + ReadTunnelLogTail());
                Stop();
                return false;
            }

            CleanupAskPass();
            Info(string.Format("[PROXY] SSH 隧道已就绪: SOCKS5 127.0.0.1:{0} → {1}@{2}:{3}（{4}）",
                _socksPort, user, host, port, keyAuth ? "密钥登录" : "密码登录"));
            return true;
        }

        private string ResolvePassword()
        {
            string password = _settings.GetSshPassword();
            if (string.IsNullOrEmpty(password))
            {
                // 无人值守验证（--ssh-proxy-test）用环境变量注入，避免把密码写进 settings.json。
                password = Environment.GetEnvironmentVariable("HERMES_SSH_PASSWORD") ?? string.Empty;
            }
            return password;
        }

        private bool StartTunnelProcess(string user, string host, int port)
        {
            var args = new StringBuilder();
            args.Append("-N -D 127.0.0.1:").Append(_socksPort);
            if (!_usePassword && !string.IsNullOrEmpty(_keyPath))
            {
                args.Append(" -i ").Append(ProcessRunner.QuoteArg(_keyPath));
            }
            args.Append(" -o ExitOnForwardFailure=yes")
                .Append(" -o ServerAliveInterval=30")
                .Append(" -o ServerAliveCountMax=3")
                .Append(" -o ConnectTimeout=15")
                .Append(" -o NumberOfPasswordPrompts=1")
                .Append(" -o StrictHostKeyChecking=accept-new")
                .Append(" -o LogLevel=ERROR")
                .Append(" -p ").Append(port);
            if (_usePassword)
            {
                args.Append(" -o PreferredAuthentications=password,keyboard-interactive")
                    .Append(" -o PubkeyAuthentication=no");
            }
            else
            {
                args.Append(" -o BatchMode=yes");
            }
            args.Append(" ").Append(ProcessRunner.QuoteArg(user + "@" + host));

            var psi = new ProcessStartInfo(_sshExe)
            {
                Arguments = args.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (_usePassword)
            {
                psi.EnvironmentVariables["SSH_ASKPASS"] = HermesPaths.SshAskPassPath;
                psi.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
                psi.EnvironmentVariables["DISPLAY"] = "localhost:0";
            }

            try
            {
                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += delegate { };
                process.ErrorDataReceived += delegate { };
                process.Exited += OnTunnelExited;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    _stdin = process.StandardInput;
                    _stdin.AutoFlush = true;
                }
                catch { }

                lock (_sync)
                {
                    _process = process;
                    _running = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                _lastError = "启动 ssh 失败: " + ex.Message;
                Warn("[PROXY] " + _lastError);
                return false;
            }
        }

        private void OnTunnelExited(object sender, EventArgs e)
        {
            Process exited = sender as Process;
            int exitCode = -1;
            try { if (exited != null) { exitCode = exited.ExitCode; } }
            catch { }

            if (_stopRequested || _disposed)
            {
                return;
            }

            _running = false;
            Warn("[PROXY] ssh 隧道进程退出（exit=" + exitCode + "）。");

            int attempt;
            lock (_sync)
            {
                attempt = ++_restarts;
            }
            if (attempt > MaxRestarts)
            {
                Warn("[PROXY] 隧道已连续退出 " + MaxRestarts + " 次，放弃自动重连。");
                return;
            }

            Task.Run(async delegate
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt)).ConfigureAwait(false);
                    if (_stopRequested || _disposed)
                    {
                        return;
                    }
                    Info("[PROXY] 尝试第 " + attempt + " 次自动重连…");
                    string host = (_settings.SshHost ?? string.Empty).Trim();
                    string user = (_settings.SshUser ?? string.Empty).Trim();
                    int port = NormalizePort(_settings.SshPort, 22);
                    if (_usePassword)
                    {
                        string password = ResolvePassword();
                        if (password.Length == 0)
                        {
                            Warn("[PROXY] 自动重连缺少密码，放弃。");
                            return;
                        }
                        WriteAskPass(password);
                    }
                    if (StartTunnelProcess(user, host, port))
                    {
                        await WaitForSocksAsync(CancellationToken.None).ConfigureAwait(false);
                        CleanupAskPass();
                    }
                }
                catch (Exception ex)
                {
                    Warn("[PROXY] 自动重连失败: " + ex.Message);
                }
            });
        }

        private async Task<string> EnsureKeyPairAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (File.Exists(HermesPaths.SshKeyPath) && File.Exists(HermesPaths.SshKeyPath + ".pub"))
                {
                    return HermesPaths.SshKeyPath;
                }
                string keygen = FindKeygenExe();
                if (keygen == null)
                {
                    Warn("[PROXY] 未找到 ssh-keygen.exe，跳过密钥生成（只能用密码登录）。");
                    return null;
                }

                Directory.CreateDirectory(HermesPaths.SshDir);
                string args = "-t ed25519 -f " + ProcessRunner.QuoteArg(HermesPaths.SshKeyPath)
                    + " -N \"\" -C " + ProcessRunner.QuoteArg(KeyComment);
                string stdout = string.Empty;
                string stderr = string.Empty;
                bool ok = await Task.Run(delegate
                {
                    return RunProcess(keygen, args, null, 30000, out stdout, out stderr);
                }, cancellationToken).ConfigureAwait(false);

                if (!ok || !File.Exists(HermesPaths.SshKeyPath))
                {
                    Warn("[PROXY] ssh-keygen 生成密钥失败: " + (stderr ?? string.Empty).Trim());
                    return null;
                }
                Info("[PROXY] 已生成一次性 ed25519 密钥: " + HermesPaths.SshKeyPath);
                return HermesPaths.SshKeyPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Warn("[PROXY] 生成密钥异常: " + ex.Message);
                return null;
            }
        }

        private async Task<bool> TestKeyAuthAsync(
            string sshExe, string keyPath, string user, string host, int port, CancellationToken cancellationToken)
        {
            string args = "-i " + ProcessRunner.QuoteArg(keyPath)
                + " -o BatchMode=yes -o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new"
                + " -o LogLevel=ERROR -p " + port + " "
                + ProcessRunner.QuoteArg(user + "@" + host) + " \"echo HERMES_KEY_OK\"";
            return await Task.Run(delegate
            {
                string o;
                string err;
                bool ok = RunProcess(sshExe, args, null, 25000, out o, out err);
                return ok && (o ?? string.Empty).IndexOf("HERMES_KEY_OK", StringComparison.Ordinal) >= 0;
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> InstallKeyAsync(
            string user, string host, int port, string password, CancellationToken cancellationToken)
        {
            try
            {
                string pub = (File.ReadAllText(HermesPaths.SshKeyPath + ".pub") ?? string.Empty).Trim();
                if (pub.Length == 0)
                {
                    return false;
                }
                WriteAskPass(password);

                string remote = "mkdir -p ~/.ssh && chmod 700 ~/.ssh"
                    + " && touch ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
                    + " && (grep -qF '" + pub + "' ~/.ssh/authorized_keys"
                    + " || echo '" + pub + "' >> ~/.ssh/authorized_keys)";
                string args = "-o PubkeyAuthentication=no"
                    + " -o PreferredAuthentications=password,keyboard-interactive"
                    + " -o NumberOfPasswordPrompts=1 -o ConnectTimeout=15"
                    + " -o StrictHostKeyChecking=accept-new -o LogLevel=ERROR"
                    + " -p " + port + " " + ProcessRunner.QuoteArg(user + "@" + host)
                    + " \"" + remote + "\"";

                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "SSH_ASKPASS", HermesPaths.SshAskPassPath },
                    { "SSH_ASKPASS_REQUIRE", "force" },
                    { "DISPLAY", "localhost:0" }
                };

                bool ok = await Task.Run(delegate
                {
                    string o;
                    string err;
                    bool done = RunProcess(_sshExe, args, env, 40000, out o, out err);
                    if (!done)
                    {
                        Warn("[PROXY] 安装公钥失败: " + (err ?? string.Empty).Trim());
                    }
                    return done;
                }, cancellationToken).ConfigureAwait(false);
                return ok;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Warn("[PROXY] 安装公钥异常: " + ex.Message);
                return false;
            }
            finally
            {
                CleanupAskPass();
            }
        }

        /// <summary>
        /// 写 askpass 脚本 + 密码文件。脚本用 <c>type</c> 原样输出，避免密码里的
        /// &amp; | &lt; &gt; ^ % 被 batch 解释（用 echo 会踩坑）。
        /// </summary>
        private void WriteAskPass(string password)
        {
            try
            {
                Directory.CreateDirectory(HermesPaths.SshDir);
                File.WriteAllText(HermesPaths.SshPasswordFile, password ?? string.Empty, new UTF8Encoding(false));
                string script = "@echo off\r\ntype \"" + HermesPaths.SshPasswordFile + "\"";
                File.WriteAllText(HermesPaths.SshAskPassPath, script, new ASCIIEncoding());
            }
            catch (Exception ex)
            {
                Warn("[PROXY] 写 askpass 失败: " + ex.Message);
            }
        }

        private void CleanupAskPass()
        {
            try { if (File.Exists(HermesPaths.SshPasswordFile)) { File.Delete(HermesPaths.SshPasswordFile); } }
            catch { }
        }

        private async Task<bool> WaitForSocksAsync(CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsRunning && !_running)
                {
                    return false;
                }
                if (IsSocksReady("127.0.0.1", _socksPort))
                {
                    return true;
                }
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>SOCKS5 握手探测：发 05 01 00，只认服务端回 05 ??。只读不写目标。</summary>
        public static bool IsSocksReady(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    Task connect = client.ConnectAsync(host, port);
                    if (!connect.Wait(1500))
                    {
                        return false;
                    }
                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 2000;
                        stream.Write(new byte[] { 0x05, 0x01, 0x00 }, 0, 3);
                        var reply = new byte[2];
                        int read = stream.Read(reply, 0, 2);
                        return read == 2 && reply[0] == 0x05;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>停隧道（不抛异常，可重复调用）。</summary>
        public void Stop()
        {
            _stopRequested = true;
            Process process;
            lock (_sync)
            {
                process = _process;
                _process = null;
            }

            try { if (_stdin != null) { _stdin.Dispose(); } }
            catch { }
            _stdin = null;

            if (process != null)
            {
                try
                {
                    if (!HasExited(process))
                    {
                        ProcessRunner.KillTree(process);
                    }
                }
                catch { }
                try { process.Dispose(); }
                catch { }
            }

            _running = false;
            CleanupAskPass();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Stop();
        }

        private static bool HasExited(Process process)
        {
            try { return process.HasExited; }
            catch { return true; }
        }

        private static int NormalizePort(int value, int fallback)
        {
            return value > 0 && value < 65536 ? value : fallback;
        }

        private string ReadTunnelLogTail()
        {
            // ssh 的 stderr 走管道（ERROR 级别），没有落盘；这里给最后一条进程状态即可。
            Process p = _process;
            if (p == null)
            {
                return "(进程未启动)";
            }
            try { return "pid=" + p.Id + " exited=" + HasExited(p); }
            catch { return "(无法读取)"; }
        }

        private static bool RunProcess(
            string exe, string args, IDictionary<string, string> env, int timeoutMs,
            out string stdout, out string stderr)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            var psi = new ProcessStartInfo(exe)
            {
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (env != null)
            {
                foreach (KeyValuePair<string, string> pair in env)
                {
                    psi.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    var outBuilder = new StringBuilder();
                    var errBuilder = new StringBuilder();
                    process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { outBuilder.AppendLine(e.Data); }
                    };
                    process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { errBuilder.AppendLine(e.Data); }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    try
                    {
                        process.StandardInput.WriteLine(string.Empty);
                        process.StandardInput.Close();
                    }
                    catch { }

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { ProcessRunner.KillTree(process); }
                        catch { }
                        stdout = outBuilder.ToString();
                        stderr = errBuilder.ToString();
                        return false;
                    }
                    process.WaitForExit();
                    stdout = outBuilder.ToString();
                    stderr = errBuilder.ToString();
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return false;
            }
        }

        private void Info(string message)
        {
            if (_logger != null) { _logger.Info(message); }
        }

        private void Warn(string message)
        {
            if (_logger != null) { _logger.Warn(message); }
        }
    }
}
