# 工具索引

版本 1.6.80.0；日期 2026-10-28。生产模型为 R5 INT8；6257 条真人封存比较采用 PyTorch 口径，旧 174 条仅作辅助回归；生产 INT8 固定音频与原生送字自检通过。生产识别与词典预览入口位于 `app/`，以下均为维护工具。

`--hotkey-test` 新增左右 Shift/Ctrl/Alt 六种残留按下状态后真实 Win+H 恢复回归，核对 `work/microphone-hotkey-test.txt`；详见[快捷键修复](../docs/WinH修饰键残留修复-2026-10-28.md)。

`--hotkey-host-test` 使用真实键盘钩子和隔离原生窗口验证缺席启动、现存转发、退出恢复、登录不录音、启动合并/超时、失败重试和重复释放；核对 `work/hotkey-host-test.txt`。正常登录入口为 `--hotkey-host`，`--stop-hotkey-host` 单独退出入口，不终止主程序。

1.6.80.0 的 `--stream-test` 同时运行 FAST-DRAFT-TEST 和 TEXT-REVEAL-TEST：`work/microphone-fast-draft-test.txt`覆盖原位纠错、正确后文、标点去重、段尾光标、离框/取消、英文接词和换轮保护；`work/text-reveal-test.txt`及`work/text-reveal-frames.txt`覆盖逐步呈现、候选合并、定稿顺序、兼容通道逐字追加、中途只读时停止并保留准确的未送出后缀、手动编辑缓存、取消、Unicode文本元素和积压追赶。旧STABLE/PACING文件仅为历史；当前逐字调度见[维护契约](../AGENTS.md#16790-逐字出字)。
Python 脚本使用项目 `.venv/Scripts/python.exe`，原生测试参数与结果文件见 [AGENTS.md](../AGENTS.md)。

1.6.73.0差异修订已撤回，当前不再发送纠错导航/删除键。`--stream-test`还验证跨三分钟非整包尾部不丢失，正文、缓存、焦点、来源组和累计录音位置保留。固定音频和专用输入框不代表真实麦克风或全部宿主验收。

## 现行测试

| 文件 | 用途 |
|---|---|
| `test_text_bridge.ps1` | 隔离跨进程 EDIT/RichEdit 原位纠错、1000组差异用例、光标、只读和外部编辑保护；需已有 C++ Build Tools/Windows SDK |
| `test_text_bridge_browser.ps1` | 使用 work 中已获准下载且签名有效的 WebView2 SDK，验证隔离 textarea/contenteditable 实际 DOM 和逐帧光标；-ReplaceSelection覆盖框选替换并检查两侧文字；不下载、不安装，默认合成夹具，可用 -Events 指定私有识别轨迹 |
| `test_text_bridge_browser.cs` | 上述浏览器桥接测试的宿主源码，由 `test_text_bridge_browser.ps1` 编译到 `work/` 后运行；不单独执行，不随生产 EXE 分发 |
| `test_sensevoice.py` | 无模型的协议、分段、草稿、尾音和标点契约 |
| `test_language_guard.py` | 外文结果拒绝与先前定稿保留 |
| `test_dictionary.py` | 共享词典规则、个人词表全部变体在独念/英文/中文中纠正、个人英文覆盖与词边界、APP/API 区分、预览子进程、最长错法与 64 词边界；不加载模型 |
| `test_dictation_history.py` | 默认关闭、留档原音及原识别、人工选择默认值、容量和坏设置、生产接线、磁盘失败不影响识别；无模型/麦克风 |
| `test_training_review.py` | 23项候选来源、批次清单/原音/模型/报告变化、独立新音、失败保留、通过/打回及实际启用哈希门禁、通过后复测原音变化/不完整对照阻止记账、按所选复测重试；无模型/麦克风 |
| `test_hotwords.py` | 16项近音触发、接受守卫、失败回退、会话及定时器释放、失败解码器清理与管线接线，无需模型 |
| `test_asr_scoring.py` | 共享评分函数的边界回归 |
| `test_microphone.py` | 真实模型、固定音频与 175 秒连续会话，不访问麦克风 |
| `evaluate_sensevoice.py` | 必须用 `--manifest` 指定语料；`--output` 指定新结果，重新推理并记录音频/模型/源码哈希、分组错误率；结果不覆盖，不再默认旧 174 条 |
| `prepare_ascend_test.py` | 已校验的 ASCEND 真人测试集去旧题重合，生成原速、变速、低音量和噪声条件；测试集禁止用于训练 |
| `test_evaluation.py` | 评分汇总、结果防覆盖、音频校验、衰减和可复现信噪比检查，无需模型 |
| `test_desktop_window.py` | 实际桌面的窗口、位置和绘制；`--exercise-reopen` 另验证普通二次启动 |

真实模型测试依赖本机权重、运行库及清单对应的 WAV。旧题库位于 `work/p0/manifest.json`；
评测默认在 `work/sensevoice-20261021/` 下写带时间和随机编号的新结果，指定 `--output` 也不覆盖已有结果。
逐条结果先写同名 `.jsonl`，完成后写 `.json`；中途失败的日志不能算完整通过。
语料及结果不入库，历史分数不能代替当前测试。

新真人基准、来源及运行方式见 [真人新语料基准与训练准备](../docs/真人新语料基准与训练准备-2026-10-22.md)。
微调实验的脚本在仓库外 `F:/VoxFixTrain/scripts/`，语料与产物同在该根下；
训练环境留在 `D:/VoxFixTrain/venv/`，脚本另在 `work/training-scripts/` 冻存一份。
R5 来源与回滚见 [R5 模型启用](../docs/R5模型启用-2026-10-23.md)；早期两轮配方见[多源真人语料微调 SenseVoice](../docs/多源真人语料微调SenseVoice-2026-10-22.md)第 11 节。

## 支持工具

词典原生验证：`bin/VoxFix Desktop.exe --dictionary-test` 离屏检查文字/录音页、私有样本读写与原有词典契约；`--dictionary-capture-test` 离屏使用真实默认麦克风三次录制/重开，检查尾缓冲、标签锁定、释放与失败恢复，不保存麦克风音频。结果分别在 `work/dictionary-test.txt` 和 `work/dictionary-capture-test.txt`，须串行运行并核对本轮退出码与新 PASS。

| 文件 | 用途与边界 |
|---|---|
| `p0_score.py` | 共享的 `units`、`distance`、`exact`、`overall`，保留原模块名避免改动现行导入；已移除依赖安卓绝对路径的旧报表 CLI |
| `make_wav.py` | `.f32` 单声道 16kHz PCM 转 PCM16 WAV；参数见 `--help`，不会自动生成金标清单 |
| `fetch.py` | HF 镜像分块下载与 SHA-256 校验；参数见 `--help`，仅下载工具，非生产网络依赖 |
| `build_text_bridge.ps1` | 编译 `app/native/` 的原生原位编辑组件；DLL 名、消息窗口类和启动消息版本均从 `Microphone.cs` 的 AssemblyVersion 生成，已加载的同版本 DLL 无法覆盖，发布新版本须先升 AssemblyVersion |
| `MicrophoneDeviceProbe.cs` | 只读查询默认采音设备、静音和音量，不修改系统设置 |
| `install-shortcuts.ps1` | 设置普通用户登录计划任务启动 `--hotkey-host`，失败重试，校验后迁移同路径旧入口；`-Remove` 撤销并停止入口，仅用户要求时执行 |

## 历史实验

34 个旧 Nemotron、Qwen、上下文、热词和旧协议探针已从当前工作树删除，源码保留在
Git 提交 [`5209268`](https://github.com/duofulang007/VoxFix-Desktop/tree/52092685ca1f42effbc411d09cff66208a008583/tools)。
`docs/` 中的旧脚本名与命令是当时的证据索引，不是当前可直接执行的入口。

查看某个旧脚本：

```powershell
git show 5209268:tools/pseudo_batch.py
```

需要复现时，在独立实验目录恢复当时的完整调用链与生产源码；仅找回单个脚本不保证接口兼容。
旧视频探针的 ffmpeg、旧上下文探针的 tokenizer 依赖已清理，必须另行准备。
模型、语料、原始评测结果与历史技术记录保留；本机训练 `outputs/` 和私有 `user-data/` 均由忽略规则排除。

agent 交接服务：`app/training_review.py` 的 register/list/evaluate/deployed 命令见[交接协议](../docs/训练交接与验证协议.md)。它不启动训练或替换生产模型；原生离屏测试还覆盖 REVIEW-TEST 和验证页截图。

状态往返：原生HISTORY-TEST覆盖归档撤回/恢复；test_training_review.py覆盖通过/打回reopen、来源撤回暂停和历史保留。所有撤回测试用隔离夹具，不触碰真实个人批次。

`test_install_shortcuts.ps1`：19项内存模拟检查，覆盖登录任务、账户短名、旧项迁移、冲突保护、失败保留及移除；不写注册表或任务。系统任务对象构造仍需真实 Windows CIM 权限。
