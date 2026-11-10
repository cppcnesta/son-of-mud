using Godot;
using SonOfMud.Player;
using GDictionary = Godot.Collections.Dictionary;

namespace SonOfMud.Core;

public partial class MudProgress : Node
{
    [Signal] public delegate void ChangedEventHandler();
    public static MudProgress Instance { get; private set; }
    public const string SavePath = "user://mudchild_whitebox_v1.json";
    public MudPrototypeRules Rules { get; private set; }
    public GDictionary Abilities { get; set; } = new();
    public GDictionary Flags { get; set; } = new();
    public GDictionary TestAbilities { get; } = new();
    public bool TestMode { get; private set; }
    public int MudModule { get; set; }
    public string CheckpointRoom { get; private set; } = "M01";
    public string CheckpointSpawn { get; private set; } = "Start";
    public string LastRoom { get; set; } = "M01";
    public string LastSpawn { get; set; } = "Start";
    public bool AllowDisk { get; set; } = true;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }
    public override void _Ready()
    {
        Rules = GD.Load<MudPrototypeRules>("res://assets/data/player/prototype_rules.tres");
        NewGame(false);
        LoadGame();
    }
    public bool HasAbility(string key) => TestMode ? TestAbilities.Bool(key, Abilities.Bool(key)) : Abilities.Bool(key);
    public void Unlock(string key)
    {
        if (TestMode) TestAbilities[key] = true;
        else { Abilities[key] = true; SaveGame(); }
        EmitSignal(SignalName.Changed);
    }
    public bool Flag(string key) => Flags.Bool(key);
    public void Mark(string key)
    {
        Flags[key] = true;
        SaveGame();
        EmitSignal(SignalName.Changed);
    }
    public bool DistrictsComplete() => Flag("district_C04") && Flag("district_C05") && Flag("district_C06");
    public void EnterTest() { TestMode = true; TestAbilities.Clear(); MudModule = Rules.MudJumpModule; }
    public void LeaveTest() { TestMode = false; TestAbilities.Clear(); MudModule = Rules.MudJumpModule; }
    public void NewGame(bool persist = true)
    {
        LeaveTest();
        Abilities.Clear();
        Flags.Clear();
        foreach (var key in Rules.InitialAbilities) Abilities[key] = true;
        CheckpointRoom = "M01";
        CheckpointSpawn = "Start";
        LastRoom = "M01";
        LastSpawn = "Start";
        if (persist) SaveGame();
        EmitSignal(SignalName.Changed);
    }
    public void SetCheckpoint(string room, string spawn)
    {
        if (TestMode) return;
        CheckpointRoom = room;
        CheckpointSpawn = spawn;
        SaveGame();
    }
    public GDictionary Snapshot() => new()
    {
        ["version"] = 1, ["abilities"] = Abilities.Duplicate(true), ["flags"] = Flags.Duplicate(true),
        ["checkpoint_room"] = CheckpointRoom, ["checkpoint_spawn"] = CheckpointSpawn,
        ["last_room"] = LastRoom, ["last_spawn"] = LastSpawn
    };
    public bool ApplySnapshot(GDictionary data)
    {
        if (data.Int("version") != 1 || !data.TryGetValue("abilities", out var abilities) ||
            abilities.VariantType != Variant.Type.Dictionary || !data.TryGetValue("flags", out var flags) ||
            flags.VariantType != Variant.Type.Dictionary) return false;
        Abilities = abilities.AsGodotDictionary().Duplicate(true);
        Flags = flags.AsGodotDictionary().Duplicate(true);
        CheckpointRoom = data.Str("checkpoint_room", "M01");
        CheckpointSpawn = data.Str("checkpoint_spawn", "Start");
        LastRoom = data.Str("last_room", CheckpointRoom);
        LastSpawn = data.Str("last_spawn", CheckpointSpawn);
        LeaveTest();
        EmitSignal(SignalName.Changed);
        return true;
    }
    public bool SaveGame()
    {
        if (TestMode || !AllowDisk) return false;
        using (var file = FileAccess.Open(SavePath + ".tmp", FileAccess.ModeFlags.Write))
        {
            if (file == null) { GD.PushError("存档写入失败"); return false; }
            file.StoreString(Json.Stringify(Snapshot(), "\t"));
        }
        return DirAccess.RenameAbsolute(SavePath + ".tmp", SavePath) == Error.Ok;
    }
    public bool LoadGame()
    {
        if (!AllowDisk || !FileAccess.FileExists(SavePath)) return false;
        var data = Json.ParseString(FileAccess.GetFileAsString(SavePath));
        return data.VariantType == Variant.Type.Dictionary && ApplySnapshot(data.AsGodotDictionary());
    }
}
