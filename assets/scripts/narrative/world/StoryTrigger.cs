using Godot;
using SonOfMud.Core;

namespace SonOfMud.Narrative.World;

/// <summary>
/// 摆在房间里的剧情触发器：发 Events.StoryRequested(Script, Label)。
/// - Interact：放在交互层（Layers.Interact），玩家的交互组件按 E 时调用 Interact（D14）；
/// - Enter：检测玩家身体层（Layers.PlayerBody），身体进入区域时触发。
/// 条件：Requirement 不满足、HideFlag 为 true、或 Once 且已经看过（seen.&lt;剧本&gt;.&lt;标签&gt;，剧情层播完后写）时不触发，并隐藏占位外观。
/// 触发器本身不写任何进度变量；没有剧情层时（测试容器 game.tscn）只是发一个没人接的事件。
/// </summary>
[GlobalClass]
public partial class StoryTrigger : Area2D, IInteractable
{
    [Export] public string ScriptId { get; set; } = "";
    [Export] public string Label { get; set; } = "";
    [Export] public TriggerMode Mode { get; set; } = TriggerMode.Interact;
    [Export] public string PromptText { get; set; } = "查看";
    /// <summary>只播一次：这段剧情播完后（seen 变量为 true）不再触发。</summary>
    [Export] public bool Once { get; set; }
    /// <summary>这个变量为 true 后不再触发（比如吃下面包后供桌不能再互动）。空表示不用。</summary>
    [Export] public StringName HideFlag { get; set; } = "";
    /// <summary>要满足的条件（能力 / 变量）；为空表示没有条件。</summary>
    [Export] public GateRequirement Requirement { get; set; }

    public string SeenKey => SeenKeyFor(ScriptId, Label);
    public static string SeenKeyFor(string script, string label) => $"seen.{script}.{label}";

    public string Prompt => PromptText;
    public bool Active { get; private set; }
    public int Requests { get; private set; }

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
        if (Mode == TriggerMode.Interact)
        {
            CollisionLayer = Layers.Interact;
            CollisionMask = 0;
            Monitoring = false;
        }
        else
        {
            CollisionLayer = 0;
            CollisionMask = Layers.PlayerBody;
            Monitoring = true;
            Monitorable = false;
            BodyEntered += OnBodyEntered;
        }
        Refresh();
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();

    public void Refresh()
    {
        ProgressStore progress = GameSession.Progress;
        Active = !string.IsNullOrEmpty(ScriptId)
                 && !(Once && progress.Flag(SeenKey))
                 && !(!string.IsNullOrEmpty(HideFlag.ToString()) && progress.Flag(HideFlag))
                 && progress.Meets(Requirement, out _);
        Visible = Active;
    }

    public bool CanInteract(Node2D actor) => Mode == TriggerMode.Interact && Active;

    public void Interact(Node2D actor)
    {
        if (CanInteract(actor)) Request();
    }

    private void OnBodyEntered(Node2D body)
    {
        if (Mode == TriggerMode.Enter && Active) Request();
    }

    private void Request()
    {
        Requests++;
        Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, ScriptId, Label);
    }
}
