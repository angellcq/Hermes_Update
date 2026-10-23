using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace HermesInstaller.Core
{
    public enum ProbeStatus
    {
        Ok,
        Warn,
        Fail,
        Info
    }

    public sealed class ProbeItem
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Expected { get; set; }
        public string Actual { get; set; }
        public ProbeStatus Status { get; set; }
        public string Advice { get; set; }

        public ProbeItem() { }

        public ProbeItem(string category, string name, string expected, string actual, ProbeStatus status, string advice = null)
        {
            Category = category;
            Name = name;
            Expected = expected;
            Actual = actual;
            Status = status;
            Advice = advice;
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ProbeStatus.Ok: return "通过";
                    case ProbeStatus.Warn: return "警告";
                    case ProbeStatus.Fail: return "不通过";
                    default: return "信息";
                }
            }
        }
    }

    public sealed class ProbeReport
    {
        public List<ProbeItem> Items { get; } = new List<ProbeItem>();
        public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.Now;

        public bool HasFailure
        {
            get { return Items.Any(i => i.Status == ProbeStatus.Fail); }
        }

        public bool HasWarning
        {
            get { return Items.Any(i => i.Status == ProbeStatus.Warn); }
        }
    }

    public enum NetworkKind
    {
        Http,
        GitRefs
    }

    public sealed class NetworkEndpoint
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Url { get; set; }
        public NetworkKind Kind { get; set; }
        public bool Critical { get; set; }

        public NetworkEndpoint() { }

        public NetworkEndpoint(string id, string name, string category, string url, NetworkKind kind, bool critical)
        {
            Id = id;
            Name = name;
            Category = category;
            Url = url;
            Kind = kind;
            Critical = critical;
        }
    }

    public sealed class NetworkResult
    {
        public NetworkEndpoint Endpoint { get; set; }
        public bool Ok { get; set; }
        public long LatencyMs { get; set; }
        public int? HttpStatus { get; set; }
        public string Detail { get; set; }

        public string StatusText
        {
            get { return Ok ? "可用" : "不可用"; }
        }
    }

    public enum OperationKind
    {
        CliInstall,
        CliRepair,
        CliUpgrade,
        CliUninstall,
        DesktopInstall,
        DesktopRepair,
        DesktopUpgrade,
        DesktopLaunch
    }

    public sealed class OperationRequest
    {
        public OperationKind Kind { get; set; }
        public string Channel { get; set; } = "stable";
        public bool SkipBrowser { get; set; }
        public bool SkipComputerUse { get; set; }
        public bool BackupBeforeUpdate { get; set; }
    }

    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string LogPath { get; set; }

        public static OperationResult Ok(string message, string logPath = null)
        {
            return new OperationResult { Success = true, Message = message, LogPath = logPath };
        }

        public static OperationResult Error(string message, string logPath = null)
        {
            return new OperationResult { Success = false, Message = message, LogPath = logPath };
        }
    }

    public sealed class ProgressInfo
    {
        public int Index { get; set; }
        public int Total { get; set; }
        public string StageName { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
        public bool IsStageChange { get; set; }
    }

    [DataContract]
    public sealed class InstallStamp
    {
        [DataMember(Name = "schemaVersion")] public int SchemaVersion { get; set; }
        [DataMember(Name = "commit")] public string Commit { get; set; }
        [DataMember(Name = "branch")] public string Branch { get; set; }
        [DataMember(Name = "baseVersion")] public string BaseVersion { get; set; }
        [DataMember(Name = "displayVersion")] public string DisplayVersion { get; set; }
        [DataMember(Name = "source")] public string Source { get; set; }
        [DataMember(Name = "updateMechanism")] public string UpdateMechanism { get; set; }
    }

    public sealed class ManifestStage
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public bool NeedsUserInput { get; set; }
    }

    public sealed class InstallManifest
    {
        public int ProtocolVersion { get; set; }
        public List<ManifestStage> Stages { get; } = new List<ManifestStage>();
    }
}
