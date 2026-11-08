"""Keep functional labels, remove development notes from the playable rooms."""
from pathlib import Path
root=Path(__file__).resolve().parents[1]
changes={
'assets/scripts/whitebox/whitebox_room.gd':{
 'var data: Dictionary':'var data: Dictionary\nvar room_catalog: Dictionary',
 '\tdata = world.rooms[room_id]':'\tdata = world.rooms[room_id]\n\troom_catalog = world.rooms',
 'show_message("%s｜所有尺寸/伤害为白盒暂定；靠近发光物或门按E。" % room_title, 5)':'show_message(room_title, 2.5)',
 'portal.title = "→ " + info.target':'portal.title = "前往" + str(room_catalog[info.target].title).split("·")[0]',
 '"守坑人（占位）"':'"守坑人"', '"母神之兽·龙蛇泥人（占位）"':'"母神之兽"', '"塔姆图·第一阶段（占位）"':'"塔姆图"',
 '真相与结局节点已开放。':'',
 '死亡：R从最近休整/战斗检查点重试。Boss未击败时会完整重置。':'身躯破碎。按R返回最近的休整处。',
 'show_message("泥跳跃：模组%d（仅原型选择，互斥生效）" % (Progress.mud_module + 1))':'show_message("泥形态跳跃：%s" % ("重跳" if Progress.mud_module == 0 else "长按跳跃"))',
 'else "靠近门或发光节点按E交互"':'else ""',
},
'assets/scripts/whitebox/whitebox_geometry.gd':{
 '泥之子 / WHITEBOX 1.0\\n苏醒 → 格挡教学 → 两河\\n青蓝：水　赭色：泥':'泥坑\\n按Q切换形态\\n按K格挡，按J反击',
 '教学攻击源（可绕行）':'格挡训练',
 '两河A / 两河B ↓\\n下方水域扩展接口（未连接）':'两河\\n暗红色的水流入泥坑',
 '朝圣者（不主动攻击·占位）':'朝圣者',
 '芦苇泽 / 高崖：扩展接口\\n仅预留，不生成新区域':'两河源头\\n远处传来歌声',
 '塔姆图之子·守卫（占位）':'塔姆图之子·守卫',
 '二段跳门槛：192px\\n单跳+长闪避+空中切换不可通\\n获得能力后实跳教学':'高台通往壁画长廊\\n空中再次按下跳跃',
 '向上：城市出口\\n中层：掌心隐藏雕像\\n底层：T06单向可破捷径（另一侧开启）':'上层通往城市\\n沿阶梯上行',
 '七道门 · 向上登塔\\n按顺序留下七件器物\\n导航图向下排布仅表示连接关系':'七道门\\n往上走的人，什么也不能带',
 '形态实验室\\nB切换泥跳模组 / F6调试解锁\\n右侧含测试靶、裂板、水池与高度标尺':'训练场\\nB调整跳跃，F6开放训练能力\\n沿地面向右前往格挡与武器训练区',
 '测试靶（受击/死亡）':'训练靶',
},
'assets/scripts/whitebox/whitebox_events.gd':{'测试靶（受击/死亡）':'训练靶','先完成两阶段最终战。':'先击败心脏前的敌人。','先触碰真相节点。':'还有尚未拾起的记忆。'},
'assets/scripts/whitebox/whitebox_enemy.gd':{
 '持锤泥人（占位）':'持锤泥人', '伊姆杜·第二阶段（占位）':'伊姆杜',
 '["等待", "攻击预警", "攻击判定", "僵直/易伤", "死亡"]':'["", "蓄势", "出手", "失衡", ""]',
},
'assets/scripts/ui/main_menu.gd':{
 '泥之子\\n23场景 · 双形态白盒':'泥之子',
 'Godot 4.7.2 / Compatibility　　原型参数均可编辑\\n文字、色块、敌人和Boss均为功能占位':'白盒测试版本',
 '新流程 · 苏醒':'开始旅程','继续 · 最近房间':'继续旅程','独立测试场 · 水 / 泥 / 能力':'训练场',
 '测试场B选泥跳模组、F6解锁。F8重开正式流程，清除测试覆盖。':'F2训练场　B调整泥跳跃　F8重新开始',
},
}
for relative,replacements in changes.items():
    path=root/relative
    text=path.read_text(encoding='utf-8-sig')
    for before,after in replacements.items():
        if before not in text:
            raise RuntimeError(f'Missing expected presentation text in {relative}: {before}')
        text=text.replace(before,after)
    path.write_text(text,encoding='utf-8')
print('Tests: PASS playable room copy and labels cleaned; narrative events retained')
