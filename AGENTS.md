# AGENTS.md

给在本仓库里动手的 AI / 协作者看的操作手册。产品说明见 [README](README.md)，技术记录的阅读顺序见
[docs/README.md](docs/README.md)。

## 这是什么

Windows x64 本地语音输入（替代 Win+H）。两个可执行单元：

- `app/Microphone.cs` —— 单文件 C#（WinForms，无 NuGet、无 .NET SDK），负责悬浮窗口、采音、送字、自检。
- `app/recognize.py` —— 子进程，stdin/stdout 逐行 JSON。负责 VAD 分段、草稿、标点、定稿。

首遍识别固定 SenseVoiceSmall INT8 + Silero VAD + CT-Transformer，CPU 推理。换模型是产品决策，
不是随手优化：`docs/中文与混说-换Qwen伪流式-2026-10-21.md` 里 Qwen3-ASR 准确率更高但被否，
别只看到准确率就换回去。

## 构建与验证

```powershell
# 编译（必须用 PowerShell 工具执行；Bash 里直调 csc.exe 会被安全策略拦）
Set-Location "D:\Projects\VoxFix Desktop"; .\build.ps1

# 编译前若应用在跑，先结束它，否则 csc 报 CS0016 文件被占用
Get-Process | Where-Object { $_.ProcessName -like 'VoxFix Desktop*' } | Stop-Process -Force
```

验证顺序（改动越大越要往全跑）：

| 命令 | 覆盖 |
|---|---|
| `tools/test_sensevoice.py` | 协议、分段、标点校验、语言守卫，无设备无需模型 |
| `tools/test_language_guard.py` | 外文输出守卫 |
| `tools/test_microphone.py` | 真实模型 + 175 秒连续会话 + 麦克风节奏 |
| `tools/evaluate_sensevoice.py` | 174 条固定语料，出分数 |
| `"bin\VoxFix Desktop.exe" --self-test` | 界面/送字/形变/音效自检，结果写 `work/microphone-self-test.txt`，末行须 `PASS:` |
| `--focus-test` / `--idle-focus-test` | 真实 WinForms + 真实 SendInput 的焦点与空闲形变回归 |
| `--target-probe [进程名]` | 某窗口能否收字，写 `work/target-probe.txt`，不抢前台 |
| `--memory-test` / `--stream-test` | 真实模型兜底与真实送字 |

`--memory-test` 会移动真实鼠标、依赖前台，用户正在用电脑时会以 `pressed=False; dragged=True`
失败，这是环境性失败，不是回归。

## 行为契约（改了就是 bug）

1. **只追加**：已经上屏的文字永不回改、回删。
2. **标点只允许加标点**：`punctuation_only()` 校验不过就返回原文。
3. **ITN 关闭**：开了会把「一百二十八元五角」写成「128元5元角」。
4. **语言不锁**：`language='auto'` 是实测最优；锁 `zh-CN` 会打废英文整句，锁 `en-US` 中文全空。
   运行时只认单个精确语言码，不接受 `multi`/`auto`/逗号组合。
5. **外文守卫**：首段出现中英之外字符，按 `zh-CN`/`en-US` 重解或拒收；上屏后再出现即停止输入。
6. **送字准入是三态**，不是二值门：`Editable` 送、`Unknown` 送、只读 `ReadOnly` 进缓存。
   别退回「UIA 判不了就整段缓存」。
7. **UI 线程不做 UIA**：判定必须走 `ClassifySafe`（工作线程 + 熔断），否则 Chromium provider 会让调用挂死。
8. **焦点判定要重试**：UIA 的无障碍焦点滞后一两帧，同一对 (窗口, 焦点) 最多重试 3 次再锁定。

## 环境坑（本机实测，别重复踩）

- Bash 工具里 `csc.exe` / `Add-Type` 被拦；`powershell -File build.ps1` 可以。
- 对 Chromium（`Chrome_WidgetWin_*`）调 UIA `FindAll` / 读 `FocusedElement`，在独立进程里可能**永不返回**
  （同样的查询在 PowerShell 里 6ms）。只能靠 300ms 熔断 + `stalledWindows` 表绕开。
- 模型权重、`runtime/` 二进制、`work/` 实验数据、`bin/`、`.venv/` 都不入库，`.gitignore` 已覆盖。
  评测资产在仓库外 `D:/VoxFixEval/`。
- HuggingFace 直连不通，`ModelScope` 是唯一高速源。
- `.tmp/` 已清空（原本 3.1GB 的 sherpa 源码构建、pip 构建工具、`hw-tokenizer`、ffmpeg 二进制）。
  历史探针 `recognition_probe.py` / `segment_probe.py` / `qwen_prefix_probe.py` / `video_asr_probe.py`
  依赖 `.tmp/video-tools` 的 ffmpeg，`sherpa_context_probe.py` 依赖 `.tmp/hw-tokenizer`，重跑前要重新装。
  `bin/` 里也不再保留 before-fix 备份 exe，需要回滚就用 git。

## 文档

- 新记录落 `docs/主题-YYYY-MM-DD.md`，文首写清版本号与日期，结论要能指到 `tools/` 脚本和 `work/` 数据。
- 写完回 `docs/README.md` 登记，标明「现行」或「历史」；结论被推翻就改状态，别删原文。
- 面向使用者的行为变化同步 `docs/使用说明.md` 与 `README.md`（版本号、检查命令）。
