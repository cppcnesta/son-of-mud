using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.World;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 计划第 4 节第 5、6、7 步（从正式入口 main_game.tscn 启动，真实玩家，真实按键）：
/// ① 选项：S1_04 供桌按 E → “别碰！那是母神的东西！”打完字直接出现 离开 / 吃下；上下键移动、E 确认；
///    两个选项都能选；变量按原类型写进 Progress（choice.供桌 是 string，item.生命之面包 是 bool）；吃下后供桌不能再互动；
///    存档 → 读档后 [如果 choice.供桌 吃下 …] 仍然走对分支。
/// ② 泥板 + 铭文：S1_02 拾取「当天还没有名字」→ 发 TabletCollected、显示残缺版、游戏暂停；设 truth 后再读显示完整版；
///    存档 → 读档后已拾取的泥板不再出现；[铭文 mural] 用同一个界面显示，不收集、不发事件。
/// ③ CG、改名、立绘：S1_06 七夜雕像按 E → CG1 + 伊姆杜的两句；[改名 ？？？ 穆尔图库尔] 后名牌变成穆尔图库尔，读档后仍保留。
/// </summary>
public partial class NarrativeChoiceTest : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_choice";

    protected override async Task RunChecks()
    {
        Listen();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            Check(finished && Narr != null && Interactor != null, "正式入口开新游戏成功（这个测试关掉了自动开场）");
            if (Narr == null || Interactor == null) return;
            await WaitSettled();
            LoadTestScript();
            await ChoiceChecks();
            await TabletChecks();
            await CgAndRenameChecks();
            await AfterLoadChecks();
            Check(Errors.Count == 0, "全程没有报错" + (Errors.Count > 0 ? "：" + string.Join(" | ", Errors.Snapshot().Take(5)) : ""));
        }
        finally
        {
            Unlisten();
        }
    }

    private async Task<bool> OpenOffering()
    {
        await Teleport("S1_04", "From_S1_03");
        bool atTable = await WalkToPrompt("供桌", PlayerActions.Right);
        if (!atTable) return false;
        await KeyTap(Key.E);
        return await WaitUntil(() => Narr.Choice.IsOpen, 240);
    }

    private async Task ChoiceChecks()
    {
        bool opened = await OpenOffering();
        Check(opened && Narr.Dialog.CurrentName == "？？？" && Narr.Dialog.CurrentText == "别碰！那是母神的东西！" && NarrativePaused,
            $"供桌：真实玩家走到供桌按 E → ？？？“别碰！那是母神的东西！”，暂停（{Narr.Dialog.CurrentName}：{Narr.Dialog.CurrentText}）");
        if (!opened) return;
        Check(Narr.Choice.Options.SequenceEqual(new[] { "离开", "吃下" }) && Narr.Choice.Focus == 0 && Narr.Dialog.IsOpen,
            "供桌：台词打完字直接出现选项 离开 / 吃下（不用再按一次），台词留在框里，默认选第一项");
        Check(OnScreen(Narr.Choice.GetNodeOrNull<Control>("List/Option0"), out string opt0) && OnScreen(Narr.Choice.GetNodeOrNull<Control>("List/Option1"), out _),
            $"供桌：两个选项按钮真的显示在屏幕内（{opt0}）");
        var choiceLayout = LayoutProblems(Narr);
        Check(choiceLayout.Count == 0, "供桌：剧情层所有可见控件都在屏幕内" + Join(choiceLayout));
        await Tap(PlayerActions.Down);
        int afterDown = Narr.Choice.Focus;
        await Tap(PlayerActions.Down);
        int wrapped = Narr.Choice.Focus;
        await Tap(PlayerActions.Up);
        int afterUp = Narr.Choice.Focus;
        Check(afterDown == 1 && wrapped == 0 && afterUp == 1, $"供桌：下 / 上移动选项，到头会绕回（{afterDown}→{wrapped}→{afterUp}）");
        await Tap(PlayerActions.Up);   // 回到“离开”
        await KeyTap(Key.E);
        await WaitUntil(() => !Narr.MainActive, 60);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        Variant left = GameSession.Progress.GetVar("choice.供桌");
        Check(left.VariantType == Variant.Type.String && left.AsString() == "离开" && !GameSession.Progress.HasVar("item.生命之面包") && !Narr.Choice.IsOpen,
            "供桌：选“离开” → choice.供桌 = \"离开\"（string），什么也不发生");
        Check(NoPauseOrLock() && Ended.LastOrDefault() == "m_pit:供桌", "供桌：选完剧情结束，恢复游戏");
        await Frames(5);
        Check(Interactor.CurrentPrompt == "供桌", "供桌：离开之后供桌还在，可以再来");

        opened = await OpenOffering();
        await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        await WaitUntil(() => !Narr.MainActive, 60);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        Variant ate = GameSession.Progress.GetVar("choice.供桌");
        Variant bread = GameSession.Progress.GetVar("item.生命之面包");
        Check(opened && ate.VariantType == Variant.Type.String && ate.AsString() == "吃下" && bread.VariantType == Variant.Type.Bool && bread.AsBool(),
            "供桌：选“吃下” → choice.供桌 = \"吃下\"（string），item.生命之面包 = true（bool）");
        await Frames(5);
        StoryTrigger table = World.CurrentRoom.GetNode<StoryTrigger>("Objects/Story_Offering");
        Check(!table.Active && !table.Visible && Interactor.CurrentPrompt != "供桌", "供桌：吃下之后供桌不能再互动（HideFlag item.生命之面包）");
    }

    private async Task TabletChecks()
    {
        await Teleport("S1_02", "From_S1_01");
        bool atTablet = await WalkToPrompt("拾取泥板", PlayerActions.Right);
        Check(atTablet, $"泥板：真实玩家走到 S1_02 的泥板旁，提示“拾取泥板”（x={LocalX:F0}）");
        if (!atTablet) return;
        int collected = Collected.Count;
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        Check(Collected.Skip(collected).SequenceEqual(new[] { "nameless_day" }) && GameSession.Progress.Flag("tablet.nameless_day"),
            "泥板：拾取后发 TabletCollected(nameless_day) 一次，写 tablet.nameless_day");
        Check(Narr.Tablet.IsOpen && Narr.Tablet.CurrentTitle == "当天还没有名字" && !Narr.Tablet.ShowingFull && Narr.Tablet.CurrentText.EndsWith("第七夜，……落下。") && NarrativePaused,
            $"泥板：显示残缺版，游戏暂停（{Narr.Tablet.CurrentTitle}）");
        Check(OnScreen(Narr.Tablet.GetNodeOrNull<Control>("Panel"), out string tabletInfo) && CoversScreen(Narr.Tablet.GetNodeOrNull<Control>("Dim"), out _),
            $"泥板：泥板面板显示在屏幕内，背后的压暗铺满屏幕（{tabletInfo}）");
        await Frames(10);
        Check(Narr.Tablet.IsOpen, "泥板：拾取的那一下 E 不会直接关掉泥板");
        await KeyTap(Key.E);
        await WaitUntil(() => !Narr.MainActive, 30);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        TabletPickup pickup = World.CurrentRoom.GetNode<TabletPickup>("Objects/Tablet_nameless_day");
        Check(!Narr.Tablet.IsOpen && NoPauseOrLock() && !pickup.Available && !pickup.Visible, "泥板：按 E 关掉，恢复游戏，地上的泥板消失");

        GameSession.Progress.SetVar("truth", true);
        Request("tablets", "nameless_day");
        await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        Check(Narr.Tablet.ShowingFull && Narr.Tablet.CurrentText.EndsWith("祂说：我的儿子。"), "泥板：设 truth 后再读，显示完整版");
        await AdvanceUntil(() => !Narr.MainActive, 5);
        GameSession.Progress.SetVar("truth", false);

        int before = Collected.Count;
        Request("test_story", "铭文");
        await WaitUntil(() => Narr.Tablet.IsOpen, 30);
        Check(Narr.Tablet.CurrentTitle == "壁画铭文" && Narr.Tablet.CurrentText == "祂把风灌进她的嘴里，海就停了。祂举起弯刃，海就分成了两半。" && Collected.Count == before,
            "铭文：[铭文 mural] 用同一个界面显示壁画铭文，不收集、不发 TabletCollected");
        await AdvanceUntil(() => !Narr.MainActive, 5);
    }

    private async Task CgAndRenameChecks()
    {
        await Teleport("S1_06", "Default");
        bool atStatue = await WalkToPrompt("触碰雕像", PlayerActions.Right);
        Check(atStatue, "七夜：真实玩家走到雕像前，提示“触碰雕像”");
        if (!atStatue) return;
        await KeyTap(Key.E);
        await WaitUntil(() => Narr.MainActive, 10);
        await Frames(2);
        Check(Narr.Cg.IsShowing && Narr.Cg.CurrentId == "seven_nights" && Narr.Cg.CurrentCaption == "CG1「七夜」", $"七夜：触碰雕像播 CG1「七夜」（占位标题 {Narr.Cg.CurrentCaption}）");
        Check(Narr.Dialog.CurrentName == "伊姆杜" && Narr.Dialog.CurrentText == "穆尔图库尔" && !Narr.Dialog.PortraitVisible, "七夜：CG 上叠对话“伊姆杜：穆尔图库尔”（伊姆杜没有立绘）");
        await WaitUntil(ReadyForKey, 120);
        await KeyTap(Key.E);
        await Frames(2);
        Check(Narr.Dialog.CurrentName == "伊姆杜" && Narr.Dialog.CurrentText == "我的儿子" && Narr.Cg.IsShowing, "七夜：第二句“伊姆杜：我的儿子”，CG 还在");
        await AdvanceUntil(() => !Narr.MainActive, 5);
        await Frames(30);
        Check(!Narr.Cg.IsShowing && !Narr.Cg.Visible && GameSession.Progress.Flag("seen.m_pit.七夜"), "七夜：播完 CG 关掉");

        Request("test_story", "改名");
        await WaitUntil(() => Narr.Dialog.IsOpen, 10);
        Check(Narr.Dialog.CurrentName == "穆尔图库尔" && GameSession.Progress.GetVar("name.？？？").AsString() == "穆尔图库尔",
            $"改名：[改名 ？？？ 穆尔图库尔] 之后 ？？？ 的名牌显示“穆尔图库尔”（{Narr.Dialog.CurrentName}）");
        await AdvanceUntil(() => !Narr.MainActive, 5);

        Request("test_story", "立绘");
        await WaitUntil(() => Narr.Dialog.IsOpen, 10);
        bool keeperPortrait = Narr.Dialog.PortraitVisible && Narr.Dialog.PortraitCaption.StartsWith("守坑人");
        await WaitUntil(ReadyForKey, 120);
        await KeyTap(Key.E);
        await Frames(2);
        bool narratorNoPortrait = !Narr.Dialog.PortraitVisible && !Narr.Dialog.NameVisible;
        Check(keeperPortrait && narratorNoPortrait, "立绘：守坑人说话时显示立绘占位，换成旁白时立绘和名牌都收起");
        await AdvanceUntil(() => !Narr.MainActive, 5);
    }

    /// <summary>在 S1_04 存档点真的按 E 存档，再从正式入口读档。</summary>
    private async Task<bool> SaveAndLoad()
    {
        await Teleport("S1_04", SpawnPoint.SaveId);
        Check(Interactor.CurrentPrompt == "存档", "存档：站在 S1_04 存档点上");
        await KeyTap(Key.E);
        await Frames(3);
        bool saved = FileAccess.FileExists(SavePath);
        // 存档之后的改动不应该被读回来
        GameSession.Progress.SetVar("choice.供桌", "离开");
        GameSession.Progress.SetVar("name.？？？", "？？？");
        bool loaded = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        LoadTestScript();
        return saved && loaded && Starts[^1].success && Narr != null;
    }

    private async Task AfterLoadChecks()
    {
        bool ok = await SaveAndLoad();
        Check(ok, "存档 → 读档：在 S1_04 按 E 存档，从正式入口“继续”读档成功");
        if (!ok) return;
        Variant choice = GameSession.Progress.GetVar("choice.供桌");
        Check(choice.VariantType == Variant.Type.String && choice.AsString() == "吃下" && GameSession.Progress.GetVar("item.生命之面包").VariantType == Variant.Type.Bool,
            "读档后：choice.供桌 = \"吃下\"（string）、item.生命之面包（bool）类型都保留");
        Request("test_story", "读档分支");
        await WaitUntil(() => !Narr.MainActive && !PauseService.IsPaused, 60);
        Check(GameSession.Progress.GetVar("test.branch").AsString() == "吃下", "读档后：[如果 choice.供桌 吃下 …] 仍然走“吃下”分支");

        await Teleport("S1_04", "From_S1_03");
        await HoldUntil(PlayerActions.Right, () => LocalX > 420, 120);
        Check(Interactor.CurrentPrompt != "供桌" && !World.CurrentRoom.GetNode<StoryTrigger>("Objects/Story_Offering").Active, "读档后：吃过的供桌仍然不能互动");

        await Teleport("S1_02", "From_S1_01");
        TabletPickup pickup = World.CurrentRoom.GetNode<TabletPickup>("Objects/Tablet_nameless_day");
        await HoldUntil(PlayerActions.Right, () => LocalX > 760, 300);
        Check(!pickup.Available && !pickup.Visible && Interactor.CurrentPrompt != "拾取泥板", "读档后：已拾取的泥板不再出现（走过去也没有拾取提示）");

        Request("test_story", "名牌");
        await WaitUntil(() => Narr.Dialog.IsOpen, 10);
        Check(Narr.Dialog.CurrentName == "穆尔图库尔", $"读档后：改名保留，？？？ 的名牌仍是“穆尔图库尔”（{Narr.Dialog.CurrentName}）");
        await AdvanceUntil(() => !Narr.MainActive, 5);
        await WaitUntil(() => !PauseService.IsPaused, 30);
        Check(NoPauseOrLock(), "最后没有暂停和输入锁残留");
    }
}
