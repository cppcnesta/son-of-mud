using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;

/// <summary>
/// 竖切片：替身从 S1_03 往右走进 S1_04。
/// 断言：先 RoomExiting("S1_03")（此时旧房间还在场景里）再 RoomEntered("S1_04")；地图坐标连续；不发 PlayerTeleported；
/// 镜头边界等于 S1_04 的尺寸。另外检查镜头缩放、对准玩家和震屏。
/// </summary>
public partial class SliceTest : WorldTestBase
{
    protected override string SuiteName => "slice_test";

    protected override async Task RunChecks()
    {
        await SetUpGame();
        Check(World.Player == Mover && Cam.Target == Mover, "搭建：WorldGame 和镜头都通过 PlayerSpawned 拿到了替身");

        Check(World.LoadRoomImmediate("S1_03", "From_S1_04"), "搭建：直接载入 S1_03，站在右侧出口平台");
        await Frames(30);
        Check(Mover.IsOnFloor(), $"搭建：替身落在平台上（位置 {Mover.GlobalPosition}）");
        Check(Cam.LimitRight == 3456 && Cam.LimitBottom == 972, $"镜头：S1_03 的边界是 6×3 格 = 3456×972（实际 {Cam.LimitRight}×{Cam.LimitBottom}）");

        var log = new List<string>();
        bool oldRoomAliveAtExit = false;
        int teleports = 0;
        Events.Instance.RoomExiting += OnExiting;
        Events.Instance.RoomEntered += OnEntered;
        Events.Instance.PlayerTeleported += OnTeleported;

        float maxStep = 0f;
        Vector2 last = MapPixel();
        Mover.MoveDir = 1f;
        for (int frame = 0; frame < 240 && !log.Contains("enter:S1_04"); frame++)
        {
            await Frames(1);
            Vector2 now = MapPixel();
            maxStep = Mathf.Max(maxStep, now.DistanceTo(last));
            last = now;
        }
        // 进房后再走一小段，确认不会被弹回 S1_03
        for (int frame = 0; frame < 20; frame++)
        {
            await Frames(1);
            Vector2 now = MapPixel();
            maxStep = Mathf.Max(maxStep, now.DistanceTo(last));
            last = now;
        }
        Mover.MoveDir = 0f;

        Events.Instance.RoomExiting -= OnExiting;
        Events.Instance.RoomEntered -= OnEntered;
        Events.Instance.PlayerTeleported -= OnTeleported;

        Check(string.Join(",", log) == "exit:S1_03,enter:S1_04", $"顺序：先 RoomExiting(S1_03) 再 RoomEntered(S1_04)，且只换了一次房（实际 {string.Join(",", log)}）");
        Check(oldRoomAliveAtExit, "RoomExiting 发出时旧房间 S1_03 还在场景树里");
        Check(teleports == 0, $"走边缘换房不发 PlayerTeleported（实际 {teleports} 次）");
        Check(maxStep < 12f, $"地图坐标连续，没有跳变（单帧最大位移 {maxStep:0.00} 像素）");
        Check(World.CurrentRoomDef?.Id == "S1_04" && World.CurrentRoom?.RoomId == "S1_04", "当前房间是 S1_04");
        Check(World.RoomsContainer.GetChildCount() == 1, $"旧房间已卸载，Rooms 下只剩一个房间（实际 {World.RoomsContainer.GetChildCount()}）");
        Check(Mover.GlobalPosition.X > 0f && Mover.GlobalPosition.X < 400f && Mover.IsOnFloor(), $"替身在 S1_04 左侧地面上（{Mover.GlobalPosition}）");
        Check(MetSysApi.CurrentRoomId == ResourceUid.IdToText(ResourceLoader.GetResourceUid(World.CurrentRoomDef.ScenePath)) && MetSysApi.CurrentCoords == new Vector3I(11, 4, 0),
            $"MetSys 当前房间是 S1_04，玩家所在格 (11,4,0)（实际 {MetSysApi.CurrentCoords}）");
        Check(MetSysApi.IsCellDiscovered(new Vector3I(10, 4, 0), false) && MetSysApi.IsCellDiscovered(new Vector3I(11, 4, 0), false), "走过的格子已探索：S1_03 (10,4) 和 S1_04 (11,4)");

        // ---- 镜头 ----
        Check(Cam.LimitLeft == 0 && Cam.LimitTop == 0 && Cam.LimitRight == 1152 && Cam.LimitBottom == 648,
            $"镜头：边界等于 S1_04 的尺寸 0,0–1152×648（实际 {Cam.LimitLeft},{Cam.LimitTop}–{Cam.LimitRight}×{Cam.LimitBottom}）");
        Check(Cam.Zoom == Cam.Settings.Zoom && Cam.Zoom == Vector2.One, $"镜头：固定缩放来自 camera_settings.tres（{Cam.Zoom}）");
        await Frames(2);
        Check(Cam.GlobalPosition.DistanceTo(Mover.GlobalPosition) < 1f, "镜头：跟随替身");
        Vector2 center = Cam.GetScreenCenterPosition();
        Check(center.X >= 576f - 0.5f && center.X <= 1152f - 576f + 0.5f && center.Y >= 324f - 0.5f && center.Y <= 648f - 324f + 0.5f,
            $"镜头：画面中心被边界夹住，看不到房间外面（中心 {center}）");

        Events.Instance.EmitSignal(Events.SignalName.CameraShakeRequested, 10f, 0.2f);
        await Frames(3);
        bool shaking = Cam.IsShaking && Cam.Offset != Vector2.Zero && Cam.Offset.Length() <= 10f * 1.5f;
        await Frames(20);
        Check(shaking && !Cam.IsShaking && Cam.Offset == Vector2.Zero, "镜头：CameraShakeRequested 震屏，到时间后归零");

        await TearDownGame();

        void OnExiting(string roomId)
        {
            log.Add("exit:" + roomId);
            oldRoomAliveAtExit = World.CurrentRoom != null && World.CurrentRoom.RoomId == roomId && World.CurrentRoom.IsInsideTree();
        }
        void OnEntered(string roomId) => log.Add("enter:" + roomId);
        void OnTeleported(StringName spawnId) => teleports++;
    }
}
