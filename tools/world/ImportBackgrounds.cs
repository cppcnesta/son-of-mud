using System.Linq;
using Godot;
using SonOfMud.World;

namespace SonOfMud.Tools;

/// <summary>
/// 不开编辑器给房间「按命名导入背景」（和房间根节点上的按钮一样），再把地形图设成视差 1、固定缩放和位置（<see cref="TerrainArt"/>），存回场景。
/// 运行：godot --headless --path . --scene res://tools/world/ImportBackgrounds.tscn -- --rooms=S4_01,S4_02
/// 不带 --rooms 时什么都不做（避免一次改动所有房间）。出错时退出码为 1。
/// </summary>
public partial class ImportBackgrounds : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        string arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--rooms="));
        if (arg == null)
        {
            GD.Print("ImportBackgrounds：没有 --rooms=…，什么都不做。");
            GetTree().Quit(0);
            return;
        }
        var layout = GD.Load<WorldLayout>(WorldLayout.DefaultPath);
        var settings = BackgroundSettings.LoadDefault();
        int code = 0;
        foreach (string id in arg["--rooms=".Length..].Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            RoomDef def = layout.Find(id);
            if (def == null) { GD.PushError($"ImportBackgrounds：房间表里没有 {id}"); code = 1; continue; }
            using var packed = ResourceLoader.Load<PackedScene>(def.ScenePath, null, ResourceLoader.CacheMode.Ignore);
            if (packed?.Instantiate(PackedScene.GenEditState.Instance) is not RoomRoot room) { GD.PushError($"ImportBackgrounds：{id} 场景打不开"); code = 1; continue; }
            AddChild(room);
            BackgroundImporter.ImportPlan plan = BackgroundImporter.PlanImport(room, settings);
            foreach (string message in plan.Messages) GD.Print($"ImportBackgrounds：{id}：{message}");
            if (!plan.Empty) BackgroundImporter.Apply(room, plan);
            bool terrain = TerrainArt.Fix(room);
            string problem = TerrainArt.Rooms.ContainsKey(id) ? TerrainArt.Problem(room) : "";
            if (problem != "") { GD.PushError($"ImportBackgrounds：{id}：地形图 {problem}"); code = 1; }
            RemoveChild(room);
            using var output = new PackedScene();
            Error pack = output.Pack(room);
            room.Free();
            if (pack != Error.Ok) { GD.PushError($"ImportBackgrounds：{id} 打包失败 {pack}"); code = 1; continue; }
            long uid = ResourceLoader.GetResourceUid(def.ScenePath);
            Error save = ResourceSaver.Save(output, def.ScenePath);
            if (save == Error.Ok && uid != ResourceUid.InvalidId) ResourceSaver.SetUid(def.ScenePath, uid);
            if (save != Error.Ok) { GD.PushError($"ImportBackgrounds：{id} 保存失败 {save}"); code = 1; continue; }
            GD.Print($"ImportBackgrounds：{id}：已导入背景{(terrain ? "，地形图已设成视差 1" : "")}。");
        }
        GetTree().Quit(code);
    }
}
