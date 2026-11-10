using Godot;

namespace SonOfMud.Narrative.Runtime;

/// <summary>剧本变量的读写入口。正式游戏用 CoreStoryVariables（Core 的 ProgressStore），值只允许 bool / int / string。</summary>
public interface IStoryVariables
{
    bool Has(string key);
    Variant Get(string key);
    void Set(string key, Variant value);
    /// <summary>变量存在且是 bool true。</summary>
    bool Flag(string key);
}
