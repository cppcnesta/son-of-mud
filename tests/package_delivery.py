"""Create verified handoff inventory, test report and clean source archive."""
from pathlib import Path
import json
import re
import zipfile

root=Path(__file__).resolve().parents[1]
docs=root/'Documentation'
world=json.loads((root/'assets/data/whitebox/world.json').read_text(encoding='utf-8'))
result=json.loads((docs/'runtime_results.json').read_text(encoding='utf-8'))
if result['failures']:
    raise RuntimeError('Runtime suite has failures; do not package as passed')

# Assert every literal res:// reference is present. UID fallback remains intact in original scenes.
missing=[]
scripts=list(root.glob('assets/**/*.gd'))+list(root.glob('assets/**/*.tscn'))+list(root.glob('assets/**/*.tres'))
for source in scripts:
    for ref in re.findall(r'res://[^\s"\'\)]+',source.read_text(encoding='utf-8-sig')):
        if '%' in ref or ref.endswith('/') or ref.endswith('.json'):
            continue
        if not (root/ref[6:]).exists(): missing.append((str(source.relative_to(root)),ref))
if missing: raise RuntimeError(f'Missing resource references: {missing}')
for name in ['import.log','runtime_suite.log','combat_feel_suite.log','scene_migration_suite.log']:
    text=(docs/name).read_text(encoding='utf-8-sig')
    if 'SCRIPT ERROR:' in text or '\nERROR:' in text or 'Tests: FAIL' in text:
        raise RuntimeError(f'Unresolved errors in {name}')

metrics=result['metrics']
scene_result=json.loads((docs/'scene_migration_results.json').read_text(encoding='utf-8'))
if scene_result['failures']: raise RuntimeError('Scene migration checks failed')
report=['# 实际运行验证报告','',f"本次场景迁移回归：**Tests: PASS，{scene_result['checks']}项，失败0项**。",'',f"上一轮完整玩法图形检查：Tests: PASS，{result['checks']}项，失败0项。动作代码未改，本次未重复执行这组完整检查。",'',
'实际Godot4.7.2、Windows图形窗口、OpenGL兼容渲染、RTX4060 Laptop GPU。以60Hz物理步进执行；使用真实Godot输入与碰撞判定，未使用无头解析代替玩法运行。', '',
'导入/解析日志：import.log；完整运行：runtime_suite.log；机器可读结果：runtime_results.json。其他回归日志保留用于追溯。', '',
'## 测量结果（当前白盒建议值）','', '|项目|水|泥模组1|泥模组2|','|---|---|---|---|',
f"|1秒水平移动|{metrics['move_0']:.1f}px|{metrics['move_1']:.1f}px|同泥基础移动|",
f"|一次闪避实际距离|{metrics['dodge_0']:.1f}px|{metrics['dodge_1']:.1f}px|同泥基础闪避|",
f"|最大单跳高度|{metrics['water_jump']['height']:.1f}px|{metrics['mud1_jump']['height']:.1f}px|{metrics['mud2_full']['height']:.1f}px|",
f"|单跳水平移动距离|{metrics['water_jump']['distance']:.1f}px|{metrics['mud1_jump']['distance']:.1f}px|{metrics['mud2_full']['distance']:.1f}px|",
f"|模组2短按跳高度|—|—|{metrics['mud2_short']['height']:.1f}px|", '',
'实测距离随60Hz离散帧取整，故与速度×配置时长的连续计算值略有差别。192px高台高于所有未升级单跳实测上限；正反形态的空中切换+长闪避组合实际测试均未绕过，二段跳解锁后实际可跃上。', '',
'## 运行范围','',
f"- 23个主线场景全部实际实例化运行；{sum(len(r['ports']) for r in world['rooms'].values())}条有向接口全部实际切换、校验目标Marker位置及无传送循环。",
'- 动作：两形态移动/闪避、真实攻击伤害与三阶段、格挡窗口与CD、命中去重、增伤打空保留/命中消费、1秒/1.5秒曲线恢复、后摇衔接、两泥跳模组互斥与合格落地资格。',
'- 能力：二段跳高台、攀墙出口、潜游水面及池底征兆、重压下砸破障碍，均包含未解锁阻挡与解锁后实际通行/命中。',
'- 进度：三街区条件、征兆顺序、七门顺序、T06侧破门与永久双向、Boss锁门/死亡重试/击败开门、最终两阶段、真相和醒来结局、实际磁盘存读与测试能力隔离。',
'- 纵向路线：T05/H01实际由移动和跳跃登阶梯并抵达顶部出口；主菜单实际运行；A/Q物理键事件通过InputMap驱动。', '',
'- 新增空引用回归：水、泥两模组的起跳前摇中先落地、待起跳资源冻结、跳数消费、受伤取消、真实过门取消及落地范围攻击资格。修复前失败记录另见jump_landing_before_fix.log；当前结果见jump_landing_suite.log及完整运行日志。', '',
'## 未验证与尚未制作','',
'未验证从M01开始由真人连续操作到两个结局的完整手感；回去睡的二次交互回环代码存在，未将其计为真人通关验证。未验证手柄、其他硬件/帧率、独立exe导出。桌面自动化短按键未可靠传入，未计为人工键盘验证。', '',
'角色/敌人/Boss均为色块。龙蛇当前为位置锁定下砸与换位失衡占位，正式冲拳/合掌/抓握动画和完整招式集、CG、配音、音效、经济掉落和最终平衡尚未制作。', '',
'## 本次场景迁移断言','',]
report += [f'- {line}' for line in scene_result['results']]
report += ['', '## 上一轮完整玩法断言', '']
report += [f'- {line}' for line in result['results']]
(docs/'Test_Report.md').write_text('\n'.join(report)+'\n',encoding='utf-8')

inventory=['# 实际修改与新增文件','', '## 修改已有文件','', '|路径|用途|','|---|---|',
'|project.godot|主入口、自动加载、基础分辨率、补充输入；Compatibility保留|',
'|assets/scripts/player/player.gd|唯一玩家控制器：形态、动作请求、运动与空中上下文|',
'|assets/scripts/world/room_travel.gd|复用切场入口，增加冷却/减速传递、重试、出生点与存档定位|',
'|assets/scripts/world/room_portal.gd|修正原protal拼写，保留接口与条件，正式化目的地提示|','',
'关卡与练习场景统一迁入scenes/whitebox。共享玩家场景迁入scenes/player，保留节点、UID与碰撞尺寸；所有引用同步。地图源文件、白盒贴图、正式美术预留目录分开管理。旧练习场景不参与新23房间连接图。', '',
'## 新增实现文件','', '|路径|用途|','|---|---|']
purpose={
'action_state.gd':'动作枚举、独立计时与中文名称', 'attack_ledger.gd':'同一攻击ID去重', 'breakable.gd':'重压下砸障碍及持久破坏',
'form_config.gd':'水泥形态配置资源类型', 'player_combat.gd':'前摇/判定/后摇、命中查询、增伤与落地范围攻击',
'prototype_rules.gd':'正式初始状态、切换/无敌/泥模组/Boss原型规则', 'room_event.gd':'交互/叙事节点',
'whitebox_enemy.gd':'占位敌人/Boss预警、攻击、受伤、僵直、两阶段与死亡', 'debug_hud.gd':'中文状态、操作与交互提示',
'input_defaults.gd':'仅补缺项输入', 'main_menu.gd':'新流程/继续/测试场', 'progress.gd':'能力/进度/检查点/磁盘存档与测试隔离',
'whitebox_room.gd':'房间生命周期、接口、Boss流程与模块调度',
'whitebox_geometry.gd':'独立白盒几何、平台、教学设施与测试池构建',
'whitebox_events.gd':'独立剧情交互、街区谜题和能力获取事件',
'movement_recovery.gd':'统一曲线减速、覆盖规则与跨房间快照',
'player_guard.gd':'格挡窗口、CD、成功刷新与攻击后摇衔接',
'recovery_ease_out.tres':'可编辑恢复曲线，默认2t−t²',
'scene_copy.json':'正式化场景文案基线',
'combat_feel_checks.gd':'曲线、收势衔接、格挡CD与目录回归',
'water.tres':'水形态可编辑参数', 'mud.tres':'泥形态可编辑参数', 'prototype_rules.tres':'全局暂定规则可编辑实例',
'world.json':'23场景尺寸、48条有向接口、出生点、事件与未连接扩展接口',
'main_menu.tscn':'F5入口', 'form_lab.tscn':'独立形态/能力测试场', 'runtime_suite.gd':'真实图形/物理/输入运行检查',
'player.tscn':'共享玩家场景；保留节点、UID及碰撞尺寸，更新模块引用',
'whitebox_tileset.tres':'原白盒TileSet资源迁移并更新测试贴图引用',
'runtime_suite.tscn':'运行检查入口', 'build_world.py':'可复现房间数据/场景/接口文档生成基线', 'package_delivery.py':'引用/日志校验、清单与打包',
'repair_scene_migration.py':'保留场景内容、修复旧依赖、建立兼容入口并修正编辑器路径历史',
'scene_migration_checks.gd':'32个场景的依赖、实例化与图形运行回归',
'Run_Tests.ps1':'一键导入及图形测试；使用独立测试数据目录',}
modified={'assets/scripts/player/player.gd','assets/scripts/world/room_travel.gd','assets/scripts/world/room_portal.gd'}
paths=list(root.glob('assets/scripts/**/*.gd'))+[p for p in root.glob('assets/data/**/*') if p.is_file()]+list(root.glob('assets/scenes/whitebox/levels/*.tscn'))+list(root.glob('assets/scenes/whitebox/tests/*.tscn'))+[root/'assets/scenes/ui/main_menu.tscn',root/'assets/scenes/player/player.tscn']+list(root.glob('tests/*.gd'))+list(root.glob('tests/*.tscn'))+list(root.glob('tests/*.py'))+[root/'Run_Tests.ps1']
paths += list(root.glob('assets/scenes/whitebox/legacy/*.tscn')) + [root/'assets/art/README.md',root/'assets/reference/whitebox/Muddson.ai',root/'assets/textures/whitebox/kenney_platformer.webp']
paths += list(root.glob('assets/scenes/whitebox/mp_*.tscn'))
for path in sorted(set(paths)):
    relative=path.relative_to(root).as_posix()
    if relative in modified or path.suffix=='.uid': continue
    description=purpose.get(path.name, f"独立主线房间{path.stem}，共享白盒构建逻辑")
    if 'legacy' in path.parts: description='原练习场景移入白盒留档目录，不接入主线'
    elif path.parent==root/'assets/scenes/whitebox': description='旧编辑器路径兼容入口，继承legacy场景，不复制关卡内容'
    elif path.name=='README.md': description='正式资源预留目录规范'
    elif path.suffix=='.ai': description='原地图源文件迁入白盒参考目录'
    elif path.suffix=='.webp': description='原测试贴图迁入白盒贴图目录'
    inventory.append(f'|{relative}|{description}|')
inventory+=['','## 交付文档和证据','',
'README_Whitebox_V1_0.md：运行入口、操作、Inspector、确认/暂定规则、进度、未完成项。',
'Room_Manifest.md：完整尺寸、接口、目标房间与出生点。Test_Report.md：实际结果与未验证范围。',
'Jump_Landing_Fix.md：本次jump_speed空引用的原因、修复与回归证据。',
'Combat_Feel_Update.md：当前手感规则、确认/暂定参数、目录与文案调整。',
'Scene_Path_Repair.md：旧场景缺失依赖的原因、修复和本次验证范围。',
'runtime_results.json及配套日志/截图：可复查的运行证据。map_source_preview.png：实际.ai的本地渲染参考。',
'Original_Project_20261105.zip：修改前工程备份（project.godot和assets）。']
(docs/'File_Changes.md').write_text('\n'.join(inventory)+'\n',encoding='utf-8')

output=root/'Delivery/MudChild_Whitebox_V1_0.zip'
output.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED) as archive:
    for path in root.rglob('*'):
        relative=path.relative_to(root)
        if relative.parts[0] not in {'assets','Documentation','tests','project.godot','icon.svg','Run_Tests.ps1','export_presets.cfg'}: continue
        if relative.parts[0]=='Delivery': continue
        if any(part.startswith('.') for part in relative.parts): continue
        if path.is_dir():
            if path.is_relative_to(root/'assets/art'): archive.write(path,relative.as_posix()+'/')
            continue
        if path.suffix in {'.import','.pyc'}: continue
        archive.write(path,relative.as_posix())
print(f'Tests: PASS {scene_result["checks"]} current scene migration checks; previous gameplay baseline {result["checks"]}; resource references and current logs valid')
print(f'Delivery: {output} ({output.stat().st_size} bytes)')
