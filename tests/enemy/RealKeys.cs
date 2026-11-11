using Godot;

/// <summary>
/// 测试用的真实按键：发 InputEventKey（和玩家按键盘一样经过 InputMap），不直接按动作。
/// 键位和 project.godot 一致：A / D 左右、空格跳、J 攻击、K 格挡、Q 切形态、E 交互、Shift 闪避、R 扔泥肝。
/// </summary>
public static class RealKeys
{
    public const Key Left = Key.A;
    public const Key Right = Key.D;
    public const Key Jump = Key.Space;
    public const Key Attack = Key.J;
    public const Key Block = Key.K;
    public const Key SwitchForm = Key.Q;
    public const Key Interact = Key.E;
    public const Key Dodge = Key.Shift;
    public const Key Throw = Key.R;

    private static readonly Key[] All = { Left, Right, Jump, Attack, Block, SwitchForm, Interact, Dodge, Throw };

    public static void Down(Key key) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });

    public static void Up(Key key) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });

    /// <summary>松开所有用到的键，再把所有动作复位（防止上一个测试留下按住的动作）。</summary>
    public static void ReleaseAll()
    {
        foreach (Key key in All) Up(key);
        foreach (StringName action in InputMap.GetActions()) Input.ActionRelease(action);
    }

    /// <summary>朝 direction（1 右、-1 左）走的那个键。</summary>
    public static Key Toward(float direction) => direction >= 0f ? Right : Left;
}
