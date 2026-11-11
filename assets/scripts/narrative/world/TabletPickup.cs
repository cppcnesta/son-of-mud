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

    public override void _Ready()
    {
        CollisionLayer = Layers.Interact;
        CollisionMask = 0;
        Monitoring = false;
        Refresh();
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
