using Godot;
using System;
using System.Threading.Tasks;
using GDictionary = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;
using Kind = MudActionState.Kind;

/// <summary>Real Godot input, physics, scene, combat and save regression tests.</summary>
public partial class MudRuntimeSuite : Node
{
    public const string OutputDirectory = "res://tests/output/";
    public int Failures { get; private set; }
    public int Checks { get; private set; }
    public GDictionary Metrics { get; } = new();
    private readonly GArray _lines = new();
    public MudWhiteboxRoom Room { get; set; }
    public MudPlayer Player { get; set; }
    private GDictionary _world;
    private MudProgress Progress => MudProgress.Instance;
    private MudRoomTravel Travel => MudRoomTravel.Instance;
    public override void _Ready() => Callable.From(Run).CallDeferred();
    public void Check(bool condition, string message)
    {
        Checks++;
        if (!condition) Failures++;
        var line = $"Tests: {(condition ? "PASS" : "FAIL")} {message}";
        _lines.Add(line);
        GD.Print(line);
    }
    public async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
    public void ReleaseInputs() { foreach (var key in InputMap.GetActions()) Input.ActionRelease(key); }
    public void RefreshScene()
    {
        Room = (MudWhiteboxRoom)GetTree().CurrentScene;
        Player = Room.GetNode<MudPlayer>("Player");
    }
    public async Task LoadRoom(string id)
    {
        ReleaseInputs();
        Travel.EnterRoom(id, "Start", false);
        await Frames(8);
        RefreshScene();
        await Frames(3);
    }
    public async Task ResetActor(Vector2 at, int form = 0)
    {
        ReleaseInputs();
        Player.RestoreTransient(new GDictionary { ["form"] = form });
        Player.Position = at;
        Player.Health = 10000;
        await Frames(5);
    }
    private async Task<GDictionary> MeasureJump(int form, int module, int holdFrames)
    {
        await ResetActor(new Vector2(400, Room.FloorY - 24), form);
        Progress.MudModule = module;
        var origin = Player.Position;
        var peak = 0f;
        var bursts = Player.Combat.LandedBursts;
        Input.ActionPress("jump"); Input.ActionPress("move_right");
        var launched = false;
        for (var i = 0; i < 160; i++)
        {
            if (i == holdFrames) Input.ActionRelease("jump");
            await Frames(1);
            peak = Mathf.Max(peak, origin.Y - Player.Position.Y);
            if (!Player.IsOnFloor()) launched = true;
            if (launched && Player.IsOnFloor()) break;
        }
        var result = new GDictionary { ["height"] = peak, ["distance"] = Player.Position.X - origin.X, ["bursts"] = Player.Combat.LandedBursts - bursts };
        ReleaseInputs();
        return result;
    }
    public async Task SnapshotImage(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(OutputDirectory + name + ".png");
    }
    private async void Run()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(OutputDirectory);
            // Keep the driver alive while replacing CurrentScene.
            if (GetParent() != GetTree().Root) Reparent(GetTree().Root);
            if (GetTree().CurrentScene == this) GetTree().CurrentScene = null;
            Progress.AllowDisk = false;
            Progress.NewGame(false);
            _world = Json.ParseString(FileAccess.GetFileAsString("res://assets/data/whitebox/world.json")).AsGodotDictionary();
            Check(Engine.GetVersionInfo().Str("string").StartsWith("4.7.2"), "实际Godot版本4.7.2");
            Check(OS.HasFeature("mono"), "实际Godot .NET运行时");
            Check(_world.Dict("rooms").Count == 24, "23主线场景+1测试场，无旧版多余房间");
            var args = OS.GetCmdlineUserArgs();
            if (System.Array.IndexOf(args, "--scene-paths-only") >= 0)
            { await new MudSceneMigrationChecks().Run(this); FinishReport("csharp_scene_migration_results"); return; }
            if (System.Array.IndexOf(args, "--feel-only") >= 0)
            { await new MudCombatFeelChecks().Run(this); FinishReport("csharp_combat_feel_results"); return; }
            if (System.Array.IndexOf(args, "--jump-landing-only") >= 0 || System.Array.IndexOf(args, "--jump-landing-repro") >= 0)
            { await JumpLandingChecks(); FinishReport("csharp_jump_landing_results"); return; }
            if (System.Array.IndexOf(args, "--attacks-only") >= 0)
            { await AttackChecks(); FinishReport("csharp_attack_results"); return; }
            if (System.Array.IndexOf(args, "--stairs-only") >= 0)
            { await ClimbChecks(); await FinalPatchChecks(); await AttackChecks(); FinishReport("csharp_stairs_results"); return; }
            await MovementAndGuardChecks();
            if (System.Array.IndexOf(args, "--focused") < 0) await ConnectionsChecks();
            await ProgressionChecks();
            await ClimbChecks();
            await FinalPatchChecks();
            await AttackChecks();
            await JumpLandingChecks();
            await new MudCombatFeelChecks().Run(this);
            await new MudSceneMigrationChecks().Run(this);
            FinishReport("csharp_runtime_results");
        }
        catch (Exception exception) { Check(false, exception.ToString()); FinishReport("csharp_runtime_results"); }
    }
    private void FinishReport(string filename)
    {
        ReleaseInputs();
        using var report = FileAccess.Open(OutputDirectory + filename + ".json", FileAccess.ModeFlags.Write);
        var data = new GDictionary
        {
            ["checks"] = Checks, ["failures"] = Failures, ["display"] = DisplayServer.GetName(),
            ["rendering"] = RenderingServer.GetCurrentRenderingMethod(), ["metrics"] = Metrics, ["results"] = _lines
        };
        report?.StoreString(Json.Stringify(data, "\t"));
        GD.Print($"Tests: {(Failures == 0 ? "PASS" : "FAIL")} 总计{Checks}项，失败{Failures}项；仅报告实际执行项目");
        GetTree().Quit(Failures == 0 ? 0 : 1);
    }
    private async Task MovementAndGuardChecks()
    {
        Progress.EnterTest();
        await LoadRoom("TEST");
        var keyEvent = new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true };
        Input.ParseInputEvent(keyEvent); await Frames(3);
        Check(Player.Form == 1, "物理键Q通过InputMap触发形态切换");
        keyEvent.Pressed = false; Input.ParseInputEvent(keyEvent); await Frames(2);
        foreach (var key in new[] { "double_jump", "wall_climb", "heavy_slam", "dive" }) Progress.TestAbilities[key] = false;
        var water = Player.WaterConfig;
        var mud = Player.MudConfig;
        Check(water.MoveSpeed > mud.MoveSpeed && water.DodgeCooldown < mud.DodgeCooldown && water.Damage < mud.Damage, "两形态配置相对关系");
        foreach (var form in new[] { 0, 1 })
        {
            await ResetActor(new Vector2(400, Room.FloorY - 24), form);
            var start = Player.Position.X;
            Input.ActionPress("move_right"); await Frames(60); Input.ActionRelease("move_right");
            Metrics[$"move_{form}"] = Player.Position.X - start;
        }
        Check(Metrics.Num("move_0") > Metrics.Num("move_1") && Metrics.Num("move_1") > 250, "真实物理帧：水移动快于泥");
        foreach (var form in new[] { 0, 1 })
        {
            await ResetActor(new Vector2(400, Room.FloorY - 24), form);
            var start = Player.Position.X;
            Player.StartDodge();
            for (var i = 0; Player.Action.State == Kind.Dodge && i < 60; i++) await Frames(1);
            Metrics[$"dodge_{form}"] = Player.Position.X - start;
            if (form != 1) continue;
            Check(Mathf.Abs(Player.PenaltyRemaining - 1f) < 0.04f && Mathf.Abs(Player.PenaltyMultiplier() - 0.2f) < 0.03f, "泥闪避后1秒曲线减速，初始倍率0.2");
            var cooldown = Player.DodgeCooldown;
            Player.SwitchForm();
            Check(Player.DodgeCooldown == cooldown && Player.PenaltyRemaining > 0.95f, "切换不刷新闪避冷却或减速");
            await Frames(30);
            Check(Mathf.Abs(Player.PenaltyMultiplier() - 0.8f) < 0.04f, "泥减速0.5秒恢复至约80%移速");
            await Frames(32);
            Check(Player.PenaltyRemaining == 0f && Mathf.IsEqualApprox(Player.PenaltyMultiplier(), 1f), "泥减速结束后完全恢复");
        }
        Check(Metrics.Num("dodge_0") > Metrics.Num("dodge_1"), "实际闪避距离：水大于泥");
        await ResetActor(new Vector2(400, Room.FloorY - 24));
        Input.ActionPress("block"); await Frames(2);
        Check(Player.Action.State == Kind.Block, "水按下K进入格挡");
        Check(Player.ReceiveAttack(10, "water_a", Player.Position) == "blocked", "水成功格挡一次");
        Check(Player.ReceiveAttack(10, "water_a", Player.Position) == "duplicate", "同一攻击全生命周期去重");
        Check(Player.ReceiveAttack(10, "water_b", Player.Position) == "hurt", "水格挡成功即结束，下一次攻击造成伤害");
        await Frames(45);
        Check(Player.Action.State != Kind.Block, "持续按住水格挡不自动续挡");
        await ResetActor(new Vector2(400, Room.FloorY - 24));
        Input.ActionPress("block"); await Frames(27);
        Check(Player.Action.State == Kind.Block && Player.Action.Remaining <= 0.1f, "水0.5秒格挡窗口末段仍有效");
        await Frames(7); Check(Player.Action.State == Kind.Free, "水格挡窗口结束");
        await ResetActor(new Vector2(1800, Room.FloorY - 24), 1);
        Input.ActionPress("block"); await Frames(2);
        Check(Player.ReceiveAttack(10, "mud_a", Player.Position) == "blocked" && Player.ReceiveAttack(10, "mud_b", Player.Position) == "blocked", "泥可格挡不同有效攻击");
        Check(Player.Combat.BonusReady, "泥格挡提供不叠加的增伤资格");
        await Frames(122); Check(Player.Action.State == Kind.Free, "泥格挡最长2秒");
        ReleaseInputs(); Player.Combat.Start(mud, false); await Frames(120);
        Check(Player.Combat.BonusReady, "挥空保留格挡增伤");
        Player.Position = new Vector2(1855, Player.Position.Y); Player.Facing = 1;
        Input.ActionPress("attack"); await Frames(120); Input.ActionRelease("attack");
        Check(!Player.Combat.BonusReady && Player.Combat.LastDamage > mud.Damage, "真实命中后消费格挡增伤");
        var waterJump = await MeasureJump(0, 0, 100);
        var mud1 = await MeasureJump(1, 0, 100);
        var shortJump = await MeasureJump(1, 1, 18);
        var fullJump = await MeasureJump(1, 1, 100);
        Metrics["water_jump"] = waterJump; Metrics["mud1_jump"] = mud1; Metrics["mud2_short"] = shortJump; Metrics["mud2_full"] = fullJump;
        Check(mud1.Int("bursts") == 0 && waterJump.Int("bursts") == 0, "普通跳跃落地不产生范围攻击");
        Check(fullJump.Num("height") > shortJump.Num("height") + 30 && fullJump.Num("height") <= 176.5f, "长按跳跃更高且不越过176px上限");
        Check(fullJump.Int("bursts") == 1 && shortJump.Int("bursts") == 0, "达到长按高度上限后仅落地爆发一次");
        await ResetActor(new Vector2(400, Room.FloorY - 280), 1); await Frames(80);
        Check(!Player.ReachedCap, "普通跌落不获得长按上限资格");
        Player.ReachedCap = true;
        var bursts = Player.Combat.LandedBursts;
        Player.RestoreTransient(Player.ExportTransient()); await Frames(5);
        Check(!Player.ReachedCap && Player.Combat.LandedBursts == bursts, "换场状态恢复清除落地伤害资格");
        await ResetActor(new Vector2(400, Room.FloorY - 24));
        Input.ActionPress("jump"); await Frames(15);
        var used = Player.JumpsUsed; Player.SwitchForm();
        Check(Player.JumpsUsed == used && Player.JumpForm == 0, "空中切换保留起跳配置及跳跃次数");
        ReleaseInputs(); await Frames(80);
    }
    private async Task ConnectionsChecks()
    {
        Progress.NewGame(false);
        var rooms = _world.Dict("rooms");
        foreach (var idValue in rooms.Keys)
        {
            var id = idValue.AsString();
            if (id == "TEST") continue;
            await LoadRoom(id);
            Check(Room.RoomId == id && GodotObject.IsInstanceValid(Player), "主线场景运行 " + id);
            foreach (var portValue in rooms.Dict(id).Array("ports"))
            {
                var port = portValue.AsGodotDictionary();
                var found = false;
                foreach (var backValue in rooms.Dict(port.Str("target")).Array("ports"))
                    if (backValue.AsGodotDictionary().Str("target") == id) found = true;
                Check(found, "目标房间有独立返回出生点 " + port.Str("id"));
            }
        }
        foreach (var key in new[] { "double_jump", "wall_climb", "heavy_slam", "dive" }) Progress.Unlock(key);
        foreach (var key in new[] { "boss_M05", "boss_T04", "boss_H03", "tutorial_double", "tutorial_wall", "flood_done", "met_igi", "district_C04", "district_C05", "district_C06", "seven_gates", "shortcut_T06_T05" }) Progress.Mark(key);
        foreach (var idValue in rooms.Keys)
        {
            var id = idValue.AsString();
            if (id == "TEST") continue;
            foreach (var portValue in rooms.Dict(id).Array("ports"))
            {
                var port = portValue.AsGodotDictionary();
                await LoadRoom(id); Travel.ArrivalLock = 0;
                Room.GetNode<MudRoomPortal>(port.Str("id")).Interact(Player);
                await Frames(9); RefreshScene();
                var spawn = Room.GetNodeOrNull<Marker2D>("Spawns/" + port.Str("spawn"));
                Check(Room.RoomId == port.Str("target") && spawn != null && Mathf.Abs(Player.Position.X - spawn.Position.X) < 2f && Mathf.Abs(Player.Position.Y - spawn.Position.Y) < 15f, "实际传送与出生定位 " + port.Str("id"));
                await Frames(3); Check(GetTree().CurrentScene == Room, "抵达后不发生传送循环 " + port.Str("id"));
            }
        }
    }
    private void Event(string key) => Room.PerformEvent(key, Room.GetNode<MudRoomEvent>(key));
    private MudRoomPortal Portal(string key) => Room.GetNode<MudRoomPortal>(key);
    private async Task ProgressionChecks()
    {
        Progress.NewGame(false);
        await LoadRoom("T05"); Check(Portal("T05_to_T06").LockReason() == "另一侧封死", "首次T05无法提前进入最终区域");
        await LoadRoom("T06"); Travel.ArrivalLock = 0; Portal("T06_to_T05").Interact(Player);
        Check(Progress.Flag("shortcut_T06_T05"), "T06破门永久记录");
        await LoadRoom("T05"); Check(Portal("T05_to_T06").LockReason().Length == 0, "破门后捷径双向开放");
        await LoadRoom("C03"); Check(Portal("C03_to_C07").LockReason().Length > 0, "三街区未完成时档案室关闭");
        Progress.Mark("district_C04"); Progress.Mark("district_C05"); Progress.Mark("met_igi");
        Check(Portal("C03_to_C07").LockReason().Length > 0, "只完成两个街区仍关闭");
        Progress.Mark("district_C06"); Check(Portal("C03_to_C07").LockReason().Length == 0, "三街区完成后档案室开放");
        await LoadRoom("C06"); Progress.Flags.Remove("district_C06"); Event("sigil_3");
        Check(!Progress.Flag("district_C06"), "乱序征兆不完成街区");
        foreach (var key in new[] { "sigil_1", "sigil_2", "sigil_3" }) Event(key);
        Check(Progress.Flag("district_C06"), "顺序征兆完成街区");
        await LoadRoom("H01"); Event("gate_7"); Check(!Progress.Flag("seven_gates"), "不能跳过前六道门");
        for (var i = 1; i <= 7; i++) Event($"gate_{i}");
        Check(Progress.Flag("seven_gates") && Portal("H01_to_H02").LockReason().Length == 0, "七门依次完成后开放H02");
        Progress.NewGame(false); await LoadRoom("T01");
        foreach (var form in new[] { 0, 1 })
        {
            await ResetActor(new Vector2(Room.RoomSize.X - 540, Room.FloorY - 24), form);
            Progress.MudModule = 1;
            Input.ActionPress("jump"); Input.ActionPress("move_right"); await Frames(24);
            Player.SwitchForm(); Player.StartDodge(); await Frames(95); ReleaseInputs();
            Check(Player.Position.X < Room.RoomSize.X - 390 && Player.Position.Y > Room.FloorY - 180, $"未解锁二段跳时形态{form}无法绕过高台");
        }
        Progress.Unlock("double_jump");
        await ResetActor(new Vector2(Room.RoomSize.X - 540, Room.FloorY - 24));
        Input.ActionPress("move_right"); Input.ActionPress("jump"); await Frames(23);
        Input.ActionRelease("jump"); await Frames(2); Input.ActionPress("jump"); await Frames(45); ReleaseInputs();
        Check(Player.Position.X > Room.RoomSize.X - 390 && Player.Position.Y < Room.FloorY - 180 && Progress.Flag("tutorial_double"), "二段跳实际越过192px高台并完成教学");
        await LoadRoom("T02"); Progress.Unlock("wall_climb");
        await ResetActor(new Vector2(Room.RoomSize.X - 460, Room.FloorY - 24));
        Input.ActionPress("move_right"); Input.ActionPress("jump"); Input.ActionPress("move_up"); await Frames(190); ReleaseInputs();
        Check(Player.Position.Y < Room.FloorY - 400 && Progress.Flag("tutorial_wall"), "攀墙实际到达出口并完成教学");
        await LoadRoom("C04"); await ResetActor(new Vector2(Room.RoomSize.X * 0.6f, Room.FloorY - 280)); await Frames(55);
        Check(Player.Position.Y < Room.FloorY - 200, "未解锁潜游时水面门控生效");
        Progress.Unlock("dive"); Input.ActionPress("move_down"); await Frames(75); ReleaseInputs();
        Check(Player.Position.Y > Room.FloorY - 140, "解锁潜游后可到池底");
        Event("omen_1"); Check(Progress.Flag("district_C04"), "池底征兆完成第一街区");
        await LoadRoom("C05"); Progress.Unlock("heavy_slam");
        var obstacle = Room.TrialBreakable;
        Check(!obstacle.ReceiveHit(99, "ordinary", new GDictionary { ["heavy"] = false }), "普通攻击无法破坏下砸障碍");
        await ResetActor(new Vector2(obstacle.Position.X, obstacle.Position.Y - 180), 1);
        Player.Action.Enter(Kind.Slam); await Frames(40);
        Check(Progress.Flag("obstacle_C05") && !GodotObject.IsInstanceValid(obstacle), "重压下砸实际击破障碍并记录");
        Event("omen_2"); Check(Progress.Flag("district_C05"), "击破障碍后完成第二街区");
        await LoadRoom("M05"); Room.StartBoss();
        Check(Room.BossActive && Portal("M05_to_M04").LockReason() == "战斗封锁", "Boss开战封门");
        var boss = Room.Boss;
        await ResetActor(boss.Position + new Vector2(-75, 48), 1);
        Input.ActionPress("block"); await Frames(100);
        Check(boss.Phase == 3 && Player.Combat.BonusReady, "真实格挡守坑人攻击触发失衡及增伤");
        ReleaseInputs(); await Frames(2); Player.ReceiveAttack(99999, "death", boss.Position);
        Check(Player.Action.State == Kind.Dead, "死亡状态正确");
        Travel.Retry(); await Frames(10); RefreshScene();
        Check(Room.RoomId == "M05" && !Room.BossActive && !Progress.Flag("boss_M05"), "死亡重试重置Boss战斗");
        Room.StartBoss(); Room.Boss.ReceiveHit(9999, "finish_keeper", new()); await Frames(3);
        Check(Progress.Flag("boss_M05") && !Room.BossActive && Portal("M05_to_M06").LockReason().Length == 0, "Boss击败后开门并保存进度");
        await LoadRoom("H03"); Room.StartBoss(); Room.Boss.ReceiveHit(9999, "phase1", new());
        Check(Room.Boss.Stage == 2 && Room.BossActive && !Progress.Flag("boss_H03"), "最终Boss第一阶段结束后继续封门");
        Room.Boss.ReceiveHit(9999, "phase2", new()); await Frames(3);
        Check(Progress.Flag("boss_H03") && !Room.BossActive, "第二阶段击败后开放结局");
        Event("truth"); Event("ending_awake"); Check(Progress.Flag("truth") && Progress.Flag("ending_awake"), "真相及醒来结局可触发");
        var oldSnapshot = Progress.Snapshot();
        var serialized = Json.Stringify(oldSnapshot);
        Progress.Flags.Clear();
        Check(Progress.ApplySnapshot(Json.ParseString(serialized).AsGodotDictionary()) && Progress.Flag("boss_M05") && Progress.Flag("boss_H03"), "兼容原版version=1存档字段及JSON往返");
        if (OS.GetEnvironment("MUDCHILD_TEST_ISOLATED") == "1")
        {
            Progress.AllowDisk = true; Check(Progress.SaveGame(), "隔离环境下实际磁盘保存成功");
            Progress.Flags.Clear(); Check(Progress.LoadGame() && Progress.Flag("boss_M05") && Progress.Flag("boss_H03"), "实际磁盘读回Boss进度");
            Progress.AllowDisk = false;
        }
        else GD.Print("Tests: SKIP 磁盘检查仅由Run_Tests.ps1隔离运行。");
        Progress.EnterTest(); Progress.Unlock("high_jump_upgrade"); Progress.NewGame(false);
        Check(!Progress.TestMode && !Progress.HasAbility("high_jump_upgrade") && !Progress.HasAbility("dive"), "重开正式流程不残留测试能力");
    }
    private async Task ClimbChecks()
    {
        Progress.NewGame(false); Progress.Unlock("double_jump");
        foreach (var id in new[] { "T05", "H01" })
        {
            await LoadRoom(id);
            var stage = 0;
            var steps = (int)((Room.FloorY - 96 - 320) / 96) + 1;
            var jumping = false; var airClock = 0;
            for (var tick = 0; tick < 2400; tick++)
            {
                var wave = stage % 10;
                var fraction = (wave <= 5 ? wave : 10 - wave) / 5f;
                var target = new Vector2(Mathf.Lerp(300, Room.RoomSize.X - 300, fraction), Room.FloorY - 96 - stage * 96);
                Input.ActionRelease("move_left"); Input.ActionRelease("move_right");
                if (Mathf.Abs(Player.Position.X - target.X) > 12) Input.ActionPress(Player.Position.X < target.X ? "move_right" : "move_left");
                if (Player.IsOnFloor())
                {
                    if (Player.Position.Y + 24 <= target.Y + 4 && ++stage >= steps) break;
                    if (!jumping) { Input.ActionPress("jump"); jumping = true; airClock = 0; }
                }
                else if (++airClock > 4) { Input.ActionRelease("jump"); jumping = false; }
                await Frames(1);
            }
            ReleaseInputs(); Check(stage >= steps - 1, $"{id}实际输入攀登阶梯 {stage}/{steps}");
            var portalX = Room.RoomSize.X - 80;
            var held = false;
            for (var tick = 0; tick < 800; tick++)
            {
                Input.ActionRelease("move_left"); Input.ActionRelease("move_right");
                if (Mathf.Abs(Player.Position.X - portalX) > 25) Input.ActionPress(Player.Position.X < portalX ? "move_right" : "move_left");
                if (Player.IsOnFloor() && Player.Action.State == Kind.Free && !held) { Input.ActionPress("jump"); held = true; }
                else if (!Player.IsOnFloor()) { Input.ActionRelease("jump"); held = false; }
                if (Mathf.Abs(Player.Position.X - portalX) < 70 && Mathf.Abs(Player.Position.Y - 320) < 80) break;
                await Frames(1);
            }
            ReleaseInputs();
            Check(Mathf.Abs(Player.Position.X - portalX) < 70 && Mathf.Abs(Player.Position.Y - 320) < 80, id + "顶部出口实际可达");
        }
    }
    private async Task FinalPatchChecks()
    {
        await LoadRoom("M03"); await Frames(100);
        var passive = true;
        foreach (var node in GetTree().GetNodesInGroup("damageables"))
            if (node is MudWhiteboxEnemy enemy) passive &= enemy.Passive && enemy.Phase == 0;
        Check(passive, "朝圣者保持被动");
        await LoadRoom("C01"); Check(GetTree().GetNodesInGroup("damageables").Count == 1, "城门复用普通守卫");
        await LoadRoom("TEST"); await ResetActor(new Vector2(600, Room.FloorY - 24));
        var start = Player.Position.X;
        var keyEvent = new InputEventKey { PhysicalKeycode = Key.A, Pressed = true };
        Input.ParseInputEvent(keyEvent); await Frames(10); keyEvent.Pressed = false; Input.ParseInputEvent(keyEvent);
        Check(Player.Position.X < start - 30, "原物理键A绑定实际驱动移动");
        Progress.LeaveTest(); GetTree().ChangeSceneToFile("res://assets/scenes/ui/main_menu.tscn"); await Frames(8);
        Check(GetTree().CurrentScene is MudMainMenu, "主入口菜单实际运行");
    }
    private async Task AttackChecks()
    {
        Progress.NewGame(false); await LoadRoom("TEST");
        var target = (MudWhiteboxEnemy)GetTree().GetFirstNodeInGroup("damageables");
        var samples = new GArray();
        foreach (var form in new[] { 0, 1 })
        {
            await ResetActor(new Vector2(1855, Room.FloorY - 24), form); Player.Facing = 1;
            var before = target.Health;
            var counts = new GDictionary { ["windup"] = 0, ["active"] = 0, ["recovery"] = 0 };
            Input.ActionPress("attack");
            for (var i = 0; i < 125; i++)
            {
                await Frames(1); if (i == 2) Input.ActionRelease("attack");
                var key = Player.Action.State switch { Kind.AttackWindup => "windup", Kind.AttackActive => "active", Kind.AttackRecovery => "recovery", _ => "" };
                if (key.Length > 0) counts[key] = counts.Int(key) + 1;
            }
            counts["damage"] = before - target.Health; samples.Add(counts);
        }
        var water = samples[0].AsGodotDictionary(); var mud = samples[1].AsGodotDictionary();
        Check(Mathf.IsEqualApprox(water.Num("damage"), 10f) && Mathf.IsEqualApprox(mud.Num("damage"), 24f), "真实攻击伤害：水10、泥24，无多帧重复伤害");
        Check(mud.Int("windup") > water.Int("windup") && mud.Int("recovery") > water.Int("recovery") * 2 && water.Int("active") > 0, "实际攻击三阶段时长关系保留");
        foreach (var module in new[] { 0, 1 })
        {
            await ResetActor(new Vector2(1940, Room.FloorY - 150), 1); Progress.MudModule = module;
            Player.Velocity = new Vector2(Player.Velocity.X, 200);
            var before = target.Health; Player.Combat.Start(Player.MudConfig, true); await Frames(125);
            Check(Mathf.Abs(before - target.Health - (module == 0 ? 43.2f : 24f)) < 0.1f, $"泥模组{module + 1}向下攻击实际伤害正确");
        }
        Metrics["attack_samples"] = samples;
    }
    private async Task JumpLandingChecks()
    {
        Progress.NewGame(false); Progress.EnterTest(); Progress.Unlock("double_jump"); await LoadRoom("TEST");
        foreach (var variant in new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(1, 1) })
        {
            var prefix = $"形态{variant.X}/模组{variant.Y + 1} ";
            await ResetActor(new Vector2(400, Room.FloorY - 24), variant.X); Progress.MudModule = variant.Y;
            Player.Position = new Vector2(400, Room.FloorY - 50); Player.Velocity = new Vector2(0, 600); await Frames(2);
            Player.Position = new Vector2(Player.Position.X, Room.FloorY - 26);
            Check(!Player.IsOnFloor() && Player.RequestJump(), prefix + "下落贴地前接受跳跃请求");
            var frozen = Player.PendingJumpProfile;
            var crossed = false; var survived = true; var launched = false; var valid = false;
            var launchCount = -1; var bursts = Player.Combat.LandedBursts; var noFallBurst = true;
            Input.ActionPress("jump");
            for (var tick = 0; tick < 125; tick++)
            {
                await Frames(1);
                if (Player.IsOnFloor() && Player.Action.State == Kind.JumpWindup)
                {
                    crossed = true; survived &= Player.PendingJumpProfile == frozen && Player.JumpProfile == null;
                    noFallBurst &= Player.Combat.LandedBursts == bursts;
                }
                if (Player.JumpInProgress && !launched)
                { launched = true; valid = Player.JumpProfile == frozen && Player.PendingJumpProfile == null; launchCount = Player.JumpsUsed; }
                if (launched && Player.IsOnFloor()) break;
            }
            ReleaseInputs();
            Check(crossed && survived, prefix + "前摇中落地保留待起跳配置");
            Check(launched && valid && launchCount == 1, prefix + "按冻结配置起跳，仅消耗一次跳跃");
            Check(noFallBurst, prefix + "前摇中落地不误触范围攻击");
            Check(Player.Combat.LandedBursts - bursts == (variant.X == 1 && variant.Y == 1 ? 1 : 0), prefix + "下一跳落地资格独立且不重复");
        }
        await ResetActor(new Vector2(400, Room.FloorY - 24), 1);
        Player.Action.Enter(Kind.JumpWindup); Player.LaunchJump();
        Check(Player.Action.State == Kind.Free && Player.JumpsUsed == 0, "空待起跳配置安全取消");
        Check(Player.RequestJump(), "受伤测试接受起跳前摇"); Player.ReceiveAttack(1, "pending_jump_hurt", Player.Position);
        Check(Player.Action.State == Kind.Hurt && Player.PendingJumpProfile == null, "受伤立即取消待起跳");
        await Frames(35); Check(Player.IsOnFloor() && !Player.JumpInProgress && Player.JumpsUsed == 0, "受伤后没有幽灵起跳");
        Check(Player.RequestJump(), "换场测试接受起跳前摇"); Travel.EnterRoom("M01", "Start", true); await Frames(25); RefreshScene();
        Check(Room.RoomId == "M01" && Player.IsOnFloor() && Player.PendingJumpProfile == null && !Player.JumpInProgress && Player.JumpsUsed == 0, "真实换场清除待起跳与落地伤害资格");
    }
}
