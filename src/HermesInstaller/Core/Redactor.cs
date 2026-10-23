using System;
using System.Text.RegularExpressions;

namespace HermesInstaller.Core
{
    /// <summary>
    /// Best-effort secret redaction for anything shown in the UI or written to logs.
    /// Hermes itself redacts its tool output, but installer/update output can still
    /// echo provider configuration.
    /// </summary>
    public static class Redactor
    {
        private static readonly Regex[] Patterns =
        {
            // key: value / key=value pairs (YAML, JSON, CLI flags)
            new Regex(
                @"(?i)(\b(?:api[_-]?key|apikey|access[_-]?key|secret|client[_-]?secret|token|password|passwd|authorization)\b\s*[:=]\s*[""']?)([^\s""',;]{6,})",
                RegexOptions.Compiled | RegexOptions.CultureInvariant),

            // Well-known token shapes
            new Regex(@"\bsk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(@"\bghp_[A-Za-z0-9]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(@"\bgithub_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(@"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            new Regex(@"\bBearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.Compiled | RegexOptions.CultureInvariant)
        };

        public const string Mask = "***";

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string result = text;
            foreach (Regex pattern in Patterns)
            {
                try
                {
                    result = pattern.Replace(result, match =>
                    {
                        if (match.Groups.Count >= 3 && match.Groups[2].Success)
                        {
                            return match.Groups[1].Value + Mask;
                        }
                        return Mask;
                    });
                }
                catch
                {
                    // Redaction must never break logging.
                }
            }
            return result;
        }
    }
}
