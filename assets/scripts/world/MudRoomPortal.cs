using Godot;
using System.Collections.Generic;
using SonOfMud.Core;
using SonOfMud.Player;
using SonOfMud.Tools;

namespace SonOfMud.World;

[GlobalClass]
public partial class MudRoomPortal : Area2D, IMudInteractable
{
    [Export] public string InterfaceId { get; set; } = "";
    [Export(PropertyHint.File, "*.tscn")] public string TargetRoom { get; set; } = "";
    [Export] public StringName TargetSpawn { get; set; } = "";
    [Export] public string[] Requirements { get; set; } = System.Array.Empty<string>();
    [Export] public bool BreakFromThisSide { get; set; }
    [Export] public bool BidirectionalAfterBreak { get; set; } = true;
    [Export] public string ShortcutFlag { get; set; } = "";
    [Export] public bool Managed { get; set; }
    [Export] public bool DebugOutput { get; set; }
    public MudWhiteboxRoom Room { get; set; }
    public string Title { get; set; } = "";
    public Label Label { get; private set; }
    private static readonly Dictionary<string, string> AbilityNames = new()
    {
        ["double_jump"] = "二段跳", ["wall_climb"] = "攀墙", ["dive"] = "潜游", ["heavy_slam"] = "重压下砸"
    };
    private static readonly Dictionary<string, string> FlagReasons = new()
    {
        ["tutorial_double"] = "先完成二段跳教学", ["tutorial_wall"] = "先完成攀墙教学",
        ["flood_done"] = "先操作水闸并等待退水", ["boss_M05"] = "先击败守坑人",
        ["boss_T04"] = "先击败龙蛇泥人", ["seven_gates"] = "先完成七道门", ["met_igi"] = "先与伊吉交谈"
    };
    public override void _Ready()
    {
        AddToGroup("interactables");
        CollisionLayer = 0;
        CollisionMask = 2;
        Label = new Label
        {
            Position = new Vector2(-100, Position.Y < 350 ? -100 : -164), Size = new Vector2(200, 85),
            HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        Label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(Label);
        if (GetChildCount() == 1)
            AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(70, 110) }, Position = new Vector2(0, -30) });
    }
    public string LockReason()
    {
        var progress = MudProgress.Instance;
        if (Room != null && Room.BossActive) return "战斗封锁";
        foreach (var key in Requirements)
        {
            if (key.StartsWith("ability:"))
            {
                var ability = key.Substring("ability:".Length);
                if (!progress.HasAbility(ability)) return "需要" + AbilityNames.GetValueOrDefault(ability, "对应能力");
            }
            else if (key == "districts") { if (!progress.DistrictsComplete()) return "三个街区尚未完成"; }
            else if (!progress.Flag(key)) return FlagReasons.GetValueOrDefault(key, "先完成前置事件");
        }
        if (ShortcutFlag.Length > 0)
        {
            if (!progress.Flag(ShortcutFlag)) return BreakFromThisSide ? "从此侧破门：按E" : "另一侧封死";
            if (!BreakFromThisSide && !BidirectionalAfterBreak) return "单向出口";
        }
        return "";
    }
    public void Interact(MudPlayer actor)
    {
        if (MudRoomTravel.Instance.ArrivalLock > 0f) return;
        var reason = LockReason();
        if (ShortcutFlag.Length > 0 && !MudProgress.Instance.Flag(ShortcutFlag) && BreakFromThisSide && (Room == null || !Room.BossActive))
        {
            MudProgress.Instance.Mark(ShortcutFlag);
            Room?.ShowMessage("破门完成：捷径已永久开启，再按E通行。");
            return;
        }
        if (reason.Length > 0) { Room?.ShowMessage(reason); return; }
        MudRoomTravel.Instance.GoToRoom(TargetRoom, TargetSpawn);
    }
    public override void _Process(double delta)
    {
        if (Label != null)
        {
            var reason = LockReason();
            Label.Text = Title + (reason.Length == 0 ? "\n[E] 通行" : "\n" + reason);
        }
        QueueRedraw();
        if (!Managed && Input.IsActionJustPressed("interact"))
            foreach (var body in GetOverlappingBodies())
                if (body is MudPlayer player) Interact(player);
    }
    public override void _Draw()
    {
        var color = new Color(LockReason().Length == 0 ? "64c9b0" : "b87863");
        DrawRect(new Rect2(-28, -82, 56, 106), color.Darkened(0.65f));
        DrawRect(new Rect2(-28, -82, 56, 106), color, false, 4);
    }
}
