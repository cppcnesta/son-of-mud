using System.Collections.Generic;
using SonOfMud.Narrative.Commands;

namespace SonOfMud.Narrative.Runtime;

/// <summary>
/// 命令注册表：关键词 → 命令。解析器用它检查剧本，执行器用它找到要执行的命令。
/// 加新命令：写一个继承 VNCommand 的类，在下面加一行。
/// </summary>
public static class VNCommands
{
    private static readonly Dictionary<string, VNCommand> Map = Build(
        // 台词
        new SayCommand(),
        new ContinueSayCommand(),
        new WaitClickCommand(),
        new WaitCommand(),
        new SubtitleModeCommand(),
        // 流程
        new JumpCommand(),
        new SetCommand(),
        new IfCommand(),
        new EndCommand(),
        new ChoiceCommand(),
        new RestartCommand(),
        // 本作内容
        new TabletCommand(),
        new InscriptionCommand(),
        new RenameCommand(),
        new EventCommand(),
        // 画面和声音
        new CgCommand(),
        new FadeCommand(),
        new ExpressionCommand(),
        new SfxCommand(),
        new BgmCommand(),
        // 玩法和效果
        new GameplayCommand(),
        new EffectCommand(),
        // 原版有、本作不支持：写了就报错
        new UnsupportedCommand("背景", "场景就是世界本身，不换背景"),
        new UnsupportedCommand("登场", "立绘只显示在对话框里"),
        new UnsupportedCommand("退场", "立绘只显示在对话框里"),
        new UnsupportedCommand("存档", "剧情中途不存档，进度由存档点保存")
    );

    private static Dictionary<string, VNCommand> Build(params VNCommand[] commands)
    {
        var result = new Dictionary<string, VNCommand>();
        foreach (VNCommand command in commands) result[command.Name] = command;
        return result;
    }

    public static bool TryGet(string name, out VNCommand command) => Map.TryGetValue(name ?? "", out command);

    public static IEnumerable<VNCommand> All => Map.Values;
}
