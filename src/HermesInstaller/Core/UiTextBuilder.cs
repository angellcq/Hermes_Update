using System;
using System.Collections.Generic;
using System.Globalization;

namespace HermesInstaller.Core
{
    /// <summary>
    /// 结论页与日志的文案工厂（DD §2.2 G10）：纯字符串/Core 级别，无 WinForms 引用。
    /// MainForm 只消费其输出，保证 UI 行数预算（NFR-09 / 偏差 D-5）。
    /// </summary>
    public static class UiTextBuilder
    {
        /// <summary>三态标题：成功 / 降级 / 失败（操作名由调用方作动词，如「升级」「安装」）。</summary>
        public static string OutcomeTitle(OperationOutcome outcome, string operationName)
        {
            string name = string.IsNullOrWhiteSpace(operationName) ? "操作" : operationName.Trim();
            switch (outcome)
            {
                case OperationOutcome.Success: return name + "完成";
                case OperationOutcome.Degraded: return "降级完成";
                default: return name + "失败";
            }
        }

        /// <summary>断言状态映射到的日志级别（AppendLog 既有配色：绿/橙/红/灰）。</summary>
        public static LogLevel LineLevel(AssertionStatus status)
        {
            switch (status)
            {
                case AssertionStatus.Pass: return LogLevel.Success;
                case AssertionStatus.Warn: return LogLevel.Warn;
                case AssertionStatus.Fail: return LogLevel.Error;
                default: return LogLevel.Info;
            }
        }

        /// <summary>断言行统一前缀：[通过]/[警告]/[失败]/[跳过]。</summary>
        public static string StatusTag(AssertionStatus status)
        {
            switch (status)
            {
                case AssertionStatus.Pass: return "[通过]";
                case AssertionStatus.Warn: return "[警告]";
                case AssertionStatus.Fail: return "[失败]";
                default: return "[跳过]";
            }
        }

        /// <summary>单条断言的日志/结论行，如 "[通过] CLI 版本 —— 实际值: v0.22.5+456"。</summary>
        public static string VerificationLine(AssertionItem item)
        {
            if (item == null)
            {
                return StatusTag(AssertionStatus.Skipped) + " (无效断言项)";
            }

            string text = StatusTag(item.Status) + " " + (item.Name ?? string.Empty);
            string detail;
            if (item.Status == AssertionStatus.Pass)
            {
                detail = string.IsNullOrEmpty(item.Actual) ? null : "实际值: " + item.Actual;
            }
            else
            {
                detail = !string.IsNullOrEmpty(item.Detail)
                    ? item.Detail
                    : (!string.IsNullOrEmpty(item.Actual) ? "实际值: " + item.Actual : "原因未知");
            }

            return detail == null ? text : text + " —— " + detail;
        }

        /// <summary>断言汇总行（逐条断言行经 Logger 出口已各自落日志，这里只补汇总）。</summary>
        public static string VerificationSummary(VerificationReport report)
        {
            if (report == null)
            {
                return "—— 断言汇总: 无 ——";
            }
            return "—— 断言汇总: " + (report.Summary ?? string.Empty) + " ——";
        }

        /// <summary>
        /// 整块断言的日志行列表（标题 + 每条断言一行 + 汇总行）。
        /// DD 冻结 API：供全块展示场景使用（如 run.json 排障输出）；MainForm 的常规路径
        /// 用 LogLine/VerificationLine/VerificationSummary 逐条与汇总，避免重复刷屏。
        /// </summary>
        public static List<string> VerificationLines(VerificationReport report)
        {
            var lines = new List<string>();
            if (report == null)
            {
                return lines;
            }

            lines.Add("—— 结果断言（" + report.Items.Count + " 项）——");
            foreach (AssertionItem item in report.Items)
            {
                lines.Add(VerificationLine(item));
            }
            lines.Add(VerificationSummary(report));
            return lines;
        }

        /// <summary>
        /// 双实例提示文案（DD §2.2 G10 冻结口径）：
        /// "另一实例正在执行：&lt;操作&gt;／阶段 &lt;阶段&gt;／已进行 &lt;n&gt; 分"。
        /// 字段缺失时逐项降级为「未知」，保证弹窗至少带上这句前缀。
        /// </summary>
        public static string OtherInstanceText(RunStateRecord rec)
        {
            if (rec == null)
            {
                return string.Empty;
            }
            string operation = string.IsNullOrWhiteSpace(rec.Operation) ? "未知操作" : rec.Operation.Trim();
            string stage = string.IsNullOrWhiteSpace(rec.StageName) ? "未知" : rec.StageName.Trim();
            int minutes = 0;
            if (!string.IsNullOrEmpty(rec.StartedAt))
            {
                DateTime started;
                if (DateTime.TryParseExact(rec.StartedAt, "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out started))
                {
                    minutes = (int)Math.Floor((DateTime.Now - started).TotalMinutes);
                    if (minutes < 0)
                    {
                        minutes = 0;
                    }
                }
            }
            return "\r\n\r\n【G6】另一实例正在执行：" + operation
                + "／阶段 " + stage
                + "／已进行 " + minutes.ToString(CultureInfo.InvariantCulture) + " 分";
        }

        /// <summary>指纹防重复命中的日志行（G3，D-1 口径：只解释本轮，不涉及跨会话）。</summary>
        public static string FingerprintChangedText(bool unchanged)
        {
            return unchanged
                ? "【G3】本轮版本指纹未变化，同一操作内不重复执行升级/重建步骤。"
                : "【G3】本轮版本指纹已变化（不同会话不做指纹判等，正常执行升级）。";
        }
    }
}
