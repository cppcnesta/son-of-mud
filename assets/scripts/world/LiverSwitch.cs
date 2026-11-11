using Godot;
using Godot.Collections;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 泥肝开关（通用组件，摆在房间的 Objects 下）：被玩家扔的泥肝打中就触发——把进度旗标 Flag 写成 true（存档保留），
/// 关联的门（Gates，GateBarrier）随之打开。
/// - 只认玩家的泥肝：受击框在敌人受击层（只有玩家方的攻击查得到它），并且 RequiredTag = player_liver，
///   玩家近战（不带这个标签）打它是 Ignored；敌人的投掷物只查玩家受击层，根本碰不到它。
/// - 门跟着进度走（GateBarrier 自己听 VariableChanged）：关联的门没配条件时，这里替它配上「需要 Flag」；
///   配了条件却不含 Flag 时报错（摆放错误）。
/// - 状态不自己记：读档 / 重新进房间时按 GameSession.Progress.Flag(Flag) 显示已触发，门也一开始就是开的。
/// 原点在脚底中心（和拾取物一样），判定框是 Size 大小的矩形。
/// </summary>
[GlobalClass]
public partial class LiverSwitch : Node2D
{
    public const string DefaultTag = "player_liver";

    [Signal] public delegate void ActivatedEventHandler();

    /// <summary>触发时写入的进度旗标（bool）。必须填，全局唯一。</summary>
    [Export] public StringName Flag { get; set; } = "";
    /// <summary>打中后打开的门。</summary>
    [Export] public Array<GateBarrier> Gates { get; set; } = new();
    /// <summary>只认带这个标签的攻击（玩家泥肝的 HitSpec 带 player_liver）。</summary>
    [Export] public string RequiredTag { get; set; } = DefaultTag;
    [Export] public Vector2 Size { get; set; } = new(36f, 56f);
    [Export] public Color IdleColor { get; set; } = new(0.62f, 0.5f, 0.3f);
    [Export] public Color TriggeredColor { get; set; } = new(0.55f, 0.2f, 0.16f);

    public HurtBox HurtBox { get; private set; }
    /// <summary>本实例里被泥肝触发的次数（已经触发过之后再打中不算）。</summary>
    public int Activations { get; private set; }
    public bool Triggered => !IsFlagEmpty && GameSession.Progress.Flag(Flag);

    private bool IsFlagEmpty => Flag == null || Flag.IsEmpty;

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
        if (IsFlagEmpty) GD.PushError($"LiverSwitch（{GetPath()}）没有填 Flag。");
        HurtBox = FindHurtBox() ?? CreateHurtBox();
        HurtBox.RequiredTag = RequiredTag;
        HurtBox.Damaged += OnStruck;
        foreach (GateBarrier gate in Gates)
        {
            if (gate == null || !IsInstanceValid(gate)) continue;
            if (gate.Requirement == null)
                gate.Requirement = new GateRequirement { Flags = new Array<StringName> { Flag } };
            else if (!gate.Requirement.Flags.Contains(Flag))
                GD.PushError($"LiverSwitch（{GetPath()}）关联的门 {gate.Name} 的条件里没有 {Flag}，打中开关也开不了它。");
            gate.Refresh();
        }
        QueueRedraw();
    }

    private HurtBox FindHurtBox()
    {
        foreach (Node child in GetChildren())
            if (child is HurtBox box) return box;
        return null;
    }

    private HurtBox CreateHurtBox()
    {
        var box = new HurtBox { Name = "HurtBox", CollisionLayer = Layers.EnemyHurt, CollisionMask = 0 };
        box.AddChild(new CollisionShape2D { Position = new Vector2(0f, -Size.Y * 0.5f), Shape = new RectangleShape2D { Size = Size } });
        AddChild(box);
        return box;
    }

    private void OnStruck(Hit hit, int dealt)
    {
        if (Triggered || IsFlagEmpty) return;
        Activations++;
        GameSession.Progress.SetVar(Flag, true);
        foreach (GateBarrier gate in Gates)
            if (gate != null && IsInstanceValid(gate)) gate.Refresh();
        EmitSignal(SignalName.Activated);
    }

    private void OnVariableChanged(StringName key, Variant value)
    {
        if (key == Flag) QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(-Size.X * 0.5f, -Size.Y, Size.X, Size.Y);
        DrawRect(rect, Triggered ? TriggeredColor : IdleColor);
        DrawRect(rect, new Color(0.15f, 0.1f, 0.06f), false, 2f);
    }
}
