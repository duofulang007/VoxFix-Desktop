# 工具索引

版本 1.6.40.0；日期 2026-10-23。生产只运行 `app/` 中的两个入口，以下均为维护工具。
Python 脚本使用项目 `.venv/Scripts/python.exe`，原生测试参数与结果文件见 [AGENTS.md](../AGENTS.md)。

## 现行测试

| 文件 | 用途 |
|---|---|
| `test_sensevoice.py` | 无模型的协议、分段、草稿、尾音和标点契约 |
| `test_language_guard.py` | 外文结果拒绝与先前定稿保留 |
| `test_hotwords.py` | 近音触发、接受守卫、失败回退、会话释放与管线接线，无需模型 |
| `test_asr_scoring.py` | 共享评分函数的边界回归 |
| `test_microphone.py` | 真实模型、固定音频与 175 秒连续会话，不访问麦克风 |
| `evaluate_sensevoice.py` | 默认旧 174 条；支持 `--manifest`、`--output`，重新推理并记录音频/模型/源码哈希、分组错误率；结果不覆盖 |
| `prepare_ascend_test.py` | 已校验的 ASCEND 真人测试集去旧题重合，生成原速、变速、低音量和噪声条件；测试集禁止用于训练 |
| `test_evaluation.py` | 评分汇总、结果防覆盖、音频校验、衰减和可复现信噪比检查，无需模型 |
| `test_desktop_window.py` | 实际桌面的窗口、位置和绘制；`--exercise-reopen` 另验证普通二次启动 |

真实模型测试依赖本机权重、运行库及清单对应的 WAV。旧题库位于 `work/p0/manifest.json`；
评测默认在 `work/sensevoice-20261021/` 下写带时间和随机编号的新结果，指定 `--output` 也不覆盖已有结果。
逐条结果先写同名 `.jsonl`，完成后写 `.json`；中途失败的日志不能算完整通过。
语料及结果不入库，历史分数不能代替当前测试。

新真人基准、来源及运行方式见 [真人新语料基准与训练准备](../docs/真人新语料基准与训练准备-2026-10-22.md)。
微调实验（两轮）的脚本在仓库外 `F:/VoxFixTrain/scripts/`，语料与产物同在该根下；
训练环境留在 `D:/VoxFixTrain/venv/`，脚本另在 `work/training-scripts/` 冻存一份。
复现命令与产物清单见 [多源真人语料微调 SenseVoice](../docs/多源真人语料微调SenseVoice-2026-10-22.md) 第 11 节。

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
