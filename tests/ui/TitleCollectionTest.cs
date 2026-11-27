using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative;
using SonOfMud.Narrative.Commands;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Save;
using SonOfMud.UI;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 主页面「收集」和跨存档记录 CollectionRecord：
/// ① 测试隔离：测试运行时记录写到测试文件，正式记录文件 user://collection.cfg 从头到尾没被创建或改动。
/// ② 旧档导入：带泥板的旧档、没有记录文件 → 进主页面（不打开收集面板），开场还没播完时已经导入并写盘；
///    再进一次不重复写盘（幂等）；开新档并覆盖旧档 → 泥板还在；删掉存档 → 泥板还在；坏档跳过、主页面照常。
/// ③ 信号：捡泥板记泥板；拿到泥板「星」（结局 A）、发出重开（结局 B）都记通关。
/// ④ 面板：泥板页列出全部 18 块，没捡到的只列标题、正文写「还没有捡到。」；捡到的没通关显示残缺版、通关后显示完整版；
///    CG 页 8 张，没解锁写「未解锁」，解锁后显示标题和占位色块；左右键换页；Esc 关闭。
/// ⑤ CG 解锁落点：静态 CG 显示后记、动画 CG 开播成功后记；自动推进、没有 CG 层、找不到 CG 数据、动画开播失败都不记。
/// </summary>
public partial class TitleCollectionTest : UiTestBase
{
    protected override string SuiteName => "title_collection";

    private const string RecordPath = "user://collection_title_test.cfg";
    private const string NarrativeFixtures = "res://tests/narrative/fixtures/cg";
    private const string UiFixtures = "res://tests/ui/fixtures/cg";

    private byte[] _realBefore;

    protected override async Task RunChecks()
    {
        _realBefore = RealRecordBytes();
        GameSession.LastStartError = "";
        try
        {
            IsolationChecks();
            await ImportChecks();
            SignalChecks();
            await PanelChecks();
            await CgUnlockChecks();
        }
        finally
        {
            CollectionRecord.ResetForTest();
            CollectionRecord.FilePathOverride = null;
        }
        byte[] realAfter = RealRecordBytes();
        Check((_realBefore == null && realAfter == null) || SameBytes(_realBefore, realAfter),
            "整个测试过程中，正式记录文件 collection.cfg 没被创建或改动");
    }

    private static byte[] RealRecordBytes() =>
        FileAccess.FileExists(CollectionRecord.FilePath) ? FileAccess.GetFileAsBytes(CollectionRecord.FilePath) : null;

    // ---------- ① 测试隔离 ----------

    private void IsolationChecks()
    {
        CollectionRecord.FilePathOverride = null;
        Check(CollectionRecord.IsTestRun && CollectionRecord.ActivePath == CollectionRecord.TestFilePath,
            $"测试运行时默认写测试记录文件（{CollectionRecord.ActivePath}），不是正式的 collection.cfg");
        CollectionRecord.FilePathOverride = RecordPath;
        Check(CollectionRecord.ActivePath == RecordPath && CollectionRecord.ResetForTest(), "测试可以指定自己的记录文件并清空");
        CollectionRecord.FilePathOverride = CollectionRecord.FilePath;
        Check(!CollectionRecord.ResetForTest(), "清空正式记录文件会被拒绝");
        CollectionRecord.FilePathOverride = RecordPath;
        CollectionRecord.ResetForTest();
    }

    // ---------- ② 旧档导入 ----------

    private static void WriteOldSave(params (string id, bool value)[] tablets)
    {
        var vars = new GDictionary();
        foreach (var (id, value) in tablets) vars["tablet." + id] = value;
        var data = new SaveData
        {
            Progress = new GDictionary { ["version"] = 1, ["abilities"] = new Godot.Collections.Array(), ["vars"] = vars },
            Room = "S1_01",
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SavePaths.DefaultDirectory));
        data.WriteAtomic(SavePath, out _);
    }

    private async Task ImportChecks()
    {
        CollectionRecord.ResetForTest();
        DeleteSave();
        WriteOldSave(("smile", true), ("curse", true), ("gate_1", false));
        Check(!FileAccess.FileExists(RecordPath), "前提：有带泥板的旧档，还没有记录文件");

        TitleMenu menu = await OpenTitleMenu(skipIntro: false);
        if (menu == null) return;
        Check(menu.IntroPlaying && menu.ImportedTablets == 2 && FileAccess.FileExists(RecordPath) && !menu.PanelOpen,
            $"进主页面（开场还在播、按钮还不能点，没打开收集面板）时已经导入 {menu.ImportedTablets} 块泥板并写盘");
        CollectionRecord.Reload();
        Check(CollectionRecord.HasTablet("smile") && CollectionRecord.HasTablet("curse") && !CollectionRecord.HasTablet("gate_1"),
            "从盘上重新读：旧档里捡到的「笑容」「诅咒」在，值为 false 的「一」不算");
        int writes = CollectionRecord.Writes;
        menu = await OpenTitleMenu();
        Check(menu != null && menu.ImportedTablets == 0 && CollectionRecord.Writes == writes, "再进一次主页面：没有新内容，不重复写盘（幂等）");
        if (menu == null) return;

        // 开新档，并让新存档覆盖旧档
        bool started = await StartFromMenu(menu.NewGameButton);
        await WaitSettled();
        bool saved = started && Saves != null && Saves.SaveToDisk("S1_01");
        SaveData overwritten = SaveData.ReadFrom(SavePath, out _);
        bool oldGone = overwritten != null && !CollectionRecord.SavedTablets(overwritten.Progress).Contains("smile");
        Check(saved && oldGone, "开新游戏并存档：新存档覆盖了旧档，存档里已经没有「笑容」");
        menu = await QuitToMenuViaPauseMenu();
        CollectionRecord.Reload();
        Check(menu != null && CollectionRecord.HasTablet("smile") && CollectionRecord.HasTablet("curse"),
            "开新档覆盖旧档后回主页面：记录里「笑容」「诅咒」还在");

        DeleteSave();
        menu = await OpenTitleMenu();
        CollectionRecord.Reload();
        Check(menu != null && CollectionRecord.HasTablet("smile") && CollectionRecord.HasTablet("curse"), "删掉存档后进主页面：泥板还在");

        WriteSaveBytes(GarbageSave());
        menu = await OpenTitleMenu();
        Check(menu != null && menu.ImportedTablets == 0 && CollectionRecord.HasTablet("smile"), "坏存档：跳过导入，主页面照常，记录不受影响");
        DeleteSave();
    }

    // ---------- ③ 信号 ----------

    private void SignalChecks()
    {
        CollectionRecord.ResetForTest();
        Events.Instance.EmitSignal(Events.SignalName.TabletCollected, (StringName)"smile");
        Check(CollectionRecord.HasTablet("smile") && !CollectionRecord.Cleared, "捡到泥板：记下泥板，不算通关");
        Events.Instance.EmitSignal(Events.SignalName.TabletCollected, (StringName)CollectionRecord.EndingTabletId);
        Check(CollectionRecord.Cleared, "结局 A：拿到泥板「星」就记通关");
        CollectionRecord.ResetForTest();
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        Check(CollectionRecord.Cleared, "结局 B：发出重开就记通关");
        CollectionRecord.Reload();
        Check(CollectionRecord.Cleared, "通关记录写到了盘上");
    }

    // ---------- ④ 面板 ----------

    private async Task PanelChecks()
    {
        CollectionRecord.ResetForTest();
        CollectionRecord.MarkTablet("divine_son");
        TitleMenu menu = await OpenTitleMenu();
        if (menu == null) return;
        bool opened = await Activate(menu.CollectionButton);
        await Frames(2);
        TitleCollectionPanel panel = menu.CollectionPanel;
        Check(opened && menu.OpenPanel == panel && panel.CurrentPage == TitleCollectionPanel.Page.Tablets && panel.List.HasFocus(),
            "按「收集」：打开收集面板，先显示泥板页，焦点在列表上");
        var tablets = panel.ItemTexts;
        Check(tablets.Count == 18 && tablets[1] == "神子" && tablets[2] == "笑容" && tablets[^1] == "星𒀭",
            $"泥板页按地图顺序列出全部 18 块，没捡到的也列出标题（{string.Join("、", tablets.Take(4))}…）");

        var divineSon = GD.Load<SonOfMud.Narrative.Data.TabletData>("res://assets/data/narrative/tablets/tablet_divine_son.tres");
        panel.Select(1);
        Check(panel.EntryName.Text == "神子" && panel.EntryBody.Text == divineSon.PartialText, "捡到过、没通关：显示残缺版");
        panel.Select(2);
        Check(panel.EntryName.Text == "笑容" && panel.EntryBody.Text == TitleCollectionPanel.NotCollectedText, "没捡到过：只有标题，正文写「还没有捡到。」");
        await SaveShot(menu, "title_collection_tablets.png");

        CollectionRecord.MarkCleared();
        panel.Refresh();
        panel.Select(1);
        Check(panel.EntryBody.Text == divineSon.FullText && divineSon.FullText != divineSon.PartialText, "通关后：有完整版的泥板显示完整版");

        // CG 页：用左右键换页
        await PressAction("ui_right");
        var cgs = panel.ItemTexts;
        Check(panel.CurrentPage == TitleCollectionPanel.Page.Cgs && cgs.Count == 8 && cgs.All(t => t.Contains(TitleCollectionPanel.LockedText))
              && cgs[0].StartsWith("CG1"), $"按右键换到 CG 页：8 张都写「未解锁」（{cgs[0]}）");
        Check(panel.EntryName.Text == TitleCollectionPanel.LockedText && !panel.CgView.Visible, "选中没解锁的 CG：写「未解锁」，不显示图");
        CollectionRecord.MarkCg("seven_nights");
        panel.ShowPage(TitleCollectionPanel.Page.Cgs);
        cgs = panel.ItemTexts;
        panel.Select(0);
        Check(cgs[0] == "CG1「七夜」" && cgs[1].Contains(TitleCollectionPanel.LockedText) && panel.EntryName.Text == "CG1「七夜」" && panel.ShowingCgPlaceholder,
            "解锁了 CG1：列表和标题显示「七夜」，还没有静态图时显示占位色块");
        await SaveShot(menu, "title_collection_cgs.png");
        await PressAction("ui_left");
        Check(panel.CurrentPage == TitleCollectionPanel.Page.Tablets, "按左键换回泥板页");

        await PressAction("ui_cancel");
        await Frames(2);
        Check(!menu.PanelOpen && menu.CollectionButton.HasFocus(), "Esc 关闭收集面板，焦点回到「收集」");
    }

    private async Task PressAction(string action)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
            await Frames(1);
        }
    }

    /// <summary>截一张面板的图到 tests/output，方便看排版（无界面运行时截不到就跳过）。</summary>
    private async Task SaveShot(Node menu, string name)
    {
        await Frames(3);
        if (DisplayServer.GetName() == "headless") return;
        Image image = GetViewport().GetTexture()?.GetImage();
        image?.SavePng(ProjectSettings.GlobalizePath("res://tests/output/" + name));
    }

    // ---------- ⑤ CG 解锁落点 ----------

    private async Task CgUnlockChecks()
    {
        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        var narr = MainGame?.GetNodeOrNull<NarrativeDirector>("NarrativeLayer");
        Check(started && narr?.MainContext != null, "进游戏，剧情层就绪");
        if (narr?.MainContext == null) return;
        VNContext ctx = narr.MainContext;
        string oldDir = ctx.Settings.CgDirectory;
        bool oldAuto = ctx.Runner.AutoAdvance;
        var command = new CgCommand();
        try
        {
            ctx.Settings.CgDirectory = NarrativeFixtures;
            ctx.Runner.AutoAdvance = false;

            CollectionRecord.ResetForTest();
            bool done = false;
            command.Execute(new[] { "test_static", "0" }, ctx, () => done = true);
            Check(done && CollectionRecord.HasCg("test_static"), "静态 CG 正常显示：记为解锁");
            command.Execute(new[] { CgCommand.None, "0" }, ctx, () => { });
            Check(!CollectionRecord.HasCg(CgCommand.None), "关掉 CG 不记");

            CollectionRecord.ResetForTest();
            ctx.Runner.AutoAdvance = true;
            command.Execute(new[] { "test_static", "0" }, ctx, () => { });
            ctx.Runner.AutoAdvance = false;
            command.Execute(new[] { CgCommand.None, "0" }, ctx, () => { });
            Check(!CollectionRecord.HasCg("test_static"), "自动推进（测试快速跳过）时显示的静态 CG 不记");

            var layer = ctx.Cg;
            ctx.Cg = null;
            done = false;
            command.Execute(new[] { "test_static", "0" }, ctx, () => done = true);
            command.Execute(new[] { "test_short", "0" }, ctx, () => { });
            ctx.Cg = layer;
            Check(done && !CollectionRecord.HasCg("test_static") && !CollectionRecord.HasCg("test_short"), "没有 CG 层：静态、动画 CG 都不记");

            bool threw = false;
            try { command.Execute(new[] { "no_such_cg", "0" }, ctx, () => { }); }
            catch (InvalidOperationException) { threw = true; }
            Check(threw && !CollectionRecord.HasCg("no_such_cg"), "找不到 CG 数据（加载失败）：报错，不记");

            ctx.Settings.CgDirectory = UiFixtures;
            int errors = NarrativeLog.ErrorCount;
            done = false;
            command.Execute(new[] { "broken_anim", "0" }, ctx, () => done = true);
            Check(done && NarrativeLog.ErrorCount > errors && ctx.ActiveSegment == null && !CollectionRecord.HasCg("broken_anim"),
                "动画 CG 开播就失败（场景根节点不是动画 CG）：跳过继续，不记");

            ctx.Settings.CgDirectory = NarrativeFixtures;
            done = false;
            command.Execute(new[] { "test_short", "0" }, ctx, () => done = true);
            Check(!done && CollectionRecord.HasCg("test_short"), "动画 CG 开播成功：还没播完就已经记为解锁");
            bool finished = await WaitUntil(() => done, 600);
            Check(finished && ctx.ActiveSegment == null, "测试动画 CG 正常播完");
        }
        finally
        {
            ctx.Settings.CgDirectory = oldDir;
            ctx.Runner.AutoAdvance = oldAuto;
            ctx.Cg?.Hide(0f);
        }
    }
}
