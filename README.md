# VoxFix Desktop

Windows x64 本地语音输入，替代 Win+H。当前版本 **1.6.80.0**。
使用 SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 本地推理；识别与送字不联网。

词典支持文字纠正、搜索、未保存内容预览，以及集中审核录音与完整文本标签。
录音样本只在本机收集，**不会自动训练或立即改变识别效果**；操作见[使用说明](docs/使用说明.md)。

当前生产模型为 R5 INT8。本机权重哈希与[R5 启用记录](docs/R5模型启用-2026-10-23.md)一致。
1.6.80.0 稳定性检查修复逐字追加中途切为只读时的漏字记账：原生编辑框变只读后立即停止追加，剩余定稿保留缓存。见[稳定性检查](docs/逐字出字稳定性检查-2026-10-28.md)。

1.6.79.0 参考手机微信录屏的逐字效果：草稿接续出现，新结果接替旧候选，积压时加快追赶；兼容追加通道在短段定稿后逐字送出。原位纠错和手动编辑保护继续保留，系统关闭动画时直接显示。见[逐字出字记录](docs/逐字出字节奏-2026-10-28.md)。

1.6.78.0 修复修饰键松开事件丢失后 Win+H 漏给系统、可能同时出现两个语音窗口的问题；见[快捷键修复](docs/WinH修饰键残留修复-2026-10-28.md)。
1.6.77.0 支持先框选文字，再说话替换选区，保留两侧原文；见[框选语音替换](docs/框选文字语音替换-2026-10-27.md)。1.6.76.0 在兼容输入框中快速显示草稿，识别自动纠错时原位替换错字、保留正确后文，光标留在最新语音文字的末尾。不再退格删除后文再重打。不支持直接修改的输入框按短段定稿追加；保留长录音后台接续。见[语音原位纠错](docs/语音原位纠错-2026-10-27.md)。1.6.74–1.6.75的呈现方案保留为历史记录。
1.6.72.0 将开机入口改为当前用户登录计划任务，失败自动重试；见[登录启动修复](docs/登录启动修复-2026-10-26.md)。

1.6.71.0 修复放弃编辑后残留改字和训练勾选、验证刷新清空备注，以及归档期间重复操作；见[交互修复记录](docs/交互状态保护修复-2026-10-25.md)。
6257 条 CS-Dialogue 真人封存比较采用 PyTorch 检查点口径，中文、英文和混说均优于 R4；
生产 INT8 已过固定音频、启动与送字自检，完整 INT8 独立集和日常麦克风验收仍未完成。
旧 174 条仅作辅助回归；历次变更与证据见[技术记录索引](docs/README.md)。

词典分为「词语 / 听写记录」，主动采样入口为听写记录的「补录一句」，无需先建词条。

「听写记录」分为 **录音 / 训练 / 验证**。录音单击选中，双击、Enter或「打开录音」进入纠错，直接勾选「留作训练」并保存；训练页在「待归档」填写批次名并二次勾选归档，「已归档」查看快照，已有批次可随时查看。训练交给 agent；完成后在验证页录新音，对照原模型和候选，选择通过或打回。通过后仍由 agent 校验、正式启用和准备回滚。见[使用说明书](docs/录音纠错使用说明书.html)和[交接协议](docs/训练交接与验证协议.md)。

微信中文/标点丢字已在 1.6.51.0 修复：微信 Qt 输入框沿用专用字符消息通道，当前采用短段定稿追加；兼容文本接口的输入框走原位纠错。不操作剪贴板，不自动发送消息。见[修复记录](docs/微信输入丢字与标点重复-2026-10-24.md)。

## 使用

- **Win+H** 呼出并录音，再按暂停，窗口保留；继续按可恢复。**Esc** 停止并收起，后台仍待命。
- 点击麦克风开始或结束；胶囊状态长按约 0.4 秒收尾并开启新轮。收尾期间按 Win+H 会排队重开，Esc 取消待开启请求。
- 开始和收尾保持当前输入目标对应的外形；不闪切处理声纹，准备期间不闪出复制图标，监听波环连续。
- 圆形、胶囊及中间外形使用矢量轮廓与四倍超采样抗锯齿，保持原有显示尺寸和材质。
- **圆形**表示当前目标可直接输入，**胶囊**表示当前不会直接输入。空闲且有缓存时，胶囊用于复制；无缓存时随焦点切换形状。
- 无输入框或只读目标时继续识别，文字暂存内存；胶囊右侧点击复制。转写完成前不能复制。
- 未上屏缓存只能点胶囊右侧“复制/已复制”取走，可重复复制；**Ctrl+C 不再被接管**，任何状态下都交给当前应用。
- 成功开始新轮才清空旧缓存；停止、收起和启动失败保留缓存，退出清空。错误态左侧重试，右侧复制。
- 拖动可移动并记住位置；右键菜单为“收起 / 词典 / 退出”。点窗口外任意位置、选任一项、按 Esc 或退出都会关闭菜单，不会一直悬在桌面上。启用开机入口后，“退出”关闭主程序与模型，轻量入口继续接管 Win+H，下次按键重新启动 VoxFix；撤销入口并退出主程序才恢复系统 Win+H。
- 兼容文本接口的输入框一有草稿就开始逐字送出，不等短句定稿；新候选接续当前进度，不重复播放旧候选。识别改正错字时直接替换当前语音文字（1.6.76.0 起原位替换，不再有“只改末尾 8 个字符”的限制），已完成段落与正确后文都不动，不发送左右移动或 Delete 键。先框选再说话可用新语音替换该选区。
- 手动编辑或切换焦点后暂停送字，未送结果留在缓存；重新确认输入框后只输入后续新话。停止录音时收齐尾音和最后一段，取消不补打迟到结果。
- 标点只允许添加，识别结果已有标点不重复叠加。连续录音内部按三分钟分轮释放音频缓冲，不结束采音、不清空正文或缓存；分轮不代表整场音频永久保存在内存。
- **个人词典**：根目录 `dictionary.json` 是你指定的“错法 → 正确词”，当前词表统一在独念、纯英文和混说时生效，包括 `get up → GitHub`、`Jason → JSON`、`soul → sol`。这些是个人纠正偏好，即使原写法是正常英文也会替换；保存即生效，文件损坏时自动停用不影响识别。
- **热词偏置**：同一文件的 `hotwords` 列表写词语（按文件顺序取前 64 个，窗口会提示超额），定稿里出现读音接近的片段时复解该段，
  救不回来就保留原文；不写这个字段则不启用救援。首次触发会多约 1 秒（进程内一次性）。

Electron、Flutter 等应用优先读取实际焦点；旧接口只读判定会用 UIA 补查，仅当前窗口内确认可编辑的输入框可覆盖该判定。只读预览进入缓存，无法判断的输入目标仍兼容送字。
交互、材质与状态的完整说明见[使用说明](docs/使用说明.md)。

词典区分拆字缩写：`A P P → APP`、`A P I → API`；单独 `A P` 保留原文，避免猜测缺失的字母。

## 本地运行

需要 Windows x64、Python 3.13 和麦克风。在项目目录执行：

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install numpy
.\.venv\Scripts\python.exe -m pip install --target runtime/sherpa-onnx-1.13.8 sherpa-onnx==1.13.8
```

按来源说明准备三个权重：

| 模型 | 文件与来源 |
|---|---|
| SenseVoiceSmall INT8 | `models/sensevoice/model.int8.onnx`，[来源](models/sensevoice/UPSTREAM.md)，词表已包含 |
| Silero VAD | `models/vad/silero_vad.onnx`，[来源](models/vad/UPSTREAM.md) |
| CT-Transformer INT8 | `models/punctuation/model.int8.onnx`，[来源](models/punctuation/UPSTREAM.md) |

```powershell
# 重建前先退出 VoxFix 主程序和快捷键入口，避免 EXE 被占用。
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
Start-Process '.\bin\VoxFix Desktop.exe' -ArgumentList '--hotkey-host' -WindowStyle Hidden
# 当前用户登录计划任务启动轻量 Win+H 入口并预热主程序，不录音；加 -Remove 撤销。
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install-shortcuts.ps1
```

构建使用系统自带 C# 编译器，以及已安装的 Visual Studio C++ Build Tools、Windows SDK 编译原位编辑组件；无需 .NET SDK 或生产 NuGet 依赖，同时生成项目根目录快捷方式。运行时保留 `bin/VoxFix.TextBridge.1.6.76.0.dll` 与 EXE 相邻。
1.6.69.0 的登录入口使用 `--hotkey-host`，主程序缺席时仍接住 Win+H 并重新启动；直接 `--background` 只启动主程序。重复后台启动不呼出或开始录音。没有卸载或永久禁用 Windows 语音组件；若入口与主程序都被结束，系统快捷键仍会恢复。见[启动与接管验证](docs/WinH后台接管与准确率复核-2026-10-25.md)。
`ExecutionPolicy Bypass` 仅作用于本次进程。模型权重、运行环境和个人实验数据不入库；
当前资产校验值见 [assets.json](work/sensevoice-20261021/assets.json)。

## 验证

```powershell
.\.venv\Scripts\python.exe tools/test_sensevoice.py
.\.venv\Scripts\python.exe tools/test_language_guard.py
.\.venv\Scripts\python.exe tools/test_dictionary.py
.\.venv\Scripts\python.exe tools/test_dictation_history.py
.\.venv\Scripts\python.exe tools/test_training_review.py
.\.venv\Scripts\python.exe tools/test_hotwords.py
.\.venv\Scripts\python.exe tools/test_asr_scoring.py
.\.venv\Scripts\python.exe tools/test_evaluation.py
.\.venv\Scripts\python.exe tools/test_microphone.py
Start-Process '.\bin\VoxFix Desktop.exe' -ArgumentList '--self-test' -Wait -PassThru | Select-Object ExitCode
```

真实模型测试需要本地模型和 `work/p0/` 评测语料；175 秒连续会话使用固定音频投递，不是真人麦克风准确率。
准确率评测必须明确指定语料：`tools/evaluate_sensevoice.py --manifest <语料清单.json> --output <新结果.json>`，不再默认运行旧 174 条。个人词典与模型采用应看有相关术语覆盖的当前样本，最终封存集不用于反复调规则。
原生测试须同时检查退出码和**本次新生成**的 PASS 文件，`--self-test` 写 `work/microphone-self-test.txt`。
焦点、快捷键、采音和送字测试入口见 [AGENTS.md](AGENTS.md) 与[工具索引](tools/README.md)。
键鼠操作可能干扰真实桌面测试，应逐项串行运行。

## 文件与资料

- `app/`：桌面与识别入口、共享词典规则、热词救援和内嵌界面资源。
- `tools/`：现行测试、共享评分、下载与音频转换工具；[索引](tools/README.md)。
- `models/`、`runtime/`：来源、许可及本地权重和运行库，二进制不入库。
- `work/`：本机测试结果、语料与窗口位置；除资产校验值外不入库。
- `user-data/`：私有录音与文本标签；`outputs/`：本机训练输出，两者均不入库。
- `docs/`：使用说明和带状态的技术记录；从 [docs/README.md](docs/README.md) 开始读。

旧 Nemotron/Qwen 实验脚本已从工作树移除，恢复方式见工具索引。历史技术记录保留，历史结论不代表当前行为。
中英混说、英文术语和粤语仍可能识别错误；真人口音、距离、噪声及跨软件长期输入仍需实际验收。
