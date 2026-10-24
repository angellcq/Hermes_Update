using System;
using System.Collections.Generic;

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
    }
}
