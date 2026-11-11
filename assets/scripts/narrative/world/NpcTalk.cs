using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.World;

/// <summary>
/// 可以对话的 NPC（交互层，按 E）。VisibleWhen 不满足时隐藏且不能交互（比如守坑人老人在 boss_keeper 之后才出现）。
/// 第一次对话播 FirstLabel；变量 talked.&lt;NpcId&gt; 为 true 之后播 RepeatLabel（为空时仍播 FirstLabel）。
/// talked 变量由剧本在第一次对话结尾写（[设置 talked.keeper 是]），这个节点只读。
/// </summary>
[GlobalClass]
public partial class NpcTalk : Area2D, IInteractable
{
    [Export] public string NpcId { get; set; } = "";
    [Export] public string ScriptId { get; set; } = "";
    [Export] public string FirstLabel { get; set; } = "";
    [Export] public string RepeatLabel { get; set; } = "";
    [Export] public string PromptText { get; set; } = "交谈";
    [Export] public GateRequirement VisibleWhen { get; set; }

    public string TalkedKey => $"talked.{NpcId}";
    public string Prompt => PromptText;
    public bool Present { get; private set; }

    /// <summary>现在按 E 会播哪个标签。</summary>
    public string NextLabel =>
        GameSession.Progress.Flag(TalkedKey) && !string.IsNullOrEmpty(RepeatLabel) ? RepeatLabel : FirstLabel;

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
        Present = GameSession.Progress.Meets(VisibleWhen, out _);
        Visible = Present;
    }

    public bool CanInteract(Node2D actor) => Present && !string.IsNullOrEmpty(ScriptId) && !string.IsNullOrEmpty(FirstLabel);

    public void Interact(Node2D actor)
    {
        if (!CanInteract(actor)) return;
        Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, ScriptId, NextLabel);
    }
}
