# VoxFix Desktop

Windows x64 本地语音输入，替代 Win+H。当前版本 **1.6.24.0**。
使用 SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 本地推理；识别与送字不联网。

## 使用

- **Win+H** 呼出并录音，再按暂停，窗口保留；继续按可恢复。**Esc** 停止并收起，后台仍待命。
- 点击麦克风开始或结束；胶囊状态长按约 0.4 秒收尾并开启新轮。收尾期间按 Win+H 会排队重开，Esc 取消待开启请求。
- **圆形**表示当前目标可直接输入，**胶囊**表示当前不会直接输入。空闲且有缓存时，胶囊用于复制；无缓存时随焦点切换形状。
- 无输入框或只读目标时继续识别，文字暂存内存；胶囊右侧点击复制。转写完成前不能复制。
- 窗口显示且有未复制的已定稿缓存时，**Ctrl+C** 复制一次，随后恢复普通复制；新增缓存后再开放一次。
- 成功开始新轮才清空旧缓存；停止、收起和启动失败保留缓存，退出清空。错误态左侧重试，右侧复制。
- 拖动可移动并记住位置；右键菜单只有“收起”和“退出”。退出后恢复系统 Win+H。
- 已输入文字只追加，永不回删；标点只允许添加，删除或替换已有标点时保留识别原文。每轮最长三分钟。

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
