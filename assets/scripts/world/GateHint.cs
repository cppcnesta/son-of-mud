using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>锁着的门旁边的提示区域：交互层上的 Area2D，门锁着时显示门的提示文字（不带按键），按交互键没有效果。</summary>
public partial class GateHint : Area2D, IInteractable
{
    public GateBarrier Gate { get; }

    public GateHint() { }
    public GateHint(GateBarrier gate) => Gate = gate;

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
    }

    public string Prompt => Gate?.LockedText ?? "";
    public bool ShowsKey => false;
    public bool CanInteract(Node2D actor) => Gate != null && !Gate.IsOpen && Prompt.Length > 0;
    public void Interact(Node2D actor) { }
}
