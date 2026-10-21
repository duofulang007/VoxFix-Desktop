# VoxFix Desktop

Windows x64 本地语音输入，目标是替代 Win+H：说完话直接落到当前光标处，不经过剪贴板、不联网。
当前版本 **1.6.5.0**，识别用 SenseVoiceSmall INT8 + Silero VAD 分段 + CT-Transformer 标点，CPU 上跑。

## 使用

- 圆 = 现在说话会直接进光标；椭圆 = 不会直接进（焦点不在输入框，或已有未上屏文字）。
  空闲时形状也随焦点变化，不必先录一句话试。
- 点击开始/结束录音；长按约 0.4 秒先收尾并清空上一轮缓存，再开新的一轮。
- 没有输入焦点时文字进「未上屏缓存」，胶囊右侧点一下即复制；缓存只在内存里，退出即清空。
- 出错且有缓存时，胶囊左侧重试、右侧复制，重试不清缓存。
- 每次录音最多三分钟，可拖动位置，右键有完整菜单。

Electron / Flutter / 自绘外壳等窗口（WorkBuddy、VS Code、Obsidian、飞书等）用可访问性描述不了光标，
这类窗口直接送字，不再整段进缓存；真正的只读文本框仍然进缓存。细节见
[Electron 输入框只走缓存](docs/Electron输入框只走缓存-原因与修复-2026-10-21.md)。

## 本地运行

需要 Windows x64、Python 3.13 和麦克风。

1. 创建环境：`py -3.13 -m venv .venv`。
2. 安装 NumPy：`.venv\Scripts\python.exe -m pip install numpy`。
3. 安装识别运行库：`.venv\Scripts\python.exe -m pip install --target runtime/sherpa-onnx-1.13.8 sherpa-onnx==1.13.8`。
4. 按来源说明准备模型：
   - [SenseVoiceSmall](models/sensevoice/UPSTREAM.md)：`models/sensevoice/model.int8.onnx`，词表已包含。
   - [Silero VAD](models/vad/UPSTREAM.md)：`models/vad/silero_vad.onnx`。
   - [CT-Transformer](models/punctuation/UPSTREAM.md)：`models/punctuation/model.int8.onnx`。
5. 运行 `./build.ps1`（调 `csc.exe`，无需 .NET SDK），打开生成的 `VoxFix Desktop.lnk` 或 `bin/VoxFix Desktop.exe`。

仓库不包含模型权重、Python 环境、运行库二进制和实验数据。模型校验值见
[assets.json](work/sensevoice-20261021/assets.json)。

## 检查

```powershell
.venv\Scripts\python.exe tools/test_sensevoice.py       # 协议与分段单元测试
.venv\Scripts\python.exe tools/test_language_guard.py   # 外文输出守卫
.venv\Scripts\python.exe tools/test_microphone.py       # 真实模型 + 175 秒连续会话
.venv\Scripts\python.exe tools/evaluate_sensevoice.py   # 174 条固定语料
"bin\VoxFix Desktop.exe" --self-test                     # 界面自检（无麦克风也能跑）
"bin\VoxFix Desktop.exe" --focus-test                    # 焦点判定回归
"bin\VoxFix Desktop.exe" --idle-focus-test               # 空闲形变
"bin\VoxFix Desktop.exe" --target-probe [进程名]         # 某窗口能否收字，写 work/target-probe.txt
```

`--self-test` 结果写 `work/microphone-self-test.txt`，最后一行是 `PASS: ...` 才算通过。

## 目录

| 路径 | 内容 |
|---|---|
| `app/Microphone.cs` | 全部窗口、采集、送字与自检逻辑（单文件 C#，WinForms）。 |
| `app/recognize.py` | 识别子进程：VAD 分段、草稿替换、标点校验、只追加定稿。 |
| `build.ps1` | 用系统自带 `csc.exe` 编译到 `bin/`，并重建快捷方式。 |
| `models/` | 三个 ONNX 权重（不入库）与各自来源说明。 |
| `tools/` | 测试脚本 + 历史实验探针。`test_*` 是现行测试，其余是当时的评测脚本。 |
| `work/` | 实验原始数据、截图、日志（不入库，只保留 `assets.json`）。 |
| `docs/` | 技术记录。**先看 [docs/README.md](docs/README.md)**，那里标了哪些结论仍然有效。 |

## 已知边界

中英混说、英文技术词和粤语仍可能错；`tools/` 里的历史探针依赖本机实验数据，不代表当前产品接口。
详细交互见[使用说明](docs/使用说明.md)，全部技术记录见 [docs 索引](docs/README.md)。
