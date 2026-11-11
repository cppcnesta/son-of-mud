using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 按键过门（目前只有 S3_08 ↔ S4_01）。交给所属 WorldGame 的 TeleportAsync 执行；
/// 正在换房时 CanInteract 返回 false，重复按键不会启动第二次传送。
/// </summary>
[GlobalClass]
public partial class RoomDoor : Area2D, IInteractable
{
    [Export] public string TargetRoom { get; set; } = "";
    [Export] public StringName TargetSpawn { get; set; } = "Default";
    [Export] public string PromptText { get; set; } = "进入";

    public string Prompt => PromptText;

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
    }

    public bool CanInteract(Node2D actor)
    {
        WorldGame game = WorldGame.FindFrom(this);
        return game != null && !game.IsTransitioning && !string.IsNullOrEmpty(TargetRoom);
    }

    public void Interact(Node2D actor)
    {
        if (!CanInteract(actor)) return;
        _ = WorldGame.FindFrom(this).TeleportAsync(TargetRoom, TargetSpawn);
    }
}
