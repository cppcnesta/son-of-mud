using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Data;
using SonOfMud.Narrative.Views;
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
            ViewSceneChecks();
            await SceneAuthorityChecks();
            await PortraitChecks();
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
        Check(OnScreen(Narr.Dialog.GetNodeOrNull<Control>("Box"), out string boxInfo) && OnScreen(Narr.Dialog.GetNodeOrNull<Control>("Box/Body"), out _),
            $"开场：对话框和正文真的显示在屏幕内（{boxInfo}）");
        Check(Narr.Dialog.GetNodeOrNull<Control>("Box").GetGlobalRect().Position.Y > GetViewport().GetVisibleRect().Size.Y / 2,
            "开场：对话框在屏幕下半部");
        Check(CoversScreen(Narr.Cg.GetNodeOrNull<Control>("Placeholder"), out string cgInfo), $"开场：黑屏 CG 铺满整个屏幕（{cgInfo}）");
        Check(CoversScreen(Narr.Fade, out string fadeInfo), $"剧情淡入淡出层铺满整个屏幕（{fadeInfo}）");
        var roomFade = MainGame.FindChild("FadeOverlay", true, false)?.GetNodeOrNull<Control>("Black");
        Check(CoversScreen(roomFade, out string roomFadeInfo), $"切房间用的黑屏遮罩铺满整个屏幕（{roomFadeInfo}）");
        var openingLayout = LayoutProblems(Narr);
        Check(openingLayout.Count == 0, "开场：剧情层所有可见控件都在屏幕内，没有 0 大小却有内容的容器" + Join(openingLayout));

        var seen = new List<(string name, string text, bool portrait)>();
        var overflow = new List<string>();
        var autoFlips = new List<string>();
        var shown = new HashSet<string>();
        int pagesBefore = Narr.Dialog.PageCount;
        int pages = pagesBefore;
        // 逐帧看：框里的字有没有放不下；有没有在玩家没按键的时候翻页（那样上一页还没看完就没了）
        void Watch(bool justPressed)
        {
            if (!Narr.Dialog.IsOpen) return;
            foreach (string line in Narr.Dialog.CurrentText.Split('\n')) shown.Add(line);
            if (!Narr.Dialog.TextFits && overflow.Count < 5)
                overflow.Add($"框里 {Narr.Dialog.CurrentText.Split('\n').Length} 行放不下（最后一句“{Narr.Dialog.CurrentText.Split('\n').Last()}”）");
            if (Narr.Dialog.PageCount > pages && !justPressed && autoFlips.Count < 5)
                autoFlips.Add($"没按键就翻到了“{Narr.Dialog.CurrentText.Split('\n')[0]}”");
            pages = Narr.Dialog.PageCount;
        }
        bool done = false;
        for (int tap = 0; tap < 40 && !done; tap++)
        {
            // 等字打完，再多等 0.75 秒（比句间停顿 LineGap 长）：同一段里自动接着出的句子也全部出完，才按键
            for (int settle = 0; settle < 20 && Narr.MainActive; settle++)
            {
                for (int f = 0; f < 300 && Narr.MainActive && !ReadyForKey(); f++)
                {
                    await Frames(1);
                    Watch(false);
                }
                int lines = Narr.Dialog.LineCount;
                for (int f = 0; f < 45 && Narr.MainActive; f++)
                {
                    await Frames(1);
                    Watch(false);
                }
                if (Narr.Dialog.LineCount == lines && ReadyForKey()) break;
            }
            if (!Narr.MainActive) { done = true; break; }
            Watch(false);
            seen.Add((Narr.Dialog.CurrentName, Narr.Dialog.CurrentText.Split('\n').Last(), Narr.Dialog.PortraitVisible));
            await KeyTap(Key.E);
            await Frames(2);
            Watch(true);
            done = !Narr.MainActive;
        }
        string[] narration =
        {
            "我是父亲用血和泥捏出的第一个。", "在祂说出我的名字之前，我只是泥。", "在人世（…?）之前，在塔庙（…?）之前，在两条河有名字之前，只有我。",
            "祂的手指在我身上留下了纹路，祂的血塑造了我的骨和肉。", "那时天和地还没有分开，混沌的母神在咸水里翻身。", "我第一次睁眼，然后看见祂。这就是这个世界的样子。",
            "父亲说：“你是我的手。”于是我成了父亲的手。", "父亲说：“你是我的刃。”于是我成了父亲的刃。",
        };
        string[] missing = narration.Where(line => !shown.Contains(line)).ToArray();
        Check(overflow.Count == 0 && missing.Length == 0 && Narr.Dialog.PageCount > pagesBefore,
            $"开场：旁白 8 句全部完整显示在框里，放不下时翻页（翻了 {Narr.Dialog.PageCount - pagesBefore} 页）"
            + (overflow.Count > 0 ? "；放不下：" + string.Join(" | ", overflow) : "") + (missing.Length > 0 ? "；没完整出现过：" + string.Join(" | ", missing) : ""));
        Check(autoFlips.Count == 0, "开场：翻页都发生在玩家按推进键之后，不会在玩家还没看完时自己翻走" + (autoFlips.Count > 0 ? "：" + string.Join(" | ", autoFlips) : ""));
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
        Control npcPortrait = Narr.Dialog.GetNodeOrNull<Control>("Portrait");
        bool boxOn = OnScreen(Narr.Dialog.GetNodeOrNull<Control>("Box"), out string npcBox);
        bool portraitOn = OnScreen(npcPortrait, out string portraitInfo);
        Check(boxOn && portraitOn && OnScreen(Narr.Dialog.GetNodeOrNull<Control>("Box/Name"), out _)
              && Mathf.Abs(npcPortrait.GetGlobalRect().End.Y - Narr.Dialog.GetNodeOrNull<Control>("Box").GetGlobalRect().Position.Y) <= 0.5f,
            $"守坑人：对话框、名牌和整张立绘都在屏幕内，立绘脚底贴着对话框上沿（框 {npcBox}；立绘 {portraitInfo}）");
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
        bool barOnScreen = OnScreen(Narr.Subtitle.GetNodeOrNull<Control>("Bar"), out string barInfo);
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
        Check(barOnScreen, $"字幕：字幕条真的显示在屏幕内（{barInfo}）");
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

    // =================================================================
    // 剧情界面来自场景：场景能单独打开、节点齐全；改了场景游戏里跟着变；大立绘和表情；单句超长
    // =================================================================

    private static readonly (string name, string scene, string[] nodes)[] ViewScenes =
    {
        ("Dialog", DialogBox.ScenePath, new[] { "Box", "Box/Name", "Box/Body", "Box/Hint", "Portrait", "Portrait/Placeholder", "Portrait/Image", "Portrait/Caption" }),
        ("Subtitle", SubtitleBar.ScenePath, new[] { "Bar", "Bar/Text" }),
        ("Choice", ChoicePanel.ScenePath, new[] { "List", "OptionTemplate" }),
        ("Tablet", TabletView.ScenePath, new[] { "Dim", "Panel", "Panel/Title", "Panel/Body", "Panel/Hint" }),
        ("Cg", CgLayer.ScenePath, new[] { "Placeholder", "Image", "Caption" }),
        ("Fade", FadeLayer.ScenePath, Array.Empty<string>()),
    };

    private void ViewSceneChecks()
    {
        foreach (var (name, scene, nodes) in ViewScenes)
        {
            Node live = Narr.GetNodeOrNull(name);
            var packed = ResourceLoader.Exists(scene) ? GD.Load<PackedScene>(scene) : null;
            Node copy = packed?.Instantiate();
            string[] missing = copy == null ? nodes : nodes.Where(n => copy.GetNodeOrNull(n) == null).ToArray();
            copy?.Free();
            Check(live != null && live.SceneFilePath == scene && copy != null && missing.Length == 0,
                $"界面场景：剧情层的 {name} 来自 {scene}，场景能单独实例化，节点齐全" + (missing.Length > 0 ? "；缺：" + string.Join(", ", missing) : ""));
        }
        Node template = Narr.Choice.GetNodeOrNull("OptionTemplate");
        Check(template is Button { Visible: false }, "界面场景：选项按钮模板在场景里，平时隐藏");
    }

    /// <summary>
    /// 模拟“在编辑器里改了 dialog_box.tscn”：实例化一份，进树前改框的位置、名牌位置、正文字号，再用它播台词。
    /// 代码如果还在自己搭节点或者重写布局，这些改动就不会生效。
    /// </summary>
    private async Task SceneAuthorityChecks()
    {
        var box = GD.Load<PackedScene>(DialogBox.ScenePath).Instantiate<DialogBox>();
        box.Name = "EditedDialog";
        box.Settings = Narr.Settings;
        var frame = box.GetNode<Control>("Box");
        frame.OffsetTop -= 40;
        frame.OffsetBottom -= 40;
        var name = box.GetNode<Label>("Box/Name");
        name.Position = new Vector2(300, 6);
        var body = box.GetNode<RichTextLabel>("Box/Body");
        body.AddThemeFontSizeOverride("normal_font_size", 34);
        Narr.AddChild(box);
        await Frames(2);

        var withPortrait = new CharacterData { DisplayName = "测试甲", HasPortrait = true };
        var noPortrait = new CharacterData { DisplayName = "测试乙", HasPortrait = false };
        float frameTop = frame.GetGlobalRect().Position.Y;
        box.ShowLine("测试甲", "有立绘时名牌和正文不被代码挪动。", withPortrait, "");
        await Frames(2);
        bool keptWith = name.Position == new Vector2(300, 6) && Mathf.IsEqualApprox(body.OffsetLeft, 24f) && box.PortraitVisible;
        box.ShowLine("测试乙", "没立绘时也一样。", noPortrait, "");
        await Frames(2);
        bool keptWithout = name.Position == new Vector2(300, 6) && Mathf.IsEqualApprox(body.OffsetLeft, 24f) && !box.PortraitVisible;
        Check(keptWith && keptWithout && Mathf.IsEqualApprox(frame.GetGlobalRect().Position.Y, frameTop) && box.BaseFontSize == 34,
            $"场景为准：改了场景里框的位置、名牌位置、正文字号（34），有立绘、没立绘两种情况代码都不改它们（名牌 {name.Position}，字号 {box.BaseFontSize}）");

        // 字号变大后，开场旁白照样逐句完整放得下（会多翻几页）
        string[] narration =
        {
            "我是父亲用血和泥捏出的第一个。", "在祂说出我的名字之前，我只是泥。", "", "在人世（…?）之前，在塔庙（…?）之前，在两条河有名字之前，只有我。", "",
            "祂的手指在我身上留下了纹路，祂的血塑造了我的骨和肉。", "那时天和地还没有分开，混沌的母神在咸水里翻身。", "我第一次睁眼，然后看见祂。这就是这个世界的样子。", "",
            "父亲说：“你是我的手。”于是我成了父亲的手。", "父亲说：“你是我的刃。”于是我成了父亲的刃。",
        };
        var bad = new List<string>();
        int pages = box.PageCount;
        bool first = true;
        foreach (string line in narration)
        {
            if (line == "") { box.BreakParagraph(); continue; }
            if (first) box.ShowLine("", line, null, "");
            else box.AppendLine(line);
            first = false;
            box.CompleteTyping();
            await Frames(1);
            if (!box.TextFits || !box.CurrentText.Contains(line) || box.CurrentFontSize != 34) bad.Add($"“{line}”（字号 {box.CurrentFontSize}）");
        }
        Check(bad.Count == 0 && box.PageCount - pages >= 3,
            $"场景为准：正文改成 34 号字后，旁白每句都完整显示、字号不变，放不下就翻页（翻了 {box.PageCount - pages} 页）" + (bad.Count > 0 ? "；有问题：" + string.Join(" | ", bad) : ""));

        // 一句话自己就超过正文区：这一页缩小字号到放得下；下一页恢复
        string longLine = string.Concat(Enumerable.Repeat("这是一句非常非常长、自己一句就超过对话框正文区的台词，", 12));
        box.ShowLine("", longLine, null, "");
        box.CompleteTyping();
        await Frames(1);
        bool shrunk = box.TextFits && box.CurrentFontSize < 34 && box.CurrentFontSize >= DialogBox.MinFontSize && box.CurrentText == longLine;
        int shrunkSize = box.CurrentFontSize;
        box.ShowLine("", "短的一句。", null, "");
        box.CompleteTyping();
        await Frames(1);
        Check(shrunk && box.CurrentFontSize == 34 && box.TextFits,
            $"单句超长：{longLine.Length} 个字的一句缩到 {shrunkSize} 号字完整放下；下一句恢复 34 号");
        box.QueueFree();
        await Frames(1);
    }

    /// <summary>大立绘：整张在屏幕内、脚底贴对话框；表情换图，没有这个表情用默认图；没图显示色块；没勾 HasPortrait 不显示。</summary>
    private async Task PortraitChecks()
    {
        DialogBox dialog = Narr.Dialog;
        Texture2D normal = ImageTexture.CreateFromImage(Image.CreateEmpty(30, 40, false, Image.Format.Rgba8));
        Texture2D surprised = ImageTexture.CreateFromImage(Image.CreateEmpty(30, 40, false, Image.Format.Rgba8));
        var hero = new CharacterData { DisplayName = "测试丙", HasPortrait = true, Portrait = normal };
        hero.ExpressionPortraits["惊讶"] = surprised;
        var blank = new CharacterData { DisplayName = "测试丁", HasPortrait = true };

        dialog.ShowLine("测试丙", "默认表情。", hero, "");
        await Frames(2);
        Control portrait = dialog.GetNode<Control>("Portrait");
        Rect2 rect = portrait.GetGlobalRect();
        bool placed = OnScreen(portrait, out string info) && Mathf.Abs(rect.End.Y - dialog.GetNode<Control>("Box").GetGlobalRect().Position.Y) <= 0.5f && rect.Size.Y >= 300;
        bool normalShown = dialog.PortraitTexture == normal;
        dialog.ShowLine("测试丙", "惊讶。", hero, "惊讶");
        bool surprisedShown = dialog.PortraitTexture == surprised;
        dialog.ShowLine("测试丙", "生气（没有这张图）。", hero, "生气");
        bool fallback = dialog.PortraitTexture == normal;
        dialog.ShowLine("测试丁", "没有图。", blank, "");
        bool placeholder = dialog.PortraitVisible && dialog.PortraitTexture == null && dialog.GetNode<CanvasItem>("Portrait/Placeholder").Visible;
        dialog.ShowLine("", "旁白不显示立绘。", null, "");
        bool narrator = !dialog.PortraitVisible;
        dialog.Hide();
        Check(placed, $"立绘：整张立绘在屏幕内，脚底贴着对话框上沿（{info}）");
        Check(normalShown && surprisedShown && fallback, "立绘：按表情换图（惊讶 → 惊讶图），没有这个表情的图就用默认立绘");
        Check(placeholder && narrator, "立绘：没有图时显示占位色块；旁白不显示立绘");
        await Frames(1);
    }
}
