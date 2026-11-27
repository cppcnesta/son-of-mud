using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;

/// <summary>
/// 计划第 4 节第 8 步：地图一剧本的集成测试。从正式入口 main_game.tscn 开新游戏，真实玩家用真实按键从 S1_01 走到 S1_07：
/// 开场 → 走进 S1_02 坑里自动播“倒影”、坑底拾取泥板「当天还没有名字」 → S1_03 朝圣者插话（字幕）→ S1_04 供桌（选吃下）
/// → S1_05 守坑人（打 Boss 用设变量 boss_keeper 代替）→ S1_06 七夜雕像（CG1）→ S1_07 两河源头（字幕）；
/// 然后回溯 S1_08：重塑雕像（CG2）和泥板「神子」。所有剧情按顺序触发，零报错。
/// 房间骨架里还没有平台：出口在高处的房间（S1_02、S1_03、S1_05、S1_06）先把人放到出口平台的出生点，再按住方向键走过房间边缘。
/// </summary>
public partial class NarrativeMap1Test : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_map1";
    protected override bool NarrativeOpening => true;

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            GD.PushError("narrative_map1 日志钩子自检");
            await Frames(1);
            Check(Errors.Count == 1 && Errors.Snapshot()[0].Contains("日志钩子自检"), "前提：测试的日志钩子能收到报错（自检，不计入后面的零报错）");
            int errorBase = Errors.Count;
            int narrativeErrors = NarrativeLog.ErrorCount;

            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(finished && Narr != null && Interactor != null && World.CurrentRoomDef?.Id == "S1_01", "正式入口开新游戏，人在 S1_01");
            if (Narr == null || Interactor == null) return;
            await Walk();

            string[] mains = Started.ToArray();
            string[] expectedMains = { "m_pit:开场", "m_pit:倒影", "tablets:nameless_day", "m_pit:供桌", "m_pit:守坑人", "m_pit:七夜", "m_pit:重塑", "tablets:divine_son" };
            Check(mains.SequenceEqual(expectedMains), $"顺序：对话框剧情依次是 {string.Join(" → ", expectedMains)}（实际 {string.Join(" → ", mains)}）");
            Check(Ended.SequenceEqual(expectedMains), $"顺序：每段都正常播完，Finished 与 Started 一一对应（{string.Join(",", Ended)}）");
            string[] requests = Requests.ToArray();
            string[] expectedRequests = { "m_pit:倒影", "tablets:nameless_day", "m_pit:朝圣者之路", "m_pit:供桌", "m_pit:守坑人", "m_pit:七夜", "m_pit:两河源头", "m_pit:重塑", "tablets:divine_son" };
            Check(requests.SequenceEqual(expectedRequests), $"顺序：房间里的触发器依次发出请求（含两段字幕）：{string.Join(" → ", requests)}");
            string[] seen = { "seen.opening", "seen.m_pit.开场", "seen.tablets.nameless_day", "seen.m_pit.倒影", "seen.m_pit.朝圣者之路", "seen.m_pit.供桌", "seen.m_pit.守坑人",
                              "seen.m_pit.七夜", "seen.m_pit.两河源头", "seen.m_pit.重塑", "tablet.nameless_day", "tablet.divine_son", "item.生命之面包", "talked.keeper" };
            string[] missing = seen.Where(k => !GameSession.Progress.Flag(k)).ToArray();
            Check(missing.Length == 0, $"进度：地图一的剧情变量都写好了（缺 {string.Join(",", missing)}）");
            Check(Collected.SequenceEqual(new[] { "nameless_day", "divine_son" }), $"进度：拾取了两块泥板（{string.Join(",", Collected)}）");
            Check(Errors.Count == errorBase && NarrativeLog.ErrorCount == narrativeErrors,
                "全程零报错" + (Errors.Count > errorBase ? "：" + string.Join(" | ", Errors.Snapshot().Skip(errorBase).Take(5)) : ""));
            Check(NoPauseOrLock() && !Narr.MainActive && !Narr.Bark.IsPlaying, "最后没有剧情在播，没有暂停和输入锁残留");
        }
        finally
        {
            Unlisten();
        }
    }

    private async Task<bool> FinishDialog(int maxTaps = 30)
    {
        bool done = await AdvanceUntil(() => !Narr.MainActive, maxTaps);
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await Frames(3);
        return done && NoPauseOrLock();
    }

    private async Task<bool> CrossEdge(string from, StringName spawn, string to)
    {
        await Teleport(from, spawn);
        return await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == to, 300);
    }

    private async Task Walk()
    {
        // S1_01：开场
        await WaitUntil(() => Narr.MainActive, 120);
        Check(Narr.MainLabel == "开场" && NarrativePaused, "S1_01：新游戏自动播开场，游戏暂停");
        Check(await FinishDialog(), "S1_01：按 E 播完开场和苏醒，恢复游戏");

        // S1_01 → S1_02：走过房间右边缘
        bool inS102 = await HoldUntil(PlayerActions.Right, () => World.CurrentRoomDef?.Id == "S1_02", 300);
        Check(inS102, "S1_01 → S1_02：真实玩家按住右键走过房间边缘");
        // 走进坑里：“倒影”自动触发（不用按 E）
        bool reflected = await HoldUntil(PlayerActions.Right, () => Narr.MainActive, 600);
        await WaitUntil(() => Narr.Dialog.IsOpen, 60);
        Check(reflected && Narr.MainLabel == "倒影" && Narr.Dialog.CurrentText == "站在水边时，倒影里能看见他头上嵌满宝石的角冠。" && !Narr.Dialog.NameVisible,
            $"S1_02：走进坑里自动播“倒影” → 旁白“站在水边时，倒影里能看见他头上嵌满宝石的角冠。”（{Narr.MainLabel}：{Narr.Dialog.CurrentText}）");
        await AdvanceUntil(() => Narr.Dialog.CurrentText == "……水太浑了。" || !Narr.MainActive, 5);
        Check(Narr.Dialog.CurrentText == "……水太浑了。" && Narr.Dialog.CurrentName == "穆尔图库尔", "S1_02：接着穆尔图库尔“……水太浑了。”");
        Check(await FinishDialog(), "S1_02：倒影播完");
        bool atTablet = await WalkToPrompt("拾取泥板", PlayerActions.Right);
        Check(atTablet, "S1_02：走到坑底的泥板「当天还没有名字」旁");
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        Check(Narr.Tablet.CurrentTitle == "当天还没有名字", "S1_02：按 E 拾取，显示泥板");
        Check(await FinishDialog(5), "S1_02：按 E 收起泥板");

        // S1_02 → S1_03：出口在右侧高处，放到出口平台再走过去；朝圣者插话是字幕
        bool inS103 = await CrossEdge("S1_02", "From_S1_03", "S1_03");
        Check(inS103, "S1_02 → S1_03：从出口平台走过房间边缘");
        Input.ActionPress(PlayerActions.Right);
        bool bark = await WaitUntil(() => Narr.Subtitle.IsShowing, 240);
        bool free = !PauseService.IsPaused && !InputLock.IsLocked;
        Input.ActionRelease(PlayerActions.Right);
        Check(bark && free && Narr.Subtitle.CurrentText == "杀了他们！", "S1_03：跑进朝圣者之路 → 字幕“？？？：杀了他们！”，不暂停");
        await WaitUntil(() => !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing, 400);

        // S1_03 → S1_04：供桌选“吃下”
        bool inS104 = await CrossEdge("S1_03", "From_S1_04", "S1_04");
        bool atTable = inS104 && await WalkToPrompt("供桌", PlayerActions.Right);
        Check(inS104 && atTable, "S1_03 → S1_04：走过边缘，走到供桌前");
        await KeyTap(Key.E);
        bool choice = await WaitUntil(() => Narr.Choice.IsOpen, 240);
        await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        Check(choice && await FinishDialog(5) && GameSession.Progress.Flag("item.生命之面包"), "S1_04：供桌选“吃下”");

        // S1_04 → S1_05：守坑人（打 Boss 用设变量代替）；路上地面有一格砖（x≈912），走到跟前跳过去
        Input.ActionPress(PlayerActions.Right);
        await WaitUntil(() => LocalX > 820, 240);
        Input.ActionPress(PlayerActions.Jump);
        await Frames(10);
        Input.ActionRelease(PlayerActions.Jump);
        bool inS105 = await WaitUntil(() => World.CurrentRoomDef?.Id == "S1_05", 300);
        Input.ActionRelease(PlayerActions.Right);
        await Frames(2);
        Check(inS105, "S1_04 → S1_05：跳过地上的砖，走过房间边缘");
        GameSession.Progress.SetVar("boss_keeper", true);
        bool atKeeper = await WalkToPrompt("交谈", PlayerActions.Right);
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.MainActive, 10);
        await WaitUntil(() => !Narr.Dialog.IsTyping, 300);
        Check(atKeeper && Narr.MainLabel == "守坑人" && Narr.Dialog.CurrentText.StartsWith("两百年来……"),
            $"S1_05：boss_keeper 之后和守坑人老人对话：“两百年来……祂……醒了……”（{Narr.Dialog.CurrentText}）");
        Check(await FinishDialog(), "S1_05：对话播完");

        // S1_05 → S1_06：七夜雕像
        bool inS106 = await CrossEdge("S1_05", "From_S1_06", "S1_06");
        Check(inS106, "S1_05 → S1_06：守坑人的门打开后，从出口平台走过边缘");
        bool atStatue = await WalkToPrompt("触碰雕像", PlayerActions.Right);
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.MainActive, 10);
        await Frames(2);
        Check(atStatue && Narr.MainLabel == "七夜" && Narr.Cg.CurrentId == "seven_nights", "S1_06：触碰七夜雕像，播 CG1");
        bool cgDone = await WaitUntil(() => !Narr.MainActive, 1800);   // CG1 是动画 CG（约 23 秒），播完自己结束
        await WaitUntil(() => !PauseService.IsPaused, 60);
        await Frames(3);
        Check(cgDone && NoPauseOrLock(), "S1_06：CG1 动画播完");

        // S1_06 → S1_07：两河源头（字幕）
        bool inS107 = await CrossEdge("S1_06", "From_S1_07", "S1_07");
        Input.ActionPress(PlayerActions.Right);
        bool song = await WaitUntil(() => Narr.Subtitle.IsShowing, 240);
        string first = Narr.Subtitle.CurrentText;
        Input.ActionRelease(PlayerActions.Right);
        bool second = await WaitUntil(() => Narr.Subtitle.CurrentText == "别听。", 400);
        Check(inS107 && song && first == "有人在唱歌。" && second, "S1_07：走进两河源头 → 字幕“有人在唱歌。”“别听。”");
        await WaitUntil(() => !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing, 400);

        // 回溯 S1_08（要攀墙才上得去，这里直接放到 S1_08）：重塑雕像 CG2 + 泥板「神子」
        await Teleport("S1_08", "Default");
        bool atReshape = await WalkToPrompt("触碰雕像", PlayerActions.Right);
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.MainActive, 10);
        await Frames(2);
        Check(atReshape && Narr.MainLabel == "重塑" && Narr.Cg.CurrentId == "reshape" && Narr.Dialog.CurrentText == "在献给父亲的那场伟大胜利之后，他沉醉在祂慈爱的目光中，即使自己已经破碎。" && !Narr.Dialog.NameVisible,
            "S1_08：触碰重塑雕像，播 CG2 和旁白");
        await AdvanceUntil(() => Narr.Dialog.CurrentName == "穆尔图库尔" || !Narr.MainActive, 5);
        Check(Narr.Dialog.CurrentText == "他又捏了我一次。我希望我一直碎着。就像永远一样久。" && Narr.Dialog.NameVisible, "S1_08：接着穆尔图库尔“他又捏了我一次……”");
        Check(await FinishDialog(), "S1_08：CG2 播完");
        bool atHand = await WalkToPrompt("拾取泥板", PlayerActions.Right);
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        Check(atHand && Narr.Tablet.CurrentTitle == "神子" && !Narr.Tablet.ShowingFull, "S1_08：拾取泥板「神子」（残缺版）");
        Check(await FinishDialog(5), "S1_08：收起泥板");
    }
}
