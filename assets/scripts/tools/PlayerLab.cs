using Godot;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>
/// 新玩家测试场的启动脚本：重置本局进度并解锁指定能力（默认只有开局的格挡和切形态）。
/// 正式流程里能力由 Save / World 写入 GameSession.Progress，这里只是给测试场用。
/// </summary>
[GlobalClass]
public partial class PlayerLab : Node2D
{
    [Export] public string[] Abilities { get; set; } = { "block", "switch_form" };
    [Export] public bool ResetSession { get; set; } = true;

    public override void _EnterTree()
    {
        if (ResetSession) GameSession.Reset();
        foreach (string id in Abilities) GameSession.Progress.Unlock(id);
    }
}
