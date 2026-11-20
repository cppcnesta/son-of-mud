using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 地上的收集品（交互层，按 E 拾取，和泥板一样）。敌人掉的由 CollectibleDropper 生成；小泥偶摆在 S1_05 场景里（VisibleWhen boss_keeper）。
/// - 已经捡过（collected.&lt;编号&gt; 为真）或 VisibleWhen 不满足：隐藏、不能交互。监听变量变化，所以同房间里同编号的另一份
///   在第一份被捡起的那一刻就隐藏（地上可能有两份：捡到之前每打死一只同种敌人都会掉）。
/// - 拾取：先再查一次有没有捡过；记 collected.&lt;编号&gt; = true（只写内存里的进度，存档时随存档落盘）；
///   请求 pickups.orph 里同名标签（对话框：名字、说明，小泥偶接着出台词）；有拾取音效就播；自己释放。
/// </summary>
[GlobalClass]
public partial class CollectiblePickup : Area2D, IInteractable
{
    /// <summary>地上显示的高度（和泥板一样）。</summary>
    public const float ArtHeight = 48f;
    public static readonly Vector2 Size = new(48f, 48f);

    [Export] public CollectibleDef Def { get; set; }
    [Export] public GateRequirement VisibleWhen { get; set; }

    public bool Available { get; private set; }
    /// <summary>这一份被拾取过（测试用）。</summary>
    public bool PickedUp { get; private set; }
    public string Prompt => "拾取" + (Def?.DisplayName ?? "");

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
        if (GetNodeOrNull<CollisionShape2D>("Shape") == null)
            AddChild(new CollisionShape2D { Name = "Shape", Position = new Vector2(0f, -Size.Y * 0.5f), Shape = new RectangleShape2D { Size = Size } });
        if (Def?.Art != null) ArtSprite.Attach(this, Def.Art, ArtHeight);
        Refresh();
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();

    public void Refresh()
    {
        Available = !PickedUp && Def != null && !string.IsNullOrEmpty(Def.Id) && !Def.Collected && GameSession.Progress.Meets(VisibleWhen, out _);
        Visible = Available;
        QueueRedraw();
    }

    public bool CanInteract(Node2D actor) => Available && Def != null && !Def.Collected;

    public void Interact(Node2D actor)
    {
        // 再查一次：同种的另一份可能刚被捡了
        if (!CanInteract(actor))
        {
            Refresh();
            return;
        }
        PickedUp = true;
        GameSession.Progress.SetVar(Def.CollectedKey, true);
        Refresh();
        if (Def.PickupSounds.Count > 0)
            Events.Instance?.EmitSignal(Events.SignalName.SoundRequested, (Godot.Collections.Array)Def.PickupSounds);
        Events.Instance?.EmitSignal(Events.SignalName.StoryRequested, CollectibleDef.StoryScript, Def.Id.ToString());
        QueueFree();
    }

    /// <summary>没有图时画一块色块（底边中点在原点）。</summary>
    public override void _Draw()
    {
        if (!Visible || GetNodeOrNull(ArtSprite.NodeName) != null) return;
        DrawRect(new Rect2(-Size.X * 0.5f, -Size.Y, Size.X, Size.Y), new Color(0.78f, 0.62f, 0.45f));
    }
}
