using System.Threading.Tasks;
using Godot;
using SonOfMud.Core;
using SonOfMud.UI;

/// <summary>
/// 读档失败的公共流程：子类往存档槽写一个坏存档 → PendingStart = Continue → 切到 main_game.tscn：
/// 收到 GameStartFinished(false)、LastStartError 不为空、已经回到主菜单场景、存档文件字节完全不变、
/// 暂停和输入锁都没有残留、上一局的玩家已释放。
/// </summary>
public abstract partial class BootFailureTestBase : FlowTestBase
{
    /// <summary>失败用例：(名字, 存档字节)。每个用例单独走一遍完整流程。</summary>
    protected abstract (string name, byte[] bytes)[] Cases();

    protected override async Task RunChecks()
    {
        foreach (var (name, bytes) in Cases())
            await RunCase(name, bytes);
    }

    private async Task RunCase(string name, byte[] bytes)
    {
        DeleteSave();
        WriteSaveBytes(bytes);
        byte[] before = ReadSaveBytes();
        Check(SameBytes(before, bytes), $"[{name}] 搭建：存档槽里写入了 {bytes.Length} 字节");

        GameSession.LastStartError = "";
        int startsBefore = Starts.Count;
        bool finished = await EnterMainGame(StartMode.Continue);
        // 失败后 GameBoot 切回主菜单：等主菜单真正成为当前场景
        bool inMenu = await WaitUntil(() => GetTree().CurrentScene is MudMainMenu, 120);
        await Frames(10);
        Node scene = GetTree().CurrentScene;

        Check(finished && Starts.Count == startsBefore + 1 && !Starts[^1].success,
            $"[{name}] 收到 GameStartFinished(false) 恰好一次（{(Starts.Count > startsBefore ? Starts[^1].error : "没收到")}）");
        Check(!string.IsNullOrEmpty(GameSession.LastStartError) && GameSession.LastStartError == Starts[^1].error,
            $"[{name}] LastStartError 已写入：{GameSession.LastStartError}");
        Check(inMenu && scene != null && scene.SceneFilePath == MenuScene, $"[{name}] 已经回到主菜单场景（当前 {scene?.SceneFilePath}）");
        Check(SameBytes(before, ReadSaveBytes()), $"[{name}] 存档文件的字节和写入时完全一样");
        Check(!FileAccess.FileExists(SavePath + ".tmp"), $"[{name}] 没有留下写了一半的临时存档");
        Check(NoPauseOrLock(), $"[{name}] 暂停和输入锁都没有残留");
        Check(GameSession.PendingStart == StartMode.None, $"[{name}] PendingStart 设回 None");
        Check(AllPlayers().Count == 0, $"[{name}] 正式场景（含玩家）已整个释放");
    }
}
