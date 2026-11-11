using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.Player.States;
using SonOfMud.World;

/// <summary>
/// 关卡几何计划 v3 第 2.1 节：战斗中贴着 ArenaLock 门体爬不上去。从正式入口 main_game.tscn 开新游戏，真实玩家、真实按键：
/// S1_04 → 走进 S1_05，在竞技场外先原地跳一次量出单跳高度 → 走进竞技场开战（入口门关上）→ 回头贴着门，
/// 按住 A+W 起跳，再连按跳 20 次：始终没有进入攀墙、没有蹬墙跳，最高点不超过单跳，身体顶部始终低于门顶。
/// （S1_05 正式房间里“三种上墙方式都上不去”要等地图 1 的新地形，竞技场墙是 X 之后再做。）
/// </summary>
public partial class ArenaLockClimbTest : EnemyFlowBase
{
    protected override string SuiteName => "arena_lock_climb";

    private const int JumpPresses = 20;
    private const int PressEvery = 15;

    protected override async Task RunChecks()
    {
        ListenEnemy();
        try
        {
            await Run();
        }
        finally
        {
            UnlistenEnemy();
        }
    }

    private float LocalY => Player.GlobalPosition.Y - World.CurrentRoom.GlobalPosition.Y;

    private async Task Run()
    {
        bool finished = await EnterMainGame(StartMode.NewGame);
        BindNarrative();
        await WaitSettled();
        Check(finished && Player != null && Player.HasAbility(SonOfMud.Player.PlayerAbilities.WallClimb), "新游戏：从正式入口开局，开局就有攀墙");
        if (Player == null) return;

        await Teleport("S1_04", SpawnPoint.SaveId);
        bool in105 = await HoldKeyUntil(RealKeys.Right, () => RoomId == "S1_05", 400);
        ArenaLock arenaLock = InRoom<ArenaLock>().FirstOrDefault();
        Check(in105 && arenaLock != null && !arenaLock.IsClosed && arenaLock.IsInGroup(Surfaces.NoClimbGroup),
            "S1_05：走进来时入口门开着，门体在 no_climb 分组里");
        if (arenaLock == null) return;

        // 竞技场外（x < 320）原地跳一次，量单跳高度
        await HoldKeyUntil(RealKeys.Right, () => LocalX >= 160f, 120);
        await Frames(5);
        float standY = LocalY, apex = standY;
        RealKeys.Down(RealKeys.Jump);
        await Frames(2);
        RealKeys.Up(RealKeys.Jump);
        bool left = false;
        for (int i = 0; i < 120; i++)
        {
            await Frames(1);
            apex = Mathf.Min(apex, LocalY);
            left |= !Player.IsOnFloor();
            if (left && Player.IsOnFloor()) break;
        }
        float single = standY - apex;
        Check(single > 60f && single < 160f && !arenaLock.IsClosed, $"竞技场外原地单跳 {single:F1}px（还没开战）");

        // 开战
        bool closed = await HoldKeyUntil(RealKeys.Right, () => arenaLock.IsClosed, 300);
        Check(closed && Encounters.Count == 1, "开战：走进竞技场，入口门关上");
        if (!closed) return;

        // 回头贴着门：按住 A+W，先按一次跳，之后每 15 帧按一次，共 20 次
        var doorShape = arenaLock.GetNode<CollisionShape2D>("Shape");
        float doorTop = doorShape.GlobalPosition.Y - ((RectangleShape2D)doorShape.Shape).Size.Y / 2f - World.CurrentRoom.GlobalPosition.Y;
        await HoldKeyUntil(RealKeys.Left, () => Player.IsOnWall() && LocalX < 120f, 300);
        float startY = LocalY, minY = startY;
        int contact = 0, airWindups = 0;
        bool sawClimb = false, stayedClosed = true;
        void OnState(StringName from, StringName to)
        {
            if (Player.State is JumpWindupState && !Player.IsOnFloor()) airWindups++;
        }
        Player.Machine.StateChanged += OnState;
        RealKeys.Down(RealKeys.Left);
        RealKeys.Down(RealKeys.MoveUp);
        for (int f = 0; f < JumpPresses * PressEvery + 60; f++)
        {
            int k = f / PressEvery, phase = f % PressEvery;
            if (k < JumpPresses && phase == 2) RealKeys.Down(RealKeys.Jump);
            if (k < JumpPresses && phase == 4) RealKeys.Up(RealKeys.Jump);
            await Frames(1);
            minY = Mathf.Min(minY, LocalY);
            sawClimb |= Player.State is WallClimbState;
            stayedClosed &= arenaLock.IsClosed;
            for (int i = 0; i < Player.GetSlideCollisionCount(); i++)
                if (Player.GetSlideCollision(i).GetCollider() == arenaLock) { contact++; break; }
        }
        RealKeys.ReleaseAll();
        Player.Machine.StateChanged -= OnState;
        await Frames(2);

        float rise = startY - minY;
        Check(stayedClosed && Deaths == 0 && RoomId == "S1_05", "攀爬尝试期间：一直在战斗中（入口门关着），玩家没死，还在 S1_05");
        Check(contact > 30, $"确实贴着 ArenaLock 门体（接触 {contact} 帧）");
        Check(!sawClimb && airWindups == 0, $"战斗中贴着门体按住 A+W 并连按跳 {JumpPresses} 次：从未进入攀墙，没有蹬墙跳（{airWindups} 次）");
        Check(rise <= single + 1f && minY - 24f > doorTop,
            $"最高点 {rise:F1}px 不超过单跳 {single:F1}px，身体顶部（{minY - 24f:F0}）始终低于门顶（{doorTop:F0}）");
    }
}
