using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 地图二～四的剧情触发（计划_剧本按新版更新）：正式入口开新游戏，真实按键。
/// 每个房间放到默认出生点：走进去自动触发的剧情当场播；按 E 的雕像、壁画、伊吉、泥板、占位触发走过去按 E 播；
/// 占位的“水闸之后”“龙蛇·倒下”“裂痕”设好开门变量（门真的打开）；最终 Boss 的占位接真相、泥板「名字」、结局选项；
/// “醒来”显示泥板「星」，“回去睡”重开。全程零报错。
/// </summary>
public partial class NarrativeStoryTest : NarrativeFlowBase
{
    protected override string SuiteName => "narrative_story";

    protected override async Task RunChecks()
    {
        Listen();
        // 这里只测剧情：每进一个房间就让里面的小怪停下来，免得打到人（死了会被送回存档点）
        Events.Instance.RoomEntered += OnRoomEntered;
        try
        {
            int errorBase = Errors.Count;
            int narrativeErrors = NarrativeLog.ErrorCount;
            bool started = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(started && Narr != null && Interactor != null, "正式入口开新游戏");
            if (Narr == null || Interactor == null) return;

            await MapTwo();
            await MapThree();
            await MapFour();

            Check(Errors.Count == errorBase && NarrativeLog.ErrorCount == narrativeErrors,
                "全程零报错" + (Errors.Count > errorBase ? "：" + string.Join(" | ", Errors.Snapshot().Skip(errorBase).Take(5)) : ""));
        }
        finally
        {
            Events.Instance.RoomEntered -= OnRoomEntered;
            Unlisten();
        }
    }

    // ---------- 工具 ----------

    private string Snap()
    {
        if (Narr.Tablet.IsOpen) return $"泥板「{Narr.Tablet.CurrentTitle}」";
        if (Narr.Choice.IsOpen) return "选项";
        string cg = Narr.Cg.IsShowing ? $"[{Narr.Cg.CurrentId}]" : "";
        return $"{cg}{(Narr.Dialog.NameVisible ? Narr.Dialog.CurrentName : "旁白")}：{Narr.Dialog.CurrentText}";
    }

    /// <summary>按 E 看完正在播的剧情，记下每一句（选项打开时停下）。</summary>
    private async Task<List<string>> Play(int maxTaps = 80)
    {
        var log = new List<string>();
        await AdvanceUntil(() => !Narr.MainActive, maxTaps, () => { string s = Snap(); if (log.LastOrDefault() != s) log.Add(s); });
        if (!Narr.MainActive)
        {
            await WaitUntil(() => !PauseService.IsPaused, 60);
            await Frames(3);
        }
        return log;
    }

    private static string Show(List<string> log) => string.Join(" / ", log.Select(l => l.Length > 24 ? l[..24] + "…" : l));

    /// <summary>放到房间默认出生点；有自动触发的剧情就等它开始（字幕剧情等字幕出现）。</summary>
    private async Task Enter(string room)
    {
        await Teleport(room, SpawnPoint.DefaultId);
        FreezeEnemies(World.CurrentRoom);
        await WaitUntil(() => Narr.MainActive || Narr.Subtitle.IsShowing, 30);
    }

    private void OnRoomEntered(string roomId) { Callable.From(() => { if (World?.CurrentRoom != null) FreezeEnemies(World.CurrentRoom); }).CallDeferred(); }

    private static void FreezeEnemies(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is SonOfMud.Enemy.EnemyBody enemy) enemy.ProcessMode = ProcessModeEnum.Disabled;
            FreezeEnemies(child);
        }
    }

    private Node2D Obj(string name) => World.CurrentRoom.GetNodeOrNull<Node2D>("Objects/" + name);

    /// <summary>走到物件旁边出现 prompt 提示，按 E，等剧情或泥板开始。走过头（超过物件 64 像素）就停，不会走出房间。</summary>
    private async Task<bool> Use(string node, string prompt)
    {
        Node2D target = Obj(node);
        if (target == null) return false;
        float x = target.GlobalPosition.X;
        bool right = x >= Player.GlobalPosition.X;
        StringName dir = right ? PlayerActions.Right : PlayerActions.Left;
        bool Passed() => right ? Player.GlobalPosition.X > x + 64f : Player.GlobalPosition.X < x - 64f;
        if (Interactor.CurrentPrompt != prompt) await HoldUntil(dir, () => Interactor.CurrentPrompt == prompt || Passed(), 400);
        await WaitUntil(() => Mathf.Abs(Player.Velocity.X) < 1f, 60);
        if (Interactor.CurrentPrompt != prompt) await HoldUntil(right ? PlayerActions.Left : PlayerActions.Right, () => Interactor.CurrentPrompt == prompt, 60);
        await Frames(2);
        if (Interactor.CurrentPrompt != prompt)
        {
            GD.Print($"    走不到 {node}：提示“{Interactor.CurrentPrompt}”，玩家 {Player.GlobalPosition}，物件 {target.GlobalPosition}，可见 {target.Visible}");
            return false;
        }
        await KeyTap(Key.E);
        return await WaitUntil(() => Narr.MainActive || Narr.Tablet.IsOpen, 60);
    }

    private static bool Flag(string key) => GameSession.Progress.Flag(key);
    private bool GateOpen(string target) => World.CurrentRoom.GetNodeOrNull<GateBarrier>("Objects/GateTo" + target)?.IsOpen == true;

    // ---------- 地图二 ----------

    private async Task MapTwo()
    {
        await Enter("S2_01");
        string label = Narr.MainLabel;
        List<string> worksite = await Play();
        Check(label == "工地入口" && worksite.FirstOrDefault() == "泥人：祂睡在泥里。祂会醒来。祂会来看我们。" && worksite.LastOrDefault() == "穆尔图库尔：……是，太吵了。",
            $"S2_01 走进工地入口自动播（{Show(worksite)}）");

        await Enter("S2_02");
        bool mural = await Use("Story_Mural", "查看壁画");
        List<string> muralLog = await Play();
        Check(mural && muralLog.FirstOrDefault()?.StartsWith("泥板「") == true && muralLog.LastOrDefault() == "穆尔图库尔：是父亲。是父亲劈开了她。",
            $"S2_02 按 E 查看壁画：先显示铭文，再“是父亲。是父亲劈开了她。”（{Show(muralLog)}）");
        bool smile = await Use("Tablet_smile", "拾取泥板");
        Check(smile && Narr.Tablet.CurrentTitle == "笑容" && Narr.Tablet.CurrentText.Contains("那是祂第一次为我笑"), $"S2_02 泥板「笑容」（{Narr.Tablet.CurrentText}）");
        await Play(5);

        await Enter("S2_03");
        bool bark = await WaitUntil(() => Narr.Subtitle.IsShowing, 60);
        Check(bark && Narr.Subtitle.CurrentText == "打开它！他们太吵了。" && !PauseService.IsPaused, $"S2_03 走进水闸：字幕“打开它！他们太吵了。”，不暂停（{Narr.Subtitle.CurrentText}）");
        await WaitUntil(() => !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing, 400);
        Check(!GateOpen("S2_04") && !Obj("Tablet_after_flood").Visible, "S2_03 水闸没开：通往 S2_04 的门关着，泥板「洪水之后」还没出现");
        bool sluice = await Use("Story_SluiceOpen", "（占位）打开水闸");
        List<string> quiet = await Play();
        Check(sluice && quiet.LastOrDefault() == "穆尔图库尔：……安静了。" && Flag("flood_done") && GateOpen("S2_04") && Obj("Tablet_after_flood").Visible,
            "S2_03 占位“打开水闸”：“……安静了。”，flood_done 成立，门打开，泥板「洪水之后」出现");

        await Enter("S2_04");
        Check(!Obj("Story_Tamtu").Visible && !GateOpen("S2_06"), "S2_04 龙蛇倒下之前：塔姆图雕像还没出现，通往 S2_06 的门关着");
        bool dragon = await Use("Story_DragonFall", "（占位）打倒龙蛇");
        List<string> fall = await Play();
        Check(dragon && fall.FirstOrDefault() == "泥人：祂……爱过我们……" && Flag("boss_dragon") && GateOpen("S2_06") && Obj("Story_Tamtu").Visible,
            $"S2_04 占位“打倒龙蛇”：四句对话，boss_dragon 成立，门打开，塔姆图雕像出现（{Show(fall)}）");
        bool tamtu = await Use("Story_Tamtu", "触碰雕像");
        List<string> sea = await Play();
        Check(tamtu && sea.Any(l => l.StartsWith("[sea_war]塔姆图：这血")) && !Narr.Cg.IsShowing && Obj("Tablet_destiny").Visible,
            $"S2_04 触碰塔姆图雕像：CG3「海中之战」，播完 CG 关掉，泥板「命运石板」出现（{Show(sea)}）");

        await Enter("S2_05");
        bool meteor = await Use("Story_Meteor", "触碰雕像");
        List<string> star = await Play();
        Check(meteor && star.Any(l => l == "[meteor]伊姆杜：星。") && star.LastOrDefault()?.EndsWith("只有那么久。") == true,
            $"S2_05 触碰流星雕像：CG4「流星」（{star.Count} 句）");
    }

    // ---------- 地图三 ----------

    private async Task MapThree()
    {
        await Enter("S3_01");
        List<string> gate = await Play();
        Check(gate.SequenceEqual(new[] { "？？？：母神的守卫。杀了他们！", "穆尔图库尔：……是，父亲。" }), $"S3_01 走进城门自动播（{Show(gate)}）");

        await Enter("S3_02");
        bool relief = await Use("Story_Relief", "查看浮雕");
        List<string> reliefLog = await Play();
        Check(relief && reliefLog.SequenceEqual(new[] { "泥板「浮雕铭文」" }), $"S3_02 按 E 查看浮雕铭文（{Show(reliefLog)}）");

        await Enter("S3_03");
        bool igi = await Use("Npc_Igi", "交谈");
        List<string> meet = await Play();
        Check(igi && meet.Contains("伊吉：我是伊吉，祂的伽拉。") && meet.LastOrDefault() == "穆尔图库尔：塔姆图？" && Flag("talked.igi"),
            $"S3_03 和伊吉对话：神殿相遇接肝占卜，talked.igi 成立（{meet.Count} 句）");
        bool again = await Use("Npc_Igi", "交谈");
        List<string> liver = await Play();
        Check(again && liver.FirstOrDefault()?.StartsWith("伊吉：神的心意，写在肝上。") == true && !liver.Any(l => l.Contains("我是伊吉")) && liver.LastOrDefault() == "穆尔图库尔：塔姆图？",
            $"S3_03 再和伊吉对话：只播肝占卜（{Show(liver)}）");

        // 肝占卜：三个街区各一个征兆，伊吉提问；先答错一次（再问一次），再答对
        Check(Obj("Tablet_hoped_world") == null, "S3_04 地上没有泥板「期望的世界」（答对肝占卜后由剧情显示）");
        await Enter("S3_04");
        List<string> world = await Divine("Story_Omen1", wrong: 0, right: 1, "S3_04 第一街区", "神看见的是什么？");
        Check(world.Contains("泥板「期望的世界」") && world.LastOrDefault() == "？？？：我的神子，我的冠军。" && Flag("liver.1") && !Obj("Story_Omen1").Visible,
            $"S3_04 第一街区答对“两条河”：泥板「期望的世界」→ 穆尔图库尔两句 → “我的神子，我的冠军。”，征兆消失（{Show(world)}）");

        await Enter("S3_06");
        Check(!GateOpen("S3_07") && !Obj("Story_Crack").Visible, "S3_06 三块没读完：“让伊吉读泥肝”还没出现，通往 S3_07 的门关着");

        await Enter("S3_05");
        Check(Obj("Tablet_too_soft") == null, "S3_05 地上没有泥板「他们太软了」");
        List<string> mud = await Divine("Story_Omen2", wrong: 0, right: 2, "S3_05 第二街区", "手里捧着的是什么？");
        Check(mud.Contains("[clay]齐：……齐。") && mud.Contains("泥板「他们太软了」") && Flag("liver.2") && !Narr.Cg.IsShowing,
            $"S3_05 第二街区答对“泥”：CG5「泥」→ 泥板「他们太软了」（{mud.Count} 段）");

        await Enter("S3_06");
        List<string> curse = await Divine("Story_Omen3", wrong: 2, right: 1, "S3_06 第三街区", "七道痕，说的是什么？");
        Check(curse.Contains("泥板「诅咒」") && Flag("liver.3") && Obj("Story_Crack").Visible, $"S3_06 第三街区答对“七个夜晚”：泥板「诅咒」，“让伊吉读泥肝”出现（{Show(curse)}）");
        bool crack = await Use("Story_Crack", "让伊吉读泥肝");
        List<string> found = await Play();
        Check(crack && found.LastOrDefault() == "穆尔图库尔：父亲在找我！我就在这里。" && Flag("districts_done") && GateOpen("S3_07"),
            "S3_06 三块都读出后“让伊吉读泥肝”：伊吉读出裂痕，districts_done 成立，门打开");

        await Enter("S3_08");
        List<string> ascend = await Play();
        Check(ascend.FirstOrDefault() == "伊吉：祂在跟谁说话？" && ascend.LastOrDefault() == "？？？：我的儿子！上来。我在上面等你。", $"S3_08 走到登塔入口自动播（{Show(ascend)}）");
    }

    /// <summary>选第 index 个选项（从 0 数，真实按键：按下 index 次再按 E）。</summary>
    private async Task Choose(int index)
    {
        await WaitUntil(() => Narr.Choice.IsOpen, 300);
        for (int i = 0; i < index; i++) await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        await Frames(2);
    }

    /// <summary>按 E 查看征兆：伊吉提问 → 先选错（伊吉说“……不是这样读的。”再问一次）→ 再选对 → 播完。返回选对之后播的内容。</summary>
    private async Task<List<string>> Divine(string omen, int wrong, int right, string where, string question)
    {
        bool used = await Use(omen, "查看征兆");
        List<string> asked = await Play();
        Check(used && Narr.Choice.IsOpen && Narr.Dialog.CurrentName == "伊吉" && Narr.Dialog.CurrentText.EndsWith(question),
            $"{where}：按 E 查看征兆，伊吉问“{question}”，出现选项（{Narr.Dialog.CurrentText}）");
        await Choose(wrong);
        List<string> retry = await Play();
        Check(retry.FirstOrDefault() == "伊吉：……不是这样读的。" && Narr.Choice.IsOpen, $"{where}：选错 → 伊吉“……不是这样读的。”，再问一次（{Show(retry)}）");
        await Choose(right);
        return await Play(120);
    }

    // ---------- 地图四 ----------

    private async Task MapFour()
    {
        await Enter("S4_02");
        string label = Narr.MainLabel;
        List<string> gates = await Play();
        Check(label == "七道门" && gates.LastOrDefault() == "旁白：第一道门前，他摘下了头上的角冠。", $"S4_02 落进七道门自动播（{Show(gates)}）");
        await Teleport("S4_02", SpawnPoint.DefaultId);
        Check(!Narr.MainActive, "S4_02 再进来不重播（只播一次）");

        await Enter("S4_05");
        List<string> summit = await Play();
        Check(summit.LastOrDefault() == "？？？：你来拯救我了，又一次，我的儿子。", $"S4_05 走进终末之室自动播（{Show(summit)}）");
        Check(Obj("Tablet_name") == null && Obj("Tablet_star") == null, "S4_05 地上没有泥板「名字」「星」（改由结局剧情显示）");
        bool boss = await Use("Story_FinalBoss", "（占位）打倒最终 Boss");
        List<string> truth = await Play(120);
        Check(boss && truth.Contains("伊姆杜：我的儿子，救救我。") && truth.Contains("穆尔图库尔：我的儿子……") && truth.Contains("[patricide]伊姆杜：我的儿子。")
              && truth.Any(l => l.StartsWith("[third_world]")) && truth.Any(l => l.StartsWith("[slumber]")) && truth.Contains("泥板「名字」") && Narr.Choice.IsOpen,
            $"S4_05 占位“打倒最终 Boss”：？？？改名为穆尔图库尔，CG6–8，泥板「名字」，停在结局选项（{truth.Count} 句）");
        await KeyTap(Key.E);
        List<string> wake = await Play();
        Check(wake.FirstOrDefault() == "穆尔图库尔：父亲？" && wake.Contains("泥板「星𒀭」") && !Narr.MainActive, $"结局选“醒来”：“父亲？”……，泥板「星」（{Show(wake)}）");

        int restarts = Restarts;
        Request("h_tower", "结局");
        await WaitUntil(() => Narr.Choice.IsOpen, 60);
        await Tap(PlayerActions.Down);
        await KeyTap(Key.E);
        await AdvanceUntil(() => Restarts > restarts || !Narr.MainActive, 20);
        Check(Restarts == restarts + 1, "结局选“回去睡”：“——我的儿子，醒来。”之后重开");
    }
}
