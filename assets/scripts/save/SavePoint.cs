using Godot;
using SonOfMud.Core;

namespace SonOfMud.Save;

/// <summary>存档点：按交互键发 Events.SaveRequested。复活位置是同一房间里名为 Save 的出生点（生成器放在存档点脚下）。</summary>
[GlobalClass]
public partial class SavePoint : Area2D, IInteractable
{
    [Export] public StringName SavePointId { get; set; } = "";
    [Export] public string PromptText { get; set; } = "存档";

    public string Prompt => PromptText;

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
    }

    public bool CanInteract(Node2D actor) => !string.IsNullOrEmpty(SavePointId.ToString());

    public void Interact(Node2D actor)
    {
        if (!CanInteract(actor)) return;
        Events.Instance?.EmitSignal(Events.SignalName.SaveRequested, SavePointId);
    }
}
