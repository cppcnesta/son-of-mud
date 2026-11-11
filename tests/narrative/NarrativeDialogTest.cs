using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.World;
using SonOfMud.Player;

/// <summary>
/// 计划第 4 节第 3、4 步（从正式入口 main_game.tscn 启动，真实玩家）：
/// ① 开场：新游戏进入 S1_01 自动播开场（黑屏 CG + 对话框），暂停游戏；旁白不显示名牌，？？？ 没有立绘、主角有立绘；
///    按 E 一路推进到苏醒结束；seen.opening 写入；开场只发一次 Started / Finished。
/// ② 对话框 + 暂停 + 推进键：真实玩家走到守坑人老人面前按 E → 游戏暂停（PauseService 有 narrative 来源）、开始打字；
///    按住 E 不放也不会跳过第一句（打开对话的那一下被吞掉）；对话结束后恢复操作，结束对话的那一下 E 不会再触发一次对话；
///    NarrativeStarted / Finished 各发一次。
/// ③ 字幕条：跑进 S1_03 触发插话 → 不暂停、不锁输入、玩家一直在跑，字幕自动消失；看过之后再进来不再触发；
///    字幕播放中开始对话框剧情 → 字幕立刻收起。
/// </summary>
public partial class NarrativeDialogTest : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_dialog";
    protected override bool NarrativeOpening => true;

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(finished && Starts.Count == 1 && Starts[0].success && Narr != null && Interactor != null, "正式入口开新游戏成功，main_game 里有剧情层 NarrativeLayer");
            if (Narr == null || Interactor == null) return;
            await OpeningChecks();
            await NpcChecks();
            await SubtitleChecks();
            Check(Errors.Count == 0, "全程没有报错" + (Errors.Count > 0 ? "：" + string.Join(" | ", Errors.Snapshot().Take(5)) : ""));
        }
        finally
        {
            Unlisten();
        }
    }

    private async Task OpeningChecks()
    {
        await WaitUntil(() => Narr.MainActive, 120);
        await Frames(5);
        Check(Narr.MainActive && Narr.MainScript == "m_pit" && Narr.MainLabel == "开场" && Started.SequenceEqual(new[] { "m_pit:开场" }),
            $"开场：进入 S1_01 后自动播 m_pit「开场」（Started：{string.Join(",", Started)}）");
        Check(NarrativePaused && Narr.HoldsPause, "开场：对话框剧情暂停游戏（PauseService 有 narrative 来源）");
        Check(Narr.Cg.IsShowing && Narr.Cg.CurrentId == "black", "开场：黑屏（CG black）");
        Check(Narr.Dialog.IsOpen && !Narr.Dialog.NameVisible && Narr.Dialog.CurrentText.StartsWith("我是父亲用血和泥捏出的第一个。") && !Narr.Dialog.PortraitVisible,
            $"开场：对话框显示旁白第一句，旁白不显示名牌、没有立绘（{Narr.Dialog.CurrentText}）");

        var seen = new List<(string name, string text, bool portrait)>();
        void Observe() => seen.Add((Narr.Dialog.CurrentName, Narr.Dialog.CurrentText.Split('\n').Last(), Narr.Dialog.PortraitVisible));
        bool done = await AdvanceUntil(() => !Narr.MainActive, 40, Observe);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        await Frames(3);
        Check(done && Ended.SequenceEqual(new[] { "m_pit:开场" }), $"开场：按 E 一路推进到苏醒结束，Finished 一次（{string.Join(",", Ended)}，按了 {seen.Count} 次）");
        var wake = seen.FirstOrDefault(s => s.text == "——我的儿子，醒来。");
        var son = seen.FirstOrDefault(s => s.text == "父亲？您在哪里？");
        var last = seen.LastOrDefault();
        Check(wake.name == "？？？" && !wake.portrait, "开场：“——我的儿子，醒来。”名牌是 ？？？，没有立绘");
        Check(son.name == "穆尔图库尔" && son.portrait, "苏醒：“父亲？您在哪里？”名牌是 穆尔图库尔，显示主角立绘（占位）");
        Check(last.text == "是，父亲。", $"苏醒：最后一句是“是，父亲。”（{last.text}）");
        int wakeAt = seen.FindIndex(s => s.text == "——我的儿子，醒来。");
        int sonAt = seen.FindIndex(s => s.text == "父亲？您在哪里？");
        Check(wakeAt >= 0 && sonAt > wakeAt, "开场旁白之后接着播苏醒");
        Check(GameSession.Progress.Flag("seen.opening") && GameSession.Progress.Flag("seen.m_pit.开场"), "开场：播完写 seen.opening 和 seen.m_pit.开场");
        Check(!Narr.Cg.IsShowing && !Narr.Dialog.IsOpen, "开场：播完 CG 和对话框都收起");
        Check(NoPauseOrLock() && !Narr.HoldsPause, "开场：播完释放暂停，没有输入锁");

        // 再进一次 S1_01 不会重播
        int before = Started.Count;
        await Teleport("S1_01", "Default");
        await Frames(10);
        Check(Started.Count == before && !Narr.MainActive, "开场：seen.opening 已设，再进入 S1_01 不重播");
    }

    private async Task NpcChecks()
    {
        await Teleport("S1_05", "From_S1_04");
        Check(Interactor.CurrentPrompt == "", "守坑人：打赢之前老人不在（S1_05 入口没有交互提示）");
        GameSession.Progress.SetVar("boss_keeper", true);   // 打 Boss 用设变量代替
        await Frames(2);
        bool atNpc = await WalkToPrompt("交谈", PlayerActions.Right);
        Check(atNpc && Interactor.Current is NpcTalk, $"守坑人：boss_keeper 之后老人出现，真实玩家走过去出现“交谈”提示（x={LocalX:F0}）");
        if (!atNpc) return;

        int started = Started.Count, ended = Ended.Count;
        KeyDown(Key.E);
        await WaitUntil(() => Narr.MainActive, 5);
        bool paused = NarrativePaused;
        bool firstLine = Narr.MainActive && Narr.MainLabel == "守坑人" && Narr.Dialog.CurrentName == "守坑人" && Narr.Dialog.CurrentText == "祂……醒了……";
        int linesAtPress = Narr.Dialog.LineCount;
        Check(paused && firstLine && Started.Count == started + 1, $"守坑人：按 E → 游戏暂停、对话框开始打第一句“祂……醒了……”（{Narr.Dialog.CurrentName}：{Narr.Dialog.CurrentText}）");
        Check(Narr.Dialog.PortraitVisible, "守坑人：显示守坑人立绘（占位）");
        await Frames(30);   // 一直按着 E
        bool notSkipped = Narr.Dialog.LineCount == linesAtPress && Narr.Dialog.CurrentText == "祂……醒了……" && Narr.MainActive && Narr.Main.WaitingForAdvance;
        KeyUp(Key.E);
        await Frames(5);
        Check(Narr.Dialog.LineCount == linesAtPress && Narr.Dialog.CurrentText == "祂……醒了……" && Narr.MainActive && notSkipped,
            "守坑人：打开对话的那一下 E 被吞掉（按住 30 帧再松开，仍停在第一句等推进）");

        await KeyTap(Key.E);
        await Frames(3);
        Check(Narr.Dialog.CurrentName == "穆尔图库尔" && Narr.Dialog.CurrentText == "……父亲在哪儿？", $"守坑人：松开再按 E 才推进到下一句（{Narr.Dialog.CurrentName}：{Narr.Dialog.CurrentText}）");
        await WaitUntil(ReadyForKey, 120);

        // 用 E 结束对话：松开之前保持暂停，松开后才恢复，这一下不会再触发一次交谈
        KeyDown(Key.E);
        await Frames(2);
        bool endedWhileHeld = !Narr.MainActive && Ended.Count == ended + 1;
        bool stillPausedWhileHeld = NarrativePaused && Narr.ReleasePending;
        KeyUp(Key.E);
        await Frames(10);
        Check(endedWhileHeld && stillPausedWhileHeld, "守坑人：最后一下 E 结束对话；按键松开之前保持暂停（这一下不会被玩家拿去再交互）");
        Check(NoPauseOrLock() && Started.Count == started + 1 && Ended.Count == ended + 1 && Ended[^1] == "m_pit:守坑人",
            $"守坑人：松开后恢复，NarrativeStarted / Finished 各发一次，没有再触发一次交谈（Started {Started.Count - started}，Finished {Ended.Count - ended}）");
        Check(GameSession.Progress.Flag("talked.keeper") && GameSession.Progress.Flag("seen.m_pit.守坑人"), "守坑人：第一次对话完写 talked.keeper");

        float x0 = LocalX;
        await HoldUntil(PlayerActions.Left, () => LocalX < x0 - 60, 60);
        Check(LocalX < x0 - 60, $"守坑人：对话结束后恢复操作，玩家能走（{x0:F0} → {LocalX:F0}）");
    }

    private async Task SubtitleChecks()
    {
        await Teleport("S1_03", "From_S1_02");
        int started = Started.Count;
        float x0 = LocalX;
        Input.ActionPress(PlayerActions.Right);
        bool shown = await WaitUntil(() => Narr.Subtitle.IsShowing, 240);
        bool running = true, neverPaused = true, neverLocked = true;
        float xShown = LocalX;
        string text = Narr.Subtitle.CurrentText, name = Narr.Subtitle.CurrentName;
        for (int i = 0; i < 40; i++)
        {
            await Frames(1);
            neverPaused &= !GetTree().Paused && !PauseService.IsPaused;
            neverLocked &= !InputLock.IsLocked;
        }
        running = LocalX > xShown + 100;
        Input.ActionRelease(PlayerActions.Right);
        Check(shown && name == "？？？" && text == "杀了他们！" && Narr.BarkLabel == "朝圣者之路", $"字幕：跑进 S1_03 触发插话“？？？：杀了他们！”（{name}：{text}）");
        Check(neverPaused && neverLocked && running && Started.Count == started, $"字幕：不暂停、不锁输入，玩家一直在跑（{xShown:F0} → {LocalX:F0}），不发 NarrativeStarted");
        bool hidden = await WaitUntil(() => !Narr.Subtitle.IsShowing && !Narr.Bark.IsPlaying, 400);
        Check(hidden && GameSession.Progress.Flag("seen.m_pit.朝圣者之路"), "字幕：停留一会儿后自动消失，写 seen.m_pit.朝圣者之路");

        int requests = Requests.Count;
        await Teleport("S1_03", "From_S1_02");
        await HoldUntil(PlayerActions.Right, () => LocalX > 700, 200);
        await Frames(10);
        Check(Requests.Count == requests && !Narr.Subtitle.IsShowing, "字幕：看过之后再跑进来不再触发（Once）");

        // 字幕播放中开始对话框剧情 → 字幕立刻收起
        Request("m_pit", "朝圣者之路");
        await Frames(3);
        bool barkOn = Narr.Subtitle.IsShowing && Narr.Bark.IsPlaying && Narr.Subtitle.CurrentText == "杀了他们！";
        Request("m_pit", "倒影");
        bool barkGone = !Narr.Subtitle.IsShowing && !Narr.Bark.IsPlaying && Narr.MainActive;
        Check(barkOn && barkGone, "字幕：字幕播放中对话框剧情开始 → 字幕当帧收起");
        await Frames(2);
        Check(Narr.Dialog.IsOpen && Narr.Dialog.CurrentText == "……水太浑了。" && NarrativePaused, "字幕：对话框剧情照常开始（倒影：“……水太浑了。”）");
        Request("m_pit", "两河源头");
        await Frames(2);
        Check(!Narr.Subtitle.IsShowing && !Narr.Bark.IsPlaying, "字幕：对话框剧情进行中，新的插话请求被丢掉");
        await AdvanceUntil(() => !Narr.MainActive, 10);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        Check(NoPauseOrLock(), "字幕：对话框剧情结束后没有暂停和输入锁残留");
    }
}
