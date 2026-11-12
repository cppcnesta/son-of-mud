using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;
using SonOfMud.Enemy;
using SonOfMud.Player;
using SonOfMud.World;

/// <summary>
/// 计划第 6 节第 3、7、8 步（从正式入口 main_game.tscn、真实玩家、真实按键）：
/// ① 晚加载拿到玩家（回应 E1）：新游戏在 S1_01 出生，走进 S1_03，祈祷泥人和虔诚泥人进入场景树时就拿到玩家；离开再回来，新生成的也拿得到；
///    全程不重新发 PlayerSpawned。
/// ② 生命周期（v1.2）：新游戏、读档、重开一局，开局完成（GameStartFinished(true)）之后 PlayerRegistry.Current 都指向场景里那个真实玩家，
///    不是 null、不是旧玩家；之后才加载的房间里，敌人都拿到它。
/// ③ 掉落和拾取：在 S1_03 用固定掉落种子打死祈祷泥人掉泥瓶——满血时走上去不捡、留在地上，掉了血站在上面立刻回 30，回血不超过上限；
///    泥肝（走 ItemDropped 这条正式路径生成）碰到 count.泥肝 加 1，最多 5 个，满了留在地上，少了再捡；两种都不播拾取台词；读档后 count.泥肝 保留。
/// ④ 摆放：各房间的敌人在设计的位置、站在地面上。
/// ⑤ 正式游戏里 S1_03 末端的虔诚泥人：玩家真实走过去，它停在约 300 像素外扔泥肝，打中掉 15 血、只结算一次。
/// </summary>
public partial class EnemyFlowTest : EnemyFlowBase
{
    protected override string SuiteName => "flow";

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            await LateLoadChecks();
            await PickupChecks();
            await DevoutInGameChecks();
            await ContinueChecks();
            await RestartChecks();
            await PlacementChecks();
        }
        finally
        {
            UnlistenEnemy();
        }
    }

    private List<EnemyBody> Prayers() => InRoom<EnemyBody>().Where(e => e.Data?.Kind == "prayer").OrderBy(e => e.GlobalPosition.X).ToList();

    // ---------- ① 晚加载 ----------

    private async Task LateLoadChecks()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        await WaitSettled();
        Check(finished && Starts.Count == 1 && Starts[0].success && Player != null && RoomId == "S1_01",
            "新游戏：从正式入口开局，收到 GameStartFinished(true)，玩家在 S1_01 出生");
        Check(PlayerRegistry.Current != null && PlayerRegistry.Current == Player,
            "生命周期（新游戏）：开局完成后 PlayerRegistry.Current 仍然指向场景里那个真实玩家");
        PlayerSpawnedCount = 0;

        bool inS102 = await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_02", 400);
        bool inS103 = inS102 && await CrossEdgeRight("S1_02", "From_S1_03", "S1_03");
        List<EnemyBody> enemies = InRoom<EnemyBody>();
        Check(inS103 && Prayers().Count == 5 && enemies.Count == 6 && enemies.All(p => p.TargetAtEnterTree == Player && p.Target == Player),
            $"晚加载：玩家在 S1_01 出生后真实走进 S1_03，{enemies.Count} 个敌人（祈祷泥人 5、虔诚泥人 1）进入场景树时就从 PlayerRegistry 拿到了玩家");
        var firstIds = enemies.Select(p => p.GetInstanceId()).ToHashSet();

        bool back = await CrossEdgeRight("S1_02", "From_S1_03", "S1_03");
        List<EnemyBody> again = InRoom<EnemyBody>();
        Check(back && again.Count == 6 && again.All(p => !firstIds.Contains(p.GetInstanceId()) && p.TargetAtEnterTree == Player && p.Target == Player)
              && firstIds.All(id => !GodotObject.IsInstanceIdValid(id)),
            "晚加载：离开 S1_03 再回来，旧的敌人已释放，新生成的同样在进入场景树时拿到玩家");
        Check(PlayerSpawnedCount == 0, $"晚加载：开局之后没有任何地方重新发 PlayerSpawned（{PlayerSpawnedCount} 次）");
    }

    // ---------- ③ 掉落和拾取 ----------

    private async Task<Pickup> KillForDrop(EnemyBody prayer)
    {
        // 反应随机数：前两次都逃跑（不反击，玩家不掉血）；掉落随机数：第一次就掉（30%）
        prayer.SetSeeds(FindSeed(r => r.Randf() < 0.5f && r.Randf() < 0.5f), FindSeed(r => r.Randf() < 0.3f));
        int killed = Killed.Count;
        for (int i = 0; i < 4 && !prayer.IsDead; i++) await Strike(prayer);
        await Frames(3);
        bool dead = prayer.IsDead && Killed.Count == killed + 1;
        Pickup pickup = InRoom<Pickup>().FirstOrDefault(p => !p.Collected);
        return dead ? pickup : null;
    }

    /// <summary>用真实按键走到拾取物上（玩家身体和它重叠）为止。</summary>
    private async Task<bool> WalkOnto(Pickup pickup)
    {
        if (!GodotObject.IsInstanceValid(pickup)) return false;
        float dx = pickup.GlobalPosition.X - Player.GlobalPosition.X;
        return await HoldKeyUntil(RealKeys.Toward(dx), () => !GodotObject.IsInstanceValid(pickup) || pickup.OverlapsBody(Player), 400);
    }

    /// <summary>走正式的 ItemDropped 路径，在玩家前方 offset 处掉一个物品（和敌人死亡时一样由 DropSpawner 生成）。</summary>
    private async Task<Pickup> DropNear(StringName item, float offset)
    {
        var before = InRoom<Pickup>().Select(p => p.GetInstanceId()).ToHashSet();
        float floorY = Player.GlobalPosition.Y + 24f;
        Events.Instance.EmitSignal(Events.SignalName.ItemDropped, item, new Vector2(Player.GlobalPosition.X + offset, floorY));
        await Frames(2);
        return InRoom<Pickup>().FirstOrDefault(p => !before.Contains(p.GetInstanceId()));
    }

    private async Task PickupChecks()
    {
        string liverKey = Pickup.CountKey("泥肝");
        await WaitUntil(() => Player.IsOnFloor(), 120);
        int pickupLines = Requests.Count(r => r.StartsWith("pickups:"));
        List<EnemyBody> prayers = Prayers();
        Pickup bottle = await KillForDrop(prayers[0]);
        Check(bottle != null && Dropped.SequenceEqual(new[] { "泥瓶" }) && bottle.ItemId == "泥瓶" && bottle.Item?.HealAmount == 30
              && bottle.GetParent() == World.CurrentRoom.GetNode("Objects"),
            "掉落：真实攻击打死祈祷泥人（固定掉落种子），World 在当前房间生成泥瓶拾取物");
        if (bottle == null) return;

        // 满血：走上去不捡，留在地上
        int max = Player.Stats.MaxHealth;
        bool onTop = Player.Stats.Health == max && await WalkOnto(bottle);
        await Frames(30);
        Check(onTop && GodotObject.IsInstanceValid(bottle) && !bottle.Collected && bottle.OverlapsBody(Player) && Player.Stats.Health == max,
            $"泥瓶：满血（{max}）时站在泥瓶上 30 帧也不捡，泥瓶留在地上");

        // 掉了血（测试直接把血量设成 100，代替挨打）：站在上面立刻捡起，回 30
        Player.Stats.SetHealth(max - 40);
        bool healed = await WaitUntil(() => !GodotObject.IsInstanceValid(bottle), 10);
        Check(healed && Player.Stats.Health == max - 10, $"泥瓶：没满血时碰到立刻回 30 血（{max - 40} → {Player.Stats.Health}），泥瓶消失");

        // 回血不超过上限
        Player.Stats.SetHealth(max - 15);
        Pickup second = await DropNear("泥瓶", 90f);
        bool picked2 = second != null && await WalkOnto(second) && await WaitUntil(() => !GodotObject.IsInstanceValid(second), 10);
        Check(picked2 && Player.Stats.Health == max, $"泥瓶：差 15 血时捡到，回满到 {Player.Stats.Health}（不超过上限 {max}）");
        Check(GameSession.Progress.GetVar(Pickup.CountKey("泥瓶"), 0).AsInt64() == 0, "泥瓶：碰到就用掉，不进计数");

        // 泥肝：每捡一个 count.泥肝 加 1，最多 5 个
        var counts = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            Pickup liver = await DropNear("泥肝", i % 2 == 0 ? 90f : -90f);
            if (liver == null || liver.Item?.MaxCount != 5) break;
            await WalkOnto(liver);
            await WaitUntil(() => !GodotObject.IsInstanceValid(liver), 10);
            counts.Add(GameSession.Progress.GetVar(liverKey, 0).AsInt64());
        }
        Check(counts.SequenceEqual(new long[] { 1, 2, 3, 4, 5 }) && GameSession.Progress.GetVar(liverKey).VariantType == Variant.Type.Int,
            $"泥肝：碰到一个 count.泥肝 加 1（int），依次 {string.Join("→", counts)}");
        Pickup sixth = await DropNear("泥肝", 90f);
        bool onSixth = sixth != null && await WalkOnto(sixth);
        await Frames(30);
        Check(onSixth && GodotObject.IsInstanceValid(sixth) && !sixth.Collected && GameSession.Progress.GetVar(liverKey, 0).AsInt64() == 5,
            "泥肝：已经有 5 个时站在第 6 个上面也不捡，留在地上，计数还是 5");
        GameSession.Progress.SetVar(liverKey, 4L);
        bool tookAgain = await WaitUntil(() => !GodotObject.IsInstanceValid(sixth), 10);
        Check(tookAgain && GameSession.Progress.GetVar(liverKey, 0).AsInt64() == 5, "泥肝：数量少于 5 时（测试直接设成 4）站在上面立刻捡起，回到 5");
        await Frames(10);
        Check(Requests.Count(r => r.StartsWith("pickups:")) == pickupLines && !Narr.Subtitle.IsShowing,
            "拾取台词：泥瓶、泥肝的拾取都不请求拾取台词（暂时没有）");
    }

    // ---------- ⑤ 正式游戏里的虔诚泥人 ----------

    private async Task DevoutInGameChecks()
    {
        EnemyBody devout = InRoom<EnemyBody>().FirstOrDefault(e => e.Data?.Kind == "devout");
        if (devout == null)
        {
            Check(false, "虔诚泥人：S1_03 里找不到虔诚泥人");
            return;
        }
        var thrown = new List<Projectile>();
        devout.ProjectileThrown += p => thrown.Add(p);
        bool spotted = await HoldKeyUntil(RealKeys.Right, () => devout.State is SonOfMud.Enemy.States.ChaseState or SonOfMud.Enemy.States.AttackWindupState, 900);
        bool threw = await WaitUntil(() => thrown.Count > 0, 300);
        float distance = Mathf.Abs(devout.GlobalPosition.X - Player.GlobalPosition.X);
        int hp = Player.Stats.Health;
        Projectile liver = thrown.FirstOrDefault();
        ProjectileEnd end = ProjectileEnd.None;
        HitOutcome? outcome = null;
        int resolutions = -1;
        if (liver != null && GodotObject.IsInstanceValid(liver))
            liver.Finished += (reason, position) => { end = (ProjectileEnd)reason; outcome = liver.Outcome; resolutions = liver.Resolutions; };
        await WaitUntil(() => end != ProjectileEnd.None, 180);
        await Frames(2);
        Check(spotted && threw && distance <= 305f && end == ProjectileEnd.Target && outcome == HitOutcome.Damaged && resolutions == 1 && Player.Stats.Health == hp - 15
              && !GodotObject.IsInstanceValid(liver),
            $"虔诚泥人（正式游戏 S1_03）：玩家真实走近，它在 {distance:F0} 像素处扔出泥肝，打中掉 15 血（{hp} → {Player.Stats.Health}），只结算一次，泥肝消失");
        // 退回安全的地方，免得后面的检查一直挨打
        await HoldKeyUntil(RealKeys.Left, () => !devout.CanSeeTarget(devout.Data.VisionRange * 1.5f), 600);
    }

    // ---------- ② 读档 ----------

    private async Task ContinueChecks()
    {
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        bool saved = FileAccess.FileExists(SavePath) && Saves.CheckpointRoom == "S1_04";
        Check(saved, "读档准备：在 S1_04 存档点真实按 E 存档");
        PlayerCharacter old = Player;
        bool finished = await EnterMainGame(StartMode.Continue);
        BindNarrative();
        await WaitSettled();
        Check(finished && Starts[^1].success && RoomId == "S1_04" && Player != null && Player != old && !GodotObject.IsInstanceValid(old),
            "读档：从正式入口“继续”，到达 S1_04，玩家是新实例，旧玩家已释放");
        Check(PlayerRegistry.Current == Player, "生命周期（读档）：开局完成后 PlayerRegistry.Current 指向新场景里的真实玩家（不是 null、不是旧玩家）");
        Check(GameSession.Progress.GetVar(Pickup.CountKey("泥肝"), 0).AsInt64() == 5, "拾取：读档后 count.泥肝 仍是 5");
        await Teleport("S1_03", "From_S1_02");
        Check(InRoom<EnemyBody>().Count == 6 && InRoom<EnemyBody>().All(p => p.TargetAtEnterTree == Player), "读档之后才加载的 S1_03 里，敌人拿到的是新玩家");
    }

    // ---------- ② 重开一局 ----------

    private async Task RestartChecks()
    {
        PlayerCharacter old = Player;
        Node oldScene = MainGame;
        int before = Starts.Count;
        Events.Instance.EmitSignal(Events.SignalName.RestartRunRequested);
        bool finished = await WaitUntil(() => Starts.Count > before && GetTree().CurrentScene != oldScene, 600);
        BindMainGame();
        BindNarrative();
        await WaitSettled();
        Check(finished && Starts[^1].success && Player != null && Player != old && !GodotObject.IsInstanceValid(old) && AllPlayers().Count == 1,
            "重开一局：重新载入正式入口，玩家是新实例，旧玩家已释放");
        Check(PlayerRegistry.Current == Player, "生命周期（重开）：开局完成后 PlayerRegistry.Current 指向新玩家，旧玩家已经注销");
        await Teleport("S1_03", "From_S1_02");
        Check(InRoom<EnemyBody>().Count == 6 && InRoom<EnemyBody>().All(p => p.TargetAtEnterTree == Player && p.Target == Player), "重开之后才加载的 S1_03 里，敌人拿到的是新玩家");
    }

    // ---------- ④ 摆放（运行中） ----------

    private async Task PlacementChecks()
    {
        var spawns = new Dictionary<string, string>
        {
            ["S1_03"] = "From_S1_02", ["S1_05"] = "From_S1_04", ["S2_01"] = "From_S1_07", ["S2_02"] = "From_S2_01",
            ["S2_03"] = "From_S2_04", ["S3_01"] = "From_S2_06", ["S3_02"] = "From_S3_01",
        };
        var problems = new List<string>();
        int count = 0;
        foreach (var (room, spawn) in spawns)
        {
            await Teleport(room, spawn);
            await Frames(30);
            var expected = EnemyUnitTest.Layout.Where(l => l.room == room).ToList();
            var live = InRoom<EnemyBody>();
            if (live.Count != expected.Count) problems.Add($"{room} 有 {live.Count} 个敌人");
            foreach (var e in expected)
            {
                EnemyBody enemy = live.FirstOrDefault(x => x.Name == e.node);
                if (enemy == null) { problems.Add($"{room} 缺 {e.node}"); continue; }
                count++;
                float tolerance = Mathf.Max(1f, enemy.Data.PatrolDistance + 6f);
                Vector2 local = enemy.GlobalPosition - World.CurrentRoom.GlobalPosition;
                Vector2 design = EnemyUnitTest.ScenePosition(room, e.node);   // 场景里的位置已由 enemy_unit 核对过在锚点格里
                if (!enemy.IsOnFloor() || Mathf.Abs(local.X - design.X) > tolerance || Mathf.Abs(local.Y - design.Y) > 1f)
                    problems.Add($"{room}/{e.node} 在 {local}（地面 {enemy.IsOnFloor()}），设计 {design}");
                if (enemy.Target != Player) problems.Add($"{room}/{e.node} 没拿到玩家");
            }
        }
        Check(problems.Count == 0 && count == EnemyUnitTest.Layout.Length,
            $"摆放：运行中 7 个房间的 {count} 个敌人都站在设计位置的地面上，并且拿到了玩家" + (problems.Count > 0 ? "：" + string.Join("; ", problems.Take(6)) : ""));
    }
}
