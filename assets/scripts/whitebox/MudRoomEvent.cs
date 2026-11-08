using Godot;

[GlobalClass]
public partial class MudRoomEvent : Node2D, IMudInteractable
{
    public string EventId { get; set; } = "";
    public string Title { get; set; } = "";
    public MudWhiteboxRoom Room { get; set; }
    public Color EventColor { get; set; } = new("c9aa6a");
    public bool Used { get; set; }
    public override void _Ready()
    {
        AddToGroup("interactables");
        var label = new Label
        {
            Text = Title, Position = new Vector2(-100, -100), Size = new Vector2(200, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", 17);
        AddChild(label);
    }
    public void Interact(MudPlayer actor) => Room.PerformEvent(EventId, this);
    public override void _Draw()
    {
        DrawRect(new Rect2(-18, -55, 36, 55), Used ? EventColor.Darkened(0.45f) : EventColor);
        DrawCircle(new Vector2(0, -62), 7, new Color("ffe9a4"));
    }
}
