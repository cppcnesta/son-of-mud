using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Core;

/// <summary>
/// 唯一的进度存储：能力 + 剧情 / 世界变量。门的条件、剧本 [如果]、存档都读写这一份。
/// 变量只允许 bool / int / string（见 StoryValue）。纯 C# 类，不是节点。
/// </summary>
public sealed class ProgressStore
{
    public const int SaveVersion = 1;

    private readonly HashSet<StringName> _abilities = new();
    private readonly Dictionary<StringName, Variant> _vars = new();

    public IEnumerable<StringName> Abilities => _abilities;
    public IEnumerable<StringName> VariableKeys => _vars.Keys;

    public bool HasAbility(StringName id) => _abilities.Contains(id);

    public void Unlock(StringName id)
    {
        if (_abilities.Add(id))
            Events.Instance?.EmitSignal(Events.SignalName.AbilityUnlocked, id);
    }

    public bool HasVar(StringName key) => _vars.ContainsKey(key);

    public Variant GetVar(StringName key, Variant fallback = default) =>
        _vars.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>只有变量存在且是 bool true 才算成立。字符串 "true" 不算，避免类型混用。</summary>
    public bool Flag(StringName key) =>
        _vars.TryGetValue(key, out var value) && value.VariantType == Variant.Type.Bool && value.AsBool();

    /// <summary>写入变量。类型不在 bool / int / string 之内时拒绝并报错，返回 false。</summary>
    public bool SetVar(StringName key, Variant value)
    {
        if (!StoryValue.TryNormalize(value, out var normalized))
        {
            GD.PushError($"进度变量 {key} 的类型 {value.VariantType} 不被允许，只能是 bool / int / string。");
            return false;
        }
        if (_vars.TryGetValue(key, out var old) && StoryValue.AreEqual(old, normalized, out _))
            return true;
        _vars[key] = normalized;
        Events.Instance?.EmitSignal(Events.SignalName.VariableChanged, key, normalized);
        return true;
    }

    /// <summary>门、泥板等共用的条件判断。不满足时 reason 是给玩家看的提示文字。</summary>
    public bool Meets(GateRequirement requirement, out string reason)
    {
        reason = "";
        if (requirement == null) return true;
        bool ok = requirement.Abilities.All(HasAbility) && requirement.Flags.All(Flag);
        if (!ok) reason = requirement.LockedText;
        return ok;
    }

    /// <summary>新游戏：清空后写入初始能力。</summary>
    public void ResetNew(AbilityTable table = null)
    {
        _abilities.Clear();
        _vars.Clear();
        if (table == null) return;
        foreach (StringName id in table.Initial) _abilities.Add(id);
    }

    /// <summary>导出为只含 Variant 的字典，交给 Save 系统写盘。</summary>
    public GDictionary ToSave()
    {
        var abilities = new GArray();
        foreach (StringName id in _abilities.OrderBy(a => a.ToString())) abilities.Add(id.ToString());
        var vars = new GDictionary();
        foreach (var (key, value) in _vars) vars[key.ToString()] = value;
        return new GDictionary { ["version"] = SaveVersion, ["abilities"] = abilities, ["vars"] = vars };
    }

    /// <summary>从存档恢复。会先清空当前内容；非法类型的变量跳过并报错。</summary>
    public void FromSave(GDictionary data)
    {
        _abilities.Clear();
        _vars.Clear();
        if (data == null) return;
        if (data.TryGetValue("abilities", out var abilities))
            foreach (Variant id in abilities.AsGodotArray()) _abilities.Add(id.AsString());
        if (data.TryGetValue("vars", out var vars))
        {
            foreach (var (key, value) in vars.AsGodotDictionary())
            {
                if (StoryValue.TryNormalize(value, out var normalized)) _vars[key.AsString()] = normalized;
                else GD.PushError($"存档里的进度变量 {key} 类型 {value.VariantType} 不被允许，已跳过。");
            }
        }
    }
}
