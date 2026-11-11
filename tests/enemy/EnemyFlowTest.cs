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
/// ① 晚加载拿到玩家（回应 E1）：新游戏在 S1_01 出生，走进 S1_03，朝圣者进入场景树时就拿到玩家；离开再回来，新生成的也拿得到；
///    全程不重新发 PlayerSpawned。
/// ② 生命周期（v1.2）：新游戏、读档、重开一局，开局完成（GameStartFinished(true)）之后 PlayerRegistry.Current 都指向场景里那个真实玩家，
///    不是 null、不是旧玩家；之后才加载的房间里，敌人都拿到它。
/// ③ 掉落和拾取：在 S1_03 用固定掉落种子打死朝圣者，碰到小泥偶 count.小泥偶 加 1；第一次出字幕台词，第二次不再出；读档后计数保留。
/// ④ 摆放：各房间的敌人在设计的位置、站在地面上。
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
            await ContinueChecks();
            await RestartChecks();
            await PlacementChecks();
        }
        finally
        {
            UnlistenEnemy();
        }
    }

    private List<EnemyBody> Pilgrims() => InRoom<EnemyBody>().Where(e => e.Data?.Kind == "pilgrim").OrderBy(e => e.GlobalPosition.X).ToList();

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
        List<EnemyBody> pilgrims = Pilgrims();
        Check(inS103 && pilgrims.Count == 5 && pilgrims.All(p => p.TargetAtEnterTree == Player && p.Target == Player),
            $"晚加载：玩家在 S1_01 出生后真实走进 S1_03，{pilgrims.Count} 个朝圣者进入场景树时就从 PlayerRegistry 拿到了玩家");
        var firstIds = pilgrims.Select(p => p.GetInstanceId()).ToHashSet();

        bool back = await CrossEdgeRight("S1_02", "From_S1_03", "S1_03");
        List<EnemyBody> again = Pilgrims();
        Check(back && again.Count == 5 && again.All(p => !firstIds.Contains(p.GetInstanceId()) && p.TargetAtEnterTree == Player && p.Target == Player)
              && firstIds.All(id => !GodotObject.IsInstanceIdValid(id)),
            "晚加载：离开 S1_03 再回来，旧的朝圣者已释放，新生成的同样在进入场景树时拿到玩家");
        Check(PlayerSpawnedCount == 0, $"晚加载：开局之后没有任何地方重新发 PlayerSpawned（{PlayerSpawnedCount} 次）");
    }

    // ---------- ③ 掉落和拾取 ----------

    private async Task<Pickup> KillForDrop(EnemyBody pilgrim)
    {
        pilgrim.SetSeeds(FindSeed(r => r.Randf() < 0.5f), FindSeed(r => r.Randf() < 0.3f));
        int killed = Killed.Count;
        for (int i = 0; i < 4 && !pilgrim.IsDead; i++) await Strike(pilgrim);
        await Frames(3);
        bool dead = pilgrim.IsDead && Killed.Count == killed + 1;
        Pickup pickup = InRoom<Pickup>().FirstOrDefault(p => !p.Collected);
        return dead ? pickup : null;
    }

    private async Task<bool> WalkOnto(Pickup pickup, string key, long expected)
    {
        float dx = pickup.GlobalPosition.X - Player.GlobalPosition.X;
        return await HoldKeyUntil(RealKeys.Toward(dx), () => GameSession.Progress.GetVar(key, 0).AsInt64() == expected, 400);
    }

    private async Task PickupChecks()
    {
        string key = Pickup.CountKey("小泥偶");
        await WaitUntil(() => Player.IsOnFloor(), 120);
        List<EnemyBody> pilgrims = Pilgrims();
        Pickup first = await KillForDrop(pilgrims[0]);
        Check(first != null && Dropped.SequenceEqual(new[] { "小泥偶" }) && first.ItemId == "小泥偶" && first.GetParent() == World.CurrentRoom.GetNode("Objects"),
            "掉落：真实攻击打死朝圣者（固定掉落种子），World 在当前房间生成小泥偶拾取物");
        if (first == null) return;
        int requests = Requests.Count(r => r == "pickups:小泥偶");
        bool picked = await WalkOnto(first, key, 1);
        bool subtitle = await WaitUntil(() => Narr.Subtitle.IsShowing && Narr.Subtitle.CurrentText == "母神的土偶！打碎它！", 30);
        Check(picked && GameSession.Progress.GetVar(key).VariantType == Variant.Type.Int && !GodotObject.IsInstanceValid(first),
            "拾取：玩家碰到小泥偶，count.小泥偶 从 0 变成 1（int），拾取物消失");
        Check(subtitle && Requests.Count(r => r == "pickups:小泥偶") == requests + 1 && !PauseService.IsPaused,
            "拾取：第一次拾取出现字幕台词“？？？：母神的土偶！打碎它！”（pickups.orph，字幕条，不暂停）");
        await WaitUntil(() => !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing, 600);

        Pickup second = await KillForDrop(pilgrims[1]);
        bool picked2 = second != null && await WalkOnto(second, key, 2);
        await Frames(30);
        Check(picked2 && Requests.Count(r => r == "pickups:小泥偶") == requests + 1 && !Narr.Bark.IsPlaying && !Narr.Subtitle.IsShowing,
            "拾取：第二次拾取计数变成 2，不再出现台词");
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
        Check(GameSession.Progress.GetVar(Pickup.CountKey("小泥偶"), 0).AsInt64() == 2, "拾取：读档后 count.小泥偶 仍是 2");
        await Teleport("S1_03", "From_S1_02");
        Check(Pilgrims().Count == 5 && Pilgrims().All(p => p.TargetAtEnterTree == Player), "读档之后才加载的 S1_03 里，敌人拿到的是新玩家");
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
        Check(Pilgrims().Count == 5 && Pilgrims().All(p => p.TargetAtEnterTree == Player && p.Target == Player), "重开之后才加载的 S1_03 里，敌人拿到的是新玩家");
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
                float tolerance = e.scene == "worker" ? 70f : 1f;
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
