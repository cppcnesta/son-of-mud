from pathlib import Path
import re, json, hashlib
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).parent
OUT = ROOT / 'Documentation' / 'Design'
OUT.mkdir(parents=True, exist_ok=True)
WORLD = json.loads((ROOT/'assets/data/whitebox/world.json').read_text(encoding='utf-8-sig'))
ROOMS = {k:v for k,v in WORLD['rooms'].items() if k!='TEST'}
SOURCE = json.loads((WORK/'source_pdf.json').read_text(encoding='utf-8'))

PURPOSE = {
 'M01':'苏醒与操作教学；在Boss前练习格挡',
 'M02':'两河、浅水及泥板；预留下方水域扩展',
 'M03':'较长教学与战斗路线；朝圣者保持被动',
 'M04':'供桌选择、休整与战前准备',
 'M05':'守坑人战斗；检验格挡反击',
 'M06':'七夜雕像、两河源头；连接工地',
 'T01':'工地入口；二段跳获取与高台教学',
 'T02':'壁画与泥板；攀墙获取与墙面教学',
 'T03':'水闸、洪水、退水与后续通路',
 'T04':'龙蛇泥人；墙面、空中换位与失衡',
 'T05':'战后雕像、隐藏记忆、纵向城市出口',
 'T06':'城市返回后的登塔入口与可破捷径',
 'C01':'城门守卫；进入跪拜者的城市',
 'C02':'拉长的跪拜街道；浮雕与叙事',
 'C03':'伊吉、泥肝地图、多接口枢纽与休整',
 'C04':'潜游获取；池底征兆与期望的世界',
 'C05':'下砸障碍；雕像门与第二征兆',
 'C06':'顺序征兆谜题；诅咒泥板',
 'C07':'三街区完成后开放；年记与档案',
 'C08':'短祭道；神殿至登塔入口的过渡',
 'H01':'向上登塔；同一场景内七个连续仪式',
 'H02':'最终战前过渡、休整与能力应用',
 'H03':'塔姆图与伊姆杜；真相及结局'
}

def font(style, size=11, bold=False):
    style.font.name = 'Calibri'
    style.font.size = Pt(size)
    style.font.bold = bold
    style.font.color.rgb = RGBColor(0,0,0)
    rpr = style.element.get_or_add_rPr()
    rf = rpr.find(qn('w:rFonts'))
    if rf is None:
        rf=OxmlElement('w:rFonts');rpr.insert(0,rf)
    for key,val in [('ascii','Calibri'),('hAnsi','Calibri'),('eastAsia','Microsoft YaHei')]:
        rf.set(qn('w:'+key),val)
    for key in ['asciiTheme','hAnsiTheme','eastAsiaTheme','cstheme']:
        rf.attrib.pop(qn('w:'+key),None)

def addtext(p, s, bold=False):
    for part in re.split('(𒀭)',s):
        if not part: continue
        run=p.add_run(part);run.bold=bold
        if part=='𒀭':
            run.font.name='Segoe UI Historic'
            run._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'),'Segoe UI Historic')
    return p

def para(d, s, style=None):
    p=d.add_paragraph(style=style)
    return addtext(p,s)

def heading(d,s,level=1):
    p=d.add_paragraph(style='Heading '+str(level))
    # Titles are descriptive words, retaining source wording in source body only.
    return addtext(p,s)

def table(d, headers, rows, widths=None):
    t=d.add_table(rows=1, cols=len(headers)); t.alignment=WD_TABLE_ALIGNMENT.CENTER
    t.autofit=False
    if widths is None: widths=[6.9/len(headers)]*len(headers)
    for i,w in enumerate(widths):t.columns[i].width=Inches(w)
    pr=t._tbl.tblPr
    borders=OxmlElement('w:tblBorders')
    for edge in ['top','left','bottom','right','insideH','insideV']:
        e=OxmlElement('w:'+edge);e.set(qn('w:val'),'single');e.set(qn('w:sz'),'4');e.set(qn('w:color'),'D9D9D9');borders.append(e)
    pr.append(borders)
    for ri, vals in enumerate([headers]+rows):
        row=t.rows[0] if ri==0 else t.add_row()
        rp=row._tr.get_or_add_trPr()
        no=OxmlElement('w:cantSplit');rp.append(no)
        if ri==0:rp.append(OxmlElement('w:tblHeader'))
        for i,(c,s) in enumerate(zip(row.cells,vals)):
            c.width=Inches(widths[i]);c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            tc=c._tc.get_or_add_tcPr()
            mar=OxmlElement('w:tcMar')
            for side,val in [('top',90),('bottom',90),('left',100),('right',100)]:
                e=OxmlElement('w:'+side);e.set(qn('w:w'),str(val));e.set(qn('w:type'),'dxa');mar.append(e)
            tc.append(mar)
            fill='DCE6ED' if ri==0 else ('F5F7F9' if ri%2==0 else 'FFFFFF')
            sh=OxmlElement('w:shd');sh.set(qn('w:fill'),fill);tc.append(sh)
            p=c.paragraphs[0];p.style=d.styles['Table Text']
            short = (i==0 and len(str(s))<14) or (i>0 and len(str(s))<14 and len(headers)>2)
            p.alignment=WD_ALIGN_PARAGRAPH.CENTER if short else WD_ALIGN_PARAGRAPH.LEFT
            addtext(p,str(s),bold=ri==0)
    d.add_paragraph().paragraph_format.space_after=Pt(2)
    return t

def docbase(title,subject):
    d=Document();s=d.sections[0]
    s.page_width=Inches(8.5);s.page_height=Inches(11)
    s.top_margin=Inches(.72);s.bottom_margin=Inches(.7)
    s.left_margin=Inches(.8);s.right_margin=Inches(.8)
    s.header_distance=Inches(.28);s.footer_distance=Inches(.3)
    normal=d.styles['Normal'];font(normal,11)
    normal.paragraph_format.line_spacing=1.25
    normal.paragraph_format.space_after=Pt(6)
    normal.paragraph_format.widow_control=True
    font(d.styles['Title'],25,True);d.styles['Title'].paragraph_format.space_after=Pt(15)
    font(d.styles['Subtitle'],12);d.styles['Subtitle'].paragraph_format.space_after=Pt(15)
    for lev,size in [(1,17),(2,13.5),(3,11.5),(4,11)]:
        st=d.styles['Heading '+str(lev)];font(st,size,True)
        st.paragraph_format.space_before=Pt(16 if lev<3 else 10)
        st.paragraph_format.space_after=Pt(8 if lev<3 else 5)
        st.paragraph_format.keep_with_next=True
        if lev==1:st.paragraph_format.page_break_before=True
    st=d.styles.add_style('Table Text',1);font(st,10)
    st.paragraph_format.line_spacing=1.12;st.paragraph_format.space_after=Pt(0)
    st.paragraph_format.widow_control=True
    for n in ['TOC 1','TOC 2','TOC 3']:
        if n in d.styles:font(d.styles[n],10.5)
    footer=s.footer.paragraphs[0];footer.alignment=WD_ALIGN_PARAGRAPH.RIGHT
    r=footer.add_run('泥之子  版本1.0    ');r.font.size=Pt(9)
    f=OxmlElement('w:fldSimple');f.set(qn('w:instr'),'PAGE');footer._p.append(f)
    d.core_properties.title=title;d.core_properties.subject=subject
    d.core_properties.author='泥之子项目组';d.core_properties.keywords='关卡设计 战斗设计 白盒 原型'
    return d

def md(d,filename):
    lines=(WORK/filename).read_text(encoding='utf-8').splitlines();i=0
    while i<len(lines):
        line=lines[i].strip()
        if not line:i+=1;continue
        if line=='{{ROOM_TABLES}}':roomtables(d);i+=1;continue
        if line.startswith('|'):
            data=[]
            while i<len(lines) and lines[i].strip().startswith('|'):
                cells=[x.strip() for x in lines[i].strip().strip('|').split('|')]
                if not all(re.fullmatch('[-:]+',c) for c in cells):data.append(cells)
                i+=1
            n=len(data[0]);w={3:[1.55,2.6,2.75],4:[1.1,1.9,1.85,2.05]}.get(n)
            table(d,data[0],data[1:],w);continue
        m=re.match(r'^(#{2,4})\s+(.*)',line)
        if m:heading(d,m[2],len(m[1]));i+=1;continue
        para(d,line);i+=1

def roomtables(d):
    for prefix,title in [('M','泥坑'),('T','塔庙空地'),('C','占卜之城'),('H','塔庙之顶')]:
        heading(d,title+'房间清单',3)
        rows=[]
        for i,r in ROOMS.items():
            if not i.startswith(prefix):continue
            dims='×'.join(str(x) for x in r['screens']);px='×'.join(str(x) for x in r['size'])
            rows.append([i+'\n'+r['title'],dims+'屏\n'+px+'px',PURPOSE[i]])
        table(d,['房间','建议尺寸与实际尺寸','关卡功能'],rows,[1.75,1.65,3.5])

def interfaces(d):
    heading(d,'定向接口与目标出生点',2)
    para(d,'本清单依据world.json自动整理。到达坐标属于目标房间，通过目标中的反向接口定位；不是来源门的位置。快捷门还需检查持久破门标记，不能将其空requires数组理解为初始可通行。坐标单位为px。')
    rows=[]
    for i,r in ROOMS.items():
        for p in r['ports']:
            target=p['target'];reverse=next(x for x in ROOMS[target]['ports'] if x['target']==i)
            cond=','.join(p.get('requires',[])) or '无'
            if p.get('shortcut'):cond='T06侧破门后双向' if not p.get('break_side') else '本侧可破；破后双向'
            xy=','.join(str(v) for v in reverse['arrival'])
            rows.append([p['id'],target+'\n'+p['spawn'],xy,cond])
    table(d,['接口ID','目标及出生点','目标落位','条件'],rows,[1.72,1.48,1.2,2.5])

def sources(d):
    heading(d,'资料与版本依据',2)
    table(d,['资料','用途'],[
      ['泥之子 3.pdf，19页','故事、角色、时间线、四章剧情、七道门、两阶段最终战与结局主体'],
      ['本次确认的水泥形态及手感修订','动作规则优先依据；1秒曲线闪避减速和1.5秒攻击后摇覆盖旧方案'],
      ['Muddson地图源文件及23房间要求','空间关系与分区；具体尺寸由白盒数据给出'],
      ['world.json与Room_Manifest.md','现行房间尺寸、接口、出生点及条件'],
      ['water.tres、mud.tres、prototype_rules.gd','可调参数、暂定规则与模块边界'],
      ['Combat_Feel_Update.md与既有测试记录','当前行为与已有验证范围；不是本次重新测试']
    ],[2.35,4.55])
    para(d,'资料优先级为本次明确机制、最新地图源文件、原PDF剧情与关卡功能、此前规划建议。原PDF尚未确定的内容继续保持待定。正式美术、配音、CG与完整Boss机制由相应制作环节补齐。')
    para(d,'版本1.0。本次整理建立专项文档与总文档，并预留下一次Boss机制设计章节。后续每次确认机制后同步更新两份文档及参数记录。')

def toc(d,items):
    heading(d,'内容导航',2)
    for x in items:para(d,x)

def cover(d,title,intro):
    para(d,title,'Title');para(d,'版本1.0  关卡与战斗白盒','Subtitle')
    para(d,intro)

def original(d):
    heading(d,'第一篇 原叙事设定',1)
    para(d,'本篇沿用原PDF的概要、人物、时间线和地图剧情。原文中的待定标记、对话、泥板与结局分支保留；新增关卡编号和战斗机制在后续篇章记录。')
    text='\n'.join(x for x in SOURCE['content'] if not x.startswith('<PARSED TEXT'))
    text=text.replace('泥之子 3\n','',1)
    lines=text.splitlines();buf=[];source_parts=[]
    def flush():
        if buf:
            s=''.join(buf).strip();buf.clear()
            if s:para(d,s);source_parts.append(s)
    for line in lines:
        s=line.strip()
        if not s:flush();continue
        ismajor=bool(re.match(r'^[一二三四]、',s))
        ismap=s.startswith('地图一：') or s.startswith('地图二：') or s.startswith('地图三：') or s.startswith('地图四：')
        ischar=s.startswith(('父神伊姆杜','儿子穆尔图库尔','原初母神塔姆图','哀歌祭司伊吉'))
        numbered=bool(re.match(r'^\d+\.\s',s)) and '时间线' not in s
        sub=s in ['定位','性格与动机','一些片段/符号：','片段与符号','一些片段与符号','一些片段与符号：','泥人及其变体']
        gate=s.startswith(('第一道门','第二道门','第三道门','第四道门','第五道门','第六道门','第七道门'))
        if ismajor or ismap or ischar or sub or gate:
            flush();heading(d,s.replace('**',''),2 if ismajor else (3 if ismap or ischar else 4));source_parts.append(s.replace('**',''));continue
        # Keep dialogue speakers and stage directions visibly distinct.
        semantic=s.startswith(('【画面】','【对话】','拾取：','掉落：','CG','结局 A','结局 B','第一街区','第二街区','第三街区','流程：','选项：','完整版','（收集时','（但实际上'))
        speaker=bool(re.match(r'^[^。！？]{1,18}[：:]$',s))
        if numbered or semantic or speaker:
            flush();buf.append(s)
            if speaker:flush()
        else:buf.append(s)
    flush()
    normalized=lambda t:re.sub(r'\s|\*','',t)
    assert normalized(text)==normalized(''.join(source_parts)), 'Original PDF body was not preserved'
    return len(normalized(text))

def specialist():
    title='泥之子关卡与战斗设计专项文档'
    d=docbase(title,'个人负责范围与关卡机制制作记录')
    cover(d,'泥之子\n关卡与战斗设计专项文档','本文件用于整理本人负责的关卡、战斗机制、数值配置与白盒制作范围，供后续协作、迭代和制作对接使用。重点是23个房间如何承载能力教学与剧情，以及水泥形态怎样形成可操作、可检验的战斗差异。')
    heading(d,'职责与协作边界',2)
    table(d,['负责领域','设计与制作内容'],[
      ['关卡','23场景拆分、尺寸与连接、平台、教学、能力门控、城市三街区及七道门'],
      ['战斗与数值','水泥形态、攻击阶段、格挡收益、闪避减速、跳跃模组与可调参数'],
      ['白盒制作','数据配置、模块接入、训练场、调试反馈、进度与验证记录'],
      ['协作对接','依据原剧情放置事件，提出Boss、美术、动画、音频与演出需求']
    ],[1.25,5.65])
    para(d,'原PDF的世界观、人物、剧情与对白作为项目基础资料引用，不据此归属为本人独立创作。代码接入与工具协作产出按实际白盒制作记录描述，不将占位资源和待讨论方案计为已完成正式内容。')
    toc(d,['一 战斗与能力设计','二 关卡设计与房间流程','三 白盒制作与验证','四 Boss机制设计预留','附录 接口清单与资料依据'])
    heading(d,'一 战斗与能力设计',1);md(d,'mechanics.md')
    heading(d,'二 关卡设计与房间流程',1);md(d,'levels.md')
    heading(d,'三 白盒制作与验证',1);md(d,'production.md')
    heading(d,'四 Boss机制设计预留',1);md(d,'bosses.md')
    heading(d,'附录 接口清单与资料依据',1);interfaces(d);sources(d)
    path=OUT/'MudChild_Level_Combat_Design_V1_0.docx';d.save(path);return path

def master():
    title='泥之子游戏设计总文档'
    d=docbase(title,'原叙事设定与关卡机制整合')
    cover(d,'泥之子\n游戏设计总文档','本文件作为剧情、关卡和战斗协作的共同依据。原PDF的概要、人物、神话时间线与四章地图剧情构成主体，后续篇章补充23场景划分、玩家机制、能力门控和白盒制作约定。Boss机制单独预留，待下一次设计工作补齐。')
    para(d,'阅读时区分四类状态：已确认规则、当前白盒实现、可调暂定参数、待确认构想。故事中的七道门、最终两阶段与结局保留；新增功能的落点和数值按其状态记录。')
    toc(d,['第一篇 原叙事设定  概要 人物 时间线 地图剧情','第二篇 关卡与流程设计  23场景 连接 条件 七道门','第三篇 玩家机制与能力  两种形态 动作 跳跃 教学 门控','第四篇 白盒制作与验收  模块 资源 输入 进度 验证','第五篇 Boss机制设计预留  守坑人 龙蛇泥人 最终两阶段','附录 接口清单与资料依据'])
    n=original(d)
    heading(d,'第二篇 关卡与流程设计',1);md(d,'levels.md')
    heading(d,'第三篇 玩家机制与能力',1);md(d,'mechanics.md')
    heading(d,'第四篇 白盒制作与验收',1);md(d,'production.md')
    heading(d,'第五篇 Boss机制设计预留',1);md(d,'bosses.md')
    heading(d,'附录 接口清单与资料依据',1);interfaces(d);sources(d)
    path=OUT/'MudChild_Master_Design_V1_0.docx';d.save(path);return path,n

one=specialist();two,n=master()
audit={'files':[str(one),str(two)],'rooms':len(ROOMS),'interfaces':sum(len(r['ports']) for r in ROOMS.values()),'original_text_characters_preserved':n,'source_pdf_pages':19}
for p in [one,two]:
    dd=Document(p)
    assert len(dd.tables)>8 and len(dd.paragraphs)>80
    assert all(h in '\n'.join(x.text for x in dd.paragraphs) for h in ['Boss机制设计预留','下劈回弹待确认构想'])
audit['sha256']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [one,two]}
(WORK/'content_audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(audit,ensure_ascii=True))
