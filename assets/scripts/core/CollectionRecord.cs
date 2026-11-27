using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Save;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Core;

/// <summary>
/// 跨存档的收藏记录（主页面「收集」用）：通关过没有、捡到过哪些泥板、解锁了哪些 CG。
/// 存在 user://collection.cfg，和存档分开：开新档、覆盖存档、删存档都不会清掉。有新内容就马上写盘，写盘失败只报错。
/// - 通关：走到任意结局。结局 A「醒来」最后拿到泥板「星」（EndingTabletId）；结局 B「回去睡」由剧本 [重开] 发出 RestartRunRequested。
/// - 泥板：Events.TabletCollected。
/// - CG：CgCommand 在 CG 真的显示出来时调用 MarkCg（静态 CG 交给 CG 层之后、动画 CG 开播成功之后）。
/// - 旧档导入：主页面一打开就把存档里的 tablet.* 并进来并写盘（ImportFromSave，幂等，存档只读）。
/// - 测试：启动场景在 res://tests/ 下时（自动测试、编辑器里单独运行测试场景）改写 TestFilePath，绝不碰正式记录；测试也可以用 FilePathOverride 指定。
/// Events autoload 进场景树时调用 Attach 接上信号。只存 C# 集合，不静态持有 Godot 对象。
/// </summary>
public static class CollectionRecord
{
    public const string FilePath = "user://collection.cfg";
    public const string TestFilePath = "user://collection_test.cfg";
    /// <summary>结局 A「醒来」最后拿到的泥板，拿到即算通关（它只出现在这个结局里）。</summary>
    public const string EndingTabletId = "star";
    private const string Section = "collection";
    private const string TabletPrefix = "tablet.";
    private const string CgPrefix = "cg.";

    private static readonly HashSet<string> Tablets = new();
    private static readonly HashSet<string> Cgs = new();
    private static bool _cleared;
    private static bool _loaded;
    private static string _loadedFrom;
    private static bool? _isTestRun;

    /// <summary>测试指定的记录文件；为空时按是否测试运行自动选 FilePath / TestFilePath。</summary>
    public static string FilePathOverride { get; set; }

    /// <summary>启动场景在 res://tests/ 下（自动测试，或在编辑器里单独运行测试场景）。</summary>
    public static bool IsTestRun => _isTestRun ??= OS.GetCmdlineArgs().Any(a => a.Contains("res://tests/"));

    /// <summary>现在读写的记录文件。</summary>
    public static string ActivePath => !string.IsNullOrEmpty(FilePathOverride) ? FilePathOverride : IsTestRun ? TestFilePath : FilePath;

    /// <summary>写盘次数（测试用：检查导入是不是幂等）。</summary>
    public static int Writes { get; private set; }

    public static bool Cleared { get { EnsureLoaded(); return _cleared; } }
    public static bool HasTablet(string id) { EnsureLoaded(); return Tablets.Contains(id); }
    public static bool HasCg(string id) { EnsureLoaded(); return Cgs.Contains(id); }
    public static IReadOnlyCollection<string> TabletIds { get { EnsureLoaded(); return Tablets; } }
    public static IReadOnlyCollection<string> CgIds { get { EnsureLoaded(); return Cgs; } }

    // ---------- 接信号 ----------

    public static void Attach(Events events)
    {
        if (events == null) return;
        events.TabletCollected += OnTabletCollected;
        events.RestartRunRequested += OnRestartRunRequested;
    }

    private static void OnTabletCollected(StringName tabletId)
    {
        string id = tabletId.ToString();
        MarkTablet(id);
        if (id == EndingTabletId) MarkCleared();
    }

    private static void OnRestartRunRequested() => MarkCleared();

    // ---------- 记录 ----------

    /// <summary>记通关；以前没记过才写盘。返回这次是不是新记的。</summary>
    public static bool MarkCleared()
    {
        EnsureLoaded();
        if (_cleared) return false;
        _cleared = true;
        Save();
        return true;
    }

    public static bool MarkTablet(string id) => Add(Tablets, id);

    public static bool MarkCg(string id) => Add(Cgs, id);

    private static bool Add(HashSet<string> set, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        EnsureLoaded();
        if (!set.Add(id)) return false;
        Save();
        return true;
    }

    /// <summary>把一批泥板并进记录；有新的才写一次盘。返回新加了几块。</summary>
    public static int ImportTablets(IEnumerable<string> ids)
    {
        EnsureLoaded();
        int added = ids.Where(id => !string.IsNullOrEmpty(id)).Count(id => Tablets.Add(id));
        if (added > 0) Save();
        return added;
    }

    /// <summary>
    /// 旧档导入：读存档进度里值为 true 的 tablet.* 并进记录（幂等）。存档只读；不存在、读不出来就跳过（读不出来时记日志）。
    /// 返回新加了几块。
    /// </summary>
    public static int ImportFromSave(string savePath)
    {
        if (string.IsNullOrEmpty(savePath) || !FileAccess.FileExists(savePath)) return 0;
        SaveData data = SaveData.ReadFrom(savePath, out string error);
        if (data == null)
        {
            GD.Print($"CollectionRecord：存档 {savePath} 读不出来，跳过导入泥板（{error}）");
            return 0;
        }
        return ImportTablets(SavedTablets(data.Progress));
    }

    public static int ImportFromSave(int slot) => ImportFromSave(SavePaths.SlotPath(slot));

    /// <summary>存档进度（ProgressStore.ToSave 的格式：{vars: {...}}）里捡到过的泥板 id。</summary>
    public static IEnumerable<string> SavedTablets(GDictionary progress)
    {
        if (progress == null || !progress.TryGetValue("vars", out Variant vars) || vars.VariantType != Variant.Type.Dictionary) yield break;
        foreach (var (key, value) in vars.AsGodotDictionary())
        {
            string name = key.AsString();
            if (name.StartsWith(TabletPrefix) && value.VariantType == Variant.Type.Bool && value.AsBool())
                yield return name.Substring(TabletPrefix.Length);
        }
    }

    // ---------- 读写 ----------

    /// <summary>从现在的记录文件重新读（测试换路径后用）。</summary>
    public static void Reload()
    {
        _loaded = false;
        EnsureLoaded();
    }

    /// <summary>
    /// 测试用：清空现在的记录文件和内存里的记录。为了不误删玩家的收藏，正式记录文件（FilePath）不能这样清，会直接拒绝。
    /// </summary>
    public static bool ResetForTest()
    {
        if (ActivePath == FilePath)
        {
            GD.PushError("CollectionRecord：拒绝清空正式记录文件");
            return false;
        }
        if (FileAccess.FileExists(ActivePath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ActivePath));
        Tablets.Clear();
        Cgs.Clear();
        _cleared = false;
        _loaded = true;
        _loadedFrom = ActivePath;
        Writes = 0;
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded && _loadedFrom == ActivePath) return;
        _loaded = true;
        _loadedFrom = ActivePath;
        Tablets.Clear();
        Cgs.Clear();
        _cleared = false;
        var file = new ConfigFile();
        if (file.Load(ActivePath) != Error.Ok) return;
        _cleared = (bool)file.GetValue(Section, "cleared", false);
        foreach (string key in file.GetSectionKeys(Section))
        {
            if (!(bool)file.GetValue(Section, key, false)) continue;
            if (key.StartsWith(TabletPrefix)) Tablets.Add(key.Substring(TabletPrefix.Length));
            else if (key.StartsWith(CgPrefix)) Cgs.Add(key.Substring(CgPrefix.Length));
        }
    }

    private static void Save()
    {
        var file = new ConfigFile();
        file.SetValue(Section, "cleared", _cleared);
        foreach (string id in Tablets.OrderBy(t => t)) file.SetValue(Section, TabletPrefix + id, true);
        foreach (string id in Cgs.OrderBy(c => c)) file.SetValue(Section, CgPrefix + id, true);
        Error result = file.Save(ActivePath);
        Writes++;
        if (result != Error.Ok) GD.PushError($"CollectionRecord：写入 {ActivePath} 失败：{result}");
    }
}
