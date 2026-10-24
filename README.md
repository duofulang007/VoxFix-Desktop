# VoxFix Desktop

Windows x64 本地语音输入，替代 Win+H。当前版本 **1.6.47.0**。
使用 SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 本地推理；识别与送字不联网。

词典支持文字纠正、搜索、未保存内容预览，以及同词多段录音与完整文本标签。
录音样本只在本机收集，**不会自动训练或立即改变识别效果**；操作见[使用说明](docs/使用说明.md)。

当前生产模型为 R5 INT8。本机权重哈希与[R5 启用记录](docs/R5模型启用-2026-10-23.md)一致。
6257 条 CS-Dialogue 真人封存比较采用 PyTorch 检查点口径，中文、英文和混说均优于 R4；
生产 INT8 已过固定音频、启动与送字自检，完整 INT8 独立集和日常麦克风验收仍未完成。
旧 174 条仅作辅助回归；历次变更与证据见[技术记录索引](docs/README.md)。

## 使用

- **Win+H** 呼出并录音，再按暂停，窗口保留；继续按可恢复。**Esc** 停止并收起，后台仍待命。
- 点击麦克风开始或结束；胶囊状态长按约 0.4 秒收尾并开启新轮。收尾期间按 Win+H 会排队重开，Esc 取消待开启请求。
- 开始和收尾保持当前输入目标对应的外形；不闪切处理声纹，准备期间不闪出复制图标，监听波环连续。
- 圆形、胶囊及中间外形使用矢量轮廓与四倍超采样抗锯齿，保持原有显示尺寸和材质。
- **圆形**表示当前目标可直接输入，**胶囊**表示当前不会直接输入。空闲且有缓存时，胶囊用于复制；无缓存时随焦点切换形状。
- 无输入框或只读目标时继续识别，文字暂存内存；胶囊右侧点击复制。转写完成前不能复制。
- 未上屏缓存只能点胶囊右侧“复制/已复制”取走，可重复复制；**Ctrl+C 不再被接管**，任何状态下都交给当前应用。
- 成功开始新轮才清空旧缓存；停止、收起和启动失败保留缓存，退出清空。错误态左侧重试，右侧复制。
- 拖动可移动并记住位置；右键菜单为“收起 / 词典 / 退出”。点窗口外任意位置、选任一项、按 Esc 或退出都会关闭菜单，不会一直悬在桌面上。退出后恢复系统 Win+H。
- 识别草稿一有文字就尝试送进当前输入框，不等停顿；继续说话时会修订当前这一段，停顿后补齐最终文字和标点。只修订本轮在原输入位置写入的当前段；手动编辑或切换焦点后停止修订，未送出结果留在缓存。
- 已完成段落不再回改，当前草稿可修订；标点只允许添加，删除或替换已有标点时保留识别原文。识别结果本身已带标点，标点模型不会再叠一个（不再出现 `。。`、`，，`）。每轮最长三分钟。
- **个人词典**：根目录 `dictionary.json` 是你指定的“错法 → 正确词”，当前词表统一在独念、纯英文和混说时生效，包括 `get up → GitHub`、`Jason → JSON`、`soul → sol`。这些是个人纠正偏好，即使原写法是正常英文也会替换；保存即生效，文件损坏时自动停用不影响识别。
- **热词偏置**：同一文件的 `hotwords` 列表写词语（按文件顺序取前 64 个，窗口会提示超额），定稿里出现读音接近的片段时复解该段，
  救不回来就保留原文；不写这个字段则不启用救援。首次触发会多约 1 秒（进程内一次性）。

Electron、Flutter 等应用优先读取实际焦点；旧接口只读判定会用 UIA 补查，仅当前窗口内确认可编辑的输入框可覆盖该判定。只读预览进入缓存，无法判断的输入目标仍兼容送字。
交互、材质与状态的完整说明见[使用说明](docs/使用说明.md)。

词典区分拆字缩写：`A P P → APP`、`A P I → API`；单独 `A P` 保留原文，避免猜测缺失的字母。
词典内容修订 **2026.10.24.1**：统一个人规则的适用语言，新增三种完整写法；程序仍为 1.6.47.0，无需重建或重启。验证与已知取舍见[个人词典统一规则](docs/个人词典统一规则-2026-10-24.md)。

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
当前资产校验值见 [assets.json](work/sensevoice-20261021/assets.json)。

## 验证

```powershell
.\.venv\Scripts\python.exe tools/test_sensevoice.py
.\.venv\Scripts\python.exe tools/test_language_guard.py
.\.venv\Scripts\python.exe tools/test_dictionary.py
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
