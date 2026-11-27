using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.Narrative.Parsing;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;

/// <summary>
/// 新手引导（计划第 2 版第四节）：从正式入口开新游戏（自动开场、自动剧情、引导都打开），真实玩家用真实按键走 S1_01 → S1_04，
/// 逐条验收「先记下该播、等空了再播」：
/// 1 开场播完以后出现移动提示；2 第一次读泥板，关掉阅读界面、剧情结束后才出现 Tab 提示；3 两条接连触发，按顺序各完整播一次；
/// 4 得到泥肝时字幕条被占着，空下来后仍出现 R 提示；5 待播时存读档、播到一半存读档都不会漏（后者重播一次）；
/// 6 被主剧情打断后重播；6b 被另一条字幕顶掉后重播（两种分开测）；7 第二次得到泥肝不再提示，看过的提示读档后不再出现；
/// 8 提示里写的按键和 project.godot 的绑定一致。全程不允许在主剧情进行中开始播提示，零报错。
/// </summary>
public partial class TutorialTest : NarrativeFlowBase
{
    private const string FixturePath = "res://tests/narrative/fixtures/tutorial_test.orph";
    private const string Fixture = "tutorial_test";

    protected override string SuiteName => "narrative_tutorial";
    protected override bool NarrativeOpening => true;
    protected override bool AutoStories => true;
    protected override bool TutorialHintsOn => true;

    private readonly List<string> _hints = new();
    private readonly List<string> _violations = new();

    private static ProgressStore P => GameSession.Progress;
    private static bool Seen(string label) => TutorialHints.Seen(label);
    private int Count(string label) => _hints.Count(h => h == label);

    private void OnHint(string label)
    {
        _hints.Add(label);
        if (Narr != null && Narr.MainActive) _violations.Add($"{label}：主剧情 {Narr.MainLabel} 还在播");
    }

    protected override async Task RunChecks()
    {
        Listen();
        TutorialHints.HintStarted += OnHint;
        try
        {
            CheckBindings();
            int errorBase = Errors.Count;
            int narrativeErrors = NarrativeLog.ErrorCount;

            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(finished && Narr != null && Narr.Tutorial != null && World.CurrentRoomDef?.Id == "S1_01", "正式入口开新游戏，人在 S1_01，剧情层下面有引导节点");
            if (Narr?.Tutorial == null) return;
            OrphLibrary.Shared.Add(OrphLibrary.LoadFile(FixturePath), FixturePath);

            await OpeningThenMove();
            await DodgeAndTablet();
            await AttackInterrupted();
            await BlockAndLiver();
            await BottleSaveLoad();

            Check(_violations.Count == 0, "全程：主剧情进行中从不开始播提示" + (_violations.Count > 0 ? "：" + string.Join(" | ", _violations) : ""));
            Check(Errors.Count == errorBase && NarrativeLog.ErrorCount == narrativeErrors,
                "全程零报错" + (Errors.Count > errorBase ? "：" + string.Join(" | ", Errors.Snapshot().Skip(errorBase).Take(5)) : ""));
            Check(NoPauseOrLock() && !Narr.MainActive, "最后没有剧情在播，没有暂停和输入锁残留" + Diag());
        }
        finally
        {
            TutorialHints.HintStarted -= OnHint;
            Unlisten();
        }
    }

    // ---------- 8：按键文字和绑定 ----------

    private static readonly (string label, string key, string action)[] KeyTable =
    {
        ("移动", "A", "move_left"), ("移动", "D", "move_right"), ("移动", "空格", "jump"),
        ("攀墙", "W", "move_up"), ("攀墙", "空格", "jump"),
        ("闪避", "Shift", "dodge"), ("闪避", "Esc", "menu"), ("闪避", "M", "map"),
        ("攻击", "J", "attack"), ("攻击", "S", "move_down"),
        ("格挡", "K", "block"), ("格挡", "Shift", "dodge"),
        ("收集页", "Tab", "collection"),
        ("泥肝", "R", "throw_item"),
        ("泥瓶", "Tab", "collection"), ("泥瓶", "E", "interact"),
    };

    private static string KeyText(InputEventKey key)
    {
        Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        return code switch
        {
            Key.Space => "空格",
            Key.Escape => "Esc",
            Key.Shift => "Shift",
            Key.Tab => "Tab",
            _ => OS.GetKeycodeString(code),
        };
    }

    private static Dictionary<string, string> HintTexts()
    {
        var texts = new Dictionary<string, string>();
        string source = FileAccess.GetFileAsString("res://assets/story/tutorial.orph");
        foreach (string block in source.Split("### ").Skip(1))
        {
            string[] lines = block.Replace("\r", "").Split('\n');
            string body = string.Join("\n", lines.Skip(1).Where(l => l.Length > 0 && !l.StartsWith("*") && !l.StartsWith("[") && !l.EndsWith("：")));
            texts[lines[0].Trim()] = body;
        }
        return texts;
    }

    private void CheckBindings()
    {
        Dictionary<string, string> texts = HintTexts();
        string[] labels = TutorialHints.Hints.Select(h => h.Label).ToArray();
        string[] missing = labels.Where(l => !texts.ContainsKey(l) || texts[l].Length == 0).ToArray();
        Check(missing.Length == 0 && texts.Count == labels.Length, $"8：tutorial.orph 里每条提示都有标签和文字，和引导表一一对应（缺 {string.Join(",", missing)}，剧本 {texts.Count} 条）");
        var problems = new List<string>();
        foreach (var (label, key, action) in KeyTable)
        {
            if (!texts.TryGetValue(label, out string text) || !text.Contains(key)) problems.Add($"{label} 里没写 {key}");
            string[] bound = InputMap.HasAction(action) ? InputMap.ActionGetEvents(action).OfType<InputEventKey>().Select(KeyText).ToArray() : Array.Empty<string>();
            if (!bound.Contains(key)) problems.Add($"{label} 写的 {key}，但 {action} 绑的是 {string.Join("/", bound)}");
        }
        Check(problems.Count == 0, "8：提示里写的按键和 project.godot 的绑定一致" + (problems.Count > 0 ? "：" + string.Join(" | ", problems) : ""));
    }

    // ---------- 1：开场播完以后出现移动提示 ----------

    private async Task OpeningThenMove()
    {
        await WaitUntil(() => Narr.MainActive, 120);
        Check(Narr.MainLabel == "开场" && NarrativePaused, "S1_01：新游戏自动播开场");
        await Frames(30);
        Check(_hints.Count == 0, "1：开场在播时不播任何提示（开场暂停了游戏，引导区要等开场结束、游戏恢复后才记下）");
        Check(await FinishDialog(), "S1_01：按 E 播完开场");
        bool shown = await WaitUntil(() => Narr.Subtitle.IsShowing && Narr.BarkScript == TutorialHints.ScriptId, 240);
        await WaitUntil(() => Narr.Subtitle.CurrentText == "A / D 移动，空格 跳跃。", 240);
        Check(shown && _hints.SequenceEqual(new[] { "移动" }) && Narr.Subtitle.CurrentText == "A / D 移动，空格 跳跃。",
            $"1：开场播完后字幕条出现移动提示（{string.Join(",", _hints)}：{Narr.Subtitle.CurrentText}）");
        Check(NoPauseOrLock(), "1：提示不暂停游戏、不锁操作");
        Check(await WaitUntil(() => Seen("移动"), 900), "1：移动提示正常播完，记为看过");
    }

    // ---------- 闪避（换房间不影响）和 2：泥板 ----------

    private async Task DodgeAndTablet()
    {
        bool due = await HoldUntil(PlayerActions.Right, () => P.Flag(TutorialHints.DueKey("闪避")), 300);
        Check(due, "S1_01：往右走到出泥坑前，记下闪避提示该播了");
        bool inS102 = await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == "S1_02", 300);
        Check(inS102 && _hints.Contains("闪避"), $"S1_01 → S1_02：闪避提示开始播（{string.Join(",", _hints)}）");

        // S1_02：走进坑里自动播“倒影”，再拾取泥板
        bool reflected = await HoldUntil(PlayerActions.Right, () => Narr.MainActive, 600);
        Check(reflected && Narr.MainLabel == "倒影", "S1_02：走进坑里自动播“倒影”");
        Check(await FinishDialog(), "S1_02：倒影播完");
        Check(await WalkToPrompt("拾取泥板", PlayerActions.Right), "S1_02：走到坑底的泥板旁");
        await KeyTap(Key.E);
        bool open = await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        await Frames(60);
        Check(open && P.Flag("tablet.nameless_day") && !_hints.Contains("收集页") && Narr.Tablet.IsOpen,
            "2：读泥板时已经捡到（tablet.nameless_day），但阅读界面开着、剧情没结束，不播 Tab 提示");
        Check(await FinishDialog(5), "2：按 E 收起泥板，剧情结束");
        bool tab = await WaitUntil(() => _hints.Contains("收集页"), 1800);
        Check(tab, $"2：关掉阅读界面以后出现 Tab 提示（{string.Join(",", _hints)}）");
        Check(await WaitUntil(() => Seen("收集页") && Seen("闪避"), 1800), "2：Tab 提示、闪避提示都正常播完（闪避被“倒影”打断的话会重播）");
        Check(Count("移动") == 1, "看过的移动提示没有再出现");
    }

    private async Task WaitAllQuiet()
    {
        await WaitUntil(() => TutorialHints.PendingLabels().Count == 0 && !Narr.Bark.IsPlaying && !Narr.MainActive, 2400);
        await Frames(10);
    }

    // ---------- 6b 和 6：攻击提示被字幕顶掉、被主剧情打断 ----------

    private async Task AttackInterrupted()
    {
        await WaitAllQuiet();
        bool inS103 = await CrossEdge("S1_02", "From_S1_03", "S1_03");
        Check(inS103, "S1_02 → S1_03");
        // 入口往右不远有一面墙（攀墙提示就是在这里第一次出现的），像玩家一样按住右和上爬过去、隔一会儿跳一下
        bool due = await ClimbRightUntil(() => P.Flag(TutorialHints.DueKey("攻击")), 900);
        bool occupied = Narr.Bark.IsPlaying;
        Check(due && occupied && !_hints.Contains("攻击") && Requests.Contains("m_pit:朝圣者之路"),
            $"S1_03：走到祈祷泥人前记下攻击提示；朝圣者的字幕已经发出，这时字幕条还占着（{Narr.BarkScript}:{Narr.BarkLabel}），不播（记下 {due}，玩家 x {LocalX:0}）" + Diag());
        // 等提示的时候回到 S1_01 空地上，免得站在泥人堆里挨打（死了会锁输入，提示也会一直等）；字幕不会因为换房间停下
        await Teleport("S1_01", "Default");
        Check(await WaitUntil(() => Narr.Tutorial.Playing == "攻击", 1800), "S1_03：字幕条空下来、空了一小会儿后播攻击提示" + Diag());

        // 6b：被另一条字幕顶掉
        await Frames(20);
        Narr.Request(Fixture, "插话");
        await Frames(1);
        Check(Narr.BarkLabel == "插话" && Narr.Tutorial.Playing != "攻击" && !Seen("攻击"), "6b：另一条字幕顶掉正在播的攻击提示，不算看过");
        Check(await WaitUntil(() => Count("攻击") == 2, 900), "6b：那条字幕播完后攻击提示重新出现" + Diag());

        // 6：被主剧情打断
        await Frames(20);
        Check(Narr.Tutorial.Playing == "攻击", "6：攻击提示正在重播");
        Narr.Request(Fixture, "对话");
        await Frames(2);
        Check(Narr.MainActive && !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing && !Seen("攻击"), "6：主剧情开始，字幕收起，攻击提示不算看过");
        Check(await FinishDialog(), "6：播完这段对话");
        Check(await WaitUntil(() => Count("攻击") == 3, 600) && await WaitUntil(() => Seen("攻击"), 900), "6：剧情结束后攻击提示再出现并正常播完");
    }

    // ---------- 3 和 4：格挡、泥肝接连触发，泥肝在字幕条被占着时得到 ----------

    private async Task BlockAndLiver()
    {
        await WaitAllQuiet();
        Narr.Request(Fixture, "占用");
        await Teleport("S1_04", "From_S1_03");
        bool due = await HoldUntil(PlayerActions.Right, () => P.Flag(TutorialHints.DueKey("格挡")), 300);
        P.SetVar(ItemCounts.Key(ItemObtainedRule.Liver), 1);
        await Teleport("S1_01", "Default");
        int before = _hints.Count;
        Check(due && P.Flag(ItemObtainedRule.Key(ItemObtainedRule.Liver)) && Narr.Bark.IsPlaying && Narr.BarkLabel == "占用",
            "4：S1_04 记下格挡提示，同时得到泥肝；这时字幕条被别的字幕占着");
        await Frames(30);
        Check(_hints.Count == before, "4：字幕条占着时两条都不播");
        bool done = await WaitUntil(() => Seen("格挡") && Seen("泥肝"), 2400);
        string[] after = _hints.Skip(before).ToArray();
        Check(done && after.SequenceEqual(new[] { "格挡", "泥肝" }), $"3/4：字幕条空了以后两条按顺序各播一次并且都正常播完（{string.Join(",", after)}）");
    }

    // ---------- 5 和 7：泥瓶提示待播时存读档、播到一半存读档 ----------

    private async Task BottleSaveLoad()
    {
        await WaitAllQuiet();
        Narr.Request(Fixture, "占用");
        P.SetVar(ItemObtainedRule.Key("泥瓶"), true);
        bool saved = Saves.SaveToDisk("S1_04");
        Check(saved && !_hints.Contains("泥瓶"), "5：得到泥瓶时字幕条占着，提示还没播就存档");

        Check(await Continue(), "5：读档");
        await Teleport("S1_01", "Default");
        await Frames(30);
        Check(await WaitUntil(() => Narr.Tutorial.Playing == "泥瓶", 600) && Count("泥瓶") == 1, "5：读档后泥瓶提示照样出现，没有漏掉");
        await Frames(20);
        Check(Saves.SaveToDisk("S1_04"), "5：泥瓶提示播到一半时存档");
        Check(await Continue(), "5：再读档");
        await Teleport("S1_01", "Default");
        Check(await WaitUntil(() => Count("泥瓶") == 2, 600) && await WaitUntil(() => Seen("泥瓶"), 900), "5：读档后泥瓶提示重播一次并正常播完");

        int before = _hints.Count;
        P.SetVar(ItemCounts.Key(ItemObtainedRule.Liver), 2);
        await Frames(180);
        Check(_hints.Count == before, "7：第二次得到泥肝不再提示");
        string[] replayed = _hints.Where(h => h != "泥瓶").GroupBy(h => h).Where(g => g.Key != "攻击" && g.Key != "闪避" && g.Count() > 1).Select(g => g.Key).ToArray();
        Check(replayed.Length == 0, $"7：看过的提示读档后没有再出现（重复的 {string.Join(",", replayed)}）");
    }

    private async Task<bool> Continue()
    {
        bool finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        return finished && Narr?.Tutorial != null && World.CurrentRoomDef != null;
    }

    // ---------- 工具 ----------

    private string Diag() =>
        $"［提示 {string.Join(",", _hints)}；正在播 {Narr?.Tutorial?.Playing}；待播 {string.Join(",", TutorialHints.PendingLabels())}；主剧情 {Narr?.MainActive}；"
        + $"字幕 {Narr?.Bark.IsPlaying}:{Narr?.BarkLabel}；暂停 {PauseService.IsPaused}；锁输入 {InputLock.IsLocked}；房间 {World?.CurrentRoomDef?.Id} x {(World?.CurrentRoom != null && Player != null ? LocalX : 0):0}；玩家状态 {Player?.Machine?.Current?.GetType().Name}］";

    private async Task<bool> FinishDialog(int maxTaps = 30)
    {
        bool done = await AdvanceUntil(() => !Narr.MainActive, maxTaps);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await Frames(3);
        return done && NoPauseOrLock();
    }

    private async Task<bool> ClimbRightUntil(Func<bool> condition, int maxFrames)
    {
        Input.ActionPress(PlayerActions.Right);
        Input.ActionPress(PlayerActions.Up);
        bool ok = false;
        for (int i = 0; i < maxFrames; i++)
        {
            if (condition()) { ok = true; break; }
            if (i % 40 == 0) Input.ActionPress(PlayerActions.Jump);
            if (i % 40 == 12) Input.ActionRelease(PlayerActions.Jump);
            await Frames(1);
        }
        Input.ActionRelease(PlayerActions.Jump);
        Input.ActionRelease(PlayerActions.Up);
        Input.ActionRelease(PlayerActions.Right);
        return ok || condition();
    }

    private async Task<bool> CrossEdge(string from, StringName spawn, string to)
    {
        await Teleport(from, spawn);
        return await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == to, 300);
    }
}
