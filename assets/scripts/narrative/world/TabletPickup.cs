using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.World;

/// <summary>
/// 世界里可以拾取的泥板（交互层，按 E）：发 StoryRequested(Script, Label)，那个标签里写 [泥板 id]。
/// Label 为空时用 TabletId（tablets.orph 里每块泥板一个同名标签）。
/// 已经拾取过（变量 tablet.&lt;id&gt; 为 true，随存档保存）就隐藏、不能交互；VisibleWhen 不满足时也隐藏。
/// </summary>
[GlobalClass]
public partial class TabletPickup : Area2D, IInteractable
{
    [Export] public string TabletId { get; set; } = "";
    [Export] public string ScriptId { get; set; } = "tablets";
    [Export] public string Label { get; set; } = "";
    [Export] public string PromptText { get; set; } = "拾取泥板";
    [Export] public GateRequirement VisibleWhen { get; set; }

    public string CollectedKey => "tablet." + TabletId;
    public string LabelOrId => string.IsNullOrEmpty(Label) ? TabletId : Label;
    public string Prompt => PromptText;
    public bool Available { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance != null) Events.Instance.VariableChanged += OnVariableChanged;
    }

    public override void _ExitTree()
    {
        if (Events.Instance != null) Events.Instance.VariableChanged -= OnVariableChanged;
    }

    /// <summary>泥板图显示的高度（像素）。</summary>
    public const float ArtHeight = 48f;

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
        ArtSprite.Attach(this, ArtSprite.Load(ArtSprite.CollectibleDirectory, ArtName(TabletId, Owner?.Name ?? "")), ArtHeight);
        Refresh();
    }

    /// <summary>泥板用哪张图：命运石板单独一张；其他按所在地图（房间名开头 S1～S4）用对应的残泥板。</summary>
    public static string ArtName(string tabletId, string roomName)
    {
        if (tabletId == "destiny") return "spr_collectible_tablet_of_destiny";
        string map = roomName.Length >= 2 ? roomName.Substring(0, 2) : "";
        return map switch
        {
            "S1" => "spr_collectible_tablet_pit",
            "S2" => "spr_collectible_tablet_temple",
            "S3" => "spr_collectible_tablet_divination",
            "S4" => "spr_collectible_tablet_summit",
            _ => "spr_collectible_tablet_pit",
        };
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();

    public void Refresh()
    {
        Available = !string.IsNullOrEmpty(TabletId) && !GameSession.Progress.Flag(CollectedKey) && GameSession.Progress.Meets(VisibleWhen, out _);
        Visible = Available;
    }

    public bool CanInteract(Node2D actor) => Available;

    public void Interact(Node2D actor)
    {
        if (!CanInteract(actor)) return;
        Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, ScriptId, LabelOrId);
    }
}
