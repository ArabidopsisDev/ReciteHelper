from pathlib import Path
import os,re,json,copy,math
BASE=Path(__file__).resolve().parents[1]
PARENT=BASE.parent
WORK=BASE/'work';OUT=BASE/'output'
for p in (WORK,OUT,WORK/'tmp',WORK/'assets',BASE/'qa'):p.mkdir(exist_ok=True,parents=True)
for k in ('TEMP','TMP','TMPDIR'):os.environ[k]=str(WORK/'tmp')
from docx import Document
from docx.shared import Cm,Pt,RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_BREAK
from docx.enum.table import WD_TABLE_ALIGNMENT,WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.section import WD_SECTION_START
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.table import Table
from docx.text.paragraph import Paragraph
from PIL import Image,ImageDraw,ImageFont

FONT='C:/Windows/Fonts/msyh.ttc'
AS=WORK/'assets'
def box(d,xy,title,lines):
    d.rectangle(xy,fill='white',outline='black',width=2)
    x0,y0,x1,y1=xy
    d.text(((x0+x1)/2,y0+20),title,anchor='mt',font=ImageFont.truetype(FONT,29),fill='black')
    for i,s in enumerate(lines.splitlines()):d.text(((x0+x1)/2,y0+74+i*38),s,anchor='mt',font=ImageFont.truetype(FONT,24),fill='black')
def arrow(d,a,b):
    d.line([a,b],fill='black',width=3);th=math.atan2(b[1]-a[1],b[0]-a[0]);l=13
    d.polygon([b,(b[0]-l*math.cos(th-.5),b[1]-l*math.sin(th-.5)),(b[0]-l*math.cos(th+.5),b[1]-l*math.sin(th+.5))],fill='black')
im=Image.new('RGB',(1500,560),'white');dr=ImageDraw.Draw(im)
for i,(a,b) in enumerate([('导入资料','确认来源\n保留版本'),('生成与审核','形成题目\n核验答案'),('独立作答','提交回答\n保存事件'),('安排复习','预测回忆\n建立队列'),('按需帮助','原文解释\n可选建议'),('延迟检验','持续复习\n评价效果')]):
    row=i//3;col=i%3 if row==0 else 2-i%3;x=20+col*500;y=20+row*280
    box(dr,(x,y,x+450,y+205),a,b)
    if row==0 and col<2:arrow(dr,(x+455,y+100),(x+491,y+100))
    if row==1 and col>0:arrow(dr,(x-5,y+100),(x-41,y+100))
arrow(dr,(1240,235),(1240,288));im.save(AS/'flow.png')
im=Image.new('RGB',(1500,830),'white');dr=ImageDraw.Draw(im)
for i,(a,b) in enumerate([('客户端','课程资料  ·  题目练习  ·  学习记录  ·  自主选择的研究采集'),('应用服务','内容生成与审核  ·  证据检索  ·  会话建议与解释'),('核心能力','FSRS / SM-2 / Leitner  ·  知识图谱  ·  统一学习事件'),('数据与外部服务','本地题库与私有日志  ·  可选生成服务  ·  独立情境模型')]):
    y=15+i*205;box(dr,(25,y,1475,y+155),a,b)
    if i<3:arrow(dr,(750,y+164),(750,y+195))
im.save(AS/'architecture.png')
im=Image.new('RGB',(1500,690),'white');dr=ImageDraw.Draw(im)
l,t,r,b=150,85,1420,560
font=ImageFont.truetype(FONT,23)
for val in [.6,.7,.8,.9,1.0]:
    y=b-(val-.55)/.45*(b-t);dr.line([(l,y),(r,y)],fill='#D0D0D0',width=1);dr.text((l-20,y),f'{val:.0%}',anchor='rm',font=font,fill='black')
dr.line([(l,t),(l,b),(r,b)],fill='black',width=2)
for day in range(0,31,5):dr.text((l+day/30*(r-l),b+18),str(day),anchor='mt',font=font,fill='black')
for j,S in enumerate([3,10,25]):
    pts=[];factor=.9**(-1/.1542)-1
    for n in range(301):
        tt=n/10;rr=(1+factor*tt/S)**(-.1542);pts.append((l+tt/30*(r-l),b-(rr-.55)/.45*(b-t)))
    color=['#909090','#000000','#505050'][j]
    if j==2:
        for k in range(0,len(pts)-5,10):dr.line(pts[k:k+6],fill=color,width=4)
    else:dr.line(pts,fill=color,width=3+j)
    x=565+j*275;dr.line([(x,35),(x+45,35)],fill=color,width=3+j);dr.text((x+60,35),f'S = {S} 天',font=font,anchor='lm',fill='black')
dr.text((l,35),'预测回忆概率',font=font,anchor='lm',fill='black');dr.text((780,655),'距上次复习的天数',font=font,anchor='mm',fill='black');im.save(AS/'retention.png')

# Read the existing plan as content, discarding its per-page layout.
old=Document(PARENT/'output'/'汇智云学_鲁信杯项目计划书.docx')
mapping={'一 项目概要':'CH1','二 项目背景与用户需求':'CH2','三 产品方案与前期基础':'CH3','四 技术原理与融合设计':'CH4','五 研发验证与评价指标':'CH5','六 市场定位与竞争分析':'CH6','七 商业模式与推广路径':'CH7','八 团队组织与实施进度':'CH8','九 资金预算与经营测算':'CH10','十 风险控制与权益保护':'CH11','十一 教育价值与成果转化':'CH12'}
blocks={k:[] for k in list(mapping.values())+['CH9','REFS']}
current=None;image_i=0
image_names=['flow.png','prototype.jpg','architecture.png','retention.png']
eyebrows={'产品方案','前期成果','总体架构','生成式 AI 与知识图谱','FSRS 记忆调度','Emotion-LLaMA 情境观察','多模态融合与项目创新','研发投入','经营情景'}
for elem in old.element.body.iterchildren():
    if elem.tag==qn('w:p'):
        p=Paragraph(elem,old);txt=p.text.strip();sty=p.style.name
        if sty=='Heading 1':
            if txt=='目录':current=None
            elif txt.startswith('参考资料与成果依据'):current='REFS'
            else:current=mapping.get(txt,current)
            continue
        if current is None:continue
        if elem.xpath('.//a:blip'):
            blocks[current].append(('figure',image_names[image_i]));image_i+=1;continue
        if elem.xpath('.//m:oMath'):
            blocks[current].append(('equation',copy.deepcopy(elem)));continue
        if sty=='Caption' or not txt or txt in eyebrows:continue
        if sty=='Heading 2':
            if txt.startswith('8.2 '):current='CH9'
            blocks[current].append(('h2',re.sub(r'^\d+\.\d+\s+','',txt)))
        else:blocks[current].append(('p',txt))
    elif elem.tag==qn('w:tbl') and current:
        tab=Table(elem,old)
        rows=[[c.text for c in row.cells] for row in tab.rows]
        blocks[current].append(('table',rows))

extras={};key=None
for line in (WORK/'expansion.md').read_text(encoding='utf-8').splitlines():
    if line.startswith('# '):key=line[2:].strip();extras[key]=[]
    elif line.startswith('## '):extras[key].append(('h2',line[3:]))
    elif line.strip():extras[key].append(('p',line.strip()))

doc=Document();sec=doc.sections[0]
def section_layout(s):
    s.page_width,s.page_height=Cm(21),Cm(29.7)
    s.top_margin,s.bottom_margin=Cm(2.5),Cm(2.3)
    s.left_margin,s.right_margin=Cm(2.8),Cm(2.3)
    s.header_distance,s.footer_distance=Cm(1.2),Cm(1.2)
section_layout(sec)
def stylefont(name,size,east='宋体',bold=False):
    st=doc.styles[name];st.font.name='Times New Roman';st.font.size=Pt(size);st.font.bold=bold;st.font.italic=False;st.font.color.rgb=RGBColor(0,0,0)
    rf=st.element.get_or_add_rPr().find(qn('w:rFonts'))
    if rf is None:rf=OxmlElement('w:rFonts');st.element.get_or_add_rPr().append(rf)
    rf.set(qn('w:eastAsia'),east)
    for attr in ['asciiTheme','hAnsiTheme','eastAsiaTheme','cstheme']:rf.attrib.pop(qn('w:'+attr),None)
    return st
st=stylefont('Normal',12);pf=st.paragraph_format;pf.first_line_indent=Pt(24);pf.line_spacing=1.5;pf.space_after=Pt(0);pf.widow_control=True
for n,sz,east in [('Title',24,'华文中宋'),('Subtitle',15,'宋体'),('Heading 1',16,'黑体'),('Heading 2',14,'黑体'),('Heading 3',12,'黑体'),('Caption',10.5,'黑体')]:
    st=stylefont(n,sz,east,n not in ('Subtitle','Caption'))
    pf=st.paragraph_format;pf.first_line_indent=Pt(0);pf.line_spacing=1.5;pf.space_before=Pt(12 if 'Heading' in n else 0);pf.space_after=Pt(6);pf.keep_with_next=True
for n in ['TOC 1','TOC 2']:
    if n not in doc.styles:doc.styles.add_style(n,1)
    st=stylefont(n,12);st.paragraph_format.line_spacing=1.3;st.paragraph_format.space_after=Pt(2)
    st.paragraph_format.first_line_indent=Pt(0)
    st.paragraph_format.left_indent=Pt(0 if n=='TOC 1' else 18)

def para(text,center=False,small=False):
    p=doc.add_paragraph(text)
    if center:p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.first_line_indent=Pt(0)
    if small:
        for r in p.runs:r.font.size=Pt(10.5)
    return p
def h(text,level=2):return doc.add_paragraph(text,'Heading '+str(level))

def table(rows,widths=None):
    cols=len(rows[0]);t=doc.add_table(rows=0,cols=cols);t.autofit=False;t.alignment=WD_TABLE_ALIGNMENT.CENTER
    if widths is None:widths=[15.9/cols]*cols
    for i,w in enumerate(widths):t.columns[i].width=Cm(w)
    for ri,row in enumerate(rows):
        cells=t.add_row().cells
        tr=t.rows[-1]._tr.get_or_add_trPr();cs=OxmlElement('w:cantSplit');tr.append(cs)
        if ri==0:rep=OxmlElement('w:tblHeader');tr.append(rep)
        for ci,val in enumerate(row):
            c=cells[ci];c.text=str(val);c.width=Cm(widths[ci]);c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            pr=c._tc.get_or_add_tcPr();m=OxmlElement('w:tcMar')
            for a,w in [('top',70),('bottom',70),('left',90),('right',90)]:
                e=OxmlElement('w:'+a);e.set(qn('w:w'),str(w));e.set(qn('w:type'),'dxa');m.append(e)
            pr.append(m);b=OxmlElement('w:tcBorders')
            for a in ['top','bottom','left','right']:
                e=OxmlElement('w:'+a);e.set(qn('w:val'),'single');e.set(qn('w:sz'),'4');e.set(qn('w:color'),'D9D9D9');b.append(e)
            pr.append(b)
            for p in c.paragraphs:
                p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.line_spacing=1.2;p.paragraph_format.space_after=Pt(0)
                if ri==0 or (ci==0 and len(str(val))<12):p.alignment=WD_ALIGN_PARAGRAPH.CENTER
                elif re.fullmatch(r'[\d,−\-\.]+',str(val)):p.alignment=WD_ALIGN_PARAGRAPH.RIGHT
                for run in p.runs:run.font.size=Pt(10.5);run.bold=ri==0
    doc.add_paragraph().paragraph_format.line_spacing=0.25
    return t

def blankpage():doc.add_page_break()
def field(p,instruction,placeholder='请更新目录'):
    begin=OxmlElement('w:fldChar');begin.set(qn('w:fldCharType'),'begin')
    r=OxmlElement('w:r');r.append(begin);p._p.append(r)
    r=OxmlElement('w:r');i=OxmlElement('w:instrText');i.set(qn('xml:space'),'preserve');i.text=instruction;r.append(i);p._p.append(r)
    r=OxmlElement('w:r');e=OxmlElement('w:fldChar');e.set(qn('w:fldCharType'),'separate');r.append(e);p._p.append(r)
    p.add_run(placeholder)
    r=OxmlElement('w:r');e=OxmlElement('w:fldChar');e.set(qn('w:fldCharType'),'end');r.append(e);p._p.append(r)

# Cover, abstract and automatic contents follow common university proposal layouts.
p=doc.add_paragraph('汇智云学','Title');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(60);p.paragraph_format.space_after=Pt(22)
p=doc.add_paragraph('基于FSRS、Emotion-LLaMA和生成式AI\n多模态融合的智能复习系统','Subtitle');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_after=Pt(36)
para('项目计划书',center=True).runs[0].font.size=Pt(20)
para('“鲁信杯”第一届山东省“人工智能＋产教应用”大赛\n人工智能＋教育创新赛道  学校组',center=True)
for s in ['参赛学校：山东农业大学','团队名称：穗知创新团队','项目负责人：刘宗睿','所在学院：农学院','学号：2024210172','指导教师：张金诺  副教授']:
    p=para(s,center=True);p.paragraph_format.space_after=Pt(5)
para('2026年10月',center=True).paragraph_format.space_before=Pt(28)
blankpage()
p=para('摘  要',center=True);p.runs[0].font.size=Pt(16);p.runs[0].bold=True;p.paragraph_format.space_after=Pt(14)
for text in [
'汇智云学是一项面向大学生专业课程复习的教育人工智能项目，以山东农业大学农学类课程为首个应用场景，拟将有权使用的教材、讲义和笔记转化为可审核的知识点与练习，再结合FSRS记忆调度、知识图谱解释和可选的多模态情境观察，形成从课程材料到持续复习的完整服务。',
'项目关注资料整理割裂、复习顺序难安排、错题原因不清楚和学习受阻时辅助不足等实际问题。系统以来源证据和真实作答事件为基础：生成式AI提出内容候选，审核流程确认答案和关键关系，复习内核根据历史安排任务，情境层在用户自主选择和验证条件满足时提供辅助建议。',
'前期已经形成ReciteHelper桌面原型及OpenRecite复习核心，具备FSRS-6、SM-2、Leitner、答题处理和项目读写相关能力。知识图谱和Emotion-LLaMA融合属于拟研发内容，现阶段不把论文基准成绩、公式示意或软件运行结果作为本项目学习效果证明。后续通过课程可行性试点、时间外预测比较和七日、三十日延迟回忆检验逐步验证。',
'经营方面，项目拟采用基础功能开放、个人增值服务、课程共建和机构实施结合的方式。首年现金预算暂设18万元，拟争取30万元分阶段项目支持；用户数量、价格、收入和财务测算均为规划假设，以真实试点、交付成本与续用证据持续修订。项目重视内容正确性、个人信息保护与第三方许可，保留不使用摄像头和麦克风的完整复习路径。']:
    para(text)
para('关键词：智能复习；FSRS；生成式人工智能；知识图谱；多模态情境',small=True)
blankpage();p=para('目  录',center=True);p.runs[0].font.size=Pt(16);p.runs[0].bold=True
p=doc.add_paragraph();p.paragraph_format.first_line_indent=Pt(0);field(p,' TOC \\o "1-2" \\h \\z \\u ')

body=doc.add_section(WD_SECTION_START.NEW_PAGE);section_layout(body)
body.header.is_linked_to_previous=False;body.footer.is_linked_to_previous=False
hp=body.header.paragraphs[0];hp.text='汇智云学项目计划书';hp.alignment=WD_ALIGN_PARAGRAPH.CENTER;hp.paragraph_format.first_line_indent=Pt(0)
for rr in hp.runs:rr.font.size=Pt(9)
fp=body.footer.paragraphs[0];fp.alignment=WD_ALIGN_PARAGRAPH.CENTER;fp.paragraph_format.first_line_indent=Pt(0);field(fp,' PAGE ','1')
pn=OxmlElement('w:pgNumType');pn.set(qn('w:start'),'1');body._sectPr.append(pn)

titles=[('CH1','项目概述'),('CH2','项目背景与需求分析'),('CH3','产品设计与应用场景'),('CH4','核心技术与研发路线'),('CH5','试验方案与效果评价'),('CH6','市场分析与竞争策略'),('CH7','商业模式与运营计划'),('CH8','项目团队与组织管理'),('CH9','项目实施与发展规划'),('CH10','资金预算与财务计划'),('CH11','风险管理与知识产权'),('CH12','教育价值与成果转化')]
captions={'flow.png':'课程复习业务流程','prototype.jpg':'前期ReciteHelper答题与知识辅助界面','architecture.png':'拟建设系统总体架构','retention.png':'不同稳定度的FSRS回忆曲线示意'}
widths_by_cols={2:[3.4,12.5],3:[3.2,6.1,6.6],4:[5.1,3.6,3.6,3.6]}
chapter_outline=[]
def render_blocks(data,num,count_start=0):
    sc=count_start;fc=0;tc=0
    for typ,obj in data:
        if typ=='h2':
            sc+=1;h(f'{num}.{sc} {obj}',2)
        elif typ=='p':para(obj)
        elif typ=='table':
            tc+=1;p=doc.add_paragraph(f'表{num}-{tc}  '+obj[0][0]+'相关信息','Caption');p.alignment=WD_ALIGN_PARAGRAPH.CENTER
            table(obj,widths_by_cols.get(len(obj[0])))
        elif typ=='figure':
            fc+=1;p=doc.add_paragraph();p.paragraph_format.first_line_indent=Pt(0);p.alignment=WD_ALIGN_PARAGRAPH.CENTER
            src=AS/obj if obj!='prototype.jpg' else PARENT/'work'/'assets'/obj
            p.add_run().add_picture(str(src),width=Cm(15.4));p.paragraph_format.keep_with_next=True
            cp=doc.add_paragraph(f'图{num}-{fc}  {captions[obj]}','Caption');cp.alignment=WD_ALIGN_PARAGRAPH.CENTER
            if obj=='retention.png':para('注：根据固定FSRS参数绘制，仅解释模型含义，不是参与者测量结果。',small=True)
        elif typ=='equation':doc.element.body.insert(len(doc.element.body)-1,copy.deepcopy(obj))
    return sc

for num,(key,title) in enumerate(titles,1):
    if num>1:blankpage()
    h(f'第{num}章  {title}',1)
    baseblocks=blocks.get(key,[])
    if key=='CH1':baseblocks=[b for b in baseblocks if b[0]!='table']
    sc=render_blocks(baseblocks,num)
    render_blocks(extras.get(key,[]),num,sc)
    if key=='CH10':
        h(f"10.{sc+sum(t=='h2' for t,_ in extras.get(key,[]))+1} 简化财务勾稽表",2)
        para('以下采用统一演示口径，金额单位为元。期初支持资金按权益性投入列示；所得税、应收应付、借款和新增设备暂未纳入，不作为已审计报表。')
        doc.add_paragraph('表10-3  简化税前经营结果测算','Caption')
        table([['项目','第一年','第二年','第三年'],['营业收入',65700,256500,845500],['现金经营支出',160000,360000,660000],['折旧演示值',4000,4000,4000],['税前经营结果',-98300,-107500,181500]],[5.1,3.6,3.6,3.6])
        doc.add_paragraph('表10-4  简化现金收支测算','Caption')
        table([['现金项目','第一年','第二年','第三年'],['期初现金',300000,185700,82200],['服务回款',65700,256500,845500],['经营现金流出',160000,360000,660000],['设备购置',20000,0,0],['期末现金',185700,82200,267700]],[5.1,3.6,3.6,3.6])
        doc.add_paragraph('表10-5  期末资产与投入勾稽','Caption')
        table([['期末项目','第一年','第二年','第三年'],['现金',185700,82200,267700],['设备净值',16000,12000,8000],['资产合计',201700,94200,275700],['假设期初投入',300000,300000,300000],['累计税前经营结果',-98300,-205800,-24300],['投入及累计结果合计',201700,94200,275700]],[5.1,3.6,3.6,3.6])
    chapter_outline.append({'chapter':num,'title':title,'subsections':sc+sum(t=='h2' for t,_ in extras.get(key,[]))})

for letter,title in [('A','用户访谈与问卷设计'),('B','课程试点操作规程'),('C','图谱样例与数据说明'),('D','财务测算口径说明')]:
    blankpage();h(f'附录{letter}  {title}',1);render_blocks(extras['APPENDIX_'+letter],letter)

blankpage();h('参考文献与成果依据',1)
for typ,txt in blocks['REFS']:
    if typ!='p':continue
    p=para(txt,small=True);p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.line_spacing=1.3;p.paragraph_format.space_after=Pt(4)
    if re.match(r'^\[\d+\]',txt):
        for r in p.runs:r.bold=True
        p.paragraph_format.keep_with_next=True
    elif txt.startswith('https://'):p.paragraph_format.keep_with_next=True

doc.core_properties.title='汇智云学——基于FSRS、Emotion-LLaMA和生成式AI多模态融合的智能复习系统'
doc.core_properties.author='穗知创新团队'
doc.core_properties.subject='鲁信杯项目计划书规范扩展版'
settings=doc.settings.element;upd=OxmlElement('w:updateFields');upd.set(qn('w:val'),'true');settings.append(upd)
path=OUT/'汇智云学_鲁信杯项目计划书_规范扩展版.docx';doc.save(path)
texts=[]
for p in doc.paragraphs:
    if p.text:texts.append(p.text)
for t in doc.tables:
    for row in t.rows:texts.extend(c.text for c in row.cells)
(WORK/'content.txt').write_text('\n\n'.join(texts),encoding='utf-8')
(WORK/'outline.json').write_text(json.dumps(chapter_outline,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'file':str(path),'characters':sum(map(len,texts)),'chapters':len(titles),'tables':len(doc.tables)},ensure_ascii=False))
