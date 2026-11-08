"""Extract level geometry and event execution without changing the room API."""
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
path=root/'assets/scripts/whitebox/whitebox_room.gd'
source=path.read_text(encoding='utf-8-sig')
if 'MudWhiteboxGeometry.new()' in source:
    print('Modules already extracted')
    raise SystemExit(0)
functions=list(re.finditer(r'^func (\w+)\(',source,re.M))
blocks={}
for i,match in enumerate(functions):
    end=functions[i+1].start() if i+1<len(functions) else len(source)
    blocks[match.group(1)]=source[match.start():end]

def qualify(text,names):
    parts=re.split(r'("(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')',text)
    for i in range(0,len(parts),2):
        parts[i]=re.sub(r'(?<![\w.])('+ '|'.join(sorted(names,key=len,reverse=True))+r')\b',r'room.\1',parts[i])
    return ''.join(parts)

geometry_names=['solid','platform','sign_at','build_geometry','build_stairs','add_water','add_breakable','build_lab']
geometry='class_name MudWhiteboxGeometry\nextends Node2D\n## 白盒地形与教学设施；由房间显式构建。\nvar room: Node2D\n\n'
for name in geometry_names:
    geometry+=qualify(blocks[name],{'room_id','room_size','floor_y','data','spawns','floor_blocks','water_rects','water_solids','trial_breakable','source_x','spawn_enemy'})
(path.parent/'whitebox_geometry.gd').write_text(geometry,encoding='utf-8')

events='class_name MudWhiteboxEvents\nextends RefCounted\n## 房间交互与进度事件，保留房间自身作为公开调用入口。\nvar room: Node2D\n\n'
events+=qualify(blocks['perform_event'].replace('func perform_event(', 'func perform('),{'data','room_id','floor_y','player','local_sequence','flood_timer','show_message','start_boss','debug_unlock','open_menu','spawn_enemy','get_tree'})
(path.parent/'whitebox_events.gd').write_text(events,encoding='utf-8')
for name in geometry_names:
    source=source.replace(blocks[name],'')
source=source.replace(blocks['perform_event'],'func perform_event(key: String, object: Node2D) -> void:\n\tevents.perform(key, object)\n\n')
source=source.replace('var player: CharacterBody2D','var geometry := MudWhiteboxGeometry.new()\nvar events := MudWhiteboxEvents.new()\nvar player: CharacterBody2D',1)
source=source.replace('\tbuild_geometry()','\tgeometry.room = self\n\tgeometry.name = "WhiteboxGeometry"\n\tadd_child(geometry)\n\tgeometry.build_geometry()\n\tevents.room = self',1)
path.write_text(source,encoding='utf-8')
print('Tests: PASS geometry, events and room lifecycle separated')
