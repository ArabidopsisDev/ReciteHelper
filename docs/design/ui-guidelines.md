# ReciteHelper 界面设计规范

## 1. 产品目标

ReciteHelper 是一款给考前复习的学生使用的 Windows 桌面工具：导入课程资料，生成题目，练习或模拟考试，看错题，再按 FSRS-6 安排复习。用户每次会连续使用几十分钟，大部分时间都在读题和输入答案。

所以界面要做到：

- 看起来像一个可靠的 Windows 工具，不像网页落地页，也不像 AI 产品演示。
- 阅读区域（题干、知识点、解析）优先，其它界面元素往后放。
- 对错反馈要清楚，但不能吵。要像老师在作业本上批改，而不是满屏弹窗。
- 模拟考试窗口继续保留仿纸质试卷的排版（宋体/黑体、密级标识），这是本产品特有的语境。

## 2. 参考资料

| 参考 | 借鉴内容 |
| --- | --- |
| Microsoft Fluent 2 / Windows 11 原生控件 | 4px 控件圆角，1px 描边，32px 控件高度，系统图标字体（Segoe Fluent Icons / Segoe MDL2 Assets），焦点框规范 |
| Visual Studio 启动窗口 | 主窗口“最近项目 + 开始操作”的布局。原界面本来就是这个结构，只调整视觉，不改用户习惯 |
| Anki / 纸质练习册 | 题目区以排版为主，答题卡是密集的数字格，不做成卡片 |
| 纸质试卷 | 考试窗口沿用试卷字体和版式 |

## 3. 需要避开的“AI 生成感”

根据 Anthropic frontend-design skill、AwesomeDesignSystem《AI slop vs human》、ai-design-tells、open-design anti-ai-slop 等分析，下面这些是最常被认出的特征，本项目一律不用：

- 用 emoji 当图标（💡 📢 ✨ 🚀 等），需要图标时使用系统图标字体。
- 紫色、靛蓝（如 #6366F1、#8B5CF6），以及蓝到紫这类双色渐变。
- 卡片套卡片，每层都有自己的内边距、圆角和阴影。
- 所有元素都用同一个大圆角（≥12px），或在所有面板下铺同一种柔和阴影。
- 圆角卡片左侧加一条粗彩色边。
- 悬停上浮、缩放、发光，悬浮在内容上方的装饰卡片，渐入动画。
- 三张一样的功能卡片，每张一个彩色图标块。
- 奶油色底色 + 衬线大标题 + 陶土色强调色（这种组合同样已被识别为 AI 风格）。
- 空洞的文案，例如“开启智能学习新体验”，以及到处出现“AI 驱动”标签和星星图标。

## 4. 视觉语言

### 颜色

强调色用墨蓝：取钢笔墨水的颜色，契合“做题、批改”的语境。错误色用朱红：取批改红笔的颜色。整套界面只用一个强调色，其余都是中性色。

| 令牌 | 值 | 用途 |
| --- | --- | --- |
| `WindowBackground` | `#F2F3F5` | 窗口底色 |
| `Surface` | `#FFFFFF` | 内容面板 |
| `SurfaceAlt` | `#F7F8FA` | 侧栏、面板标题栏、只读区 |
| `SurfaceHover` / `SurfacePressed` | `#EEF0F3` / `#E4E7EB` | 列表行、次要按钮的交互状态 |
| `Divider` | `#E1E4E8` | 面板边框、分隔线 |
| `ControlStroke` | `#C9CED4` | 输入框、按钮描边 |
| `TextPrimary` / `Secondary` / `Tertiary` | `#1E2227` / `#59616A` / `#868D95` | 文字三级 |
| `Accent` | `#24527A` | 主按钮、选中、当前题 |
| `AccentSubtle` | `#E7EEF5` | 选中项底色 |
| `Correct` | `#2E6B3F`，底 `#EAF3EC` | 答对 |
| `Wrong` | `#B3261E`，底 `#FBECEA` | 答错、危险操作 |
| `Warning` | `#8A5A00`，底 `#FFF4DC` | 提醒 |

### 字体

- 界面字体：`Segoe UI, Microsoft YaHei UI`。这是系统原生字体，不另外引入字体。
- 字号：说明文字 12，正文 14，题干 16（行高 26），小标题 16 半粗，窗口标题 20 半粗。
- 只用 Regular 和 SemiBold 两种字重。数字题号使用 SemiBold。
- 考试试卷区继续使用 `Times New Roman, SimSun / SimHei`。

### 形状与层级

- 圆角分三档：答题卡格子 2，控件 4，面板 6。不使用更大的圆角。
- 不使用阴影。层级靠底色差异和 1px 分隔线表达。
- 每个区域只有一层面板。面板里的列表用分隔线区分各行，不再嵌套带边框的卡片。
- 间距统一用 4 / 8 / 12 / 16 / 24 / 32。

### 动效

- 只保留对用户操作的反馈（悬停变色、按下变深）。不做装饰性动画。
- 原有的 Phonk 彩蛋属于功能，保持不变。

### 图标

- 使用 `Segoe Fluent Icons, Segoe MDL2 Assets` 字形，例如关闭 `E711`、对勾 `E73E`、警告 `E7BA`、信息 `E946`、对话 `E8F2`。
- 原有 PNG 图标可以继续用在文件类型、项目图标等位置，但不再放进彩色色块里。

## 5. 共享资源

所有样式放在 `ReciteHelper.Wpf/Themes`，由 `Generic.xaml` 合并。各窗口不再在本地重复定义主按钮、输入框等样式。

- 窗口：`AppWindowStyle`
- 按钮：默认按钮（隐式样式）、`PrimaryButtonStyle`、`SecondaryButtonStyle`、`SubtleButtonStyle`、`DangerButtonStyle`、`IconButtonStyle`、`ListRowButtonStyle`、`LinkButtonStyle`
- 输入：隐式 TextBox / PasswordBox / ComboBox / CheckBox / RadioButton，并保留 `ModernTextBox`、`ModernPasswordBox` 两个旧键名
- 文字：`TitleTextStyle`、`SubtitleTextStyle`、`SectionLabelTextStyle`、`BodyTextStyle`、`CaptionTextStyle`、`FieldLabel`、`IconGlyphStyle`
- 面板：`PanelStyle`、`PaneHeaderStyle`、`FooterBarStyle`
- 提示条：`NoticeInfoStyle`、`NoticeCorrectStyle`、`NoticeWrongStyle`、`NoticeWarningStyle`
- 答题卡：`AnswerCardButtonStyle`、`CorrectAnswerStyle`、`WrongAnswerStyle`、`CurrentQuestionStyle`，以及旧键名 `*CardStyle`
