# VoxFix Desktop

Windows x64 本地语音输入，替代 Win+H。当前版本 **1.6.31.0**。
使用 SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 本地推理；识别与送字不联网。
1.6.29.0 把识别权重换成本仓库 2026-10-22 用公开真人语料（ASCEND + AISHELL-1 + LibriSpeech）微调的版本；
1.6.30.0 新增**个人词典**：项目根目录 `dictionary.json` 人工纠正固定错法（如 GitHub→"get up"），
草稿与定稿都替换、保存即生效。
1.6.31.0 新增**热词偏置救援**：词典里写 `hotwords` 后，定稿出现"读起来像这个词"的片段时，
用偏置解码把这段音频复解一次，`pyython→python`、`gitthub→github` 这类错法不再靠逐条枚举。
来源、判据与回滚见 [记录](docs/多源真人语料微调SenseVoice-2026-10-22.md) 与
[热词偏置](docs/热词偏置-救援通道-2026-10-22.md)。

## 使用

- **Win+H** 呼出并录音，再按暂停，窗口保留；继续按可恢复。**Esc** 停止并收起，后台仍待命。
- 点击麦克风开始或结束；胶囊状态长按约 0.4 秒收尾并开启新轮。收尾期间按 Win+H 会排队重开，Esc 取消待开启请求。
- 开始和收尾保持当前输入目标对应的外形；不闪切处理声纹，准备期间不闪出复制图标，监听波环连续。
- 圆形、胶囊及中间外形使用矢量轮廓与四倍超采样抗锯齿，保持原有显示尺寸和材质。
- **圆形**表示当前目标可直接输入，**胶囊**表示当前不会直接输入。空闲且有缓存时，胶囊用于复制；无缓存时随焦点切换形状。
- 无输入框或只读目标时继续识别，文字暂存内存；胶囊右侧点击复制。转写完成前不能复制。
- 窗口显示且有未复制的已定稿缓存时，**Ctrl+C** 每轮成功复制一次，随后持续恢复普通复制；本轮新增缓存不再接管，成功开始新一轮才重置。
- 成功开始新轮才清空旧缓存；停止、收起和启动失败保留缓存，退出清空。错误态左侧重试，右侧复制。
- 拖动可移动并记住位置；右键菜单只有“收起”和“退出”。退出后恢复系统 Win+H。
- 识别草稿一有文字就尝试送进当前输入框，不等停顿；继续说话时会修订当前这一段，停顿后补齐最终文字和标点。只修订本轮在原输入位置写入的当前段；手动编辑或切换焦点后停止修订，未送出结果留在缓存。
- 已完成段落不再回改，当前草稿可修订；标点只允许添加，删除或替换已有标点时保留识别原文。每轮最长三分钟。
- **个人词典**：根目录 `dictionary.json` 里把识别的固定错法映射到正确写法（默认带 GitHub 一条，只在中英混说句生效），保存即生效，文件损坏时自动停用不影响识别。
- **热词偏置**：同一文件的 `hotwords` 列表写词语（默认 GitHub、Python、API、JSON、PDF、WiFi），定稿里出现读音接近的片段时复解该段，
  救不回来就保留原文；不写这个字段则行为与 1.6.30.0 完全一致。首次触发会多约 1 秒（进程内一次性）。

Electron、Flutter 等应用优先读取实际焦点，只读预览进入缓存，无法判断的输入目标仍兼容送字。
交互、材质与状态的完整说明见[使用说明](docs/使用说明.md)。

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
# 重建前先退出正在运行的 VoxFix，避免 EXE 被占用。
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
Start-Process '.\bin\VoxFix Desktop.exe'
# 可选：当前用户登录后隐藏待命；加 -Remove 撤销。
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\install-shortcuts.ps1
```

构建使用系统自带 C# 编译器，无需 .NET SDK 或 NuGet；同时生成项目根目录快捷方式。
`ExecutionPolicy Bypass` 仅作用于本次进程。模型权重、运行环境和个人实验数据不入库；
校验值见 [assets.json](work/sensevoice-20261021/assets.json)。

## 验证

```powershell
.\.venv\Scripts\python.exe tools/test_sensevoice.py
.\.venv\Scripts\python.exe tools/test_language_guard.py
.\.venv\Scripts\python.exe tools/test_asr_scoring.py
.\.venv\Scripts\python.exe tools/test_microphone.py
.\.venv\Scripts\python.exe tools/evaluate_sensevoice.py
Start-Process '.\bin\VoxFix Desktop.exe' -ArgumentList '--self-test' -Wait -PassThru | Select-Object ExitCode
```

真实模型测试需要本地模型和 `work/p0/` 评测语料；175 秒连续会话使用固定音频投递，不是真人麦克风准确率。
原生测试须同时检查退出码和**本次新生成**的 PASS 文件，`--self-test` 写 `work/microphone-self-test.txt`。
焦点、快捷键、采音和送字测试入口见 [AGENTS.md](AGENTS.md) 与[工具索引](tools/README.md)。
键鼠操作可能干扰真实桌面测试，应逐项串行运行。

## 文件与资料

- `app/`：两个生产入口和内嵌界面资源。
- `tools/`：现行测试、共享评分、下载与音频转换工具；[索引](tools/README.md)。
- `models/`、`runtime/`：来源、许可及本地权重和运行库，二进制不入库。
- `work/`：本机测试结果、语料与窗口位置；除资产校验值外不入库。
- `docs/`：使用说明和带状态的技术记录；从 [docs/README.md](docs/README.md) 开始读。

旧 Nemotron/Qwen 实验脚本已从工作树移除，恢复方式见工具索引。历史技术记录保留，历史结论不代表当前行为。
中英混说、英文术语和粤语仍可能识别错误；真人口音、距离、噪声及跨软件长期输入仍需实际验收。
