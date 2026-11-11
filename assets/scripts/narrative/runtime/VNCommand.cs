using System;
using System.Collections.Generic;
using System.Globalization;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 所有命令的基类。一个命令 = 剧本里的一种 [关键词 参数]。
/// 约定只有一条：命令做完以后调用 done。马上完成的命令立刻调用；“说”等推进键、[等待] 等时间、[玩法] 等结果。
/// </summary>
public abstract class VNCommand
{
    /// <summary>CG、BGM 这类命令的参数写“无”，表示关掉。</summary>
    public const string None = "无";

    public abstract string Name { get; }
    public virtual int MinArgs => 0;
    public virtual string Usage => $"[{Name}]";

    /// <summary>解析时检查参数。没问题返回 null，有问题返回错误说明。</summary>
    public virtual string Validate(string[] args) =>
        args.Length < MinArgs ? "参数不够。用法：" + Usage : null;

    /// <summary>这个命令会跳到哪些标签，解析器检查它们都存在。</summary>
    public virtual IEnumerable<string> LabelArgs(string[] args) => Array.Empty<string>();

    /// <summary>会不会打断一段连续的台词（跳转、选项、结束……）。</summary>
    public virtual bool EndsDialogueBlock => false;

    /// <summary>[选项]：后面连续的「- 文本 -> 标签」行属于它，解析器收集完再整体检查。</summary>
    public virtual bool CollectsOptions => false;

    /// <summary>CollectsOptions 的命令：只检查命令行本身的参数。</summary>
    public virtual string ValidateHeader(string[] args) => null;

    /// <summary>紧跟在台词后面时，台词打完字就直接执行它、不等推进键（选项和最后一句台词一起出现）。</summary>
    public virtual bool ShowsWithPreviousLine => false;

    /// <summary>字幕模式（不暂停游戏）里能不能用。选项、泥板、CG 这类只能在对话框模式里用。</summary>
    public virtual bool AllowedInSubtitle => true;

    public abstract void Execute(string[] args, VNContext ctx, Action done);

    // ---------- 读取参数的小工具 ----------

    protected static string Arg(string[] args, int i, string fallback = "") =>
        i < args.Length && !string.IsNullOrEmpty(args[i]) ? args[i] : fallback;

    protected static float FloatArg(string[] args, int i, float fallback) =>
        i < args.Length && float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    protected static string[] ArgsFrom(string[] args, int start)
    {
        if (start >= args.Length) return Array.Empty<string>();
        var rest = new string[args.Length - start];
        Array.Copy(args, start, rest, 0, rest.Length);
        return rest;
    }
}
