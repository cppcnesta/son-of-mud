using Godot;
using System.Threading.Tasks;
using Kind = MudActionState.Kind;
using Source = MudMovementRecovery.Source;

public sealed class MudCombatFeelChecks
{
    private MudRuntimeSuite _driver;
    private async Task WaitForRecovery(MudPlayer player)
    {
        for (var i = 0; i < 45 && player.Action.State != Kind.AttackRecovery; i++) await _driver.Frames(1);
    }
    public async Task Run(MudRuntimeSuite driver)
    {
        _driver = driver;
        var progress = MudProgress.Instance;
        progress.NewGame(false); progress.EnterTest(); await driver.LoadRoom("TEST");
        var player = driver.Player;
        var floor = driver.Room.FloorY;
        foreach (var duration in new[] { 1f, 1.5f })
        {
            var recovery = new MudMovementRecovery();
            recovery.Start(duration == 1f ? Source.Dodge : Source.Attack, duration, 0.2f, player.MudConfig.RecoveryCurve);
            driver.Check(Mathf.Abs(recovery.Multiplier() - 0.2f) < 0.001f, $"{duration:F1}秒后摇从80%减速开始");
            var expected = new[] { 0.55f, 0.8f, 0.95f, 1f };
            var correct = true;
            foreach (var sample in expected) { recovery.Tick(duration * 0.25f); correct &= Mathf.Abs(recovery.Multiplier() - sample) < 0.005f; }
            driver.Check(correct, $"{duration:F1}秒实际曲线20%→55%→80%→95%→100%");
        }
        await driver.ResetActor(new Vector2(400, floor - 24), 1);
        driver.Check(player.Combat.Start(player.Config(), false), "泥普通攻击进入前摇");
        await WaitForRecovery(player);
        driver.Check(player.Action.State == Kind.AttackRecovery && Mathf.Abs(player.Recovery.Remaining - 1.5f) < 0.04f && player.Recovery.RecoverySource == Source.Attack, "有效判定后进入1.5秒曲线减速后摇");
        var startX = player.Position.X; Input.ActionPress("move_right"); await driver.Frames(15); Input.ActionRelease("move_right");
        driver.Check(player.Position.X > startX + 8 && player.Position.X < startX + 70, "攻击后摇期间实际可移动且受减速");
        driver.Check(!player.Combat.Start(player.Config(), false), "攻击后摇不可连点重开攻击");
        driver.Check(!player.StartBlock() && player.Guard.CooldownRemaining == 0f, "攻击后摇前1秒拒绝格挡且不消耗CD");
        for (var i = 0; i < 80 && player.Combat.RecoveryRemaining > 0.45f; i++) await driver.Frames(1);
        Input.ActionPress("block");
        driver.Check(player.StartBlock() && player.Action.State == Kind.Block, "泥攻击后摇最后0.5秒允许格挡");
        var previousInterval = player.Combat.AttackIntervalRemaining; var previousCd = player.Guard.CooldownRemaining;
        driver.Check(player.ReceiveAttack(1, "feel_mud_parry", player.Position) == "blocked" && player.Combat.BonusReady && player.Guard.CooldownRemaining == previousCd && player.Combat.AttackIntervalRemaining == previousInterval, "泥格挡保留增伤但不刷新水形态专属CD/攻击间隔");
        await driver.Frames(3); Input.ActionRelease("block"); await driver.Frames(3);
        driver.Check(player.Action.State == Kind.Free && !player.Combat.Start(player.Config(), false), "提前松开泥格挡不能绕过剩余攻击间隔");
        await driver.Frames(40); driver.Check(player.Recovery.Remaining == 0f && player.Combat.IsReady(), "攻击收势到期后可再次攻击");
        foreach (var ticks in new[] { 0, 35, 65 })
        {
            await driver.ResetActor(new Vector2(400, floor - 24), 1);
            player.Combat.Start(player.Config(), false); await WaitForRecovery(player); await driver.Frames(ticks);
            driver.Check(player.StartDodge(), $"泥攻击后摇第{ticks}帧可Shift衔接");
            driver.Check(player.Combat.RecoveryRemaining == 0f && player.Combat.AttackIntervalRemaining == 0f && player.Recovery.RecoverySource != Source.Attack, "Shift清除攻击收势及隐藏计时");
            for (var i = 0; i < 40 && player.Action.State == Kind.Dodge; i++) await driver.Frames(1);
            driver.Check(player.Recovery.RecoverySource == Source.Dodge && Mathf.Abs(player.Recovery.Remaining - 1f) < 0.04f && Mathf.Abs(player.PenaltyMultiplier() - 0.2f) < 0.03f, "Shift结束后1秒闪避曲线覆盖攻击减速");
            await driver.Frames(30); driver.Check(Mathf.Abs(player.PenaltyMultiplier() - 0.8f) < 0.04f, "真实物理帧：闪避后0.5秒约80%移速");
            await driver.Frames(32); driver.Check(player.Recovery.Remaining == 0f && player.Combat.IsReady() && player.Action.State == Kind.Free, "闪避收势结束后无残留攻击计时");
        }
        await driver.ResetActor(new Vector2(400, floor - 24)); Input.ActionPress("block"); await driver.Frames(2);
        driver.Check(player.Action.State == Kind.Block && player.Guard.CooldownRemaining > 2.9f && player.Guard.CooldownRemaining <= 3f, "水格挡按下时启动3秒CD");
        Input.ActionRelease("block"); await driver.Frames(3); driver.Check(!player.StartBlock(), "水未格挡成功时不能提前再挡");
        var cd = player.Guard.CooldownRemaining;
        driver.Check(player.SwitchForm() && player.Guard.CooldownRemaining == cd && !player.StartBlock(), "形态切换不清除格挡CD");
        await driver.Frames(185); driver.Check(player.Guard.CooldownRemaining == 0f, "格挡CD自然到期");
        await driver.ResetActor(new Vector2(400, floor - 24)); player.Combat.AttackIntervalRemaining = 1f;
        Input.ActionPress("block"); await driver.Frames(2);
        driver.Check(player.ReceiveAttack(1, "feel_water_parry", player.Position) == "blocked", "水成功处理格挡");
        driver.Check(player.Guard.CooldownRemaining == 0f && player.Combat.AttackIntervalRemaining == 0f && player.Action.State == Kind.Free, "水成功格挡清零格挡CD及攻击间隔");
        await driver.Frames(5); driver.Check(player.Action.State == Kind.Free, "水成功格挡后按住不自动续挡");
        Input.ActionRelease("block"); driver.Check(player.Combat.Start(player.Config(), false), "水格挡成功可立即反击");
        await driver.ResetActor(new Vector2(400, floor - 160), 1); player.Action.Enter(Kind.Slam); await driver.Frames(20);
        driver.Check(player.Action.State == Kind.AttackRecovery && player.Combat.CanMoveInRecovery() && player.Recovery.RecoverySource == Source.Attack && player.Recovery.Duration == 1.5f, "泥下砸落地使用1.5秒可移动攻击收势");
        driver.Check(player.StartDodge() && player.Combat.RecoveryRemaining == 0f, "泥下砸后摇也可被Shift覆盖");
        for (var i = 0; i < 40 && player.Action.State == Kind.Dodge; i++) await driver.Frames(1);
        driver.Check(player.Recovery.RecoverySource == Source.Dodge && player.Recovery.Duration == 1f, "泥下砸衔接Shift后仅保留1秒闪避收势");
        await driver.ResetActor(new Vector2(400, floor - 24), 1);
        player.Recovery.Start(Source.Dodge, 1f, 0.2f, player.MudConfig.RecoveryCurve); player.Recovery.Remaining = 0.75f;
        player.Guard.CooldownRemaining = 2f; player.Combat.AttackIntervalRemaining = 0.6f;
        MudRoomTravel.Instance.EnterRoom("M01", "Start", true); await driver.Frames(10); driver.RefreshScene(); player = driver.Player;
        driver.Check(driver.Room.RoomId == "M01" && player.Guard.CooldownRemaining > 1.7f && player.Guard.CooldownRemaining < 2f && player.Combat.AttackIntervalRemaining > 0.3f, "真实换场保留格挡CD及攻击间隔");
        driver.Check(player.Recovery.RecoverySource == Source.Dodge && player.Recovery.Remaining > 0.45f && player.Recovery.Remaining < 0.75f && player.Recovery.Duration == 1f, "真实换场保留曲线来源、剩余时间及总时长");
        var world = Json.ParseString(FileAccess.GetFileAsString("res://assets/data/whitebox/world.json")).AsGodotDictionary();
        var scenesExist = true; var cleanCopy = true;
        foreach (var idValue in world.Dict("rooms").Keys)
        {
            var id = idValue.AsString(); if (id == "TEST") continue;
            scenesExist &= ResourceLoader.Exists($"res://assets/scenes/whitebox/levels/{id}.tscn", "PackedScene");
            foreach (var entryValue in world.Dict("rooms").Dict(id).Array("events"))
            {
                var entry = entryValue.AsGodotDictionary();
                foreach (var forbidden in new[] { "原型", "占位", "暂定", "接口", "PDF", "CG1", "CG3", "CG5", "尚未制作" })
                    cleanCopy &= !(entry.Str("title") + entry.Str("text")).Contains(forbidden);
            }
        }
        driver.Check(scenesExist && !DirAccess.DirExistsAbsolute("res://assets/scenes/mainline") && !DirAccess.DirExistsAbsolute("res://assets/scenes/testing"), "23关卡及测试场使用当前whitebox目录");
        driver.Check(cleanCopy && world.Dict("rooms").Dict("H01").Array("events").Count == 7, "文案及七门事件完整保留");
        driver.Check(DirAccess.DirExistsAbsolute("res://assets/art/characters/player") && DirAccess.DirExistsAbsolute("res://assets/art/environment/tilesets") && DirAccess.DirExistsAbsolute("res://assets/art/audio/sfx"), "正式美术、环境及音频目录保留");
        await driver.SnapshotImage("runtime_csharp_combat_feel");
    }
}
