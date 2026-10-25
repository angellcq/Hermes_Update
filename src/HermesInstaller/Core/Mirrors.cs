using System;
using System.Collections.Generic;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Known-good endpoints and the mirror candidates the probe chooses between.
    /// The defaults were verified from this network: the official installer page is
    /// reachable, ghproxy.net proxies git, and npmmirror serves Electron and
    /// electron-builder binaries.
    /// M2 起端点带 Level 分级（FR-4.1）：0=官方直连、1=加速代理、2=国内镜像；
    /// RepoCandidates 改为官方优先（D-11），镜像只作降级候选；业务选源走 NetworkPlanner，
    /// 本类的 Pick* 仅保留给 UI 展示。
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

        // D-11：官方直连优先，两个代理镜像只作降级候选（repo-0 恒为官方且 Critical）。
        public static readonly string[] RepoCandidates =
        {
            "https://github.com/NousResearch/hermes-agent.git",
            "https://ghproxy.net/https://github.com/NousResearch/hermes-agent.git",
            "https://gh-proxy.com/https://github.com/NousResearch/hermes-agent.git"
        };

        /// <summary>
        /// 端点总表（DD-M2 §2.2 G3，17 条，逐条对应 endpoint 分级）。
        /// Id 前缀 = 分组依据（repo- / gitdeps- / electron- / builder- / npm- / pip- /
        /// chromium- / installer / release-），UI 表格按 Id 过滤，命名规则勿改。
        /// 注意 Npm / Pip / Chromium 只有 L0 与 L2（无 L1），Level 直接取 0 和 2。
        /// </summary>
        public static List<NetworkEndpoint> BuildEndpoints()
        {
            var list = new List<NetworkEndpoint>
            {
                new NetworkEndpoint("installer-0", "官方安装脚本", "官方", OfficialInstaller, NetworkKind.Http, true, 0),
                new NetworkEndpoint("release-0", "GitHub 版本 API", "官方", GithubApi, NetworkKind.Http, false, 0),

                new NetworkEndpoint("repo-0", "源码仓库（官方）", "源码", RepoCandidates[0], NetworkKind.GitRefs, true, 0),
                new NetworkEndpoint("repo-1", "源码仓库镜像 ghproxy", "源码", RepoCandidates[1], NetworkKind.GitRefs, false, 1),
                new NetworkEndpoint("repo-2", "源码仓库镜像 gh-proxy", "源码", RepoCandidates[2], NetworkKind.GitRefs, false, 1),

                new NetworkEndpoint("gitdeps-0", "GitHub 主站（git 依赖）", "官方", "https://github.com/", NetworkKind.GitRefs, false, 0),
                new NetworkEndpoint("gitdeps-1", "ghproxy（git 依赖镜像）", "镜像", "https://ghproxy.net/https://github.com/", NetworkKind.GitRefs, false, 1),

                new NetworkEndpoint("electron-0", "Electron 官方 releases-download", "构建", "https://github.com/electron/electron/releases/download/", NetworkKind.Http, false, 0),
                new NetworkEndpoint("electron-1", "Electron 镜像", "构建", ElectronMirror, NetworkKind.Http, false, 1),
                new NetworkEndpoint("builder-0", "electron-builder 官方 releases-download", "构建", "https://github.com/electron-userland/electron-builder-binaries/releases/download/", NetworkKind.Http, false, 0),
                new NetworkEndpoint("builder-1", "electron-builder 工具镜像", "构建", BuilderMirror, NetworkKind.Http, false, 1),

                new NetworkEndpoint("npm-0", "npm 官方源", "构建", "https://registry.npmjs.org/", NetworkKind.Http, false, 0),
                new NetworkEndpoint("npm-1", "npm 镜像", "构建", "https://registry.npmmirror.com/", NetworkKind.Http, false, 2),
                new NetworkEndpoint("pip-0", "PyPI 官方源", "构建", "https://pypi.org/simple/", NetworkKind.Http, false, 0),
                new NetworkEndpoint("pip-1", "PyPI 镜像", "构建", "https://pypi.tuna.tsinghua.edu.cn/simple/", NetworkKind.Http, false, 2),
                new NetworkEndpoint("chromium-0", "Chromium 官方源", "构建", "https://cdn.playwright.dev/", NetworkKind.Http, false, 0),
                new NetworkEndpoint("chromium-1", "Chromium 镜像", "构建", "https://npmmirror.com/mirrors/chromium-browser-snapshots/", NetworkKind.Http, false, 2)
            };

            return list;
        }

        /// <summary>按组取该组全部端点（含 Level，顺序按 Id 前缀匹配序）。组内无端点时返回空表。</summary>
        public static List<NetworkEndpoint> GroupsFor(EndpointGroup group)
        {
            var list = new List<NetworkEndpoint>();
            string prefix = GroupPrefix(group);
            if (prefix == null)
            {
                return list;
            }
            foreach (NetworkEndpoint endpoint in BuildEndpoints())
            {
                if (endpoint.Id != null && endpoint.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(endpoint);
                }
            }
            return list;
        }

        /// <summary>EndpointGroup → Id 前缀（DD-M2 §2.2 G3 的映射表）。</summary>
        private static string GroupPrefix(EndpointGroup group)
        {
            switch (group)
            {
                case EndpointGroup.Repo: return "repo-";
                case EndpointGroup.GitDeps: return "gitdeps-";
                case EndpointGroup.Electron: return "electron-";
                case EndpointGroup.Builder: return "builder-";
                case EndpointGroup.Npm: return "npm-";
                case EndpointGroup.Pip: return "pip-";
                case EndpointGroup.Chromium: return "chromium-";
                case EndpointGroup.Installer: return "installer";
                case EndpointGroup.ReleaseApi: return "release-";
                default: return null;
            }
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
            string picked = Pick(results, "electron-");
            return picked ?? ElectronMirror;
        }

        public static string PickBuilderMirror(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "builder-");
            return picked ?? BuilderMirror;
        }

        public static string PickNpmRegistry(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "npm-");
            return picked ?? NpmRegistry;
        }

        public static string PickPipIndex(IEnumerable<NetworkResult> results)
        {
            string picked = Pick(results, "pip-");
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
