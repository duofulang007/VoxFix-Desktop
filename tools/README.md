# 工具索引

版本 1.6.24.0；日期 2026-10-21。生产只运行 `app/` 中的两个入口，以下均为维护工具。
Python 脚本使用项目 `.venv/Scripts/python.exe`，原生测试参数与结果文件见 [AGENTS.md](../AGENTS.md)。

## 现行测试

| 文件 | 用途 |
|---|---|
| `test_sensevoice.py` | 无模型的协议、分段、草稿、尾音和标点契约 |
| `test_language_guard.py` | 外文结果拒绝与先前定稿保留 |
| `test_asr_scoring.py` | 共享评分函数的边界回归 |
| `test_microphone.py` | 真实模型、固定音频与 175 秒连续会话，不访问麦克风 |
| `evaluate_sensevoice.py` | 固定 174 条语料、整段与生产分段对照 |
| `test_desktop_window.py` | 实际桌面的窗口、位置和绘制；`--exercise-reopen` 另验证普通二次启动 |

真实模型测试依赖本机权重、运行库、`work/p0/manifest.json` 和 WAV，结果写入
`work/sensevoice-20261021/`。语料及结果不入库，历史分数不能代替当前测试。

## 支持工具

| 文件 | 用途与边界 |
|---|---|
| `p0_score.py` | 共享的 `units`、`distance`、`exact`、`overall`，保留原模块名避免改动现行导入；已移除依赖安卓绝对路径的旧报表 CLI |
| `make_wav.py` | `.f32` 单声道 16kHz PCM 转 PCM16 WAV；参数见 `--help`，不会自动生成金标清单 |
| `fetch.py` | HF 镜像分块下载与 SHA-256 校验；参数见 `--help`，仅下载工具，非生产网络依赖 |
| `MicrophoneDeviceProbe.cs` | 只读查询默认采音设备、静音和音量，不修改系统设置 |
| `install-shortcuts.ps1` | 设置当前用户登录后 `--background`；`-Remove` 撤销，仅用户要求时执行 |

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
本轮未删除模型、语料、原始评测结果或历史技术记录。
