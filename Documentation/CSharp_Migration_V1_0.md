# 泥之子 · C# 迁移 V1.0

以 `D:\GAMEOFF\son-of-mud` 本次读取到的工程为基准，迁移20个游戏脚本和3个测试脚本。新增 `MudData.cs` 为类型化 JSON/存档读取及战斗、交互接口提供共享实现。全部提供完整源文件。

## 已转换的内容

- 角色移动、形态、闪避、格挡、跳跃、攻击判定与状态机。
- 曲线减速：泥闪避后1秒、泥攻击后1.5秒；攻击后摇末0.5秒格挡窗口，Shift覆盖攻击后摇。
- 水格挡3秒冷却；成功格挡清零格挡CD和攻击间隔。泥格挡的增伤、持续窗口与刷新规则沿用实际工程配置。
- 待起跳请求与当前空中轨迹分开保存，保留前摇落地、受伤取消和换场取消修复。
- 进度、测试能力覆盖层、检查点、存档、菜单、HUD、白盒地形、敌人、交互和房间传送。
- 23个主线房间、测试场及现有兼容入口。场景和 `.tres` 的导出字段改为对应C#属性名，脚本UID沿用原值。
- `SonOfMud.csproj`、`SonOfMud.sln`、C#入口、启动脚本及测试脚本。

`world.json`、现有贴图、TileSet和房间连接数据保持原字节。现有数值作为迁移依据；本次没有将讨论中的新Boss方案额外接入工程。

## 环境与打开方法

需要 **Godot 4.7.2 .NET版** 及 **.NET SDK**。普通版Godot无法执行C#脚本。项目SDK为 `Godot.NET.Sdk/4.7.2`，目标框架为 `net8.0`。

1. 使用Godot .NET版导入 `project.godot`。
2. 首次构建需解析Godot SDK包；随后点击Godot的Build/构建按钮，或在项目目录运行 `dotnet build SonOfMud.csproj`。
3. F5启动主菜单，F2进入训练场。其他键位保留原InputMap设置。

也可运行：

```powershell
.\Start_CSharp_Project.ps1 -GodotPath '你的Godot.NET可执行文件完整路径'
```

官方说明：<https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html>

## 存档与接口

应用名称、`user://mudchild_whitebox_v1.json` 路径及version=1字段保持不变。正式进度JSON可继续读回；测试能力仍不写入正式存档。

| 原脚本 | C#源文件 |
| --- | --- |
| `player.gd` | `assets/scripts/player/MudPlayer.cs` |
| `form_config.gd` | `assets/scripts/player/components/MudFormConfig.cs` |
| `prototype_rules.gd` | `assets/scripts/player/components/MudPrototypeRules.cs` |
| `player_combat.gd` | `assets/scripts/player/components/MudPlayerCombat.cs` |
| `player_guard.gd` | `assets/scripts/player/components/MudPlayerGuard.cs` |
| `movement_recovery.gd` | `assets/scripts/player/components/MudMovementRecovery.cs` |
| `progress.gd` | `assets/scripts/progression/MudProgress.cs` |
| `room_travel.gd` | `assets/scripts/world/MudRoomTravel.cs` |
| `room_portal.gd` | `assets/scripts/world/MudRoomPortal.cs` |
| `whitebox_room.gd` | `assets/scripts/whitebox/MudWhiteboxRoom.cs` |

Autoload节点名称仍为 `InputDefaults`、`Progress`、`RoomTravel`。C#直接使用 `MudProgress.Instance` 和 `MudRoomTravel.Instance`。方法、字段采用PascalCase，例如 `EnterRoom`、`ReceiveAttack`、`StartBlock`。新受击对象实现 `IMudDamageable`，新交互对象实现 `IMudInteractable`。

## 回归测试

```powershell
.\Run_Tests.ps1 -GodotPath '你的Godot.NET控制台程序完整路径'
.\Run_Tests.ps1 -GodotPath '你的Godot.NET控制台程序完整路径' -Headless
.\Run_Tests.ps1 -GodotPath '你的Godot.NET控制台程序完整路径' -Suite feel
```

支持 `all`、`feel`、`scene-paths`、`jump-landing`、`attacks`、`stairs`、`focused`。启动器隔离APPDATA后运行，磁盘测试不会覆盖玩家正式存档。测试失败会返回非零退出码；实际结果写入 `Documentation/csharp_*_results.json`。

本次已执行的检查与未完成的引擎检查见 `CSharp_Verification_Report.json`。C#回归套件已提供；本机缺Godot .NET与Godot.NET.Sdk包、无法联网取得依赖，因此没有宣称完成整个Godot工程的编译或运行验证。
