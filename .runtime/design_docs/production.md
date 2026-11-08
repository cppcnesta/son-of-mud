## 白盒制作与模块管理

当前运行入口为assets/scenes/ui/main_menu.tscn，共用玩家场景为assets/scenes/player/player.tscn。23个主线关卡统一位于assets/scenes/whitebox/levels，独立训练场位于assets/scenes/whitebox/tests/form_lab.tscn。旧练习场景放在whitebox/legacy，少量原入口仅保留继承兼容，不计入主线房间数。

### 数据与代码职责

|模块|职责|维护位置|
|---|---|---|
|形态参数|移动、闪避、格挡、攻击、跳跃数值|assets/data/player内water.tres和mud.tres|
|全局原型规则|初始形态、能力、切换与跳跃模组|assets/data/player/prototype_rules.tres|
|动作与玩家组件|动作互斥、攻击阶段、格挡、曲线恢复|assets/scripts/player/components|
|伤害判定|攻击ID、重复命中记录、可破坏物|assets/scripts/combat|
|房间数据|尺寸、接口、事件和进度条件|assets/data/whitebox/world.json|
|场景文案|正式名称和事件文本基线|assets/data/whitebox/scene_copy.json|
|房间切换|目标房间、出生点、到达保护|assets/scripts/world|
|进度与存档|能力、关卡标记、检查点|assets/scripts/progression|
|白盒场景|几何、事件、占位敌人与Boss|assets/scripts/whitebox|
|反馈与输入|中文HUD、菜单、补充输入绑定|assets/scripts/ui与core|

玩家控制器维持一套；形态参数、动作、攻击判定、能力解锁、房间连接和关卡进度分别管理。中文Inspector按形态、闪避、格挡、攻击、后摇衔接、跳跃分组。资源类型与单位明确标注，暂定数值不写作正式平衡。

恢复曲线资源为assets/data/player/curves/recovery_ease_out.tres。mud.tres中的penalty_duration控制闪避后1秒减速，attack_recovery控制1.5秒攻击收势，block_cancel_window控制末尾0.5秒格挡窗口。格挡CD与成功后的重置行为在两个形态资源分别设置。

### 正式美术资源预留

assets/art下预留characters、environment、ui、effects、fonts与audio。角色再区分player、npcs、enemies、bosses；环境再区分tilesets、props、backgrounds；音频再区分music、sfx、voice。当前这些目录用于后续正式资源接入；现有色块、测试贴图和开发引用仍保留在whitebox相关目录。

场内文案使用地名、人物名、器物名和直接操作提示。移除剧情画面中的开发解释；资源缺口、占位状态与制作限制记入开发文档。调试HUD与训练场仍保留必要的中文数据反馈。

### 输入与反馈

|操作|默认键位|补充|
|---|---|---|
|水平移动|A和D|保留工程已有绑定|
|跳跃|Space或W|二段跳需能力解锁|
|切形态|Q|动作状态与冷却限制仍有效|
|闪避|Shift|泥攻击后摇可衔接|
|攻击与格挡|J和K|格挡需检查CD|
|重压下砸|空中S加J|需解锁，当前与下劈回弹尚未分配独立输入|
|互动与重试|E和R|门、事件、休整、Boss挑战|
|泥跳跃模组选择|B|训练场及M04、C03、H02休整区|
|训练场与调试解锁|F2和F6|调试解锁独立于正式进度|
|重新开始正式流程|F8|清理测试能力残留|
|返回菜单|Esc|主菜单可选择正式流程或训练|

中文HUD显示当前房间、形态、泥跳跃模组、动作状态、闪避CD、格挡窗口与CD、攻击间隔、格挡增伤、泥减速来源及剩余时间、已解锁能力。训练场包含安全地面、单跳与二段跳标尺、可攀墙面、测试靶、格挡攻击源、下砸障碍和水池。

### 进度与失败恢复

正式进度记录能力、事件标记、三街区完成、七道门、Boss击败、捷径及检查点。存档为user目录下的mudchild_whitebox_v1.json，先写临时文件再替换。训练场调试能力不写入正式存档。

进入Boss挑战后封锁战斗房接口；玩家死亡可重试，未击败Boss复位。胜利后写入击败标记并开放出口。快捷门开启结果永久保存。死亡、传送及过门不得残留跳跃范围伤害资格。

## 已有验证与制作缺口

以下为工程已留存结果的归档，本次文档整理未重新执行游戏测试。自动检查通过不能代替连续真人游玩、手感验收和正式资源验收。

|既有记录|结果|覆盖范围|
|---|---|---|
|完整图形运行检查|Tests: PASS，305项，0失败|形态动作、跳跃、门控、23房间、48接口、Boss流程与存档|
|战斗手感专项|Tests: PASS，48项，0失败|1秒和1.5秒曲线、格挡末段衔接、Shift覆盖、CD刷新|
|场景路径迁移回归|Tests: PASS，72项，0失败|真实依赖、旧入口兼容、主线数量、接口与条件|

这些记录存在覆盖交集，不相加为独立测试总量。日志记录的环境为Godot4.7.2、Windows图形窗口、Compatibility渲染器、60Hz。主要依据为Documentation内Test_Report.md、runtime_results.json、combat_feel_results.json和scene_migration_results.json。

目前尚未完成正式角色与场景美术、完整Boss动作组、完整CG、配音音乐、正式难度平衡及连续真人通关验收。未验证的范围包括手柄、其他硬件和帧率、导出exe、完整不间断M01至结局真人流程，以及回去睡后二次循环的完整章节闭环。供桌奖励效果、跪拜泥人奖励差异、七道门与能力剥离、完整收集品背包和叙事补全表现仍需单独落实验收，不能仅凭有事件文字即判定全部完成。

## 下劈回弹待确认构想

下劈回弹目前停留在方案讨论，尚未接入工程，也不计入已完成机制。拟议逻辑是空中向下攻击实际命中可回弹目标后，施加独立向上速度；每次攻击最多回弹一次。目标是否可回弹应与是否受伤分别配置，避免无敌敌人或机关阻断预期交互。

仍需确认独立输入、回弹高度、连续回弹规则、两种形态差异、是否刷新空中行动，以及与重压下砸的区分。当前建议不自动刷新跳跃和闪避次数；回弹应清除泥模组2的落地伤害资格。上述建议尚未定案，实施前应重新验证二段跳门控与Boss场地。
