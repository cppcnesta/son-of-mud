"""One-time, idempotent asset migration; validates all moves stay inside this project."""
from pathlib import Path
import shutil

root = Path(__file__).resolve().parents[1]
files = {
 'assets/scenes/player.tscn':'assets/scenes/player/player.tscn',
 'assets/scenes/main_menu.tscn':'assets/scenes/ui/main_menu.tscn',
 'assets/scenes/testing/form_lab.tscn':'assets/scenes/whitebox/tests/form_lab.tscn',
 'assets/scenes/rooms/Room01.tscn':'assets/scenes/whitebox/legacy/Room01.tscn',
 'assets/scenes/tilesets/whitebox_tileset.tres':'assets/data/whitebox/whitebox_tileset.tres',
 'assets/using_tilesets_kenney_abstract_platformer_tile_sheet.webp':'assets/textures/whitebox/kenney_platformer.webp',
 'Muddson.ai':'assets/reference/whitebox/Muddson.ai',
 'assets/data/water.tres':'assets/data/player/water.tres',
 'assets/data/mud.tres':'assets/data/player/mud.tres',
 'assets/data/prototype_rules.tres':'assets/data/player/prototype_rules.tres',
 'assets/data/world.json':'assets/data/whitebox/world.json',
 'assets/scripts/player.gd':'assets/scripts/player/player.gd',
 'assets/scripts/room_travel.gd':'assets/scripts/world/room_travel.gd',
 'assets/scripts/room_protal.gd':'assets/scripts/world/room_portal.gd',
 'assets/scripts/debug_hud.gd':'assets/scripts/ui/debug_hud.gd',
 'assets/scripts/main_menu.gd':'assets/scripts/ui/main_menu.gd',
 'assets/scripts/input_defaults.gd':'assets/scripts/core/input_defaults.gd',
 'assets/scripts/progress.gd':'assets/scripts/progression/progress.gd',
 'assets/scripts/whitebox_room.gd':'assets/scripts/whitebox/whitebox_room.gd',
 'assets/scripts/components/action_state.gd':'assets/scripts/player/components/action_state.gd',
 'assets/scripts/components/attack_ledger.gd':'assets/scripts/combat/attack_ledger.gd',
 'assets/scripts/components/breakable.gd':'assets/scripts/combat/breakable.gd',
 'assets/scripts/components/form_config.gd':'assets/scripts/player/components/form_config.gd',
 'assets/scripts/components/player_combat.gd':'assets/scripts/player/components/player_combat.gd',
 'assets/scripts/components/prototype_rules.gd':'assets/scripts/player/components/prototype_rules.gd',
 'assets/scripts/components/room_event.gd':'assets/scripts/whitebox/room_event.gd',
 'assets/scripts/components/whitebox_enemy.gd':'assets/scripts/whitebox/whitebox_enemy.gd',
}
for scene in (root/'assets/scenes/mainline').glob('*.tscn'):
    files[scene.relative_to(root).as_posix()] = 'assets/scenes/whitebox/levels/'+scene.name
for scene in (root/'assets/scenes/whitebox').glob('mp_*.tscn'):
    files[scene.relative_to(root).as_posix()] = 'assets/scenes/whitebox/legacy/'+scene.name

def checked(relative):
    path=(root/relative).resolve()
    if not path.is_relative_to(root): raise ValueError(path)
    return path

for source,destination in files.items():
    for suffix in ['', '.uid', '.import']:
        src,dst=checked(source+suffix),checked(destination+suffix)
        if not src.exists(): continue
        if dst.exists(): raise FileExistsError(dst)
        dst.parent.mkdir(parents=True,exist_ok=True)
        shutil.move(str(src),str(dst))

replacements=dict(files)
replacements.update({
 'assets/scenes/mainline':'assets/scenes/whitebox/levels',
 'assets/scenes/testing':'assets/scenes/whitebox/tests',
 'assets/scenes/tilesets':'assets/data/whitebox',
})
for path in root.rglob('*'):
    if not path.is_file() or any(part.startswith('.') for part in path.relative_to(root).parts): continue
    if path == Path(__file__).resolve() or path.suffix not in {'.gd','.tscn','.tres','.godot','.py','.ps1','.md','.json','.import'}: continue
    text=path.read_text(encoding='utf-8-sig')
    original=text
    for before,after in sorted(replacements.items(),key=lambda p:len(p[0]),reverse=True):
        text=text.replace(before,after)
    if text!=original: path.write_text(text,encoding='utf-8')

art_directories=[
 'characters/player','characters/npcs','characters/enemies','characters/bosses',
 'environment/tilesets','environment/props','environment/backgrounds',
 'ui','effects','fonts','audio/music','audio/sfx','audio/voice',
]
for relative in art_directories: checked('assets/art/'+relative).mkdir(parents=True,exist_ok=True)
(root/'assets/art/README.md').write_text('''# 正式资源目录

此目录预留正式美术与音频。白盒关卡、测试贴图和参考图分别保存在scenes/whitebox、textures/whitebox与reference/whitebox中。

|目录|资源|
|---|---|
|characters/player|主角水、泥形态的Sprite、动画与材质|
|characters/npcs|剧情角色|
|characters/enemies|普通敌人|
|characters/bosses|Boss外观与动画|
|environment/tilesets|正式地形TileSet与贴图|
|environment/props|场景道具|
|environment/backgrounds|背景与远景|
|ui|界面图像|
|effects|战斗及环境特效|
|fonts|项目授权字体|
|audio/music|配乐|
|audio/sfx|音效|
|audio/voice|语音|
''',encoding='utf-8')
for path in sorted((root/'assets').rglob('*'),key=lambda p:len(p.parts),reverse=True):
    if path.is_dir() and not any(path.iterdir()) and not path.is_relative_to(root/'assets/art'):
        checked(path.relative_to(root)).rmdir()
print('Tests: PASS layout migrated; all 23 levels and tests are under scenes/whitebox; art folders reserved')
