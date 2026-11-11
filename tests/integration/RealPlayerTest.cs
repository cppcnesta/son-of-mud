using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Player.States;
using SonOfMud.Tools;
using SonOfMud.World;

/// <summary>
/// 真实玩家 + 真实 World / Save 的集成验收（审查 M5、M6）。不用 TestMover，也不手动发 RoomExiting / PlayerDied。
/// ① 竖切片：真玩家按住右键从 S1_03 走进 S1_04。
/// ② 实际跨边界攻击：在 S2_02 地洞上方下劈，判定帧跨过房间边界落进 S2_03；先做同房间的对照组证明假人在攻击范围内。
/// ③ 实际致死复活：用 HurtBox.Receive 真实扣血到 0 → 结果是 Killed、扣血非负；复活在到达检查点之后才发；
///    传送失败不报告复活；过门途中死亡，复活排队到过门结束后执行。
/// ④ 过门：真玩家过 S3_08 → S4_01，锁输入期间按键无效，到达后完整清理，解锁后回 Idle。
/// </summary>
public partial class RealPlayerTest : WorldTestBase
{
    protected override string SuiteName => "real_player";

    private const string PlayerScene = "res://assets/scenes/player/player_v2.tscn";
    private const string CombatLab = "res://assets/scenes/tests/combat_lab.tscn";

    private PlayerCharacter _player;

    protected override async Task RunChecks()
    {
        await SetUpRealGame();
        if (_player == null) return;
        await SliceChecks();
        await EdgeAttackChecks();
        await LethalRespawnChecks();
        await RespawnFailureChecks();
        await DeathDuringDoorChecks();
        await DoorChecks();
        ReleaseAll();
    }

    // ---------- 搭建 ----------

    private async Task SetUpRealGame()
    {
        GameSession.Reset();
        GameSession.Progress.Unlock("block");
        GameSession.Progress.Unlock("switch_form");
        MetSysApi.ResetState();
        MetSysApi.SetSaveData();
        Game = GD.Load<PackedScene>(GameScene).Instantiate();
        AddChild(Game);
        World = Game.GetNode<WorldGame>("WorldGame");
        Saves = Game.GetNode<SonOfMud.Save.SaveService>("SaveService");
        Cam = Game.GetNode<SonOfMud.Camera.RoomCamera>("RoomCamera");
        _player = GD.Load<PackedScene>(PlayerScene).Instantiate<PlayerCharacter>();
        _player.Name = "Player";
        Game.AddChild(_player);
        await Frames(2);
        Check(World.Player == _player && Cam.Target == _player, "搭建：真实玩家 player_v2 通过 PlayerSpawned 接入 WorldGame 和镜头（没有 TestMover）");
        Check(FindChild<TestMover>(Game) == null, "搭建：场景里没有 TestMover");
    }

    private Vector2 MapPixelOf(Node2D node)
    {
        Vector2 cell = MetSysApi.CellSize;
        Vector2I origin = World.CurrentRoomDef.Origin;
        return new Vector2(origin.X * cell.X, origin.Y * cell.Y) + node.GlobalPosition;
    }

    private static void ReleaseAll()
    {
        foreach (StringName action in InputMap.GetActions()) Input.ActionRelease(action);
    }

    private async Task Tap(StringName action)
    {
        Input.ActionPress(action);
        await Frames(1);
        Input.ActionRelease(action);
        await Frames(1);
    }

    private static Hit Lethal(string id, float damage, Vector2 source) => new()
    {
        AttackId = id, Damage = damage, SourcePosition = source, Blockable = false,
    };

    /// <summary>从战斗测试场里借一个训练假人（两个 HurtBox 的 DummyB 不用，用单 HurtBox 的 DummyA）。</summary>
    private TrainingDummy BorrowDummy()
    {
        Node lab = GD.Load<PackedScene>(CombatLab).Instantiate();
        var dummy = lab.GetNode<TrainingDummy>("DummyA");
        lab.RemoveChild(dummy);
        lab.Free();
        dummy.Name = "IntegrationDummy";
        Game.AddChild(dummy);
        return dummy;
    }

    /// <summary>回到可操作的干净状态：满血、Idle、水形态、冷却清空。</summary>
    private async Task ResetPlayer(string room, StringName spawn)
    {
        ReleaseAll();
        await WaitUntil(() => !World.IsTransitioning, 240);
        World.LoadRoomImmediate(room, spawn);
        // 测试场复位：满血、Idle、水形态、冷却和缓冲清空，位置保持在刚放好的出生点
        _player.ResetForLab(_player.GlobalPosition, 0);
        await Frames(20);
    }

    // ---------- ① 竖切片 ----------

    private async Task SliceChecks()
    {
        await ResetPlayer("S1_03", "From_S1_04");
        Check(_player.IsOnFloor(), $"竖切片搭建：真实玩家站在 S1_03 右侧出口平台上（{_player.GlobalPosition}）");

        var log = new List<string>();
        int teleports = 0;
        float speedBeforeExit = 0f, speedAfterEnter = 0f;
        string stateAtEnter = "";
        void OnExit(string id) { log.Add("exit:" + id); speedBeforeExit = _player.Velocity.X; }
        void OnEnter(string id) { log.Add("enter:" + id); speedAfterEnter = _player.Velocity.X; stateAtEnter = _player.State?.GetType().Name; }
        void OnTele(StringName s) => teleports++;
        Events.Instance.RoomExiting += OnExit;
        Events.Instance.RoomEntered += OnEnter;
        Events.Instance.PlayerTeleported += OnTele;

        float maxStep = 0f;
        Vector2 last = MapPixelOf(_player);
        Input.ActionPress(PlayerActions.Right);
        for (int i = 0; i < 300 && !log.Contains("enter:S1_04"); i++)
        {
            await Frames(1);
            Vector2 now = MapPixelOf(_player);
            maxStep = Mathf.Max(maxStep, now.DistanceTo(last));
            last = now;
        }
        for (int i = 0; i < 15; i++)
        {
            await Frames(1);
            Vector2 now = MapPixelOf(_player);
            maxStep = Mathf.Max(maxStep, now.DistanceTo(last));
            last = now;
        }
        Input.ActionRelease(PlayerActions.Right);
        Events.Instance.RoomExiting -= OnExit;
        Events.Instance.RoomEntered -= OnEnter;
        Events.Instance.PlayerTeleported -= OnTele;

        Check(string.Join(",", log) == "exit:S1_03,enter:S1_04", $"竖切片：真实玩家按住右键走过边缘，先 RoomExiting(S1_03) 再 RoomEntered(S1_04)（{string.Join(",", log)}）");
        Check(teleports == 0, "竖切片：走边缘换房不发 PlayerTeleported");
        Check(speedBeforeExit > 0f && Mathf.IsEqualApprox(speedBeforeExit, speedAfterEnter) && stateAtEnter == nameof(RunState),
            $"竖切片：换房前后速度不变、仍在 Run（{speedBeforeExit:0.0} → {speedAfterEnter:0.0}，{stateAtEnter}）");
        Check(maxStep < 12f, $"竖切片：地图坐标连续（单帧最大位移 {maxStep:0.00}）");
        Check(Cam.LimitRight == 1152 && Cam.LimitBottom == 648, $"竖切片：镜头边界换成 S1_04 的 1152×648（{Cam.LimitRight}×{Cam.LimitBottom}）");
    }

    // ---------- ② 实际跨边界攻击 ----------

    private float HoleX()
    {
        // S2_02↓S2_03 的地洞在全局第 25 列（map_check 里核对过）
        RoomDef def = World.CurrentRoomDef;
        return (25 - def.Origin.X + 0.5f) * MetSysApi.CellSize.X;
    }

    private async Task EdgeAttackChecks()
    {
        TrainingDummy dummy = BorrowDummy();
        Vector2 offset = new(0f, 56f);

        // 对照组：同一个房间里，在空中下劈，判定打开时假人正好在 offset 处 → 必须掉血。
        await ResetPlayer("S2_02", "From_S2_01");
        _player.GlobalPosition = new Vector2(HoleX() - 400f, 300f);
        dummy.GlobalPosition = new Vector2(-5000f, -5000f);
        await Frames(10);
        int hpControl = dummy.Stats.Health;
        Input.ActionPress(PlayerActions.Down);
        Input.ActionPress(PlayerActions.Attack);
        int windupFrames = 0, activeFrames = 0;
        bool placed = false;
        for (int i = 0; i < 60; i++)
        {
            await Frames(1);
            if (i == 0) Input.ActionRelease(PlayerActions.Attack);
            if (_player.AttackHitBox.Active)
            {
                if (!placed) { placed = true; dummy.GlobalPosition = _player.GlobalPosition + offset; }
                activeFrames++;
            }
            else if (!placed) windupFrames++;
            else break;
        }
        await WaitUntil(() => _player.State is not AttackState, 60);
        Input.ActionRelease(PlayerActions.Down);
        Check(placed && activeFrames > 0, $"跨边界对照组：实测按键到判定打开 {windupFrames} 帧，判定持续 {activeFrames} 帧");
        Check(placed && dummy.Stats.Health < hpControl, $"跨边界对照组：同房间下劈，假人放在玩家下方 {offset.Y} 像素处会掉血（{hpControl} → {dummy.Stats.Health}）");

        // 实验组：从地洞上方下落，下劈的判定帧跨过 S2_02 → S2_03 的边界。
        await ResetPlayer("S2_02", "From_S2_01");
        dummy.Stats.Refill();
        dummy.GlobalPosition = new Vector2(-5000f, -5000f);
        float boundaryY = World.CurrentRoomDef.Size.Y * MetSysApi.CellSize.Y;
        _player.GlobalPosition = new Vector2(HoleX(), boundaryY - 260f);
        await Frames(1);

        // Player 比测试先订阅 RoomExiting，测试的处理器读到时判定已被 Player 关掉；
        // 所以用“离开房间前那一帧结束时”的判定状态和已开帧数来证明判定帧跨过了边界。
        bool activeAtExit = false, judgementClosed = false, reopened = false, attackAtEnter = false;
        bool activeLastFrame = false;
        int activeFramesBeforeExit = 0, framesActiveSoFar = 0;
        string enteredRoom = "";
        float cooldownBefore = 0f, cooldownAfter = 0f;
        void OnExit(string id)
        {
            activeAtExit = activeLastFrame;
            activeFramesBeforeExit = framesActiveSoFar;
            cooldownBefore = _player.Timers.DodgeCooldown;
        }
        void OnEnter(string id)
        {
            enteredRoom = id;
            attackAtEnter = _player.State is AttackState;
            judgementClosed = _player.State is AttackState { JudgementClosed: true } && !_player.AttackHitBox.Active;
            cooldownAfter = _player.Timers.DodgeCooldown;
            dummy.GlobalPosition = _player.GlobalPosition + offset;
        }
        Events.Instance.RoomExiting += OnExit;
        Events.Instance.RoomEntered += OnEnter;
        _player.Timers.DodgeCooldown = 2f;
        int hpBefore = dummy.Stats.Health;

        bool triggered = false;
        for (int i = 0; i < 240 && enteredRoom == ""; i++)
        {
            // 按实测的前摇 / 判定帧数和当前下落速度，算出让判定帧中段正好落在边界上的出刀时机
            float perFrame = Mathf.Max(_player.Velocity.Y, 1f) / 60f;
            float lead = perFrame * (windupFrames + activeFrames * 0.5f);
            if (!triggered && boundaryY - _player.GlobalPosition.Y <= lead)
            {
                triggered = true;
                Input.ActionPress(PlayerActions.Down);
                Input.ActionPress(PlayerActions.Attack);
                await Frames(1);
                Input.ActionRelease(PlayerActions.Attack);
                continue;
            }
            await Frames(1);
            if (enteredRoom == "")
            {
                activeLastFrame = _player.AttackHitBox.Active;
                if (activeLastFrame) framesActiveSoFar++;
            }
        }
        for (int i = 0; i < 40 && _player.State is AttackState; i++)
        {
            reopened |= _player.AttackHitBox.Active;
            await Frames(1);
        }
        Input.ActionRelease(PlayerActions.Down);
        Events.Instance.RoomExiting -= OnExit;
        Events.Instance.RoomEntered -= OnEnter;

        Check(triggered && enteredRoom == "S2_03", $"跨边界：下落中出刀，真实穿过 S2_02 → S2_03 的边界（进入 {enteredRoom}）");
        Check(activeAtExit && activeFramesBeforeExit > 0 && activeFramesBeforeExit < activeFrames,
            $"跨边界前提：离开 S2_02 前一帧判定正开着，已开 {activeFramesBeforeExit} 帧、总共 {activeFrames} 帧，越过边界时还有剩余有效帧");
        Check(attackAtEnter && judgementClosed, "跨边界：进入新房间时仍在攻击状态，但判定已关闭并标记为判定结束");
        Check(!reopened && dummy.Stats.Health == hpBefore, $"跨边界：剩余有效帧没有重新打开，新房间里同样位置的假人不掉血（{hpBefore} → {dummy.Stats.Health}）");
        Check(_player.State is not AttackState && Mathf.IsEqualApprox(cooldownBefore, cooldownAfter), "跨边界：攻击照常按计时收招，闪避冷却在换房时保留");
        dummy.QueueFree();
    }

    // ---------- ③ 实际致死复活 ----------

    private async Task LethalRespawnChecks()
    {
        await ResetPlayer("S1_04", "Save");
        Check(Saves.SaveToDisk("altar_s1_04"), "致死搭建：在 S1_04 存档");
        GameSession.Progress.SetVar("after_save_flag", true);
        await ResetPlayer("S1_03", "From_S1_04");

        var log = new List<string>();
        int died = 0, respawned = 0;
        int healthAtArrival = -1, healthAtRespawnSignal = -1;
        string roomAtRespawn = "";
        var healthValues = new List<int>();
        void OnDied() { died++; log.Add("died"); }
        void OnTravel(int id, string room, StringName spawn) => log.Add("travel:" + room);
        void OnEnter(string room) { log.Add("enter:" + room); healthAtArrival = _player.Stats.Health; }
        void OnRespawn() { respawned++; log.Add("respawn"); roomAtRespawn = World.CurrentRoomDef?.Id; healthAtRespawnSignal = _player.Stats.Health; }
        void OnHealth(int oldValue, int newValue) => healthValues.Add(newValue);
        Events.Instance.PlayerDied += OnDied;
        Events.Instance.TravelRequested += OnTravel;
        Events.Instance.RoomEntered += OnEnter;
        Events.Instance.CheckpointRespawned += OnRespawn;
        _player.Stats.HealthChanged += OnHealth;

        _player.HurtBox.Receive(Lethal("bring_to_10", 130f, _player.GlobalPosition + new Vector2(40, 0)));
        Check(_player.Stats.Health == 10, $"致死搭建：先打到剩 10 血（{_player.Stats.Health}）");

        Hit killing = Lethal("killing_blow", 20f, _player.GlobalPosition + new Vector2(40, 0));
        HitOutcome outcome = _player.HurtBox.Receive(killing);
        bool syncDied = died > 0, syncRespawn = respawned > 0;
        int healthRightAfter = _player.Stats.Health;

        Check(outcome == HitOutcome.Killed, $"致死：HurtBox.Receive 返回 Killed（实际 {outcome}）");
        Check(killing.Dealt == 10, $"致死：实际扣血 10，非负（实际 {killing.Dealt}）");
        bool syncTravel = log.Count > 0;
        Check(healthRightAfter == 0 && !syncDied && !syncRespawn && !syncTravel, "致死：Receive 返回时血量是 0，PlayerDied、换房请求、复活都还没发生（没有重入）");

        bool done = await WaitUntil(() => respawned > 0 && !World.IsTransitioning, 300);
        await Frames(2);
        Events.Instance.PlayerDied -= OnDied;
        Events.Instance.TravelRequested -= OnTravel;
        Events.Instance.RoomEntered -= OnEnter;
        Events.Instance.CheckpointRespawned -= OnRespawn;
        _player.Stats.HealthChanged -= OnHealth;

        Check(done && died == 1 && respawned == 1, $"致死：PlayerDied 和 CheckpointRespawned 各一次（{died}/{respawned}）");
        var ordered = log.Where(l => l != "died").ToList();
        Check(log.Contains("died") && string.Join(",", ordered) == "travel:S1_04,enter:S1_04,respawn",
            $"致死：死亡之后依次是 换房请求 → 到达检查点 → 复活通知（{string.Join(",", log)}）");
        Check(roomAtRespawn == "S1_04" && healthAtArrival == 0,
            $"致死：到达检查点那一刻血量仍是 0，复活通知发出时人已在 S1_04（到达时血量 {healthAtArrival}）");
        Check(_player.Stats.Health == 140 && _player.State is IdleState, $"致死：复活后回满 140 血、回 Idle（{_player.Stats.Health}，{_player.State?.GetType().Name}）");
        Check(healthValues.All(v => v >= 0) && healthValues.IndexOf(140) == healthValues.Count - 1,
            $"致死：血量变化过程没有负数，回满只发生在最后（{string.Join(",", healthValues)}）");
        Check(GameSession.Progress.Flag("after_save_flag"), "致死：存档之后写的进度变量复活后还在（D4）");
    }

    private async Task RespawnFailureChecks()
    {
        await ResetPlayer("S1_04", "Save");
        string oldNewGameRoom = Saves.NewGameRoom;
        Saves.NewGameRoom = "NO_SUCH_ROOM";
        Saves.StartNewGame();
        await Frames(5);
        Saves.NewGameRoom = oldNewGameRoom;

        int respawned = 0;
        void OnRespawn() => respawned++;
        Events.Instance.CheckpointRespawned += OnRespawn;
        _player.HurtBox.Receive(Lethal("fail_kill", 999f, _player.GlobalPosition));
        await Frames(30);
        Events.Instance.CheckpointRespawned -= OnRespawn;

        Check(respawned == 0 && _player.State is DeadState, "复活失败：检查点房间不存在时不发 CheckpointRespawned，玩家仍是死亡状态");
        Check(Saves.LastError.Contains("复活失败") && Saves.PendingRespawnId == -1, $"复活失败：记录错误并清掉挂起的请求（{Saves.LastError}）");

        // 恢复：重新在 S1_04 存档，作为后面测试的检查点
        await ResetPlayer("S1_04", "Save");
        Saves.SaveToDisk("altar_s1_04");
    }

    private async Task DeathDuringDoorChecks()
    {
        await ResetPlayer("S3_08", "From_S3_07");
        RoomDoor door = FindChild<RoomDoor>(World.CurrentRoom);
        _player.GlobalPosition = door.GlobalPosition;
        await Frames(3);

        var log = new List<string>();
        void OnEnter(string room) => log.Add("enter:" + room);
        void OnRespawn() => log.Add("respawn");
        Events.Instance.RoomEntered += OnEnter;
        Events.Instance.CheckpointRespawned += OnRespawn;

        // 先受致死伤害（PlayerDied 会延到帧末才发），同一帧里换房开始 → 复活请求到达时 World 正忙。
        HitOutcome kill = _player.HurtBox.Receive(Lethal("door_kill", 999f, _player.GlobalPosition));
        _ = World.TeleportAsync("S4_01", "From_S3_08");
        bool busy = World.IsTransitioning && kill == HitOutcome.Killed;
        await Frames(1);
        bool done = await WaitUntil(() => log.Contains("respawn") && !World.IsTransitioning, 400);
        Events.Instance.RoomEntered -= OnEnter;
        Events.Instance.CheckpointRespawned -= OnRespawn;

        Check(busy, "换房途中死亡搭建：致死结算后同一帧开始换房，死亡通知到达时 World 正忙");
        Check(done && string.Join(",", log) == "enter:S4_01,enter:S1_04,respawn",
            $"换房途中死亡：复活请求排队，等这次换房结束后才回检查点，再发复活通知（{string.Join(",", log)}）");
        Check(_player.Stats.Health == 140 && World.CurrentRoomDef?.Id == "S1_04", "换房途中死亡：最终在 S1_04 满血复活");
    }

    // ---------- ④ 过门 ----------

    private async Task DoorChecks()
    {
        await ResetPlayer("S3_08", "From_S3_07");
        RoomDoor door = FindChild<RoomDoor>(World.CurrentRoom);
        _player.GlobalPosition = door.GlobalPosition + new Vector2(-30, 0);
        Input.ActionPress(PlayerActions.Right);
        await Frames(8);
        Check(_player.Velocity.X > 0f, "过门搭建：玩家正在往右跑");
        _player.Buffer.Press(PlayerActions.Jump);

        bool lockedSeen = false, attackDuringLock = false, teleportedClean = false;
        HitOutcome hitDuringLock = HitOutcome.Damaged;
        string stateAtUnlock = "";
        void OnTele(StringName spawn)
        {
            teleportedClean = _player.Velocity == Vector2.Zero && !_player.AttackHitBox.Active && !_player.SlamHitBox.Active
                              && !_player.Buffer.Has(PlayerActions.Jump, 10f);
        }
        void OnLock(bool locked)
        {
            if (!locked) stateAtUnlock = _player.State?.GetType().Name;
        }
        Events.Instance.PlayerTeleported += OnTele;
        Events.Instance.InputLockChanged += OnLock;

        door.Interact(_player);
        for (int i = 0; i < 400 && (World.IsTransitioning || i < 2); i++)
        {
            if (InputLock.IsLocked)
            {
                lockedSeen |= _player.State is LockedState;
                if (i % 7 == 0)
                {
                    Input.ActionPress(PlayerActions.Attack);
                    Input.ActionPress(PlayerActions.Jump);
                }
                else
                {
                    Input.ActionRelease(PlayerActions.Attack);
                    Input.ActionRelease(PlayerActions.Jump);
                }
                attackDuringLock |= _player.AttackHitBox.Active || _player.State is AttackState or JumpWindupState;
                if (i == 5) hitDuringLock = _player.HurtBox.Receive(Lethal("lock_hit", 20f, _player.GlobalPosition));
            }
            await Frames(1);
        }
        ReleaseAll();
        await Frames(3);
        Events.Instance.PlayerTeleported -= OnTele;
        Events.Instance.InputLockChanged -= OnLock;

        Check(World.CurrentRoomDef?.Id == "S4_01", "过门：真实玩家到达 S4_01");
        Check(lockedSeen && !attackDuringLock, "过门：锁输入期间玩家处于 Locked，按攻击和跳都没有反应");
        Check(hitDuringLock == HitOutcome.Ignored && _player.Stats.Health == 140, $"过门：锁输入期间受击被忽略（{hitDuringLock}）");
        Check(teleportedClean, "过门：PlayerTeleported 时速度清零、判定框关闭、跳跃缓冲清空");
        Check(stateAtUnlock == nameof(IdleState) || _player.State is IdleState, $"过门：解锁后回到 Idle（解锁时 {stateAtUnlock}，现在 {_player.State?.GetType().Name}）");
        Check(!InputLock.IsLocked && !World.IsTransitioning, "过门：输入锁已释放");
    }
}
