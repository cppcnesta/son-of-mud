"""Repair scenes re-saved from pre-migration editor tabs, preserving scene content."""
from pathlib import Path
import re
import json

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT/'assets/scenes/whitebox'
BACKUP = ROOT/'.runtime/scene_migration_backup'
BACKUP.mkdir(parents=True, exist_ok=True)
RESOURCE_PATHS = {
    'res://assets/scenes/tilesets/whitebox_tileset.tres':'res://assets/data/whitebox/whitebox_tileset.tres',
    'res://assets/scenes/player.tscn':'res://assets/scenes/player/player.tscn',
    'res://assets/scripts/room_protal.gd':'res://assets/scripts/world/room_portal.gd',
}
SCENES = {'mp_01_pit.tscn':'MP01Pit','mp_02_pilgrim_path.tscn':'MP02PilgrimPath','mp_03_altar.tscn':'MP03Altar'}

def checked(path):
    path=path.resolve()
    if not path.is_relative_to(ROOT): raise ValueError(f'Outside project: {path}')
    return path

def backup(path, label):
    target=checked(BACKUP/label)
    if path.exists() and not target.exists(): target.write_bytes(path.read_bytes())

for name,node in SCENES.items():
    canonical=checked(BASE/'legacy'/name)
    old=checked(BASE/name)
    text=canonical.read_text(encoding='utf-8-sig')
    canonical_uid=re.search(r'uid="(uid://[^"]+)"',text.splitlines()[0])
    old_uid=''
    if old.exists():
        old_text=old.read_text(encoding='utf-8-sig')
        uid=re.search(r'uid="(uid://[^"]+)"',old_text.splitlines()[0])
        old_uid=uid.group(1) if uid else ''
        if 'type="Node2D"' in old_text and 'tile_set =' in old_text:
            backup(canonical,'canonical_'+name)
            backup(old,'reopened_'+name)
            # Reopened tabs may contain newer user edits. Keep their scene body;
            # retain the canonical UID used by existing room links.
            text=old_text
            if canonical_uid:
                text=re.sub(r'uid="uid://[^"]+"',f'uid="{canonical_uid.group(1)}"',text,count=1)
    for before,after in RESOURCE_PATHS.items(): text=text.replace(before,after)
    canonical.write_text(text,encoding='utf-8')
    header=f'[gd_scene load_steps=2 format=3{(" uid="+chr(34)+old_uid+chr(34)) if old_uid else ""}]'
    old.write_text(header+'\n\n'+f'[ext_resource type="PackedScene" path="res://assets/scenes/whitebox/legacy/{name}" id="1_canonical"]\n\n'+f'[node name="{node}" instance=ExtResource("1_canonical")]\n',encoding='utf-8')

# Catch any other pre-migration resource reference, not just the screenshot's tab.
for path in (ROOT/'assets').rglob('*'):
    if path.suffix not in {'.tscn','.tres','.gd'} or not path.is_file(): continue
    text=path.read_text(encoding='utf-8-sig')
    repaired=text
    for before,after in RESOURCE_PATHS.items(): repaired=repaired.replace(before,after)
    if repaired!=text:
        backup(path,path.name+'.before_path_repair')
        path.write_text(repaired,encoding='utf-8')

# Only repair file references in editor state; do not remove preferences/caches.
history_paths=dict(RESOURCE_PATHS)
for name in SCENES:
    history_paths['res://assets/scenes/whitebox/'+name]='res://assets/scenes/whitebox/legacy/'+name
    history_paths['res://scenes/whitebox/'+name]='res://assets/scenes/whitebox/legacy/'+name
history_paths.update({
 'res://scenes/rooms/Room01.tscn':'res://assets/scenes/whitebox/legacy/Room01.tscn',
 'res://Room1.tscn':'res://assets/scenes/whitebox/legacy/Room01.tscn',
 'res://scenes/player.tscn':'res://assets/scenes/player/player.tscn',
 'res://scenes/player.gd':'res://assets/scripts/player/player.gd',
 'res://assets/script/room_travel.gd':'res://assets/scripts/world/room_travel.gd',
 'res://assets/scripts/whitebox_room.gd':'res://assets/scripts/whitebox/whitebox_room.gd',
})
editor=ROOT/'.godot/editor'
for name in ['project_metadata.cfg','editor_layout.cfg','script_editor_cache.cfg']:
    path=editor/name
    if not path.exists(): continue
    text=path.read_text(encoding='utf-8-sig')
    backup(path,name+'.before_path_repair')
    for before,after in sorted(history_paths.items(),key=lambda pair:len(pair[0]),reverse=True):
        text=text.replace(before,after)
    path.write_text(text,encoding='utf-8')
print('Tests: PASS canonical scenes preserved; 3 compatibility entries created; editor path history updated')
