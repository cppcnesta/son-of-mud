using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 条件门：通道上的阻挡物。条件满足（GameSession.Progress.Meets）就打开：去掉碰撞并隐藏。
/// 收到 VariableChanged / AbilityUnlocked 时立即重新判断。含能力条件的出口靠地形挡、不用这个类（攀墙开局就有之后，目前没有能力门）。
/// 两种用法：出口上的门（名字 GateTo + 目标，条件跟房间表）；房间里面的门块（名字 Gate_字母，文字图的 @gate）。
/// 门体在 Surfaces.NoClimbGroup 分组里：不能攀爬、不能蹬墙跳（静态模拟器把门块也当不可攀）。
/// 锁着且有提示文字时，门四周各多出 <see cref="HintReach"/> 像素的交互区域：玩家靠近就显示提示（不带按键），门一开提示就消失。
/// </summary>
[GlobalClass]
public partial class GateBarrier : StaticBody2D
{
    public const string HintName = "Hint";
    public const float HintReach = 48f;
    public static readonly Color PlaceholderColor = new(0.75f, 0.2f, 0.2f, 0.85f);

    [Export] public GateRequirement Requirement { get; set; }

    public bool IsOpen { get; private set; }

    public string LockedText => Requirement?.LockedText ?? "";

    /// <summary>运行时建的提示区域（不存进场景）；没有提示文字时为 null。</summary>
    public GateHint Hint { get; private set; }

    public override void _EnterTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged += OnVariableChanged;
        Events.Instance.AbilityUnlocked += OnAbilityUnlocked;
    }

    public override void _ExitTree()
    {
        if (Events.Instance == null) return;
        Events.Instance.VariableChanged -= OnVariableChanged;
        Events.Instance.AbilityUnlocked -= OnAbilityUnlocked;
    }

    public override void _Ready()
    {
        AddToGroup(Surfaces.NoClimbGroup);
        if (!Engine.IsEditorHint() && LockedText.Length > 0 && GetNodeOrNull<CollisionShape2D>("Shape") is { Shape: RectangleShape2D rect } shape)
        {
            Hint = new GateHint(this) { Name = HintName, Position = shape.Position };
            // 四周各多出 HintReach：竖着的门站在旁边、地板上的活板门站在上面，都能碰到
            Hint.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size + new Vector2(HintReach * 2f, HintReach * 2f) } });
            AddChild(Hint);
        }
        Refresh();
    }

    private void OnVariableChanged(StringName key, Variant value) => Refresh();
    private void OnAbilityUnlocked(StringName abilityId) => Refresh();

    public void Refresh()
    {
        IsOpen = GameSession.Progress.Meets(Requirement, out _);
        CollisionLayer = IsOpen ? 0u : Layers.World;
        CollisionMask = 0;
        Visible = !IsOpen;
    }

    // ---------- 生成工具用 ----------

    public static string InnerName(char letter) => $"Gate_{letter}";

    /// <summary>门块的条件：一个变量 + 锁着时的提示。</summary>
    public static GateRequirement FlagRequirement(string flag, string text) =>
        new() { Flags = new Godot.Collections.Array<StringName> { flag }, LockedText = text ?? "" };

    public static GateRequirement CopyRequirement(GateRequirement source) => source == null ? null : new GateRequirement
    {
        Abilities = new Godot.Collections.Array<StringName>(source.Abilities),
        Flags = new Godot.Collections.Array<StringName>(source.Flags),
        LockedText = source.LockedText,
    };

    /// <summary>条件的比较用文字：能力 | 变量 | 提示。</summary>
    public static string Signature(GateRequirement req) =>
        req == null ? "（无条件）" : $"能力[{string.Join(",", req.Abilities)}] 变量[{string.Join(",", req.Flags)}] 提示“{req.LockedText}”";

    /// <summary>新建一道门：节点位置是门的底边中点，碰撞形状和占位色块从底边往上。</summary>
    public static GateBarrier Create(string name, Vector2 size, GateRequirement requirement)
    {
        var gate = new GateBarrier { Name = name, Requirement = requirement };
        gate.AddChild(new CollisionShape2D { Name = "Shape" });
        gate.AddChild(new ColorRect { Name = "Placeholder", MouseFilter = Control.MouseFilterEnum.Ignore, Color = PlaceholderColor });
        gate.Resize(size);
        return gate;
    }

    public bool HasSize(Vector2 size)
    {
        if (GetNodeOrNull<CollisionShape2D>("Shape") is not { } shape) return false;
        using Shape2D current = shape.Shape;
        return current is RectangleShape2D rect && rect.Size == size && shape.Position == new Vector2(0, -size.Y / 2f);
    }

    public void Resize(Vector2 size)
    {
        if (GetNodeOrNull<CollisionShape2D>("Shape") is { } shape)
        {
            shape.Shape = new RectangleShape2D { Size = size };
            shape.Position = new Vector2(0, -size.Y / 2f);
        }
        if (GetNodeOrNull<ColorRect>("Placeholder") is { } placeholder)
        {
            placeholder.OffsetLeft = -size.X / 2f;
            placeholder.OffsetRight = size.X / 2f;
            placeholder.OffsetTop = -size.Y;
            placeholder.OffsetBottom = 0f;
        }
    }

    /// <summary>门块在文字图里占的像素矩形（房间局部）：按碰撞形状算；没有矩形形状时为 null。</summary>
    public Rect2? LocalRect()
    {
        if (GetNodeOrNull<CollisionShape2D>("Shape") is not { } shape) return null;
        using Shape2D current = shape.Shape;
        if (current is not RectangleShape2D rect) return null;
        Vector2 center = Position + shape.Position;
        return new Rect2(center - rect.Size / 2f, rect.Size);
    }
}
