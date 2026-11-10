namespace SonOfMud.Player;

/// <summary>
/// 跳跃下落方式。ConstantDescent：水形态，过顶点后恒速下落；
/// HeavyFall：泥形态模组 1，按重力加速下落，受终端速度限制（D5，不做模组 2）。
/// </summary>
public enum JumpStyle
{
    ConstantDescent,
    HeavyFall,
}
