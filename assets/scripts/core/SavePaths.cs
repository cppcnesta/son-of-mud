using Godot;

namespace SonOfMud.Core;

/// <summary>
/// 存档路径的规则（唯一出处）：槽位 n 的存档是 &lt;目录&gt;/save_n.sav，默认目录 user://。
/// SaveService 写盘读盘、主菜单判断“继续”是否可用，都用这里的规则。
/// </summary>
public static class SavePaths
{
    public const string DefaultDirectory = "user://";

    public static string SlotPath(int slot) => SlotPath(DefaultDirectory, slot);

    public static string SlotPath(string directory, int slot) =>
        (string.IsNullOrEmpty(directory) ? DefaultDirectory : directory).PathJoin($"save_{slot}.sav");

    public static bool Exists(int slot) => Exists(DefaultDirectory, slot);

    public static bool Exists(string directory, int slot) => FileAccess.FileExists(SlotPath(directory, slot));
}
