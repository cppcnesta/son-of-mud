using Godot;
using SonOfMud.Core;

namespace SonOfMud.Tools;

/// <summary>
/// 新玩家测试场的启动脚本：重置本局进度，写入和新游戏相同的初始能力（正式能力表 ability_table.tres 的 Initial），
/// 再额外解锁 Abilities 里列出的能力（默认没有）。
/// 正式流程里能力由 Save / World 写入 GameSession.Progress，这里只是给测试场用。
/// </summary>
[GlobalClass]
public partial class PlayerLab : Node2D
{
    [Export] public string[] Abilities { get; set; } = System.Array.Empty<string>();
    [Export] public bool ResetSession { get; set; } = true;

    public override void _EnterTree()
    {
        if (ResetSession) GameSession.Reset(GD.Load<AbilityTable>(AbilityTable.DefaultPath));
        foreach (string id in Abilities) GameSession.Progress.Unlock(id);
    }
}
