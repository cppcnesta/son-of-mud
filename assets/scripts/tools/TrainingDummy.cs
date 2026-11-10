using System.Collections.Generic;
using Godot;
using SonOfMud.Combat;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>
/// 训练假人：子节点里的 HurtBox（可以有多个，共用一份去重记录）、Stats、StaggerWindow，以及一个可选的 HitBox。
/// Strike() 用 StrikeSpec 向自己的 HitBox 范围出一刀（用来打玩家：格挡、眩晕、受伤测试）；
/// 出的刀被格挡且带 CounterOnBlock 时进入反制僵直（StaggerWindow.Begin）。
/// 只依赖 Core 和 Combat，不认识 Player。
/// </summary>
[GlobalClass]
public partial class TrainingDummy : Node2D
{
    [Signal] public delegate void StrikeResolvedEventHandler(int outcome);

    [Export] public HitSpec StrikeSpec { get; set; }
    [Export] public Color Tint { get; set; } = new("c86464");

    public Stats Stats { get; private set; }
    public StaggerWindow Stagger { get; private set; }
    public HitBox HitBox { get; private set; }
    public List<HurtBox> HurtBoxes { get; } = new();
    public List<HitOutcome> StrikeOutcomes { get; } = new();
    public int DamageEvents { get; private set; }
    public int LastDealt { get; private set; }

    private int _sequence;

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            switch (child)
            {
                case Stats s: Stats = s; break;
                case StaggerWindow w: Stagger = w; break;
                case HitBox h: HitBox = h; break;
                case HurtBox hb:
                    // 假人直接摆在测试场里（不是独立场景），Owner 是测试场根节点，所以显式指定实体根。
                    hb.EntityRoot ??= this;
                    HurtBoxes.Add(hb);
                    break;
            }
        }
        foreach (HurtBox hb in HurtBoxes)
            hb.Damaged += (hit, dealt) => { DamageEvents++; LastDealt = dealt; QueueRedraw(); };
        if (HitBox != null) HitBox.HitLanded += OnStrikeLanded;
        QueueRedraw();
    }

    /// <summary>出一刀：激活 HitBox 并立即查询一次，然后关闭。返回本次的攻击 ID。</summary>
    public string Strike(HitSpec spec = null)
    {
        if (HitBox == null) return "";
        string id = $"dummy_{GetInstanceId()}_{++_sequence}";
        HitBox.Activate(Hit.From(spec ?? StrikeSpec, id, GlobalPosition, 1f, this));
        HitBox.Deactivate();
        return id;
    }

    private void OnStrikeLanded(HurtBox target, Hit hit, int outcomeValue)
    {
        var outcome = (HitOutcome)outcomeValue;
        StrikeOutcomes.Add(outcome);
        if (outcome == HitOutcome.Blocked && hit.CounterOnBlock) Stagger?.Begin();
        EmitSignal(SignalName.StrikeResolved, outcomeValue);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta) => QueueRedraw();

    public override void _Draw()
    {
        Color color = Stagger is { Active: true } ? new Color("ffd166") : Tint;
        DrawRect(new Rect2(-24, -48, 48, 96), color);
        if (Stats != null) DrawString(ThemeDB.FallbackFont, new Vector2(-24, -56), $"{Stats.Health}");
    }
}
