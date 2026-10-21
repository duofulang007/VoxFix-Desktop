using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("VoxFix Desktop")]
[assembly: AssemblyProduct("VoxFix Desktop")]
[assembly: AssemblyDescription("VoxFix Desktop 本地语音输入")]
[assembly: AssemblyVersion("1.6.5.0")]

static class Native
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr process);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder name, int length, out int needed);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    internal static string ClassOf(IntPtr window) {
        var name = new StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
    internal static string TitleOf(IntPtr window) {
        var text = new StringBuilder(512);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte operation, flags, alpha, format; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point destination, ref Size size, IntPtr source, ref Point origin, uint key, ref Blend blend, uint flags);
    internal static void Present(IntPtr window, Point location, Bitmap bitmap) {
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, image = IntPtr.Zero, previous = IntPtr.Zero;
        try {
            memory = CreateCompatibleDC(screen); image = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(memory, image);
            Point origin = Point.Empty; Size size = bitmap.Size;
            var blend = new Blend { alpha = 255, format = 1 };
            if (!UpdateLayeredWindow(window, screen, ref location, ref size, memory, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (image != IntPtr.Zero) DeleteObject(image);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern bool PlaySound(string sound, IntPtr module, uint flags);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
    internal static bool AnimationsEnabled() {
        int enabled;
        return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled != 0;
    }
    internal const uint ShowMicrophone = 0x8035;
    internal static readonly UIntPtr InputTag = new UIntPtr(0x56464454);
    internal static string DesktopName() {
        var name = new StringBuilder(256); int needed;
        if (!GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, name.Capacity * 2, out needed))
            throw new IOException("无法确定当前桌面。");
        return name.ToString();
    }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo {
        public int size; public uint flags; public IntPtr active, focus, capture, menu, move, caret;
        public Rectangle caretRect;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard {
        public ushort vk, scan; public uint flags, time; public UIntPtr extra;
    }
    [StructLayout(LayoutKind.Explicit)] internal struct Payload {
        [FieldOffset(0)] public Keyboard keyboard;
        [FieldOffset(0)] public Mouse mouse;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse {
        public int x, y; public uint data, flags, time; public UIntPtr extra;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type; public Payload data; }
    internal static IntPtr Focus(IntPtr window) {
        var info = new GuiInfo { size = Marshal.SizeOf(typeof(GuiInfo)) };
        return GetGUIThreadInfo(GetWindowThreadProcessId(window, IntPtr.Zero), ref info) ? info.focus : IntPtr.Zero;
    }
    internal static Input[] TextInputs(string text) {
        var inputs = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++) {
            inputs[i * 2] = new Input { type = 1, data = new Payload { keyboard = new Keyboard { scan = text[i], flags = 4, extra = InputTag } } };
            inputs[i * 2 + 1] = new Input { type = 1, data = new Payload { keyboard = new Keyboard { scan = text[i], flags = 6, extra = InputTag } } };
        }
        return inputs;
    }
}

sealed class Recorder : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)] struct Format {
        public ushort tag, channels; public uint rate, bytes; public ushort align, bits, extra;
    }
    [StructLayout(LayoutKind.Sequential)] struct Header {
        public IntPtr data; public uint length, recorded; public UIntPtr user;
        public uint flags, loops; public IntPtr next; public UIntPtr reserved;
    }
    [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr handle, uint device, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr handle);
    [DllImport("winmm.dll")] internal static extern uint waveInGetNumDevs();
    IntPtr device;
    readonly List<IntPtr> buffers = new List<IntPtr>();
    readonly List<IntPtr> headers = new List<IntPtr>();
    int next;
    const int BufferCount = 32, BufferBytes = 3200;
    uint headerSize = (uint)Marshal.SizeOf(typeof(Header));
    static void Check(uint result) { if (result != 0) throw new IOException("麦克风无法打开或读取（" + result + "）。请检查 Windows 麦克风权限和默认输入设备。"); }
    public Recorder() {
        try {
            var format = new Format { tag = 1, channels = 1, rate = 16000, bytes = 32000, align = 2, bits = 16 };
            Check(waveInOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
            for (int i = 0; i < BufferCount; i++) {
                IntPtr data = Marshal.AllocHGlobal(BufferBytes); buffers.Add(data);
                IntPtr header = Marshal.AllocHGlobal((int)headerSize); headers.Add(header);
                Marshal.StructureToPtr(new Header { data = data, length = BufferBytes }, header, false);
                Check(waveInPrepareHeader(device, header, headerSize));
                Check(waveInAddBuffer(device, header, headerSize));
            }
            Check(waveInStart(device));
        } catch { Dispose(); throw; }
    }
    public byte[] Stop() {
        Check(waveInReset(device));
        return Read(false);
    }
    public byte[] Split() {
        Check(waveInReset(device));
        byte[] tail = Read(false);
        for (int i = 0; i < headers.Count; i++) Check(waveInAddBuffer(device, headers[(next + i) % headers.Count], headerSize));
        Check(waveInStart(device));
        return tail;
    }
    public byte[] Read(bool requeue) {
        using (var output = new MemoryStream()) {
            int read = 0;
            while (read < headers.Count) {
                IntPtr header = headers[next];
                var value = (Header)Marshal.PtrToStructure(header, typeof(Header));
                if ((value.flags & 1) == 0) break;
                int length = (int)(value.recorded - value.recorded % 2);
                if (length > BufferBytes) throw new IOException("录音缓冲区长度异常。");
                var bytes = new byte[length]; Marshal.Copy(value.data, bytes, 0, length); output.Write(bytes, 0, length);
                if (requeue) Check(waveInAddBuffer(device, header, headerSize));
                next = (next + 1) % headers.Count; read++;
            }
            if (requeue && read == BufferCount) throw new IOException("识别未能跟上录音，已停止，避免漏音。请重新开始。");
            return output.ToArray();
        }
    }
    public void Dispose() {
        if (device != IntPtr.Zero) {
            waveInReset(device);
            foreach (IntPtr header in headers) {
                var value = (Header)Marshal.PtrToStructure(header, typeof(Header));
                if ((value.flags & 2) != 0 && waveInUnprepareHeader(device, header, headerSize) != 0) return;
            }
            waveInClose(device); device = IntPtr.Zero;
        }
        foreach (IntPtr header in headers) Marshal.FreeHGlobal(header); headers.Clear();
        foreach (IntPtr data in buffers) Marshal.FreeHGlobal(data); buffers.Clear();
    }
}

sealed class ShapeMotion
{
    internal double Value, Velocity;
    double previous = -1;
    internal bool Settled = true;
    internal void Update(bool pill, double now, bool reduced) {
        double target = pill ? 1 : 0;
        double elapsed = previous < 0 ? 0 : Math.Max(0, Math.Min(.064, (now - previous) / 1000));
        previous = now;
        if (reduced) { Value = target; Velocity = 0; Settled = true; return; }
        while (elapsed > 0) {
            double step = Math.Min(elapsed, 1.0 / 120);
            Velocity += ((target - Value) * 100 - Velocity * 10) * step;
            Value += Velocity * step; elapsed -= step;
        }
        Settled = Math.Abs(Value - target) < .002 && Math.Abs(Velocity) < .02;
        if (Settled) { Value = target; Velocity = 0; }
    }
}

sealed class TextDelivery
{
    internal string Delivered = "", Accounted = "", Latest = "", Paused = "";
    internal bool Cancelled;
    internal bool Invalidated;
    internal bool MemoryStarted;
    internal bool NewMemoryBlock;
    internal string Pending { get { return Latest.StartsWith(Accounted, StringComparison.Ordinal) ? Latest.Substring(Accounted.Length) : Latest; } }
    internal string TakePending() {
        string pending = Pending;
        Accounted = Latest;
        return pending;
    }
    internal void Sent(string text) {
        Delivered += text; Accounted += text;
    }
    internal string Accept(string text) {
        Latest = text;
        if (!text.StartsWith(Accounted, StringComparison.Ordinal)) { Invalidated = true; Paused = "识别结果发生回退，已停止输入。"; }
        return Paused.Length == 0 ? Pending : "";
    }
}

sealed class InteractionGuard : IDisposable
{
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)] struct Key { public uint vk, scan, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public Point point; public uint data, flags, time; public UIntPtr extra; }
    readonly Hook keyboard, mouse;
    IntPtr keyHandle, mouseHandle;
    internal InteractionGuard(Func<Point, bool> ownWindow, Action<string> pause) {
        keyboard = (code, message, data) => {
            if (code >= 0 && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104)) {
                var key = (Key)Marshal.PtrToStructure(data, typeof(Key));
                if (key.extra != Native.InputTag) pause((key.flags & 16) != 0 ? "外部模拟按键" : "键盘操作");
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        mouse = (code, message, data) => {
            long kind = message.ToInt64();
            if (code >= 0 && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20A || kind == 0x20B || kind == 0x20E)) {
                var value = (Mouse)Marshal.PtrToStructure(data, typeof(Mouse));
                if (!ownWindow(value.point)) pause("鼠标操作");
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        keyHandle = SetWindowsHookEx(13, keyboard, GetModuleHandle(null), 0);
        mouseHandle = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
        if (keyHandle == IntPtr.Zero || mouseHandle == IntPtr.Zero) { Dispose(); throw new IOException("无法监测输入位置变化，已停止录音。请重新打开程序。"); }
    }
    public void Dispose() {
        if (keyHandle != IntPtr.Zero) { UnhookWindowsHookEx(keyHandle); keyHandle = IntPtr.Zero; }
        if (mouseHandle != IntPtr.Zero) { UnhookWindowsHookEx(mouseHandle); mouseHandle = IntPtr.Zero; }
    }
}

sealed class DictationMotion
{
    internal float Activity, Voice, Level;
    bool active, speaking;
    float activityFrom, voiceFrom;
    double activityAt, voiceAt, lastSound = -1000, lastUpdate;
    internal static float Ease(double progress) {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        // Invert x for cubic-bezier(.23, 1, .32, 1).
        double low = 0, high = 1, t = 0;
        for (int i = 0; i < 16; i++) {
            t = (low + high) / 2;
            double x = 3 * (1 - t) * (1 - t) * t * .23 + 3 * (1 - t) * t * t * .32 + t * t * t;
            if (x < progress) low = t; else high = t;
        }
        return (float)(1 - Math.Pow(1 - t, 3));
    }
    internal void Update(string state, float level, double now, bool reduced, bool? detectedSpeech = null) {
        bool nextActive = state == "recording" || state == "busy";
        if (state == "recording" && (detectedSpeech ?? (level >= .02f))) lastSound = now;
        bool nextSpeaking = state == "recording" && now - lastSound < 220;
        Activity = activityFrom + ((active ? 1 : 0) - activityFrom) * Ease((now - activityAt) / 180);
        Voice = voiceFrom + ((speaking ? 1 : 0) - voiceFrom) * Ease((now - voiceAt) / 160);
        if (active != nextActive) { activityFrom = Activity; activityAt = now; active = nextActive; }
        if (speaking != nextSpeaking) { voiceFrom = Voice; voiceAt = now; speaking = nextSpeaking; }
        if (reduced) { Activity = active ? 1 : 0; Voice = speaking ? 1 : 0; }
        Level += (level - Level) * (float)(1 - Math.Exp(-Math.Max(0, now - lastUpdate) / 60));
        lastUpdate = now;
        if (state != "recording") lastSound = -1000;
    }
}

sealed class Microphone : Form
{
    readonly string root;
    readonly bool smokeTest;
    readonly bool streamTest;
    readonly ToolTip tip = new ToolTip();
    readonly System.Windows.Forms.Timer limit = new System.Windows.Forms.Timer { Interval = 180000 };
    readonly System.Windows.Forms.Timer capture = new System.Windows.Forms.Timer { Interval = 40 };
    readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 16 };
    // Idle focus watch: the capsule reports whether text would land in a real input box
    // even before recording starts. Cheap when the target pair has not changed.
    readonly System.Windows.Forms.Timer focusWatch = new System.Windows.Forms.Timer { Interval = 400 };
    IntPtr idleWindow, idleFocus;
    int idleAttempts;
    bool idleWatching;
    readonly SemaphoreSlim pipeline = new SemaphoreSlim(1, 1);
    readonly bool animationsEnabled = Native.AnimationsEnabled();
    Process worker;
    Recorder recorder;
    InteractionGuard guard;
    TextDelivery delivery;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    Action<string> copyText;
    string copiedInTest = "";
    readonly bool memoryTest;
    readonly bool probeOnly;
    bool inputFocused;
    bool recoveryCopied;
    bool hovered, pressed;
    float copyFeedback, hoverFeedback, pressFeedback;
    bool heldTriggered;
    double pressedAt;
    readonly ShapeMotion shape = new ShapeMotion();
    string recovery = "", draft = "";
    string recordingId;
    int sentBytes;
    string state = "loading";
    IntPtr target, targetFocus;
    Task targetProbe;
    double nextTargetProbe;
    int targetEpoch;
    int audioRoute, appliedSegments;
    readonly HashSet<int> backupRoutes = new HashSet<int>();
    Point down, origin;
    bool dragged, closing;
    bool listening;
    float level;
    bool speechDetected, transcribing;
    bool restarting, restartPill;
    Action<string, bool> playEarcon = Earcon;
    float readyFeedback;
    readonly DictationMotion motion = new DictationMotion();
    readonly Stopwatch motionClock = Stopwatch.StartNew();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08080080; return p; } }
    public Microphone(string path, string test) {
        root = path; smokeTest = test == "--smoke-test"; streamTest = test == "--stream-test"; Text = "VoxFix Desktop"; AccessibleName = "VoxFix Desktop 麦克风";
        memoryTest = test == "--memory-test" || test == "--focus-test" || test == "--idle-focus-test";
        probeOnly = test == "--focus-test" || test == "--idle-focus-test";
        if (memoryTest || streamTest || smokeTest) playEarcon = (name, wait) => { };
        copyText = streamTest || memoryTest ? new Action<string>(text => copiedInTest = text) : new Action<string>(Clipboard.SetText);
        menu.Items.Add("复制未上屏文字", null, (s, e) => CopyRecovery());
        menu.Items.Add("开始录音", null, async (s, e) => { if (state == "recording") await EndRecording(); else await Toggle(); });
        menu.Items.Add("清空未上屏记忆", null, (s, e) => { recovery = ""; recoveryCopied = false; UpdateMemoryShape(); RefreshSurface(); });
        menu.Items.Add("退出", null, (s, e) => Close());
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(128, 96);
        StartPosition = FormStartPosition.Manual;
        PlaceOnCurrentScreen();
        DoubleBuffered = true; Cursor = Cursors.Hand;
        tip.SetToolTip(this, "正在加载本地模型…");
        limit.Tick += async (s, e) => { limit.Stop(); await Toggle(); };
        capture.Tick += async (s, e) => await Pump();
        focusWatch.Tick += async (s, e) => await WatchIdleFocus();
        animation.Tick += async (s, e) => {
            if (TakeLongPress(motionClock.Elapsed.TotalMilliseconds)) { await NewRecording(); return; }
            motion.Update(restarting ? "recording" : state, level, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled, speechDetected);
            shape.Update(MemoryPill, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled);
            copyFeedback = Feedback(copyFeedback, recoveryCopied ? 1 : 0);
            hoverFeedback = Feedback(hoverFeedback, hovered ? 1 : 0);
            pressFeedback = Feedback(pressFeedback, pressed && !dragged ? 1 : 0);
            readyFeedback = Feedback(readyFeedback, CopyReady ? 1 : 0);
            if (state == "idle" || state == "error") {
                if (!pressed && shape.Settled && motion.Activity < .001f && motion.Voice < .001f &&
                    copyFeedback == (recoveryCopied ? 1 : 0) && hoverFeedback == (hovered ? 1 : 0) &&
                    pressFeedback == (pressed && !dragged ? 1 : 0) && readyFeedback == (CopyReady ? 1 : 0)) animation.Stop();
            }
            RefreshSurface();
        };
        MouseEnter += (s, e) => { hovered = true; animation.Start(); };
        MouseLeave += (s, e) => { hovered = false; if (!Capture) pressed = false; animation.Start(); };
        MouseCaptureChanged += (s, e) => { if (!Capture) { pressed = false; animation.Start(); } };
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = Cursor.Position; origin = Location; dragged = false; pressed = true; heldTriggered = false; pressedAt = motionClock.Elapsed.TotalMilliseconds; Capture = true; animation.Start(); } };
        MouseMove += (s, e) => {
            if (e.Button != MouseButtons.Left) return;
            Point p = Cursor.Position;
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) > 5) dragged = true;
            if (dragged) {
                var area = Screen.FromPoint(p).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, origin.X + p.X - down.X)), Math.Max(area.Top, Math.Min(area.Bottom - Height, origin.Y + p.Y - down.Y)));
            }
        };
        MouseUp += async (s, e) => {
            pressed = false; Capture = false; animation.Start();
            if (e.Button == MouseButtons.Right) {
                menu.Items[0].Enabled = CopyReady;
                menu.Items[1].Text = state == "recording" ? "结束录音" : "开始录音";
                menu.Items[1].Enabled = state == "idle" || state == "error" || state == "recording";
                menu.Items[2].Enabled = recovery.Length > 0;
                menu.Show(this, e.Location); return;
            }
            if (e.Button == MouseButtons.Left && !dragged) {
                await ShortClick(e.X);
            }
        };
        Shown += async (s, e) => { focusWatch.Start(); if (!probeOnly) await StartWorker(); };
        FormClosed += (s, e) => {
            closing = true; limit.Stop(); limit.Dispose();
            capture.Stop(); capture.Dispose();
            focusWatch.Stop(); focusWatch.Dispose();
            if (guard != null) guard.Dispose();
            if (recorder != null) recorder.Dispose();
            EndListening(true);
            animation.Stop(); animation.Dispose();
            delivery = null;
            StopWorker();
            if (worker != null) worker.Dispose();
            tip.Dispose();
            menu.Dispose();
        };
    }
    void PlaceOnCurrentScreen() {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - 36), Math.Max(area.Top, area.Bottom - Height - 54));
    }
    protected override void WndProc(ref Message message) {
        if (message.Msg == Native.ShowMicrophone) { PlaceOnCurrentScreen(); Show(); TopMost = true; RefreshSurface(); return; }
        base.WndProc(ref message);
    }
    void Status(string value, string description) {
        state = value; AccessibleDescription = description; tip.SetToolTip(this, description); RefreshSurface();
        UpdateMemoryShape();
        if (value != "recording") { level = 0; speechDetected = false; }
        if (value == "idle" || value == "error") transcribing = false;
        motion.Update(restarting ? "recording" : state, level, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled, speechDetected);
        animation.Start();
    }
    float Feedback(float value, float targetValue) {
        if (!animationsEnabled || Math.Abs(value - targetValue) < .015f) return targetValue;
        return value + (targetValue - value) * .45f;
    }
    void CopyRecovery() {
        if (!CopyReady) { tip.SetToolTip(this, "转写完成后才能复制；左侧可结束录音并收齐尾字。"); return; }
        recoveryCopied = false;
        try {
            copyText(recovery);
            recoveryCopied = true;
            tip.SetToolTip(this, state == "error" ? "已复制 · 左侧重试 · 右侧重新复制" : "已复制 · 录音时点左侧声纹结束，点右侧重新复制 · 长按清空并开始新录音");
        } catch { tip.SetToolTip(this, "剪贴板暂时不可用，文字仍在图标里，点击重试。"); }
        UpdateMemoryShape();
        animation.Start();
        RefreshSurface();
        if (!recoveryCopied) ShowRecoveryNotice();
    }
    void ShowRecoveryNotice() {
        if (recovery.Length == 0 || !Visible) return;
        tip.Show(recoveryCopied ? "已复制，按 Ctrl+V 粘贴" : "复制失败，点击重试", this, Width / 2, -28, 3500);
    }
    void KeepRecovery(string text) {
        if (text.Length == 0) return;
        if (delivery == null || !delivery.MemoryStarted || delivery.NewMemoryBlock) {
            if (recovery.Length > 0) recovery += Environment.NewLine;
            if (delivery != null) { delivery.MemoryStarted = true; delivery.NewMemoryBlock = false; }
        }
        recovery += text; recoveryCopied = false; animation.Start(); RefreshSurface();
        UpdateMemoryShape();
        tip.SetToolTip(this, "左侧声纹：结束录音 · 右侧：复制 · 长按：清空缓存并开始新一轮");
    }
    void UpdateMemoryShape() {
        bool pill = MemoryPill;
        AccessibleName = !pill ? "VoxFix Desktop 麦克风"
            : state == "error" ? "识别失败，左侧重试，右侧复制"
            : recovery.Length == 0 ? "当前焦点不能直接输入，文字会先缓存"
            : transcribing || speechDetected || state == "busy" ? "正在转写，暂不可复制"
            : CopyReady ? (recoveryCopied ? "已复制未上屏文字" : "复制未上屏文字")
            : "正在监听，等待讲话";
        if (pill) AccessibleDescription = state == "error" ? "左侧重试 · 右侧复制 · 缓存文字保留"
            : recovery.Length == 0 ? "点一下开始说话 · 焦点移回输入框会变回圆形"
            : "录音时点左侧结束，点右侧复制 · 长按约0.4秒清空缓存并开始新录音";
        animation.Start();
    }
    bool MemoryPill {
        get {
            if (restarting) return restartPill;
            // While recording the shape reports the live delivery gate.
            if (delivery != null && (state == "recording" || state == "busy")) return !inputFocused;
            // Otherwise a waiting cache owns the capsule, and with nothing cached the
            // shape still reports whether this focus can receive text right now.
            if (recovery.Length > 0) return true;
            return !inputFocused && (state == "idle" || state == "error");
        }
    }
    bool CopyReady { get { return !restarting && recovery.Length > 0 && !transcribing && !speechDetected && state != "busy" && state != "loading"; } }
    void SetInputFocus(bool focused) {
        if (inputFocused == focused) return;
        inputFocused = focused;
        if (state == "idle" && recovery.Length == 0)
            tip.SetToolTip(this, focused ? "这里可以直接输入，点一下开始说话" : "这里不会直接输入，文字会先存成可复制内容");
        UpdateMemoryShape(); RefreshSurface();
    }
    sealed class IdleProbe { internal IntPtr Window, Focus; internal TargetKind Kind; }
    // Same reporting while nothing is being recorded, so the capsule shows the delivery
    // gate at a glance. A stalled provider must not freeze the overlay either.
    async Task WatchIdleFocus() {
        if (closing || idleWatching) return;
        if (state != "idle" && state != "error") return;
        if (recovery.Length > 0) {
            // A waiting transcript owns the capsule and stays a copy button.
            idleWindow = idleFocus = IntPtr.Zero; idleAttempts = 0;
            if (inputFocused) SetInputFocus(false);
            return;
        }
        IntPtr window = Native.GetForegroundWindow(), own = Handle;
        if (window == IntPtr.Zero || window == own) {
            idleWindow = idleFocus = IntPtr.Zero; idleAttempts = 0;
            if (inputFocused) SetInputFocus(false);
            return;
        }
        IntPtr focus = Native.Focus(window);
        bool samePair = window == idleWindow && focus == idleFocus;
        // Accessibility focus trails a real focus move by a frame or two, so a pair is
        // asked up to three times before its verdict is trusted and the polling stops.
        if (samePair && idleAttempts >= 3) return;
        idleWatching = true;
        try {
            var probe = Task.Run(() => new IdleProbe { Window = window, Focus = focus, Kind = Classify(window, focus, own, null) });
            if (await Task.WhenAny(probe, Task.Delay(400)) != probe) return;
            IdleProbe result = await probe;
            if (closing || (state != "idle" && state != "error") || recovery.Length > 0) return;
            idleAttempts = samePair ? idleAttempts + 1 : 1;
            idleWindow = result.Window; idleFocus = result.Focus;
            SetInputFocus(Deliverable(result.Kind));
        } catch { } finally { idleWatching = false; }
    }
    bool OwnsSurface(Point point) {
        SizeF size = ShapeSize(shape.Value);
        double diameter = Math.Min(size.Width, size.Height);
        double dx = Math.Max(0, Math.Abs(point.X - Left - 64) - (size.Width - diameter) / 2);
        double dy = Math.Max(0, Math.Abs(point.Y - Top - 48) - (size.Height - diameter) / 2);
        return dx * dx + dy * dy <= diameter * diameter / 4;
    }
    bool TakeLongPress(double now) {
        if (!pressed || dragged || heldTriggered || !MemoryPill || (state != "idle" && state != "recording" && state != "error") || now - pressedAt < 400) return false;
        heldTriggered = true;
        return true;
    }
    async Task NewRecording() {
        if (restarting) return;
        restartPill = MemoryPill; restarting = true;
        tip.Hide(this); tip.Active = false;
        try {
            if (state == "error") await StartWorker(false);
            if (state == "recording") await EndRecording(true);
            if (state == "idle") {
                recovery = draft = ""; recoveryCopied = false;
                UpdateMemoryShape(); RefreshSurface();
                await Toggle();
            }
        } finally {
            restarting = false;
            if (!closing) { tip.Active = true; UpdateMemoryShape(); RefreshSurface(); }
        }
    }
    async Task ShortClick(int x) {
        if (heldTriggered) return;
        if (state == "error" && (!MemoryPill || x < 68)) { await Toggle(); return; }
        if (state == "recording" && (!MemoryPill || x < 68)) { await EndRecording(); return; }
        if (MemoryPill) {
            if (recovery.Length > 0) { CopyRecovery(); return; }
            // An idle capsule with nothing cached is a focus hint, not a copy button.
            if (state != "recording" && state != "busy") { await Toggle(); return; }
            tip.Show("正在定稿，停顿后可复制；点左侧声纹可结束录音并收齐尾字", this, Width / 2, -28, 2500);
            return;
        }
        await Toggle();
    }
    void ApplyReply(Dictionary<string, object> reply) {
        if (reply.ContainsKey("level")) level = Convert.ToSingle(reply["level"]);
        if (reply.ContainsKey("speaking")) speechDetected = Convert.ToBoolean(reply["speaking"]);
        if (reply.ContainsKey("transcribing")) transcribing = Convert.ToBoolean(reply["transcribing"]);
        if (reply.ContainsKey("segments") && delivery != null) {
            int index = 0;
            foreach (Dictionary<string, object> segment in (System.Collections.IEnumerable)reply["segments"]) {
                if (index++ < appliedSegments) continue;
                ApplyText(delivery.Latest + Convert.ToString(segment["text"]), backupRoutes.Contains(Convert.ToInt32(segment["route"])));
                appliedSegments = index;
            }
        } else ApplyText(Convert.ToString(reply["text"]));
        string preview = reply.ContainsKey("preview") ? Convert.ToString(reply["preview"]) : "";
        if (draft != preview) { draft = preview; UpdateMemoryShape(); }
        UpdateMemoryShape();
    }
    internal static string EarconPath(string name) {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy", "InputApp", "Assets", "Dictation", name + ".wav");
    }
    static void Earcon(string name, bool wait = false) {
        string path = EarconPath(name);
        if (File.Exists(path)) Native.PlaySound(path, IntPtr.Zero, wait ? 0x20002u : 0x20003u);
    }
    void EndListening(bool cancelled, bool silent = false) {
        if (!listening) return;
        listening = false;
        if (!silent) playEarcon(cancelled ? "BasicCancelledEarcon" : "BasicDoneListeningEarcon", closing);
    }
    async Task StartWorker(bool runChecks = true) {
        try {
            Status("loading", "正在加载 SenseVoiceSmall 本地模型…");
            StopWorker();
            var info = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe"), "-u \"" + Path.Combine(root, "app", "recognize.py") + "\"") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            worker = Process.Start(info);
            worker.ErrorDataReceived += (s, e) => { }; worker.BeginErrorReadLine();
            var reply = await Reply();
            if (closing) return;
            if (!reply.ContainsKey("ready") || !reply.ContainsKey("segmented")) throw new IOException("SenseVoice 识别服务启动失败。");
            Status("idle", "点击说话 · 拖动移动 · 右键退出");
            if (runChecks && memoryTest) { await RunMemoryTest(); return; }
            if (runChecks && streamTest) { await RunStreamTest(); return; }
            if (runChecks && smokeTest) {
                using (var bitmap = new Bitmap(Width, Height)) {
                    DrawToBitmap(bitmap, ClientRectangle);
                    bitmap.Save(Path.Combine(root, "work", "microphone-live-render.png"));
                }
                File.WriteAllText(Path.Combine(root, "work", "microphone-ui-test.txt"), "PASS: native form / worker ready / JSON bridge / graceful exit");
                Close();
            }
        } catch (Exception error) {
            if (smokeTest || streamTest || memoryTest) {
                File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-test-failure.txt"), error.ToString());
                Environment.ExitCode = 1; Close();
            } else Fail(error.Message);
        }
    }
    async Task<Dictionary<string, object>> Reply() {
        var read = worker.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(read, Task.Delay(60000)) != read) {
            StopWorker();
            throw new IOException("识别服务超时，请右键退出后重新打开。");
        }
        string line = await read;
        if (line == null) throw new IOException("识别服务已停止，请右键退出后重新打开。");
        return json.Deserialize<Dictionary<string, object>>(line);
    }
    void StopWorker() {
        if (worker == null) return;
        var process = worker; worker = null;
        try {
            if (process.HasExited) return;
            process.StandardInput.Close();
            if (!process.WaitForExit(2000)) {
                // The Windows venv launcher can own a child interpreter; terminate the complete tree on timeout.
                using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F") {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                })) kill.WaitForExit(2000);
            }
        } catch { }
        finally { process.Dispose(); }
    }
    void Fail(string message) {
        if (closing) return;
        capture.Stop(); limit.Stop();
        if (guard != null) { guard.Dispose(); guard = null; }
        if (recorder != null) { recorder.Dispose(); recorder = null; }
        EndListening(true);
        if (delivery != null && !delivery.Cancelled)
            KeepRecovery(delivery.TakePending() + (draft.Length > 0 ? "\n[未定稿] " + draft : ""));
        delivery = null;
        StopWorker();
        Status("error", message + (recovery.Length > 0 ? " 左侧重试 · 右侧复制 · 右键退出" : " 点击重试 · 右键退出"));
    }
    // UIA is a positive signal, not a gate. Chromium/Electron with accessibility off
    // (WorkBuddy, several IDEs), Flutter and XAML-island apps expose no Edit/Document
    // node at all: their whole window tree is a single Pane. Treating that silence as
    // "no input box" sent every sentence to the copy cache, so the tree's absence is
    // now its own state and the text is still delivered at the caret.
    internal enum TargetKind { ReadOnly, Unknown, Editable }
    static bool Deliverable(TargetKind kind) { return kind == TargetKind.Editable || kind == TargetKind.Unknown; }
    // Pure decision, kept testable. A coarse window (Chromium with accessibility off,
    // Flutter, custom-drawn shells) answers with one placeholder node that describes the
    // window itself - Electron composers report a read-only Document - so its verdict is
    // discarded and the text is delivered at the caret instead of being cached.
    static TargetKind DecideTarget(bool coarse, bool textHost, bool writable) {
        if (coarse) return TargetKind.Unknown;
        if (textHost) return writable ? TargetKind.Editable : TargetKind.ReadOnly;
        return TargetKind.ReadOnly;
    }
    readonly Dictionary<IntPtr, bool> coarseWindows = new Dictionary<IntPtr, bool>();
    // Chromium/Electron hosts keep accessibility off until a client asks, and then answer
    // with a placeholder read-only Document - or stall the client outright - so their tree
    // can never describe the caret. Flutter and custom-drawn shells behave the same way.
    // The window class is instant and cannot stall, so it decides without touching UIA.
    // Ordinary dialogs are never coarse: UIA reports their buttons and fields reliably.
    static bool CoarseClass(IntPtr window) {
        string name = Native.ClassOf(window);
        return name.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)
            || name == "FLUTTER_RUNNER_WIN32_WINDOW" || name == "CEF-OSC-WIDGET"
            || name == "TaskManagerWindow" || name == "Intermediate D3D Window";
    }
    // Cached per window and per recording.
    static bool WindowIsCoarse(IntPtr window, Dictionary<IntPtr, bool> cache) {
        bool cached;
        if (cache != null && cache.TryGetValue(window, out cached)) return cached;
        bool coarse = CoarseClass(window);
        if (cache != null) cache[window] = coarse;
        return coarse;
    }
    static TargetKind Classify(IntPtr window, IntPtr focus, IntPtr own, Dictionary<IntPtr, bool> cache) {
        if (window == IntPtr.Zero || window == own) return TargetKind.ReadOnly;
        // A coarse window cannot answer for the caret, so ask nothing further.
        if (WindowIsCoarse(window, cache)) return TargetKind.Unknown;
        if (focus == IntPtr.Zero) return TargetKind.ReadOnly;
        try {
            var element = System.Windows.Automation.AutomationElement.FocusedElement;
            if (element == null) return TargetKind.ReadOnly;
            if (!element.Current.IsEnabled || !element.Current.IsKeyboardFocusable || !element.Current.HasKeyboardFocus) return TargetKind.ReadOnly;
            // UIA can briefly return the previous application's focused element.
            var ancestor = element;
            bool belongs = false;
            for (int i = 0; ancestor != null && i < 64; i++) {
                IntPtr handle = new IntPtr(ancestor.Current.NativeWindowHandle);
                if (handle == window || (handle != IntPtr.Zero && Native.IsChild(window, handle))) { belongs = true; break; }
                ancestor = System.Windows.Automation.TreeWalker.RawViewWalker.GetParent(ancestor);
            }
            var type = element.Current.ControlType;
            bool textHost = type == System.Windows.Automation.ControlType.Edit
                || type == System.Windows.Automation.ControlType.Document
                || type == System.Windows.Automation.ControlType.ComboBox;
            // A foreign or hidden focus node cannot prove this window is editable.
            if (!belongs || !textHost) return TargetKind.ReadOnly;
            object pattern;
            if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out pattern))
                return DecideTarget(false, true, !((System.Windows.Automation.ValuePattern)pattern).Current.IsReadOnly);
            if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out pattern))
                return DecideTarget(false, true, Object.Equals(((System.Windows.Automation.TextPattern)pattern).DocumentRange.GetAttributeValue(System.Windows.Automation.TextPattern.IsReadOnlyAttribute), false));
            return DecideTarget(false, true, true);
        } catch { return TargetKind.Unknown; }
    }
    TargetKind ClassifyTarget(IntPtr window, IntPtr focus) { return Classify(window, focus, Handle, coarseWindows); }
    // A stalled accessibility provider must never freeze dictation: give it one bounded
    // chance, then stop asking that window and let the text reach the caret.
    readonly HashSet<IntPtr> stalledWindows = new HashSet<IntPtr>();
    async Task<TargetKind> ClassifySafe(IntPtr window, IntPtr focus) {
        if (stalledWindows.Contains(window)) return TargetKind.Unknown;
        var probe = Task.Run(() => ClassifyTarget(window, focus));
        if (await Task.WhenAny(probe, Task.Delay(300)) != probe) {
            Task ignored = probe.ContinueWith(task => { var inner = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            stalledWindows.Add(window);
            return TargetKind.Unknown;
        }
        return await probe;
    }
    bool HasEditableTarget() { return ClassifyTarget(target, targetFocus) == TargetKind.Editable; }
    bool EditingGestureDown() {
        if (Native.GetAsyncKeyState(0x01) < 0 && !OwnsSurface(Cursor.Position)) return true;
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if (Native.GetAsyncKeyState(key) < 0) return true;
        return false;
    }
    bool HasEditableTarget(IntPtr window, IntPtr focus) { return ClassifyTarget(window, focus) == TargetKind.Editable; }
    async Task ProbeTarget(IntPtr testWindow) {
        TextDelivery session = delivery;
        if (session == null || session.Invalidated) return;
        int epoch = targetEpoch;
        IntPtr window = Native.GetForegroundWindow(), focus = Native.Focus(window);
        if (memoryTest && window != testWindow) return;
        try {
            var kind = await ClassifySafe(window, focus);
            if (closing || delivery != session || epoch != targetEpoch || Native.GetForegroundWindow() != window || Native.Focus(window) != focus) return;
            if (!memoryTest && state != "recording") return;
            if (!Deliverable(kind)) { SetInputFocus(false); Pause("当前没有可确认的输入位置，继续录音并保存未上屏文字。"); return; }
            if (EditingGestureDown()) return;
            if (session.Paused.Length > 0) {
                await pipeline.WaitAsync();
                try {
                    if (closing || delivery != session || epoch != targetEpoch || Native.GetForegroundWindow() != window || Native.Focus(window) != focus) return;
                    if (!memoryTest && state != "recording") return;
                    if (recorder != null && state == "recording") {
                        await Feed(recorder.Split());
                        if (closing || delivery != session || epoch != targetEpoch) return;
                        audioRoute++;
                        backupRoutes.Add(audioRoute);
                        ApplyReply(await Exchange(new { op = "route", id = recordingId, route = audioRoute }));
                    }
                    if (closing || delivery != session || epoch != targetEpoch || Native.GetForegroundWindow() != window || Native.Focus(window) != focus) return;
                    KeepRecovery(session.TakePending());
                    backupRoutes.Remove(audioRoute);
                    session.Paused = ""; session.NewMemoryBlock = true;
                } finally { pipeline.Release(); }
            }
            target = window; targetFocus = focus;
            SetInputFocus(true); UpdateMemoryShape();
            if (state == "recording") {
                AccessibleDescription = "已检测到输入框，继续输入后续识别内容";
                tip.SetToolTip(this, AccessibleDescription);
            }
        } catch (Exception error) { Fail(error.Message); }
    }
    async Task Toggle() {
        if (state == "error") { await StartWorker(); return; }
        if (state == "loading" || state == "busy") return;
        if (state == "idle") {
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if (Native.GetAsyncKeyState(key) < 0) { tip.SetToolTip(this, "请松开修饰键后再开始录音。"); return; }
            target = Native.GetForegroundWindow(); targetFocus = Native.Focus(target);
            try {
                // Preserve the onset while UI Automation and the worker initialize.
                recorder = new Recorder();
                Status("busy", "正在开始录音…");
                bool editable = !memoryTest && Deliverable(await ClassifySafe(target, targetFocus));
                SetInputFocus(editable);
                await BeginStream();
                if (!editable) Pause("没有可确认的输入框，继续识别并保存未上屏文字。");
                if (closing) return;
                guard = new InteractionGuard(OwnsSurface, reason => Pause(reason + "改变了输入位置，正在重新检测输入框。", true));
                listening = true; playEarcon("BasicListeningEarcon", false);
                Status("recording", editable ? "正在录音，停顿后输入 · 点击麦克风结束" : "正在录音，未上屏文字将直接显示为复制按钮 · 右键可结束录音"); limit.Start(); capture.Start();
            } catch (Exception error) { Fail(error.Message); }
            return;
        }
        await EndRecording();
    }
    async Task BeginStream() {
        draft = ""; speechDetected = transcribing = false; tip.Hide(this);
        coarseWindows.Clear();
        stalledWindows.Clear();
        recordingId = Guid.NewGuid().ToString("N"); delivery = new TextDelivery(); sentBytes = 0;
        audioRoute = appliedSegments = 0; backupRoutes.Clear();
        targetEpoch++; nextTargetProbe = 0;
        var reply = await Exchange(new { op = "start", id = recordingId });
        if (Convert.ToString(reply["event"]) != "started") throw new IOException("无法开始流式识别。");
    }
    async Task<Dictionary<string, object>> Exchange(object request) {
        await worker.StandardInput.WriteLineAsync(json.Serialize(request));
        await worker.StandardInput.FlushAsync();
        var reply = await Reply();
        if (reply.ContainsKey("error")) {
            if (delivery != null && reply.ContainsKey("text")) delivery.Latest = Convert.ToString(reply["text"]);
            if (reply.ContainsKey("preview")) draft = Convert.ToString(reply["preview"]);
            throw new IOException(Convert.ToString(reply["error"]));
        }
        if (!reply.ContainsKey("id") || Convert.ToString(reply["id"]) != recordingId) throw new IOException("识别结果编号不匹配，已停止输入。");
        return reply;
    }
    async Task Feed(byte[] audio) {
        int route = audioRoute;
        for (int offset = 0; offset < audio.Length && !closing; offset += 32000) {
            int length = Math.Min(Math.Min(32000, audio.Length - offset), 32000 * 180 - sentBytes);
            if (length <= 0) return;
            var reply = await Exchange(new { op = "audio", id = recordingId, route = route, pcm = Convert.ToBase64String(audio, offset, length) });
            sentBytes += length;
            if (!closing) ApplyReply(reply);
        }
    }
    void ApplyText(string text, bool forceMemory = false) {
        if (delivery == null || closing) return;
        string suffix = delivery.Accept(text);
        if (forceMemory || delivery.Paused.Length != 0) { KeepRecovery(delivery.TakePending()); return; }
        if (suffix.Length == 0 || closing) return;
        if (Native.GetForegroundWindow() != target || targetFocus == IntPtr.Zero || Native.Focus(target) != targetFocus || EditingGestureDown()) {
            Pause("输入位置已变化，继续录音并保存未上屏文字。");
            KeepRecovery(delivery.TakePending()); return;
        }
        var inputs = Native.TextInputs(suffix);
        uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.Input)));
        if (sent != inputs.Length) {
            delivery.Sent(suffix.Substring(0, Math.Min(suffix.Length, (int)((sent + 1) / 2))));
            Pause("文字未完整送出，继续录音并保存剩余文字。");
            KeepRecovery(delivery.TakePending()); return;
        }
        delivery.Sent(suffix);
    }
    void Pause(string reason, bool interaction = false) {
        if (closing || delivery == null) return;
        if (delivery.Paused.Length == 0 || interaction) backupRoutes.Add(audioRoute);
        if (delivery.Paused.Length == 0) { audioRoute++; backupRoutes.Add(audioRoute); }
        if (delivery.Paused.Length == 0 || interaction) { targetEpoch++; nextTargetProbe = motionClock.Elapsed.TotalMilliseconds + 150; }
        delivery.Paused = reason;
        if (inputFocused && (Native.GetForegroundWindow() != target || Native.Focus(target) != targetFocus)) SetInputFocus(false);
        if (state == "recording") {
            AccessibleDescription = reason; tip.SetToolTip(this, reason);
        }
    }
    async Task Pump() {
        if (closing || state != "recording") return;
        if (sentBytes >= 32000 * 180) { await EndRecording(); return; }
        if (inputFocused && (Native.GetForegroundWindow() != target || Native.Focus(target) != targetFocus)) Pause("输入位置已变化，继续录音并保存未上屏文字。");
        if (motionClock.Elapsed.TotalMilliseconds >= nextTargetProbe && (targetProbe == null || targetProbe.IsCompleted)) {
            nextTargetProbe = motionClock.Elapsed.TotalMilliseconds + 250;
            targetProbe = ProbeTarget(IntPtr.Zero);
        }
        if (!await pipeline.WaitAsync(0)) return;
        try {
            if (!closing && state == "recording") {
                byte[] audio = recorder.Read(true);
                if (audio.Length != 0) { level = AudioLevel(audio); await Feed(audio); }
                else ApplyReply(await Exchange(new { op = "poll", id = recordingId }));
            }
        }
        catch (Exception error) { Fail(error.Message); }
        finally { pipeline.Release(); }
    }
    async Task EndRecording(bool silent = false) {
        if (closing || state != "recording") return;
        limit.Stop(); capture.Stop(); Status("busy", "正在补全尾字…");
        try {
            byte[] tail;
            try { tail = recorder.Stop(); } finally { recorder.Dispose(); recorder = null; }
            EndListening(delivery != null && delivery.Cancelled, silent);
            await FinishRecording(tail);
        } catch (Exception error) { Fail(error.Message); }
    }
    async Task FinishRecording(byte[] tail) {
        bool acquired = false;
        try {
            await pipeline.WaitAsync(); acquired = true;
            if (closing || delivery == null || state == "error") return;
            if (delivery.Cancelled) {
                await Exchange(new { op = "cancel", id = recordingId });
                return;
            }
            await Feed(tail);
            var result = await Exchange(new { op = "stop", id = recordingId });
            if (!closing) {
                ApplyReply(result);
            }
        } catch (Exception error) { Fail(error.Message); }
        finally {
            if (acquired) pipeline.Release();
            if (guard != null) { guard.Dispose(); guard = null; }
            if (!closing) {
                if (state != "error") Status("idle", recovery.Length > 0 ? "有未上屏文字 · 点击复制 · 长按开始下一段录音" : "点击说话 · 拖动移动 · 右键退出");
                delivery = null;
            }
        }
    }
    internal static float AudioLevel(byte[] audio) {
        if (audio.Length == 0) return 0;
        double energy = 0;
        for (int i = 0; i + 1 < audio.Length; i += 2) {
            double sample = (short)(audio[i] | audio[i + 1] << 8) / 32768.0;
            energy += sample * sample;
        }
        return (float)Math.Min(1, Math.Sqrt(energy / (audio.Length / 2)) * 24);
    }
    async Task VerifyFallbackBeforeReturn(byte[] audio) {
        using (var window = new Form { Text = "VoxFix fallback without input test", Width = 300, Height = 120, TopMost = true })
        using (var button = new Button { Text = "No input field - test in progress", Dock = DockStyle.Fill }) {
            window.Controls.Add(button); window.Show();
            uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
            bool attached = previous != current && Native.AttachThreadInput(previous, current, true);
            try { Native.SetForegroundWindow(window.Handle); window.Activate(); button.Focus(); }
            finally { if (attached) Native.AttachThreadInput(previous, current, false); }
            target = window.Handle; targetFocus = button.Handle;
            if (await Task.Run(() => HasEditableTarget())) throw new Exception("Non-input window was accepted as editable");
            // Send the production transport's large packet path, not only easy 100ms test packets.
            await Feed(audio);
            for (int i = 0; i < 60 && recovery.Length == 0; i++) {
                await Task.Delay(50); ApplyReply(await Exchange(new { op = "poll", id = recordingId }));
            }
            for (int i = 0; i < 120 && shape.Value < .99; i++) await Task.Delay(16);
            if (Native.GetForegroundWindow() != window.Handle || Native.Focus(window.Handle) != button.Handle)
                throw new Exception("External input interrupted fallback focus test");
            if (state != "recording" || !recovery.Contains("你好") || copiedInTest.Length != 0 || !MemoryPill || shape.Value < .99)
                throw new Exception("Fallback did not recognize and morph before any focus return");
            using (var image = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(image)) {
                DrawCurrent(ink); image.Save(Path.Combine(root, "work", "fallback-before-focus-return.png"));
            }
        }
    }
    async Task RunMemoryTest() {
        await VerifyStartupCapture();
        await BeginStream();
        Status("recording", "兜底记忆测试");
        target = targetFocus = IntPtr.Zero;
        Pause("无输入框测试");
        byte[] source = File.ReadAllBytes(Path.Combine(root, "work", "p0", "wav", "new-short-0.wav"));
        byte[] audio = new byte[source.Length - 44 + 32000]; Array.Copy(source, 44, audio, 0, source.Length - 44);
        await VerifyFallbackBeforeReturn(audio);
        string first = recovery;
        Pause("切换窗口测试");
        for (int offset = 0; offset < audio.Length; offset += 3200) {
            var part = new byte[Math.Min(3200, audio.Length - offset)]; Array.Copy(audio, offset, part, 0, part.Length);
            await Feed(part); await Task.Delay(10);
        }
        await FinishRecording(new byte[0]);
        if (!recovery.StartsWith(first) || recovery.IndexOf("你好", first.Length, StringComparison.Ordinal) < 0 || copiedInTest.Length != 0)
            throw new Exception("Recognition did not continue after focus change");
        CopyRecovery();
        if (!recoveryCopied || copiedInTest != recovery || ClientSize != new Size(128, 96)) throw new Exception("Copy state failed");
        string remembered = recovery;
        await BeginStream();
        delivery.Paused = "下一次录音";
        await FinishRecording(new byte[0]);
        if (recovery != remembered) throw new Exception("New session lost memory");
        var cues = new List<string>(); playEarcon = (name, wait) => cues.Add(name);
        await HoldCopiedPillTest();
        if (cues.Count != 1 || cues[0] != "BasicListeningEarcon") throw new Exception("Idle long press must play only one start cue");
        await Task.Delay(200);
        KeepRecovery("进行中的已复制缓存"); recoveryCopied = true;
        cues.Clear();
        Task restart = NewRecording();
        while (!restart.IsCompleted) {
            if (!MemoryPill || motion.Activity < .95f) throw new Exception("Restart must preserve capsule and listening halo");
            await Task.Delay(1);
        }
        await restart;
        if (cues.Count != 1 || cues[0] != "BasicListeningEarcon") throw new Exception("Recording restart must not play stop/cancel plus start cues");
        if (state != "recording" || recovery.Length != 0 || recoveryCopied || draft.Length != 0 || !MemoryPill) throw new Exception("Long press did not reset memory and start in no-input capsule mode");
        await pipeline.WaitAsync();
        try { await Feed(recorder.Split()); }
        finally { pipeline.Release(); }
        await Task.Delay(120);
        copiedInTest = "";
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 64, 48, 0));
        if (copiedInTest.Length != 0 || state != "recording") throw new Exception("Long press release copied text or stopped recording");
        KeepRecovery("本轮暂存");
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
        if (state != "recording" || copiedInTest != recovery) throw new Exception("Copy button must copy without stopping");
        copiedInTest = "";
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 64, 48, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 64, 48, 0));
        for (int i = 0; i < 150 && state == "busy"; i++) await Task.Delay(20);
        if (state != "idle" || copiedInTest.Length != 0 || recovery.Contains(remembered)) throw new Exception("Short click after long press did not stop or old memory returned");
        CopyRecovery();
        Fail("测试已复制状态下识别服务恢复");
        await HoldCopiedPillTest();
        if (recovery.Length != 0 || recoveryCopied || draft.Length != 0 || state != "recording") throw new Exception("Error-state long press failed to create a clean round");
        await EndRecording();
        await RunFocusRecoveryTest();
        File.WriteAllText(Path.Combine(root, "work", "microphone-memory-test.txt"), "PASS: " + Assembly.GetExecutingAssembly().GetName().Version + " / stale foreign-window UIA rejected / resume stays capsule while audio boundary is pending / repeated non-input probes remain capsule / real SenseVoice / focus return shows circle with old cache retained / actual non-input window / large audio packet / recording continues / manual copy / native mouse long press in idle and error states / service recovery / clears old memory / release does not copy or stop / next click stops / stored gap never replayed / new editable field / read-only fallback");
        Close();
    }
    async Task VerifyStartupCapture() {
        var clock = Stopwatch.StartNew();
        Task starting = Toggle();
        bool armedBeforeWaiting = recorder != null;
        await starting;
        if (state != "recording" || recorder == null) throw new Exception("Startup did not reach recording");
        capture.Stop(); limit.Stop();
        await Task.Delay(200);
        int bytes;
        try { bytes = recorder.Stop().Length; } finally { recorder.Dispose(); recorder = null; }
        EndListening(false, true);
        await Exchange(new { op = "cancel", id = recordingId });
        if (guard != null) { guard.Dispose(); guard = null; }
        delivery = null; recovery = draft = ""; Status("idle", "启动采集测试完成");
        if (!armedBeforeWaiting) throw new Exception("Initial speech is unprotected: microphone opens only after asynchronous setup");
        if (bytes < 3200) throw new Exception("Startup capture did not retain its initial microphone buffers");
        File.WriteAllText(Path.Combine(root, "work", "microphone-startup-test.txt"), "PASS: capture armed before async setup; initial PCM retained=" + bytes + "; observation ms=" + clock.ElapsedMilliseconds + "; audio discarded in memory");
    }
    async Task HoldCopiedPillTest() {
        string previousId = recordingId;
        Point previousPointer = Cursor.Position;
        bool keepCapsule = MemoryPill, keepListening = state == "recording";
        var area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Left + 40, area.Top + 100);
        RefreshSurface(); await Task.Delay(80);
        Native.SetCursorPos(Left + 88, Top + 48);
        var mouseDown = new[] { new Native.Input { type = 0, data = new Native.Payload { mouse = new Native.Mouse { flags = 2 } } } };
        var mouseUp = new[] { new Native.Input { type = 0, data = new Native.Payload { mouse = new Native.Mouse { flags = 4 } } } };
        try {
            if (Native.WindowFromPoint(Cursor.Position) != Handle) throw new Exception("Gesture test's mouse target is not its own window");
            Native.SendInput(1, mouseDown, Marshal.SizeOf(typeof(Native.Input)));
            for (int i = 0; i < 75 && (state != "recording" || recordingId == previousId); i++) {
                await Task.Delay(20);
                if (keepCapsule && !MemoryPill) throw new Exception("Long press restart flashed from capsule to circle");
                if (keepListening && motion.Activity < .95f) throw new Exception("Long press restart flashed listening halo off");
            }
            if (state != "recording" || recordingId == previousId)
                throw new Exception("Native copied-pill long press failed: pressed=" + pressed + "; held=" + heldTriggered + "; dragged=" + dragged + "; capture=" + Capture);
        } finally {
            Native.SendInput(1, mouseUp, Marshal.SizeOf(typeof(Native.Input)));
            Native.SetCursorPos(previousPointer.X, previousPointer.Y);
        }
        await Task.Delay(30);
    }
    async Task<bool> WaitShape(bool pill, int milliseconds = 3000) {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) {
            await Task.Delay(50);
            if (MemoryPill == pill && shape.Settled && Math.Abs(shape.Value - (pill ? 1 : 0)) < .01) return true;
        }
        return false;
    }
    // Idle morph: moving the caret in and out of a real input box must animate the
    // circle into the capsule and back, without any recording in progress.
    async Task RunIdleShapeTest() {
        using (var editor = new Form { Text = "VoxFix idle shape test", Width = 320, Height = 160, TopMost = true })
        using (var other = new Form { Text = "VoxFix idle no input test", Width = 220, Height = 120, TopMost = true })
        using (var field = new TextBox { Dock = DockStyle.Fill })
        using (var button = new Button { Text = "No input here", Dock = DockStyle.Fill }) {
            editor.Controls.Add(field); other.Controls.Add(button);
            other.Show(); editor.Show();
            Action<Form, Control> focus = (form, control) => {
                uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                bool attached = foreground != current && Native.AttachThreadInput(current, foreground, true);
                try { Native.SetForegroundWindow(form.Handle); form.Activate(); control.Focus(); }
                finally { if (attached) Native.AttachThreadInput(current, foreground, false); }
            };
            delivery = null; recovery = draft = ""; idleWindow = idleFocus = IntPtr.Zero; idleAttempts = 0;
            Status("idle", "空闲形变测试"); focusWatch.Start();
            focus(editor, field);
            if (!await WaitShape(false)) throw new Exception("Idle circle did not appear for a text field");
            focus(other, button);
            if (!await WaitShape(true)) throw new Exception("Idle capsule did not appear outside a text field");
            focus(editor, field);
            if (!await WaitShape(false)) throw new Exception("Idle shape did not return to the circle");
            recovery = "等待复制的文字"; UpdateMemoryShape();
            focus(other, button);
            await Task.Delay(600);
            if (!MemoryPill || !AccessibleName.Contains("复制")) throw new Exception("Cached text must pin the copy capsule regardless of focus");
        }
    }
    async Task RunFocusRecoveryTest() {
        using (var editor = new Form { Text = "VoxFix focus recovery test", Width = 320, Height = 160, TopMost = true })
        using (var other = new Form { Text = "VoxFix no input test", Width = 200, Height = 100, TopMost = true })
        using (var first = new TextBox { Dock = DockStyle.Top })
        using (var second = new TextBox { Dock = DockStyle.Bottom })
        using (var button = new Button { Text = "No editable target", Dock = DockStyle.Fill }) {
            editor.Controls.Add(first); editor.Controls.Add(second); other.Controls.Add(button);
            other.Show(); editor.Show();
            Action<Form, Control> focus = (form, field) => {
                uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                bool attached = foreground != current && Native.AttachThreadInput(current, foreground, true);
                try { Native.SetForegroundWindow(form.Handle); form.Activate(); field.Focus(); }
                finally { if (attached) Native.AttachThreadInput(current, foreground, false); }
            };
            recovery = ""; delivery = new TextDelivery { Paused = "开始于无输入框" }; Status("recording", "输入框恢复测试");
            focus(editor, first);
            if (await Task.Run(() => HasEditableTarget(other.Handle, button.Handle))) throw new Exception("Stale UIA focus from another window was accepted");
            SetInputFocus(false);
            await pipeline.WaitAsync();
            Task resume = ProbeTarget(editor.Handle);
            try {
                await Task.Delay(350);
                if (inputFocused || !MemoryPill) throw new Exception("Shape resumed before audio boundary and target were committed");
            } finally { pipeline.Release(); }
            await resume;
            if (delivery.Paused.Length != 0 || MemoryPill) throw new Exception("Editable field must resume and show circle");
            ApplyText("A"); await Task.Delay(30);
            if (first.Text != "A") throw new Exception("Initial field injection failed");
            focus(other, button); await ProbeTarget(other.Handle);
            if (!MemoryPill) throw new Exception("Losing input focus must show capsule before any recognized text");
            for (int i = 0; i < 6; i++) {
                await ProbeTarget(other.Handle); await Task.Delay(40);
                if (!MemoryPill || inputFocused) throw new Exception("Non-input focus flickered back to circle");
            }
            ApplyText("AB"); await Task.Delay(30);
            if (recovery != "B" || first.Text != "A") throw new Exception("Non-input window did not enter fallback");
            focus(editor, first); first.SelectionStart = first.TextLength; await ProbeTarget(editor.Handle); ApplyText("ABC"); await Task.Delay(30);
            if (first.Text != "AC" || recovery != "B" || delivery.Paused.Length != 0 || MemoryPill)
                throw new Exception("Focus return replayed stored text or failed to resume");
            focus(editor, second); await ProbeTarget(editor.Handle); ApplyText("ABCD"); await Task.Delay(30);
            if (second.Text != "D" || first.Text != "AC" || recovery != "B") throw new Exception("New input field was not adopted");
            second.ReadOnly = true; await ProbeTarget(editor.Handle); ApplyText("ABCDE"); await Task.Delay(30);
            if (second.Text != "D" || !recovery.EndsWith("E")) throw new Exception("Read-only field accepted text");
            second.ReadOnly = false; second.SelectionStart = second.TextLength; await ProbeTarget(editor.Handle); ApplyText("ABCDEF"); await Task.Delay(30);
            if (second.Text != "DF" || recovery.Contains("F")) throw new Exception("Read-only recovery did not resume only new text");
            appliedSegments = 0; backupRoutes.Add(100);
            var delayed = new Dictionary<string, object> {
                { "text", "ABCDEFTN" },
                { "segments", new object[] {
                    new Dictionary<string, object> { { "route", 100 }, { "text", "T" } },
                    new Dictionary<string, object> { { "route", 101 }, { "text", "N" } }
                } }
            };
            ApplyReply(delayed); await Task.Delay(30);
            ApplyReply(delayed); await Task.Delay(30);
            if (second.Text != "DFN" || !recovery.EndsWith("T") || MemoryPill)
                throw new Exception("Late old audio escaped fallback or copy control disappeared");
            delivery = null; Status("idle", "输入框恢复测试通过");
        }
    }
    async Task RunStreamTest() {
        using (var form = new Form { Text = "VoxFix Desktop test target", Width = 500, Height = 200, TopMost = true })
        using (var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "原文：" }) {
            form.Controls.Add(box); form.Show(); form.Activate(); Native.SetForegroundWindow(form.Handle); box.Focus(); box.SelectionStart = box.TextLength;
            // Test harness only: foreground-lock rules otherwise prevent an unattended test window from receiving input.
            uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
            uint testThread = Native.GetCurrentThreadId();
            bool attached = foregroundThread != testThread && Native.AttachThreadInput(testThread, foregroundThread, true);
            try { Native.SetForegroundWindow(form.Handle); box.Focus(); }
            finally { if (attached) Native.AttachThreadInput(testThread, foregroundThread, false); }
            await Task.Delay(100);
            target = Native.GetForegroundWindow(); targetFocus = Native.Focus(target);
            if (target != form.Handle || targetFocus != box.Handle) throw new Exception("Test target did not receive focus: desktop=" + Native.DesktopName() + "; foreground=" + target + "; expected=" + form.Handle + "; focus=" + targetFocus + "; expectedFocus=" + box.Handle + "; managedFocus=" + box.Focused);
            if (!await Task.Run(() => HasEditableTarget())) throw new Exception("Editable field was not detected");
            box.ReadOnly = true;
            if (await Task.Run(() => HasEditableTarget())) throw new Exception("Read-only field accepted as editable");
            box.ReadOnly = false;
            await BeginStream(); Status("busy", "流式输入测试");
            guard = new InteractionGuard(OwnsSurface, reason => Pause("Test user input: " + reason));
            byte[] source = File.ReadAllBytes(Path.Combine(root, "work", "p0", "wav", "new-short-0.wav"));
            // The real-time long-session contract is tested over the worker protocol;
            // this desktop test needs one endpoint and actual Unicode delivery.
            byte[] wav = new byte[source.Length + 32000]; Array.Copy(source, wav, source.Length);
            var clock = Stopwatch.StartNew(); int first = -1;
            for (int offset = 44; offset < wav.Length; offset += 3200) {
                int length = Math.Min(3200, wav.Length - offset); var part = new byte[length]; Array.Copy(wav, offset, part, 0, length);
                await Feed(part);
                if (delivery.Delivered.Length > 0 && first < 0) first = (int)clock.ElapsedMilliseconds;
                await Task.Delay(Math.Max(1, (int)((offset - 44 + length) / 32) - (int)clock.ElapsedMilliseconds));
            }
            if (first < 0 || first >= clock.ElapsedMilliseconds) throw new Exception("No text before stop: latest=" + delivery.Latest + "; paused=" + delivery.Paused + "; foreground=" + Native.GetForegroundWindow() + "; target=" + target + "; focus=" + Native.Focus(target) + "; expectedFocus=" + targetFocus);
            if (!delivery.Delivered.Contains("你好")) throw new Exception("Endpoint text incomplete: delivered=" + delivery.Delivered.Length + "; latest=" + delivery.Latest.Length + "; paused=" + delivery.Paused);
            var final = await Exchange(new { op = "stop", id = recordingId }); ApplyText(Convert.ToString(final["text"]));
            await Task.Delay(100);
            if (!box.Text.Contains("你好") || box.Text != "原文：" + delivery.Delivered || delivery.Paused.Length != 0) throw new Exception("Streaming injection/guard failed: " + box.Text + " / " + delivery.Paused);
            string original = box.Text;
            ApplyText(delivery.Latest); await Task.Delay(50);
            if (box.Text != original) throw new Exception("Final result was duplicated");
            var input = Native.TextInputs("Z");
            for (int i = 0; i < input.Length; i++) input[i].data.keyboard.extra = UIntPtr.Zero;
            Native.SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Native.Input))); await Task.Delay(50);
            if (delivery.Paused.Length == 0) throw new Exception("User edits did not pause injection");
            original = box.Text; ApplyText(delivery.Latest + "不得输入"); await Task.Delay(50);
            if (box.Text != original) throw new Exception("Input continued after user editing");
            delivery = new TextDelivery(); targetFocus = IntPtr.Zero;
            ApplyText("不得输入"); await Task.Delay(50);
            if (box.Text != original || delivery.Paused.Length == 0) throw new Exception("Focus guard failed");
            guard.Dispose(); guard = null;
            recovery = ""; copiedInTest = "";
            await BeginStream();
            delivery.Paused = "输入位置变化测试";
            byte[] shortWav = File.ReadAllBytes(Path.Combine(root, "work", "p0", "wav", "new-short-0.wav"));
            for (int offset = 44; offset < shortWav.Length; offset += 3200) {
                int length = Math.Min(3200, shortWav.Length - offset);
                var part = new byte[length]; Array.Copy(shortWav, offset, part, 0, length);
                await Feed(part);
            }
            await FinishRecording(new byte[0]);
            if (!recovery.Contains("你好") || recoveryCopied || copiedInTest.Length != 0 || box.Text != original)
                throw new Exception("Undelivered text recovery failed");
            if (Application.OpenForms.Count != 2) throw new Exception("Unexpected transcript window");
            copyText = text => { throw new ExternalException("Clipboard busy"); };
            CopyRecovery();
            if (recoveryCopied || !recovery.Contains("你好")) throw new Exception("Clipboard failure lost recovery text");
            copyText = text => copiedInTest = text;
            CopyRecovery();
            if (!recoveryCopied || copiedInTest != recovery) throw new Exception("Clipboard retry failed");
            string recoveryFile = Path.Combine(root, "work", "未输入的文字.txt");
            byte[] recoveryBefore = File.Exists(recoveryFile) ? File.ReadAllBytes(recoveryFile) : new byte[0];
            string remembered = recovery;
            await BeginStream();
            if (recovery != remembered) throw new Exception("New recording cleared memory");
            copiedInTest = "";
            delivery.Paused = "取消测试";
            delivery.Cancelled = true;
            await FinishRecording(new byte[1280]);
            if (delivery != null || state != "idle" || copiedInTest.Length > 0 || recovery != remembered) throw new Exception("Cancellation changed memory or clipboard");
            byte[] recoveryAfter = File.Exists(recoveryFile) ? File.ReadAllBytes(recoveryFile) : new byte[0];
            if (Convert.ToBase64String(recoveryBefore) != Convert.ToBase64String(recoveryAfter)) throw new Exception("Cancelled text was saved");
            await BeginStream();
            await FinishRecording(new byte[0]);
            if (delivery != null || state != "idle") throw new Exception("New session after cancellation failed");
            File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-stream-test.txt"), "PASS: paced text before stop / real SenseVoice / real SendInput / existing text preserved / no final duplication / user edit guard / focus guard / manual memory copy (test sink) / retain across sessions / append only undelivered text / focus changes keep recording / clipboard busy retry / cancellation never copies / restart after cancellation / no transcript window");
        }
        Close();
    }
    void RefreshSurface() {
        if (closing || !Visible || !IsHandleCreated) return;
        using (var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
            using (var ink = Graphics.FromImage(bitmap)) DrawCurrent(ink);
            Native.Present(Handle, Location, bitmap);
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        DrawCurrent(e.Graphics);
    }
    void DrawCurrent(Graphics g) {
        DrawShape(g, shape.Value, restarting ? "recording" : state, recoveryCopied, copyFeedback, hoverFeedback, pressFeedback,
            motion.Activity, motion.Voice, motion.Level, animationsEnabled ? motionClock.Elapsed.TotalMilliseconds : 0,
            restarting || (state == "recording" && !inputFocused && recovery.Length == 0),
            !restarting && (transcribing || speechDetected || state == "busy"), readyFeedback,
            // A capsule with nothing to copy yet: idle, focus outside any input.
            MemoryPill && recovery.Length == 0 && !transcribing && !restarting);
    }
    internal static SizeF ShapeSize(double shape) {
        double ratio = 1 + (106.0 / 42 - 1) * shape;
        double minor = Math.Sqrt(Math.PI * 36 * 36 / (Math.Max(ratio, 1 / ratio) - 1 + Math.PI / 4));
        return ratio >= 1 ? new SizeF((float)(minor * ratio), (float)minor) : new SizeF((float)minor, (float)(minor / ratio));
    }
    internal static void DrawShape(Graphics g, double shape, string state, bool copied, float success, float hover, float press,
        float activity = 0, float voice = 0, float level = 0, double now = 0, bool pending = false, bool transcribing = false, float ready = 1, bool stashed = false) {
        g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
        // A capsule keeps the circle's area, including spring overshoot and reversal.
        SizeF size = ShapeSize(shape);
        float scale = 1 - .025f * press;
        float width = size.Width * scale, height = size.Height * scale;
        float x = 64 - width / 2, y = 48 - height / 2;
        float blend = (float)Math.Max(0, Math.Min(1, shape));
        using (var outline = new GraphicsPath()) {
            float diameter = Math.Min(width, height);
            if (width >= height) {
                outline.AddArc(x + .5f, y + .5f, diameter - 1, diameter - 1, 90, 180);
                outline.AddArc(x + width - diameter + .5f, y + .5f, diameter - 1, diameter - 1, 270, 180);
            } else {
                outline.AddArc(x + .5f, y + .5f, diameter - 1, diameter - 1, 180, 180);
                outline.AddArc(x + .5f, y + height - diameter + .5f, diameter - 1, diameter - 1, 0, 180);
            }
            outline.CloseFigure();
            using (var surface = new SolidBrush(Color.FromArgb(101, 104, 104))) g.FillPath(surface, outline);
            int shade = (int)(115 + hover * 8 - press * 6);
            using (var sheen = new LinearGradientBrush(new PointF(0, y), new PointF(0, y + height),
                Color.FromArgb((int)(255 * blend), shade + 5, shade + 7, shade + 7),
                Color.FromArgb((int)(255 * blend), shade, shade + 2, shade + 2))) g.FillPath(sheen, outline);
            using (var rim = new Pen(Color.FromArgb((int)((45 + hover * 20) * blend), 235, 238, 237), 1)) g.DrawPath(rim, outline);
            var saved = g.Save(); g.SetClip(outline);
            if (blend > 0) using (var pill = new Bitmap(106, 42)) using (var ink = Graphics.FromImage(pill)) {
                DrawMemory(ink, copied, success, hover, press, false, activity, level, now, state == "busy" || state == "loading", pending, voice, transcribing, ready, false, stashed);
                DrawFade(g, pill, 11, 27, blend);
            }
            var motif = g.Save();
            float motifSize = 72 - 30 * blend;
            g.TranslateTransform(64 - 29 * blend - motifSize / 2, 48 - motifSize / 2);
            g.ScaleTransform(motifSize / 72, motifSize / 72);
            using (var circle = new Bitmap(72, 72)) using (var ink = Graphics.FromImage(circle)) {
                DrawListening(ink, state, activity, voice, level, now, false, 1 + .6f * blend);
                DrawFade(g, circle, 0, 0, 1 - blend * (1 - activity));
            }
            g.Restore(motif);
            g.Restore(saved);
        }
    }
    static void DrawFade(Graphics g, Bitmap image, int x, int y, float opacity) {
        if (opacity <= 0) return;
        using (var attributes = new System.Drawing.Imaging.ImageAttributes()) {
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
            attributes.SetColorMatrix(matrix);
            g.DrawImage(image, new Rectangle(x, y, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
    }
    internal static void DrawMemory(Graphics g, bool copied, float success = -1, float hover = 0, float press = 0, bool backdrop = true, float activity = 0, float level = 0, double now = 0, bool processing = false, bool pending = false, float voice = 0, bool transcribing = false, float ready = 1, bool listeningMotif = true, bool stashed = false) {
        if (success < 0) success = copied ? 1 : 0;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (backdrop) {
            g.Clear(Color.Transparent);
            using (var surface = new SolidBrush(Color.FromArgb(117 + (int)(hover * 8), 120 + (int)(hover * 8), 120 + (int)(hover * 8)))) {
                using (var capsule = new GraphicsPath()) {
                    capsule.AddArc(.5f, .5f, 41, 41, 90, 180); capsule.AddArc(64.5f, .5f, 41, 41, 270, 180); capsule.CloseFigure();
                    g.FillPath(surface, capsule);
                    using (var rim = new Pen(Color.FromArgb(55, 235, 238, 237), 1)) g.DrawPath(rim, capsule);
                }
            }
        }
        using (var well = new SolidBrush(Color.FromArgb((int)(255 * (1 - activity)), 101, 104, 104))) g.FillEllipse(well, 8, 5, 32, 32);
        var icon = g.Save();
        g.TranslateTransform(2.4f, -.6f); g.ScaleTransform(1.2f, 1.2f);
        using (var line = new Pen(Color.FromArgb((int)(232 * (1 - success) * (1 - activity)), 232, 234, 233), 1.5f)) {
            line.LineJoin = LineJoin.Round; line.StartCap = line.EndCap = LineCap.Round;
            g.DrawLines(line, new[] { new PointF(14, 12), new PointF(12, 12), new PointF(12, 22), new PointF(14, 22) });
            using (var page = new GraphicsPath()) {
                page.AddArc(15, 14, 3, 3, 180, 90); page.AddArc(20, 14, 3, 3, 270, 90);
                page.AddArc(20, 22, 3, 3, 0, 90); page.AddArc(15, 22, 3, 3, 90, 90); page.CloseFigure(); g.DrawPath(line, page);
            }
        }
        using (var line = new Pen(Color.FromArgb((int)(255 * success * (1 - activity)), 220, 236, 226), 1.8f)) {
            line.StartCap = line.EndCap = LineCap.Round; line.LineJoin = LineJoin.Round;
            g.DrawLines(line, new[] { new PointF(12.5f, 18), new PointF(16.5f, 22), new PointF(23.5f, 14.5f) });
        }
        g.Restore(icon);
        if (listeningMotif && activity > .001f) {
            var motif = g.Save(); g.TranslateTransform(3, 0); g.ScaleTransform(7f / 12, 7f / 12);
            DrawListening(g, processing ? "busy" : "recording", activity, voice, level, now, false, 1.6f); g.Restore(motif);
        }
        using (var divider = new Pen(Color.FromArgb(38, 240, 243, 241), 1)) g.DrawLine(divider, 48, 10, 48, 32);
        float leaving = Math.Max(0, 1 - 2 * success), entering = Math.Max(0, 2 * success - 1);
        using (var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (transcribing || pending) {
                using (var ink = new SolidBrush(Color.FromArgb(185, 239, 241, 240))) g.DrawString(transcribing ? "转写中" : "监听中", font, ink, new RectangleF(51, 2, 48, 35), format);
                if (transcribing) {
                    using (var track = new Pen(Color.FromArgb(35, 240, 243, 241), 2)) g.DrawLine(track, 59, 33, 89, 33);
                    float travel = now == 0 ? .5f : (float)((now % 1000) / 1000);
                    using (var progress = new Pen(Color.FromArgb(150, 240, 243, 241), 2)) { progress.StartCap = progress.EndCap = LineCap.Round; g.DrawLine(progress, 59 + 20 * travel, 33, 69 + 20 * travel, 33); }
                }
            } else if (stashed) {
                // Nothing cached and no deliverable focus: say where the words will go.
                using (var ink = new SolidBrush(Color.FromArgb(150, 239, 241, 240))) g.DrawString("缓存", font, ink, new RectangleF(51, 2, 48, 35), format);
                using (var line = new Pen(Color.FromArgb(45, 240, 243, 241), 2)) { line.StartCap = line.EndCap = LineCap.Round; g.DrawLine(line, 62, 34, 86, 34); }
            } else {
                int alpha = (int)(150 + 105 * ready);
                using (var before = new SolidBrush(Color.FromArgb((int)(alpha * leaving), 239, 241, 240))) g.DrawString("复制", font, before, new RectangleF(51, 3 - 3 * (1 - leaving), 47, 35), format);
                using (var after = new SolidBrush(Color.FromArgb((int)(alpha * entering), 239, 241, 240))) g.DrawString("已复制", font, after, new RectangleF(51, 3 + 3 * (1 - entering), 47, 35), format);
                using (var line = new Pen(Color.FromArgb((int)(70 * ready), 240, 243, 241), 2)) { line.StartCap = line.EndCap = LineCap.Round; g.DrawLine(line, 62, 34, 86, 34); }
            }
        }
    }
    static void DrawVoice(Graphics g, float cx, float cy, float opacity, float level, double now, bool processing, Color color) {
        using (var pen = new Pen(Color.FromArgb((int)(255 * opacity), color), 1.8f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            for (int i = 0; i < 5; i++) {
                float pulse = now == 0 ? .6f : (float)(.5 + .5 * Math.Sin(now * Math.PI * 2 / 467 - i * .65));
                float envelope = 1 - Math.Abs(i - 2) * .18f;
                float height = processing ? 0 : (6 + level * 26) * (.45f + .55f * pulse) * envelope;
                float x = cx + (i - 2) * 4.2f;
                g.DrawLine(pen, x, cy - height / 2, x, cy + height / 2 + .01f);
            }
        }
    }
    internal static void Draw(Graphics g, string state, float level = 0, int frame = 0) {
        DrawListening(g, state, state == "recording" || state == "busy" ? 1 : 0,
            state == "recording" && level >= .02f ? 1 : 0, level, frame * 33);
    }
    internal static void DrawListening(Graphics g, string state, float activity, float voice, float level, double milliseconds, bool backdrop = true, float glyphScale = 1) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (backdrop) {
            g.Clear(Color.Transparent);
            using (var surface = new SolidBrush(Color.FromArgb(101, 104, 104))) g.FillEllipse(surface, .5f, .5f, 71, 71);
        }
        float breath = milliseconds == 0 ? 0 : (float)(.5 + .5 * Math.Sin(milliseconds * Math.PI / 800));
        for (int i = 7; i >= 0; i--) {
            float diameter = 48 + i * 1.5f + (breath * 6 + level * 3) * activity;
            using (var halo = new SolidBrush(Color.FromArgb((int)(activity * (8 - i) * 2), 220, 222, 221)))
                g.FillEllipse(halo, 36 - diameter / 2, 36 - diameter / 2, diameter, diameter);
        }
        for (int i = 0; i < 2; i++) {
            float phase = milliseconds == 0 ? i * .5f : (float)((milliseconds / 1400 + i * .5) % 1);
            float diameter = 49 + 20 * phase;
            using (var ring = new Pen(Color.FromArgb((int)(70 * activity * (1 - phase)), 237, 240, 239), 1.6f))
                g.DrawEllipse(ring, 36 - diameter / 2, 36 - diameter / 2, diameter, diameter);
        }
        int disc = (int)(112 + activity * (76 + 20 * breath));
        float size = 44 + activity * 4;
        using (var fill = new SolidBrush(Color.FromArgb(disc, disc + 2, disc + 2)))
            g.FillEllipse(fill, 36 - size / 2, 36 - size / 2, size, size);
        if (state == "error") using (var error = new Pen(Color.FromArgb(211, 156, 73), 2)) g.DrawEllipse(error, 10, 10, 52, 52);
        bool processing = state == "busy" || state == "loading";
        float bars = processing ? 1 : voice;
        int inkValue = (int)(232 * (1 - activity));
        Color inkColor = Color.FromArgb(inkValue, inkValue, inkValue);
        var glyph = g.Save();
        g.TranslateTransform(36, 36); g.ScaleTransform(glyphScale, glyphScale); g.TranslateTransform(-36, -36);
        float barOpacity = Math.Max(0, bars * 2 - 1);
        if (barOpacity > .001f) DrawVoice(g, 36, 36, barOpacity, level, milliseconds, processing, inkColor);
        float microphone = Math.Max(0, 1 - bars * 2);
        if (microphone <= .001f) { g.Restore(glyph); return; }
        var saved = g.Save();
        float scale = .85f + .15f * (1 - bars);
        g.TranslateTransform(36, 36); g.ScaleTransform(scale, scale); g.TranslateTransform(-36, -36);
        using (var ink = new SolidBrush(Color.FromArgb((int)(255 * microphone), inkColor)))
        using (var pen = new Pen(Color.FromArgb((int)(255 * microphone), inkColor), 1.7f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.FillEllipse(ink, 33, 27, 6, 6); g.FillRectangle(ink, 33, 30, 6, 6); g.FillEllipse(ink, 33, 33, 6, 6);
            g.DrawArc(pen, 30, 31, 12, 12, 0, 180); g.DrawLine(pen, 36, 43, 36, 46);
        }
        g.Restore(saved);
        g.Restore(glyph);
    }
    // Diagnostic: report how a target window is classified without dictating.
    // --target-probe [process] [--type text]
    static int RunTargetProbe(string[] args, string root) {
        IntPtr previous = Native.GetForegroundWindow(), window = previous;
        string filter = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "";
        string typed = "";
        for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--type") typed = args[i + 1];
        if (filter.Length > 0) {
            foreach (var process in Process.GetProcessesByName(filter)) {
                if (process.MainWindowHandle != IntPtr.Zero) { window = process.MainWindowHandle; break; }
            }
        }
        Thread.Sleep(120);
        IntPtr foreground = Native.GetForegroundWindow();
        IntPtr focus = Native.Focus(window);
        var cache = new Dictionary<IntPtr, bool>();
        bool coarse = WindowIsCoarse(window, cache);
        // Reading the focused element of a coarse host can stall indefinitely, and it
        // describes the window rather than the caret anyway.
        string elementInfo = "skipped (coarse host)", patternInfo = "";
        if (!coarse) {
            var element = System.Windows.Automation.AutomationElement.FocusedElement;
            if (element != null) {
                try {
                    var current = element.Current;
                    elementInfo = current.ControlType.ProgrammaticName.Replace("ControlType.", "") + "|class=" + current.ClassName
                        + "|hwnd=0x" + ((long)current.NativeWindowHandle).ToString("X") + "|focus=" + current.HasKeyboardFocus
                        + "|focusable=" + current.IsKeyboardFocusable + "|enabled=" + current.IsEnabled
                        + "|name=" + (current.Name.Length > 24 ? current.Name.Substring(0, 24) : current.Name);
                    object value;
                    if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out value))
                        patternInfo += "value(readOnly=" + ((System.Windows.Automation.ValuePattern)value).Current.IsReadOnly + ") ";
                    if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out value)) {
                        object readOnly = ((System.Windows.Automation.TextPattern)value).DocumentRange
                            .GetAttributeValue(System.Windows.Automation.TextPattern.IsReadOnlyAttribute);
                        patternInfo += "text(readOnly=" + readOnly + ") ";
                    }
                    if (patternInfo.Length == 0) patternInfo = "no value/text pattern";
                } catch { elementInfo = "unreadable"; }
            }
        }
        var lines = new List<string>();
        lines.Add("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("window=0x" + ((long)window).ToString("X") + " class=" + Native.ClassOf(window) + " title=" + Native.TitleOf(window));
        lines.Add("foreground=0x" + ((long)foreground).ToString("X") + " active=" + (foreground == window) + " focus=0x" + ((long)focus).ToString("X"));
        lines.Add("uia: coarse=" + coarse + " focused=" + elementInfo + " patterns=" + patternInfo);
        var kind = Classify(window, focus, IntPtr.Zero, cache);
        lines.Add("kind=" + kind + " deliver=" + Deliverable(kind) + (foreground == window ? "" : " (projected: window is not active, silence is what matters)"));
        if (typed.Length > 0 && foreground == window) {
            var inputs = Native.TextInputs(typed);
            uint sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.Input)));
            lines.Add("typed='" + typed + "' inputs=" + inputs.Length + " sent=" + sent
                + " foregroundKept=" + (Native.GetForegroundWindow() == window));
        } else if (typed.Length > 0) lines.Add("typed=skipped (window is not active)");
        lines.Add("");
        Directory.CreateDirectory(Path.Combine(root, "work"));
        File.AppendAllText(Path.Combine(root, "work", "target-probe.txt"), String.Join("\r\n", lines.ToArray()) + "\r\n");
        return 0;
    }
    [STAThread] static void Main(string[] args) {
        Native.SetProcessDPIAware();
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        if (args.Length > 0 && args[0] == "--capture-test") {
            try {
                var counts = new List<int>();
                foreach (int duration in new[] { 250, 450, 650 }) {
                    using (var recorder = new Recorder()) {
                        int count = 0; var clock = Stopwatch.StartNew();
                        while (clock.ElapsedMilliseconds < duration) { Thread.Sleep(20); count += recorder.Read(true).Length; }
                        count += recorder.Stop().Length;
                        if (count % 2 != 0 || count < (duration - 150) * 32 || count > (duration + 200) * 32) throw new Exception("Capture length mismatch: " + count);
                        counts.Add(count);
                    }
                }
                File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-capture-test.txt"), "PASS: real default microphone / continuous buffer reads / stop tail drain / three reopen cycles; PCM byte counts=" + String.Join(",", counts));
            } catch (Exception error) {
                File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-test-failure.txt"), error.ToString()); Environment.ExitCode = 1;
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--export-icon") {
            Directory.CreateDirectory(Path.Combine(root, "app", "assets"));
            using (var bitmap = new Bitmap(72, 72)) using (var g = Graphics.FromImage(bitmap)) {
                g.Clear(Color.Transparent);
                Draw(g, "idle");
                IntPtr handle = bitmap.GetHicon();
                try { using (var icon = Icon.FromHandle(handle)) using (var output = File.Create(Path.Combine(root, "app", "assets", "VoxFix Desktop.ico"))) icon.Save(output); }
                finally { Native.DestroyIcon(handle); }
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--target-probe") { RunTargetProbe(args, root); return; }
        if (args.Length > 0 && args[0] == "--idle-focus-test") {
            Directory.CreateDirectory(Path.Combine(root, "work"));
            using (var shell = new Microphone(Path.Combine(root, ".tmp", Guid.NewGuid().ToString("N")), "--idle-focus-test")) {
                shell.Show();
                var context = new ApplicationContext();
                var timer = new System.Windows.Forms.Timer { Interval = 40 };
                timer.Tick += async (s, e) => {
                    timer.Stop();
                    try {
                        await shell.RunIdleShapeTest();
                        File.WriteAllText(Path.Combine(root, "work", "microphone-idle-shape-test.txt"),
                            "PASS: idle circle inside a text field / idle capsule outside it / animated morph both ways / cached text pins the copy capsule");
                    } catch (Exception error) {
                        File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-test-failure.txt"), error.ToString());
                        Environment.ExitCode = 1;
                    } finally { timer.Dispose(); context.ExitThread(); }
                };
                timer.Start();
                Application.Run(context);
            }
            return;
        }
        // Focus gate only: no microphone, no model. Used to prove the delivery decision.
        if (args.Length > 0 && args[0] == "--focus-test") {
            Directory.CreateDirectory(Path.Combine(root, "work"));
            // The form installs a WinForms synchronization context, so the async test needs
            // a running message loop to resume on; blocking on the thread would deadlock.
            using (var shell = new Microphone(Path.Combine(root, ".tmp", Guid.NewGuid().ToString("N")), "--focus-test")) {
                // Showing the overlay is what lets this process take the foreground back
                // for its own test windows; the window itself never activates.
                shell.Show();
                var context = new ApplicationContext();
                var timer = new System.Windows.Forms.Timer { Interval = 40 };
                timer.Tick += async (s, e) => {
                    timer.Stop();
                    try {
                        await shell.RunFocusRecoveryTest();
                        File.WriteAllText(Path.Combine(root, "work", "microphone-focus-test.txt"),
                            "PASS: stale foreign-window UIA rejected / editable field resumes and injects / non-input window enters fallback / focus return resumes only new text / new field adopted / read-only field stays in fallback / recovery resumes without replay / late old audio stays in fallback");
                    } catch (Exception error) {
                        File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-test-failure.txt"), error.ToString());
                        Environment.ExitCode = 1;
                    } finally { timer.Dispose(); context.ExitThread(); }
                };
                timer.Start();
                Application.Run(context);
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--self-test") {
            if (Marshal.SizeOf(typeof(Native.Input)) != (IntPtr.Size == 8 ? 40 : 28)) throw new Exception("INPUT layout");
            // A coarse window (Electron/Flutter/custom-drawn) must stay deliverable,
            // while a real read-only field and a visible non-text control must not.
            if (DecideTarget(true, false, false) != TargetKind.Unknown || !Deliverable(TargetKind.Unknown))
                throw new Exception("Coarse window must stay deliverable");
            // Electron answers with a read-only whole-document placeholder; it is still deliverable.
            if (DecideTarget(true, true, false) != TargetKind.Unknown)
                throw new Exception("Placeholder read-only document must not veto a coarse window");
            if (Deliverable(DecideTarget(false, false, false)) || Deliverable(DecideTarget(false, true, false)))
                throw new Exception("Visible non-text or read-only targets must stay in fallback");
            if (DecideTarget(false, true, true) != TargetKind.Editable || !Deliverable(TargetKind.Editable))
                throw new Exception("Writable text host must deliver");
            var inputs = Native.TextInputs("你好A😀");
            if (inputs.Length != 10 || inputs[0].data.keyboard.scan != '你' || inputs[1].data.keyboard.flags != 6) throw new Exception("Unicode input");
            Directory.CreateDirectory(Path.Combine(root, "work"));
            using (var shell = new Microphone(Path.Combine(root, ".tmp", Guid.NewGuid().ToString("N")), "")) {
                shell.recovery = "保留的缓存";
                shell.copyText = text => shell.copiedInTest = text;
                shell.Status("error", "原错误");
                shell.ShortClick(88).GetAwaiter().GetResult();
                if (shell.copiedInTest != shell.recovery || shell.state != "error") throw new Exception("Error capsule right side must still copy");
                shell.copiedInTest = "";
                // A missing Python path makes retry fail synchronously without requiring a model or a message loop.
                shell.ShortClick(64).GetAwaiter().GetResult();
                if (shell.copiedInTest.Length != 0 || shell.recovery != "保留的缓存" || !shell.tip.GetToolTip(shell).Contains("左侧重试"))
                    throw new Exception("Error capsule left side must retry without copying or clearing memory");
            }
            using (var bitmap = new Bitmap(72, 72)) using (var g = Graphics.FromImage(bitmap)) {
                Draw(g, "idle");
                bool softEdge = false;
                for (int x = 0; x < bitmap.Width; x++) for (int y = 0; y < bitmap.Height; y++) {
                    int alpha = bitmap.GetPixel(x, y).A;
                    if (alpha > 0 && alpha < 255) softEdge = true;
                }
                if (!softEdge || bitmap.GetPixel(0, 0).A != 0 || bitmap.GetPixel(36, 36).A != 255)
                    throw new Exception("Microphone must have transparent corners and antialiased edges");
                bitmap.Save(Path.Combine(root, "work", "microphone-preview.png"));
            }
            using (var bitmap = new Bitmap(212, 42)) using (var g = Graphics.FromImage(bitmap)) {
                for (int i = 0; i < 2; i++) using (var cell = new Bitmap(106, 42)) using (var ink = Graphics.FromImage(cell)) {
                    DrawMemory(ink, i == 1);
                    bool softEdge = false;
                    for (int x = 0; x < cell.Width; x++) {
                        int alpha = cell.GetPixel(x, 1).A;
                        if (alpha > 0 && alpha < 255) softEdge = true;
                    }
                    if (!softEdge || cell.GetPixel(0, 0).A != 0) throw new Exception("Copy pill must have soft transparent edges");
                    g.DrawImageUnscaled(cell, i * 106, 0);
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-copy-states.png"));
            }
            using (var bitmap = new Bitmap(720, 192)) using (var g = Graphics.FromImage(bitmap)) {
                g.Clear(Color.FromArgb(242, 243, 242)); g.ScaleTransform(2, 2);
                for (int i = 0; i < 3; i++) using (var cell = new Bitmap(i == 0 ? 72 : 106, i == 0 ? 72 : 42)) using (var ink = Graphics.FromImage(cell)) {
                    if (i == 0) Draw(ink, "idle"); else DrawMemory(ink, i == 2);
                    var saved = g.Save(); g.TranslateTransform(i == 0 ? 12 : i == 1 ? 112 : 236, i == 0 ? 12 : 30);
                    using (var clip = new GraphicsPath()) {
                        if (i == 0) clip.AddEllipse(0, 0, 71, 71);
                        else { clip.AddArc(0, 0, 35, 35, 90, 180); clip.AddArc(64, 0, 35, 35, 270, 180); clip.CloseFigure(); }
                        g.DrawImageUnscaled(cell, 0, 0);
                    }
                    g.Restore(saved);
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-copy-design.png"));
            }
            using (var shell = new Microphone(root, "--stream-test")) {
                float feedback = shell.Feedback(0, 1);
                if (feedback <= 0 || feedback > 1) throw new Exception("Copy feedback must advance without overshoot");
                for (int i = 0; i < 24; i++) feedback = shell.Feedback(feedback, 1);
                if (feedback != 1 || shell.Feedback(feedback, 0) >= feedback) throw new Exception("Copy feedback must settle and reverse for new text");
                shell.KeepRecovery("测试恢复结果");
                if (shell.recoveryCopied || shell.copiedInTest.Length != 0) throw new Exception("Memory must not copy automatically");
                shell.delivery = new TextDelivery { Delivered = "已上屏", Accounted = "已上屏", Latest = "已上屏未上屏", Paused = "切换窗口" };
                shell.state = "recording";
                shell.ApplyText("已上屏未上屏");
                shell.ApplyText("已上屏未上屏继续说");
                shell.ApplyText("已上屏未上屏继续说");
                Application.DoEvents();
                if (shell.state != "recording" || shell.recovery != "测试恢复结果" + Environment.NewLine + "未上屏继续说" || shell.copiedInTest.Length != 0)
                    throw new Exception("Focus switch must keep recording and remember only undelivered text once");
                shell.delivery = new TextDelivery { Paused = "无输入框" };
                shell.ApplyText("新会话");
                if (!shell.recovery.EndsWith(Environment.NewLine + "新会话")) throw new Exception("Memory lost across sessions");
                shell.recovery = "";
                shell.ApplyText("新会话继续");
                if (shell.recovery != "继续") throw new Exception("Cleared memory was reinserted");
                shell.delivery = null; shell.state = "idle";
                shell.recovery = "测试恢复结果";
                shell.copyText = text => { throw new ExternalException("Clipboard busy"); };
                shell.CopyRecovery();
                if (shell.recoveryCopied || shell.recovery != "测试恢复结果") throw new Exception("Clipboard failure lost result");
                shell.copyText = text => shell.copiedInTest = text;
                shell.CopyRecovery();
                if (!shell.recoveryCopied || shell.copiedInTest != shell.recovery) throw new Exception("Clipboard retry failed");
            }
            using (var quiet = new Bitmap(128, 96)) using (var speaking = new Bitmap(128, 96))
            using (var quietInk = Graphics.FromImage(quiet)) using (var speakingInk = Graphics.FromImage(speaking)) {
                DrawShape(quietInk, 1, "recording", false, 0, 0, 0, 1, 0, 0, 0);
                DrawShape(speakingInk, 1, "recording", false, 0, 0, 0, 1, 1, 0, 0);
                int different = 0;
                for (int x = 24; x < 56; x++) for (int y = 30; y < 66; y++)
                    if (quiet.GetPixel(x, y) != speaking.GetPixel(x, y)) different++;
                if (different < 10) throw new Exception("Quiet capsule must return to microphone independently of audio level");
            }
            using (var bitmap = new Bitmap(128 * 4, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                for (int i = 0; i < 4; i++) using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                    bool speaking = i % 2 != 0;
                    DrawShape(layer, i < 2 ? 0 : 1, "recording", false, 0, 0, 0, 1, speaking ? 1 : 0, speaking ? .7f : 0, 600, i >= 2);
                    ink.DrawImageUnscaled(cell, i * 128, 0);
                }
                bitmap.Save(Path.Combine(root, "work", "listening-silence-and-speech.png"));
            }
            using (var bitmap = new Bitmap(128 * 2, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                // Idle: circle inside a text field, capsule (nothing cached yet) outside it.
                for (int i = 0; i < 2; i++) using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                    DrawShape(layer, i, "idle", false, 0, 0, 0, 0, 0, 0, 0, false, false, i == 1 ? 0 : 1, i == 1);
                    ink.DrawImageUnscaled(cell, i * 128, 0);
                }
                bitmap.Save(Path.Combine(root, "work", "idle-focus-states.png"));
            }
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.state = "recording"; shell.delivery = new TextDelivery { Paused = "无输入焦点" };                shell.ApplyReply(new Dictionary<string, object> { { "text", "" }, { "preview", "未定稿的话" } });
                if (!shell.MemoryPill || shell.recovery.Length != 0 || shell.copiedInTest.Length != 0) throw new Exception("Fallback draft must morph without exposing draft to clipboard");
                shell.ApplyReply(new Dictionary<string, object> { { "text", "已定稿的话" }, { "preview", "" } });
                if (!shell.MemoryPill || shell.recovery != "已定稿的话") throw new Exception("Final fallback did not retain copy pill");
            }
            using (var bitmap = new Bitmap(128 * 4, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                for (int i = 0; i < 4; i++) using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                    DrawShape(layer, i == 0 ? 0 : 1, "recording", i == 3, i == 3 ? 1 : 0, 0, 0, 1, 1, i == 1 ? 0 : .7f, i * 120, i == 1);
                    ink.DrawImageUnscaled(cell, i * 128, 0);
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-live-waveform.png"));
            }
            using (var shell = new Microphone(root, "--stream-test"))
            using (var bitmap = new Bitmap(128 * 3, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                shell.state = "recording"; shell.delivery = new TextDelivery();
                shell.motion.Update("recording", .6f, 0, true);
                for (int i = 0; i < 3; i++) {
                    if (i == 2) { shell.recovery = "旧缓存"; shell.delivery.MemoryStarted = true; }
                    shell.SetInputFocus(i != 1);
                    if (shell.MemoryPill != (i == 1)) throw new Exception("Mode shape must follow input focus, not cache contents");
                    shell.shape.Update(shell.MemoryPill, i * 1000, true);
                    using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                        shell.DrawCurrent(layer); ink.DrawImageUnscaled(cell, i * 128, 0);
                    }
                }
                bitmap.Save(Path.Combine(root, "work", "focus-mode-states.png"));
            }
            using (var shell = new Microphone(root, "--stream-test")) {
                // Idle shape must report the delivery gate too, not only while recording.
                shell.state = "idle"; shell.delivery = null; shell.recovery = "";
                shell.SetInputFocus(true);
                if (shell.MemoryPill) throw new Exception("Idle focus inside an input box must stay a circle");
                shell.SetInputFocus(false);
                if (!shell.MemoryPill) throw new Exception("Idle focus outside an input box must morph to a capsule");
                shell.recovery = "等待复制的文字";
                shell.SetInputFocus(true);
                if (!shell.MemoryPill) throw new Exception("A waiting transcript must keep the copy capsule");
                if (!shell.AccessibleName.Contains("复制")) throw new Exception("Capsule with cached text must stay a copy control");
                shell.recovery = "";
                shell.SetInputFocus(false);
                if (shell.AccessibleName.Contains("复制")) throw new Exception("Idle capsule without cache must not announce copying");
            }
            var morph = new ShapeMotion();
            double circleArea = 0;
            foreach (double phase in new[] { 0, .25, .5, 1, 1.16 }) using (var cell = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(cell)) {
                DrawShape(ink, phase, "idle", false, 0, 0, 0);
                double area = 0;
                for (int x = 0; x < cell.Width; x++) for (int y = 0; y < cell.Height; y++) area += cell.GetPixel(x, y).A / 255.0;
                if (phase == 0) circleArea = area;
                if (Math.Abs(area - circleArea) / circleArea > .02) throw new Exception("Circle/ellipse morph must preserve visible area, including overshoot");
            }
            foreach (double phase in new[] { 0.0, 1.0 }) using (var first = new Bitmap(128, 96)) using (var second = new Bitmap(128, 96))
            using (var a = Graphics.FromImage(first)) using (var b = Graphics.FromImage(second)) {
                DrawShape(a, phase, "recording", false, 0, 0, 0, 1, 0, 0, 200);
                DrawShape(b, phase, "recording", false, 0, 0, 0, 1, 0, 0, 900);
                int changed = 0;
                for (int x = 10; x < 110; x++) for (int y = 12; y < 84; y++) if (first.GetPixel(x, y) != second.GetPixel(x, y)) changed++;
                if (changed < 150) throw new Exception("Silent listening halo must remain visibly animated in both shapes");
            }
            using (var preview = new Bitmap(128 * 6, 124)) using (var ink = Graphics.FromImage(preview)) using (var font = new Font("Microsoft YaHei UI", 9)) {
                ink.Clear(Color.FromArgb(39, 44, 51));
                string[] labels = { "静音 · 持续监听", "讲话 · 声纹", "无焦点 · 讲话", "转写中 · 不可复制", "完成 · 可以复制", "已复制" };
                for (int i = 0; i < 6; i++) using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                    DrawShape(layer, i < 2 ? 0 : 1, "recording", i == 5, i == 5 ? 1 : 0, 0, 0, 1, i == 1 || i == 2 ? 1 : 0, .7f, 600, i == 2, i == 3, i >= 4 ? 1 : 0);
                    ink.DrawImageUnscaled(cell, i * 128, 0);
                    using (var label = new SolidBrush(Color.FromArgb(210, 215, 220)))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center }) ink.DrawString(labels[i], font, label, new RectangleF(i * 128, 97, 128, 24), format);
                }
                preview.Save(Path.Combine(root, "work", "voice-interaction-design.png"));
            }
            morph.Update(false, 0, false);
            double maximum = 0;
            for (int i = 1; i <= 120; i++) { morph.Update(true, i * 16, false); maximum = Math.Max(maximum, morph.Value); }
            if (maximum <= 1 || morph.Value != 1 || !morph.Settled) throw new Exception("Jelly spring must overshoot and settle");
            morph.Update(false, 1936, false); double interruptedShape = morph.Value;
            morph.Update(true, 1936, false);
            if (morph.Value != interruptedShape) throw new Exception("Morph reversal jumped");
            morph.Update(false, 1952, true);
            if (morph.Value != 0 || morph.Velocity != 0 || !morph.Settled) throw new Exception("Reduced motion must switch instantly");
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.recovery = "已复制内容"; shell.recoveryCopied = true;
                shell.state = "idle"; shell.pressed = true; shell.pressedAt = 100;
                if (shell.TakeLongPress(499) || !shell.TakeLongPress(500) || shell.TakeLongPress(900)) throw new Exception("Long press threshold or repeat guard failed");
                if (!shell.heldTriggered || shell.copiedInTest.Length != 0) throw new Exception("Long press must suppress release copy");
                shell.heldTriggered = false; shell.dragged = true;
                if (shell.TakeLongPress(1000)) throw new Exception("Drag must cancel long press");
                shell.dragged = false; shell.pressed = false;
                if (shell.TakeLongPress(1000)) throw new Exception("Released press must not trigger");
                shell.delivery = new TextDelivery(); shell.state = "recording"; shell.inputFocused = true;
                if (shell.MemoryPill) throw new Exception("New recording must return to microphone despite old memory");
                shell.ApplyText("新内容");
                if (!shell.MemoryPill || shell.recoveryCopied) throw new Exception("New undelivered text must immediately show copy pill");
            }
            using (var bitmap = new Bitmap(128 * 8, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                morph = new ShapeMotion(); morph.Update(false, 0, false);
                int frame = 0;
                for (int i = 0; i <= 120; i++) {
                    morph.Update(true, i * 16, false);
                    if (i == 0 || i == 5 || i == 10 || i == 15 || i == 20 || i == 30 || i == 50 || i == 120) {
                        using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                            DrawShape(layer, morph.Value, "idle", false, 0, 0, 0); ink.DrawImageUnscaled(cell, frame++ * 128, 0);
                        }
                    }
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-shape-transition.png"));
            }
            using (var bitmap = new Bitmap(128 * 5, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                for (int i = 0; i < 5; i++) using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                    DrawShape(layer, 1, "idle", true, i / 4f, 0, 0); ink.DrawImageUnscaled(cell, i * 128, 0);
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-copy-transition.png"));
            }
            var delivery = new TextDelivery();
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.state = "recording"; shell.delivery = new TextDelivery { Paused = "缓存测试" };
                shell.KeepRecovery("旧缓存");
                shell.ApplyReply(new Dictionary<string, object> { { "text", "" }, { "transcribing", true }, { "speaking", false } });
                shell.CopyRecovery();
                if (shell.CopyReady || shell.copiedInTest.Length != 0) throw new Exception("Inflight final must disable copying old incomplete cache");
                shell.ApplyReply(new Dictionary<string, object> { { "text", "新文字" }, { "transcribing", false } });
                if (!shell.CopyReady) throw new Exception("Final completion must enable copy while recording continues");
                shell.CopyRecovery();
                if (!shell.copiedInTest.Contains("新文字") || !shell.recoveryCopied || shell.state != "recording") throw new Exception("Ready copy omitted final text or stopped recording");
                shell.copiedInTest = "";
                shell.ApplyReply(new Dictionary<string, object> { { "text", "新文字" }, { "speaking", true } }); shell.CopyRecovery();
                if (shell.CopyReady || shell.copiedInTest.Length != 0) throw new Exception("New speech must freeze copy immediately");
            }
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.state = "recording";
                shell.ApplyReply(new Dictionary<string, object> { { "text", "" }, { "level", .35 }, { "speaking", false } });
                shell.motion.Update(shell.state, shell.level, 0, true, shell.speechDetected);
                shell.motion.Update(shell.state, shell.level, 1000, true, shell.speechDetected);
                if (shell.motion.Voice != 0 || shell.motion.Activity != 1) throw new Exception("Amplified noise must keep listening microphone");
                shell.ApplyReply(new Dictionary<string, object> { { "text", "" }, { "level", .001 }, { "speaking", true } });
                shell.motion.Update(shell.state, shell.level, 1100, true, shell.speechDetected);
                if (shell.motion.Voice != 1) throw new Exception("Quiet detected speech must show waveform");
                shell.ApplyReply(new Dictionary<string, object> { { "text", "" }, { "level", .35 }, { "speaking", false } });
                shell.motion.Update(shell.state, shell.level, 1500, true, shell.speechDetected);
                if (shell.motion.Voice != 0 || shell.state != "recording") throw new Exception("Speech end must restore microphone without stopping listening");
            }
            var motion = new DictationMotion();
            motion.Update("idle", 0, 0, false);
            motion.Update("recording", 0, 10, false);
            motion.Update("recording", 0, 190, false);
            if (motion.Activity != 1 || motion.Voice != 0) throw new Exception("Listening without speech must show microphone");
            motion.Update("recording", .5f, 200, false);
            motion.Update("recording", .5f, 360, false);
            if (motion.Voice != 1) throw new Exception("Speech must morph into five fine bars");
            motion.Update("recording", 0, 500, false);
            if (motion.Voice != 1) throw new Exception("Speech indicator flickered during sample gap");
            motion.Update("recording", 0, 590, false);
            motion.Update("recording", 0, 750, false);
            if (motion.Voice != 0) throw new Exception("Silence must return to microphone");
            motion.Update("idle", 0, 760, false); motion.Update("idle", 0, 800, false);
            float interrupted = motion.Activity;
            motion.Update("recording", 0, 800, false);
            if (motion.Activity != interrupted) throw new Exception("Interrupted transition jumped");
            motion.Update("recording", 0, 980, false);
            if (motion.Activity != 1) throw new Exception("Interrupted transition failed to settle");
            motion.Update("recording", .5f, 1000, true);
            if (motion.Activity != 1 || motion.Voice != 1) throw new Exception("Reduced motion feedback");
            motion.Update("idle", 0, 1001, true);
            if (motion.Activity != 0 || motion.Voice != 0) throw new Exception("Reduced motion stop");
            if (AudioLevel(new byte[1280]) != 0 || AudioLevel(new byte[0]) != 0 || AudioLevel(new byte[] { 0, 64 }) != 1)
                throw new Exception("Audio feedback level");
            foreach (string cue in new[] { "BasicListeningEarcon", "BasicDoneListeningEarcon", "BasicCancelledEarcon" }) {
                string path = EarconPath(cue);
                if (!File.Exists(path) || Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4) != "RIFF")
                    throw new Exception("Windows dictation earcon missing: " + cue);
            }
            using (var bitmap = new Bitmap(72 * 5, 72)) using (var g = Graphics.FromImage(bitmap)) {
                string[] states = { "idle", "recording", "recording", "busy", "error" };
                for (int i = 0; i < states.Length; i++) {
                    var saved = g.Save(); g.SetClip(new Rectangle(i * 72, 0, 72, 72)); g.TranslateTransform(i * 72, 0);
                    Draw(g, states[i], i == 2 ? 0.7f : 0, 4); g.Restore(saved);
                }
                bitmap.Save(Path.Combine(root, "work", "microphone-states.png"));
            }
            string frames = Path.Combine(root, "work", "motion-preview"); Directory.CreateDirectory(frames);
            motion = new DictationMotion();
            morph = new ShapeMotion();
            for (int i = 0; i < 150; i++) {
                double now = i * 1000.0 / 30;
                string previewState = now < 300 || now >= 4000 ? "idle" : "recording";
                float previewLevel = now >= 1100 && now < 2700 ? .55f : 0;
                motion.Update(previewState, previewLevel, now, false);
                morph.Update(now >= 1400 && now < 3800, now, false);
                using (var bitmap = new Bitmap(128, 96)) using (var g = Graphics.FromImage(bitmap)) {
                    DrawShape(g, morph.Value, previewState, false, 0, 0, 0, motion.Activity, motion.Voice, motion.Level, now, now < 3300, now >= 1400 && now < 3300, now >= 3300 ? 1 : 0);
                    bitmap.Save(Path.Combine(frames, i.ToString("D3") + ".png"));
                }
            }
            if (delivery.Accept("hello ") != "hello ") throw new Exception("Initial text");
            delivery.Sent("hello ");
            if (delivery.Accept("hello world") != "world") throw new Exception("English boundary");
            delivery.Sent("world");
            if (delivery.Accept("hello world") != "") throw new Exception("Duplicate text");
            if (delivery.Accept("hello Word") != "" || delivery.Paused.Length == 0) throw new Exception("Revision guard");
            delivery = new TextDelivery();
            delivery.Accept("A"); delivery.Sent("A");
            delivery.Paused = "无输入框"; delivery.Accept("AB");
            if (delivery.TakePending() != "B") throw new Exception("First fallback gap");
            delivery.Paused = "";
            if (delivery.Accept("ABC") != "C") throw new Exception("Resume must skip stored fallback");
            delivery.Sent("C"); delivery.Paused = "再次离开"; delivery.Accept("ABCD");
            if (delivery.TakePending() != "D" || delivery.TakePending() != "" || delivery.Delivered != "AC" || delivery.Accounted != "ABCD")
                throw new Exception("Interleaved delivery/fallback ledger duplicated or lost text");
            File.WriteAllText(Path.Combine(root, "work", "microphone-self-test.txt"), "PASS: x64 INPUT / Unicode / coarse-host delivery / read-only and non-text veto / idle shape follows focus / append-only text / English spaces / duplicate and revision guards / audio levels / Windows dictation earcon resources / quiet microphone / fine five-bar waveform in circle and capsule / speech hold / silence return / interruption / reduced motion / state rendering / jelly spring / long press restart guard / direct copy pill without dot / manual memory copy (test sink) / retain across sessions / append only undelivered text / focus changes keep recording / clipboard busy and retry; input devices=" + Recorder.waveInGetNumDevs());
            return;
        }
        bool created;
        bool testing = args.Length > 0 && (args[0] == "--smoke-test" || args[0] == "--stream-test" || args[0] == "--memory-test");
        // Desktops share the Local namespace; an invisible sandbox instance must not block the user's desktop.
        using (var mutex = new Mutex(true, "Local\\VoxFix Desktop-" + Native.DesktopName() + (testing ? "-test-" + Guid.NewGuid().ToString("N") : ""), out created)) {
            if (!created) {
                IntPtr existing = Native.FindWindow(null, "VoxFix Desktop");
                if (existing != IntPtr.Zero) Native.PostMessage(existing, Native.ShowMicrophone, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Microphone(root, args.Length > 0 ? args[0] : ""));
        }
    }
}
