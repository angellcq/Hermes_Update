using System;
using System.Collections.Generic;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Known-good endpoints and the mirror candidates the probe chooses between.
    /// The defaults were verified from this network: the official installer page is
    /// reachable, ghproxy.net proxies git, and npmmirror serves Electron and
    /// electron-builder binaries.
    /// </summary>
    public static class Mirrors
    {
        public const string OfficialInstaller = "https://hermes-agent.nousresearch.com/install.ps1";
        public const string OfficialDownload = "https://hermes-assets.nousresearch.com/Hermes-Setup.exe";
        public const string Github = "https://github.com";
        public const string GithubApi = "https://api.github.com/repos/NousResearch/hermes-agent/releases/latest";
        public const string RawGithub = "https://raw.githubusercontent.com/NousResearch/hermes-agent/main/scripts/install.ps1";

        public const string ElectronMirror = "https://npmmirror.com/mirrors/electron/";
        public const string BuilderMirror = "https://npmmirror.com/mirrors/electron-builder-binaries/";
        public const string NpmRegistry = "https://registry.npmmirror.com";
        public const string PipIndex = "https://pypi.tuna.tsinghua.edu.cn/simple";

        public static readonly string[] RepoCandidates =
        {
            "https://ghproxy.net/https://github.com/NousResearch/hermes-agent.git",
            "https://gh-proxy.com/https://github.com/NousResearch/hermes-agent.git",
            "https://github.com/NousResearch/hermes-agent.git"
        };

        public static List<NetworkEndpoint> BuildEndpoints()
        {
            var list = new List<NetworkEndpoint>
            {
                new NetworkEndpoint("official-install", "官方安装脚本", "官方", OfficialInstaller, NetworkKind.Http, true),
                new NetworkEndpoint("github-api", "GitHub 版本 API", "官方", GithubApi, NetworkKind.Http, false),
                new NetworkEndpoint("github", "GitHub 主站", "官方", Github, NetworkKind.Http, false),
                new NetworkEndpoint("raw-github", "raw.githubusercontent", "官方", RawGithub, NetworkKind.Http, false)
            };

            for (int i = 0; i < RepoCandidates.Length; i++)
            {
                string url = RepoCandidates[i];
                string name = i == RepoCandidates.Length - 1 ? "源码仓库（官方）" : "源码仓库镜像 " + (i + 1);
                list.Add(new NetworkEndpoint("repo-" + i, name, "源码", url, NetworkKind.GitRefs, i == 0));
            }

            list.Add(new NetworkEndpoint("electron-mirror", "Electron 镜像", "构建", ElectronMirror, NetworkKind.Http, false));
            list.Add(new NetworkEndpoint("builder-mirror", "electron-builder 工具镜像", "构建", BuilderMirror, NetworkKind.Http, false));
            list.Add(new NetworkEndpoint("npm-registry", "npm 镜像", "构建", NpmRegistry, NetworkKind.Http, false));
            list.Add(new NetworkEndpoint("pip-index", "PyPI 镜像", "构建", PipIndex, NetworkKind.Http, false));

            return list;
        }

        public static string PickRepoMirror(IEnumerable<NetworkResult> results)
        {
            if (results == null)
            {
                return null;
            }
            string best = null;
            long bestLatency = long.MaxValue;
            foreach (NetworkResult result in results)
            {
                if (result.Ok && result.Endpoint != null && result.Endpoint.Id != null &&
                    result.Endpoint.Id.StartsWith("repo-", StringComparison.OrdinalIgnoreCase) &&
                    result.LatencyMs < bestLatency)
                {
                    best = result.Endpoint.Url;
                    bestLatency = result.LatencyMs;
                }
            }
            return best;
        }

        public static string PickElectronMirror(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "electron-mirror");
            return picked ?? ElectronMirror;
        }

        public static string PickBuilderMirror(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "builder-mirror");
            return picked ?? BuilderMirror;
        }

        public static string PickNpmRegistry(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "npm-registry");
            return picked ?? NpmRegistry;
        }

        public static string PickPipIndex(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "pip-index");
            return picked ?? PipIndex;
        }

        private static string Pick(IEnumerable<NetworkResult> results, string idPrefix)
        {
            if (results == null)
            {
                return null;
            }
            foreach (NetworkResult result in results)
            {
                if (result.Ok && result.Endpoint != null && result.Endpoint.Id != null &&
                    result.Endpoint.Id.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return result.Endpoint.Url;
                }
            }
            return null;
        }
    }
}
