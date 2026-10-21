# WorkBuddy 预览焦点修复

版本：1.6.14.0；日期：2026-10-21。

## 原因与修复

旧版看到 `Chrome_WidgetWin_*` 等粗粒度宿主就直接返回 `Unknown`，而 `Unknown` 允许送字。这把 WorkBuddy 的可编辑聊天框与只读文件预览视为同一目标。实际对照还发现，UIA `FocusedElement` 对二者均返回无焦点的页面文档，而 MSAA `IAccessible.accFocus` 能逐级取得实际焦点：聊天框 role=42、state=1048580；预览 role=15、state=1048644（只读）。

`app/Microphone.cs` 对粗粒度窗口沿 MSAA 的焦点链查询终端节点，不再只根据窗口类型放行。明确可写的文本目标送字，明确只读文档或非文本控件进缓存；无焦点、接口缺失、异常、超时、仅有窗口/Pane/Client 占位节点仍返回 `Unknown`，保持既有三态兼容规则。没有读取或记录输入框正文。

MSAA 使用系统自带 `Accessibility.dll`，`build.ps1` 添加系统程序集引用，无额外安装。查询仍在 `ClassifySafe` 工作线程内执行，300ms 超时熔断。空闲检测也复用该保护。

WorkBuddy 内部切换焦点时 HWND 和原生焦点句柄都不变，故粗粒度窗口即使同一对句柄已检测三次，也继续按 400ms 轮询，避免形状锁在旧状态。录音中仍每 250ms 检测目标；缓存、音频分界与恢复不重放规则沿用原有实现。

接口依据：[微软 accFocus](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-iaccessible-get_accfocus)、[对象状态](https://learn.microsoft.com/en-us/windows/win32/winauto/object-state-constants)。

## 验证

复现：旧版在实际 WorkBuddy 文件预览中返回 `Unknown deliver=True`，断言报 `REPRO: WorkBuddy read-only preview is incorrectly treated as a delivery target`。

实窗对照使用 `--target-probe WorkBuddy --expect ReadOnly` 和 `--expect Editable`。探针不抢前台、不送字，实际输出见 `work/msaa-target-probe-verified.txt`：同一个活动窗口和焦点句柄，预览 `ReadOnly deliver=False`，输入框 `Editable deliver=True`。`--expect` 不符时退出码为 1。

- `--self-test`：包含 MSAA 可写、只读、按钮、不可用/隐藏节点、无焦点和粗粒度占位节点分支；也覆盖 Ctrl+C 复制一次放行及滚轮保护。
- `--hotkey-test`：快捷键与真实鼠标回调回归通过。
- `--focus-test`：实际 WinForms 输入、只读、跨窗切换、恢复后不重放等通过。首次运行失败，随后旧版和修复版对照重跑均通过，未修改或放宽测试断言。
- `--idle-focus-test`：空闲焦点形变与缓存状态通过。

结果分别见 `work/microphone-self-test.txt`、`work/microphone-hotkey-test.txt`、`work/microphone-focus-test.txt`、`work/microphone-idle-shape-test.txt`。

未重跑 ASR 准确率评测，识别模型未改；实际 WorkBuddy 验证没有输入测试文字或发送消息。其他应用无法暴露焦点时仍走 Unknown 兼容路径，并非所有窗口均能确认实际文字落点。

交付：已使用 build.ps1 构建正式程序并后台重启，确认运行 1.6.14.0 单实例；正式 exe 在 WorkBuddy 输入框与预览中分别通过 Editable、ReadOnly 断言。SHA256：AB17C8F478DB25ACEDD1136E50FE4759D3A9DCD90CAB3EDEDC7824E13B44DB97。
