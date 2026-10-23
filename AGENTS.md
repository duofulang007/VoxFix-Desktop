# AGENTS.md

本仓库的操作约定。当前版本 **1.6.46.0**；产品说明见 [README](README.md)，
技术记录从 [docs/README.md](docs/README.md) 读起，工具入口见 [tools/README.md](tools/README.md)。

## 生产链路

- `app/Microphone.cs`：单文件 C# WinForms，负责窗口、全局快捷键、采音、焦点、送字与原生自检；无 NuGet、无 .NET SDK。
- `app/recognize.py`：stdin/stdout 逐行 JSON 子进程，负责 VAD 分段、可修订草稿、个人词典、标点和按序定稿。
  个人词典在项目根 `dictionary.json`（用户可编辑、保存即生效），解码后、标点前替换，
  草稿与定稿都过词表；文件缺失或损坏时该层自动停用。`context: "zh"` 只在含中文的整段话里替换（1.6.40.0 起按整段话判断，
  不再只看当前分段），防止改坏纯英文短语；不写 `context` 的规则在任何语言下都生效。
- `app/dictionary_rules.py`：共享纯文本词典规则与只读预览入口，不加载模型；`recognize.py` 继续导出原有函数名。词条内错法按长度降序匹配，跨词连锁替换仍按文件顺序。救援候选按文件顺序取前 64 个，窗口须显示超额提示，不得静默丢词。
- `app/hotwords.py`：解码期热词偏置（救援通道）。定稿文本出现词表词的近音片段时才用 ORT 复解该段，
  命中更多词表词才采用；额外 ONNX 会话惰性创建、闲置 180 秒释放。由 `dictionary.json` 的 `hotwords`
  字段显式启用，不写则完全不动。
- SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 推理；`app/assets/graphite-material.png` 嵌入 EXE。
- 当前程序版本 1.6.46.0 使用 R5 整轮微调 INT8；6257 条 CS-Dialogue 真人封存比较是在相同 PyTorch 检查点口径下完成，三类均优于 R4，不是 INT8 封存集完整重测。生产 INT8 已过固定音频协议、启动和送字自检；旧 174 条仅作辅助回归，不作采用判断。模型来源、哈希、验证与 R4 回滚见 `docs/R5模型启用-2026-10-23.md`。
- 1.6.29.0 起曾使用公开真人语料微调权重（`27377bac…`），
  代码与交互未变。回滚备份 `work/model.int8.onnx.20261018-original` 与 `work/VoxFix Desktop.exe.1.6.28.0`。

换模型是产品决策。Qwen3-ASR 准确率更高但延迟、内存不合要求，用户已否决；
见 `docs/中文与混说-换Qwen伪流式-2026-10-21.md`，不要仅凭准确率换回去。

## 构建与验证

必须在 PowerShell 执行构建。先退出正在运行的 VoxFix（退出会清空内存缓存），否则 EXE 被占用报 CS0016：

```powershell
Set-Location 'D:\Projects\VoxFix Desktop'
Get-Process | Where-Object { $_.ProcessName -like 'VoxFix Desktop*' } | Stop-Process -Force
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`Bypass` 只作用于本次进程，不修改用户执行策略。沙箱可能看不到真实桌面的 VoxFix 进程；
若仍报占用，需在真实桌面权限下确认进程，不能把旧 EXE 的测试当作新源码验证。

`app/recognize.py` 只在应用启动（`Microphone.cs` 的 `Shown`）和错误重试时被加载，之后整轮录音复用同一个子进程；
改完 Python 识别层必须重启 VoxFix 才会生效，否则现场看到的仍是旧模块的行为（会误判成"没修好"）。
核对方式：`Get-CimInstance Win32_Process` 里 `python.exe` 命令行含 `recognize.py` 的进程创建时间应晚于改动时间。

Python 测试统一使用 `.venv/Scripts/python.exe`，按改动范围运行：

| 脚本 | 覆盖与前提 |
|---|---|
| `tools/test_sensevoice.py` | 协议、分段、草稿、标点保留；无需模型或设备 |
| `tools/test_language_guard.py` | 中英字符边界、外文拒绝与先前定稿恢复；无需模型 |
| `tools/test_dictionary.py` | 共享规则、纯英文/中文/混说、预览进程、最长错法优先与 64 词边界；无需模型 |
| `tools/test_hotwords.py` | 近音触发、接受守卫、失败回退、会话释放与管线接线；无需模型 |
| `tools/test_asr_scoring.py` | 中文按字、英文按词、否定、静音与外文计分 |
| `tools/test_microphone.py` | 真实模型协议、尾音、轻声、坏包恢复、175 秒固定音频连续会话；不访问麦克风 |
| `tools/evaluate_sensevoice.py` | 默认旧 174 条，仅作辅助回归；`--manifest` 指定新语料，`--output` 指定新结果；不覆盖已有结果，记录音频/模型/源码哈希 |
| `tools/prepare_ascend_test.py` | 从已校验的 ASCEND 测试集去旧题重合，生成原速/变速/低音量/噪声条件；测试集禁止用于训练 |
| `tools/test_evaluation.py` | 新评测的评分、结果防覆盖、音频校验与压力变换；无需模型 |
| `tools/test_desktop_window.py` | 实际交互桌面的窗口可见性与表面绘制，须已有可见 VoxFix 窗口 |

原生测试用 `Start-Process` 等待退出，不能在 PowerShell 直接把带引号路径当命令。本机若环境里同时存在
`HTTPS_PROXY` 与 `https_proxy`（Windows 环境变量不区分大小写），`Start-Process` 会报「字典中的关键字重复」，
此时改用管道调用：`$out = & '.\bin\VoxFix Desktop.exe' --dictionary-test 2>&1 | Out-String`，
管道会等进程退出，`DICTIONARY-TEST` / `TIDY-TEST` 也能直接落盘。

```powershell
$p = Start-Process '.\bin\VoxFix Desktop.exe' -ArgumentList '--self-test' -Wait -PassThru
$p.ExitCode
Get-Content work\microphone-self-test.txt
```

| 参数 | 覆盖 / `work/` 结果文件 |
|---|---|
| `--self-test` | 界面、送字规则、形变、音效；`microphone-self-test.txt` |
| `--hotkey-test` | 真实快捷键拦截，不录音；1.6.32.0 起 Ctrl+C 不再被吞，会真实落到当前前台应用（程序自身不读写剪贴板）；`microphone-hotkey-test.txt` |
| `--hotkey-cycle-test` | 开始、暂停、重开、Esc；短暂采音但不向其他软件送字；`microphone-hotkey-cycle-test.txt` |
| `--focus-test` / `--idle-focus-test` | 真实窗口焦点、送字和空闲形变；`microphone-focus-test.txt` / `microphone-idle-shape-test.txt` |
| `--capture-test` | 默认麦克风三次重开与尾缓冲；`VoxFix Desktop-capture-test.txt` |
| `--smoke-test` | 真实模型启动与缓存换轮；`work/microphone-ui-test.txt` / `work/microphone-round-reset-test.txt` |
| `--stream-test` / `--memory-test` | 真实模型、专用输入框送字与缓存兜底；`VoxFix Desktop-stream-test.txt` / `microphone-memory-test.txt` |
| `--target-probe [进程名]` | 只读诊断，不抢前台；`target-probe.txt`。仅明确要求时才加会送字的 `--type` |
| `--dictionary-capture-test` | 真实默认麦克风三次采样与重开、尾缓冲及释放；不保存原音，`dictionary-capture-test.txt` |
| `--dictionary-test` | 离屏渲染词典编辑器（窗口放在桌面范围外）并写 `dictionary-dialog.png`；同时把当前词典写进 `dictionary-roundtrip.json` 供 Python 侧解析校验 |

原生测试在真实交互桌面**串行运行**；同时检查退出码为 0、结果文件更新时间属于本次运行且内容为 PASS。
旧 `VoxFix Desktop-test-failure.txt` 或旧 PASS 均不能代表本轮结果。真实键鼠操作会干扰焦点和保护机制，
尤其 `--memory-test` 会移动鼠标；受干扰失败应单独复测，不能直接判成产品回归。
完成构建和验证后可用 `--background` 恢复后台待命，不自动录音。

## 行为契约

录音采样入口自 1.6.46.0 起位于词典「录音纠音」页，属于本地样本采集，不是自动训练。每词可有多段音频，各段保存完整 `correctText`；元数据 `state` 固定为 `collected`，不得宣称模型已学会。私有目录为 `user-data/voice-samples/`，音频和索引均不得入库；新 WAV 先完整落盘，再原子提交独立 JSON。删除词条保留录音；删除样本将索引改名 `.json.deleted`，保留可恢复文件。新旧录音不得静默覆盖。录音最长 60 秒，停止排空尾缓冲；录制期间锁定词语/标签，切词或关闭前处理未保存录音，失败保留。采样前结束普通听写，采样期间普通录音不得抢占麦克风；关闭/取消/异常必须释放 Recorder、定时器与播放器。UI 采用文字/录音双页、单行图标搜索和统一矢量图标，保持 926×680 无边框与唯一说明图标。改此功能须跑离屏 `--dictionary-test` 和真实设备 `--dictionary-capture-test`（不保存麦克风原音）。

1. 草稿识别到文字即送字，并允许修订本轮在同一输入位置写入的当前段；已完成段落与用户原文不回改。手动编辑、焦点变化或送字不完整后停止修订，最终结果或未送出后缀进缓存。分段身份、最长公共前缀、退格范围与送字保护须一起验证。
2. `punctuation_only()` 同时保护词元及原有标点，标点模型只能添加；验证失败返回原文。
   守卫通过后由 `drop_inserted_punctuation()` 丢弃"模型插入且紧邻已有标点"的字符（识别结果自带标点，
   否则会出现 `。。`、`，，`），只删模型新增字符，原文标点、小数点与有意义的连续标点保留。
3. ITN 关闭，否则金额可能被错误改写；不得按评测词句硬编码替换。
4. SenseVoice 使用 `language='auto'`，不全局锁中英；锁中文会损坏英文整句，锁英文可能使中文全空。
5. 草稿中的中英以外字符不展示；定稿出现则拒绝并报错，保留先前定稿供恢复。不删除外文字符掩盖错误，不承诺自动重解。
6. 送字准入三态：`Editable` 和 `Unknown` 送字，明确的 `ReadOnly` 进缓存。Chromium 等优先读取 MSAA 实际焦点，不能仅凭窗口外壳决定只读。
7. UI 线程不做 UIA/MSAA，生产判定走 `ClassifySafe`：工作线程 + 300ms 熔断 + `stalledWindows`。
8. 无障碍焦点可能滞后，同一窗口/焦点组合最多重试 3 次；粗粒度宿主继续复查内部焦点。
9. 成功开始新轮才清旧缓存；停止、收起、启动失败保留，退出清空。离框音频只入缓存，回框只送新话。
10. 热词复解只作用于定稿，且只在文本出现词表词近音片段时运行；近音检测只比整词与相邻两词（避免英文子串误触发），
    接受条件为"连写命中（`strong_hits`）不减少且确有增加、或连写不变但压缩命中增加"，长度不短于原文 60%，
    任何异常一律保留 sherpa 原文。`hotwords` 未配置时该路径必须完全不创建会话。改这些口径必须重跑
    `work/hotword-prototype/p2_prod_eval.py`（生产路径 174 与封存 1,314）。
11. Win+H 开始/暂停，Esc 收起；不恢复浮动小字提示。UI 几何、材质和动画改动必须验证实际桌面。
12. 复制未上屏缓存只能由胶囊右侧按钮触发；键盘钩子不得捕获 Ctrl+C（1.6.32.0 起），无缓存、有缓存、复制前后都一样交给原应用。
13. 右键菜单固定为三条命令加一条分隔线：收起 / 词典 / ─ / 退出，白色卡片、每行一个矢量图标、行宽按文字自适应（不许留右侧空白）。
    「整理」不挂在菜单上、窗口也不放按钮：它是词典窗口「容易识别成」框内的能力——统一拦截原生粘贴入口，整理本次粘贴，支持一次 Ctrl+Z 撤销。
    按换行/制表与 `。！？；，、,;|｜` 拆行并忽略大小写去重；普通/全角空格不拆，配对引号或括号保护内部标点，保留 `.NET`/`C++` 等符号。
    保存只按已有行清理去重，不能再次按标点拆分；跨词错法冲突、读取失败或外部文件已修改时阻止覆盖，保存失败不能关窗。
    窗口为暖白双栏、细滚动条，不绘制虚线焦点框；标题旁圆圈感叹号是唯一新增悬停说明（兼容键盘聚焦），不恢复悬浮窗提示。
    标签测量与绘制必须使用相同的字体渲染方式，说明图标跟随标题实际宽度定位并透明绘制；主窗/删除确认窗保留无边框样式，仅请求 DWM 平滑圆角。
    不得为圆角添加 WS_THICKFRAME/WS_CAPTION、扩展原生边框或改写 WM_NCCALCSIZE；必须重放真实 ShowDialog 返回后的激活/重绘，并断言无系统边框、客户区 926×680。
    删除先显示同风格确认窗；确认后提供当前窗口内 10 秒撤回条，按单调时钟判定超时，连续删除分别保留；撤回须完整恢复词语、错法、context 和顺序，仍须保存生效。
    改这里必须跑 `--dictionary-test`，输出 `TIDY-TEST PASS` 一行并检查本轮 `work/dictionary-test.txt` 为 PASS。`词典` 打开 `DictionaryDialog`，保存必须用 `File.Replace` 原子写
    `dictionary.json`（Python 侧按 mtime 重载），`hotwords` 写全部词、`rules` 只写有错法的词，
    每条规则的 `context` 沿用文件里的原值（缺省则继续不写），不能一律改成 `zh`；新建规则默认不写 `context`（任意语言生效），已有规则不得静默放开；
    搜索按词语/错法忽略大小写过滤，隐藏当前条目时保留右侧编辑，删除/撤回走真实索引。预览使用当前未保存内容，独立进程调用共享规则，2 秒超时，失败不阻塞保存；关闭释放进程，过时结果不展示。新建规则、搜索、预览和 80 词提示须纳入离屏自检。
    有未保存修改时关闭要先问。改这个窗口必须跑 `--dictionary-test`（离屏截图 + 往返 JSON 校验），不要弹到桌面。
14. 悬浮窗是 `WS_EX_NOACTIVATE`，收不到失焦消息，菜单不能指望 WinForms 自动关闭：`ContextMenuStrip` 打开期间必须挂 `MenuDismissHook`（WH_MOUSE_LL），
    点窗口外任意位置关闭；选任一项、Esc、收起、退出、隐藏窗口、销毁前也必须关闭并释放钩子。改这块要跑 `--self-test` 的菜单段（含钩子回调重放）。

## 文件、数据与历史

- 模型、`runtime/` 二进制、`work/` 实验数据、`outputs/` 训练输出、`user-data/` 私有样本、`bin/`、`.venv/` 不入库；`work/sensevoice-20261021/assets.json` 例外，记录当前生产资产；换模型后按本机文件核对并更新，与 `models/sensevoice/UPSTREAM.md` 和启用记录对齐。
- 不删除模型、语料、窗口位置、个人数据或未知来源的本地实验文件。旧题库在本机 `work/p0/`，
  新真人基准与微调语料在仓库外 `F:/VoxFixTrain/`（可离线，拔盘不影响产品识别）。`D:/VoxFixEval/` 已于 2026-10-22 删除。
- 当前不使用旧 Nemotron/Qwen 探针。34 个历史脚本从工作树移除，在 Git `5209268` 中保留；按工具索引恢复到独立实验目录，勿冒充当前回归。
- 不假定 HuggingFace 可直连；本机此前通过 ModelScope 获取权重，下载后按来源和校验值核验。
- 保留既有未提交工作，提交前检查范围；只提交代码、文档、许可和必要界面资源，不提交凭据或私有转写数据。

## 文档

新记录用 `docs/主题-YYYY-MM-DD.md`，文首标版本、日期、状态，结论指向脚本与结果；
在 `docs/README.md` 登记为“现行”或“历史”。结论过时改状态，保留原始记录。
面向使用者的行为变化同步 `README.md`、`docs/使用说明.md` 和版本号；维护规则变化同步本文件。
