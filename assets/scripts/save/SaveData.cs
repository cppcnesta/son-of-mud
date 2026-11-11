using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Save;

/// <summary>
/// 存档格式 v2：{version, metsys, progress, world{room, savePoint}, meta}，用 VarToStr 写成文本。
/// 写盘先写临时文件再改名，写到一半断电也不会留下半个存档。
/// </summary>
public sealed class SaveData
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public GDictionary MetSys { get; set; } = new();
    public GDictionary Progress { get; set; } = new();
    public string Room { get; set; } = "";
    /// <summary>存档点编号；空字符串表示还没有在任何存档点存过（复活在新游戏起点）。</summary>
    public string SavePoint { get; set; } = "";
    public GDictionary Meta { get; set; } = new();

    public GDictionary ToDictionary() => new()
    {
        ["version"] = Version,
        ["metsys"] = MetSys,
        ["progress"] = Progress,
        ["world"] = new GDictionary { ["room"] = Room, ["savePoint"] = SavePoint },
        ["meta"] = Meta,
    };

    public static SaveData FromDictionary(GDictionary data, out string error)
    {
        error = "";
        if (data == null || !data.ContainsKey("version")) { error = "不是存档文件（缺 version）"; return null; }
        int version = data["version"].AsInt32();
        if (version != CurrentVersion) { error = $"存档版本 {version} 不受支持（当前 {CurrentVersion}）"; return null; }
        if (!data.ContainsKey("world") || data["world"].VariantType != Variant.Type.Dictionary) { error = "存档缺 world"; return null; }
        GDictionary world = data["world"].AsGodotDictionary();
        var result = new SaveData
        {
            Version = version,
            MetSys = data.TryGetValue("metsys", out Variant m) ? m.AsGodotDictionary() : new GDictionary(),
            Progress = data.TryGetValue("progress", out Variant p) ? p.AsGodotDictionary() : new GDictionary(),
            Room = world.TryGetValue("room", out Variant r) ? r.AsString() : "",
            SavePoint = world.TryGetValue("savePoint", out Variant s) ? s.AsString() : "",
            Meta = data.TryGetValue("meta", out Variant meta) ? meta.AsGodotDictionary() : new GDictionary(),
        };
        if (string.IsNullOrEmpty(result.Room)) { error = "存档里没有房间"; return null; }
        return result;
    }

    /// <summary>先写 path.tmp，成功后再改名覆盖 path。</summary>
    public bool WriteAtomic(string path, out string error)
    {
        error = "";
        string tmp = path + ".tmp";
        using (FileAccess file = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (file == null) { error = $"无法写入 {tmp}：{FileAccess.GetOpenError()}"; return false; }
            file.StoreString(GD.VarToStr(ToDictionary()));
            file.Flush();
        }
        string from = ProjectSettings.GlobalizePath(tmp), to = ProjectSettings.GlobalizePath(path);
        Error result = DirAccess.RenameAbsolute(from, to);
        if (result != Error.Ok && FileAccess.FileExists(path))
        {
            // 某些平台的改名不能覆盖已有文件：先删旧档再改名。
            DirAccess.RemoveAbsolute(to);
            result = DirAccess.RenameAbsolute(from, to);
        }
        if (result != Error.Ok) { error = $"改名 {tmp} → {path} 失败：{result}"; return false; }
        return true;
    }

    public static SaveData ReadFrom(string path, out string error)
    {
        if (!FileAccess.FileExists(path)) { error = $"存档 {path} 不存在"; return null; }
        string text = FileAccess.GetFileAsString(path);
        Variant parsed = GD.StrToVar(text);
        if (parsed.VariantType != Variant.Type.Dictionary) { error = $"存档 {path} 无法解析"; return null; }
        return FromDictionary(parsed.AsGodotDictionary(), out error);
    }
}
