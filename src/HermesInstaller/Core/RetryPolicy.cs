using System;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Nine operation classes with frozen budget parameters (DD §2.2 G1, from PRD FR-2.8).
    /// </summary>
    public enum OperationClass
    {
        EndpointProbe = 0,
        FileDownload = 1,
        GitFetch = 2,
        GitClone = 3,
        NpmCi = 4,
        UvSync = 5,
        NodeGyp = 6,
        Generic = 7,
        OperationBudget = 8
    }

    /// <summary>
    /// Per-run timeout options. Null timeouts mean "no limit of that kind".
    /// </summary>
    public sealed class ProcessRunOptions
    {
        public OperationClass Class { get; set; }
        public TimeSpan? NoProgressTimeout { get; set; }
        public TimeSpan? TotalTimeout { get; set; }
        public int PollIntervalMs { get; set; }

        public ProcessRunOptions()
        {
            PollIntervalMs = 500;
            Class = OperationClass.Generic;
        }
    }

    /// <summary>
    /// Frozen parameter table (DD §2.2 G1). Do not alter values without updating the DD.
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>Builds default options for an operation class (table's NoProgress/Total columns).</summary>
        public static ProcessRunOptions For(OperationClass cls)
        {
            var options = new ProcessRunOptions();
            options.Class = cls;
            switch (cls)
            {
                case OperationClass.EndpointProbe:
                    options.NoProgressTimeout = null;
                    options.TotalTimeout = TimeSpan.FromSeconds(5);
                    break;
                case OperationClass.FileDownload:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(30);
                    options.TotalTimeout = TimeSpan.FromSeconds(90);
                    break;
                case OperationClass.GitFetch:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(60);
                    options.TotalTimeout = TimeSpan.FromSeconds(180);
                    break;
                case OperationClass.GitClone:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(60);
                    options.TotalTimeout = TimeSpan.FromSeconds(600);
                    break;
                case OperationClass.NpmCi:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(120);
                    options.TotalTimeout = TimeSpan.FromSeconds(900);
                    break;
                case OperationClass.UvSync:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(120);
                    options.TotalTimeout = TimeSpan.FromSeconds(900);
                    break;
                case OperationClass.NodeGyp:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(180);
                    options.TotalTimeout = TimeSpan.FromSeconds(600);
                    break;
                case OperationClass.Generic:
                    options.NoProgressTimeout = null;
                    options.TotalTimeout = TimeSpan.FromSeconds(1800);
                    break;
                case OperationClass.OperationBudget:
                    options.NoProgressTimeout = TimeSpan.FromSeconds(180);
                    options.TotalTimeout = TimeSpan.FromSeconds(1500);
                    break;
                default:
                    options.NoProgressTimeout = null;
                    options.TotalTimeout = TimeSpan.FromSeconds(1800);
                    break;
            }
            return options;
        }

        /// <summary>Max attempts (including the first). 0 means "no retry".</summary>
        public static int MaxAttempts(OperationClass cls)
        {
            switch (cls)
            {
                case OperationClass.EndpointProbe: return 2;
                case OperationClass.FileDownload: return 3;
                case OperationClass.GitFetch: return 2;
                case OperationClass.GitClone: return 2;
                case OperationClass.NpmCi: return 1;
                case OperationClass.UvSync: return 2;
                case OperationClass.NodeGyp: return 0;
                case OperationClass.Generic: return 0;
                case OperationClass.OperationBudget: return 0;
                default: return 0;
            }
        }

        /// <summary>Backoff delay before retry. attemptIndex is 0-based (the just-failed attempt).</summary>
        public static TimeSpan BackoffFor(OperationClass cls, int attemptIndex)
        {
            switch (cls)
            {
                case OperationClass.EndpointProbe:
                    return attemptIndex == 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2);
                case OperationClass.FileDownload:
                    if (attemptIndex == 0) { return TimeSpan.FromSeconds(1); }
                    if (attemptIndex == 1) { return TimeSpan.FromSeconds(2); }
                    return TimeSpan.FromSeconds(4);
                case OperationClass.GitFetch:
                case OperationClass.GitClone:
                    return attemptIndex == 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);
                case OperationClass.UvSync:
                    return attemptIndex == 0 ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(15);
                default:
                    // NpmCi / NodeGyp / Generic / OperationBudget: no retry.
                    return TimeSpan.Zero;
            }
        }

        /// <summary>Operation budget total timeout: CLI 1500s, desktop 2400s (with desktop-side stages).</summary>
        public static TimeSpan BudgetFor(bool includeDesktop)
        {
            return includeDesktop ? TimeSpan.FromSeconds(2400) : TimeSpan.FromSeconds(1500);
        }

        /// <summary>
        /// Retry gate. NodeGyp / Generic / OperationBudget never retry;
        /// compile-type errors (error C / MSB / node-gyp) never retry either.
        /// </summary>
        public static bool ShouldRetry(OperationClass cls, string lastError)
        {
            if (cls == OperationClass.NodeGyp
                || cls == OperationClass.Generic
                || cls == OperationClass.OperationBudget)
            {
                return false;
            }
            if (MaxAttempts(cls) <= 1)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(lastError))
            {
                string text = lastError;
                if (text.IndexOf("error C", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("MSB", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("node-gyp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
