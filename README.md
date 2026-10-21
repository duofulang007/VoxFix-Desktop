# VoxFix Desktop

Windows x64 本地语音输入工具。使用 SenseVoiceSmall INT8、Silero VAD 和 CT-Transformer 标点，在 CPU 上运行，不调用云服务。

## 使用

- 点击悬浮麦克风开始或结束录音，停顿后把定稿输入当前输入框。
- 没有输入焦点时保留文字供手动复制；恢复焦点后只输入后续新内容。
- 支持拖动、右键菜单和长按开启新一轮录音，每次录音最多三分钟。
- 听写缓存只存在内存，退出后清空。
- 出错且有缓存时，点击胶囊左侧重试、右侧复制；重试不会清空缓存。

## 本地运行

需要 Windows x64、Python 3.13 和麦克风。

1. 创建环境：`py -3.13 -m venv .venv`。
2. 安装 NumPy：`.venv\Scripts\python.exe -m pip install numpy`。
3. 安装识别运行库：`.venv\Scripts\python.exe -m pip install --target runtime/sherpa-onnx-1.13.8 sherpa-onnx==1.13.8`。
4. 按来源说明准备模型：
   - [SenseVoiceSmall](models/sensevoice/UPSTREAM.md)：`models/sensevoice/model.int8.onnx`，词表已包含。
   - [Silero VAD](models/vad/UPSTREAM.md)：`models/vad/silero_vad.onnx`。
   - [CT-Transformer](models/punctuation/UPSTREAM.md)：`models/punctuation/model.int8.onnx`。
5. 运行 `./build.ps1`，打开生成的 `VoxFix Desktop.lnk` 或 `bin/VoxFix Desktop.exe`。

仓库不包含模型权重、Python 环境、运行库二进制和实验数据。模型校验值见 [assets.json](work/sensevoice-20261021/assets.json)。

## 检查

```powershell
.venv\Scripts\python.exe tools/test_sensevoice.py
.venv\Scripts\python.exe tools/test_language_guard.py
```

这是分段识别，持续讲话时定稿可能等到分段或结束录音后才输入。中英混说、英文及粤语仍可能出错。

详细交互见[使用说明](docs/使用说明.md)，技术记录见 [docs](docs)。历史实验脚本依赖本机实验数据，不代表当前产品接口。
