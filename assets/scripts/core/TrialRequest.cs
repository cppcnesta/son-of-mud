using System.Collections.Generic;

namespace SonOfMud.Core;

/// <summary>
/// 试玩（编辑器里房间检查器的「从这里试玩」）：这一局从哪个房间、哪个出生点开始，开局时补哪些剧情变量。
/// 只在试玩场景里由 GameBoot 读出来放进 GameSession.Trial；回主菜单、开局失败时清掉。存档服务看到它就按它开局、不写存档文件。
/// </summary>
public sealed class TrialRequest
{
    public string Room = "";
    public string Spawn = "Default";
    /// <summary>勾了“补齐前面的进度”。</summary>
    public bool Fill;
    /// <summary>开局时设成 true 的变量（补齐时按房间表算好；白纸时为空）。</summary>
    public List<string> Flags = new();

    public string Describe() => $"试玩：{Room} / {Spawn}（{(Fill ? "补齐进度" : "白纸")}）";
}
