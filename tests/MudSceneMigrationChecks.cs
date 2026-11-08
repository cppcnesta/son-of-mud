using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public sealed class MudSceneMigrationChecks
{
    public async Task Run(MudRuntimeSuite driver)
    {
        MudProgress.Instance.NewGame(false); MudProgress.Instance.EnterTest();
        var paths = new List<string>
        {
            "res://assets/scenes/whitebox/tests/form_lab.tscn", "res://assets/scenes/ui/main_menu.tscn"
        };
        using var directory = DirAccess.Open("res://assets/scenes/whitebox/levels");
        var count = 0;
        foreach (var filename in directory.GetFiles())
            if (filename.EndsWith(".tscn")) { paths.Add("res://assets/scenes/whitebox/levels/" + filename); count++; }
        driver.Check(count == 23, "主线仍为23场景，沿用main对旧练习场景的清理");
        foreach (var path in paths)
        {
            var packed = GD.Load<PackedScene>(path);
            driver.Check(packed != null, "场景依赖加载 " + path);
            if (packed == null) return;
            var error = driver.GetTree().ChangeSceneToPacked(packed);
            driver.Check(error == Error.Ok, "切换场景 " + path);
            await driver.Frames(10);
            var scene = driver.GetTree().CurrentScene;
            var menu = path.EndsWith("main_menu.tscn");
            driver.Check(scene != null && (menu || scene.GetNodeOrNull<MudPlayer>("Player") != null), "实际运行 " + path);
        }
        driver.GetTree().ChangeSceneToFile("res://assets/scenes/ui/main_menu.tscn"); await driver.Frames(8);
        await driver.SnapshotImage("runtime_csharp_scene_paths");
    }
}
