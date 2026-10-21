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
[assembly: AssemblyVersion("1.6.24.0")]

static class Native
{
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr process);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible accessible);
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
    internal const uint ShortcutMessage = 0x8036;
    internal static readonly UIntPtr InputTag = new UIntPtr(0x56464454);
    internal static Input KeyInput(ushort key, bool up) {
        return new Input { type = 1, data = new Payload { keyboard = new Keyboard { vk = key, flags = up ? 2u : 0u, extra = InputTag } } };
    }
    internal static bool ModifierKey(uint key) { return (key >= 0x10 && key <= 0x12) || (key >= 0xA0 && key <= 0xA5) || key == 0x5B || key == 0x5C; }
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
            Velocity += ((target - Value) * 256 - Velocity * 32) * step;
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
            // Let a downstream shortcut consume its key before treating it as an edit.
            IntPtr next = CallNextHookEx(IntPtr.Zero, code, message, data);
            if (next == IntPtr.Zero && code >= 0 && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104)) {
                var key = (Key)Marshal.PtrToStructure(data, typeof(Key));
                if (key.extra != Native.InputTag && !Native.ModifierKey(key.vk)) pause((key.flags & 16) != 0 ? "外部模拟按键" : "键盘操作");
            }
            return next;
        };
        mouse = (code, message, data) => {
            long kind = message.ToInt64();
            if (code >= 0 && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B)) {
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

sealed class ShortcutKeys
{
    readonly HashSet<int> held = new HashSet<int>(), captured = new HashSet<int>();
    internal int Process(int key, bool down, int available, out bool swallow) {
        if (down) held.Add(key); else held.Remove(key);
        swallow = captured.Contains(key);
        if (!down) { captured.Remove(key); return 0; }
        if (swallow) return 0;
        bool win = held.Contains(0x5B) || held.Contains(0x5C);
        bool ctrl = held.Contains(0xA2) || held.Contains(0xA3);
        bool extra = held.Contains(0xA0) || held.Contains(0xA1) || held.Contains(0xA4) || held.Contains(0xA5);
        int command = !extra && win && !ctrl && key == 0x48 ? 1 :
            !extra && !win && !ctrl && key == 0x1B && (available & 1) != 0 ? 2 :
            !extra && !win && ctrl && key == 0x43 && (available & 2) != 0 ? 3 : 0;
        if (command != 0) { captured.Add(key); swallow = true; }
        return command;
    }
}

sealed class DictationShortcut : IDisposable
{
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    readonly Thread thread;
    Control dispatcher;
    ApplicationContext context;
    Exception failure;
    internal volatile int Available;
    internal volatile bool MaskFailed;
    internal DictationShortcut(Action<int> command) {
        using (var ready = new ManualResetEventSlim()) {
            thread = new Thread(() => {
                var keys = new ShortcutKeys();
                IntPtr handle = IntPtr.Zero;
                Hook hook = (code, message, data) => {
                    long kind = message.ToInt64();
                    if (code >= 0 && (kind == 0x100 || kind == 0x101 || kind == 0x104 || kind == 0x105)) {
                        bool swallow;
                        int action = keys.Process(Marshal.ReadInt32(data), kind == 0x100 || kind == 0x104, Available, out swallow);
                        if (action == 1) {
                            // An unused key masks the lone Win release without typing into the target.
                            var mask = new[] { Native.KeyInput(0xE8, false), Native.KeyInput(0xE8, true) };
                            if (Native.SendInput(2, mask, Marshal.SizeOf(typeof(Native.Input))) != 2) MaskFailed = true;
                        }
                        if (action != 0) command(action);
                        if (swallow) return new IntPtr(1);
                    }
                    return CallNextHookEx(IntPtr.Zero, code, message, data);
                };
                try {
                    dispatcher = new Control(); IntPtr window = dispatcher.Handle;
                    context = new ApplicationContext();
                    foreach (int key in new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) {
                        bool ignored;
                        if (Native.GetAsyncKeyState(key) < 0) keys.Process(key, true, 0, out ignored);
                    }
                    handle = SetWindowsHookEx(13, hook, GetModuleHandle(null), 0);
                    if (handle == IntPtr.Zero) throw new IOException("无法接管 Win+H，请退出后重新打开 VoxFix。");
                } catch (Exception error) { failure = error; }
                ready.Set();
                try { if (failure == null) Application.Run(context); }
                finally {
                    if (handle != IntPtr.Zero) UnhookWindowsHookEx(handle);
                    GC.KeepAlive(hook);
                    if (context != null) context.Dispose();
                    if (dispatcher != null) dispatcher.Dispose();
                }
            }) { IsBackground = true, Name = "VoxFix shortcuts" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
            if (failure != null) { thread.Join(); throw failure; }
        }
    }
    public void Dispose() {
        dispatcher.BeginInvoke(new Action(context.ExitThread));
        thread.Join();
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

sealed class MicrophoneMenuRenderer : ToolStripProfessionalRenderer
{
    internal static GraphicsPath Outline(Rectangle bounds, int radius) {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
        e.Graphics.Clear(e.ToolStrip.BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Outline(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 9))
            Microphone.DrawMaterial(e.Graphics, path, path.GetBounds(), 0, 0, 0, 0);
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if (!e.Item.Selected) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Outline(new Rectangle(6, 2, e.Item.Width - 12, e.Item.Height - 4), 5))
        using (var fill = new SolidBrush(Color.FromArgb(28, 244, 246, 255))) e.Graphics.FillPath(fill, path);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        e.TextColor = e.ToolStrip.ForeColor;
        base.OnRenderItemText(e);
    }
}

sealed class Microphone : Form
{
    readonly string root;
    readonly bool smokeTest;
    readonly bool streamTest;
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
    readonly bool shortcutCycleTest;
    bool inputFocused;
    bool recoveryCopied;
    bool hovered, pressed, pressCancelled;
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
    double revealAt = -180;
    bool startOnReveal;
    bool opening;
    bool dismissRequested;
    DictationShortcut shortcut;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08080080; return p; } }
    public Microphone(string path, string test) {
        root = path; smokeTest = test == "--smoke-test"; streamTest = test == "--stream-test"; Text = "VoxFix Desktop"; AccessibleName = "VoxFix Desktop 麦克风";
        shortcutCycleTest = test == "--hotkey-cycle-test";
        memoryTest = test == "--memory-test" || test == "--focus-test" || test == "--idle-focus-test" || shortcutCycleTest;
        probeOnly = test == "--focus-test" || test == "--idle-focus-test";
        if (memoryTest || streamTest || smokeTest) playEarcon = (name, wait) => { };
        copyText = streamTest || memoryTest ? new Action<string>(text => copiedInTest = text) : new Action<string>(Clipboard.SetText);
        menu.ShowImageMargin = menu.ShowCheckMargin = false;
        menu.ShowItemToolTips = false;
        menu.BackColor = Color.FromArgb(47, 52, 65); menu.ForeColor = Color.FromArgb(245, 246, 250);
        menu.Font = new Font("Microsoft YaHei UI", 9.5f);
        menu.Padding = new Padding(4); menu.Renderer = new MicrophoneMenuRenderer();
        menu.SizeChanged += (s, e) => {
            if (menu.Width < 2 || menu.Height < 2) return;
            var previous = menu.Region;
            using (var outline = MicrophoneMenuRenderer.Outline(new Rectangle(Point.Empty, menu.Size), 9)) menu.Region = new Region(outline);
            if (previous != null) previous.Dispose();
        };
        menu.Items.Add("收起", null, async (s, e) => await Dismiss());
        menu.Items.Add("退出", null, (s, e) => Close());
        foreach (ToolStripItem item in menu.Items) { item.AutoSize = false; item.Size = new Size(88, 28); }
        menu.MinimumSize = new Size(88, 60);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(128, 96);
        StartPosition = FormStartPosition.Manual;
        RestorePosition();
        DoubleBuffered = true; Cursor = Cursors.Hand;
        AccessibleDescription = "正在加载本地模型…";
        limit.Tick += async (s, e) => { limit.Stop(); await Toggle(); };
        capture.Tick += async (s, e) => await Pump();
        focusWatch.Tick += async (s, e) => await WatchIdleFocus();
        animation.Tick += async (s, e) => {
            if (!Visible && !startOnReveal) { animation.Stop(); return; }
            if (TakeRevealStart()) {
                if (state == "error") await StartWorker(false);
                if (!closing && (Visible || opening) && state == "idle") await Toggle(true);
                if (opening && !dismissRequested && (state == "recording" || state == "error")) {
                    shape.Update(MemoryPill, motionClock.Elapsed.TotalMilliseconds, true);
                    ShowSurface(false);
                }
                opening = false;
                if (state == "recording") startOnReveal = false;
                UpdateShortcutState();
                return;
            }
            if (TakeLongPress(motionClock.Elapsed.TotalMilliseconds)) { await NewRecording(); return; }
            motion.Update(restarting ? "recording" : state, level, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled, speechDetected);
            shape.Update(MemoryPill, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled);
            copyFeedback = Feedback(copyFeedback, recoveryCopied ? 1 : 0);
            hoverFeedback = Feedback(hoverFeedback, hovered ? 1 : 0);
            pressFeedback = Feedback(pressFeedback, pressed && !pressCancelled && !dragged ? 1 : 0);
            readyFeedback = Feedback(readyFeedback, CopyReady ? 1 : 0);
            if (state == "idle" || state == "error") {
                if (!startOnReveal && RevealProgress >= 1 && !pressed && shape.Settled && motion.Activity < .001f && motion.Voice < .001f &&
                    copyFeedback == (recoveryCopied ? 1 : 0) && hoverFeedback == (hovered ? 1 : 0) &&
                    pressFeedback == (pressed && !pressCancelled && !dragged ? 1 : 0) && readyFeedback == (CopyReady ? 1 : 0)) animation.Stop();
            }
            RefreshSurface();
        };
        MouseEnter += (s, e) => { hovered = true; animation.Start(); };
        MouseLeave += (s, e) => { hovered = false; pressCancelled = true; if (!Capture) pressed = false; animation.Start(); };
        MouseCaptureChanged += (s, e) => { if (!Capture) { pressed = false; animation.Start(); } };
        MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = Cursor.Position; origin = Location; dragged = false; pressed = true; pressCancelled = false; heldTriggered = false; pressedAt = motionClock.Elapsed.TotalMilliseconds; Capture = true; animation.Start(); } };
        MouseMove += (s, e) => {
            if (e.Button != MouseButtons.Left || !pressed) return;
            Point p = Cursor.Position;
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) > 5) dragged = true;
            if (dragged) {
                var area = Screen.FromPoint(p).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, origin.X + p.X - down.X)), Math.Max(area.Top, Math.Min(area.Bottom - Height, origin.Y + p.Y - down.Y)));
            }
        };
        MouseUp += async (s, e) => {
            bool click = pressed && !pressCancelled && !dragged && OwnsSurface(PointToScreen(e.Location));
            pressed = false; Capture = false; animation.Start();
            if (dragged) SavePosition();
            if (e.Button == MouseButtons.Right) {
                menu.Show(this, e.Location); return;
            }
            if (e.Button == MouseButtons.Left && click) {
                await ShortClick(e.X);
            }
        };
        VisibleChanged += (s, e) => UpdateShortcutState();
        Shown += async (s, e) => {
            if (shortcutCycleTest || (!probeOnly && !memoryTest && !streamTest && !smokeTest)) {
                IntPtr window = Handle;
                try { shortcut = new DictationShortcut(action => Native.PostMessage(window, Native.ShortcutMessage, new IntPtr(action), IntPtr.Zero)); }
                catch (Exception error) { Fail(error.Message); return; }
            }
            if (test == "--background" || shortcutCycleTest) { Hide(); focusWatch.Stop(); animation.Stop(); }
            else { if (!probeOnly && !memoryTest && !streamTest && !smokeTest) Hide(); Reveal(); }
            UpdateShortcutState();
            if (!probeOnly) await StartWorker(!shortcutCycleTest);
            if (shortcutCycleTest && !closing) {
                try { await RunShortcutCycleTest(); }
                catch (Exception error) { File.WriteAllText(Path.Combine(root, "work", "microphone-hotkey-cycle-test.txt"), error.ToString()); Environment.ExitCode = 1; }
                finally { Close(); }
            }
        };
        FormClosing += (s, e) => SavePosition();
        FormClosed += (s, e) => {
            closing = true; limit.Stop(); limit.Dispose();
            if (shortcut != null) shortcut.Dispose();
            capture.Stop(); capture.Dispose();
            focusWatch.Stop(); focusWatch.Dispose();
            if (guard != null) guard.Dispose();
            if (recorder != null) recorder.Dispose();
            EndListening(true);
            animation.Stop(); animation.Dispose();
            delivery = null;
            StopWorker();
            if (worker != null) worker.Dispose();
            menu.Font.Dispose(); menu.Dispose();
        };
    }
    string PositionFile { get { return Path.Combine(root, "work", "window-position.txt"); } }
    internal static Point ReadPosition(string path, Point fallback) {
        try {
            string[] values = File.ReadAllText(path).Trim().Split(','); int x, y;
            if (values.Length == 2 && int.TryParse(values[0], out x) && int.TryParse(values[1], out y)) return new Point(x, y);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return fallback;
    }
    internal static Point VisiblePosition(Point point, Size size, Rectangle area) {
        return new Point((int)Math.Max(area.Left, Math.Min((long)area.Right - size.Width, point.X)),
            (int)Math.Max(area.Top, Math.Min((long)area.Bottom - size.Height, point.Y)));
    }
    void RestorePosition() {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Point fallback = new Point(area.Right - Width - 36, area.Bottom - Height - 54);
        Point point = memoryTest || streamTest || smokeTest ? fallback : ReadPosition(PositionFile, fallback);
        Location = VisiblePosition(point, Size, Screen.FromPoint(point).WorkingArea);
    }
    void SavePosition() {
        if (memoryTest || streamTest || smokeTest) return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(PositionFile));
            File.WriteAllText(PositionFile, Location.X + "," + Location.Y);
        } catch (IOException) { AccessibleDescription = "窗口位置保存失败，本次位置仍然保留。"; }
        catch (UnauthorizedAccessException) { AccessibleDescription = "窗口位置保存失败：目录不可写。"; }
    }
    float RevealProgress { get { return animationsEnabled ? DictationMotion.Ease((motionClock.Elapsed.TotalMilliseconds - revealAt) / 180) : 1; } }
    void Reveal(bool resume = false) {
        if (shortcutCycleTest || (!probeOnly && !memoryTest && !streamTest && !smokeTest)) {
            if ((Visible && !resume) || opening) return;
            dismissRequested = false; opening = startOnReveal = true;
            UpdateShortcutState(); animation.Start();
            return;
        }
        ShowSurface(true);
    }
    void ShowSurface(bool animate) {
        dismissRequested = false;
        if (!animate) revealAt = -180;
        else if (!Visible) revealAt = motionClock.Elapsed.TotalMilliseconds;
        Location = VisiblePosition(Location, Size, Screen.FromPoint(Location).WorkingArea);
        Show(); TopMost = true; focusWatch.Start(); animation.Start(); RefreshSurface();
    }
    bool TakeRevealStart() {
        if (state == "recording") startOnReveal = false;
        if (!startOnReveal || (!Visible && !opening) || closing || (state != "idle" && state != "error")) return false;
        startOnReveal = false;
        return true;
    }
    static bool ModifiersDown() {
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if (Native.GetAsyncKeyState(key) < 0) return true;
        return false;
    }
    protected override void WndProc(ref Message message) {
        if (message.Msg == Native.ShowMicrophone) { Reveal(); return; }
        if (message.Msg == Native.ShortcutMessage) { HandleShortcut(message.WParam.ToInt32()); return; }
        base.WndProc(ref message);
    }
    void UpdateShortcutState() {
        if (shortcut != null) shortcut.Available = closing || (!Visible && !opening) ? 0 : 1 | (ShortcutCopyReady ? 2 : 0);
    }
    async void HandleShortcut(int action) {
        try {
            if (closing) return;
            if (action == 2) { if (Visible || opening) await Dismiss(); return; }
            if (action == 3) { if (ShortcutCopyReady) CopyRecovery(); return; }
            if (action != 1) return;
            if (state == "recording") { startOnReveal = false; await EndRecording(); }
            else if (opening || startOnReveal) {
                opening = startOnReveal = false; dismissRequested = true; UpdateShortcutState();
            }
            else Reveal(true);
        } catch (Exception error) { Fail(error.Message); }
    }
    async Task Dismiss() {
        opening = startOnReveal = false; dismissRequested = true;
        pressed = hovered = false; pressCancelled = true; Capture = false;
        hoverFeedback = pressFeedback = 0;
        SavePosition(); Hide(); focusWatch.Stop(); animation.Stop();
        if (state == "recording") await EndRecording();
    }
    void Status(string value, string description) {
        state = value; AccessibleDescription = description; RefreshSurface();
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
        if (!CopyReady) { AccessibleDescription = "转写完成后才能复制；左侧可结束录音并收齐尾字。"; return; }
        recoveryCopied = false;
        try {
            copyText(recovery);
            recoveryCopied = true;
            AccessibleDescription = state == "error" ? "已复制 · 左侧重试 · 右侧重新复制" : "已复制 · 录音时点左侧声纹结束，点右侧重新复制 · 长按清空并开始新录音";
        } catch { AccessibleDescription = "剪贴板暂时不可用，文字仍在图标里，点击重试。"; }
        UpdateMemoryShape();
        animation.Start();
        RefreshSurface();
    }
    void KeepRecovery(string text) {
        if (text.Length == 0) return;
        if (delivery == null || !delivery.MemoryStarted || delivery.NewMemoryBlock) {
            if (recovery.Length > 0) recovery += Environment.NewLine;
            if (delivery != null) { delivery.MemoryStarted = true; delivery.NewMemoryBlock = false; }
        }
        recovery += text; recoveryCopied = false; animation.Start(); RefreshSurface();
        UpdateMemoryShape();
        AccessibleDescription = "左侧声纹：结束录音 · 右侧：复制 · 长按：清空缓存并开始新一轮";
    }
    void UpdateMemoryShape() {
        bool pill = MemoryPill;
        AccessibleName = !pill ? "VoxFix Desktop 麦克风"
            : state == "error" ? "识别失败，左侧重试，右侧复制"
            : state == "idle" && recovery.Length == 0 ? "麦克风未开启，点击开始说话"
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
    bool ShortcutCopyReady { get { return Visible && CopyReady && !recoveryCopied; } }
    void SetInputFocus(bool focused) {
        if (inputFocused == focused) return;
        inputFocused = focused;
        if (state == "idle" && recovery.Length == 0)
            AccessibleDescription = focused ? "这里可以直接输入，点一下开始说话" : "这里不会直接输入，文字会先存成可复制内容";
        UpdateMemoryShape(); RefreshSurface();
    }
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
        if (samePair && idleAttempts >= 3 && !CoarseClass(window)) return;
        idleWatching = true;
        try {
            var kind = await ClassifySafe(window, focus);
            if (closing || (state != "idle" && state != "error") || recovery.Length > 0) return;
            if (Native.GetForegroundWindow() != window || Native.Focus(window) != focus) return;
            idleAttempts = samePair ? idleAttempts + 1 : 1;
            idleWindow = window; idleFocus = focus;
            SetInputFocus(Deliverable(kind));
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
        if (!pressed || pressCancelled || dragged || heldTriggered || !MemoryPill || (state != "idle" && state != "recording" && state != "error") || now - pressedAt < 400) return false;
        heldTriggered = true;
        return true;
    }
    async Task NewRecording() {
        if (restarting) return;
        restartPill = MemoryPill; restarting = true;
        try {
            if (state == "error") await StartWorker(false);
            if (state == "recording") await EndRecording(true);
            if (state == "idle") {
                await Toggle();
            }
        } finally {
            restarting = false;
            if (!closing) { UpdateMemoryShape(); RefreshSurface(); }
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
                recovery = "上一轮缓存"; recoveryCopied = true;
                await BeginStream();
                if (recovery.Length != 0 || recoveryCopied) throw new Exception("New round must clear old cache after backend starts");
                KeepRecovery("本轮已定稿");
                await FinishRecording(new byte[0]);
                await Dismiss();
                if (recovery != "本轮已定稿") throw new Exception("Stop and Escape must retain current round cache");
                Reveal();
                await BeginStream();
                if (recovery.Length != 0 || recoveryCopied) throw new Exception("Next round must start with empty cache");
                await FinishRecording(new byte[0]);
                recovery = "启动失败前的缓存";
                StopWorker();
                bool failed = false;
                try { await BeginStream(); } catch { failed = true; }
                if (!failed || recovery != "启动失败前的缓存") throw new Exception("Failed start must preserve previous cache");
                delivery = null;
                File.WriteAllText(Path.Combine(root, "work", "microphone-round-reset-test.txt"), "PASS: real SenseVoice start handshake / new round clears cache and copied flag / stop and dismiss retain current cache / next round clears again / failed start retains previous cache; no microphone or clipboard access");
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
        startOnReveal = false;
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
    // Missing accessibility information stays deliverable; confirmed read-only targets use the cache.
    internal enum TargetKind { ReadOnly, Unknown, Editable }
    static bool Deliverable(TargetKind kind) { return kind == TargetKind.Editable || kind == TargetKind.Unknown; }
    static TargetKind DecideTarget(bool textHost, bool writable) {
        if (textHost) return writable ? TargetKind.Editable : TargetKind.ReadOnly;
        return TargetKind.ReadOnly;
    }
    // Coarse hosts need MSAA's focused descendant; UIA may only report the page root.
    static bool CoarseClass(IntPtr window) {
        string name = Native.ClassOf(window);
        return name.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)
            || name == "FLUTTER_RUNNER_WIN32_WINDOW" || name == "CEF-OSC-WIDGET"
            || name == "TaskManagerWindow" || name == "Intermediate D3D Window";
    }
    static TargetKind DecideAccessibleTarget(int role, int state) {
        if ((state & (int)AccessibleStates.Focused) == 0) return TargetKind.Unknown;
        if (role == (int)AccessibleRole.Client || role == (int)AccessibleRole.Pane || role == (int)AccessibleRole.Window || role == (int)AccessibleRole.None)
            return TargetKind.Unknown;
        bool text = role == (int)AccessibleRole.Text || role == (int)AccessibleRole.Document || role == (int)AccessibleRole.ComboBox;
        bool writable = (state & (int)(AccessibleStates.ReadOnly | AccessibleStates.Unavailable | AccessibleStates.Invisible | AccessibleStates.Offscreen)) == 0;
        return DecideTarget(text, writable);
    }
    static TargetKind ClassifyCoarse(IntPtr window) {
        try {
            Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            Accessibility.IAccessible node;
            if (Native.AccessibleObjectFromWindow(window, 0xFFFFFFFC, ref iid, out node) != 0) return TargetKind.Unknown;
            for (int depth = 0; node != null && depth < 64; depth++) {
                object focused = node.accFocus;
                if (focused is int)
                    return DecideAccessibleTarget(Convert.ToInt32(node.get_accRole(focused)), Convert.ToInt32(node.get_accState(focused)));
                node = focused as Accessibility.IAccessible;
            }
        } catch { }
        return TargetKind.Unknown;
    }
    static TargetKind Classify(IntPtr window, IntPtr focus, IntPtr own) {
        if (window == IntPtr.Zero || window == own) return TargetKind.ReadOnly;
        if (CoarseClass(window)) return ClassifyCoarse(window);
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
                return DecideTarget(true, !((System.Windows.Automation.ValuePattern)pattern).Current.IsReadOnly);
            if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out pattern))
                return DecideTarget(true, Object.Equals(((System.Windows.Automation.TextPattern)pattern).DocumentRange.GetAttributeValue(System.Windows.Automation.TextPattern.IsReadOnlyAttribute), false));
            return DecideTarget(true, true);
        } catch { return TargetKind.Unknown; }
    }
    TargetKind ClassifyTarget(IntPtr window, IntPtr focus) { return Classify(window, focus, Handle); }
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
            }
        } catch (Exception error) { Fail(error.Message); }
    }
    async Task Toggle(bool fromShortcut = false) {
        if (state == "error") { await StartWorker(); return; }
        if (state == "loading" || state == "busy") return;
        if (state == "idle") {
            if (!fromShortcut && ModifiersDown()) { AccessibleDescription = "请松开修饰键后再开始录音。"; return; }
            target = Native.GetForegroundWindow(); targetFocus = Native.Focus(target);
            try {
                // Preserve the onset while UI Automation and the worker initialize.
                recorder = new Recorder();
                Status("busy", "正在开始录音…");
                bool editable = false;
                for (int attempt = 0; attempt < 3 && !editable && (!memoryTest || shortcutCycleTest); attempt++) {
                    target = Native.GetForegroundWindow(); targetFocus = Native.Focus(target);
                    editable = Deliverable(await ClassifySafe(target, targetFocus));
                    if (!editable && attempt < 2) await Task.Delay(16);
                }
                SetInputFocus(editable);
                await BeginStream();
                if (shortcutCycleTest) Pause("隔离测试，禁止向其他应用送字。");
                else if (!editable) Pause("没有可确认的输入框，继续识别并保存未上屏文字。");
                if (closing) return;
                guard = new InteractionGuard(OwnsSurface, reason => Pause(reason + "改变了输入位置，正在重新检测输入框。", true));
                listening = true; playEarcon("BasicListeningEarcon", false);
                Status("recording", editable ? "正在录音，停顿后输入 · 点击麦克风结束" : "正在录音，未上屏文字将直接显示为复制按钮 · 右键可结束录音"); limit.Start(); capture.Start();
                if (dismissRequested) await EndRecording();
            } catch (Exception error) { Fail(error.Message); }
            return;
        }
        await EndRecording();
    }
    async Task BeginStream() {
        draft = ""; speechDetected = transcribing = false;
        stalledWindows.Clear();
        recordingId = Guid.NewGuid().ToString("N"); delivery = new TextDelivery(); sentBytes = 0;
        audioRoute = appliedSegments = 0; backupRoutes.Clear();
        targetEpoch++; nextTargetProbe = 0;
        var reply = await Exchange(new { op = "start", id = recordingId });
        if (Convert.ToString(reply["event"]) != "started") throw new IOException("无法开始流式识别。");
        recovery = ""; recoveryCopied = false; copyFeedback = readyFeedback = 0;
        UpdateMemoryShape(); RefreshSurface();
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
            AccessibleDescription = reason;
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
        if (recovery.Length != 0 || recoveryCopied) throw new Exception("Successful new session must clear old memory");
        recovery = remembered; recoveryCopied = true;
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
            if (recovery.Length != 0 || recoveryCopied) throw new Exception("New recording must clear old memory");
            remembered = "";
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
            File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-stream-test.txt"), "PASS: paced text before stop / real SenseVoice / real SendInput / existing text preserved / no final duplication / user edit guard / focus guard / manual memory copy (test sink) / clear old cache on successful new round / append only undelivered text / focus changes keep recording / clipboard busy retry / cancellation never copies / restart after cancellation / no transcript window");
        }
        Close();
    }
    void RefreshSurface() {
        UpdateShortcutState();
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
        float progress = RevealProgress;
        if (progress >= 1) { DrawContent(g); return; }
        using (var bitmap = new Bitmap(128, 96)) {
            using (var ink = Graphics.FromImage(bitmap)) DrawContent(ink);
            DrawReveal(g, bitmap, progress);
        }
    }
    internal static void DrawReveal(Graphics g, Bitmap image, float progress) {
        g.Clear(Color.Transparent);
        var saved = g.Save();
        float scale = .95f + .05f * progress;
        g.TranslateTransform(64, 48 + 6 * (1 - progress));
        g.ScaleTransform(scale, scale); g.TranslateTransform(-64, -48);
        DrawFade(g, image, 0, 0, progress);
        g.Restore(saved);
    }
    void DrawContent(Graphics g) {
        DrawShape(g, shape.Value, restarting ? "recording" : state, recoveryCopied, copyFeedback, hoverFeedback, pressFeedback,
            motion.Activity, motion.Voice, motion.Level, animationsEnabled ? motionClock.Elapsed.TotalMilliseconds : 0,
            restarting || (state == "recording" && !inputFocused && recovery.Length == 0),
            !restarting && (transcribing || speechDetected || state == "busy"), readyFeedback,
            // Keep one microphone visible throughout the idle morph in either direction.
            (state == "idle" || state == "error") && recovery.Length == 0 && !transcribing && !restarting);
    }
    internal static SizeF ShapeSize(double shape) {
        double ratio = 1 + (106.0 / 42 - 1) * shape;
        double minor = Math.Sqrt(Math.PI * 36 * 36 / (Math.Max(ratio, 1 / ratio) - 1 + Math.PI / 4));
        return ratio >= 1 ? new SizeF((float)(minor * ratio), (float)minor) : new SizeF((float)minor, (float)(minor / ratio));
    }
    static readonly Bitmap[] materialTextures = LoadMaterialTextures();
    static Bitmap[] LoadMaterialTextures() {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VoxFix.GraphiteMaterial"))
        using (var atlas = Image.FromStream(stream)) {
            var crops = new[] { new Rectangle(74, 60, 626, 624), new Rectangle(828, 134, 1265, 497) };
            var textures = new Bitmap[2];
            for (int i = 0; i < textures.Length; i++) {
                textures[i] = new Bitmap(i == 0 ? 144 : 212, i == 0 ? 144 : 84);
                using (var ink = Graphics.FromImage(textures[i])) {
                    ink.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    ink.DrawImage(atlas, new Rectangle(Point.Empty, textures[i].Size), crops[i], GraphicsUnit.Pixel);
                }
            }
            return textures;
        }
    }
    internal static void DrawMaterial(Graphics g, GraphicsPath outline, RectangleF bounds, float hover, float press, float activity, double now, bool error = false) {
        var shadowState = g.Save();
        g.TranslateTransform(0, 1.6f);
        for (int spread = 6; spread >= 2; spread -= 2)
            using (var shadow = new Pen(Color.FromArgb(12 - spread, 12, 16, 28), spread)) g.DrawPath(shadow, outline);
        g.Restore(shadowState);
        float phase = Math.Max(0, Math.Min(1, (bounds.Width / bounds.Height - 1) / (106f / 42 - 1)));
        Bitmap blended = null;
        try {
            Bitmap face = phase <= 0 ? materialTextures[0] : materialTextures[1];
            if (phase > 0 && phase < 1) {
                blended = new Bitmap(128, 96); face = blended;
                using (var ink = Graphics.FromImage(blended)) {
                    ink.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    ink.DrawImage(materialTextures[0], new Rectangle(0, 0, 128, 96));
                    using (var attributes = new System.Drawing.Imaging.ImageAttributes()) {
                        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = phase });
                        ink.DrawImage(materialTextures[1], new Rectangle(0, 0, 128, 96), 0, 0,
                            materialTextures[1].Width, materialTextures[1].Height, GraphicsUnit.Pixel, attributes);
                    }
                }
            }
            // Both endpoints come from the same reference; the material blends with the live silhouette.
            using (var texture = new TextureBrush(face, WrapMode.Clamp))
            using (var placement = new Matrix(bounds.Width / face.Width, 0, 0, bounds.Height / face.Height, bounds.X, bounds.Y)) {
                texture.Transform = placement; g.FillPath(texture, outline);
            }
        } finally { if (blended != null) blended.Dispose(); }
        float breath = now == 0 ? .5f : (float)(.5 + .5 * Math.Sin(now * Math.PI / 1600));
        using (var light = new SolidBrush(Color.FromArgb((int)(hover * 8 + activity * (2 + 4 * breath)), 245, 247, 255))) g.FillPath(light, outline);
        using (var pressure = new SolidBrush(Color.FromArgb((int)(press * 8), 0, 0, 0))) g.FillPath(pressure, outline);
        if (error) using (var warning = new Pen(Color.FromArgb(180, 235, 178, 102), 1.2f)) g.DrawPath(warning, outline);
    }
    internal static void DrawShape(Graphics g, double shape, string state, bool copied, float success, float hover, float press,
        float activity = 0, float voice = 0, float level = 0, double now = 0, bool pending = false, bool transcribing = false, float ready = 1, bool stashed = false) {
        g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
        // A capsule keeps the circle's area, including spring overshoot and reversal.
        SizeF size = ShapeSize(shape);
        float scale = 1 - .025f * press;
        var surfaceState = g.Save();
        g.TranslateTransform(64, 48); g.ScaleTransform(scale, scale); g.TranslateTransform(-64, -48);
        float width = size.Width, height = size.Height;
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
            DrawMaterial(g, outline, outline.GetBounds(), hover, press, activity, now, state == "error");
            var saved = g.Save(); g.SetClip(outline);
            if (blend > 0) using (var pill = new Bitmap(106, 42)) using (var ink = Graphics.FromImage(pill)) {
                DrawMemory(ink, copied, success, hover, press, false, activity, level, now, state == "busy" || state == "loading", pending, voice, transcribing, ready, false, stashed);
                DrawFade(g, pill, 11, 27, Math.Max(0, 2 * blend - 1));
            }
            var motif = g.Save();
            float motifSize = Math.Min(width, height);
            g.TranslateTransform(64 - 29 * (float)shape - motifSize / 2, 48 - motifSize / 2);
            g.ScaleTransform(motifSize / 72, motifSize / 72);
            using (var circle = new Bitmap(72, 72)) using (var ink = Graphics.FromImage(circle)) {
                DrawListening(ink, state, activity, voice, level, now, false, 1.65f - .05f * blend);
                DrawFade(g, circle, 0, 0, stashed ? 1 : activity + (1 - activity) * Math.Max(0, 1 - 2 * blend));
            }
            g.Restore(motif);
            g.Restore(saved);
        }
        g.Restore(surfaceState);
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
            using (var capsule = new GraphicsPath()) {
                capsule.AddArc(.5f, .5f, 41, 41, 90, 180); capsule.AddArc(64.5f, .5f, 41, 41, 270, 180); capsule.CloseFigure();
                DrawMaterial(g, capsule, capsule.GetBounds(), hover, press, activity, now);
            }
        }
        if (stashed && !pending && !transcribing) {
            if (listeningMotif) DrawMicrophoneGlyph(g, 24, 21, 1, .95f);
            using (var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular))
                DrawStatusLabel(g, "未开启", font, Color.FromArgb(245, 246, 250));
            return;
        }
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
        float leaving = Math.Max(0, 1 - 2 * success), entering = Math.Max(0, 2 * success - 1);
        using (var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular)) {
            if (transcribing || pending) {
                DrawStatusLabel(g, transcribing ? "转写中" : "监听中", font, Color.FromArgb(235, 245, 246, 250));
                if (transcribing) {
                    using (var track = new Pen(Color.FromArgb(35, 240, 243, 241), 2)) g.DrawLine(track, 59, 33, 89, 33);
                    float travel = now == 0 ? .5f : (float)((now % 1000) / 1000);
                    using (var progress = new Pen(Color.FromArgb(150, 240, 243, 241), 2)) { progress.StartCap = progress.EndCap = LineCap.Round; g.DrawLine(progress, 59 + 20 * travel, 33, 69 + 20 * travel, 33); }
                }
            } else {
                int alpha = (int)(150 + 105 * ready);
                DrawStatusLabel(g, "复制", font, Color.FromArgb((int)(alpha * leaving), 245, 246, 250));
                DrawStatusLabel(g, "已复制", font, Color.FromArgb((int)(alpha * entering), 245, 246, 250));
            }
        }
    }
    static void DrawStatusLabel(Graphics g, string text, Font font, Color color) {
        if (color.A == 0) return;
        using (var path = new GraphicsPath()) {
            path.AddString(text, font.FontFamily, (int)font.Style, font.SizeInPoints * g.DpiY / 72,
                PointF.Empty, StringFormat.GenericTypographic);
            RectangleF ink = path.GetBounds();
            var saved = g.Save();
            g.TranslateTransform(74 - (ink.Left + ink.Right) / 2, 21 - (ink.Top + ink.Bottom) / 2);
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            g.Restore(saved);
        }
    }
    static void DrawVoice(Graphics g, float cx, float cy, float opacity, float level, double now, bool processing, Color color) {
        using (var pen = new Pen(Color.FromArgb((int)(255 * opacity), color), 1.8f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            for (int i = 0; i < 5; i++) {
                float pulse = now == 0 ? .6f : (float)(.5 + .5 * Math.Sin(now * Math.PI * 2 / 467 - i * .65));
                float envelope = 1 - Math.Abs(i - 2) * .18f;
                float height = (processing ? 6 + 14 * pulse : (6 + level * 26) * (.45f + .55f * pulse)) * envelope;
                float x = cx + (i - 2) * 4.2f;
                g.DrawLine(pen, x, cy - height / 2, x, cy + height / 2 + .01f);
            }
        }
    }
    internal static void Draw(Graphics g, string state, float level = 0, int frame = 0) {
        DrawListening(g, state, state == "recording" || state == "busy" ? 1 : 0,
            state == "recording" && level >= .02f ? 1 : 0, level, frame * 33);
    }
    internal static void DrawListening(Graphics g, string state, float activity, float voice, float level, double milliseconds, bool backdrop = true, float glyphScale = 1.65f) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (backdrop) {
            g.Clear(Color.Transparent);
            using (var outline = new GraphicsPath()) {
                outline.AddEllipse(.5f, .5f, 71, 71);
                DrawMaterial(g, outline, outline.GetBounds(), 0, 0, activity, milliseconds, state == "error");
            }
        }
        bool processing = state == "busy" || state == "loading";
        for (int i = 0; i < 2; i++) {
            float phase = milliseconds == 0 ? i * .5f : (float)((milliseconds / (processing ? 1000 : 1400) + i * .5) % 1);
            float diameter = 48 + 21 * phase;
            using (var ring = new Pen(Color.FromArgb((int)(100 * activity * (1 - phase)), 233, 241, 255), 1.3f))
                g.DrawEllipse(ring, 36 - diameter / 2, 36 - diameter / 2, diameter, diameter);
        }
        float bars = processing ? 1 : voice;
        Color inkColor = Color.FromArgb(245, 246, 250);
        var glyph = g.Save();
        g.TranslateTransform(36, 36); g.ScaleTransform(glyphScale, glyphScale); g.TranslateTransform(-36, -36);
        float barOpacity = Math.Max(0, bars * 2 - 1);
        if (barOpacity > .001f) DrawVoice(g, 36, 36, barOpacity, level, milliseconds, processing, inkColor);
        float microphone = Math.Max(0, 1 - bars * 2);
        if (microphone <= .001f) { g.Restore(glyph); return; }
        var saved = g.Save();
        float scale = .85f + .15f * (1 - bars);
        g.TranslateTransform(36, 36); g.ScaleTransform(scale, scale); g.TranslateTransform(-36, -36);
        DrawMicrophoneGlyph(g, 36, 36, microphone, 1);
        g.Restore(saved);
        g.Restore(glyph);
    }
    static void DrawMicrophoneGlyph(Graphics g, float cx, float cy, float opacity, float scale) {
        var saved = g.Save();
        g.TranslateTransform(cx, cy); g.ScaleTransform(scale, scale); g.TranslateTransform(-36, -36);
        Color color = Color.FromArgb((int)(255 * opacity), 245, 246, 250);
        using (var ink = new SolidBrush(color))
        using (var pen = new Pen(color, 1.7f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.FillEllipse(ink, 33, 27, 6, 6); g.FillRectangle(ink, 33, 30, 6, 6); g.FillEllipse(ink, 33, 33, 6, 6);
            g.DrawArc(pen, 30, 31, 12, 12, 0, 180); g.DrawLine(pen, 36, 43, 36, 46);
        }
        g.Restore(saved);
    }
    // Diagnostic: report how a target window is classified without dictating.
    // --target-probe [process] [--type text]
    static int RunTargetProbe(string[] args, string root) {
        IntPtr previous = Native.GetForegroundWindow(), window = previous;
        string filter = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "";
        string typed = "";
        string expected = "";
        for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--type") typed = args[i + 1];
        for (int i = 1; i < args.Length - 1; i++) if (args[i] == "--expect") expected = args[i + 1];
        if (filter.Length > 0) {
            foreach (var process in Process.GetProcessesByName(filter)) {
                if (process.MainWindowHandle != IntPtr.Zero) { window = process.MainWindowHandle; break; }
            }
        }
        Thread.Sleep(120);
        IntPtr foreground = Native.GetForegroundWindow();
        IntPtr focus = Native.Focus(window);
        bool coarse = CoarseClass(window);
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
        var classify = Task.Run(() => Classify(window, focus, IntPtr.Zero));
        var kind = classify.Wait(1000) ? classify.Result : TargetKind.Unknown;
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
        return expected.Length == 0 || expected == kind.ToString() ? 0 : 1;
    }
    async Task RunShortcutCycleTest() {
      using (var editor = new Form { Text = "VoxFix first frame input test", Width = 320, Height = 160, TopMost = true })
      using (var other = new Form { Text = "VoxFix first frame no input test", Width = 220, Height = 120, TopMost = true })
      using (var field = new TextBox { Dock = DockStyle.Fill })
      using (var button = new Button { Text = "No input here", Dock = DockStyle.Fill }) {
        editor.Controls.Add(field); other.Controls.Add(button); other.Show(); editor.Show();
        bool expectedPill = false;
        int firstFrames = 0;
        string frameFailure = null;
        EventHandler firstFrame = (s, e) => {
            if (!Visible) return;
            firstFrames++;
            if (shape.Value != (expectedPill ? 1 : 0) || RevealProgress != 1)
                frameFailure = "First visible frame must use current focus and full opacity: expectedPill=" + expectedPill + " shape=" + shape.Value + " reveal=" + RevealProgress;
            using (var bitmap = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(bitmap)) {
                DrawCurrent(ink); bitmap.Save(Path.Combine(root, "work", "shortcut-first-frame-" + firstFrames + ".png"));
            }
        };
        VisibleChanged += firstFrame;
        try {
        for (int cycle = 0; cycle < 3; cycle++) {
            expectedPill = cycle == 1;
            Form active = expectedPill ? other : editor;
            Control control = expectedPill ? (Control)button : field;
            uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
            bool attached = foreground != current && Native.AttachThreadInput(current, foreground, true);
            try { Native.SetForegroundWindow(active.Handle); active.Activate(); control.Focus(); }
            finally { if (attached) Native.AttachThreadInput(current, foreground, false); }
            await Task.Delay(100);
            recovery = "上一轮缓存"; shape.Update(true, motionClock.Elapsed.TotalMilliseconds, true);
            var press = new[] { Native.KeyInput(0x5B, false), Native.KeyInput(0x48, false), Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true) };
            var startPress = cycle == 0 ? new[] { Native.KeyInput(0x5B, false), Native.KeyInput(0x48, false) } : press;
            try {
                if (Native.SendInput((uint)startPress.Length, startPress, Marshal.SizeOf(typeof(Native.Input))) != startPress.Length) throw new Exception("Shortcut injection failed");
                await WaitCycleState("recording", cycle, cycle == 0 ? "start while Win+H is still held" : "start");
                if (cycle == 0) {
                    await Task.Delay(120);
                    if (!ModifiersDown() || sentBytes == 0) throw new Exception("Audio must reach recognition while Win+H remains held");
                }
            } finally {
                if (cycle == 0) {
                    var release = new[] { Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true) };
                    Native.SendInput(2, release, Marshal.SizeOf(typeof(Native.Input)));
                }
            }
            if (!Visible || frameFailure != null || firstFrames != cycle + 1) throw new Exception(frameFailure ?? "Win+H on must show exactly once");
            double entrance = revealAt;
            Reveal();
            if (entrance != revealAt || RevealProgress != 1) throw new Exception("Repeated recall must not replay an entrance");
            await Task.Delay(120);
            if (Native.SendInput((uint)press.Length, press, Marshal.SizeOf(typeof(Native.Input))) != press.Length) throw new Exception("Stop injection failed");
            await WaitCycleState("idle", cycle, "stop");
            if (!Visible) throw new Exception("Win+H pause must retain the visible window");
            HandleShortcut(1);
            await WaitCycleState("recording", cycle, "resume while window remains visible");
            if (!Visible || firstFrames != cycle + 1) throw new Exception("Resume must not hide, show or replay the first frame");
            HandleShortcut(2);
            await WaitCycleState("idle", cycle, "Escape is the only dismiss action");
            if (Visible) throw new Exception("Escape must hide the window");
        }
        HandleShortcut(1);
        HandleShortcut(1);
        await Task.Delay(100);
        if (Visible || opening || startOnReveal || state != "idle") throw new Exception("Rapid open-close must stay hidden without flashing");
        HandleShortcut(1);
        await WaitCycleState("recording", 3, "start before rapid restart");
        HandleShortcut(1);
        if (state != "busy") throw new Exception("Rapid restart probe must enter tail finalization");
        HandleShortcut(1);
        await WaitCycleState("recording", 3, "restart requested during tail finalization");
        HandleShortcut(1);
        HandleShortcut(1);
        HandleShortcut(2);
        await WaitCycleState("idle", 4, "Escape cancels queued restart");
        await Task.Delay(100);
        if (Visible || startOnReveal || state != "idle") throw new Exception("Escape must cancel queued restart and remain hidden");
        await Dismiss();
        if (frameFailure != null) throw new Exception(frameFailure);
        File.WriteAllText(Path.Combine(root, "work", "microphone-hotkey-cycle-test.txt"), "PASS: Win+H key-down capture while held / pause retains window / resume without visibility replay / only Escape dismisses / first frame circle in real TextBox / first frame capsule on real Button / old cache does not affect first frame / no entrance animation or replay / restart queued during tail finalization / Escape cancels queued restart / real keyboard hook / real microphone / real SenseVoice / no text sent to other apps / no audio saved");
        } finally { VisibleChanged -= firstFrame; }
      }
    }
    async Task WaitCycleState(string expected, int cycle, string phase) {
        var wait = Stopwatch.StartNew();
        while (state != expected && wait.ElapsedMilliseconds < 6000) await Task.Delay(20);
        if (state != expected) throw new Exception("cycle=" + cycle + " phase=" + phase + " expected=" + expected + " actual=" + state + " pending=" + startOnReveal + " visible=" + Visible + " modifiers=" + ModifiersDown() + " description=" + AccessibleDescription);
    }
    static bool WaitShortcut(WaitHandle signal) {
        var watch = Stopwatch.StartNew();
        while (!signal.WaitOne(0) && watch.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(5); }
        return watch.ElapsedMilliseconds < 2000;
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
        if (args.Length > 0 && args[0] == "--target-probe") { Environment.ExitCode = RunTargetProbe(args, root); return; }
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
        if (args.Length > 0 && args[0] == "--hotkey-test") {
            if (ModifiersDown()) throw new Exception("Release modifier keys before hotkey test");
            using (var invoked = new AutoResetEvent(false)) {
                int last = 0, calls = 0, edits = 0;
                using (var hook = new DictationShortcut(action => { Interlocked.Exchange(ref last, action); Interlocked.Increment(ref calls); invoked.Set(); })) {
                  using (var editGuard = new InteractionGuard(point => false, reason => Interlocked.Increment(ref edits))) {
                    hook.Available = 1;
                    try {
                        var press = new[] { Native.KeyInput(0x5B, false), Native.KeyInput(0x48, false), Native.KeyInput(0x48, false), Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true) };
                        for (int i = 0; i < press.Length; i++) press[i].data.keyboard.extra = UIntPtr.Zero;
                        if (Native.SendInput((uint)press.Length, press, Marshal.SizeOf(typeof(Native.Input))) != press.Length) throw new Exception("Win+H test injection failed");
                        if (!WaitShortcut(invoked) || Volatile.Read(ref last) != 1 || Volatile.Read(ref calls) != 1) throw new Exception("Real Win+H hook/repeat suppression failed");
                        var escape = new[] { Native.KeyInput(0x1B, false), Native.KeyInput(0x1B, true) };
                        for (int i = 0; i < escape.Length; i++) escape[i].data.keyboard.extra = UIntPtr.Zero;
                        if (Native.SendInput(2, escape, Marshal.SizeOf(typeof(Native.Input))) != 2) throw new Exception("Escape test injection failed");
                        if (!WaitShortcut(invoked) || Volatile.Read(ref last) != 2 || Volatile.Read(ref calls) != 2 || hook.MaskFailed) throw new Exception("Real Escape hook or Win masking failed");
                        hook.Available = 3;
                        var copy = new[] { Native.KeyInput(0xA2, false), Native.KeyInput(0x43, false), Native.KeyInput(0x43, true), Native.KeyInput(0xA2, true) };
                        for (int i = 0; i < copy.Length; i++) copy[i].data.keyboard.extra = UIntPtr.Zero;
                        if (Native.SendInput((uint)copy.Length, copy, Marshal.SizeOf(typeof(Native.Input))) != copy.Length) throw new Exception("Ctrl+C test injection failed");
                        if (!WaitShortcut(invoked) || Volatile.Read(ref last) != 3 || Volatile.Read(ref calls) != 3 || Volatile.Read(ref edits) != 0) throw new Exception("Shortcut keys must not pause text delivery as user edits");
                        // Replay the actual mouse callback without scrolling or clicking the user's page.
                        var mouseCallback = (Delegate)typeof(InteractionGuard).GetField("mouse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(editGuard);
                        IntPtr mouseData = Marshal.AllocHGlobal(32);
                        try {
                            Marshal.Copy(new byte[32], 0, mouseData, 32);
                            foreach (int message in new[] { 0x20A, 0x20E, 0x200 }) {
                                mouseCallback.DynamicInvoke(0, new IntPtr(message), mouseData);
                                if (Volatile.Read(ref edits) != 0) throw new Exception("Scrolling must not pause text delivery when the caret stays in the input box");
                            }
                            foreach (int message in new[] { 0x201, 0x204, 0x207, 0x20B }) mouseCallback.DynamicInvoke(0, new IntPtr(message), mouseData);
                            if (Volatile.Read(ref edits) != 4) throw new Exception("Mouse button presses must still guard text delivery");
                        } finally { Marshal.FreeHGlobal(mouseData); }
                    } finally {
                        var release = new[] { Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true), Native.KeyInput(0x1B, true), Native.KeyInput(0x43, true), Native.KeyInput(0xA2, true) };
                        Native.SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(Native.Input)));
                    }
                  }
                }
                File.WriteAllText(Path.Combine(root, "work", "microphone-hotkey-test.txt"), "PASS: dedicated hook thread / real SendInput Win+H / key repeat suppressed / real Escape / Ctrl+C / shortcuts bypass edit guard / replayed wheel and movement preserve delivery / replayed mouse buttons still guard delivery / Win release masked / clean unhook; no model, audio or clipboard access");
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--self-test") {
            try {
            var shortcutKeys = new ShortcutKeys(); bool swallowed;
            shortcutKeys.Process(0x5B, true, 0, out swallowed);
            if (swallowed || shortcutKeys.Process(0x48, true, 0, out swallowed) != 1 || !swallowed) throw new Exception("Win+H must be intercepted");
            if (shortcutKeys.Process(0x48, true, 0, out swallowed) != 0 || !swallowed) throw new Exception("Win+H repeat must not toggle twice");
            shortcutKeys.Process(0x5B, false, 0, out swallowed);
            shortcutKeys.Process(0x48, false, 0, out swallowed);
            if (!swallowed) throw new Exception("Captured H release must not reach target after Win release");
            shortcutKeys.Process(0x48, true, 0, out swallowed);
            if (swallowed) throw new Exception("Ordinary H must pass");
            shortcutKeys.Process(0x48, false, 0, out swallowed);
            if (shortcutKeys.Process(0x1B, true, 1, out swallowed) != 2 || !swallowed) throw new Exception("Visible Escape must dismiss");
            shortcutKeys.Process(0x1B, false, 0, out swallowed);
            shortcutKeys.Process(0x1B, true, 0, out swallowed);
            if (swallowed) throw new Exception("Hidden Escape must pass");
            shortcutKeys.Process(0x1B, false, 0, out swallowed);
            shortcutKeys.Process(0xA3, true, 0, out swallowed);
            if (shortcutKeys.Process(0x43, true, 3, out swallowed) != 3 || !swallowed) throw new Exception("Ctrl+C must copy ready cache");
            shortcutKeys.Process(0x43, false, 0, out swallowed);
            shortcutKeys.Process(0x43, true, 1, out swallowed);
            if (swallowed) throw new Exception("Ctrl+C without ready cache must pass");
            shortcutKeys.Process(0x43, false, 0, out swallowed);
            shortcutKeys.Process(0x5C, true, 0, out swallowed);
            shortcutKeys.Process(0x48, true, 3, out swallowed);
            if (swallowed) throw new Exception("Win+Ctrl+H must not be assigned");
            shortcutKeys.Process(0x48, false, 0, out swallowed);
            shortcutKeys.Process(0xA3, false, 0, out swallowed);
            if (shortcutKeys.Process(0x48, true, 0, out swallowed) != 1 || !swallowed) throw new Exception("Right Win+H must work");
            string positionTest = Path.Combine(root, "work", "window-position-test.txt");
            Point fallbackPosition = new Point(20, 30);
            try {
                File.WriteAllText(positionTest, "-1500,260");
                if (ReadPosition(positionTest, fallbackPosition) != new Point(-1500, 260)) throw new Exception("Position memory lost negative monitor coordinates");
                File.WriteAllText(positionTest, "broken");
                if (ReadPosition(positionTest, fallbackPosition) != fallbackPosition) throw new Exception("Invalid position must use default");
                File.Delete(positionTest);
                if (ReadPosition(positionTest, fallbackPosition) != fallbackPosition) throw new Exception("Missing position must use default");
            } finally { if (File.Exists(positionTest)) File.Delete(positionTest); }
            if (VisiblePosition(new Point(9000, -400), new Size(128, 96), new Rectangle(0, 0, 1920, 1040)) != new Point(1792, 0))
                throw new Exception("Disconnected monitor position must return to working area");
            if (VisiblePosition(new Point(-1500, 260), new Size(128, 96), new Rectangle(-1920, 0, 1920, 1040)) != new Point(-1500, 260))
                throw new Exception("Visible remembered position must not move");
            using (var shell = new Microphone(root, "--idle-focus-test")) {
                IntPtr foreground = Native.GetForegroundWindow();
                shell.Reveal();
                double started = shell.revealAt;
                Point remembered = shell.Location;
                shell.Reveal();
                if (shell.animationsEnabled && shell.revealAt != started) throw new Exception("Repeated reveal must not restart entrance");
                if (shell.menu.Items.Count != 2 || shell.menu.Items[0].Text != "收起" || shell.menu.Items[1].Text != "退出")
                    throw new Exception("Menu must contain only dismiss and exit");
                shell.menu.Show(shell, new Point(0, 0));
                shell.menu.Items[0].Select();
                using (var preview = new Bitmap(shell.menu.Width, shell.menu.Height)) {
                    shell.menu.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(root, "work", "microphone-menu.png"));
                }
                if (shell.menu.Region == null || shell.menu.Region.IsVisible(Point.Empty) || !shell.menu.Region.IsVisible(20, 20))
                    throw new Exception("Menu must have rounded corners and preserve item hit targets");
                shell.menu.Close();
                shell.menu.Items[0].PerformClick();
                if (shell.Visible || shell.focusWatch.Enabled || shell.animation.Enabled) throw new Exception("Dismiss must hide and stop idle timers");
                shell.Reveal();
                if (!shell.Visible || shell.Location != remembered) throw new Exception("Recall must retain position");
                if (Native.GetForegroundWindow() != foreground) throw new Exception("Reveal stole the input focus");
                shell.revealAt = shell.motionClock.Elapsed.TotalMilliseconds - 180;
                if (shell.RevealProgress != 1) throw new Exception("Entrance must finish in 180ms");
                shell.state = "idle"; shell.recovery = "已定稿的缓存";
                shell.HandleShortcut(3);
                if (shell.copiedInTest != shell.recovery || !shell.recoveryCopied) throw new Exception("Ctrl+C action must copy and retain ready cache");
                if (shell.ShortcutCopyReady) throw new Exception("Successful cache copy must release Ctrl+C to the target app");
                shell.copiedInTest = "";
                shell.HandleShortcut(3);
                if (shell.copiedInTest.Length != 0) throw new Exception("Repeated Ctrl+C must not overwrite the clipboard with old cache");
                shell.KeepRecovery("新缓存");
                if (!shell.ShortcutCopyReady) throw new Exception("New cached text must allow one more shortcut copy");
                shell.copyText = text => { throw new ExternalException("Clipboard busy"); };
                shell.HandleShortcut(3);
                if (!shell.ShortcutCopyReady) throw new Exception("Failed clipboard write must retain the shortcut retry");
                shell.copyText = text => shell.copiedInTest = text;
                shell.HandleShortcut(3);
                if (shell.ShortcutCopyReady || shell.copiedInTest != shell.recovery) throw new Exception("Retry must copy new cache then release Ctrl+C");
                shell.copiedInTest = "";
                shell.CopyRecovery();
                if (shell.copiedInTest != shell.recovery) throw new Exception("Copy button must still allow manual recopy");
                shell.startOnReveal = true; shell.state = "loading";
                if (shell.TakeRevealStart() || !shell.startOnReveal) throw new Exception("Recall must wait for model readiness");
                shell.state = "idle";
                if (!shell.TakeRevealStart() || shell.TakeRevealStart()) throw new Exception("Recall must start exactly once without a modifier-release gate");
                shell.startOnReveal = true; shell.state = "busy";
                if (shell.TakeRevealStart() || !shell.startOnReveal) throw new Exception("Busy recall must wait");
                shell.state = "recording";
                if (shell.TakeRevealStart() || shell.startOnReveal) throw new Exception("Recall during recording must not toggle or queue another session");
                shell.state = "error"; shell.startOnReveal = true;
                if (!shell.TakeRevealStart()) throw new Exception("Recall must allow worker recovery");
                shell.startOnReveal = true; shell.menu.Items[0].PerformClick();
                if (shell.startOnReveal || shell.TakeRevealStart()) throw new Exception("Dismiss must cancel queued recording");
                if (shell.recovery != "已定稿的缓存" + Environment.NewLine + "新缓存") throw new Exception("Dismiss must preserve fallback cache");
                shell.Close();
            }
            using (var source = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(source))
            using (var revealFrames = new Bitmap(128 * 5, 96)) using (var preview = Graphics.FromImage(revealFrames)) {
                DrawShape(ink, 0, "loading", false, 0, 0, 0);
                int previousAlpha = -1;
                for (int i = 0; i < 5; i++) using (var frame = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(frame)) {
                    DrawReveal(layer, source, DictationMotion.Ease(i / 4.0));
                    int alpha = frame.GetPixel(64, 48).A;
                    if (alpha < previousAlpha || (i == 0 && alpha != 0) || (i == 4 && alpha != source.GetPixel(64, 48).A))
                        throw new Exception("Reveal must fade from transparent to full surface");
                    previousAlpha = alpha; preview.DrawImageUnscaled(frame, i * 128, 0);
                }
                revealFrames.Save(Path.Combine(root, "work", "reveal-preview.png"));
            }
            if (Marshal.SizeOf(typeof(Native.Input)) != (IntPtr.Size == 8 ? 40 : 28)) throw new Exception("INPUT layout");
            if (!Deliverable(TargetKind.Unknown)) throw new Exception("Unknown target must stay deliverable");
            if (Deliverable(DecideTarget(false, false)) || Deliverable(DecideTarget(true, false)))
                throw new Exception("Visible non-text or read-only targets must stay in fallback");
            if (DecideTarget(true, true) != TargetKind.Editable || !Deliverable(TargetKind.Editable))
                throw new Exception("Writable text host must deliver");
            int focusedState = (int)(AccessibleStates.Focused | AccessibleStates.Focusable);
            if (DecideAccessibleTarget((int)AccessibleRole.Text, focusedState) != TargetKind.Editable)
                throw new Exception("Focused MSAA input must deliver even when UIA only reports a document");
            foreach (var blocked in new[] { AccessibleStates.ReadOnly, AccessibleStates.Unavailable, AccessibleStates.Invisible, AccessibleStates.Offscreen })
                if (DecideAccessibleTarget((int)AccessibleRole.Text, focusedState | (int)blocked) != TargetKind.ReadOnly)
                    throw new Exception("Unavailable or read-only MSAA text must stay in cache");
            if (DecideAccessibleTarget((int)AccessibleRole.Document, focusedState | (int)AccessibleStates.ReadOnly) != TargetKind.ReadOnly
                || DecideAccessibleTarget((int)AccessibleRole.PushButton, focusedState) != TargetKind.ReadOnly)
                throw new Exception("Focused preview document and buttons must not receive dictation");
            if (DecideAccessibleTarget((int)AccessibleRole.Document, (int)AccessibleStates.ReadOnly) != TargetKind.Unknown)
                throw new Exception("Unfocused whole-window placeholder must remain unknown");
            if (DecideAccessibleTarget((int)AccessibleRole.Pane, focusedState) != TargetKind.Unknown)
                throw new Exception("Coarse host placeholders must not veto custom input controls");
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
                if (shell.copiedInTest.Length != 0 || shell.recovery != "保留的缓存" || !shell.AccessibleDescription.Contains("左侧重试"))
                    throw new Exception("Error capsule left side must retry without copying or clearing memory");
            }
            var visualFailures = new List<string>();
            var visualMetrics = new List<string>();
            for (int labelState = 0; labelState < 5; labelState++)
            using (var label = new Bitmap(106, 42)) using (var ink = Graphics.FromImage(label)) {
                DrawMemory(ink, labelState == 3, labelState == 3 ? 1 : 0, backdrop: false,
                    pending: labelState == 1, transcribing: labelState == 2, stashed: labelState == 0);
                int left = 106, right = 0, top = 42, bottom = 0;
                for (int x = 46; x < 104; x++) for (int y = 7; y < 31; y++) if (label.GetPixel(x, y).A > 80) {
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                double cx = (left + right) / 2.0, cy = (top + bottom) / 2.0;
                visualMetrics.Add("label=" + labelState + " inkCenter=" + cx + "," + cy);
                if (left > right || Math.Abs(cx - 74) > 1 || Math.Abs(cy - 21) > 1)
                    visualFailures.Add("Status label ink must be centered in its shared slot: " + labelState);
            }
            foreach (string pulseState in new[] { "recording", "busy" })
            using (var pulse = new Bitmap(72, 72)) using (var ink = Graphics.FromImage(pulse)) {
                DrawListening(ink, pulseState, 1, 0, 0, 600, false);
                int ringPixels = 0;
                for (int x = 0; x < 72; x++) for (int y = 0; y < 72; y++) {
                    double radius = Math.Sqrt((x - 36) * (x - 36) + (y - 36) * (y - 36));
                    if (radius >= 23 && radius <= 35 && pulse.GetPixel(x, y).A > 12) ringPixels++;
                }
                visualMetrics.Add(pulseState + " ringPixels=" + ringPixels);
                if (ringPixels < 80) visualFailures.Add("Outward listening rings must remain visible: " + pulseState);
            }
            File.WriteAllLines(Path.Combine(root, "work", "visual-feedback-regression.txt"), visualMetrics.ToArray());
            if (visualFailures.Count > 0) throw new Exception(String.Join(" / ", visualFailures));
            int[] glyphHeights = new int[2];
            for (int mode = 0; mode < 2; mode++)
            using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                DrawShape(layer, mode, "recording", false, 0, 0, 0, 1, 0, 0, 600);
                int top = 96, bottom = 0;
                int cx = mode == 0 ? 64 : 35;
                for (int x = cx - 20; x <= cx + 20; x++) for (int y = 26; y < 70; y++) {
                    Color pixel = cell.GetPixel(x, y);
                    if (pixel.R > 210 && pixel.G > 210 && pixel.B > 210) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                }
                glyphHeights[mode] = bottom - top + 1;
            }
            File.AppendAllText(Path.Combine(root, "work", "visual-feedback-regression.txt"), "microphoneHeights=" + String.Join(",", glyphHeights) + Environment.NewLine);
            if (glyphHeights[0] < 28 || glyphHeights[0] < glyphHeights[1] * 1.45)
                throw new Exception("The circle microphone must be visibly larger than the capsule microphone");
            foreach (double phase in new[] { 0.0, .25, .5, .75, 1, 1.16 })
            using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                DrawShape(layer, phase, "idle", false, 0, 0, 0, stashed: true);
                int cx = (int)(64 - 29 * phase), inkPixels = 0;
                for (int x = cx - 14; x <= cx + 14; x++) for (int y = 30; y < 68; y++) {
                    Color pixel = cell.GetPixel(x, y);
                    if (pixel.R > 210 && pixel.G > 210 && pixel.B > 210) inkPixels++;
                }
                if (inkPixels < 30) throw new Exception("Idle microphone must not vanish halfway through the jelly morph");
            }
            foreach (bool checkedIcon in new[] { false, true })
            using (var cell = new Bitmap(106, 42)) using (var layer = Graphics.FromImage(cell)) {
                DrawMemory(layer, checkedIcon, backdrop: false);
                int left = 106, right = 0, top = 42, bottom = 0;
                for (int x = 5; x < 42; x++) for (int y = 5; y < 37; y++) if (cell.GetPixel(x, y).A > 100) {
                    left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                if (right - left < 12 || right - left > 24 || bottom - top < 9 || bottom - top > 24
                    || Math.Abs((left + right) / 2.0 - 24) > 1.5 || Math.Abs((top + bottom) / 2.0 - 21) > 1.5)
                    throw new Exception("Copy and check icons must keep a centered, legible capsule scale");
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
            using (var idle = new Bitmap(106, 42)) using (var ink = Graphics.FromImage(idle)) {
                DrawMemory(ink, false, backdrop: false, stashed: true);
                if (idle.GetPixel(48, 12).A != 0 || idle.GetPixel(70, 34).A != 0 || idle.GetPixel(10, 21).A != 0)
                    throw new Exception("Idle capsule must have no divider, underline or icon disc");
                int iconPixels = 0, textPixels = 0;
                for (int x = 18; x < 98; x++) for (int y = 8; y < 33; y++)
                    if (idle.GetPixel(x, y).A > 100) { if (x < 35) iconPixels++; else if (x > 40) textPixels++; }
                if (iconPixels < 30 || textPixels < 80) throw new Exception("Idle capsule must show microphone and inactive label");
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
                if (changed < 150) throw new Exception("Listening material must breathe gently in both shapes");
            }
            // Material stays on the full silhouette while content and spring direction change.
            foreach (double phase in new[] { -.08, 0, .25, .5, .75, 1, 1.16 })
            using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                DrawShape(layer, phase, "idle", false, 0, 0, 0);
                SizeF size = ShapeSize(phase);
                Color face = cell.GetPixel(64, (int)(48 - size.Height * .3));
                if (face.A < 250 || face.B <= face.R || cell.GetPixel(0, 0).A != 0)
                    throw new Exception("Embedded graphite artwork must cover the face and preserve transparent corners throughout morphs");
                using (var active = new Bitmap(128, 96)) using (var activeInk = Graphics.FromImage(active)) {
                    DrawShape(activeInk, phase, "recording", false, 0, 0, 0, 1, 1, .7f, 600);
                    Color activeFace = active.GetPixel(64, (int)(48 - size.Height * .3));
                    if (Math.Abs(activeFace.A - face.A) > 1)
                        throw new Exception("Listening must preserve the embedded background coverage");
                }
            }
            foreach (string glyphState in new[] { "recording", "error" })
            using (var glyph = new Bitmap(72, 72)) using (var layer = Graphics.FromImage(glyph)) {
                DrawListening(layer, glyphState, 1, 0, 0, 600, false);
                if (glyph.GetPixel(36, 36).R < 230 || glyph.GetPixel(16, 36).A != 0)
                    throw new Exception("Active microphone must stay white without an opaque inner disc");
            }
            using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                DrawShape(layer, 0, "idle", false, 0, 0, 0);
                Color left = cell.GetPixel(44, 44), right = cell.GetPixel(84, 44);
                if (Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B) < 6)
                    throw new Exception("The reference wallpaper must be visible inside the widget, not only in the preview background");
            }
            foreach (double phase in new[] { 0.0, 1.0 })
            using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                DrawShape(layer, phase, "idle", false, .5f, 0, 0);
                SizeF size = ShapeSize(phase); int x = (int)(64 + size.Width * .22);
                Color crown = cell.GetPixel(x, (int)(48 - size.Height * .4)), core = cell.GetPixel(x, 48);
                File.AppendAllText(Path.Combine(root, "work", "visual-feedback-regression.txt"), "material=" + phase + " crown=" + crown + " core=" + core + Environment.NewLine);
                if (crown.R + crown.G + crown.B - core.R - core.G - core.B < 15)
                    throw new Exception("The curved reference highlight must stay brighter than the material core in both shapes");
            }
            using (var glyph = new Bitmap(106, 42)) using (var layer = Graphics.FromImage(glyph)) {
                DrawMemory(layer, false, backdrop: false);
                if (glyph.GetPixel(10, 21).A != 0 || glyph.GetPixel(48, 12).A != 0)
                    throw new Exception("Copy feedback must not reintroduce an icon well or divider");
            }
            using (var sheet = new Bitmap(128 * 4, 124 * 3)) using (var ink = Graphics.FromImage(sheet))
            using (var font = new Font("Microsoft YaHei UI", 8.5f)) {
                string[] labels = { "圆形 · 关闭", "圆形 · 监听", "圆形 · 讲话", "圆形 · 错误",
                    "胶囊 · 未开启", "胶囊 · 监听", "胶囊 · 转写", "胶囊 · 已复制",
                    "形变 · 25%", "形变 · 50%", "悬停", "按下" };
                for (int i = 0; i < labels.Length; i++) {
                    int x = i % 4 * 128, y = i / 4 * 124;
                    using (var backdrop = new LinearGradientBrush(new Rectangle(x, y, 128, 124),
                        Color.FromArgb(104, 146, 212), Color.FromArgb(227, 192, 207), 35)) ink.FillRectangle(backdrop, x, y, 128, 124);
                    using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                        double phase = i < 4 ? 0 : i == 8 ? .25 : i == 9 ? .5 : 1;
                        bool listening = i == 1 || i == 2 || i == 5 || i == 6;
                        DrawShape(layer, phase, i == 3 ? "error" : listening ? "recording" : "idle", i == 7, i == 7 ? 1 : 0,
                            i == 10 ? 1 : 0, i == 11 ? 1 : 0, listening ? 1 : 0, i == 2 || i == 6 ? 1 : 0, .7f, 600,
                            i == 5, i == 6, 1, i == 4);
                        ink.DrawImageUnscaled(cell, x, y);
                    }
                    using (var label = new SolidBrush(Color.FromArgb(33, 39, 51)))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center })
                        ink.DrawString(labels[i], font, label, new RectangleF(x, y + 98, 128, 24), format);
                }
                sheet.Save(Path.Combine(root, "work", "graphite-material-states.png"));
            }
            string referencePath = Path.Combine(root, "work", "material-reference-target.png");
            if (File.Exists(referencePath)) using (var reference = new Bitmap(referencePath))
            using (var preview = new Bitmap(reference)) using (var ink = Graphics.FromImage(preview)) {
                ink.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var circle = new Bitmap(72, 72)) using (var layer = Graphics.FromImage(circle)) {
                    DrawListening(layer, "idle", 0, 0, 0, 0);
                    ink.DrawImage(circle, new Rectangle(10, 5, 96, 96));
                }
                using (var pill = new Bitmap(106, 42)) using (var layer = Graphics.FromImage(pill))
                using (var outline = MicrophoneMenuRenderer.Outline(new Rectangle(0, 0, 105, 41), 21)) {
                    layer.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawMaterial(layer, outline, outline.GetBounds(), 0, 0, 0, 0);
                    DrawMicrophoneGlyph(layer, 24, 21, 1, .95f);
                    DrawVoice(layer, 74, 21, 1, .8f, 0, false, Color.FromArgb(245, 246, 250));
                    ink.DrawImage(pill, new Rectangle(125, 16, 193, 76));
                }
                preview.Save(Path.Combine(root, "work", "graphite-reference-render.png"));
                using (var comparison = new Bitmap(reference.Width, reference.Height * 2)) using (var compare = Graphics.FromImage(comparison)) {
                    compare.DrawImageUnscaled(reference, 0, 0); compare.DrawImageUnscaled(preview, 0, reference.Height);
                    comparison.Save(Path.Combine(root, "work", "graphite-reference-comparison.png"));
                }
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
            double maximum = 0, previousShape = 0; int reach90 = 0, reach95 = 0;
            for (int i = 1; i <= 120; i++) {
                morph.Update(true, i * 16, false); maximum = Math.Max(maximum, morph.Value);
                if (morph.Value < previousShape || morph.Value > 1.00001)
                    throw new Exception("Smooth spring must converge without bouncing past the capsule");
                if (reach90 == 0 && morph.Value >= .9) reach90 = i * 16;
                if (reach95 == 0 && morph.Value >= .95) reach95 = i * 16;
                previousShape = morph.Value;
            }
            if (reach90 > 300 || reach90 < 200 || reach95 > 350 || morph.Value != 1 || !morph.Settled)
                throw new Exception("Smooth spring must complete its main change around 250-300ms and settle");
            File.WriteAllText(Path.Combine(root, "work", "smooth-motion-test.txt"), "reach90Ms=" + reach90 + "; reach95Ms=" + reach95 + "; maximum=" + maximum);
            morph.Update(false, 1936, false); double interruptedShape = morph.Value;
            double interruptedVelocity = morph.Velocity;
            morph.Update(true, 1936, false);
            if (morph.Value != interruptedShape || morph.Velocity != interruptedVelocity) throw new Exception("Morph reversal must preserve position and velocity");
            for (int i = 1; i <= 24; i++) {
                morph.Update(i % 2 == 0, 1936 + i * 16, false);
                if (morph.Value < 0 || morph.Value > 1) throw new Exception("Rapid retargeting must remain within the two shapes");
            }
            previousShape = morph.Value;
            for (int i = 1; i <= 120; i++) {
                morph.Update(false, 2320 + i * 16, false);
                if (morph.Value > previousShape + .00001 || morph.Value < 0) throw new Exception("Return to the circle must converge without bounce");
                previousShape = morph.Value;
            }
            if (morph.Value != 0 || !morph.Settled) throw new Exception("Circle return must settle");
            morph.Update(false, 4256, true);
            if (morph.Value != 0 || morph.Velocity != 0 || !morph.Settled) throw new Exception("Reduced motion must switch instantly");
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.state = "idle"; shell.recovery = "交互回归缓存";
                shell.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                if (shell.copiedInTest.Length != 0) throw new Exception("Release without a press must not copy");
                shell.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                shell.Capture = false;
                shell.OnMouseCaptureChanged(EventArgs.Empty);
                shell.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                if (shell.copiedInTest.Length != 0) throw new Exception("Lost capture must cancel release action");
                shell.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                shell.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 128, 48, 0));
                if (shell.copiedInTest.Length != 0) throw new Exception("Release outside the surface must not copy");
                shell.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                shell.OnMouseLeave(EventArgs.Empty);
                if (shell.TakeLongPress(shell.pressedAt + 500)) throw new Exception("Leaving the surface must cancel long press");
                shell.OnMouseEnter(EventArgs.Empty);
                shell.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                if (shell.copiedInTest.Length != 0) throw new Exception("Reentry must not revive a cancelled click");
                shell.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                shell.OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 88, 48, 0));
                if (shell.copiedInTest != shell.recovery) throw new Exception("Fresh click after cancellation must copy");
                Point resting = shell.Location;
                shell.down = new Point(Cursor.Position.X - 20, Cursor.Position.Y);
                shell.origin = resting;
                shell.OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 88, 48, 0));
                if (shell.Location != resting) throw new Exception("Move without a press must not drag");
                shell.hovered = shell.pressed = true;
                shell.Dismiss().GetAwaiter().GetResult();
                if (shell.pressed || shell.hovered || shell.Capture) throw new Exception("Dismiss must clear pointer feedback and capture");
            }
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
                string previewState = now < 300 || now >= 4500 ? "idle" : "recording";
                float previewLevel = (now >= 900 && now < 1300) || (now >= 2400 && now < 2900) ? .55f : 0;
                motion.Update(previewState, previewLevel, now, false);
                morph.Update(now >= 1500 && now < 4200, now, false);
                using (var bitmap = new Bitmap(128, 96)) using (var g = Graphics.FromImage(bitmap)) {
                    DrawShape(g, morph.Value, previewState, false, 0, 0, 0, motion.Activity, motion.Voice, motion.Level, now, now < 3300, now >= 2400 && now < 3300, now >= 3300 ? 1 : 0);
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
            File.WriteAllText(Path.Combine(root, "work", "microphone-self-test.txt"), "PASS: Win+H and Escape / contextual Ctrl+C / automatic recall start / model readiness / no modifier-release gate / recording no-op / dismiss cancels pending start / reveal fade / repeat recall / no activation / dismiss timers / position parsing and monitor clamp / x64 INPUT / Unicode / coarse-host delivery / read-only and non-text veto / idle shape follows focus / append-only text / English spaces / duplicate and revision guards / audio levels / Windows dictation earcon resources / quiet microphone / fine five-bar waveform in circle and capsule / speech hold / silence return / interruption / reduced motion / embedded wallpaper through morph and overshoot / centered status ink / circle and capsule icon proportions / no midpoint icon disappearance / outward listening and processing rings / unified white glyphs without inner discs / state rendering / smooth critically damped spring / velocity-preserving retargeting / long press restart guard / direct copy pill without dot / manual memory copy (test sink) / clear old cache on successful new round / append only undelivered text / focus changes keep recording / clipboard busy and retry; input devices=" + Recorder.waveInGetNumDevs());
            } catch (Exception error) {
                File.WriteAllText(Path.Combine(root, "work", "microphone-self-test.txt"), "FAIL: " + error);
                Environment.ExitCode = 1;
            }
            return;
        }
        bool created;
        bool testing = args.Length > 0 && (args[0] == "--smoke-test" || args[0] == "--stream-test" || args[0] == "--memory-test" || args[0] == "--hotkey-cycle-test");
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
