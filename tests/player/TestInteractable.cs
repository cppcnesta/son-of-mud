using Godot;
using SonOfMud.Core;

/// <summary>测试用的可交互对象：放在交互层上，记录被交互的次数。</summary>
public partial class TestInteractable : Area2D, IInteractable
{
    public string PromptText { get; set; } = "测试";
    public bool Enabled { get; set; } = true;
    public int Count { get; private set; }

    public string Prompt => PromptText;

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
        if (GetChildCount() == 0)
            AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(32, 32) } });
    }

    public bool CanInteract(Node2D actor) => Enabled;

    public void Interact(Node2D actor)
    {
        if (CanInteract(actor)) Count++;
    }
}
