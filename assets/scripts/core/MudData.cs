using Godot;
using GDictionary = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

/// <summary>Typed access to existing world JSON and version 1 save dictionaries.</summary>
public static class MudData
{
    public static string Str(this GDictionary data, string key, string fallback = "") =>
        data.TryGetValue(key, out var value) && value.VariantType != Variant.Type.Nil ? value.AsString() : fallback;
    public static float Num(this GDictionary data, string key, float fallback = 0f) =>
        data.TryGetValue(key, out var value) && value.VariantType != Variant.Type.Nil ? value.AsSingle() : fallback;
    public static int Int(this GDictionary data, string key, int fallback = 0) =>
        data.TryGetValue(key, out var value) && value.VariantType != Variant.Type.Nil ? value.AsInt32() : fallback;
    public static bool Bool(this GDictionary data, string key, bool fallback = false) =>
        data.TryGetValue(key, out var value) && value.VariantType != Variant.Type.Nil ? value.AsBool() : fallback;
    public static GDictionary Dict(this GDictionary data, string key) =>
        data.TryGetValue(key, out var value) && value.VariantType == Variant.Type.Dictionary ? value.AsGodotDictionary() : new();
    public static GArray Array(this GDictionary data, string key) =>
        data.TryGetValue(key, out var value) && value.VariantType == Variant.Type.Array ? value.AsGodotArray() : new();
    public static Vector2 Point(this GDictionary data, string key)
    {
        var values = data.Array(key);
        return new Vector2(values[0].AsSingle(), values[1].AsSingle());
    }
    public static string[] Strings(this GArray values)
    {
        var result = new string[values.Count];
        for (var i = 0; i < values.Count; i++) result[i] = values[i].AsString();
        return result;
    }
}

public interface IMudDamageable
{
    bool ReceiveHit(float amount, string attackId, GDictionary info);
}

public interface IMudInteractable
{
    string Title { get; }
    void Interact(MudPlayer actor);
}
