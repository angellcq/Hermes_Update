using System;
using System.Collections.Generic;
using System.IO;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Builds the environment block handed to install.ps1 / hermes update so that
    /// git, npm, PyPI and the Electron build all use reachable mirrors.
    /// </summary>
    public sealed class NetworkPreparer
    {
        private readonly Settings _settings;
        private readonly Logger _logger;
        private bool _logged;

        public NetworkPreparer(Settings settings, Logger logger)
        {
            _settings = settings ?? new Settings();
            _logger = logger;
        }

        public Dictionary<string, string> BuildEnvironment()
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string repo = FirstNonEmpty(_settings.RepoMirror, Mirrors.RepoCandidates[0]);
            string electron = FirstNonEmpty(_settings.ElectronMirror, Mirrors.ElectronMirror);
            string builder = FirstNonEmpty(_settings.BuilderMirror, Mirrors.BuilderMirror);
            string npm = FirstNonEmpty(_settings.NpmRegistry, Mirrors.NpmRegistry);
            string pip = FirstNonEmpty(_settings.PipIndex, Mirrors.PipIndex);

            env["HERMES_REPO_URL"] = repo;
            env["ELECTRON_MIRROR"] = electron;
            env["ELECTRON_BUILDER_BINARIES_MIRROR"] = builder;
            env["ELECTRON_BUILDER_CACHE"] = Path.Combine(HermesPaths.CacheDir, "electron-builder");
            env["npm_config_registry"] = npm;
            env["NPM_CONFIG_REGISTRY"] = npm;
            env["PIP_INDEX_URL"] = pip;
            env["PIP_TRUSTED_HOST"] = "pypi.tuna.tsinghua.edu.cn";
            env["UV_DEFAULT_INDEX"] = pip;
            env["UV_INDEX_URL"] = pip;
            env["GIT_TERMINAL_PROMPT"] = "0";
            env["GCM_INTERACTIVE"] = "never";
            env["PYTHONUTF8"] = "1";
            env["PYTHONIOENCODING"] = "utf-8";

            try
            {
                Directory.CreateDirectory(env["ELECTRON_BUILDER_CACHE"]);
            }
            catch { }

            if (_logger != null && !_logged)
            {
                _logger.Info("源码仓库: " + repo);
                _logger.Info("Electron 镜像: " + electron);
                _logger.Info("electron-builder 镜像: " + builder);
                _logger.Info("npm registry: " + npm);
                _logger.Info("PyPI index: " + pip);
                _logged = true;
            }

            return env;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }
    }
}
