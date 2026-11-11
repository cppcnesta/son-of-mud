using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 过门 S3_08 → S4_01（回应审查 M4）。用故意延迟 10 帧的假黑幕，断言事件顺序完全是：
/// 锁输入 → FadeRequested(a,淡出) → FadeCompleted(a,淡出) → RoomExiting → 换房 → PlayerTeleported → RoomEntered
/// → FadeRequested(b,淡入) → FadeCompleted(b,淡入) → 解锁。
/// 另外：FadeCompleted(a) 之前旧房间还在；途中再按交互只传送一次；id 对不上的完成通知不会推进流程；没有超时；出生点正确。
/// </summary>
public partial class DoorTest : WorldTestBase
{
    protected override string SuiteName => "door_test";

    protected override async Task RunChecks()
    {
        await SetUpGame(withFadeOverlay: false);
        var fader = new DelayedFader { Name = "DelayedFader", DelayFrames = 10 };
        Game.AddChild(fader);

        World.LoadRoomImmediate("S3_08", "From_S3_07");
        await Frames(10);
        RoomDoor door = FindChild<RoomDoor>(World.CurrentRoom);
        Check(door != null && door.TargetRoom == "S4_01" && door.TargetSpawn == "From_S3_08", "搭建：S3_08 里有通往 S4_01 的门，到达出生点 From_S3_08");
        if (door == null) return;
        Mover.GlobalPosition = door.GlobalPosition;
        await Frames(5);
        Check(door.CanInteract(Mover) && !World.IsTransitioning, "搭建：换房前门可以交互");

        var log = new List<string>();
        var requested = new HashSet<(int, bool)>();
        int fadeRequests = 0, teleports = 0;
        Vector2 teleportedAt = Vector2.Zero, expectedSpawn = Vector2.Inf;
        Events.Instance.InputLockChanged += OnLock;
        Events.Instance.FadeRequested += OnFadeRequested;
        Events.Instance.FadeCompleted += OnFadeCompleted;
        Events.Instance.RoomExiting += OnExiting;
        Events.Instance.PlayerTeleported += OnTeleported;
        Events.Instance.RoomEntered += OnEntered;
        World.RoomsContainer.ChildEnteredTree += OnRoomAdded;

        int firstId = -1;
        bool oldRoomAliveWhileWaiting = true, waitedOnFadeOut = false, secondPressBlocked = false, secondTeleportRefused = false, bogusSent = false;
        fader.OnWaitingFrame = (id, fadeOut, left) =>
        {
            if (!fadeOut) return;
            firstId = id;
            waitedOnFadeOut = true;
            oldRoomAliveWhileWaiting &= World.CurrentRoom?.RoomId == "S3_08" && World.CurrentRoom.IsInsideTree() && !log.Any(l => l.StartsWith("exit"));
            if (left == 7)
            {
                // id 对不上、方向对不上的完成通知都不能让流程往下走
                Events.Instance.EmitSignal(Events.SignalName.FadeCompleted, id + 100, true);
                Events.Instance.EmitSignal(Events.SignalName.FadeCompleted, id, false);
                bogusSent = true;
            }
            if (left == 5)
            {
                secondPressBlocked = !door.CanInteract(Mover);
                door.Interact(Mover); // 换房途中再按一次交互键
                Task<bool> again = World.TeleportAsync("S4_01", "From_S3_08");
                secondTeleportRefused = again.IsCompleted && !again.Result;
            }
        };

        int teleportsBefore = Mover.TeleportCount;
        ulong startMs = Time.GetTicksMsec();
        door.Interact(Mover);
        Check(World.IsTransitioning && InputLock.IsLocked, "按交互后立刻进入换房状态并锁住输入");
        bool done = await WaitUntil(() => !World.IsTransitioning, 240);
        ulong elapsedMs = Time.GetTicksMsec() - startMs;
        await Frames(10); // 让 MetSys 补发一次当前房间的 room_changed，确认不会再换房

        Events.Instance.InputLockChanged -= OnLock;
        Events.Instance.FadeRequested -= OnFadeRequested;
        Events.Instance.FadeCompleted -= OnFadeCompleted;
        Events.Instance.RoomExiting -= OnExiting;
        Events.Instance.PlayerTeleported -= OnTeleported;
        Events.Instance.RoomEntered -= OnEntered;
        World.RoomsContainer.ChildEnteredTree -= OnRoomAdded;

        Check(done, "过门流程在 4 秒内结束");
        int a = firstId, b = firstId + 1;
        string[] expected =
        {
            "lock", $"fadeReq({a},out)", $"fadeDone({a},out)", "exit:S3_08", "swap:S4_01", "teleported:From_S3_08", "enter:S4_01",
            $"fadeReq({b},in)", $"fadeDone({b},in)", "unlock",
        };
        Check(log.SequenceEqual(expected), "事件顺序完全是：锁输入 → 淡出请求 → 淡出完成 → RoomExiting → 换房 → PlayerTeleported → RoomEntered → 淡入请求 → 淡入完成 → 解锁"
              + $"（实际 {string.Join(" → ", log)}）");
        Check(waitedOnFadeOut && oldRoomAliveWhileWaiting, "收到 FadeCompleted(a) 之前，旧房间 S3_08 一直在场景里，没有发 RoomExiting");
        Check(bogusSent, "途中发过 id 不对和方向不对的 FadeCompleted，流程没有因此往下走（由上面两条断言保证）");
        Check(secondPressBlocked && secondTeleportRefused, "换房途中 CanInteract 为 false，再按交互和再调 TeleportAsync 都被拒绝");
        Check(teleports == 1 && fadeRequests == 2 && fader.Completed == 2, $"只发生一次传送（PlayerTeleported {teleports} 次，FadeRequested {fadeRequests} 次）");
        Check(World.FadeTimeouts == 0 && elapsedMs < 3000, $"没有触发 3 秒超时（超时 {World.FadeTimeouts} 次，用时 {elapsedMs} ms）");
        Check(teleportedAt.DistanceTo(expectedSpawn) < 0.01f, $"出生点正确：传送时替身在 S4_01 的 From_S3_08（{teleportedAt} / {expectedSpawn}）");
        Check(World.CurrentRoomDef?.Id == "S4_01" && World.RoomsContainer.GetChildCount() == 1, "结束后当前房间是 S4_01，旧房间已卸载");
        Check(!InputLock.IsLocked && !World.IsTransitioning, "结束后输入已解锁");
        Check(Mover.TeleportCount - teleportsBefore == 1 && Mover.Velocity.X == 0f, "替身收到一次 PlayerTeleported 并清零速度");
        Check(Cam.LimitRight == 1728 && Cam.LimitBottom == 648, $"镜头边界换成 S4_01 的 3×2 格（{Cam.LimitRight}×{Cam.LimitBottom}）");

        // 回程：S4_01 的门回到 S3_08
        RoomDoor back = FindChild<RoomDoor>(World.CurrentRoom);
        Check(back != null && back.TargetRoom == "S3_08" && back.TargetSpawn == "From_S4_01", "S4_01 有回 S3_08 的门");
        fader.OnWaitingFrame = null;
        if (back != null)
        {
            back.Interact(Mover);
            bool backDone = await WaitUntil(() => !World.IsTransitioning, 240);
            Check(backDone && World.CurrentRoomDef?.Id == "S3_08" && World.FadeTimeouts == 0, "回程过门成功，没有超时");
        }

        await TearDownGame();

        void OnLock(bool locked) => log.Add(locked ? "lock" : "unlock");
        void OnFadeRequested(int id, bool fadeOut)
        {
            fadeRequests++;
            requested.Add((id, fadeOut));
            log.Add($"fadeReq({id},{(fadeOut ? "out" : "in")})");
        }
        void OnFadeCompleted(int id, bool fadeOut)
        {
            if (requested.Contains((id, fadeOut))) log.Add($"fadeDone({id},{(fadeOut ? "out" : "in")})");
        }
        void OnExiting(string roomId) => log.Add("exit:" + roomId);
        void OnTeleported(StringName spawnId)
        {
            teleports++;
            log.Add("teleported:" + spawnId);
            teleportedAt = Mover.GlobalPosition;
            expectedSpawn = World.CurrentRoom.FindSpawn("From_S3_08")?.GlobalPosition ?? Vector2.Inf;
        }
        void OnEntered(string roomId) => log.Add("enter:" + roomId);
        void OnRoomAdded(Node node) => log.Add("swap:" + (node as RoomRoot)?.RoomId);
    }
}
