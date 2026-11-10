using System.Collections.Generic;
using Godot;
using SonOfMud.Narrative.Runtime;

/// <summary>测试用的剧本变量：放在内存字典里（对应原版测试里的 ctx.Variables）。</summary>
public sealed class MemoryStoryVariables : IStoryVariables
{
    public readonly Dictionary<string, Variant> Values = new();

    public bool Has(string key) => Values.ContainsKey(key);
    public Variant Get(string key) => Values.TryGetValue(key, out Variant value) ? value : default;
    public void Set(string key, Variant value) => Values[key] = value;
    public bool Flag(string key) => Values.TryGetValue(key, out Variant value) && value.VariantType == Variant.Type.Bool && value.AsBool();
}
