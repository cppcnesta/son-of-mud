using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Enemy.Bosses.Keeper;
using SonOfMud.Enemy.Bosses.Keeper.States;
using SonOfMud.World;

/// <summary>
/// 守坑人巡游的正式房间集成测试（计划 v2 测试 B）：从正式入口 main_game.tscn 开新游戏，真实玩家、正式 S1_05 和正式 Boss 配置。
/// 直接传送到 S1_05 入口（旧 keeper_flow 从 S1_04 走过来会被 S1_04 的地形挡住，见计划里的基线），真实按键走进场地开战，然后：
/// ① 固定种子，玩家真实按键在场地里两个位置之间来回走，观察至少 20 秒、直到左右两端都到过（最多 40 秒）：走过的范围覆盖可走范围 ≥ 75%、至少到过一端（离端点 ≤ 150）、至少横穿 1 次、至少出 1 招，没卡住、没出场地，不出招时死区外面朝玩家；
/// ② 死亡重来：被打死 → 在存档点复活、入口门打开 → 再进来守坑人满血、等待，开战后照样巡游（5 秒走动 &gt; 300）；
/// ③ 击败后出口可用：把守坑人打到只剩 1 血再真实按键打死 → 闪白 → 出口门打开 → 玩家真实走进 S1_06。
/// </summary>
public partial class KeeperRoamFlowTest : EnemyFlowBase
{
    protected override string SuiteName => "keeper_roam_flow";

    private const ulong RoamSeed = 20261120;
    private KeeperBoss _keeper;
    private ArenaLock _lock;
    private GateBarrier _gate;

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            await Run();
        }
        finally
        {
            RealKeys.ReleaseAll();
            if (Player != null) Player.HurtBox.SetInvulnerable("roam_flow", false);
            UnlistenEnemy();
        }
    }

    private void BindRoom()
    {
        _keeper = InRoom<KeeperBoss>().FirstOrDefault();
        _lock = InRoom<ArenaLock>().FirstOrDefault();
        _gate = InRoom<GateBarrier>().FirstOrDefault();
        _keeper?.SetRoamSeed(RoamSeed);
    }

    /// <summary>传送到 S1_05 入口，真实按右走到场地中间附近（开战）。</summary>
    private async Task<bool> EnterArena()
    {
        await Teleport("S1_05", "From_S1_04");
        await Frames(5);
        BindRoom();
        bool dormant = _keeper != null && _keeper.State is DormantState && _keeper.Stats.Health == _keeper.Stats.MaxHealth;
        bool walked = await HoldKeyUntil(RealKeys.Right, () => LocalX >= 1300f, 600);
        await Frames(5);
        return dormant && walked && _keeper.FightActive;
    }

    private async Task Run()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        await WaitSettled();
        Check(finished && Player != null, "新游戏：从正式入口开局");

        // 在 S1_04 存档（复活点）
        await Teleport("S1_04", SpawnPoint.SaveId);
        await KeyTap(RealKeys.Interact);
        await Frames(3);
        Check(Saves.CheckpointRoom == "S1_04", "搭建：在 S1_04 存档点真实按 E 存档");

        bool entered = await EnterArena();
        Check(entered && _lock != null && _lock.IsClosed && Encounters.Count == 1,
            $"开战：传送到 S1_05 入口、真实按右走进场地 → 守坑人开战（{_keeper?.State?.DisplayName}），入口门关上");
        if (!entered) return;

        await RoamObserveChecks();
        await DeathRetryChecks();
        await DefeatExitChecks();
    }

    // ---------- ① 巡游（最多 40 秒） ----------

    private async Task RoamObserveChecks()
    {
        Player.HurtBox.SetInvulnerable("roam_flow", true);
        float min = _keeper.RoamMinX, max = _keeper.RoamMaxX;
        float lo = _keeper.GlobalPosition.X, hi = lo, lastX = lo;
        int crossings = _keeper.Crossings, attacks = 0, stuck = 0, worstStuck = 0;
        bool inside = true, facing = true, wasRoam = _keeper.State is ApproachState;
        var roam = _keeper.Machine.Get<ApproachState>();
        // 观察到左右两端都到过为止，最多 40 秒（一个种子 20 秒里两端都到的概率约一半，见 keeper_lab 的 8 种子统计）
        int frames = 0;
        // 玩家真实按键在场地里两个位置之间来回走（每 8 秒换一次：局部 x 1000 / 1600），守坑人会跟着换边
        float roomX0 = World.CurrentRoom.GlobalPosition.X;
        Key held = default;
        for (; frames < 2400 && !(frames >= 1200 && lo <= min + 150f && hi >= max - 150f); frames++)
        {
            float goal = (frames / 480) % 2 == 0 ? 1000f : 1600f;
            float toGoal = goal - (Player.GlobalPosition.X - roomX0);
            Key want = Mathf.Abs(toGoal) > 24f ? RealKeys.Toward(toGoal) : default;
            if (want != held)
            {
                if (held != default) RealKeys.Up(held);
                if (want != default) RealKeys.Down(want);
                held = want;
            }
            await Frames(1);
            float x = _keeper.GlobalPosition.X;
            lo = Mathf.Min(lo, x);
            hi = Mathf.Max(hi, x);
            inside &= x >= min - 1f && x <= max + 1f;
            bool isRoam = _keeper.State is ApproachState;
            if (wasRoam && !isRoam) attacks++;
            wasRoam = isRoam;
            // 卡住：巡游中、没在停、却一直没走动
            stuck = isRoam && !roam.Paused && Mathf.Abs(x - lastX) < 0.5f ? stuck + 1 : 0;
            worstStuck = Mathf.Max(worstStuck, stuck);
            lastX = x;
            float dx = Player.GlobalPosition.X - x;
            if (isRoam && Mathf.Abs(dx) >= _keeper.Keeper.TurnDeadzone + 10f) facing &= _keeper.Facing == Mathf.Sign(dx);
        }
        if (held != default) RealKeys.Up(held);
        Player.HurtBox.SetInvulnerable("roam_flow", false);
        float roomX = World.CurrentRoom.GlobalPosition.X;
        // 同一段里两端都到过是随机的（keeper_lab 8 个种子各 20 秒：约一半做到），这里按覆盖率 ≥ 75% + 至少到过一端验收
        float coverage = (hi - lo) / (max - min);
        bool anEnd = lo <= min + 150f || hi >= max - 150f;
        Check(coverage >= 0.75f && anEnd && inside,
            $"S1_05 巡游（固定种子，{frames / 60f:F1} 秒）：走过 {lo - roomX:F0}～{hi - roomX:F0}，覆盖可走范围 {min - roomX:F0}～{max - roomX:F0} 的 {coverage:P0}（≥ 75%），到过{(lo <= min + 150f ? "左" : "")}{(hi >= max - 150f ? "右" : "")}端，没出场地");
        Check(_keeper.Crossings > crossings && attacks >= 1,
            $"S1_05 巡游：横穿过玩家 {_keeper.Crossings - crossings} 次，出招 {attacks} 次");
        Check(worstStuck < 120 && facing, $"S1_05 巡游：没卡住（最长原地 {worstStuck} 帧 < 2 秒），不出招时玩家在死区外始终面朝玩家");
    }

    // ---------- ② 死亡重来 ----------

    private async Task DeathRetryChecks()
    {
        ulong first = _keeper.GetInstanceId();
        ArenaLock firstLock = _lock;
        bool died = await WaitUntil(() => Deaths > 0, 3600);
        bool lockOpened = await WaitUntil(() => GodotObject.IsInstanceValid(firstLock) && !firstLock.IsClosed, 5);
        bool respawned = await WaitUntil(() => RoomId == "S1_04" && !World.IsTransitioning && Player.Stats.Health == Player.Stats.MaxHealth, 600);
        await Frames(10);
        Check(died && lockOpened && respawned && Defeats.Count == 0, "死亡重来：站着不动被守坑人打死，入口门打开，在 S1_04 存档点满血复活");

        bool entered = await EnterArena();
        Check(entered && _keeper.GetInstanceId() != first && _keeper.Stats.Health == _keeper.Stats.MaxHealth && Encounters.Count == 2,
            "死亡重来：再进 S1_05，守坑人重新生成、满血，开战");
        Player.HurtBox.SetInvulnerable("roam_flow", true);
        float travelled = 0f, lastX = _keeper.GlobalPosition.X;
        for (int i = 0; i < 300; i++)
        {
            await Frames(1);
            travelled += Mathf.Abs(_keeper.GlobalPosition.X - lastX);
            lastX = _keeper.GlobalPosition.X;
        }
        Player.HurtBox.SetInvulnerable("roam_flow", false);
        Check(travelled > 300f, $"死亡重来：开战后照样巡游（5 秒走了 {travelled:F0} 像素）");
    }

    // ---------- ③ 击败后出口可用 ----------

    private async Task DefeatExitChecks()
    {
        Player.HurtBox.SetInvulnerable("roam_flow", true);
        _keeper.Stats.TakeDamage(_keeper.Stats.Health - 1);
        var bot = new KeeperBot(Player, _keeper, n => Frames(n));
        bool won = await bot.FightUntil(() => _keeper.State is TransformState or GoneState, 1800);
        bool gone = await WaitUntil(() => _keeper.State is GoneState, 180);
        Player.HurtBox.SetInvulnerable("roam_flow", false);
        Check(won && gone && Defeats.SequenceEqual(new[] { "keeper" }) && _gate != null && _gate.IsOpen,
            $"击败：只剩 1 血后真实按键打死（出刀 {bot.Attacks}），闪白结束出口门打开");
        // 出口在高处平台上（要攀墙上去）：用出口旁的出生点（从 S1_06 回来时站的地方）把玩家放上那层平台（房间按 boss_keeper 重新载入），
        // 再真实按右走出去。门没开的话会被挡在这里。
        await Teleport("S1_05", "From_S1_06");
        await Frames(10);
        BindRoom();
        bool reloadedOpen = RoomId == "S1_05" && _gate != null && _gate.IsOpen && _keeper != null && _keeper.State is GoneState;
        bool exited = reloadedOpen && await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_06", 600);
        Check(exited, $"击败后出口可用：站到出口那层平台上（守坑人已击败、出口门开着）真实按右，穿过出口门进入 S1_06（现在在 {RoomId}）");
    }
}
