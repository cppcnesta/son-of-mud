using Godot;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>
/// 按 F 发出 Events.DebugFlyToggled（玩家进入 / 退出调试飞行）。只放在测试场里。
/// 用按键状态的边沿检测，不依赖输入事件分发，无界面运行（测试）时同样有效。
/// </summary>
[GlobalClass]
public partial class DebugFlyToggle : Node
{
    [Export] public Key ToggleKey { get; set; } = Key.F;

    private bool _wasDown;

    public override void _PhysicsProcess(double delta)
    {
        bool down = Input.IsPhysicalKeyPressed(ToggleKey) || Input.IsKeyPressed(ToggleKey);
        if (down && !_wasDown) Events.Instance?.EmitSignal(Events.SignalName.DebugFlyToggled);
        _wasDown = down;
    }
}
