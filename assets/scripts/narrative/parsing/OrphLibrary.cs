using System.Collections.Generic;
using System.Linq;
using Godot;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Parsing;

/// <summary>
/// 已解析的剧本表：剧本编号（文件名去掉 .orph）→ OrphScript。
/// 进程里只有一份（Shared）：解析结果不会变，重开一局时新的剧情层直接复用。
/// 导出游戏时 .orph 不是 Godot 资源，要靠导出预设的 include_filter（*.orph）打包进去。
/// </summary>
public sealed class OrphLibrary
{
    public const string Extension = ".orph";

    public static OrphLibrary Shared { get; } = new();

    private readonly Dictionary<string, OrphScript> _scripts = new();
    private readonly Dictionary<string, string> _paths = new();
    private readonly HashSet<string> _loadedDirectories = new();

    public IEnumerable<OrphScript> All => _scripts.Values;
    public IEnumerable<string> Ids => _scripts.Keys;

    public bool TryGet(string id, out OrphScript script) => _scripts.TryGetValue(id ?? "", out script);

    public string PathOf(string id) => _paths.TryGetValue(id ?? "", out string path) ? path : "";

    /// <summary>加入（或替换）一份剧本。测试用它放进测试剧本。</summary>
    public void Add(OrphScript script, string path = "")
    {
        if (script == null) return;
        _scripts[script.Id] = script;
        _paths[script.Id] = path;
    }

    public bool Remove(string id)
    {
        _paths.Remove(id ?? "");
        return _scripts.Remove(id ?? "");
    }

    /// <summary>目录还没读过就读一遍（同一个目录在进程里只读一次）。</summary>
    public void EnsureLoaded(string directory)
    {
        if (_loadedDirectories.Contains(directory)) return;
        LoadDirectory(directory);
    }

    /// <summary>读目录下全部 .orph 并解析。错误用 PushError 报出文件和行号，警告用 PushWarning。返回读到的剧本数。</summary>
    public int LoadDirectory(string directory)
    {
        _loadedDirectories.Add(directory);
        if (!DirAccess.DirExistsAbsolute(directory))
        {
            NarrativeLog.Error($"[剧本] 找不到剧本目录 {directory}");
            return 0;
        }
        int count = 0;
        foreach (string file in DirAccess.GetFilesAt(directory).OrderBy(f => f, System.StringComparer.Ordinal))
        {
            if (!file.EndsWith(Extension)) continue;
            string path = $"{directory}/{file}";
            OrphScript script = LoadFile(path);
            if (script == null) continue;
            Add(script, path);
            count++;
        }
        return count;
    }

    /// <summary>读一个 .orph 文件并解析（不加入表）。读不到返回 null。</summary>
    public static OrphScript LoadFile(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            NarrativeLog.Error($"[剧本] 找不到文件 {path}");
            return null;
        }
        string id = path.GetFile().GetBaseName();
        OrphScript script = OrphParser.Parse(FileAccess.GetFileAsString(path), id);
        foreach (string error in script.Errors) NarrativeLog.Error($"[剧本] {path}  {error}（这条已跳过）");
        foreach (string warning in script.Warnings) NarrativeLog.Warn($"[剧本] {path}  {warning}");
        return script;
    }

    /// <summary>全部剧本的错误，前面带剧本编号。</summary>
    public IEnumerable<string> AllErrors() =>
        _scripts.Values.SelectMany(s => s.Errors.Select(e => $"{s.Id}: {e}"));
}
