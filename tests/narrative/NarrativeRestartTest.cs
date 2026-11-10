using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.Player;

/// <summary>
/// 计划第 4 节第 9 步（v2.2）：结局分支的框架（不依赖地图四剧本，用测试剧本 test_story「结局」）。
/// 从正式入口开新游戏（开场照常播完），然后播“结局”，选“回去睡”：
/// - 发 RestartRunRequested 恰好一次；剧情系统不发 NewGameRequested（重开前一个都没有，之后只有新的 GameBoot 发的那一次）；
/// - 重开后：旧剧情层和两个旧执行器实例都已释放；旧剧本、旧选项没有残留；“回去睡”那一段的暂停已经释放；
/// - 新实例的开场剧情正常开始（新一局开场合法地请求暂停，不算残留）、按 E 正常播完；开场结束后暂停来源里不再有 narrative。
/// </summary>
public partial class NarrativeRestartTest : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_restart";
    protected override bool NarrativeOpening => true;

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(finished && Narr != null, "第一局：正式入口开新游戏成功");
            if (Narr == null) return;
            await WaitUntil(() => Narr.MainActive, 120);
            await AdvanceUntil(() => !Narr.MainActive, 40);
            await WaitUntil(() => !PauseService.IsPaused, 60);
            Check(Ended.SequenceEqual(new[] { "m_pit:开场" }) && NoPauseOrLock(), "第一局：开场播完，恢复游戏");
            await WakeBranchCheck();
            await SleepBranchChecks();
            Check(Errors.Count == 0, "全程没有报错" + (Errors.Count > 0 ? "：" + string.Join(" | ", Errors.Snapshot().Take(5)) : ""));
        }
        finally
        {
            Unlisten();
        }
    }

    /// <summary>对照：选“醒来”只走醒来分支，不重开。</summary>
    private async Task WakeBranchCheck()
    {
        LoadTestScript();
        Request("test_story", "结局");
        await WaitUntil(() => Narr.Choice.IsOpen, 240);
        Check(Narr.Choice.Options.SequenceEqual(new[] { "醒来", "回去睡" }), "结局：选项 醒来 / 回去睡");
        await KeyTap(Key.E);
        await WaitUntil(() => !Narr.MainActive && !PauseService.IsPaused, 60);
        Check(GameSession.Progress.GetVar("test.ending").AsString() == "醒来" && Restarts == 0, "结局：选“醒来”走醒来分支，不重开");
    }

    private async Task SleepBranchChecks()
    {
        Node oldScene = MainGame;
        NarrativeDirector oldNarr = Narr;
        ulong oldDirector = Narr.GetInstanceId();
        ulong oldMain = Narr.Main.GetInstanceId();
        ulong oldBark = Narr.Bark.GetInstanceId();
        ulong oldChoice = Narr.Choice.GetInstanceId();
        int restarts = Restarts, newGames = NewGames, starts = Starts.Count, startedCount = Started.Count;
        int restartsBeforeSceneGone = -1, newGamesBeforeSceneGone = -1;

        Request("test_story", "结局");
        await WaitUntil(() => Narr.Choice.IsOpen, 240);
        await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.Dialog.CurrentText == "——我的儿子，醒来。", 60);
        Check(oldNarr.MainLabel == "结局" && oldNarr.Cg.CurrentId == "black" && NarrativePaused, "回去睡：黑屏，“？？？：——我的儿子，醒来。”");
        await WaitUntil(ReadyForKey, 120);
        bool heldPauseAtRestart = true;
        string lastEndedAtRestart = "";
        void OnRestartRequested()
        {
            heldPauseAtRestart = oldNarr.HoldsPause || oldNarr.MainActive;
            lastEndedAtRestart = Ended.LastOrDefault();
        }
        Events.Instance.RestartRunRequested += OnRestartRequested;
        KeyDown(Key.E);
        bool requested = await WaitUntil(() => Restarts > restarts, 10);
        KeyUp(Key.E);
        Events.Instance.RestartRunRequested -= OnRestartRequested;
        restartsBeforeSceneGone = Restarts - restarts;
        newGamesBeforeSceneGone = NewGames - newGames;
        Check(requested && restartsBeforeSceneGone == 1 && newGamesBeforeSceneGone == 0,
            $"回去睡：发 RestartRunRequested 恰好一次，剧情系统没有发 NewGameRequested（重开 {restartsBeforeSceneGone}，新游戏 {newGamesBeforeSceneGone}）");
        Check(lastEndedAtRestart == "test_story:结局" && !heldPauseAtRestart, "回去睡：发重开请求时这段剧情已经结束（先发了 NarrativeFinished），剧情层已经释放自己的暂停");

        bool rebuilt = await WaitUntil(() => Starts.Count > starts && GetTree().CurrentScene != oldScene && GetTree().CurrentScene != null, 600);
        BindMainGame();
        BindNarrative();
        Check(rebuilt && MainGame != null && MainGame != oldScene && Starts[^1].success, "重开：World 重新载入正式入口（新的 main_game 实例），开局成功");
        if (Narr == null) return;
        Check(Restarts == restarts + 1 && NewGames == newGames + 1 && Boot.StartedWith == StartMode.NewGame,
            $"重开：整个过程 RestartRunRequested 一次；NewGameRequested 只有新 GameBoot 发的那一次（{NewGames - newGames}）");
        Check(!GodotObject.IsInstanceIdValid(oldDirector) && !GodotObject.IsInstanceIdValid(oldMain) && !GodotObject.IsInstanceIdValid(oldBark) && !GodotObject.IsInstanceIdValid(oldChoice),
            "重开：旧剧情层、旧的两个执行器、旧选项面板都已释放");
        Check(Narr.GetInstanceId() != oldDirector && FindAll<NarrativeDirector>().Count() == 1, "重开：场景里只有一个新的剧情层");

        await WaitUntil(() => Narr.MainActive, 120);
        await Frames(3);
        Check(Narr.MainActive && Narr.MainScript == "m_pit" && Narr.MainLabel == "开场" && Started.Skip(startedCount).Last() == "m_pit:开场",
            $"重开：新实例从 S1_01 开场重新开始（{Narr.MainScript}「{Narr.MainLabel}」）");
        Check(!Narr.Choice.IsOpen && Narr.Choice.Options.Count == 0 && Narr.Main.Script?.Id == "m_pit" && !Narr.Bark.IsPlaying && !GameSession.Progress.HasVar("test.ending") && !GameSession.Progress.HasVar("choice.结局"),
            "重开：旧剧本、旧选项没有残留（新执行器播的是 m_pit，选项面板关着，上一局的变量清空）");
        Check(NarrativePaused && Narr.HoldsPause, "重开：新一局的开场合法地请求暂停（narrative 来源属于新实例）");
        Check(World.CurrentRoomDef?.Id == "S1_01", "重开：人在 S1_01");

        bool done = await AdvanceUntil(() => !Narr.MainActive, 40);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await Frames(5);
        Check(done && Ended.LastOrDefault() == "m_pit:开场" && GameSession.Progress.Flag("seen.opening"), "重开：新的开场按 E 正常播完");
        Check(!PauseService.IsRequestedBy(PauseService.Narrative) && !GetTree().Paused && !InputLock.IsLocked && !Narr.HoldsPause,
            "重开：开场结束后暂停来源里不再有 narrative，游戏没有暂停、没有输入锁");
    }

    private System.Collections.Generic.IEnumerable<T> FindAll<T>() where T : Node
    {
        var found = new System.Collections.Generic.List<T>();
        void Walk(Node node)
        {
            if (node is T t) found.Add(t);
            foreach (Node child in node.GetChildren()) Walk(child);
        }
        Walk(GetTree().Root);
        return found;
    }
}
