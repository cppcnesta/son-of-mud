using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Core;

namespace SonOfMud.World;

/// <summary>
/// 「从这里试玩」（计划_从这里试玩 第 2 版）：
/// - 编辑器里的按钮把请求写进一次性文件 <see cref="RequestPath"/>，然后启动试玩场景 <see cref="TrialScene"/>；
/// - 只有试玩场景里的 GameBoot 会读它（读完立刻删），正式场景 main_game.tscn 永远不看这个文件，
///   所以按钮启动失败留下的文件不会影响正常运行，只会被下一次按按钮覆盖。
/// - 补齐前面的进度：房间表的顺序就是主线顺序，把排在目标房间前面的所有房间里出口条件用到的变量设成 true，
///   再加上 seen.opening（当作开场看过了）。只补开门用的条件，不是完整的剧情进度。
/// </summary>
public static class DebugStart
{
    public const string RequestPath = "user://debug_start.json";
    public const string TrialScene = "res://assets/scenes/world/trial_game.tscn";
    public const string OpeningSeen = "seen.opening";

    /// <summary>补齐 target 之前的进度要设的变量（按房间表顺序，不重复）。房间表里没有 target 时为空。</summary>
    public static List<string> FillFlags(WorldLayout layout, string target)
    {
        var flags = new List<string>();
        if (layout?.Find(target) == null) return flags;
        foreach (RoomDef room in layout.Rooms)
        {
            if (room.Id == target) break;
            foreach (ExitDef exit in room.Exits.Where(e => e?.Requirement != null))
                foreach (StringName flag in exit.Requirement.Flags)
                    if (!flags.Contains(flag.ToString())) flags.Add(flag.ToString());
        }
        flags.Add(OpeningSeen);
        return flags;
    }

    public static void WriteRequest(string room, string spawn, bool fill)
    {
        var data = new Godot.Collections.Dictionary { ["room"] = room, ["spawn"] = spawn, ["fill"] = fill };
        using FileAccess file = FileAccess.Open(RequestPath, FileAccess.ModeFlags.Write);
        file?.StoreString(Json.Stringify(data));
    }

    public static bool HasRequestFile => FileAccess.FileExists(RequestPath);

    public static void DeleteRequestFile()
    {
        if (FileAccess.FileExists(RequestPath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RequestPath));
    }

    /// <summary>读请求并立刻删掉文件；没有文件、格式不对、房间不在房间表里时返回 null（error 说明原因）。</summary>
    public static TrialRequest Consume(WorldLayout layout, out string error)
    {
        error = "";
        if (!FileAccess.FileExists(RequestPath)) { error = "没有试玩请求（user://debug_start.json）"; return null; }
        string text = FileAccess.GetFileAsString(RequestPath);
        DeleteRequestFile();
        if (Json.ParseString(text).AsGodotDictionary() is not { } data || !data.ContainsKey("room")) { error = "试玩请求格式不对：" + text; return null; }
        string room = data["room"].AsString();
        if (layout?.Find(room) == null) { error = $"试玩请求的房间 {room} 不在房间表里"; return null; }
        var request = new TrialRequest
        {
            Room = room,
            Spawn = data.ContainsKey("spawn") && data["spawn"].AsString().Length > 0 ? data["spawn"].AsString() : SpawnPoint.DefaultId.ToString(),
            Fill = data.ContainsKey("fill") && data["fill"].AsBool(),
        };
        if (request.Fill) request.Flags = FillFlags(layout, room);
        return request;
    }
}
