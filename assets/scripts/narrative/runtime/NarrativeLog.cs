using System.Collections.Generic;
using Godot;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 剧情系统的报错出口：GD.PushError / PushWarning，同时计数并留下最近的消息（测试用来确认“零报错”或“报了警告”）。
/// </summary>
public static class NarrativeLog
{
    private const int Keep = 50;
    private static readonly List<string> RecentErrors = new();
    private static readonly List<string> RecentWarnings = new();

    public static int ErrorCount { get; private set; }
    public static int WarningCount { get; private set; }
    public static IReadOnlyList<string> Errors => RecentErrors;
    public static IReadOnlyList<string> Warnings => RecentWarnings;
    public static string LastWarning => RecentWarnings.Count > 0 ? RecentWarnings[^1] : "";
    public static string LastError => RecentErrors.Count > 0 ? RecentErrors[^1] : "";

    public static void Error(string message)
    {
        ErrorCount++;
        Remember(RecentErrors, message);
        GD.PushError(message);
    }

    public static void Warn(string message)
    {
        WarningCount++;
        Remember(RecentWarnings, message);
        GD.PushWarning(message);
    }

    private static void Remember(List<string> list, string message)
    {
        list.Add(message);
        if (list.Count > Keep) list.RemoveAt(0);
    }
}
