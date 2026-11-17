# TKCode.Arc 研究与 WPF 适配记录

研究与修订日期：2026-11-16。工作目录：`D:\Projects\ReciteHelper`。

## 来源与结论

继续使用 WPF，依据 TKCode.Arc 的 MIT 源码重写模板。固定参考版本为 `1bef215d677fdbaf7820f4a86b4dacd51a06ad1e`。

| 参考 | 采用内容 |
| --- | --- |
| [Arc 源码](https://github.com/AIDotNet/TKCode.Arc/tree/1bef215d677fdbaf7820f4a86b4dacd51a06ad1e) | ArcDesignToken、ArcPalette、ArcFonts、ButtonToken，以及 Button、Input、Textarea、Select 模板与状态。 |
| [主题文档](https://docs.tkcode.si/) | 中性色、字号、控件尺寸、圆角与间距。 |
| [设置页](https://docs.tkcode.si/shots/Docs/Blocks/SettingsPage/images/SettingsPage.webp) | 左侧分组导航，右侧连续配置区，限制正文宽度，以间距和细线分组。 |
| [侧栏](https://docs.tkcode.si/shots/Docs/Blocks/Sidebar/images/AdminNavigation.webp) | 浅灰侧栏、柔和选中态、分组入口与固定底部操作。 |
| [按钮](https://docs.tkcode.si/shots/Docs/Buttons/Button/images/BillingFooter.webp) | 黑色主操作、描边次操作与文字操作；默认中号高度 44、圆角 18。 |
| [设计集](https://uiarc.dev/showcase) | 标题、说明、操作与正文的层级和留白。 |

上一版选用了 36 像素控件、4 像素圆角和蓝色强调色，首页仍以右侧操作栏组织。它与 Arc 默认设计的差异过大。本次重新对照示例截图和实际源码，恢复中性配色、44 像素中号控件、18 像素控件圆角，并重做页面结构。

## 页面调整

- 首页：244 像素左侧工作区导航，右侧为最近项目与创建入口；项目使用线条文件夹图标。
- 项目页：学习、考试和项目管理归入侧栏；章节名称、题量与掌握程度按列对齐，智能复习为主要操作。
- 模型设置：220 像素侧栏与连续表单；点击服务名称会展开并定位配置区。正文最大宽度 760。
- 规则化出题：大类列表与表单分栏，要求和资料在同一滚动区；管理已有项目时收起不可修改的项目目录区域。
- 项目类型：具有选择状态的列表行，保留搜索与确认。
- 答题：正文最大宽度 760，题干 21 像素、行高 34；多问独立输入，批注替换原输入区域；底部操作固定。
- 经典项目创建、考试设置：移除重复定义的旧蓝色控件，采用共享表单、分区标题和单一主要操作。
- 资源中心：检索与共享表单分层，项目列表按行显示，移除多列资源卡片。
- 知识点：浅灰目录侧栏与阅读区；修正空状态在 DockPanel 中被挤窄的问题。
- 文件合并：资料按行显示，文件类型不再使用彩色图标块。
- 考试与回顾：操作栏使用共享控件；试卷正文保留纸张、宋体、题号和分值。回顾按题目连续排列，批注直接呈现在原答案上。
- 进度与异常窗口：采用相同的中性控件与排版，扩大进度窗口以完整呈现进度区。

没有引入 Avalonia 运行时、Arc Pro 源码、上游字体或图标包。WPF 保留标准文本输入、键盘导航和下拉框命名部件。

## 关键令牌

| 项目 | 当前值 |
| --- | --- |
| 正文 / 浅侧栏 | #FFFFFF / #F8F8F8 |
| 主文字 / 次文字 / 辅助文字 | #0B0B0B / #585858 / #7D7D7D |
| 边界 / 强边界 | #E9E9E9 / #C4C4C4 |
| 主按钮 | #0B0B0B，白色文字 |
| 默认控件高度 / 圆角 | 44 / 18 |
| 导航圆角 / 焦点圆角 | 14 / 22 |
| 页面标题 / 分区标题 | 28 / 18 |
| 表单与答题内容最大宽度 | 760 |

## 授权

完整 MIT 版权文本与适配说明保存在 `ReciteHelper.Wpf/ThirdPartyNotices/TKCode.Arc`，随构建输出复制。共享资源入口为 `Themes/Generic.xaml`；令牌、控件模板与页面结构分别维护。

## 验证

使用原生 WPF RenderTargetBitmap 渲染真实窗口，截图数据为离线样例。检查输入编辑、44 像素高度、下拉框键盘选择与弹窗、可编辑下拉框同步、设置定位、考试滑块与预设菜单，以及多问答题、原位批注和预览高度。较小窗口检查滚动与底部操作可访问性。

本轮截图在 [screenshots](screenshots/)：`home.png`、`chapters.png`、`settings.png`、`settings-expanded.png`、`creation-v2.png`、`cfsrs-creation.png`、`cfsrs-multipart-feedback.png`、`classic-create.png`、`exam-settings.png`、`resources.png`、`knowledge.png`、`merge.png`、`exam-current.png`、`exam-review.png`、`progress.png`。截图不代表在线服务或真实学习效果验证。

现有 131 项测试通过，WPF Release 编译通过。没有为纯样式变化新增业务测试。
