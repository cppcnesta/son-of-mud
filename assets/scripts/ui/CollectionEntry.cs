using Godot;
using SonOfMud.Core;

namespace SonOfMud.UI;

/// <summary>
/// 收集页的一个格子（道具或一类收集品），列在 assets/data/ui/collection_entries.tres。
/// - 得到过 = Flags 里任意一个进度变量为真（泥板类用 TabletIds，变量是 tablet.&lt;编号&gt;）。
/// - 进度：Flags（或 TabletIds）有不止一个时，格子右下角显示「已得到数 / 总数」。
/// - 数量：CountKey 不为空时（泥肝）显示「现有数量 / MaxCount」，不看 Flags 的个数。
/// - 说明：Description；泥板类在说明后面列出已得到的泥板名字（名字来自 UiSettings.TabletNames）。
/// </summary>
[GlobalClass]
public partial class CollectionEntry : KeptResource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    /// <summary>判断「得到过」的进度变量（任意一个为真就算）。</summary>
    [Export] public Godot.Collections.Array<StringName> Flags { get; set; } = new();
    /// <summary>和 Flags 一一对应的说明（泥片：虔诚泥片、祈祷泥片各一句）；填了就只列已得到的那几句，不用 Description。</summary>
    [Export] public Godot.Collections.Array<string> FlagNotes { get; set; } = new();
    /// <summary>泥板类：包含的泥板编号（变量 tablet.&lt;编号&gt;）。</summary>
    [Export] public Godot.Collections.Array<StringName> TabletIds { get; set; } = new();
    /// <summary>显示数量用的计数变量（泥肝：count.泥肝）；为空就不显示数量。</summary>
    [Export] public StringName CountKey { get; set; } = "";
    [Export] public int MaxCount { get; set; }

    public System.Collections.Generic.IEnumerable<string> AllFlags()
    {
        foreach (StringName flag in Flags) yield return flag.ToString();
        foreach (StringName id in TabletIds) yield return "tablet." + id;
    }

    public int FlagCount => Flags.Count + TabletIds.Count;

    public int Earned(ProgressStore progress)
    {
        int n = 0;
        foreach (string flag in AllFlags()) if (progress.Flag(flag)) n++;
        return n;
    }

    public bool Obtained(ProgressStore progress) => Earned(progress) > 0;

    /// <summary>格子右下角的数字：泥肝「3/5」，有几件的类别「1/2」；只有一件的类别不显示。</summary>
    public string CountText(ProgressStore progress)
    {
        if (!string.IsNullOrEmpty(CountKey)) return $"{progress.GetVar(CountKey, 0).AsInt64()}/{MaxCount}";
        return FlagCount > 1 ? $"{Earned(progress)}/{FlagCount}" : "";
    }
}
