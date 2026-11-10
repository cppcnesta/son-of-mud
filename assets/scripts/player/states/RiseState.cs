namespace SonOfMud.Player.States;

/// <summary>上升。起跳参数已在起跳那一刻冻结，空中切形态不会改变轨迹。</summary>
public partial class RiseState : AirState
{
    public override string AnimName => "rise";
    public override string DisplayName => "上升";
}
