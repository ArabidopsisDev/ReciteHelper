from pathlib import Path
import os, math, json, shutil
BASE = Path(__file__).resolve().parents[1]
WORK = BASE / 'work'
OUT = BASE / 'output'
QA = BASE / 'qa'
for d in (WORK, OUT, QA, WORK/'tmp', WORK/'assets'):
    d.mkdir(parents=True, exist_ok=True)
os.environ['TMP'] = os.environ['TEMP'] = os.environ['TMPDIR'] = str(WORK/'tmp')
os.environ['MPLCONFIGDIR'] = str(WORK/'tmp')

from docx import Document
from docx.shared import Cm, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from PIL import Image, ImageDraw, ImageFont

FONT = 'C:/Windows/Fonts/msyh.ttc'
BLACK = RGBColor(0,0,0)
NAVY = '#173A55'
BLUE = '#2E6B91'
PALE = '#EDF3F7'
ASSET = WORK/'assets'
doc = Document()
s = doc.sections[0]
s.page_width, s.page_height = Cm(21), Cm(29.7)
s.top_margin, s.bottom_margin = Cm(2.05), Cm(2)
s.left_margin, s.right_margin = Cm(2.3), Cm(2.3)
s.header_distance, s.footer_distance = Cm(0.9), Cm(0.9)
s.different_first_page_header_footer = True

def font_style(style, size, east='宋体', bold=False):
    style.font.name='Times New Roman'
    style.font.size=Pt(size)
    style.font.bold=bold
    style.font.italic=False
    style.font.color.rgb=BLACK
    rpr=style.element.get_or_add_rPr()
    rf=rpr.find(qn('w:rFonts'))
    if rf is None:
        rf=OxmlElement('w:rFonts');rpr.append(rf)
    rf.set(qn('w:eastAsia'),east)
    for k in ('asciiTheme','hAnsiTheme','eastAsiaTheme','cstheme'):
        rf.attrib.pop(qn('w:'+k),None)

font_style(doc.styles['Normal'],11)
normal=doc.styles['Normal'].paragraph_format
normal.line_spacing=1.4
normal.space_after=Pt(7)
normal.first_line_indent=Pt(22)
for name,size in [('Title',30),('Subtitle',15),('Heading 1',17),('Heading 2',12.5),('Heading 3',11)]:
    font_style(doc.styles[name],size,'黑体',name!='Subtitle')
    pf=doc.styles[name].paragraph_format
    pf.first_line_indent=Pt(0);pf.space_before=Pt(10);pf.space_after=Pt(9)
    pf.keep_with_next=True
font_style(doc.styles['Caption'],9,'宋体')
doc.styles['Caption'].paragraph_format.first_line_indent=Pt(0)

h=s.header.paragraphs[0]
h.alignment=WD_ALIGN_PARAGRAPH.RIGHT
h.paragraph_format.first_line_indent=Pt(0)
r=h.add_run('汇智云学  |  鲁信杯项目计划书');r.font.size=Pt(9);r.font.color.rgb=BLACK
f=s.footer.paragraphs[0]
f.alignment=WD_ALIGN_PARAGRAPH.CENTER;f.paragraph_format.first_line_indent=Pt(0)
f.add_run('— ')
field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');f._p.append(field)
f.add_run(' —')

all_text=[]
pages=[]
def para(text,bold=False,center=False,small=False):
    p=doc.add_paragraph()
    if center:
        p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.first_line_indent=Pt(0)
    r=p.add_run(text);r.bold=bold
    if small:r.font.size=Pt(9)
    all_text.append(text)
    return p

def heading(text):
    p=doc.add_paragraph(text,'Heading 2');all_text.append(text);return p

def page(title,eyebrow=None):
    doc.add_page_break()
    if eyebrow:
        p=para(eyebrow,small=True);p.paragraph_format.space_after=Pt(4)
    p=doc.add_paragraph(title,'Heading 1');all_text.append(title)
    pages.append(title)

def table(headers,rows,widths):
    t=doc.add_table(rows=1, cols=len(headers))
    t.alignment=WD_TABLE_ALIGNMENT.CENTER;t.autofit=False
    for i,w in enumerate(widths):t.columns[i].width=Cm(w)
    for i,txt in enumerate(headers):t.rows[0].cells[i].text=txt
    for row in rows:
        cells=t.add_row().cells
        for i,txt in enumerate(row):cells[i].text=str(txt)
    for ri,row in enumerate(t.rows):
        pr=row._tr.get_or_add_trPr()
        if ri==0:
            flag=OxmlElement('w:tblHeader');pr.append(flag)
        flag=OxmlElement('w:cantSplit');pr.append(flag)
        for i,c in enumerate(row.cells):
            c.width=Cm(widths[i]);c.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER
            tcPr=c._tc.get_or_add_tcPr()
            shade=OxmlElement('w:shd');shade.set(qn('w:fill'), '173A55' if ri==0 else ('F0F4F7' if ri%2==0 else 'FFFFFF'));tcPr.append(shade)
            margins=OxmlElement('w:tcMar')
            for side,value in [('top','85'),('bottom','85'),('left','105'),('right','105')]:
                el=OxmlElement('w:'+side);el.set(qn('w:w'),value);el.set(qn('w:type'),'dxa');margins.append(el)
            tcPr.append(margins)
            borders=OxmlElement('w:tcBorders')
            for edge in ['top','left','bottom','right']:
                e=OxmlElement('w:'+edge);e.set(qn('w:val'),'single');e.set(qn('w:sz'),'4');e.set(qn('w:color'),'D9D9D9');borders.append(e)
            tcPr.append(borders)
            for p in c.paragraphs:
                p.paragraph_format.first_line_indent=Pt(0)
                p.paragraph_format.line_spacing=1.2
                p.paragraph_format.space_after=Pt(1)
                p.paragraph_format.keep_with_next=(ri==0)
                if i==0 and len(c.text)<14:p.alignment=WD_ALIGN_PARAGRAPH.CENTER
                for r in p.runs:
                    r.font.size=Pt(9.5);r.font.name='Times New Roman'
                    r._element.get_or_add_rPr().get_or_add_rFonts().set(qn('w:eastAsia'),'宋体')
                    r.font.color.rgb=RGBColor(255,255,255) if ri==0 else BLACK
                    r.bold=(ri==0)
    all_text.extend(headers)
    for row in rows:all_text.extend(str(x) for x in row)
    p=doc.add_paragraph();p.paragraph_format.space_after=Pt(1);p.paragraph_format.space_before=Pt(0);p.paragraph_format.line_spacing=0.4
    return t

def draw_box(draw,xy,title,body,fill=PALE):
    draw.rounded_rectangle(xy,radius=12,fill=fill,outline='#AEBFCB',width=2)
    x0,y0,x1,y1=xy
    ft=ImageFont.truetype(FONT,31);fb=ImageFont.truetype(FONT,23)
    draw.text(((x0+x1)/2,y0+24),title,font=ft,fill=NAVY,anchor='mt')
    for line_index, line in enumerate(body.splitlines()):
        draw.text(((x0+x1)/2,y0+79+line_index*38),line,font=fb,fill='#202C35',anchor='mt')

def arrow(draw,a,b):
    draw.line([a,b],fill=BLUE,width=5)
    angle=math.atan2(b[1]-a[1],b[0]-a[0]);l=14
    pts=[b,(b[0]-l*math.cos(angle-0.5),b[1]-l*math.sin(angle-0.5)),(b[0]-l*math.cos(angle+0.5),b[1]-l*math.sin(angle+0.5))]
    draw.polygon(pts,fill=BLUE)

def assets():
    im=Image.new('RGB',(1500,610),'white');d=ImageDraw.Draw(im)
    nodes=[('导入课程资料','教材与讲义\n保留页码和来源'),('生成与审核','知识点和题目\n人工确认关键内容'),('完成真实作答','独立回忆与反馈\n记录提交时间'),('安排复习','FSRS 估计遗忘\n按到期风险选题'),('按需辅助','先修解释与自述\n可选情境建议'),('延迟检验','7 日与 30 日回忆\n改进内容与策略')]
    for idx,(a,b) in enumerate(nodes):
        row=idx//3;col=idx%3 if row==0 else 2-idx%3
        x=30+col*500;y=35+row*300
        draw_box(d,(x,y,x+440,y+220),a,b)
        if row==0 and col<2:arrow(d,(x+447,y+110),(x+492,y+110))
        if row==1 and col>0:arrow(d,(x-7,y+110),(x-52,y+110))
    arrow(d,(1250,265),(1250,322))
    im.save(ASSET/'flow.png')
    im=Image.new('RGB',(1500,900),'white');d=ImageDraw.Draw(im)
    layers=[('学习入口','桌面端与课程资料  ·  题目练习  ·  自主控制的摄像头和麦克风'),('业务与决策','生成式 AI 内容服务  ·  知识图谱解释  ·  可退出的会话建议'),('记忆与证据','OpenRecite 复习内核  ·  FSRS / SM-2 / Leitner  ·  真实作答日志'),('数据与模型','本地题库和 SQLite 图谱  ·  私有情境记录  ·  独立 Emotion-LLaMA 研究服务')]
    for i,(a,b) in enumerate(layers):
        y=20+i*220;draw_box(d,(40,y,1460,y+160),a,b)
        if i<3:arrow(d,(750,y+167),(750,y+213))
    im.save(ASSET/'architecture.png')
    im=Image.new('RGB',(1500,650),'white');d=ImageDraw.Draw(im)
    l,t,r,b=150,70,1400,515
    d.line([(l,t),(l,b),(r,b)],fill='#333333',width=3)
    font=ImageFont.truetype(FONT,24);title=ImageFont.truetype(FONT,28)
    for v in [0.6,0.7,0.8,0.9,1]:
        yy=b-(v-.55)/.45*(b-t);d.line([(l,yy),(r,yy)],fill='#E3E8EB',width=2);d.text((l-20,yy),f'{v:.0%}',font=font,fill='black',anchor='rm')
    for day in [0,5,10,15,20,25,30]:
        xx=l+day/30*(r-l);d.text((xx,b+14),str(day),font=font,fill='black',anchor='mt')
    decay=.1542;factor=.9**(-1/decay)-1
    for S,color,name in [(3,'#8F9CA5','S = 3 天'),(10,BLUE,'S = 10 天'),(25,'#4F8168','S = 25 天')]:
        pts=[]
        for n in range(301):
            day=n/10;R=(1+factor*day/S)**(-decay);pts.append((l+day/30*(r-l),b-(R-.55)/.45*(b-t)))
        d.line(pts,fill=color,width=5)
        xx=580+[(3),(10),(25)].index(S)*270;yy=30;d.line([(xx,yy),(xx+45,yy)],fill=color,width=5);d.text((xx+60,yy),name,font=font,fill='black',anchor='lm')
    d.text((750,610),'距上次复习的天数',font=title,fill='black',anchor='mm')
    d.text((l,20),'预测回忆概率',font=title,fill='black')
    im.save(ASSET/'retention.png')
    shutil.copy2('D:/Projects/ReciteHelper/docs/Resources/12-run-quiz.jpg',ASSET/'prototype.jpg')

def figure(name,caption,width=16.0):
    p=doc.add_paragraph();p.paragraph_format.first_line_indent=Pt(0);p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    p.add_run().add_picture(str(ASSET/name),width=Cm(width))
    p.paragraph_format.keep_with_next=True
    p=doc.add_paragraph(caption,'Caption');p.alignment=WD_ALIGN_PARAGRAPH.CENTER
    all_text.append(caption)

def mathrun(txt):
    r=OxmlElement('m:r');t=OxmlElement('m:t');t.text=txt;r.append(t);return r

def fsrs_equation():
    p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.first_line_indent=Pt(0)
    o=OxmlElement('m:oMath')
    o.append(mathrun('R(t,S) = '))
    sup=OxmlElement('m:sSup');base=OxmlElement('m:e');base.append(mathrun('(1 + F·t/S)'));power=OxmlElement('m:sup');power.append(mathrun('−d'));sup.append(base);sup.append(power);o.append(sup)
    o.append(mathrun('，F = '))
    sup=OxmlElement('m:sSup');base=OxmlElement('m:e');base.append(mathrun('0.9'));power=OxmlElement('m:sup');power.append(mathrun('−1/d'));sup.append(base);sup.append(power);o.append(sup);o.append(mathrun(' − 1'))
    p._p.append(o)

assets()

# 1 Cover
p=doc.add_paragraph('汇智云学','Title');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(66);p.paragraph_format.space_after=Pt(28)
p=doc.add_paragraph('基于FSRS、Emotion-LLaMA和生成式AI\n多模态融合的智能复习系统','Subtitle');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.line_spacing=1.55;p.paragraph_format.space_after=Pt(40)
para('“鲁信杯”第一届山东省\n“人工智能＋产教应用”大赛项目计划书',center=True,bold=True)
para('拟申报赛道  人工智能＋教育创新\n拟申报组别  学校组',center=True)
para('参赛学校  山东农业大学\n团队名称  穗知创新团队\n项目负责人  刘宗睿\n所在学院  农学院\n学号  2024210172\n指导教师  张金诺  副教授',center=True)
p=para('2026 年 10 月',center=True);p.paragraph_format.space_before=Pt(32)
doc.core_properties.title='汇智云学——基于FSRS、Emotion-LLaMA和生成式AI多模态融合的智能复习系统'
doc.core_properties.subject='鲁信杯人工智能加产教应用大赛项目计划书'
doc.core_properties.author='穗知创新团队'

# 2
page('一 项目概要')
para('汇智云学面向大学生专业课程复习，拟将课程资料转化为可追溯的知识点与题目，再通过记忆调度安排复习、通过知识图谱解释薄弱环节，并以自主开启的多模态情境分析辅助学习节奏。项目以山东农业大学农学类课程为首个验证场景，逐步形成可复用的课程学习服务。')
para('农业类课程包含大量概念、过程和相近术语。学生既要记住知识，又要理解知识之间的联系；期末集中复习时，还需要在有限时间内判断先复习什么。现有资料整理、题库练习、记忆卡片和 AI 问答往往分散使用。我们希望在一个学习项目中保存资料、题目、证据和真实作答，使每次辅助都有出处、每次安排都能解释。')
para('项目已具备 ReciteHelper 桌面原型和独立的 OpenRecite 复习核心，核心包含 FSRS-6、SM-2、Leitner、答题处理及项目读写。知识图谱和 Emotion-LLaMA 融合处于方案设计与待验证阶段；现有成果证明了软件开发基础，不等于已证明学习效果。下一阶段优先完成课程小规模验证，再决定多模态功能的启用范围。')
table(['项目要素','实施定位'],[
('首批用户','有明确课程资料、需要持续复习的在校大学生'),
('首批场景','农业生态学、智慧农业相关课程的知识整理与复习'),
('核心价值','减少整理割裂，建立记忆计划，提供有依据的错题辅助'),
('研发路径','生成式 AI 与 FSRS 为基础，图谱和情境能力分阶段验证'),
('商业路径','基础功能免费，课程服务与可选增值能力支持持续运营'),
('阶段目标','形成课程级可展示原型、可复现评估和可扩展 SDK')],[3,13.4])
para('本计划坚持以延迟无提示回忆和实际学习负担评价产品。拟申请的研发与验证支持资金为 30 万元，分阶段投入；预算、用户量和收入均为规划假设，不作为已实现业绩。',small=True)

# 3
page('目录')
for a,b in [('一 项目概要','2'),('二 项目背景与用户需求','4'),('三 产品方案与前期基础','5–6'),('四 技术原理与融合设计','7–11'),('五 研发验证与评价指标','12'),('六 市场定位与竞争分析','13'),('七 商业模式与推广路径','14'),('八 团队组织与实施进度','15'),('九 资金预算与经营测算','16–17'),('十 风险控制与权益保护','18'),('十一 教育价值与成果转化','19'),('参考资料与成果依据','20–21')]:
    p=doc.add_paragraph();p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.space_after=Pt(14)
    p.paragraph_format.tab_stops.add_tab_stop(Cm(15.5),WD_ALIGN_PARAGRAPH.RIGHT)
    p.add_run(a+'\t'+b)
heading('项目阅读要点')
para('项目的竞争力在于把课程内容、学习历史和辅助策略连成可验证的流程。技术部分说明各模块分别承担什么任务，经营部分说明从一个课程小组逐步扩展的方式。资金测算按基础复习服务建立，不依赖尚未取得产品使用授权的情绪模型收入。')
para('阶段界定贯穿全文：已有原型与 SDK 属于开发基础；知识图谱、多模态融合和课程规模化运营属于拟实施内容；试验目标和经营预测均需在后续实践中验证。')

# 4
page('二 项目背景与用户需求')
heading('2.1 从真实课程复习出发')
para('在专业课学习中，学生通常同时面对教材、教师讲义、课堂笔记和往年练习。资料中同一概念可能有不同表述，章节之间又存在先修关系。学生常需先人工整理，再使用题库、记忆工具或 AI 助手；整理耗时、复习时间分配和答案来源核对成为重复工作。上述判断来自项目使用场景分析，后续将通过访谈与使用日志验证其普遍性。')
table(['需求环节','具体困难','拟提供的支持'],[
('资料整理','知识点和练习分散，改版后难同步','保留来源位置，生成可审核的课程项目'),
('复习安排','凭感觉重复熟题，忽视即将遗忘内容','依据历史作答建立到期队列'),
('理解纠错','知道答案不等于理解概念关系','按题目追溯概念与先修证据'),
('持续学习','受阻时机械刷题，辅助时机难判断','可选自述与情境建议，保留自主选择')],[2.7,6.3,7.4])
heading('2.2 政策和教育应用契合')
para('教育部等九部门《关于加快推进教育数字化的意见》提出以应用为导向，探索“人工智能＋教育”新场景，并强调数据治理和安全保障。汇智云学将这些要求落实到课程级可验证工具：内容有来源、模型有边界、学习者可控制数据使用。[4]')
para('本届鲁信杯教育创新赛道覆盖教育核心算法、模型、平台与工具应用。项目拟以高校专业课程为切入点，同时探索教师内容审核与软件研发协作，体现人工智能服务真实教学需求的产教融合方向。[1]')
heading('2.3 优先服务的用户')
para('首期面向山东农业大学自愿参加的成年学生，围绕农业生态学及智慧农业相关课程开展小规模使用。教师是课程内容审核和评价协议的共建方。项目不以摄像头开启率衡量参与程度，也不将情绪结果用于成绩评定、考勤或学生排名。')

# 5
page('三 产品方案与前期基础','产品方案')
heading('3.1 一份资料形成连续学习项目')
figure('flow.png','图 1 拟建设的课程复习流程  各环节保留来源与事件记录')
para('学生导入有权使用的教材节选、讲义或个人笔记后，系统先生成知识点和练习候选，并标明出处。学生或教师确认关键内容，形成可持续修订的课程项目。每次作答后，系统更新记忆记录；需要辅助时，优先检索该题对应的知识解释，避免泛化聊天替代课程证据。')
heading('3.2 一次学习中的用户体验')
para('以“生态系统能量流动”为例，学生进入当天复习清单，先独立回答。若回答错误，系统展示来自已授权资料的相关说明，再提供查看先修概念的入口；看过答案后的复述被标记为辅助学习，不伪装成独立回忆成功。用户可自行报告需要休息，或在明确授权后参与情境观察研究。')
para('系统不会因一个表情就认定学生疲劳或遗忘。所有建议均允许跳过，关闭情境模块仍可完成资料整理、题目练习和记忆调度。学习结束后，学生看到本次完成情况、需要再次复习的内容和可追溯的薄弱知识关联。')
heading('3.3 农学场景的迁移方式')
para('首期用真实课程概念建立映射和审核规范，形成适合术语辨析、过程理解和知识记忆的示范项目。随后按同样的数据结构迁移到其他课程。农业学科是首个应用切口，产品能力不依赖单一专业名词库。')

# 6
page('三 产品方案与前期基础','前期成果')
figure('prototype.jpg','图 2 现有 ReciteHelper 桌面原型的答题与知识辅助界面  来源为项目自有资料')
para('项目具有连续的软件实现基础。ReciteHelper 已形成资料处理、题目练习、知识库辅助和复习相关模块；OpenRecite 将其中平台无关的复习能力拆为独立核心，可供不同客户端调用。图中界面属于前期原型，不能据此认定图谱和 Emotion-LLaMA 功能已经完成。')
table(['成果','现阶段证据','下一步'],[
('复习核心','FSRS-6、SM-2、Leitner 与答题处理源码','建立一致性与回放验证'),
('独立 SDK','OpenRecite 包项目及本地 NuGet 产物','完善调用示例与版本管理'),
('课程辅助原型','已有桌面界面和知识库相关代码','进行课程内容审核与可用性验证'),
('融合研究','图谱、情境、事件与实验方案','先补事件底座，再开展小样验证')],[3.1,6.6,6.7])
para('目前未把用户规模、付费收入、试点合作、专利或学习效果改善列为既有业绩。参赛展示将提供源码、核心包、可运行原型和可复现的实验设计，并以实际测量补充后续结果。',small=True)

# 7
page('四 技术原理与融合设计','总体架构')
figure('architecture.png','图 3 拟采用的分层架构  情境推理与复习核心解耦')
para('客户端负责展示课程、收集真实作答和落实用户选择；业务层负责资料生成、检索与建议；复习内核只处理稳定的题目数据、作答事实和调度状态。知识图谱与情境记录各自保存，避免一个模型故障中断整个学习流程。')
para('“云学”体现服务能力和课程协作可扩展，不意味着所有学习数据默认上云。个人数据优先本地保存，文本生成服务由用户或部署方明确配置；涉及音视频的研究推理单独授权，数据不随普通项目分享。')
para('项目拟建设统一事件层，分别记录题目展示、用户提交、判题完成和反馈展示时间。学生真实反应时不包含网络判题耗时；每次作答只提交一次状态更新，为后续模型比较和教学评价提供一致基础。')

# 8
page('四 技术原理与融合设计','生成式 AI 与知识图谱')
heading('4.1 让生成内容能够核查')
para('生成式 AI 的任务是从课程资料中提出知识点、题目、答案与解释候选。系统保留文档版本、页码或片段位置，要求关键事实能够回到原文。对解释、题型、正确答案进行规则检查，对歧义和重要概念交由人工审核。模型无法找到依据时应提示需要补充资料，而不是编造出处。')
heading('4.2 从章节列表到概念关系')
para('知识图谱拟采用本地 SQLite 保存概念、别名、关系、题目映射和来源证据。每道题和每个概念拥有持久标识，材料修改产生新版本，避免同名概念被误合并。先修、上下位、相关和对比关系分别建模；所有参与检索扩展和排序的关系必须进入审核通过状态。[10]')
table(['对象','记录内容','用途'],[
('概念','名称、别名、定义、学科范围','识别同义表达与同词异义'),
('关系','先修、上下位、相关、对比','解释知识之间的联系'),
('题目映射','核心概念、辅助概念、版本','将具体错题连接到知识结构'),
('来源证据','文档、页码、片段、审核记录','使解释可追溯、可修订')],[2.6,7.1,6.7])
heading('4.3 图谱如何改变学习体验')
para('系统从错题对应的核心概念开始，在一至两跳范围内寻找有依据的解释或先修提示。首期图谱用于辅助理解，不把邻居题目全部判为薄弱，也不自动改写记忆参数。未来排序实验先固定 FSRS 选定的题目集合，仅在同等风险的题目内改善概念覆盖；图谱不可用时沿用基础队列。')
para('这一路线将内容生成与事实确认分开，使课程共建可以渐进推进。教师可先维护少量重要概念和易混淆关系，不必一次性构建覆盖全部课程的庞大图谱。')

# 9
page('四 技术原理与融合设计','FSRS 记忆调度')
heading('4.4 用历史作答估计回忆概率')
para('FSRS 以难度、稳定度和可提取性刻画记忆。稳定度 S 可以理解为记忆保持能力，时间 t 增长时预测回忆概率下降。系统根据真实回忆结果更新状态，使同一道题的后续安排能够适应不同学习者。当前核心使用 FSRS-6，保留 SM-2 和 Leitner 作为传统算法接口与对照，不将算法名称包装成项目自研成果。[6]')
fsrs_equation()
para('式中 R 为预测回忆概率，d 为衰减参数，F 由衰减参数确定。当 t 等于 S 时，模型定义下 R 为 0.9。这里的 0.9 是调度目标的解释，不是对某个用户实际记忆率的保证。',small=True)
figure('retention.png','图 4 不同稳定度下的 FSRS 曲线示意  使用固定参数绘制  非用户实测结果')
para('产品将当日到期题目作为优先复习对象，避免仅凭主观熟悉感重复同类内容。对新用户使用默认参数，积累足够、可用且具有时间顺序的历史后再研究个性化拟合。个性化效果必须在未用于训练的后续记录上验证，训练损失变小不等于学习效果提高。')

# 10
page('四 技术原理与融合设计','Emotion-LLaMA 情境观察')
heading('4.5 识别任务与学习任务分开验证')
para('Emotion-LLaMA 是面向多模态情绪识别和解释的研究模型，利用视觉、音频以及文本相关信息生成情绪描述。项目拟把它作为独立研究组件，探索这些观察能否为学习受阻、辅助时机提供补充信息，而不是据此判定学生能力或心理健康。[7][8]')
para('论文完整路径与公开 demo 路径存在差别：demo 主要使用视频首帧和音频，并将两组视觉特征置零。因此，项目将固定源码版本、权重和预处理流程，分别记录模型真正使用的输入，不能用论文基准成绩代替本产品的实测表现。')
table(['信号或状态','产品处理','验证重点'],[
('自愿提供的音视频','短时采样、异步推理、私有保存','光照、遮挡、语言与设备差异'),
('模型情绪输出','保存原始结果及版本，单独做校准','置信度与真实错误率是否匹配'),
('本人自述','独立记录需要帮助或休息的意愿','不能被模型结果覆盖'),
('缺失或失效输入','返回不可用，保留正常复习','不能把无声音或无人脸当中性')],[3.3,6.6,6.5])
heading('4.6 当前硬件下的实施选择')
para('当前设备仅识别到核显与约 16 GiB 内存，不能承诺官方模型本地实时推理。首期使用行为记录与自述建立基线，在具备合适 GPU 且用途授权明确的研究环境进行离线验证。云端或独立推理服务只作可选扩展，既不默认上传个人视频，也不成为复习功能的前置条件。')
para('情绪标签与认知状态存在差异，悲伤、担忧或中性不能直接换算成疲劳、专注或遗忘。只有在真实学习场景中形成可重复、可校准的证据后，才决定是否用于辅助建议。')

# 11
page('四 技术原理与融合设计','多模态融合与项目创新')
heading('4.7 分阶段建立融合能力')
para('第一阶段保持 FSRS 的评分和记忆更新规则，以知识图谱、自述和行为证据提供可接受或拒绝的学习建议。第二阶段只计算情境特征加入后的“影子预测”，不控制用户复习计划。第三阶段只有在时间外预测与真实干预均获得支持后，才研究受限的稳定度修正。')
table(['层次','融合内容','进入下一阶段的条件'],[
('会话层','建议查看先修解释、调整呈现或短暂休息','用户可控，误触发率和打扰可接受'),
('预测层','在 FSRS 基线之上加入行为、图谱、自述与情绪特征','相较低成本基线有稳定增量且校准不恶化'),
('记忆层','研究有边界、可回退的有效稳定度修正','独立调度试验支持延迟回忆或负担改善')],[2.6,7.2,6.6])
heading('4.8 项目拟形成的创新点')
para('其一，将课程来源、知识关系和逐题记忆历史统一到学习项目，推动“有出处的内容生成”与“按遗忘安排的复习”协同。其二，把多模态信号放在可解释、可退出的辅助层，保留无传感器的完整学习路径。其三，通过分阶段消融和延迟测验评价新增能力，形成可重放的教育 AI 验证方法。')
para('这些创新主要是系统集成、数据组织与应用验证创新。FSRS、Emotion-LLaMA 和通用大模型分别属于已有算法或外部研究成果；项目自有价值来自课程工作流、事件与证据体系、策略实现和真实场景评估。')
heading('4.9 防止把相关性当作效果')
para('模型结果必须在作出决策之前已经可用。答题内容、显示的标准答案和反馈后的表情不能用于预测同一次作答，再宣称是情绪带来的收益。所有辅助提示单独记为学习曝光，保证后续评价能够区分独立回忆与提示后回答。')

# 12
page('五 研发验证与评价指标')
heading('5.1 从内容可用到学习有效')
para('首轮拟邀请一个课程小组自愿参与，以 20–40 名成年学生、2–3 个课程单元作为可行性目标。该数量用于发现操作、内容和日志问题，不保证具备验证微小记忆增益的统计功效。正式效果比较将在先导数据估计方差后确定样本量，并明确伦理、退出和数据使用安排。')
table(['验证对象','方法','拟定验收目标'],[
('生成内容','人工复核题干、答案、原文证据','关键题正确答案均经审核；错项可追溯修订'),
('知识图谱','标注题目映射及先修边','启用关系有证据；抽查先修精确率目标≥95%'),
('复习内核','固定事件回放与缺失故障检查','同输入可重放；一次作答一次更新'),
('情境观察','分日期保留集及模态缺失对照','报告校准、覆盖、误触发和推理时延'),
('学习效果','固定学习预算下的 7 日与 30 日测验','以无提示回忆为主，报告差值和区间')],[2.5,6.8,7.1])
heading('5.2 让每个技术模块接受对照')
para('预测比较依次采用 FSRS、FSRS 加行为、再加图谱、再加自述、最后加入 Emotion-LLaMA。若自动观测不优于行为加自述基线，就不作为必需功能。消融还应区分音频、视觉和文本，并检查题目内容泄漏。')
para('干预试验固定基础调度和材料版本，在合格会话中随机开启或关闭建议。主要看延迟回忆、总学习时间和退出情况，提示接受率仅为过程指标。拒绝建议者仍按原分配分析，未完成随访单列缺失；不能把当场心情改善直接等同于长期记忆提高。')
heading('5.3 结果发布口径')
para('所有准确率、提效比例和用户增长结果须附样本、对照、时间范围和版本。现阶段不宣称已提高记忆率或节省某一固定比例时间；计划书中的阈值是研发目标。后续允许发表“未发现增量收益”的结果，并据此缩减复杂功能。')

# 13
page('六 市场定位与竞争分析')
heading('6.1 从可服务的小市场切入')
para('教育部公布的 2024 年统计中，各种形式高等教育在学总规模为 4846 万人，说明高等教育学习服务具有广泛用户基础；这一数字包含多种教育形式，不能直接当作本项目付费市场。[5] 项目按课程触达能力测算：首期争取覆盖 4 个课程学习小组，每组约 30–50 人；验证之后再扩至 20 个课程班级。以上是运营目标，不是现有用户或合作承诺。')
para('首批高意愿用户是有明确复习材料、持续复习意愿和个人电脑使用条件的大学生。首批组织客户拟为课程组、教学团队和承担培训任务的机构。短期优先验证学生重复使用和教师愿意共建内容的条件，再判断付费意愿。')
heading('6.2 与现有工具比较')
table(['产品或方式','公开能力及优势','汇智云学的差异化方向'],[
('Anki','成熟的记忆卡片生态与 FSRS 调度[11]','围绕课程资料、答案证据与概念关系整合学习流程'),
('Quizlet','笔记转学习材料和练习工具[12]','研究中文专业课程的内容审核、逐题记忆和情境辅助'),
('NotebookLM','围绕来源材料生成解释、总结等内容[13]','把来源辅助与持续作答历史、延迟记忆评价连接'),
('课程题库','题目组织明确，适合章节练习','补充自有资料整理和按个人历史安排复习')],[2.7,6.5,7.2])
para('比较仅依据公开能力与本项目拟定方向，不表示竞品缺少所有相关功能。项目不以“功能更多”作为竞争结论，而以课程场景的任务完成、可靠性、成本和学习证据作为选择依据。')
heading('6.3 可建立的持续优势')
para('具有授权的高质量课程映射、经核查的知识关系、稳定的学习事件规范，以及可复现的课程验证流程，将比模型名称更难替代。用户数据归用户控制，不通过锁定导出格式形成竞争壁垒。')

# 14
page('七 商业模式与推广路径')
heading('7.1 基础开放与服务收费结合')
para('OpenRecite 核心采用 LGPL-3.0-only 许可。项目拟保留基础本地复习能力，以课程整理、托管生成额度、协作管理和实施支持形成增值服务。收费对象是可交付的服务与维护，不把开源算法包装为独占技术。第三方模型各自遵守许可证，情绪研究模块不计入基础收入前提。')
table(['服务层级','拟定定价假设','交付边界'],[
('个人基础版','免费','本地核心复习、项目导入导出、基础记录'),
('个人增值版','99 元／年','限额内容生成、同步与增强服务，按成本设额度'),
('课程共建服务','6000 元／课程年起','内容模板、教师审核支持与课程使用报告'),
('机构实施服务','12000 元／项目起','部署、接口适配和培训，按合同约定范围')],[3.2,4.4,8.8])
para('上述价格用于经营测算，并非市场调查后的最终售价。正式定价前需测量生成调用、人力审核、客服及机构交付成本，验证用户愿付价格。免费用户不默认获得无限云模型调用，也不要求上传个人视频。')
heading('7.2 推广形成的实际流程')
para('先邀请课程小组体验一个完整单元，完成资料授权、题目审核、使用与反馈。再以课程示范说明可交付效果，通过学生学习社群与教师共建推荐扩展。机构服务应从有明确责任人与预算的单门课程启动，不以大量免费注册代替有效使用。')
para('运营跟踪从触达到激活、完成首次复习、连续周使用、课程续用和付费转化。每一层记录实际人数和退出原因，关注获客成本与服务成本，避免为短期增长过度补贴。拟建立问题反馈和内容修订机制，让课程更新与用户需求进入同一迭代流程。')

# 15
page('八 团队组织与实施进度')
heading('8.1 团队组织')
para('穗知创新团队由山东农业大学农学院刘宗睿负责项目统筹，指导教师为张金诺副教授。负责人围绕自身专业课复习需求推进软件原型与核心整理。指导教师公开研究方向包含智慧农业与智能决策，为项目结合专业场景提供指导基础；本计划不将导师既有论文或专利列为本项目成果。[16]')
table(['角色','拟承担职责','人员安排'],[
('项目负责人','需求、核心软件、版本与参赛统筹','刘宗睿  农学院  2024210172'),
('指导教师','研究方案、专业场景与成果规范指导','张金诺  副教授'),
('算法与数据协作','图谱构建、情境校准、实验分析','拟招募 1–2 名协作成员'),
('产品与课程协作','交互设计、内容复核、试点沟通','拟招募 1–2 名协作成员')],[3.0,7.3,6.1])
heading('8.2 分阶段交付')
table(['时间','交付成果','判断标准'],[
('2026 年 10 月','参赛原型整理、事件方案与课程样例','展示来源清楚，区分已有和拟研发'),
('2026 年 11–12 月','图谱小集、课程可用性试点、早期延迟测试','内容可审核、日志可回放、形成问题清单'),
('2027 年 1–3 月','完成较长随访、情境离线验证','效果与成本有对照，许可满足使用范围'),
('2027 年 4–6 月','依据证据开放增值服务与课程扩展','真实续用、交付成本和反馈支持扩大规模')],[3.0,7.3,6.1])
para('团队按周复盘需求与缺陷，关键内容审核、实验口径和发布由负责人统一登记。跨专业协作不超过本届学校组人数限制。合作课程与试点参与者均需另行确认，本计划不把拟联系对象写成已签约伙伴。',small=True)

# 16
page('九 资金预算与经营测算','研发投入')
heading('9.1 首年研发与验证预算')
para('首年拟以 18 万元现金支出完成核心迭代、课程验证和早期服务交付。采用已有软件基础、小规模内容共建与按需算力，优先投资数据质量和验证能力。团队课余投入与学校既有公共资源不计为虚构现金收入；若不能取得资源支持，应相应缩小范围。')
table(['预算项目','金额  元','使用说明'],[
('研发与必要技术协作','60000','核心迭代、接口和软件质量改进'),
('模型调用与算力','20000','限额生成、离线实验与性能测量'),
('课程内容和试验支持','30000','审核、样例整理、参与支持与分析'),
('知识产权和合规服务','15000','权属梳理、合同及数据流程审查'),
('课程推广与培训','15000','课程演示、使用培训和运营材料'),
('设备与工具','20000','按需共享、租用或购置，先测后选'),
('预备费用','20000','验证返工、延期与价格变动'),
('合计','180000','计划预算，按实际合同与支出登记')],[4.2,3.2,9.0])
heading('9.2 资金使用与资源申请')
para('拟申请 30 万元项目支持，以非股权研发资助、孵化资源和真实服务回款优先组合。资金目前未落实，分阶段拨付建议为：研发基础 12 万元，课程试验 10 万元，达到使用与续用目标后再投入 8 万元。未达到前阶段条件时冻结后续扩张。')
para('如赛事提供算力额度、导师或对接资源，按实际获批情况减少现金采购，不把政策资源预先确认为收入。项目早期不承诺投资回报或估值，若将来涉及股权融资，再依据权属、主体设立和真实经营情况制定方案。')

# 17
page('九 资金预算与经营测算','经营情景')
heading('9.3 三年基础情景')
para('以下以经营年度而非自然年度测算，自正式服务启动起计算。收入采用当年实际付费人数、课程数和项目数乘以对应价格；不包含情绪模型授权收入、广告收入或数据销售。该表是可检验的经营假设，不是已签订单或业绩预测保证。')
table(['项目','第 1 年','第 2 年','第 3 年'],[
('个人付费用户','300 人','1500 人','4500 人'),
('个人服务收入','29700','148500','445500'),
('课程服务规模','4 × 6000','12 × 6000','30 × 8000'),
('课程服务收入','24000','72000','240000'),
('机构实施规模','1 × 12000','3 × 12000','8 × 20000'),
('机构实施收入','12000','36000','160000'),
('收入合计  元','65700','256500','845500'),
('现金运营支出  元','180000','360000','660000'),
('收支差额  元','−114300','−103500','185500')],[5.3,3.7,3.7,3.7])
para('测算中个人年价统一为 99 元；第三年课程和实施客单价提高，前提是增加实际交付内容并得到客户认可。表内支出包含研发、算力、内容、交付和运营的计划现金费用，未作税项、折旧、资本化及应收账期的会计利润调整。')
heading('9.4 敏感性与现金管理')
para('若所有收入仅达基础情景的 60%、支出不变，三年累计现金收支约为 −49.9 万元；若为 100%，累计约 −3.2 万元；若为 130%，累计约 31.8 万元。前两年的基础累计缺口约 21.8 万元，30 万元支持额度需兼顾周转和安全余量，实际按月控制现金。')
para('经营重点是验证续用与成本，而不是提前追求规模。课程服务应采用明确的范围与分阶段验收；模型调用设置额度。收入、回款和支出出现偏差时，先延后扩张和非必要模型投入，再调整产品范围。')

# 18
page('十 风险控制与权益保护')
table(['风险','可能后果','控制措施'],[
('内容幻觉与错误答案','误导复习，形成错误记忆','原文证据、关键答案审核、版本回滚和纠错入口'),
('情境识别偏差','建议不合适或打断学习','独立校准、可拒判、用户自主开关和低频建议'),
('缺失或迟到信号','策略误用旧观察','记录时间与模态状态，失效即回到基础复习'),
('版权与模型授权','材料或权重不能用于计划用途','资料授权、依赖清单、按用途审核第三方许可'),
('数据泄露','暴露学习记录与音视频','本地优先、分区保存、加密与最少权限'),
('效果和经营未达目标','试点不能扩展，现金消耗','阶段验收、小额试点、冻结无证据扩张')],[3.2,5.1,8.1])
heading('10.1 数据权益')
para('采集音视频需要明确目的与必要性，单独说明用途、保存期、是否上传及撤回方式。依法涉及敏感个人信息的处理履行相应要求。首期试点限自愿成年人，不以参加研究换取课程成绩或基本服务。系统不进行身份识别，不公开个人情绪标签，教师只能在授权范围内查看与教学目的相关的汇总信息。[14]')
heading('10.2 开源与商业化边界')
para('OpenRecite 自有核心按 LGPL-3.0-only 许可发布，但这一声明不覆盖外部模型或全部桌面依赖。Emotion-LLaMA 的源码许可与 MER 数据及衍生模型条款需要分别核查，其中科学、非商业用途限制不能因独立部署而消失。[9] 在取得适用授权前，情绪模块限定于允许范围内的研究验证，不进入付费服务承诺。')
para('项目拟先完成代码贡献、课程材料与模型依赖的权属清单，再决定申请软件著作权或其他成果保护；尚未申请和获权的内容不得写成已有专利。')

# 19
page('十一 教育价值与成果转化')
heading('11.1 服务专业学习而非替代学习')
para('汇智云学的教育目标是帮助学生开展更有依据的独立回忆和知识理解。系统将复习任务、来源说明和学习记录组织起来，使学生知道为什么被安排这道题、错误与哪些概念有关，同时保留独立思考和向教师求助的入口。AI 生成与情绪观察不承担教学评价的最终判断。')
para('对农业类专业，项目有机会将概念记忆与过程理解联系起来。例如围绕课程中易混淆的资源、能量和生态关系建立可检索说明，再用不同题型检验理解。该课程样例的价值由教师审核和学生使用决定，不能单靠图谱节点数量说明教育质量。')
heading('11.2 产教协作方式')
para('拟以课程组提供场景与内容审核、学生团队提供软件与验证工具、技术服务伙伴提供合规算力和部署支持的方式推进。每一方的贡献、材料授权、数据范围和成果归属在合作前书面约定。学院和指导教师的支持不自动代表全校采购或机构背书。')
table(['成果类别','拟交付内容','检验方式'],[
('软件与工具','复习核心、课程原型、可关闭情境接口','版本、调用示例、回放与故障记录'),
('课程资源','审核后的题目、概念映射与来源证据','抽查质量、更新记录、授权清单'),
('教育研究','消融、延迟回忆与使用负担评估','协议、样本、对照和完整结果'),
('推广材料','课程演示、使用手册和培训材料','用户可完成核心任务，反馈可处理')],[3.0,7.5,5.9])
heading('11.3 预期社会价值')
para('项目拟通过基础功能开放和较低硬件门槛，扩大有资料但缺少系统复习工具的学生的选择。长期就业与经济效益应随真实业务增长逐步形成，当前不虚列带动就业人数。我们将以可持续维护、可靠内容和经验证的学习收益，作为成果转化是否值得扩大的依据。')

# 20,21 references
refs=[
('1','山东财经大学团委','关于组织报名参加“鲁信杯”第一届山东省“人工智能＋产教应用”大赛的通知','2026','https://tuanwei.sdufe.edu.cn/info/1022/46851.htm','用于确认本届赛事名称、教育创新赛道及组队、申报规则。校内截止时间须以本校通知为准。'),
('2','武汉传媒学院创新创业学院','中国国际大学生创新大赛高教主赛道商业计划书参考模板','2024','https://cxcyxy.whmc.edu.cn/info/1091/2592.htm','参考公开正文的项目、市场、产品、商业、团队、财务和教育价值结构，不套用其中其他赛事的资格条件。'),
('3','湖南工程学院创新创业指导中心','关于启动2022年“挑战杯”大学生创业计划竞赛的预通知','2021','https://cxcy.hnie.edu.cn/info/1010/2095.htm','参考真实项目、实践与预期成果及知识产权证明要求。另检索2020年内蒙古师范大学模板发布页，附件受验证码限制，未作为已读正文。'),
('4','教育部等九部门','关于加快推进教育数字化的意见  教办〔2025〕3号','2025','https://www.moe.gov.cn/srcsite/A01/s7048/202504/t20250416_1187476.html','用于教育应用、数字化和安全治理背景。'),
('5','教育部','2024年全国教育事业发展统计公报','2025','https://www.moe.gov.cn/jyb_sjzl/sjzl_fztjgb/202506/t20250611_1193760.html','高等教育在学总规模为4846.00万人，包含多种教育形式，仅作用户背景。'),
('6','Open Spaced Repetition','The Algorithm  FSRS 官方算法说明','访问于2026-10-13','https://github.com/open-spaced-repetition/awesome-fsrs/wiki/The-Algorithm','支持FSRS-6状态、回忆曲线和参数定义；不等于本项目学习效果验证。'),
('7','Cheng 等','Emotion-LLaMA  Multimodal Emotion Recognition and Reasoning with Instruction Tuning','NeurIPS 2024','https://arxiv.org/abs/2406.11161','用于多模态情绪模型原理；论文情绪基准不是课程回忆基准。'),
('8','ZebangCheng/Emotion-LLaMA','模型源码和推理路径','固定提交 6f8d05087b2a91e445ad986545977fda7eb1b6a4','https://github.com/ZebangCheng/Emotion-LLaMA/tree/6f8d05087b2a91e445ad986545977fda7eb1b6a4','用于核实公开demo与完整特征处理的区别。'),
('9','Emotion-LLaMA 作者仓库','MER 数据许可条款','访问于2026-10-13','https://github.com/ZebangCheng/Emotion-LLaMA/blob/6f8d05087b2a91e445ad986545977fda7eb1b6a4/LICENSE_EULA.md','用于核实数据与衍生模型用途限制，独立于源码许可。'),
('10','Microsoft GraphRAG','Local Search','访问于2026-10-13','https://microsoft.github.io/graphrag/query/local_search/','参考关系与原文结合的检索思路，本项目另设计教育概念模式。'),
('11','Anki','Deck Options','访问于2026-10-13','https://docs.ankiweb.net/deck-options.html','竞品公开功能资料，包含FSRS和目标保持率说明。'),
('12','Quizlet','AI Study Guide Maker','访问于2026-10-13','https://quizlet.com/features/study-guides/','竞品公开功能资料，说明笔记转学习资料服务。'),
('13','Google','NotebookLM 教育应用简介','访问于2026-10-13','https://services.google.com/fh/files/misc/notebooklm_gfe_one_pager.pdf','竞品公开功能资料，说明围绕来源材料的学习辅助。'),
('14','全国人大常委会  工信部公开转载','中华人民共和国个人信息保护法','2021','https://www.miit.gov.cn/jgsj/zfs/fl/art/2022/art_515a4b20c12f430eab54bb4f56d89f56.html','用于个人信息、敏感信息和同意机制的设计依据。'),
('15','穗知创新团队项目资料','OpenRecite 与 ReciteHelper 源码、包产物及研究方案','2026-10-13','https://github.com/ArabidopsisDev/OpenRecite','本地源码核查、前期原型图片和方案为开发基础；不提供商业订单或学习效果证明。'),
('16','山东农业大学农学院','张金诺教师介绍','访问于2026-10-13','https://agronomy.sdau.edu.cn/_t65/2024/1209/c1344a247140/page.psp','仅用于核对指导教师职称和研究领域，不把个人既有成果归入本项目。')]
for idx,chunk in enumerate([refs[:8],refs[8:]]):
    page('参考资料与成果依据' + (' 一' if idx==0 else ' 二'))
    for num,author,title,year,url,note in chunk:
        p=para(f'[{num}] {author}．{title}．{year}。',bold=True,small=True)
        p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.space_after=Pt(2);p.paragraph_format.keep_with_next=True
        p=para(url,small=True);p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.space_after=Pt(2);p.paragraph_format.keep_with_next=True
        p=para(note,small=True);p.paragraph_format.first_line_indent=Pt(0);p.paragraph_format.space_after=Pt(11)

# Avoid unwanted title borders and theme-dependent heading colors.
for sty in ['Title','Subtitle','Heading 1','Heading 2','Heading 3']:
    st=doc.styles[sty]
    for node in st.element.xpath('.//w:pBdr'):
        node.getparent().remove(node)

settings=doc.settings.element
upd=OxmlElement('w:updateFields');upd.set(qn('w:val'),'true');settings.append(upd)
output=OUT/'汇智云学_鲁信杯项目计划书.docx'
doc.save(output)
(WORK/'content.txt').write_text('\n\n'.join(all_text),encoding='utf-8')
(WORK/'page-plan.json').write_text(json.dumps({'planned_pages':len(pages)+1,'titles':pages,'characters':sum(len(t) for t in all_text),'output':str(output)},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'output':str(output),'planned_pages':len(pages)+1,'characters':sum(len(t) for t in all_text)},ensure_ascii=False))
