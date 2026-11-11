"""Reproducible whitebox data/scenes (run from repo root: python tools/build_world.py). Runtime reads world.json; no editor plugin required."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
spec = [
 ('M01','苏醒',1,1),('M02','坑底·两河',1.2,1.5),('M03','朝圣者之路',3,1),
 ('M04','供桌',.8,1),('M05','守坑人',1.8,1.2),('M06','七夜·两河源头',1.2,1.2),
 ('T01','工地入口',1.3,1.35),('T02','壁画长廊',1.5,1.5),('T03','水闸',1.4,1.3),
 ('T04','龙蛇泥人',2.2,1.8),('T05','战后雕像·城市出口',1.2,2.5),('T06','最终入口·捷径',1,1),
 ('C01','城门',1,1),('C02','跪拜街道',2,1),('C03','底层神殿·伊吉',1.8,1.2),
 ('C04','第一街区·期望的世界',1.5,1.3),('C05','第二街区·门',1.5,1.3),('C06','第三街区·诅咒',1.5,1.3),
 ('C07','档案室',1.2,1),('C08','过渡祭道',.8,1),('H01','七道门·登塔',1.8,4.5),('H02','塔顶前厅',1.5,1),('H03','塔姆图·伊姆杜',2.5,1.8),
]
rooms = {}
for rid,title,w,h in spec:
    width = round(w*1152/64)*64
    height = round(h*648/64)*64
    rooms[rid] = dict(id=rid,title=title,screens=[w,h],size=[width,height],ports=[],events=[])

def connect(a,b,ra=None,rb=None,shortcut=False):
    for src,dst,req in [(a,b,ra or []),(b,a,rb or [])]:
        port = dict(id=f'{src}_to_{dst}',target=dst,spawn=f'From{src}',requires=req)
        if shortcut:
            port.update(shortcut='shortcut_T06_T05',break_side=src=='T06',bidirectional_after_break=True)
        rooms[src]['ports'].append(port)

chain = ['M01','M02','M03','M04','M05','M06','T01','T02','T03','T04','T05','C01','C02','C03']
for a,b in zip(chain,chain[1:]):
    requirements = {'M05':['boss_M05'],'T01':['ability:double_jump','tutorial_double'],'T02':['ability:wall_climb','tutorial_wall'],'T03':['flood_done'],'T04':['boss_T04']}.get(a,[])
    connect(a,b,requirements)
for a,b in [('C03','C04'),('C04','C05'),('C05','C06'),('C06','C03')]:
    connect(a,b)
connect('C03','C07',['districts','met_igi'])
for a,b in [('C07','C08'),('C08','T06'),('T06','H01'),('H01','H02'),('H02','H03')]:
    connect(a,b,['seven_gates'] if a=='H01' else (['districts'] if a=='T06' else []))
connect('T06','T05',shortcut=True)

# Ports sit in explicitly separated bays. Some progression ports are on real elevated ledges.
for rid,r in rooms.items():
    floor = r['size'][1]-64
    n = len(r['ports'])
    for i,p in enumerate(r['ports']):
        x = 80 if i==0 else r['size'][0]-80 if i==1 else round((i-1)*r['size'][0]/(n-1)/64)*64
        y = floor-24
        if rid=='C04': y -= 200
        if rid=='T01' and p['target']=='T02': y -= 192
        if rid=='T02' and p['target']=='T03': y -= 448
        if rid=='T05' and p['target']=='C01': y = 320
        if rid=='H01' and p['target']=='H02': y = 320
        p['position'] = [x,y]
        # Inward offset prevents arrival overlap/repeated transport.
        p['arrival'] = [x+(128 if x<r['size'][0]/2 else -128),y]

def event(rid,key,title,x=.5,y=None,text='',**extra):
    r=rooms[rid]
    r['events'].append(dict(id=key,title=title,position=[round(r['size'][0]*x/64)*64,r['size'][1]-88 if y is None else y],text=text,**extra))

event('M01','awakening','苏醒·泥板',.45,text='——我的儿子，醒来。\n我是父亲用血和泥捏出的第一个。父亲？您在哪里？\n[白盒文字演出，CG/配音待制作]')
event('M01','block_lesson','格挡练习',.72,text='K开启格挡。水：0.5秒且只挡一次，松开再按。泥：按住最多2秒，成功后下次有效命中增伤。')
event('M02','tablet_sky','当天还没有名字',.32,text='当天还没有名字，地也还没有名字。只有海。海底有泥，祂取出泥，混上自己的血……第七夜，……落下。')
event('M02','reflection','水面倒影',.7,text='穆尔图库尔：……水太浑了。\n水形态不等于潜游。下方水域为保留接口，不通往新增房间。')
event('M03','pilgrims','朝圣者（叙事占位）',.35,text='路边的泥人跪着，双手交握。它们并不主动攻击。\n？？？：杀了他们！')
event('M04','rest','供桌·休整/存档',.5,text='面包与清水。E吃下生命之面包并休整（白盒暂定恢复生命），B可选择泥跳模组。')
event('M05','arena','挑战守坑人',.24,text='成功格挡指定攻击，使守坑人短暂僵直并进入易伤。黄色预警，红色攻击。')
event('M06','seven_nights','七夜雕像',.48,text='CG1占位：七个夜晚，父亲用拇指按出他的身体。第七夜，他睁眼，一颗星从海面落下。\n伊姆杜：穆尔图库尔。我的儿子。')
event('M06','song','两河源头',.72,text='穆尔图库尔：有人在唱歌。\n？？？：别听。')
event('T01','unlock_double','二段跳·教学',.32,text='原型能力安排：现在获得二段跳。空中再次按跳跃，抵达右侧192像素高台；完成后才开放T02。',ability='double_jump')
event('T02','mural','壁画·笑容',.3,text='祂把风灌进她的嘴里，海就停了。祂举起弯刃，海就分成了两半。\n泥板「笑容」：她的血溅到我身上的时候，那是他第一次为我笑。')
event('T02','unlock_wall','攀墙·教学',.56,text='获得攀墙。贴住右侧墙面，朝墙按方向并按住W向上攀爬，抵达上层出口。',ability='wall_climb')
event('T03','flood','操作水闸',.45,text='？？？：打开它！他们太吵了。\n洪水冲进下层工地，歌声停下。[动态水色块/文字占位]')
event('T03','after_flood','洪水之后',.68,text='神不愿听到我们的声音，降下了大洪水。我们依旧向祂祈祷。我们唱得更小声了。')
event('T04','arena','挑战龙蛇泥人',.2,text='引导锁定下砸落空、使用二段跳/攀墙换位，可使占位Boss失衡。三套正式招式动画尚未制作。')
event('T05','sea_statue','塔姆图雕像',.3,text='CG3占位：母神张开巨大的双臂，抱住了他。你出生那夜，有一颗星落进我的水里。\n拾取「命运石板」。')
event('T05','meteor','掌心深处·流星',.7,y=900,text='隐藏回溯CG4占位：他的手在我头上，停了一颗星从天这头落到那头那么久。只有那么久。')
event('T06','final_entry','登塔入口',.48,text='伊吉：……我只听见祂的声音。\n？？？：我的儿子！上来。我在上面等你。')
event('C01','city_guard','城门守卫',.45,text='？？？：跪着的，是在拜我。放过他们。\n穆尔图库尔：……是，父亲。')
event('C02','relief','街边浮雕',.45,text='祂睡在泥里。祂是破碎的。祂没有醒。')
event('C03','igi','伊吉·泥肝地图',.28,text='我是伊吉，祂的伽拉。每死一个，我就唱一首。\n城里三个街区，各对应泥肝一块；找到征兆、解开谜题，回来开启档案室。')
event('C03','unlock_slam','重压下砸·获得',.52,text='城市原型能力安排：获得重压下砸。空中S+J，击碎黄色裂纹障碍。',ability='heavy_slam')
event('C03','rest','火盆·休整/存档',.69,text='休整并保存。B切换互斥泥跳模组。')
event('C04','unlock_dive','潜游·获得',.22,text='城市原型能力安排：获得潜游。水形态进入水池，W/S控制上下，读取池底征兆。',ability='dive')
event('C04','omen_1','池底征兆·期望的世界',.6,y=rooms['C04']['size'][1]-110,text='两条河。河边有田。人会耕种，会建城，会唱歌。城里有神庙，神庙里有人记住这个世界。')
event('C05','omen_2','裂板下的雕像·门',.58,y=rooms['C05']['size'][1]-88,text='CG5占位：父亲捏出第一个泥人，教它文字、耕种、占星。\n「他们太软了」：泥人那么软，一碰就碎。他为什么要碰它们？')
event('C06','sigil_1','征兆①',.28,text='暂定谜题：按①→②→③读取征兆（PDF未规定具体谜题）。')
event('C06','sigil_2','征兆②',.5)
event('C06','sigil_3','征兆③·诅咒',.72,text='弑亲者，命落其子。第七夜，（缺）……')
event('C07','archive','年记',.5,text='第一年，祂碎在泥里。我们捧来河泥，一点一点把祂补好。第三年，祂没有醒。第四十年，塔庙建到了第七层。第两百年，……')
event('C08','processional','伊吉的疑问',.48,text='伊吉：祂在跟谁说话？\n穆尔图库尔：跟父亲。你听不见吗？')
gate_texts=['我来到他的身边，他戴着众神的角冠，创造新的世界。','他没有抬头。我只听见一句话：穆尔图库尔，回去吧。','我有很多话想对他说，可喉咙里只有泥。','他终于抬头看我，很久以来他第一次看我。我也知晓了自己的命运。','我从腰间拔出弯刃。他教会我的，只有这一件事。','他抓住了我的手，那只手曾在我的头颅上停留过一颗星落下的时间。','刀进去的时候，他的血浸透了我的长袍。他叫我：我的儿子。']
for i,item in enumerate(['角冠','金耳环','项链','胸饰','腰带','手镯','长袍']):
    event('H01',f'gate_{i+1}',f'第{i+1}道门·留下{item}',.35 if i%2==0 else .65,y=rooms['H01']['size'][1]-120-i*384,text=f'留下{item}。泥板「{i+1}」：{gate_texts[i]}')
event('H02','rest','最后休整',.25,text='七件器物留在门前。能力不剥夺（原型暂定）。前方验证二段跳、潜游与重压下砸。')
event('H03','arena','触碰心脏·最终战',.18,text='塔姆图从心脏里升起。[两阶段Boss与演出均为占位]')
event('H03','truth','真相·名字',.68,text='CG6弑父、CG7第三个世界、CG8沉睡（文字占位）。\n他用父亲的尸体造了世界，然后走回泥坑。他听见的父亲，是自己的声音。\n穆尔图库尔：星落之时造成的武器。')
event('H03','ending_awake','结局：醒来',.8,text='心脏停了。塔庙里第一次这么安静。\n穆尔图库尔：父亲？……\n泥板「星」：他爱我。可是只有一颗星落下那么久。')
event('H03','ending_sleep','结局：回去睡',.88,text='伊吉跪在坑边唱歌。——我的儿子，醒来。再按E返回苏醒，开始新的循环。')

lab=dict(id='TEST',title='形态与能力测试场',screens=[3,1.5],size=[3456,960],ports=[],events=[])
rooms['TEST']=lab
for key,title,x in [('rest','测试休整',.07),('test_all','仅测试：解锁全部能力',.16),('test_clear','仅测试：清除扩展能力',.25),('test_source','格挡攻击源·开关',.42),('test_target','测试靶·复位',.55),('test_exit','返回主菜单',.94)]:
    event('TEST',key,title,x)

copy_overrides = json.loads((ROOT/'assets/data/whitebox/scene_copy.json').read_text(encoding='utf-8'))
for rid,room in rooms.items():
    for entry in room['events']:
        entry.update(copy_overrides.get(rid,{}).get(entry['id'],{}))
for rid,title in copy_overrides.get('_room_titles',{}).items():
    rooms[rid]['title'] = title
for i,entry in enumerate(rooms['H01']['events']):
    number = '一二三四五六七'[i]
    entry['title'] = entry['title'].replace(f'第{i+1}道门',f'第{number}道门')
    entry['text'] = entry['text'].replace(f'泥板「{i+1}」',f'泥板「{number}」')
rooms['TEST']['title'] = '训练场'
reserved = [dict(id='water_down',label='M02下方水域',anchor='M02',target=None),dict(id='reed_marsh',label='芦苇泽',anchor=None,target=None),dict(id='high_cliff',label='高崖',anchor=None,target=None),dict(id='eye_temple',label='眼之殿',anchor=None,target=None)]
(ROOT/'assets/data/whitebox/world.json').write_text(json.dumps(dict(viewport=[1152,648],grid=64,reserved_expansions=reserved,rooms=rooms),ensure_ascii=False,indent=2),encoding='utf-8')
for rid in rooms:
    path=ROOT/('assets/scenes/whitebox/tests/form_lab.tscn' if rid=='TEST' else f'assets/scenes/whitebox/levels/{rid}.tscn')
    path.write_text(f'''[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://assets/scripts/tools/MudWhiteboxRoom.cs" id="1"]
[node name="{rid}" type="Node2D"]
script = ExtResource("1")
RoomId = "{rid}"
''',encoding='utf-8')

lines=['# 23场景白盒尺寸与接口清单','', 'W=1152px，H=648px，沿用64px网格。尺寸为白盒建议值，四舍五入至64px。', '', '|房间|用途|建议W×H|实际像素|', '|---|---|---|---|']
for rid,r in rooms.items():
    if rid!='TEST': lines.append(f"|{rid}|{r['title']}|{r['screens'][0]}×{r['screens'][1]}|{r['size'][0]}×{r['size'][1]}|")
lines += ['', '每条接口目标出生点存在于目标场景Spawns下；出生点位于对应门内侧128px。门需主动E交互，抵达后锁定0.45秒。', '', '|接口ID|目标房间|目标出生点|本门坐标|本门内侧出生点|条件|','|---|---|---|---|---|---|']
for rid,r in rooms.items():
    for p in r['ports']:
        cond=','.join(p['requires']) or '无'
        if 'shortcut' in p: cond += ';T06侧可破，永久双向'
        lines.append(f"|{p['id']}|{p['target']}|{p['spawn']}|{p['position']}|From{p['target']}={p['arrival']}|{cond}|")
(ROOT/'Documentation/Room_Manifest.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('Tests: PASS 生成23个主线场景、独立测试场、可编辑world.json和接口清单')
