using System;
using System.Collections.Generic;
using Godot;
using SonOfMud.Core;
using SonOfMud.Narrative.Runtime;

namespace SonOfMud.Narrative.Commands;

/// <summary>
/// [如果 变量 值 标签]：变量等于这个值时跳到标签，否则继续往下。
/// 比较用 StoryValue.AreEqual：类型和值都相同才相等；类型不同按不相等处理并报警告。没设置过的变量和任何值都不相等。
/// </summary>
public class IfCommand : VNCommand
{
    public override string Name => "如果";
    public override bool EndsDialogueBlock => true;
    public override int MinArgs => 3;
    public override string Usage => "[如果 变量 值 标签]";
    public override IEnumerable<string> LabelArgs(string[] args) => new[] { Arg(args, 2) };

    public override void Execute(string[] args, VNContext ctx, Action done)
    {
        if (ctx.Vars.Has(args[0]))
        {
            Variant current = ctx.Vars.Get(args[0]);
            Variant expected = StoryValue.Parse(args[1]);
            bool equal = StoryValue.AreEqual(current, expected, out bool mismatch);
            if (mismatch)
            {
                int line = ctx.Runner.CurrentEntry?.SourceLine ?? 0;
                NarrativeLog.Warn($"[剧情] {ctx.Runner.Script?.Id} 第{line}行 [如果]：变量 {args[0]} 是 {TypeName(current)}，" +
                                  $"比较的值「{args[1]}」是 {TypeName(expected)}，类型不同，按不相等处理");
            }
            if (equal) ctx.Runner.JumpTo(args[2]);
        }
        done();
    }

    private static string TypeName(Variant value) => value.VariantType switch
    {
        Variant.Type.Bool => "bool",
        Variant.Type.Int => "int",
        Variant.Type.String or Variant.Type.StringName => "string",
        _ => value.VariantType.ToString(),
    };
}
