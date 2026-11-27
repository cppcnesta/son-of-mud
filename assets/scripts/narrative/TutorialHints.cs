using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative;

/// <summary>
/// 新手引导：剧本 tutorial.orph 里每条提示一个标签（第一行 [字幕]），用底部字幕条显示，不暂停、不锁操作。
/// 提示不在条件满足的那一刻播，而是「先记下该播，等空了再播」：
/// - 条件：引导区（TutorialZone）写的 tutorial.due.&lt;标签&gt;，或者现成的进度变量（捡到过泥板 tablet.*、obtained.泥肝、obtained.泥瓶），
///   以及第一次贴到能爬的墙（本节点看到后写 tutorial.due.攀墙）。这些都在进度里，跟着存档走。
/// - 要播的提示 = 条件成立 且 还没看过（seen.tutorial.&lt;标签&gt;，字幕正常播完时剧情层写）。每次都从进度重新算，不另存队列，
///   所以换房间、存读档以后结果一样；播到一半被主剧情打断、被别的字幕顶掉都不算看过，空下来后重播。
/// - 什么时候播：没有主剧情（也没有排队的）、字幕条空着、游戏没暂停、没有输入锁，并且这样空闲了 <see cref="Gap"/> 秒，
///   按 <see cref="Hints"/> 的顺序拿第一条。
/// 本节点由 NarrativeDirector 建在自己下面，ProcessMode 跟着剧情层（Always），自己检查暂停。
/// </summary>
public partial class TutorialHints : Node
{
    public const string ScriptId = "tutorial";
    public const string DuePrefix = "tutorial.due.";
    public const string Climb = "攀墙";

    /// <summary>总开关，默认开。流程测试默认关掉（和 AutoOpeningEnabled 一样），只有测引导的测试打开。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>前后两条字幕之间至少空这么久（秒），免得连在一起读不过来。</summary>
    public static double Gap { get; set; } = 0.6;

    public enum Source { Zone, Tablet, Item }

    public readonly record struct Hint(string Label, Source Source, string Item = "");

    /// <summary>全部提示，按优先顺序。标签就是 tutorial.orph 里的标签。</summary>
    public static readonly Hint[] Hints =
    {
        new("移动", Source.Zone),
        new(Climb, Source.Zone),
        new("闪避", Source.Zone),
        new("攻击", Source.Zone),
        new("格挡", Source.Zone),
        new("收集页", Source.Tablet),
        new("泥肝", Source.Item, "泥肝"),
        new("泥瓶", Source.Item, "泥瓶"),
    };

    public NarrativeDirector Director { get; set; }
    /// <summary>正在播的提示标签（字幕条上是它）；没有时为空。</summary>
    public string Playing { get; private set; } = "";
    /// <summary>每次开始播一条提示就发一次（测试用，看顺序和次数；读档后剧情层是新的，所以放成静态事件）。</summary>
    public static event Action<string> HintStarted;

    private double _idle;
    /// <summary>请求失败的标签（剧本里没有这个标签等，剧情层已经报过错），不再反复请求。</summary>
    private readonly HashSet<string> _broken = new();

    public static string DueKey(string label) => DuePrefix + label;
    public static string SeenKey(string label) => $"seen.{ScriptId}.{label}";

    /// <summary>引导区和本节点用：记下这条提示该播了（已经记过就不动）。</summary>
    public static void MarkDue(string label)
    {
        if (!string.IsNullOrEmpty(label) && !GameSession.Progress.Flag(DueKey(label))) GameSession.Progress.SetVar(DueKey(label), true);
    }

    public static bool Seen(string label) => GameSession.Progress.Flag(SeenKey(label));

    public static bool ConditionMet(Hint hint)
    {
        ProgressStore progress = GameSession.Progress;
        switch (hint.Source)
        {
            case Source.Tablet:
                foreach (StringName key in progress.VariableKeys)
                    if (key.ToString().StartsWith("tablet.") && progress.Flag(key)) return true;
                return false;
            case Source.Item:
                return progress.Flag(ItemObtainedRule.Key(hint.Item));
            default:
                return progress.Flag(DueKey(hint.Label));
        }
    }

    /// <summary>现在该播、还没看过的提示，按顺序。</summary>
    public static List<string> PendingLabels()
    {
        var list = new List<string>();
        foreach (Hint hint in Hints)
            if (!Seen(hint.Label) && ConditionMet(hint)) list.Add(hint.Label);
        return list;
    }

    /// <summary>剧情层和游戏都空着：没有主剧情、没有排队、没有字幕、没暂停、没锁输入。</summary>
    public bool Idle()
    {
        NarrativeDirector d = Director;
        return d != null && !d.MainActive && d.PendingCount == 0 && !d.ReleasePending && !d.Bark.IsPlaying && !d.Subtitle.IsShowing
               && !d.Tablet.IsOpen && !PauseService.IsPaused && !InputLock.IsLocked;
    }

    public override void _Process(double delta)
    {
        if (Director == null) return;
        if (Playing != "" && !(Director.Bark.IsPlaying && Director.BarkScript == ScriptId && Director.BarkLabel == Playing)) Playing = "";
        if (!Enabled) return;

        if (PlayerRegistry.Current is IWallSensor wall && !PauseService.IsPaused && wall.TouchingClimbableWall) MarkDue(Climb);

        if (!Idle())
        {
            _idle = 0;
            return;
        }
        _idle += delta;
        if (_idle < Gap) return;
        foreach (Hint hint in Hints)
        {
            if (_broken.Contains(hint.Label) || Seen(hint.Label) || !ConditionMet(hint)) continue;
            _idle = 0;
            if (Director.Request(ScriptId, hint.Label))
            {
                Playing = hint.Label;
                HintStarted?.Invoke(hint.Label);
            }
            else _broken.Add(hint.Label);
            return;
        }
    }
}
