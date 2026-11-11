using Godot;
using SonOfMud.Core;
using SonOfMud.World;

namespace SonOfMud.Tools;

public partial class MudWhiteboxGeometry : Node2D
{
    public MudWhiteboxRoom Room { get; set; }
    public StaticBody2D Solid(Vector2 at, Vector2 dimensions, Color? color = null)
    {
        var body = new StaticBody2D { Position = at, CollisionLayer = 1, CollisionMask = 2 };
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = dimensions } });
        var half = dimensions * 0.5f;
        body.AddChild(new Polygon2D
        {
            Polygon = new[] { new Vector2(-half.X, -half.Y), new Vector2(half.X, -half.Y), half, new Vector2(-half.X, half.Y) },
            Color = color ?? new Color("3f4c52")
        });
        AddChild(body);
        Room.FloorBlocks.Add(new Rect2(at - half, dimensions));
        return body;
    }
    public void Platform(float x, float y, float width, bool oneWay = false)
    {
        var body = Solid(new Vector2(x, y + 10f), new Vector2(width, 20), new Color("59686b"));
        if (oneWay) ((CollisionShape2D)body.GetChild(0)).OneWayCollision = true;
    }
    public void SignAt(string text, Vector2 at, float width = 360f)
    {
        var label = new Label { Text = text, Position = at, Size = new Vector2(width, 100), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 19);
        label.AddThemeColorOverride("font_color", new Color("b9cbc9"));
        AddChild(label);
    }
    public void BuildGeometry()
    {
        var size = Room.RoomSize;
        var floor = Room.FloorY;
        if (Room.RoomId == "C04")
        {
            var left = size.X * 0.48f;
            var right = size.X * 0.75f;
            var surface = floor - 200f;
            Solid(new Vector2(left * 0.5f, surface + 150), new Vector2(left, 300));
            Solid(new Vector2((right + size.X) * 0.5f, surface + 150), new Vector2(size.X - right, 300));
            Solid(new Vector2((left + right) * 0.5f, floor + 32), new Vector2(right - left, 64));
            AddWater(new Rect2(left, surface - 20, right - left, floor - surface + 20), true);
            foreach (var value in Room.Data.Array("ports"))
            {
                var port = value.AsGodotDictionary();
                port.Array("position")[1] = surface - 24f;
                port.Array("arrival")[1] = surface - 24f;
            }
            var start = Room.Spawns.GetNode<Marker2D>("Start");
            start.Position = new Vector2(start.Position.X, surface - 24f);
            foreach (var value in Room.Data.Array("events"))
            {
                var entry = value.AsGodotDictionary();
                if (entry.Str("id") != "omen_1") entry.Array("position")[1] = surface - 24f;
            }
        }
        else Solid(new Vector2(size.X * 0.5f, floor + 32), new Vector2(size.X, 64));
        Solid(new Vector2(0, size.Y * 0.5f), new Vector2(32, size.Y));
        Solid(new Vector2(size.X, size.Y * 0.5f), new Vector2(32, size.Y));
        Solid(new Vector2(size.X * 0.5f, 0), new Vector2(size.X, 32));
        switch (Room.RoomId)
        {
            case "M01":
                SignAt("泥坑\n按Q切换形态\n按K格挡，按J反击", new Vector2(64, 220));
                Room.SpawnEnemy(new Vector2(size.X * 0.9f, floor - 32), "格挡训练", "", 200); break;
            case "M02":
                AddWater(new Rect2(size.X * 0.55f, floor - 45, size.X * 0.32f, 45), false);
                SignAt("两河\n暗红色的水流入泥坑", new Vector2(size.X * 0.5f, floor - 220));
                Platform(size.X * 0.35f, floor - 192, 256); break;
            case "M03":
                foreach (var x in new[] { 1100, 2200 }) Room.SpawnEnemy(new Vector2(x, floor - 32), "朝圣者", "", 45).Passive = true;
                break;
            case "M06": SignAt("两河源头\n远处传来歌声", new Vector2(128, 180)); break;
            case "C01": Room.SpawnEnemy(new Vector2(size.X * 0.62f, floor - 32), "塔姆图之子·守卫", "", 60); break;
            case "T01":
                Platform(size.X - 189, floor - 192, 378);
                Solid(new Vector2(size.X - 390, floor - 96), new Vector2(24, 192));
                SignAt("高台通往壁画长廊\n空中再次按下跳跃", new Vector2(size.X - 600, floor - 380)); break;
            case "T02":
                Solid(new Vector2(size.X - 360, floor - 224), new Vector2(32, 448));
                Platform(size.X - 176, floor - 448, 320);
                SignAt("可攀墙面 →\n贴墙+朝墙方向+W", new Vector2(size.X - 660, floor - 330)); break;
            case "T03": AddWater(new Rect2(size.X * 0.4f, floor - 64, size.X * 0.4f, 64), false); break;
            case "T04":
                Platform(300, floor - 192, 220); Platform(size.X - 300, floor - 192, 220);
                Solid(new Vector2(130, floor - 220), new Vector2(32, 440));
                Solid(new Vector2(size.X - 130, floor - 220), new Vector2(32, 440)); break;
            case "T05":
                BuildStairs(320, floor - 96, 96);
                SignAt("上层通往城市\n沿阶梯上行", new Vector2(300, 250), 700); break;
            case "C05":
                var center = Mathf.Round(size.X * 0.58f / 64f) * 64f;
                Platform(center - 200, floor - 160, 160); Platform(center + 200, floor - 160, 160);
                Room.TrialBreakable = AddBreakable(new Vector2(center, floor - 110), new Vector2(260, 32), "obstacle_C05");
                SignAt("黄色裂板：空中S+J重压下砸\n征兆需要先破坏裂板，普通攻击无效", new Vector2(center - 320, floor - 350), 640); break;
            case "H01":
                BuildStairs(320, floor - 96, 96);
                foreach (var value in Room.Data.Array("events"))
                {
                    var point = value.AsGodotDictionary().Point("position");
                    Platform(point.X, point.Y + 24f, 384, true);
                }
                SignAt("七道门\n往上走的人，什么也不能带", new Vector2(64, floor - 360), 600); break;
            case "H02":
                Platform(size.X * 0.55f, floor - 192, 220);
                AddWater(new Rect2(size.X * 0.65f, floor - 50, 250, 50), false);
                AddBreakable(new Vector2(size.X * 0.5f, floor - 30), new Vector2(100, 40), "obstacle_H02"); break;
            case "TEST": BuildLab(); break;
        }
    }
    public void BuildStairs(float top, float bottom, float step)
    {
        var index = 0;
        for (var y = bottom; y >= top; y -= step, index++)
        {
            var wave = index % 10;
            var fraction = (wave <= 5 ? wave : 10 - wave) / 5f;
            Platform(Mathf.Lerp(300f, Room.RoomSize.X - 300f, fraction), y, 384, true);
        }
        Platform(Room.RoomSize.X - 230, top + 24, 420, true);
        Platform(Room.RoomSize.X * 0.5f, top + 120, Room.RoomSize.X - 128, true);
    }
    public void AddWater(Rect2 rect, bool gated)
    {
        Room.WaterRects.Add(rect);
        if (gated)
            Room.WaterSolids.Add(Solid(new Vector2(rect.GetCenter().X, rect.Position.Y + 8), new Vector2(rect.Size.X, 16), new Color(0.3f, 0.6f, 0.7f, 0.25f)));
    }
    public MudBreakable AddBreakable(Vector2 at, Vector2 dimensions, string flagKey)
    {
        var progress = MudProgress.Instance;
        if (!progress.TestMode && progress.Flag(flagKey)) return null;
        var block = new MudBreakable { Position = at, Dimensions = dimensions, FlagKey = progress.TestMode ? "" : flagKey };
        AddChild(block);
        return block;
    }
    public void BuildLab()
    {
        var floor = Room.FloorY;
        SignAt("训练场\nB调整跳跃，F6开放训练能力\n沿地面向右前往格挡与武器训练区", new Vector2(100, 220), 650);
        Room.SourceX = 1450;
        Platform(2360, floor - 192, 220);
        Solid(new Vector2(2500, floor - 230), new Vector2(32, 460));
        Room.TrialBreakable = AddBreakable(new Vector2(2720, floor - 70), new Vector2(150, 32), "");
        AddWater(new Rect2(2860, floor - 125, 320, 125), false);
        Room.SpawnEnemy(new Vector2(1940, floor - 32), "训练靶", "dummy", 400);
        foreach (var height in new[] { 64, 128, 176, 192, 224, 256, 384 })
        {
            SignAt($"{height}px {(height == 192 ? "单跳门控" : "")}", new Vector2(2080, floor - height - 26), 260);
            Solid(new Vector2(2060, floor - height), new Vector2(100, 2), new Color("749195"));
        }
    }
}
