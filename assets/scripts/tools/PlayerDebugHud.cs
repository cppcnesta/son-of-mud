using Godot;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>
/// 新玩家的调试 HUD：显示状态、正在播放的动画、形态、血量。
/// 不引用 Player 类：通过「player」组找到玩家，读它的 Core.StateMachine 和 AnimatedSprite2D，血量和形态听 Events。
/// </summary>
[GlobalClass]
public partial class PlayerDebugHud : CanvasLayer
{
    public Label Status { get; private set; }
    public string StateName { get; private set; } = "";
    public string StateAnim { get; private set; } = "";
    public string PlayingAnimation { get; private set; } = "";
    public int Health { get; private set; }
    public int MaxHealth { get; private set; }
    public int Form { get; private set; }

    public override void _Ready()
    {
        Layer = 10;
        Status = new Label { Position = new Vector2(16, 12), Size = new Vector2(1100, 80), MouseFilter = Control.MouseFilterEnum.Ignore };
        Status.AddThemeFontSizeOverride("font_size", 16);
        AddChild(Status);
        if (Events.Instance is { } events)
        {
            events.PlayerHealthChanged += OnHealth;
            events.PlayerFormChanged += OnForm;
        }
    }

    public override void _ExitTree()
    {
        if (Events.Instance is not { } events) return;
        events.PlayerHealthChanged -= OnHealth;
        events.PlayerFormChanged -= OnForm;
    }

    private void OnHealth(int current, int max) { Health = current; MaxHealth = max; }
    private void OnForm(int form) => Form = form;

    public override void _Process(double delta) => Refresh();

    public void Refresh()
    {
        Node player = GetTree().GetFirstNodeInGroup("player");
        if (player == null) return;
        var machine = player.GetNodeOrNull<StateMachine>("StateMachine");
        var sprite = player.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
        StateName = machine?.Current?.DisplayName ?? "";
        StateAnim = machine?.Current?.AnimName ?? "";
        PlayingAnimation = sprite?.Animation.ToString() ?? "";
        Status.Text = $"{(Form == 1 ? "泥" : "水")}形态　{StateName}　动画 {PlayingAnimation}　生命 {Health}/{MaxHealth}\n" +
            "A/D 移动　空格跳　W+方向 攀墙　Q 形态　Shift 闪避　J 攻击　S+J 下劈/下砸　K 格挡　F 调试飞行";
    }
}
