using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Player;
using SonOfMud.Save;
using SonOfMud.UI;
using SonOfMud.World;
using GDictionary = Godot.Collections.Dictionary;

/// <summary>
/// 收集页（计划 v2）验收：正式入口开新游戏，真实按键（Tab / M / Esc / 方向键 / E / R）。
/// ① 按键和互斥：Tab 开 / 关并暂停；M 只开地图；地图 ↔ 收集页、暂停菜单 → 收集页切换时每一帧都暂停；Esc 只关收集页；关掉后没有残留暂停来源；
///    剧情对话中按 Tab 打不开。
/// ② 新游戏 11 格全空，说明「？？？ / 还没有得到。」；方向键按列移动；鼠标移到格子上会选中。
/// ③ 画面：各层和 11 个格子都在屏幕里、格子不重叠；每个说明都放得下。
/// ④ 真实流程（CP2）：真实打死祈祷泥人、按 E 捡起 → 泥片格亮、1/2、说明对 → 存档、从正式入口继续 → 仍是 1/2。
///    真实碰到泥瓶 → 泥瓶格亮。
/// ⑤ 旧存档的泥肝（CP1）：存档里有 count.泥肝 = 3、没有 obtained.泥肝 → 继续 → 泥肝格 3/5 → 真实按 R 扔完 → 仍亮 0/5 → 存读档仍亮。
/// ⑥ 人物页（排在 ③ 之后）：一开始只有主角一页、没有箭头和页码；talked.igi 成立后两页，真实点鼠标左右箭头翻页（到头接回来），方向键不翻页；
///    在 S3_03 存档、读档后还是两页。
/// </summary>
public partial class CollectionScreenTest : EnemyFlowBase
{
    protected override string SuiteName => "collection_screen";
    protected override string ResultPrefix => "ui";

    private CollectionScreen Screen => FindChild<CollectionScreen>(MainGame);
    private MapScreen MapUi => FindChild<MapScreen>(MainGame);
    private PauseMenu MenuUi => FindChild<PauseMenu>(MainGame);

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            bool finished = await EnterMainGame(StartMode.NewGame);
            BindNarrative();
            await WaitSettled();
            Check(finished && Player != null && Screen != null && MapUi != null && MenuUi != null, "新游戏：正式入口开局，界面里有收集页、地图、暂停菜单");
            if (Screen == null) return;
            await KeyChecks();
            await EmptyAndNavigationChecks();
            await LayoutChecks();
            await PeopleChecks();
            await RealPickupChecks();
            await OldSaveLiverChecks();
        }
        finally
        {
            RealKeys.ReleaseAll();
            UnlistenEnemy();
        }
    }

    /// <summary>按住 2 帧再松开（界面按每帧轮询检测按下沿，真人按键也都会持续好几帧）。</summary>
    private async Task Press(Key key)
    {
        KeyDown(key);
        await Frames(2);
        KeyUp(key);
        await Frames(1);
    }

    private async Task Tab() => await Press(Key.Tab);

    /// <summary>按一下 key，记录按下前后若干帧里有没有一帧没暂停。</summary>
    private async Task<bool> AlwaysPausedAround(Key key, int frames = 6)
    {
        bool always = GetTree().Paused;
        KeyDown(key);
        for (int i = 0; i < frames; i++)
        {
            await Frames(1);
            if (i == 1) KeyUp(key);
            always &= GetTree().Paused && PauseService.IsPaused;
        }
        return always;
    }

    // ---------- ① 按键和互斥 ----------

    private async Task KeyChecks()
    {
        await Tab();
        await Frames(2);
        Check(Screen.IsOpen && Screen.Visible && GetTree().Paused && PauseService.IsRequestedBy(PauseService.Collection) && !MapUi.IsOpen,
            "按 Tab：收集页打开，游戏暂停（暂停来源 collection），地图没开");
        await Tab();
        await Frames(2);
        Check(!Screen.IsOpen && NoPauseOrLock() && !PauseService.IsPaused, "再按 Tab：收集页关闭，暂停解除");

        await Press(Key.M);
        await Frames(2);
        Check(MapUi.IsOpen && !Screen.IsOpen, "按 M：开的是地图，不是收集页（Tab 不再开地图）");
        bool paused = await AlwaysPausedAround(Key.Tab);
        Check(Screen.IsOpen && !MapUi.IsOpen && paused && !PauseService.IsRequestedBy(PauseService.Map),
            "地图开着按 Tab：切到收集页，切换的每一帧都暂停，地图的暂停来源已释放");
        paused = await AlwaysPausedAround(Key.M);
        Check(MapUi.IsOpen && !Screen.IsOpen && paused && !PauseService.IsRequestedBy(PauseService.Collection),
            "收集页开着按 M：切到地图，每一帧都暂停，收集页的暂停来源已释放");
        await Press(Key.M);
        await Frames(2);

        await Press(Key.Escape);
        await Frames(2);
        Check(MenuUi.IsOpen, "按 Esc：暂停菜单打开");
        paused = await AlwaysPausedAround(Key.Tab);
        Check(Screen.IsOpen && !MenuUi.IsOpen && paused && !PauseService.IsRequestedBy(PauseService.PauseMenu),
            "暂停菜单开着按 Tab：切到收集页，切换的每一帧都暂停，菜单的暂停来源已释放");
        await Press(Key.Escape);
        await Frames(2);
        Check(!Screen.IsOpen && !MenuUi.IsOpen && !MapUi.IsOpen && !PauseService.IsPaused && NoPauseOrLock(),
            "收集页开着按 Esc：只关收集页（不打开菜单），关掉后没有任何残留的暂停来源");

        Request("m_pit", "倒影");
        await WaitUntil(() => Narr.MainActive && Narr.Dialog.IsOpen, 60);
        await Tab();
        await Frames(2);
        Check(Narr.MainActive && !Screen.IsOpen, "剧情对话中按 Tab：打不开收集页");
        await AdvanceUntil(() => !Narr.MainActive, 12);
        await WaitUntil(() => NoPauseOrLock(), 60);
    }

    // ---------- ② 空格子、选格子 ----------

    private async Task EmptyAndNavigationChecks()
    {
        await Tab();
        await Frames(2);
        bool allEmpty = Enumerable.Range(0, Screen.Slots.Count).All(i => !Screen.IsObtained(i) && Screen.IconPath(i) == "" && Screen.CountOf(i) == "");
        Check(Screen.Slots.Count == 11 && allEmpty && Screen.NameLabel.Text == "？？？" && Screen.BodyLabel.Text == "还没有得到。",
            $"新游戏：11 个格子全是空的，说明栏「？？？ / 还没有得到。」（{Screen.Slots.Count} 格）");
        Check(Screen.Selected == 0 && Screen.Slots[0].GetNode<CanvasItem>("Highlight").Visible, "打开时选中第一个道具格，有高亮");
        var path = new List<int>();
        foreach (Key k in new[] { Key.Right, Key.Down, Key.Left, Key.Up })
        {
            await KeyTap(k);
            await Frames(1);
            path.Add(Screen.Selected);
        }
        // 道具 3 格、收集品 4 格，两排的列对不齐：上下换排时选横向最近的那一格
        Check(path.SequenceEqual(new[] { 1, 5, 4, 1 }), $"方向键：右 → 道具 2，下 → 横向最近的收集品第 3 格，左 → 第 2 格，上 → 横向最近的道具 2（{string.Join(",", path)}）");
        await KeyTap(Key.D);
        await Frames(1);
        Check(Screen.Selected == 2, $"WASD 也能移动：按 D 从道具 2 到道具 3（{Screen.Selected}）");
        Control target = Screen.Slots[7];
        Vector2 pos = target.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = pos, GlobalPosition = pos }, true);
        await Frames(3);
        Check(Screen.Selected == 7, $"鼠标移到收集品第 4 格上：选中它（选中 {Screen.Selected}）");
        await Tab();
        await Frames(2);
    }

    // ---------- ③ 画面 ----------

    private async Task LayoutChecks()
    {
        // 临时给满进度，看每个说明都放得下；看完还原
        ProgressStore p = GameSession.Progress;
        var saved = p.ToSave();
        foreach (CollectionEntry e in Screen.Entries)
            foreach (string flag in e.AllFlags()) p.SetVar(flag, true);
        p.SetVar("count.泥肝", 5);
        await Tab();
        await Frames(3);
        Rect2 view = GetViewport().GetVisibleRect().Grow(1f);
        var layers = new[] { "Bg", "Rays", "Portrait", "Panel", "Divider", "Desc", "Frame" }.Select(n => Screen.GetNode<Control>(n)).ToList();
        bool layersIn = layers.All(c => view.Encloses(c.GetGlobalRect()));
        bool slotsIn = Screen.Slots.All(s => view.Encloses(s.GetGlobalRect()));
        bool overlap = false;
        for (int i = 0; i < Screen.Slots.Count; i++)
            for (int j = i + 1; j < Screen.Slots.Count; j++)
                overlap |= Screen.Slots[i].GetGlobalRect().Intersects(Screen.Slots[j].GetGlobalRect());
        Check(layersIn && slotsIn && !overlap, "画面：各层图和 11 个格子都在屏幕里，格子互不重叠");
        var notFit = new List<string>();
        for (int i = 0; i < Screen.Slots.Count; i++)
        {
            Screen.Select(i);
            await Frames(1);
            Label body = Screen.BodyLabel;
            if (body.GetLineCount() > body.GetVisibleLineCount()) notFit.Add(Screen.NameLabel.Text);
        }
        Check(notFit.Count == 0 && Screen.Slots.Select((_, i) => Screen.IconPath(i)).All(x => x != ""), $"全部得到时：11 格都有图，每个说明都放得下（放不下：{string.Join("、", notFit)}）");
        await Tab();
        await Frames(2);
        p.FromSave(saved);
    }

    // ---------- ④ 真实流程 ----------

    private async Task<Vector2?> KillByStrikes(EnemyBody enemy)
    {
        if (enemy == null) return null;
        Vector2 at = enemy.GlobalPosition;
        for (int i = 0; i < 10 && !enemy.IsDead; i++)
        {
            await Strike(enemy);
            if (IsInstanceValid(enemy)) at = enemy.GlobalPosition;
        }
        await Frames(4);
        return enemy.IsDead ? at : null;
    }

    private async Task StandAt(Vector2 at)
    {
        RealKeys.ReleaseAll();
        await WaitUntil(() => PlayerFree && NoPauseOrLock(), 180);
        BodyFeet.PlaceFeetAt(Player, at + new Vector2(0f, -2f));
        Player.Velocity = Vector2.Zero;
        await Frames(8);
        await WaitUntil(() => PlayerFree, 60);
    }

    private async Task<string> SlotAfterTab(int index)
    {
        await WaitUntil(() => NoPauseOrLock() && !Narr.MainActive, 120);
        await Tab();
        await WaitUntil(() => Screen.IsOpen, 10);
        if (!Screen.IsOpen) return $"收集页没打开（剧情 {Narr.MainActive}，暂停 {PauseService.IsPaused}）";
        Screen.Select(index);
        await Frames(1);
        string info = $"{Screen.IsObtained(index)}|{Screen.CountOf(index)}|{Screen.NameLabel.Text}|{Screen.BodyLabel.Text}";
        await Tab();
        await Frames(2);
        return info;
    }

    private async Task SaveAndContinue(string room = "S1_04")
    {
        await Teleport(room, SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
    }

    private async Task RealPickupChecks()
    {
        await Teleport("S1_03", "From_S1_02");
        EnemyBody prayer = InRoom<EnemyBody>().FirstOrDefault(e => !e.IsDead && e.Data?.Kind == "prayer");
        await KillByStrikes(prayer);
        await Frames(3);
        CollectiblePickup shard = InRoom<CollectiblePickup>().FirstOrDefault(c => c.Available && c.Def?.Id == "prayer_shard");
        if (shard != null)
        {
            await StandAt(shard.GlobalPosition);
            await KeyTap(RealKeys.Interact);
            await WaitUntil(() => Narr.MainActive, 60);
            await AdvanceUntil(() => !Narr.MainActive, 12);
            await WaitUntil(() => NoPauseOrLock() && PlayerFree, 120);
        }
        string info = await SlotAfterTab(9);   // 收集品第 7 格：泥片
        Check(shard != null && info == "True|1/2|泥片|祈祷泥片：这块土，来自塔姆图的祈祷之子。",
            $"真实打死祈祷泥人、按 E 捡起泥片 → 收集页泥片格亮、1/2、说明对（掉了 {(shard != null ? "有" : "没有")}，捡起 {GameSession.Progress.Flag("collected.prayer_shard")}，{info}）");

        // 真实碰到泥瓶：先掉一点血，掉一个泥瓶在脚下 → 捡起来存着，不当场回血
        Player.HurtBox.Receive(new SonOfMud.Combat.Hit { AttackId = "collection_test_hurt", Damage = 20f, SourcePosition = Player.GlobalPosition, Blockable = false });
        await WaitUntil(() => PlayerFree, 120);
        int hurt = Player.Stats.Health, max = Player.Stats.MaxHealth;
        // 祈祷泥人按几率掉泥瓶，打死时可能已经自动捡了一瓶：先清零，让这里只算测试掉的这一瓶
        GameSession.Progress.SetVar(Pickup.CountKey("泥瓶"), 0);
        Events.Instance.EmitSignal(Events.SignalName.ItemDropped, "泥瓶", BodyFeet.FeetOffset(Player) * Vector2.Down + Player.GlobalPosition);
        await WaitUntil(() => Pickup.CountOf("泥瓶") == 1, 60);
        info = await SlotAfterTab(0);
        Check(info.StartsWith("True|1/5|泥瓶|") && Player.Stats.Health == hurt, $"真实碰到泥瓶 → 泥瓶格亮、存着 1/5，不当场回血（血 {Player.Stats.Health}，{info}）");

        // Tab 打开收集页，选中泥瓶按 E：喝一瓶回 30（不超过上限），变成 0/5；再按 E 说明栏写“没有泥瓶了。”
        await WaitUntil(() => NoPauseOrLock(), 120);
        await Tab();
        await WaitUntil(() => Screen.IsOpen, 10);
        Screen.Select(0);
        await Frames(1);
        await KeyTap(RealKeys.Interact);
        await Frames(2);
        bool drank = Player.Stats.Health == Mathf.Min(hurt + 30, max) && Screen.CountOf(0) == "0/5" && Screen.IsOpen;
        await KeyTap(RealKeys.Interact);
        await Frames(2);
        string empty = Screen.BodyLabel.Text;
        await Tab();
        await Frames(2);
        Check(drank && empty.EndsWith("没有泥瓶了。"), $"收集页里选中泥瓶按 E：喝一瓶，血 {hurt} → {Player.Stats.Health}，剩 0/5；再按 E 提示“没有泥瓶了。”（{empty}）");

        await SaveAndContinue();
        info = await SlotAfterTab(9);
        Check(Screen != null && info == "True|1/2|泥片|祈祷泥片：这块土，来自塔姆图的祈祷之子。", $"存档后从正式入口继续：泥片仍是 1/2（{info}）");
    }

    // ---------- ⑤ 旧存档的泥肝 ----------

    private async Task OldSaveLiverChecks()
    {
        // 做一个「旧存档」：正常存一次，再把存档里的 obtained.泥肝 删掉、count.泥肝 改成 3
        GameSession.Progress.SetVar("count.泥肝", 3);
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        SaveData data = SaveData.ReadFrom(SavePath, out string error);
        if (data != null)
        {
            var vars = data.Progress["vars"].AsGodotDictionary();
            vars.Remove("obtained.泥肝");
            vars["count.泥肝"] = 3;
            data.WriteAtomic(SavePath, out error);
        }
        bool oldFormat = data != null && SaveData.ReadFrom(SavePath, out _) is { } check && !check.Progress["vars"].AsGodotDictionary().ContainsKey("obtained.泥肝");
        await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        string info = await SlotAfterTab(1);
        Check(oldFormat && info.StartsWith("True|3/5|泥肝|"), $"旧存档（3 个泥肝、没有获得记录）继续游戏：泥肝格亮、3/5（{info}）");

        for (int i = 0; i < 3; i++)
        {
            await WaitUntil(() => PlayerFree && NoPauseOrLock(), 120);
            RealKeys.Down(RealKeys.Throw);
            await Frames(1);
            RealKeys.Up(RealKeys.Throw);
            await Frames(40);
        }
        await Frames(20);
        info = await SlotAfterTab(1);
        Check(ItemCounts.Get("泥肝") == 0 && info.StartsWith("True|0/5|泥肝|"), $"真实按 R 扔完 3 个：泥肝格仍亮、0/5（剩 {ItemCounts.Get("泥肝")}，{info}）");

        await SaveAndContinue();
        info = await SlotAfterTab(1);
        Check(info.StartsWith("True|0/5|泥肝|"), $"存档、读档后：泥肝格仍亮（{info}）");
    }

    // ---------- ⑥ 人物页 ----------

    private const string MulPortrait = "res://assets/data/narrative/portrait/por_mul_tukul_full.png";
    private const string IgiPortrait = "res://assets/data/narrative/portrait/por_igi_full.png";

    private string PersonInfo() =>
        $"{Screen.CurrentPerson}|{Screen.PersonName?.Text}|{Screen.PortraitRect?.Texture?.ResourcePath}|{(Screen.PersonPage is { Visible: true } p ? p.Text : "")}|{Screen.PrevPerson?.IsVisibleInTree() == true}|{Screen.NextPerson?.IsVisibleInTree() == true}";

    private async Task ClickAt(Vector2 position)
    {
        foreach (bool pressed in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = position, GlobalPosition = position });
            await Frames(2);
        }
    }

    private async Task Click(Control button)
    {
        Vector2 pos = button.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = pos, GlobalPosition = pos }, true);
        await Frames(1);
        await ClickAt(pos);
    }

    /// <summary>截一张收集页的图到 tests/output（人工看布局用，不做判断）。</summary>
    private void Snapshot(string name)
    {
        Image image = GetViewport().GetTexture()?.GetImage();
        if (image == null) return;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tests/output"));
        image.SavePng(ProjectSettings.GlobalizePath($"res://tests/output/collection_people_{name}.png"));
    }

    private async Task PeopleChecks()
    {
        GameSession.Progress.SetVar("talked.igi", false);
        await Tab();
        await Frames(2);
        string info = PersonInfo();
        Check(Screen.IsOpen && info == $"mul_tukul|穆尔图库尔|{MulPortrait}||False|False", $"人物页：还没遇到伊吉，只有主角一页，没有箭头和页码（{info}）");
        Snapshot("1_only_hero");
        await Tab();
        await Frames(2);

        // 伊吉第一次说完话时剧本设 talked.igi（见 c_city.orph「伊吉」），这里直接设同一个变量
        GameSession.Progress.SetVar("talked.igi", true);
        await Tab();
        await Frames(2);
        info = PersonInfo();
        Check(info == $"mul_tukul|穆尔图库尔|{MulPortrait}|1 / 2|True|True", $"talked.igi 成立后：两页，先显示主角，出现左右箭头和页码 1 / 2（{info}）");
        Snapshot("2_hero_of_two");

        int selected = Screen.Selected;
        await Click(Screen.NextPerson);
        info = PersonInfo();
        Check(info == $"igi|伊吉|{IgiPortrait}|2 / 2|True|True" && Screen.Selected == selected, $"鼠标点右箭头：翻到伊吉，全身立绘换成伊吉，2 / 2；右边选中的格子不变（{info}）");
        Snapshot("3_igi");
        await Click(Screen.NextPerson);
        string wrapped = PersonInfo();
        await Click(Screen.PrevPerson);
        info = PersonInfo();
        Check(wrapped.StartsWith("mul_tukul|") && wrapped.EndsWith("|1 / 2|True|True") && info.StartsWith("igi|伊吉|"), $"再点右箭头回到主角，点左箭头又到伊吉（{wrapped} → {info}）");

        await KeyTap(Key.Left);
        await Frames(1);
        await KeyTap(Key.Right);
        await Frames(1);
        Check(Screen.CurrentPerson == "igi", $"方向键只选格子，不翻人物页（{Screen.CurrentPerson}）");
        await Tab();
        await Frames(2);

        await SaveAndContinue("S3_03");
        await Tab();
        await Frames(2);
        info = PersonInfo();
        Check(GameSession.Progress.Flag("talked.igi") && (info.EndsWith("|1 / 2|True|True") || info.EndsWith("|2 / 2|True|True")), $"存档、读档后：仍是两页（{info}）");
        await Tab();
        await Frames(2);
    }
}
