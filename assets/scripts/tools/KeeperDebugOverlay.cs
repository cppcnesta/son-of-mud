using Godot;
using SonOfMud.Enemy.Bosses.Keeper;

namespace SonOfMud.Tools;

/// <summary>
/// 守坑人测试场（keeper_lab.tscn）的调试叠层：显示状态、血量、两个技能的冷却（已充 / 总长，施法中标出来）和反制僵直剩余时间。
/// 只给测试场用（D12：Tools 可以依赖任何系统，正式代码不依赖 Tools）。
/// </summary>
[GlobalClass]
public partial class KeeperDebugOverlay : CanvasLayer
{
    [Export] public KeeperBoss Keeper { get; set; }

    public Label Text { get; private set; }

    public override void _Ready()
    {
        Layer = 50;
        Text = new Label { Name = "Text", Position = new Vector2(16f, 100f) };
        Text.AddThemeFontSizeOverride("font_size", 18);
        AddChild(Text);
    }

    public override void _Process(double delta)
    {
        if (Text == null) return;
        if (!IsInstanceValid(Keeper))
        {
            Text.Text = "守坑人：无";
            return;
        }
        Text.Text = Describe(Keeper);
    }

    public static string Describe(KeeperBoss k)
    {
        string state = k.State?.DisplayName ?? "-";
        string sweep = k.Sweep == null ? "-" : $"{k.Sweep.Charged:F1}/{k.Sweep.Duration:F0}{(k.Sweep.IsCasting ? "（施法中）" : "")}";
        string tail = k.Tail == null ? "-" : $"{k.Tail.Charged:F1}/{k.Tail.Duration:F0}{(k.Tail.IsCasting ? "（施法中）" : "")}";
        string stagger = k.Stagger is { Active: true } ? $"  僵直剩 {k.Stagger.Remaining:F1}s" : "";
        return $"守坑人 [{state}]  血 {k.Stats?.Health}/{k.Stats?.MaxHealth}\n横扫 CD {sweep}\n蝎尾 CD {tail}{stagger}";
    }
}
