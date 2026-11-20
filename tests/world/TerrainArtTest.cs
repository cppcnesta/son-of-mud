using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.World;

/// <summary>
/// 地图四的「地形图」（美术画了地板、台阶、洞口的那张图，碰撞照它描）和碰撞对得上（见 TerrainArt）：
/// ① 三个房间（S4_01、S4_02、S4_0304）的场景里地形图都放好了：视差 1，缩放和位置是表里的值；
/// ② 正式游戏里镜头在房间左、中、右三处，地形图在世界里的位置一动不动；
/// ③ 图上地板的上沿和碰撞地面的上沿对得上：每个房间至少 3 处，误差不超过 16 像素（按图片透明度从上往下找第一块不透明的像素）；
/// ④ 再按命名导入一次背景，地形图的参数不变；打包成场景再打开，仍然对。
/// </summary>
public partial class TerrainArtTest : FlowTestBase
{
    protected override string SuiteName => "terrain_art";
    protected override string ResultPrefix => "world";

    private const float Tolerance = 16f;
    /// <summary>每个房间量的点：房间局部 x、这里地面的瓦片行（照着图选的，避开柱子、装饰、台阶边和门块）。</summary>
    private static readonly Dictionary<string, (float X, int Row)[]> Points = new()
    {
        ["S4_01"] = new[] { (250f, 14), (330f, 14), (800f, 17), (1050f, 17) },
        ["S4_02"] = new[] { (700f, 15), (1100f, 15), (1600f, 15) },
        ["S4_0304"] = new[] { (150f, 11), (700f, 11), (1300f, 20), (1800f, 20), (2200f, 20) },
    };

    private RoomRoot Room => World?.CurrentRoom;

    protected override async Task RunChecks()
    {
        foreach (string id in TerrainArt.Rooms.Keys)
            SceneChecks(id);

        bool started = await EnterMainGame(StartMode.NewGame);
        await WaitSettled();
        foreach (string seen in new[] { "h_tower.七道门", "h_tower.塔顶" })
            GameSession.Progress.SetVar("seen." + seen, true);
        Check(started && World != null && Cam != null && Player != null, "正式入口开新游戏成功，有 WorldGame、RoomCamera、玩家");
        if (World == null || Cam == null || Player == null) return;
        foreach (string id in TerrainArt.Rooms.Keys)
            await CameraChecks(id);
    }

    // ① ③ ④ 场景本身
    private void SceneChecks(string id)
    {
        WorldLayout layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        RoomDef def = layout.Find(id);
        var room = GD.Load<PackedScene>(def.ScenePath).Instantiate<RoomRoot>();
        AddChild(room);
        string problem = TerrainArt.Problem(room);
        Check(problem == "", $"{id}：场景里地形图放好了（视差 1、缩放 {TerrainArt.Rooms[id].Scale:0.####}、位置 {TerrainArt.Rooms[id].Position}）" + (problem == "" ? "" : "：" + problem));
        AlignChecks(id, room, "");

        // 再导入一次（和房间根节点上的“按命名导入背景”按钮一样）：只换图，地形图节点的参数不变
        BackgroundImporter.ImportPlan plan = BackgroundImporter.PlanImport(room, BackgroundSettings.LoadDefault());
        if (!plan.Empty) BackgroundImporter.Apply(room, plan);
        string again = TerrainArt.Problem(room);
        Check(again == "", $"{id}：再按命名导入一次背景，地形图的视差、缩放、位置都没变" + (again == "" ? "" : "：" + again));

        // 打包成场景再打开
        RemoveChild(room);
        var packed = new PackedScene();
        Error pack = packed.Pack(room);
        room.Free();
        var reopened = pack == Error.Ok ? packed.Instantiate<RoomRoot>() : null;
        if (reopened != null) AddChild(reopened);
        string after = reopened == null ? "打包失败 " + pack : TerrainArt.Problem(reopened);
        Check(after == "", $"{id}：打包成场景再打开，地形图仍然放好" + (after == "" ? "" : "：" + after));
        if (reopened != null)
        {
            AlignChecks(id, reopened, "（再打开后）");
            reopened.QueueFree();
        }
    }

    /// <summary>③ 图上地板上沿 vs 碰撞地面上沿。</summary>
    private void AlignChecks(string id, RoomRoot room, string tag)
    {
        TerrainArt.Entry entry = TerrainArt.Rooms[id];
        Sprite2D sprite = TerrainArt.FindImage(room, entry.File);
        var layer = room.GetNodeOrNull<TileMapLayer>("Geometry");
        Image image = sprite?.Texture?.GetImage();
        if (image != null && image.IsCompressed()) image.Decompress();
        if (sprite == null || layer == null || image == null)
        {
            Check(false, $"{id}{tag}：找得到地形图、图片数据和 Geometry 瓦片层");
            return;
        }
        var results = new List<string>();
        int good = 0;
        foreach ((float x, int row) in Points[id])
        {
            int col = Mathf.FloorToInt((x - layer.Position.X) / 32f);
            var cell = new Vector2I(col, row);
            bool solid = layer.GetCellSourceId(cell) != -1 && layer.GetCellSourceId(cell + Vector2I.Up) == -1;
            float tileTop = layer.MapToLocal(cell).Y - 16f + layer.Position.Y;
            float? artTop = ArtTop(image, sprite, x, tileTop - 96f, tileTop + 64f);
            float diff = artTop.HasValue ? artTop.Value - tileTop : float.NaN;
            bool ok = solid && artTop.HasValue && Mathf.Abs(diff) <= Tolerance;
            if (ok) good++;
            results.Add($"x {x:0}：碰撞 {tileTop:0}，图 {(artTop.HasValue ? artTop.Value.ToString("0") : "没找到")}（{diff:+0.#;-0.#;0}）{(solid ? "" : "，这里不是地面的上沿")}");
        }
        Check(good == Points[id].Length && good >= 3, $"{id}{tag}：图上地板和碰撞地面对得上（{good}/{Points[id].Length} 处误差 ≤ {Tolerance}）：{string.Join("；", results)}");
    }

    /// <summary>在房间局部 x 处，从 fromY 往下到 toY 找地形图第一块不透明的像素，返回它的房间局部 y。</summary>
    private static float? ArtTop(Image image, Sprite2D sprite, float x, float fromY, float toY)
    {
        float k = sprite.Scale.X;
        int px = Mathf.FloorToInt((x - sprite.Position.X) / k);
        if (px < 0 || px >= image.GetWidth()) return null;
        int y0 = Mathf.Max(0, Mathf.FloorToInt((fromY - sprite.Position.Y) / k));
        int y1 = Mathf.Min(image.GetHeight() - 1, Mathf.CeilToInt((toY - sprite.Position.Y) / k));
        // 起点要在空中（透明），不然量的不是地板上沿
        if (image.GetPixel(px, y0).A > 0.5f) return null;
        for (int py = y0; py <= y1; py++)
            if (image.GetPixel(px, py).A > 0.5f) return sprite.Position.Y + py * k;
        return null;
    }

    // ② 镜头左中右，地形图在世界里不动
    private async Task CameraChecks(string id)
    {
        ReleaseInputs();
        World.LoadRoomImmediate(id, SpawnPoint.DefaultId);
        await Frames(15);
        TerrainArt.Entry entry = TerrainArt.Rooms[id];
        Sprite2D sprite = Room == null ? null : TerrainArt.FindImage(Room, entry.File);
        var parallax = sprite?.GetParent() as Parallax2D;
        if (sprite == null || parallax == null || World.CurrentRoomDef?.Id != id)
        {
            Check(false, $"{id}：游戏里进了房间、找得到地形图");
            return;
        }
        float width = World.CurrentRoomDef.Size.X * 576f;
        var seen = new List<Vector2>();
        var cams = new List<float>();
        foreach (float fx in new[] { 0.05f, 0.5f, 0.95f })
        {
            Player.GlobalPosition = Room.GlobalPosition + new Vector2(width * fx, 200f);
            Player.Velocity = Vector2.Zero;
            Cam.SnapToTarget();
            await Frames(3);
            seen.Add(sprite.GlobalPosition);
            cams.Add(Cam.GetScreenCenterPosition().X - Room.GlobalPosition.X);
        }
        Vector2 expected = Room.GlobalPosition + entry.Position;
        // 只有一屏宽的房间（S4_01）镜头左右不动，只看图的位置
        bool scrolls = width > 1152f + 1f;
        bool moved = !scrolls || cams.Max() - cams.Min() > 100f;
        bool still = seen.All(p => p.DistanceTo(expected) < 0.5f);
        Check(parallax.ScrollScale == Vector2.One && moved && still,
            $"{id}：游戏里镜头在左中右（镜头中心 x {string.Join(" / ", cams.Select(c => c.ToString("0")))}{(scrolls ? "" : "，一屏宽的房间镜头不动")}；镜头边界 {Cam.LimitLeft}～{Cam.LimitRight}；玩家 {Player.GlobalPosition - Room.GlobalPosition:0}），地形图在世界里一直在 {expected - Room.GlobalPosition}（实际 {string.Join(" / ", seen.Select(p => (p - Room.GlobalPosition).ToString("0.#")))}），视差 {parallax.ScrollScale}");
    }
}
