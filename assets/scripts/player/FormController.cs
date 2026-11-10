using Godot;

namespace SonOfMud.Player;

/// <summary>
/// 水 / 泥形态。切换只改形态本身：不刷新任何冷却、不解除减速、不重复发放增伤（ME11）。
/// 能不能切由当前状态（PlayerState.CanSwitchForm）和这里的冷却、能力、空中规则共同决定。
/// </summary>
[GlobalClass]
public partial class FormController : Node
{
    public const int Water = 0;
    public const int Mud = 1;

    [Signal] public delegate void FormChangedEventHandler(int form);

    [Export] public FormConfig WaterConfig { get; set; }
    [Export] public FormConfig MudConfig { get; set; }

    public int Form { get; private set; }
    public FormConfig Current => Form == Mud ? MudConfig : WaterConfig;
    public FormConfig Get(int form) => form == Mud ? MudConfig : WaterConfig;

    /// <summary>直接设定形态（初始化、复位、测试用），不检查冷却。</summary>
    public void SetForm(int form)
    {
        int next = form == Mud ? Mud : Water;
        if (next == Form) return;
        Form = next;
        EmitSignal(SignalName.FormChanged, Form);
    }

    /// <summary>按规则尝试切换。stateAllows 由当前状态给出。</summary>
    public bool TrySwitch(PlayerCharacter player, bool stateAllows)
    {
        if (!stateAllows || !player.HasAbility(PlayerAbilities.SwitchForm)) return false;
        if (!player.Timers.SwitchReady) return false;
        if (!player.IsOnFloor() && !player.Rules.AllowAirSwitch) return false;
        player.Timers.SwitchCooldown = player.Rules.SwitchCooldown;
        SetForm(1 - Form);
        return true;
    }
}
