using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
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
[assembly: AssemblyVersion("1.6.94.0")]

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] internal static extern IntPtr Cue(IntPtr window, int message, IntPtr wparam, string text);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    internal static string ClassOf(IntPtr window) {
        var name = new StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    internal static bool NativeReadOnly(IntPtr focus) {
        string name = ClassOf(focus);
        return (name.Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0) && (GetWindowLong(focus, -16) & 0x800) != 0;
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
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr first, IntPtr second, uint flags, uint timeout, out UIntPtr result);
    internal static bool CharacterMessageTarget(string windowClass, string processName) {
        return windowClass.StartsWith("Qt", StringComparison.Ordinal) &&
            (string.Equals(processName, "Weixin", StringComparison.OrdinalIgnoreCase) || string.Equals(processName, "WeChat", StringComparison.OrdinalIgnoreCase));
    }
    internal static uint SendToTarget(IntPtr window, IntPtr focus, Input[] inputs, Func<bool> permitted = null) {
        if (NativeReadOnly(focus)) return 0;
        bool direct = false;
        string windowClass = ClassOf(window);
        if (windowClass.StartsWith("Qt", StringComparison.Ordinal)) try {
            uint pid; WindowProcessId(window, out pid);
            using (var process = Process.GetProcessById((int)pid)) direct = CharacterMessageTarget(windowClass, process.ProcessName);
        } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        return direct ? SendCharacterMessages(window, focus, inputs, permitted) : SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] internal static extern uint WindowProcessId(IntPtr window, out uint process);
    // WeChat corrupts VK_PACKET after full-width punctuation; retain paired-input accounting.
    internal static uint SendCharacterMessages(IntPtr window, IntPtr focus, Input[] inputs, Func<bool> permitted = null) {
        uint completed = 0;
        for (int i = 0; i + 1 < inputs.Length; i += 2) {
            if (GetForegroundWindow() != window || focus == IntPtr.Zero || Focus(window) != focus || NativeReadOnly(focus) || (permitted != null && !permitted())) break;
            var down = inputs[i].data.keyboard; UIntPtr result;
            if (down.flags == 4 && down.vk == 0) {
                if (down.scan == '\r' || down.scan == '\n') break; // Never turn dictated text into WeChat's send action.
                if (SendMessageTimeout(focus, 0x102, new IntPtr(down.scan), new IntPtr(1), 3, 80, out result) == IntPtr.Zero) break;
                completed += 2;
            } else if (down.vk == 8 && down.flags == 0) {
                if (SendMessageTimeout(focus, 0x100, new IntPtr(8), new IntPtr(1), 3, 80, out result) == IntPtr.Zero) break;
                completed++;
                if (SendMessageTimeout(focus, 0x101, new IntPtr(8), new IntPtr(unchecked((int)0xC0000001)), 3, 80, out result) == IntPtr.Zero) break;
                completed++;
            } else break;
        }
        return completed;
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

sealed class DraftText
{
    internal string Text = "", Separator = "";
    internal bool InPlace;
    // No writable text store: the segment can only grow, never be rewritten.
    internal bool AppendOnly;
    // Previous preview decode (punctuation removed). Text that two decodes agree on is
    // no longer where the recogniser changes its mind, and can be shown on a host that
    // has no way to take it back.
    internal string Candidate = "";
    // Commit target computed for the current decode, kept while it is being typed out.
    internal string Target = "";
    internal ulong Segment;
}

sealed class TextReveal
{
    internal Dictionary<string, object> Segment;
    internal string Key;
    internal bool Final, AppendOnly;
    internal double Started, Next;
    // Append-only hosts show previews too, so their pacing clock restarts whenever
    // the target grows; Target is what the current burst is aiming at.
    internal string Target = "";
    internal TextDelivery Owner;
    // A host without a draft interface only accepts finalized text, and once appended it
    // cannot be rewritten. There the only honest progress signal is typing: one text
    // element per beat. A 240ms catch-up would paste the whole segment in one blink.
    internal const double AppendStepMs = 70, AppendWindowMs = 1800;
    // A preview that would rewrite most of the segment waits for the final instead;
    // deleting and retyping a long run mid-draft only makes the box flicker.
    internal const int ReviseLimit = 8;
    // shortcut: ASR has no character timestamps; pace arriving text until timestamped streaming is available.
    internal static string Step(string shown, string wanted, double age) {
        return Pace(shown, wanted, age, 35, 240);
    }
    internal static string Append(string shown, string wanted, double age) {
        return Pace(shown, wanted, age, AppendStepMs, AppendWindowMs);
    }
    // The first visible element starts the clock, so it must be exactly one element:
    // a burst on the opening frame reads as a paste even when the rest types out.
    internal static string First(string wanted) {
        if (wanted.Length == 0) return wanted;
        int[] boundaries = StringInfo.ParseCombiningCharacters(wanted);
        return boundaries.Length <= 1 ? wanted : wanted.Substring(0, boundaries[1]);
    }
    static string Pace(string shown, string wanted, double age, double beat, double window) {
        if (wanted.Length <= shown.Length) return wanted;
        int[] boundaries = StringInfo.ParseCombiningCharacters(wanted);
        int index = 0;
        while (index < boundaries.Length && boundaries[index] < shown.Length) index++;
        // Catch up before the window closes instead of accumulating typing lag. The append
        // path uses a wide window so each element keeps its own frame, which also survives
        // a stalled UI tick that would otherwise dump the remaining burst at once.
        int count = Math.Max(1, (int)Math.Ceiling((boundaries.Length - index) * beat / Math.Max(beat, window - age)));
        int end = Math.Min(boundaries.Length, index + count);
        return end == boundaries.Length ? wanted : wanted.Substring(0, boundaries[end]);
    }
}

sealed class TextDelivery
{
    internal readonly Dictionary<string, DraftText> Drafts = new Dictionary<string, DraftText>();
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

// The pill never activates, so it never receives WM_KILLFOCUS and a ContextMenuStrip shown from it
// cannot rely on deactivation to close. While the menu is open we watch real clicks ourselves and
// dismiss it when one lands anywhere outside its surface.
sealed class MenuDismissHook : IDisposable
{
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public Point point; public uint data, flags, time; public UIntPtr extra; }
    readonly Hook callback;
    IntPtr handle;
    internal static bool Outside(Rectangle surface, Point point) { return !surface.Contains(point); }
    internal MenuDismissHook(Func<Rectangle> surface, Action dismiss) {
        callback = (code, message, data) => {
            long kind = message.ToInt64();
            if (code >= 0 && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B)) {
                var value = (Mouse)Marshal.PtrToStructure(data, typeof(Mouse));
                if (Outside(surface(), value.point)) dismiss();
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        handle = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
    }
    public void Dispose() {
        if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}

sealed class ShortcutKeys
{
    readonly HashSet<int> held = new HashSet<int>(), captured = new HashSet<int>();
    internal void RefreshModifiers() {
        // Other hooks can consume releases; historical events are not physical key state.
        foreach (int key in new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) {
            if (Native.GetAsyncKeyState(key) < 0) held.Add(key); else held.Remove(key);
        }
    }
    internal int Process(int key, bool down, int available, out bool swallow) {
        if (down) held.Add(key); else held.Remove(key);
        swallow = captured.Contains(key);
        if (!down) { captured.Remove(key); return 0; }
        if (swallow) return 0;
        bool win = held.Contains(0x5B) || held.Contains(0x5C);
        bool ctrl = held.Contains(0xA2) || held.Contains(0xA3);
        bool extra = held.Contains(0xA0) || held.Contains(0xA1) || held.Contains(0xA4) || held.Contains(0xA5);
        // Ctrl+C is never captured: the cached transcript is copied by the capsule button only.
        int command = !extra && win && !ctrl && key == 0x48 ? 1 :
            !extra && !win && !ctrl && key == 0x1B && (available & 1) != 0 ? 2 : 0;
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
    readonly ShortcutKeys keys = new ShortcutKeys();
    Control dispatcher;
    ApplicationContext context;
    Exception failure;
    internal volatile int Available;
    internal volatile bool MaskFailed;
    internal DictationShortcut(Action<int> command) {
        using (var ready = new ManualResetEventSlim()) {
            thread = new Thread(() => {
                IntPtr handle = IntPtr.Zero;
                Hook hook = (code, message, data) => {
                    long kind = message.ToInt64();
                    if (code >= 0 && (kind == 0x100 || kind == 0x101 || kind == 0x104 || kind == 0x105)) {
                        int key = Marshal.ReadInt32(data);
                        if ((kind == 0x100 || kind == 0x104) && (key == 0x48 || key == 0x1B)) keys.RefreshModifiers();
                        bool swallow;
                        int action = keys.Process(key, kind == 0x100 || kind == 0x104, Available, out swallow);
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
                    keys.RefreshModifiers();
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

// Vector icons for the right-click menu, drawn at 4x and scaled down so the
// strokes stay smooth at 16 px. Cached per icon+colour; no image assets needed.
static class MenuIcons
{
    static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
    const int Scale = 4;

    internal static Image Get(string name, Color ink) {
        var key = name + "/" + ink.ToArgb();
        lock (cache) {
            Image cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            var bitmap = Draw(name, ink);
            cache[key] = bitmap;
            return bitmap;
        }
    }

    static Image Draw(string name, Color ink) {
        int side = 16 * Scale;  // coordinates below are in 16 px units, multiplied by Scale
        using (var big = new Bitmap(side, side)) {
            using (var g = Graphics.FromImage(big)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(ink, 1.5f * Scale)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (name == "chevron") {
                        var arm = 3.1f * Scale;
                        g.DrawLines(pen, new[] {
                            new PointF(8 * Scale - arm, 6.2f * Scale), new PointF(8 * Scale, 9.4f * Scale),
                            new PointF(8 * Scale + arm, 6.2f * Scale) });
                    } else if (name == "book") {
                        using (var page = new GraphicsPath()) {
                            page.AddLine(8 * Scale, 4.6f * Scale, 3.4f * Scale, 6.2f * Scale);
                            page.AddLine(3.4f * Scale, 6.2f * Scale, 3.4f * Scale, 12.2f * Scale);
                            page.AddLine(3.4f * Scale, 12.2f * Scale, 8 * Scale, 10.6f * Scale);
                            page.AddLine(8 * Scale, 10.6f * Scale, 12.6f * Scale, 12.2f * Scale);
                            page.AddLine(12.6f * Scale, 12.2f * Scale, 12.6f * Scale, 6.2f * Scale);
                            page.AddLine(12.6f * Scale, 6.2f * Scale, 8 * Scale, 4.6f * Scale);
                            g.DrawPath(pen, page);
                        }
                        g.DrawLine(pen, 8 * Scale, 5.0f * Scale, 8 * Scale, 11.2f * Scale);
                    } else if (name == "power") {
                        g.DrawArc(pen, 3.5f * Scale, 4.0f * Scale, 9.0f * Scale, 9.0f * Scale, 300, 300);
                        g.DrawLine(pen, 8 * Scale, 2.6f * Scale, 8 * Scale, 7.6f * Scale);
                    } else if (name == "theme") {
                        // A half filled disc: the product's own mark for appearance.
                        float box = 3.3f * Scale, disc = 9.4f * Scale;
                        g.DrawEllipse(pen, box, box, disc, disc);
                        using (var half = new GraphicsPath()) {
                            half.AddRectangle(new RectangleF(box, box, disc / 2, disc));
                            var saved = g.Save();
                            g.SetClip(half);
                            using (var fill = new SolidBrush(ink)) g.FillEllipse(fill, box, box, disc, disc);
                            g.Restore(saved);
                        }
                    }
                }
            }
            // The scale-down happens only after the drawing Graphics is disposed.
            var small = new Bitmap(16, 16);
            using (var scaled = Graphics.FromImage(small)) {
                scaled.InterpolationMode = InterpolationMode.HighQualityBicubic;
                scaled.PixelOffsetMode = PixelOffsetMode.HighQuality;
                scaled.DrawImage(big, new Rectangle(0, 0, 16, 16));
            }
            return small;
        }
    }
}

sealed class MicrophoneMenuRenderer : ToolStripProfessionalRenderer
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static void SmoothCorners(ToolStripDropDown menu) {
        if (!menu.IsHandleCreated) return;
        // DWM clips with antialiasing; a GDI Region has binary, visibly stepped edges.
        int preference = 2, border = -2;
        DwmSetWindowAttribute(menu.Handle, 33, ref preference, 4);
        DwmSetWindowAttribute(menu.Handle, 34, ref border, 4);
    }
    internal static readonly Color Surface = Color.White;
    internal static readonly Color Ink = Color.FromArgb(43, 48, 56);
    internal static readonly Color Danger = Color.FromArgb(214, 69, 69);
    internal static readonly Color Hover = Color.FromArgb(237, 243, 253);
    internal static readonly Color Border = Color.FromArgb(227, 231, 236);
    internal static readonly Color Divider = Color.FromArgb(234, 237, 241);

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
        e.Graphics.Clear(Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Outline(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 11)) {
            using (var fill = new SolidBrush(Surface)) e.Graphics.FillPath(fill, path);
            using (var pen = new Pen(Border)) e.Graphics.DrawPath(pen, path);
        }
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
        using (var brush = new SolidBrush(Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if (!e.Item.Selected) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Outline(new Rectangle(6, 2, e.Item.Width - 12, e.Item.Height - 4), 8))
        using (var fill = new SolidBrush(Hover)) e.Graphics.FillPath(fill, path);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
        int y = e.Item.Height / 2;
        using (var pen = new Pen(Divider)) e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        // With no image margin WinForms never asks for the image, so it is drawn
        // here at a fixed x; the label starts at 30 and the row is sized to fit.
        if (e.Item.Image != null) {
            int top = (e.Item.Height - 16) / 2;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(e.Item.Image, new Rectangle(8, top, 16, 16));
        }
        e.TextColor = e.Item.Tag is Color ? (Color)e.Item.Tag : Ink;
        e.TextRectangle = new Rectangle(30, 0, Math.Max(0, e.Item.Width - 38), e.Item.Height);
        e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        base.OnRenderItemText(e);
    }
}

sealed class WordGroup
{
    public string Word;
    // Reference corpus rule: "zh" only rewrites Chinese utterances; empty means
    // every utterance. Keep whatever the file said, otherwise re-saving the
    // editor would quietly turn English-only fixes into Chinese-only ones.
    public string Context = "";
    public readonly List<string> Variants = new List<string>();
}

// Keep native editing/selection/IME behavior; normalize only the pasted selection.
sealed class DictionaryTextBox : TextBox
{
    int wheelRemainder;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, string text);
    internal Action<string> Pasted;
    internal Action ViewChanged;
    internal Func<string> ReadPaste = () => Clipboard.GetText();
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        DictionaryScrollBar.SendMessage(Handle, 0x00D3, new IntPtr(3), IntPtr.Zero);
    }
    internal void InsertPaste(string text) {
        string cleaned = DictionaryDialog.CleanVariants(text, true);
        if (cleaned.Length == 0) return;
        string before = Text;
        int start = SelectionStart, length = SelectionLength;
        string prefix = before.Substring(0, start), suffix = before.Substring(start + length);
        // Only complete surrounding lines participate in deduplication.
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int last = prefix.LastIndexOf('\n'), next = suffix.IndexOf('\n');
        string complete = (last < 0 ? "" : prefix.Substring(0, last + 1)) +
                          (next < 0 ? "" : suffix.Substring(next));
        foreach (string line in complete.Split('\n')) existing.Add(line.Trim());
        if ((prefix.Length == 0 || prefix.EndsWith("\n")) &&
            (suffix.Length == 0 || suffix.StartsWith("\r") || suffix.StartsWith("\n"))) {
            var kept = new List<string>();
            foreach (string line in cleaned.Split(new[] { "\r\n" }, StringSplitOptions.None))
                if (existing.Add(line)) kept.Add(line);
            cleaned = string.Join("\r\n", kept.ToArray());
            if (cleaned.Length == 0) { if (Pasted != null) Pasted("这些写法已经存在"); return; }
        }
        // EM_REPLACESEL with undo enabled makes paste + cleanup one native undo step.
        SendMessage(Handle, 0x00C2, new IntPtr(1), cleaned);
        if (Pasted != null) Pasted("已整理粘贴内容 · Ctrl+Z 撤销");
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == 0x020A && Multiline && ScrollBars == ScrollBars.None) {
            int lines = DictionaryScrollBar.WheelLines(ref wheelRemainder, (short)((long)m.WParam >> 16), Math.Max(1, ClientSize.Height / Font.Height));
            DictionaryScrollBar.SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(-lines));
            if (ViewChanged != null) ViewChanged();
            return;
        }
        if (m.Msg == 0x0302 && Pasted != null) {
            try { InsertPaste(ReadPaste()); }
            catch (ExternalException) { Pasted("剪贴板暂不可用，请重试"); }
            return;
        }
        base.WndProc(ref m);
        if (ViewChanged != null && (m.Msg == 0x000F || m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0101 || m.Msg == 0x000C)) ViewChanged();
    }
}

sealed class HistoryMenu : ContextMenuStrip
{
    internal HistoryMenu() { Renderer = new MicrophoneMenuRenderer(); ShowImageMargin = false; Font = new Font("Microsoft YaHei UI", 11f); Padding = new Padding(4); }
    internal void Fit() { FitItems(this); }
    static void FitItems(ToolStripDropDown menu) {
        int width = 120; foreach (ToolStripItem item in menu.Items) if (!(item is ToolStripSeparator)) width = Math.Max(width, TextRenderer.MeasureText(item.Text, menu.Font).Width + 64);
        foreach (ToolStripItem item in menu.Items) {
            item.AutoSize = false; item.Size = new Size(width, item is ToolStripSeparator ? 9 : menu.Font.Height + 16);
            var child = item as ToolStripMenuItem;
            if (child != null && child.HasDropDownItems) { child.DropDown.Font = menu.Font; child.DropDown.Renderer = menu.Renderer; var dropdown = child.DropDown as ToolStripDropDownMenu; if (dropdown != null) dropdown.ShowImageMargin = false; FitItems(child.DropDown); }
        }
    }
    protected override void OnOpening(System.ComponentModel.CancelEventArgs e) { Fit(); base.OnOpening(e); }
}

static class HistoryCard
{
    internal const string TextFamily = "Microsoft YaHei UI";
    internal static readonly Color Canvas = Color.FromArgb(251, 250, 248), Paper = Color.FromArgb(255, 254, 252);
    internal static readonly Color SelectedPaper = Color.FromArgb(237, 244, 250), MarkInk = Color.FromArgb(49, 92, 129);
    internal static readonly Color NumberInk = Color.FromArgb(232, 230, 225), SelectedNumberInk = Color.FromArgb(223, 231, 239);
    internal static Rectangle Bounds(Rectangle row) { return new Rectangle(row.X + 2, row.Y + 3, row.Width - 5, row.Height - 8); }
    internal static void Draw(Graphics graphics, Rectangle row, int number, Font font, bool current, bool marked, bool hover, bool pressed, bool focus) {
        var saved = graphics.Save(); graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var brush = new SolidBrush(Canvas)) graphics.FillRectangle(brush, row);
        Rectangle bounds = Bounds(row);
        using (var shape = MicrophoneMenuRenderer.Outline(bounds, 9)) {
            using (var brush = new SolidBrush(pressed ? Color.FromArgb(225, 233, 240) : marked ? SelectedPaper : hover ? Color.FromArgb(248, 247, 244) : Paper)) graphics.FillPath(brush, shape);
            using (var pen = new Pen(current ? Color.FromArgb(96, 100, 107) : marked ? MarkInk : hover ? Color.FromArgb(138, 137, 133) : Color.FromArgb(190, 187, 181), current || focus ? 1.5f : 1f)) graphics.DrawPath(pen, shape);
            graphics.SetClip(shape, CombineMode.Intersect);
            // Draw the decorative index first: full-width text is allowed over it.
            using (var numberFont = new Font(font.FontFamily, Math.Min(44, bounds.Height * .66f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(marked || pressed ? SelectedNumberInk : NumberInk))
            using (var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(number.ToString(), numberFont, brush, new Rectangle(bounds.X + 8, bounds.Y, bounds.Width - 16, bounds.Height + 2), format);
            if (current) using (var brush = new SolidBrush(Color.FromArgb(96, 100, 107))) graphics.FillRectangle(brush, bounds.X + 1, bounds.Y + 10, 2, bounds.Height - 20);
        }
        graphics.Restore(saved);
    }
    internal static void Check(Graphics graphics, Rectangle box, bool value, bool hover, bool pressed, bool focus, bool enabled) {
        var saved = graphics.Save(); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (hover || focus) using (var halo = MicrophoneMenuRenderer.Outline(new Rectangle(box.X - 4, box.Y - 4, box.Width + 8, box.Height + 8), 7)) using (var brush = new SolidBrush(Color.FromArgb(222, 232, 241))) graphics.FillPath(brush, halo);
        using (var shape = MicrophoneMenuRenderer.Outline(box, 4)) {
            Color ink = enabled ? MarkInk : Color.FromArgb(151, 152, 153);
            using (var brush = new SolidBrush(value ? (pressed ? Color.FromArgb(35, 70, 103) : ink) : pressed ? Color.FromArgb(222, 232, 241) : Paper)) graphics.FillPath(brush, shape);
            using (var pen = new Pen(value || hover || focus ? ink : Color.FromArgb(123, 124, 124), focus ? 2f : 1.3f)) graphics.DrawPath(pen, shape);
            if (value) using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) graphics.DrawLines(pen, new[] { new Point(box.X + 4, box.Y + 9), new Point(box.X + 8, box.Y + 13), new Point(box.X + 14, box.Y + 5) });
        }
        graphics.Restore(saved);
    }
    internal static void CheckForTest(string work) {
        using (var bitmap = new Bitmap(538, 304)) using (var graphics = Graphics.FromImage(bitmap)) using (var font = new Font(TextFamily, 11f)) {
            int[] numbers = { 1, 99, 100, 1000 };
            for (int i = 0; i < numbers.Length; i++) Draw(graphics, new Rectangle(0, i * 76, 538, 76), numbers[i], font, i == 1, i == 2, i == 3, false, false);
            if (bitmap.GetPixel(200, 74).ToArgb() != Canvas.ToArgb() || bitmap.GetPixel(200, 12).ToArgb() != Paper.ToArgb()) throw new Exception("CARD-TEST: visible gap separates cards from the list surface");
            var pixels = new List<Point>(); for (int y = 4; y < 70; y++) for (int x = 380; x < 530; x++) if (bitmap.GetPixel(x, y).ToArgb() == NumberInk.ToArgb()) pixels.Add(new Point(x, y));
            if (pixels.Count < 10) throw new Exception("CARD-TEST: index is visible at the right edge");
            using (var brush = new SolidBrush(Color.FromArgb(20, 25, 31))) using (var format = new StringFormat { LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap }) graphics.DrawString(new string('W', 50), font, brush, new Rectangle(12, 20, 514, 42), format);
            bitmap.Save(Path.Combine(work, "dictionary-card-states.png"));
            if (!pixels.Exists(point => bitmap.GetPixel(point.X, point.Y).R < 100)) throw new Exception("CARD-TEST: full-width text paints over the decorative index");
        }
        Console.WriteLine("CARD-TEST PASS: card gaps / 1-4 digit watermarks / full-width text over index / separate current and checked states");
    }
}

sealed class DictionaryCheckBox : CheckBox
{
    bool hover, pressed;
    public DictionaryCheckBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
        HistoryCard.Check(e.Graphics, new Rectangle(4, (Height - 18) / 2, 18, 18), Checked, Enabled && hover, Enabled && pressed, Focused && ShowFocusCues, Enabled);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(30, 0, Math.Max(0, Width - 30), Height), Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
    internal void CheckForTest() {
        bool initial = Checked; int changes = 0; EventHandler changed = (o, e) => changes++; CheckedChanged += changed;
        try {
            OnClick(EventArgs.Empty); if (Checked == initial || changes != 1) throw new Exception("CHECK-TEST: native check state and change event");
            if (AccessibilityObject.Role != AccessibleRole.CheckButton) throw new Exception("CHECK-TEST: native checkbox accessibility role");
            OnClick(EventArgs.Empty); if (Checked != initial || changes != 2) throw new Exception("CHECK-TEST: repeat click restores state");
            OnKeyDown(new KeyEventArgs(Keys.Space)); if (!pressed) throw new Exception("CHECK-TEST: space press feedback");
            OnLostFocus(EventArgs.Empty); if (pressed) throw new Exception("CHECK-TEST: focus loss clears pressed feedback");
        } finally { CheckedChanged -= changed; }
    }
}

sealed class DictionaryListBox : ListBox
{
    [StructLayout(LayoutKind.Sequential)]
    struct PaintData { public IntPtr dc; public int erase, left, top, right, bottom, restore, update; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] reserved; }
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr window, out PaintData data);
    [DllImport("user32.dll")] static extern bool EndPaint(IntPtr window, ref PaintData data);
    internal int BufferedPaints;
    internal int BufferedItems;
    int wheelRemainder;
    int lastCount = -1, lastTop = -1, lastPage = -1;
    internal bool CardFeedback;
    internal int PressedIndex = -1;
    int hovered = -1;
    internal int HoverIndex { get { var point = PointToClient(MousePosition); return ClientRectangle.Contains(point) ? IndexFromPoint(point) : -1; } }
    internal Action ViewChanged;
    protected override void OnDrawItem(DrawItemEventArgs e) {
        if (!CardFeedback || e.Index < 0 || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) { base.OnDrawItem(e); return; }
        // Native selection can draw directly without WM_PAINT; buffer that path too.
        using (var image = new Bitmap(e.Bounds.Width, e.Bounds.Height)) using (var graphics = Graphics.FromImage(image)) {
            graphics.TranslateTransform(-e.Bounds.X, -e.Bounds.Y);
            base.OnDrawItem(new DrawItemEventArgs(graphics, e.Font, e.Bounds, e.Index, e.State, e.ForeColor, e.BackColor));
            e.Graphics.DrawImageUnscaled(image, e.Bounds.Location); BufferedItems++;
        }
    }
    void NotifyView() {
        int page = Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight));
        if (lastCount == Items.Count && lastTop == TopIndex && lastPage == page) return;
        lastCount = Items.Count; lastTop = TopIndex; lastPage = page;
        if (ViewChanged != null) ViewChanged();
    }
    void InvalidateItem(int index) { if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index)); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (CardFeedback && hovered != HoverIndex) { int previous = hovered; hovered = HoverIndex; InvalidateItem(previous); InvalidateItem(hovered); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (CardFeedback) { int previous = hovered; hovered = -1; InvalidateItem(previous); } }
    protected override void OnMouseDown(MouseEventArgs e) { if (CardFeedback && e.Button == MouseButtons.Left) { PressedIndex = IndexFromPoint(e.Location); InvalidateItem(PressedIndex); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (CardFeedback) { int previous = PressedIndex; PressedIndex = -1; InvalidateItem(previous); } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (CardFeedback && !Capture) { int previous = PressedIndex; PressedIndex = -1; InvalidateItem(previous); } }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (CardFeedback) InvalidateItem(SelectedIndex); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (CardFeedback) InvalidateItem(SelectedIndex); }
    protected override CreateParams CreateParams {
        get { var value = base.CreateParams; value.Style = (value.Style | 0x1000) & ~0x00200000; return value; }
    }
    protected override void WndProc(ref Message m) {
        if (CardFeedback && m.Msg == 0x0014) { m.Result = new IntPtr(1); return; }
        if (CardFeedback && m.Msg == 0x000F && ClientSize.Width > 0 && ClientSize.Height > 0) {
            PaintData paint; IntPtr dc = BeginPaint(Handle, out paint);
            try {
                using (var target = Graphics.FromHdc(dc)) using (var buffer = BufferedGraphicsManager.Current.Allocate(target, ClientRectangle)) {
                    buffer.Graphics.Clear(BackColor); IntPtr buffered = buffer.Graphics.GetHdc();
                    try { var print = Message.Create(Handle, 0x0318, buffered, new IntPtr(4)); base.WndProc(ref print); }
                    finally { buffer.Graphics.ReleaseHdc(buffered); }
                    buffer.Render(target); BufferedPaints++;
                }
            } finally { EndPaint(Handle, ref paint); }
            NotifyView(); m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x020A) {
            int page = Math.Max(1, ClientSize.Height / ItemHeight);
            int lines = DictionaryScrollBar.WheelLines(ref wheelRemainder, (short)((long)m.WParam >> 16), page);
            if (Items.Count > 0) TopIndex = Math.Max(0, Math.Min(Math.Max(0, Items.Count - page), TopIndex - lines));
            NotifyView();
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == 0x000F || m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0101) NotifyView();
    }
}

// Native controls retain editing and selection; only the scrollbar is custom drawn.
sealed class DictionaryScrollBar : Control
{
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    readonly Func<int> count, page, position;
    readonly Action<int> scroll;
    int drag = -1, wheelRemainder;
    internal static readonly Color IdleInk = Color.FromArgb(132, 132, 136);
    internal bool CardTrack;
    internal static int WheelLines(ref int remainder, int delta, int page) {
        remainder += delta;
        int steps = remainder / 120; remainder %= 120;
        int lines = SystemInformation.MouseWheelScrollLines;
        return steps * (lines < 0 ? page : lines);
    }
    public DictionaryScrollBar(Func<int> count, Func<int> page, Func<int> position, Action<int> scroll) {
        this.count = count; this.page = page; this.position = position; this.scroll = scroll;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        TabStop = true; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "垂直滚动";
        Cursor = Cursors.Hand;
    }
    Rectangle Thumb {
        get {
            if (count() <= page()) return Rectangle.Empty;
            int height = Math.Min(Height, Math.Max(24, Height * page() / Math.Max(1, count())));
            int top = (Height - height) * Math.Min(position(), Math.Max(0, count() - page())) / Math.Max(1, count() - page());
            int width = CardTrack ? 6 : 4;
            return new Rectangle((Width - width) / 2, top, width, height);
        }
    }
    internal bool Overflows { get { return count() > page(); } }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Rectangle thumb = Thumb;
        if (thumb.IsEmpty) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (CardTrack) using (var track = MicrophoneMenuRenderer.Outline(new Rectangle(1, 0, Width - 2, Height - 1), 5)) using (var brush = new SolidBrush(Color.FromArgb(234, 232, 227))) e.Graphics.FillPath(brush, track);
        using (var shape = MicrophoneMenuRenderer.Outline(thumb, 2))
        using (var ink = new SolidBrush(Capture || ClientRectangle.Contains(PointToClient(MousePosition)) || Focused
            ? Color.FromArgb(102, 106, 113) : IdleInk)) e.Graphics.FillPath(ink, shape);
    }
    void MoveThumb(int y) {
        var thumb = Thumb;
        if (thumb.IsEmpty) return;
        int top = Math.Max(0, Math.Min(Height - thumb.Height, y - Math.Max(0, drag)));
        scroll((int)Math.Round((double)top * Math.Max(0, count() - page()) / Math.Max(1, Height - thumb.Height)));
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Thumb.IsEmpty) return;
        Focus(); drag = Thumb.Contains(e.Location) ? e.Y - Thumb.Top : Thumb.Height / 2;
        Capture = true; MoveThumb(e.Y);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) MoveThumb(e.Y); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; drag = -1; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scroll(Math.Max(0, Math.Min(Math.Max(0, count() - page()), position() - WheelLines(ref wheelRemainder, e.Delta, page())))); Invalidate(); }
    protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(key); }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        int target = position();
        if (e.KeyCode == Keys.Up) target--; else if (e.KeyCode == Keys.Down) target++;
        else if (e.KeyCode == Keys.PageUp) target -= page(); else if (e.KeyCode == Keys.PageDown) target += page();
        else if (e.KeyCode == Keys.Home) target = 0; else if (e.KeyCode == Keys.End) target = count(); else return;
        scroll(Math.Max(0, Math.Min(Math.Max(0, count() - page()), target))); Invalidate(); e.Handled = true;
    }
    internal void CheckForTest() {
        if (Thumb.IsEmpty) throw new Exception("Expected overflowing content");
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Thumb.Top + 2, 0));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, Width / 2, Height - 1, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height - 1, 0));
        if (position() <= 0) throw new Exception("Scrollbar drag did not scroll");
        OnKeyDown(new KeyEventArgs(Keys.Home));
        if (position() != 0) throw new Exception("Scrollbar Home did not reset");
        OnKeyDown(new KeyEventArgs(Keys.End)); int end = position();
        OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
        if (position() >= end) throw new Exception("Scrollbar wheel did not scroll");
    }
}

sealed class DictionaryButton : Button
{
    internal bool Primary, Outlined, Selected;
    internal Color? DisabledInk;
    internal static readonly Color OutlineInk = Color.FromArgb(138, 137, 133);
    internal string Glyph;
    public DictionaryButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Parent is DictionaryField ? Color.FromArgb(246, 245, 243) : Parent == null ? BackColor : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        using (var outline = MicrophoneMenuRenderer.Outline(new Rectangle(0, 0, Width - 1, Height - 1), 8)) {
            if (Primary) {
                using (var brush = new LinearGradientBrush(ClientRectangle,
                    Enabled ? Color.FromArgb(48, 49, 52) : Color.FromArgb(194, 193, 190),
                    Enabled ? Color.FromArgb(39, 40, 43) : Color.FromArgb(194, 193, 190), 90f)) e.Graphics.FillPath(brush, outline);
            } else using (var brush = new SolidBrush(hover ? Color.FromArgb(237, 236, 233) : BackColor)) e.Graphics.FillPath(brush, outline);
            if (Outlined && !Primary) using (var pen = new Pen(Enabled ? OutlineInk : Color.FromArgb(214, 212, 207))) e.Graphics.DrawPath(pen, outline);
            if (Focused) using (var pen = new Pen(Color.FromArgb(133, 132, 127))) e.Graphics.DrawPath(pen, outline);
        }
        if (Selected) using (var brush = new SolidBrush(Color.FromArgb(96, 100, 107))) e.Graphics.FillRectangle(brush, 1, 8, 3, Height - 16);
        Rectangle label = ClientRectangle; if (TextAlign == ContentAlignment.MiddleLeft) { label.X += 14; label.Width -= 28; }
        if (!string.IsNullOrEmpty(Glyph)) {
            int textWidth = (int)Math.Ceiling(e.Graphics.MeasureString(Text, Font).Width);
            int iconX = Text.Length == 0 ? (Width - 20) / 2 : Math.Max(8, (Width - textWidth - 30) / 2);
            DictionaryGlyph.Draw(e.Graphics, Glyph, new Rectangle(iconX, (Height - 20) / 2, 20, 20), Primary ? Color.White : Enabled ? ForeColor : (DisabledInk ?? SystemColors.GrayText));
            if (Text.Length > 0) label = new Rectangle(iconX + 28, 0, textWidth, Height);
        } else if (Text == "删除" && !Primary) {
            using (var pen = new Pen(Enabled ? ForeColor : SystemColors.GrayText, 1.2f)) {
                int x = 6, y = (Height - 20) / 2;
                e.Graphics.DrawLine(pen, x, y + 4, x + 16, y + 4);
                e.Graphics.DrawRectangle(pen, x + 3, y + 4, 10, 15);
                e.Graphics.DrawLine(pen, x + 5, y, x + 11, y);
                e.Graphics.DrawLine(pen, x + 6, y + 8, x + 6, y + 15);
                e.Graphics.DrawLine(pen, x + 10, y + 8, x + 10, y + 15);
            }
            label.X += 25; label.Width -= 25;
        }
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var format = new StringFormat { Alignment = TextAlign == ContentAlignment.MiddleLeft ? StringAlignment.Near : StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
        using (var brush = new SolidBrush(Primary ? Color.White : Enabled ? ForeColor : (DisabledInk ?? SystemColors.GrayText)))
            e.Graphics.DrawString(Text, Font, brush, label, format);
    }
}

static class DictionaryGlyph
{
    internal static void Draw(Graphics graphics, string kind, Rectangle bounds, Color ink) {
        var state = graphics.Save(); graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(ink, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) {
            if (kind == "search") { graphics.DrawEllipse(pen, 3, 3, 12, 12); graphics.DrawLine(pen, 13, 13, 21, 21); }
            else if (kind == "close") { graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 6, 18, 18, 6); }
            else if (kind == "plus") { graphics.DrawLine(pen, 5, 12, 19, 12); graphics.DrawLine(pen, 12, 5, 12, 19); }
            else if (kind == "trash") { graphics.DrawLine(pen, 4, 6, 20, 6); graphics.DrawLine(pen, 9, 3, 15, 3); graphics.DrawLines(pen, new[] { new Point(6, 6), new Point(7, 21), new Point(17, 21), new Point(18, 6) }); graphics.DrawLine(pen, 10, 10, 10, 17); graphics.DrawLine(pen, 14, 10, 14, 17); }
            else if (kind == "mic") { graphics.DrawArc(pen, 8, 2, 8, 8, 180, 180); graphics.DrawArc(pen, 8, 7, 8, 8, 0, 180); graphics.DrawLine(pen, 8, 6, 8, 11); graphics.DrawLine(pen, 16, 6, 16, 11); graphics.DrawArc(pen, 5, 6, 14, 13, 0, 180); graphics.DrawLine(pen, 12, 19, 12, 22); graphics.DrawLine(pen, 8, 22, 16, 22); }
            else if (kind == "stop") { using (var brush = new SolidBrush(ink)) graphics.FillRectangle(brush, 6, 6, 12, 12); }
            else if (kind == "play") { graphics.DrawPolygon(pen, new[] { new Point(8, 4), new Point(20, 12), new Point(8, 20) }); }
            else if (kind == "check") { graphics.DrawLines(pen, new[] { new Point(5, 12), new Point(10, 17), new Point(20, 6) }); }
            else if (kind == "arrow") { graphics.DrawLine(pen, 4, 12, 20, 12); graphics.DrawLines(pen, new[] { new Point(14, 6), new Point(20, 12), new Point(14, 18) }); }
            else if (kind == "back" || kind == "forward") { int a = kind == "back" ? 18 : 6, b = 24 - a; graphics.DrawLine(pen, a, 12, b, 12); graphics.DrawLines(pen, new[] { new Point(b + (kind == "back" ? 5 : -5), 7), new Point(b, 12), new Point(b + (kind == "back" ? 5 : -5), 17) }); }
            else if (kind == "edit") { graphics.DrawLines(pen, new[] { new Point(4, 16), new Point(15, 5), new Point(20, 10), new Point(9, 21), new Point(3, 22), new Point(4, 16) }); graphics.DrawLine(pen, 13, 7, 18, 12); }
        }
        graphics.Restore(state);
    }
}

sealed class DictionaryField : Panel
{
    internal string Glyph;
    internal static readonly Color BorderInk = Color.FromArgb(138, 137, 133);
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
    public DictionaryField() { DoubleBuffered = true; BackColor = Color.FromArgb(251, 250, 248); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = MicrophoneMenuRenderer.Outline(new Rectangle(0, 0, Width - 1, Height - 1), 9)) {
            using (var brush = new SolidBrush(Color.FromArgb(246, 245, 243))) e.Graphics.FillPath(brush, shape);
            using (var pen = new Pen(ContainsFocus ? Color.FromArgb(102, 106, 113) : BorderInk)) e.Graphics.DrawPath(pen, shape);
        }
        if (Glyph != null) DictionaryGlyph.Draw(e.Graphics, Glyph, new Rectangle(13, (Height - 18) / 2, 18, 18), Color.FromArgb(102, 106, 113));
    }
}

sealed class VoiceSample
{
    public string id, word, correctText, promptText, wav, createdUtc, state;
    public int sampleRate, channels, bitsPerSample;
    public double durationSeconds;
    public override string ToString() { return durationSeconds.ToString("0.0") + " 秒  ·  " + correctText; }
}

// One committed JSON per clip; deleting a clip hides it but retains recoverable audio.
sealed class VoiceSampleStore
{
    readonly string folder;
    internal VoiceSampleStore(string root) { folder = Path.Combine(root, "user-data", "voice-samples"); }
    internal List<VoiceSample> Load(string word) {
        var samples = new List<VoiceSample>();
        if (!Directory.Exists(folder)) return samples;
        foreach (string file in Directory.GetFiles(folder, "*.json")) {
            var sample = new JavaScriptSerializer().Deserialize<VoiceSample>(File.ReadAllText(file));
            Guid id;
            if (sample == null || !Guid.TryParseExact(sample.id, "N", out id) || Path.GetFileNameWithoutExtension(file) != sample.id || sample.wav != sample.id + ".wav" || sample.sampleRate != 16000 || sample.channels != 1 || sample.bitsPerSample != 16 || string.IsNullOrWhiteSpace(sample.word) || string.IsNullOrWhiteSpace(sample.correctText) || sample.correctText.Length > 2000 || sample.durationSeconds < .4 || sample.durationSeconds > 60 || sample.state != "collected")
                throw new IOException("录音索引损坏，已保留文件，请先检查本地样本。");
            if (string.Equals(sample.word, word, StringComparison.OrdinalIgnoreCase)) {
                if (!File.Exists(Path.Combine(folder, sample.wav))) throw new IOException("样本音频缺失，已保留记录。");
                samples.Add(sample);
            }
        }
        samples.Sort((a, b) => string.CompareOrdinal(b.createdUtc, a.createdUtc)); return samples;
    }
    internal static byte[] Wave(byte[] pcm) {
        if (pcm == null || pcm.Length == 0 || pcm.Length % 2 != 0 || pcm.Length > 60 * 32000) throw new IOException("录音数据无效或超过 60 秒。");
        using (var stream = new MemoryStream()) {
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm); writer.Flush(); return stream.ToArray();
            }
        }
    }
    internal static void Validate(byte[] pcm, string word, string text) {
        if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new IOException("请填写这段录音对应的完整文字。");
        if (pcm == null || pcm.Length < 12800 || pcm.Length > 60 * 32000 || pcm.Length % 2 != 0) throw new IOException("请录制 0.4 到 60 秒的声音。");
        int peak = 0; for (int i = 0; i < pcm.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, i)));
        if (peak < 64) throw new IOException("没有录到有效声音，请检查麦克风后重录。");
    }
    internal VoiceSample Save(string word, string text, byte[] pcm) {
        Validate(pcm, word, text); Directory.CreateDirectory(folder);
        var sample = new VoiceSample { id = Guid.NewGuid().ToString("N"), word = word.Trim(), correctText = text.Trim(), promptText = text.Trim(), createdUtc = DateTime.UtcNow.ToString("o"), state = "collected", sampleRate = 16000, channels = 1, bitsPerSample = 16, durationSeconds = pcm.Length / 32000.0 };
        sample.wav = sample.id + ".wav";
        string audio = Path.Combine(folder, sample.wav), metadata = Path.Combine(folder, sample.id + ".json");
        try {
            File.WriteAllBytes(audio + ".tmp", Wave(pcm)); File.Move(audio + ".tmp", audio);
            File.WriteAllText(metadata + ".tmp", new JavaScriptSerializer().Serialize(sample), new UTF8Encoding(false)); File.Move(metadata + ".tmp", metadata);
            return sample;
        } catch {
            foreach (string partial in new[] { audio + ".tmp", metadata + ".tmp", audio }) try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }
    internal string AudioPath(VoiceSample sample) {
        Guid id; if (sample == null || !Guid.TryParseExact(sample.id, "N", out id) || sample.wav != sample.id + ".wav") throw new IOException("样本路径无效。");
        return Path.Combine(folder, sample.wav);
    }
    internal void Remove(VoiceSample sample) {
        AudioPath(sample);
        string file = Path.Combine(folder, sample.id + ".json"); File.Move(file, file + ".deleted");
    }
}

sealed class SampleCaptureDialog : DictionaryWindow
{
    readonly TextBox spokenText = new TextBox();
    readonly Label sampleNotice = new DictionaryLabel();
    readonly Label captureTime = new DictionaryLabel();
    readonly Panel meter = new Panel();
    readonly VoiceSampleStore sampleStore;
    readonly System.Windows.Forms.Timer sampleTimer = new System.Windows.Forms.Timer { Interval = 80 };
    readonly Stopwatch sampleClock = new Stopwatch();
    readonly List<float> sampleLevels = new List<float>();
    readonly System.Media.SoundPlayer samplePlayer = new System.Media.SoundPlayer();
    Button recordButton, sampleSaveButton, samplePlayButton, sampleDeleteButton;
    Recorder sampleRecorder;
    MemoryStream sampleAudio;
    byte[] pendingAudio;
    bool sampleStarting;
    int sampleStartRevision;
    double playbackUntil;
    internal Func<Task> PrepareRecording;
    internal VoiceSample SavedSample;
    internal string SavedAudioPath { get { return SavedSample == null ? "" : sampleStore.AudioPath(SavedSample); } }
    internal bool RecordingBusy { get { return sampleStarting || sampleRecorder != null; } }
    static readonly Color Face = Color.FromArgb(251, 250, 248), Ink = Color.FromArgb(20, 25, 31), Dim = Color.FromArgb(102, 106, 113), Field = Color.FromArgb(246, 245, 243);
    readonly Panel captureContent = new Panel();
    internal SampleCaptureDialog(string root) {
        sampleStore = new VoiceSampleStore(root); ClientSize = new Size(620, 330); Font = new Font("Segoe UI Variable Text", 14f);
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; TopMost = true; ShowInTaskbar = false;
        BackColor = Face; ForeColor = Ink; Text = "补录样本"; AccessibleName = Text;
        var title = DictionaryDialog.Caption(Text, 30, 20, Ink); title.Font = new Font(Font.FontFamily, 20f, FontStyle.Bold); Controls.Add(title);
        Controls.Add(IconButton("close", "关闭补录", new Rectangle(550, 20, 40, 40), Close));
        captureContent.Bounds = new Rectangle(32, 82, 556, 220); captureContent.BackColor = Face; Controls.Add(captureContent); BuildCaptureControls();
        sampleNotice.Text = "录音仅保存样本 · 不占救援名额，不自动训练"; UpdateSampleControls();
        KeyPreview = true; KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) { if (RecordingBusy) StopSampleRecording(); else Close(); e.SuppressKeyPress = true; } };
    }
    void BuildCaptureControls() {
        var label = DictionaryDialog.Caption("朗读内容", 0, 0, Dim); label.Font = new Font(Font.FontFamily, 12f); captureContent.Controls.Add(label);
        var note = DictionaryDialog.Caption("单词或整句 · 与录音内容一致", 294, 3, Dim); note.Font = new Font(Font.FontFamily, 10f); captureContent.Controls.Add(note);
        var field = new DictionaryField { Bounds = new Rectangle(0, 32, 556, 64) };
        spokenText.Bounds = new Rectangle(14, 10, 526, 44); spokenText.Multiline = true; spokenText.MaxLength = 2000;
        spokenText.Font = new Font(Font.FontFamily, 12f); spokenText.BorderStyle = BorderStyle.None; spokenText.BackColor = Field; spokenText.ForeColor = Ink;
        spokenText.AccessibleName = "这段录音对应的完整文字"; field.Controls.Add(spokenText); captureContent.Controls.Add(field);
        recordButton = DictionaryDialog.MakeButton("录音", new Rectangle(0, 110, 124, 44), async () => await ToggleSampleRecording());
        ((DictionaryButton)recordButton).Glyph = "mic"; ((DictionaryButton)recordButton).Primary = true; captureContent.Controls.Add(recordButton);
        meter.Bounds = new Rectangle(140, 118, 150, 28); meter.BackColor = Face; meter.AccessibleName = "麦克风声音强度";
        meter.Paint += (o, e) => {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(sampleRecorder != null ? Color.FromArgb(179, 66, 55) : Dim, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 25; i++) { float value = i < sampleLevels.Count ? sampleLevels[i] : 0; float half = 1 + Math.Min(11, value * 11); e.Graphics.DrawLine(pen, i * 6 + 2, 14 - half, i * 6 + 2, 14 + half); }
        }; captureContent.Controls.Add(meter);
        captureTime.Bounds = new Rectangle(304, 119, 90, 28); captureTime.Font = new Font(Font.FontFamily, 11f); captureTime.ForeColor = Dim; captureTime.Text = "0:00"; captureContent.Controls.Add(captureTime);
        sampleSaveButton = DictionaryDialog.MakeButton("保存样本", new Rectangle(410, 110, 146, 44), () => { if (SaveSample()) { DialogResult = DialogResult.OK; Close(); } });
        ((DictionaryButton)sampleSaveButton).Glyph = "check"; captureContent.Controls.Add(sampleSaveButton);
        sampleNotice.Bounds = new Rectangle(0, 170, 454, 28); sampleNotice.Font = new Font(Font.FontFamily, 10.5f); sampleNotice.ForeColor = Dim;
        sampleNotice.AutoEllipsis = true; sampleNotice.AccessibleName = "录音样本状态"; captureContent.Controls.Add(sampleNotice);
        samplePlayButton = IconButton("play", "试听录音", new Rectangle(466, 164, 40, 36), PlaySample);
        sampleDeleteButton = IconButton("trash", "删除录音", new Rectangle(516, 164, 40, 36), RemoveSample);
        captureContent.Controls.Add(samplePlayButton); captureContent.Controls.Add(sampleDeleteButton);
        sampleTimer.Tick += (o, e) => {
            if (sampleRecorder != null) {
                try { AppendSampleAudio(sampleRecorder.Read(true)); if (sampleClock.ElapsedMilliseconds >= 60000) { StopSampleRecording(); sampleNotice.Text = "已到 60 秒，请试听后保存"; } }
                catch (Exception error) { StopSampleRecording(); sampleNotice.Text = "录音中断：" + error.Message; }
            } else if (playbackUntil > 0 && sampleClock.Elapsed.TotalSeconds >= playbackUntil) StopSamplePlayback();
        };
    }

    Button IconButton(string glyph, string name, Rectangle bounds, Action click) {
        var button = (DictionaryButton)DictionaryDialog.MakeButton("", bounds, click); button.Glyph = glyph;
        button.AccessibleName = name; button.AccessibleDescription = name; return button;
    }
    void UpdateSampleControls() {
        bool busy = RecordingBusy;
        recordButton.Text = busy ? "停止" : "录音"; ((DictionaryButton)recordButton).Glyph = busy ? "stop" : "mic";
        ((DictionaryButton)recordButton).Primary = !busy; recordButton.AccessibleName = busy ? "停止录音" : "开始补录";
        spokenText.ReadOnly = busy; sampleSaveButton.Enabled = samplePlayButton.Enabled = sampleDeleteButton.Enabled = !busy && pendingAudio != null;
        recordButton.Invalidate();
    }
    async Task ToggleSampleRecording() {
        if (RecordingBusy) { StopSampleRecording(); return; }
        if (!ResolvePendingAudio()) return;
        if (string.IsNullOrWhiteSpace(spokenText.Text)) { sampleNotice.Text = "先填写这段录音的完整文字"; spokenText.Focus(); return; }
        StopSamplePlayback(); int revision = ++sampleStartRevision; sampleStarting = true; UpdateSampleControls();
        try {
            if (PrepareRecording != null) await PrepareRecording();
            if (IsDisposed || Disposing || revision != sampleStartRevision) return;
            sampleAudio = new MemoryStream(); sampleLevels.Clear();
            sampleRecorder = new Recorder(); sampleClock.Restart(); captureTime.Text = "0:00"; sampleTimer.Start(); sampleNotice.Text = "录音中 · 按上方文字自然朗读";
        } catch (Exception error) {
            if (sampleRecorder != null) { sampleRecorder.Dispose(); sampleRecorder = null; }
            if (sampleAudio != null) { sampleAudio.Dispose(); sampleAudio = null; }
            sampleNotice.Text = error.Message;
        } finally { if (revision == sampleStartRevision) sampleStarting = false; if (!IsDisposed && !Disposing) UpdateSampleControls(); }
    }
    void AppendSampleAudio(byte[] bytes) {
        int length = Math.Min(bytes.Length, 60 * 32000 - (int)sampleAudio.Length);
        if (length > 0) sampleAudio.Write(bytes, 0, length);
        double squares = 0; for (int i = 0; i + 1 < length; i += 2) { double value = BitConverter.ToInt16(bytes, i) / 32768.0; squares += value * value; }
        float level = length == 0 ? 0 : (float)Math.Min(1, Math.Sqrt(squares / (length / 2)) * 15);
        sampleLevels.Add(level); while (sampleLevels.Count > 25) sampleLevels.RemoveAt(0);
        TimeSpan elapsed = TimeSpan.FromSeconds(sampleAudio.Length / 32000.0); captureTime.Text = elapsed.Minutes + ":" + elapsed.Seconds.ToString("00"); meter.Invalidate();
    }
    internal void StopSampleRecording() {
        ++sampleStartRevision; sampleStarting = false; sampleTimer.Stop();
        if (sampleRecorder != null) {
            string error = null;
            try { AppendSampleAudio(sampleRecorder.Stop()); } catch (Exception failure) { error = failure.Message; }
            finally { sampleRecorder.Dispose(); sampleRecorder = null; }
            pendingAudio = sampleAudio.ToArray(); sampleAudio.Dispose(); sampleAudio = null;
            sampleNotice.Text = error == null ? "待保存 · 可试听并核对完整文字" : "录音中断，请试听或重录：" + error;
        }
        if (!IsDisposed && !Disposing) { UpdateSampleControls(); meter.Invalidate(); }
    }
    void StopSamplePlayback() {
        samplePlayer.Stop(); if (samplePlayer.Stream != null) { samplePlayer.Stream.Dispose(); samplePlayer.Stream = null; }
        playbackUntil = 0; if (sampleRecorder == null) sampleTimer.Stop();
        if (samplePlayButton != null) { ((DictionaryButton)samplePlayButton).Glyph = "play"; samplePlayButton.AccessibleName = "试听录音"; samplePlayButton.Invalidate(); }
    }
    void PlaySample() {
        if (playbackUntil > 0) { StopSamplePlayback(); return; }
        try {
            if (pendingAudio == null) return;
            byte[] audio = VoiceSampleStore.Wave(pendingAudio);
            StopSamplePlayback(); samplePlayer.Stream = new MemoryStream(audio); samplePlayer.Load(); samplePlayer.Play();
            sampleClock.Restart(); playbackUntil = Math.Max(.1, (audio.Length - 44) / 32000.0); sampleTimer.Start();
            ((DictionaryButton)samplePlayButton).Glyph = "stop"; samplePlayButton.AccessibleName = "停止试听"; samplePlayButton.Invalidate();
        } catch (Exception error) { StopSamplePlayback(); sampleNotice.Text = "试听失败：" + error.Message; }
    }
    bool SaveSample() {
        if (pendingAudio == null || RecordingBusy) return false;
        try {
            StopSamplePlayback(); SavedSample = sampleStore.Save("个人补录", spokenText.Text, pendingAudio);
            pendingAudio = null; UpdateSampleControls(); sampleNotice.Text = "样本已保存 · 请在听写记录中审核和选择"; return true;
        } catch (Exception error) { sampleNotice.Text = "保存失败：" + error.Message; return false; }
    }
    bool ResolvePendingAudio() {
        if (RecordingBusy) return false;
        if (pendingAudio == null) return true;
        var answer = MessageBox.Show(this, "这段录音尚未保存，保存为样本吗？", "录音样本", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) return SaveSample();
        StopSamplePlayback(); pendingAudio = null; UpdateSampleControls(); return true;
    }
    void RemoveSample() {
        if (RecordingBusy || pendingAudio == null) return;
        using (var confirmation = new DictionaryDeleteDialog("", true)) if (confirmation.ShowDialog(this) != DialogResult.OK) return;
        StopSamplePlayback(); pendingAudio = null; UpdateSampleControls(); sampleNotice.Text = "已放弃本次未保存录音";
    }
    internal static void CheckSamplesForTest(string work) {
        Action<bool, string> check = (ok, reason) => { if (!ok) throw new Exception("VOICE-SAMPLE-TEST: " + reason); };
        Action<Action, string> fails = (action, reason) => { bool failed = false; try { action(); } catch (IOException) { failed = true; } check(failed, reason); };
        string fixture = Path.Combine(work, "voice-sample-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "dictionary.json"), "{\"hotwords\":[\"Example\",\"Other\"],\"rules\":[]}");
        var pcm = new byte[32000];
        for (int i = 0; i < pcm.Length / 2; i++) { short value = (short)(Math.Sin(i * .12) * 2000); pcm[2 * i] = (byte)(value & 255); pcm[2 * i + 1] = (byte)(value >> 8); }
        var store = new VoiceSampleStore(fixture);
        var first = store.Save("Example", "Example", pcm); var second = store.Save("Example", "打开 Example", pcm); store.Save("Other", "Other", pcm);
        var saved = store.Load("EXAMPLE"); check(saved.Count == 2 && first.id != second.id && store.Load("Other").Count == 1, "multiple independent clips per word, case insensitive lookup, no word mixing");
        byte[] wav = File.ReadAllBytes(store.AudioPath(first));
        check(wav.Length == pcm.Length + 44 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt16(wav, 22) == 1 && BitConverter.ToInt16(wav, 34) == 16 && BitConverter.ToInt32(wav, 40) == pcm.Length, "valid mono PCM16 16k WAV including exact tail");
        using (var audio = new MemoryStream(wav)) using (var player = new System.Media.SoundPlayer(audio)) { player.Load(); check(player.IsLoadCompleted, "recorded WAV is accepted by the actual playback backend without emitting test audio"); }
        check(saved.Exists(item => item.correctText == "打开 Example") && saved.TrueForAll(item => item.state == "collected"), "full bilingual labels persist and never claim training");
        fails(() => store.Save("Example", "", pcm), "missing label blocked");
        fails(() => store.Save("Example", "Example", new byte[32000]), "silent clip blocked");
        fails(() => store.Save("Example", "Example", new byte[3]), "invalid PCM blocked");
        fails(() => store.AudioPath(new VoiceSample { id = "../escape", wav = "../escape.wav" }), "metadata cannot escape sample folder");
        check(store.Load("Example").Count == 2, "invalid saves never replace previous samples");
        using (var dialog = new SampleCaptureDialog(fixture)) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show(); Application.DoEvents();
            dialog.pendingAudio = pcm; dialog.spokenText.Text = "我正在用 Example 写代码"; dialog.UpdateSampleControls();
            check(dialog.sampleSaveButton.Enabled && dialog.samplePlayButton.Enabled, "supplement recording offers save and playback");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-supplement.png")); }
            dialog.spokenText.Clear(); check(!dialog.SaveSample() && dialog.pendingAudio == pcm, "failed save keeps recording available");
            dialog.spokenText.Text = "我正在用 Example 写代码";
            check(dialog.SaveSample() && dialog.pendingAudio == null && store.Load("个人补录").Count == 1, "supplement saves without dictionary entry");
            string errors; long bytes; var records = new HistoryStore(fixture).Load(out errors, out bytes);
            check(errors.Length == 0 && records.Exists(item => item.Text("word") == "个人补录" && !item.Flag("selectedForTraining")), "supplements reach shared review without automatic selection");
            check(!dialog.HasNativeFrame && dialog.ClientSize == new Size(620, 330), "supplement keeps borderless style"); dialog.Close();
        }
        store.Remove(first); check(store.Load("Example").Count == 1 && File.Exists(store.AudioPath(first)) && File.Exists(Path.Combine(fixture, "user-data", "voice-samples", first.id + ".json.deleted")), "sample deletion retains recoverable original audio and labels");
        File.WriteAllText(Path.Combine(fixture, "user-data", "voice-samples", "broken.json"), "{}");
        fails(() => store.Load("Example"), "invalid metadata is reported instead of silently hidden");
        Console.WriteLine("VOICE-SAMPLE-TEST PASS: multi-clip / bilingual transcripts / per-word isolation / PCM WAV / silent and invalid rejection / failed-save retention / recoverable deletion / unified supplement / no automatic training");
    }

    internal static void CheckCaptureForTest(string work) {
        string fixture = Path.Combine(work, "voice-capture-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "dictionary.json"), "{\"hotwords\":[\"Capture test\"],\"rules\":[]}");
        var counts = new List<int>();
        using (var dialog = new SampleCaptureDialog(fixture)) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show(); Application.DoEvents(); dialog.spokenText.Text = "Capture test";
            int prepared = 0; dialog.PrepareRecording = () => { prepared++; return Task.FromResult(0); };
            for (int cycle = 0; cycle < 3; cycle++) {
                dialog.ToggleSampleRecording().GetAwaiter().GetResult();
                if (!dialog.RecordingBusy || !dialog.spokenText.ReadOnly || dialog.sampleSaveButton.Enabled) throw new Exception("Recording controls did not lock the word and transcript");
                var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < 700) { Application.DoEvents(); Thread.Sleep(10); }
                dialog.StopSampleRecording();
                if (dialog.RecordingBusy || dialog.sampleTimer.Enabled || dialog.pendingAudio == null || dialog.pendingAudio.Length < 16000 || dialog.spokenText.ReadOnly || !dialog.sampleSaveButton.Enabled) throw new Exception("Sample capture did not drain its tail or release the microphone");
                counts.Add(dialog.pendingAudio.Length); dialog.pendingAudio = null; dialog.UpdateSampleControls();
            }
            if (prepared != 3 || Directory.Exists(Path.Combine(fixture, "user-data"))) throw new Exception("Capture must prepare each start and never save audio without an explicit save");
            dialog.PrepareRecording = () => { throw new IOException("injected unavailable microphone"); };
            dialog.ToggleSampleRecording().GetAwaiter().GetResult();
            if (dialog.RecordingBusy || !dialog.sampleNotice.Text.Contains("injected")) throw new Exception("Start failure must release recording state and show the error");
            dialog.Close();
        }
        File.WriteAllText(Path.Combine(work, "dictionary-capture-test.txt"), "PASS: real default microphone / sample recording controller / three reopen cycles / tail drain / word and label lock / release and retry / injected start failure / no automatic save or training; PCM byte counts=" + string.Join(",", counts));
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        StopSampleRecording(); if (!ResolvePendingAudio()) { e.Cancel = true; return; } base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) {
        if (disposing) {
            ++sampleStartRevision; sampleTimer.Stop();
            if (sampleRecorder != null) { sampleRecorder.Dispose(); sampleRecorder = null; }
            if (sampleAudio != null) { sampleAudio.Dispose(); sampleAudio = null; }
            StopSamplePlayback(); samplePlayer.Dispose(); sampleTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

sealed class HistoryStatusTag : Control
{
    internal static void Colors(string state, out Color ink, out Color fill) {
        if (state == "待验证" || state == "待复测") { ink = Color.FromArgb(101, 53, 153); fill = Color.FromArgb(243, 233, 255); }
        else if (state == "已打回") { ink = Color.FromArgb(156, 49, 38); fill = Color.FromArgb(255, 233, 232); }
        else if (state == "待核对" || state == "待识别") { ink = Color.FromArgb(133, 82, 0); fill = Color.FromArgb(255, 244, 214); }
        else if (state == "已保存" || state == "已对照" || state == "验证通过" || state == "验收通过" || state == "已启用") { ink = Color.FromArgb(32, 108, 69); fill = Color.FromArgb(227, 245, 234); }
        else if (state == "已选训练") { ink = Color.FromArgb(39, 83, 160); fill = Color.FromArgb(230, 239, 255); }
        else { ink = Color.FromArgb(91, 97, 107); fill = Color.FromArgb(235, 236, 239); }
    }
    internal static Size Measure(Graphics graphics, string text, Font font) { return new Size((int)Math.Ceiling(graphics.MeasureString(text, font).Width) + 14, 22); }
    internal static int Draw(Graphics graphics, string text, Font font, Point location) {
        Color ink, fill; Colors(text, out ink, out fill); var bounds = new Rectangle(location, Measure(graphics, text, font)); graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var shape = MicrophoneMenuRenderer.Outline(bounds, 4)) using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, shape);
        using (var brush = new SolidBrush(ink)) using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap }) graphics.DrawString(text, font, brush, bounds, format);
        return bounds.Width;
    }
    internal HistoryStatusTag() { Font = new Font(HistoryCard.TextFamily, 9.5f); TabStop = false; AccessibleRole = AccessibleRole.StaticText; DoubleBuffered = true; Size = new Size(88, 22); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor); Draw(e.Graphics, Text, Font, Point.Empty); }
}

sealed class HistoryEntry
{
    internal string FileName, Original, Kind, TrainingStatus = "";
    internal Dictionary<string, object> Data;
    internal string Text(string key) { object value; return Data.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
    internal bool Flag(string key) { object value; return Data.TryGetValue(key, out value) && value is bool && (bool)value; }
    internal double Seconds { get { return Convert.ToDouble(Data["durationSeconds"]); } }
    internal string Audio { get { return Path.Combine(Path.GetDirectoryName(FileName), Text("wav")); } }
    public override string ToString() { return State + "，" + Text("correctText"); }
    internal string State { get { return Flag("ignored") ? "已忽略" : Flag("selectedForTraining") ? "已选训练" : Flag("reviewed") ? "已保存" : "待核对"; } }
}

sealed class HistoryStore
{
    internal readonly string Root;
    internal HistoryStore(string root) { Root = Path.Combine(root, "user-data"); }
    internal string Settings { get { return Path.Combine(Root, "history-settings.json"); } }
    internal static Dictionary<string, object> Parse(string text) {
        var data = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        if (data == null) throw new IOException("记录必须是 JSON 对象"); return data;
    }
    internal static void Atomic(string path, string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal bool Enabled() { if (!File.Exists(Settings)) return false; object value; return Parse(File.ReadAllText(Settings)).TryGetValue("enabled", out value) && value is bool && (bool)value; }
    internal void SetEnabled(bool value) { Atomic(Settings, new JavaScriptSerializer().Serialize(new { enabled = value })); }
    internal List<HistoryEntry> Load(out string errors, out long bytes) {
        var result = new List<HistoryEntry>(); var failures = new List<string>(); bytes = 0;
        foreach (string kind in new[] { "dictation-history", "voice-samples" }) {
            string folder = Path.Combine(Root, kind); if (!Directory.Exists(folder)) continue;
            foreach (string item in Directory.GetFiles(folder)) { try { bytes += new FileInfo(item).Length; } catch (IOException) { } }
            foreach (string file in Directory.GetFiles(folder, "*.json")) try {
                if (new FileInfo(file).Length > 131072) throw new IOException("索引过大");
                string original = File.ReadAllText(file); var entry = new HistoryEntry { FileName = file, Original = original, Kind = kind, Data = Parse(original) };
                Guid id; DateTime created;
                if (!Guid.TryParseExact(entry.Text("id"), "N", out id) || Path.GetFileNameWithoutExtension(file) != entry.Text("id") || entry.Text("wav") != entry.Text("id") + ".wav" ||
                    entry.Text("state") != "collected" || entry.Text("sampleRate") != "16000" || entry.Text("channels") != "1" || entry.Text("bitsPerSample") != "16" ||
                    !DateTime.TryParse(entry.Text("createdUtc"), out created) || entry.Text("correctText").Length > (kind == "voice-samples" ? 2000 : 10000) ||
                    double.IsNaN(entry.Seconds) || entry.Seconds <= 0 || entry.Seconds > 180) throw new IOException("索引或音频路径无效");
                if (!File.Exists(entry.Audio)) throw new IOException("原音缺失");
                if (entry.Flag("selectedForTraining") && (!entry.Flag("reviewed") || entry.Flag("ignored") || string.IsNullOrWhiteSpace(entry.Text("correctText")))) throw new IOException("训练选择缺少已核对的完整文字");
                result.Add(entry);
            } catch (Exception error) { failures.Add(Path.GetFileName(file) + "：" + error.Message); }
        }
        result.Sort((a, b) => string.CompareOrdinal(b.Text("createdUtc"), a.Text("createdUtc")));
        errors = string.Join("\n", failures.ToArray()); return result;
    }
    internal void Save(HistoryEntry entry, string correct, bool selected, bool ignored) {
        if (string.IsNullOrWhiteSpace(correct) || correct.Length > (entry.Kind == "voice-samples" ? 2000 : 10000)) throw new IOException("请填写这段原音实际说出的完整文字；补录最多 2000 字，听写记录最多 10000 字");
        if (File.ReadAllText(entry.FileName) != entry.Original) throw new IOException("记录已被其他窗口修改，请刷新后重试");
        var data = Parse(entry.Original); data["correctText"] = correct.Trim(); data["reviewed"] = true;
        data["selectedForTraining"] = selected && !ignored; data["ignored"] = ignored; data["updatedUtc"] = DateTime.UtcNow.ToString("o");
        Write(entry, data);
    }
    internal void Write(HistoryEntry entry, Dictionary<string, object> data) {
        if (File.ReadAllText(entry.FileName) != entry.Original) throw new IOException("记录已被其他窗口修改，请刷新后重试");
        string text = new JavaScriptSerializer().Serialize(data); Atomic(entry.FileName, text); entry.Data = data; entry.Original = text;
    }
    internal void ChangeState(HistoryEntry entry, string action) {
        var data = Parse(entry.Original);
        if (action == "select" && (!entry.Flag("reviewed") || entry.Flag("ignored") || string.IsNullOrWhiteSpace(entry.Text("correctText"))))
            throw new IOException("请先试听并保存正确文字，再留作训练");
        if (action == "pending") { data["reviewed"] = false; data["ignored"] = false; }
        else if (action == "ignore") data["ignored"] = true;
        else if (action != "select" && action != "unselect") throw new IOException("未知录音操作");
        data["selectedForTraining"] = action == "select"; data["updatedUtc"] = DateTime.UtcNow.ToString("o"); Write(entry, data);
    }
    internal void Remove(HistoryEntry entry) {
        if (File.ReadAllText(entry.FileName) != entry.Original) throw new IOException("记录已修改，请刷新后重试");
        File.Move(entry.FileName, entry.FileName + ".deleted");
    }
    internal void ValidatePermanentRemoval(HistoryEntry entry) {
        string folder = Path.GetFullPath(Path.Combine(Root, entry.Kind)); Guid id;
        if ((entry.Kind != "voice-samples" && entry.Kind != "dictation-history") || !Guid.TryParseExact(entry.Text("id"), "N", out id) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.FileName)), folder, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(entry.FileName) != entry.Text("id") + ".json" || entry.Text("wav") != entry.Text("id") + ".wav") throw new IOException("删除路径不属于此录音，未处理");
        if (File.ReadAllText(entry.FileName) != entry.Original) throw new IOException("记录已被外部修改，请刷新后再删除");
    }
    internal void PermanentlyRemove(HistoryEntry entry) {
        ValidatePermanentRemoval(entry);
        File.Delete(entry.Audio);
        File.Delete(entry.FileName);
    }
    internal static string ArchiveState(string folder, out string original) {
        string path = Path.Combine(folder, "lifecycle.json"); original = File.Exists(path) ? File.ReadAllText(path) : null;
        if (original == null) return "archived";
        var data = Parse(original); string state = data.ContainsKey("status") ? Convert.ToString(data["status"]) : "";
        if (!data.ContainsKey("schemaVersion") || Convert.ToInt32(data["schemaVersion"]) != 1 || (state != "archived" && state != "withdrawn")) throw new IOException("批次状态记录无效，未覆盖");
        return state;
    }
    internal void SetArchiveState(string folder, string expected, bool withdraw) {
        string resolved = Path.GetFullPath(folder), parent = Path.GetFullPath(Path.Combine(Root, "training-batches"));
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(resolved, "manifest.json"))) throw new IOException("批次路径不属于当前归档");
        string current; string before = ArchiveState(resolved, out current);
        if (current != expected || before == (withdraw ? "withdrawn" : "archived")) throw new IOException("批次状态已变化，请刷新后重试");
        var data = current == null ? new Dictionary<string, object>() : Parse(current);
        var history = data.ContainsKey("history") ? new List<object>((object[])data["history"]) : new List<object>();
        string after = withdraw ? "withdrawn" : "archived", now = DateTime.UtcNow.ToString("o");
        history.Add(new { from = before, to = after, createdUtc = now });
        data["schemaVersion"] = 1; data["status"] = after; data["updatedUtc"] = now; data["history"] = history;
        Atomic(Path.Combine(resolved, "lifecycle.json"), new JavaScriptSerializer().Serialize(data));
    }
    internal static void ValidateAudio(HistoryEntry entry) {
        using (var reader = new BinaryReader(File.OpenRead(entry.Audio))) {
            if (reader.BaseStream.Length != 44 + (long)Math.Round(entry.Seconds * 16000) * 2 ||
                Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new IOException("录音长度或格式异常");
            reader.BaseStream.Position = 8;
            if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "WAVEfmt " || reader.ReadInt32() != 16 || reader.ReadInt16() != 1 || reader.ReadInt16() != 1 || reader.ReadInt32() != 16000 || reader.ReadInt32() != 32000 || reader.ReadInt16() != 2 || reader.ReadInt16() != 16 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "data" || reader.ReadInt32() != reader.BaseStream.Length - 44)
                throw new IOException("需要完整的 16kHz 单声道 PCM16 录音");
        }
    }
    internal string Export(List<HistoryEntry> confirmed = null, string name = null) {
        name = string.IsNullOrWhiteSpace(name) ? "批次 " + DateTime.Now.ToString("MM-dd HH:mm") : name.Trim();
        if (name.Length > 80 || Array.Exists(name.ToCharArray(), char.IsControl)) throw new IOException("批次名称限80字，不能含换行或控制字符");
        string errors; long bytes; var entries = Load(out errors, out bytes);
        if (errors.Length > 0) throw new IOException("有损坏记录，请先检查，未导出不完整批次：\n" + errors);
        var chosen = entries.FindAll(item => item.Flag("selectedForTraining") && !item.Flag("ignored") && (confirmed == null || confirmed.Exists(value => value.FileName == item.FileName)));
        if (confirmed != null && (chosen.Count != confirmed.Count || chosen.Exists(item => confirmed.Find(value => value.FileName == item.FileName).Original != item.Original))) throw new IOException("本批录音或文字已变化，请刷新后重新确认");
        if (chosen.Count == 0) throw new IOException("请先试听、核对并勾选留作训练");
        string target = Path.Combine(Root, "training-batches", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        string staging = target + ".partial"; Directory.CreateDirectory(Path.Combine(staging, "audio"));
        var samples = new List<object>(); var lines = new List<string>();
        foreach (var entry in chosen) {
            ValidateAudio(entry); string filename = entry.Kind + "-" + entry.Text("wav");
            string copied = Path.Combine(staging, "audio", filename); File.Copy(entry.Audio, copied);
            string hash; using (var sha = System.Security.Cryptography.SHA256.Create()) using (var stream = File.OpenRead(copied)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            samples.Add(new { sourceKind = entry.Kind, sourceId = entry.Text("id"), audio = "audio/" + filename, sha256 = hash, metadata = Parse(entry.Original) });
            lines.Add(new JavaScriptSerializer().Serialize(new { sourceKind = entry.Kind, sourceId = entry.Text("id"), audio = "audio/" + filename, sha256 = hash,
                promptText = entry.Text("promptText"), spokenText = entry.Text("correctText"), recognizedText = entry.Text("text"), rawRecognition = entry.Text("rawText"), firstDraft = entry.Text("firstText"), correctText = entry.Text("correctText"),
                roundId = entry.Text("roundId"), sourceGroup = entry.Text("roundId").Length > 0 ? entry.Text("roundId") : "manual-date:" + entry.Text("createdUtc").Substring(0, 10),
                createdUtc = entry.Text("createdUtc"), durationSeconds = entry.Seconds, model = entry.Text("model"), reviewed = true, split = "unassigned" }));
        }
        File.WriteAllText(Path.Combine(staging, "manifest.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new {
            schemaVersion = 2, name = name, status = "prepared_not_trained", createdUtc = DateTime.UtcNow.ToString("o"), split = "unassigned_keep_rounds_together", samples = samples
        }), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(staging, "samples.jsonl"), lines.ToArray(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(staging, "下一步.txt"), "本批次只准备材料，尚未训练。\r\n先检查 lifecycle.json：status=withdrawn 表示已撤回，暂停使用；archived 或没有此文件表示有效归档。\r\n撤回和恢复只改状态，不改原音或清单快照。\r\n\r\n批次位置：" + target + "\r\n清单：manifest.json；原音：audio/。\r\n\r\n1. 保留本批次不改；后续改字请另建批次。\r\n2. 在隔离训练环境导入 samples[].metadata.correctText 与音频，按原录音轮次分训练/开发/验收，不按切片随机分组。\r\n3. 以当前生产 R5 INT8 为对照，单独训练候选、导出 INT8，比较中文、英文、混说及速度和内存。\r\n4. 另录新会话验收；检查救援和词典替换前的裸识别，避免把替换成功当成模型学会。\r\n5. 用户验证通过后，由agent完成通用回归、正式启用与R5回滚准备；未通过继续用现模型。\r\n\r\n可向 Codex 提出：请读取此批次 manifest.json，先核验音频与完整标签、按轮次去重分组和估算训练空间，给出隔离训练及验收方案。不要覆盖日用模型，不要把本批次直接当最终验收集。需要移动固态空间时先确认。\r\n\r\n应用只负责记录、批次归档和人工验证。训练由agent执行；训练完成后用 app/training_review.py register 登记候选、来源归档和报告，用户在「验证」录新音对照后通过或打回。通过只代表个人复测通过，agent还需完成通用回归、INT8速度/内存、生产启用与回滚校验。启用后用 deployed 命令核对生产哈希并记录。详见 docs/训练交接与验证协议.md。", new UTF8Encoding(false));
        Directory.Move(staging, target); return target;
    }
}

sealed class TrainingReviewPanel : Panel
{
    readonly string root, programRoot;
    readonly ComboBox versions = new ComboBox();
    readonly HashSet<string> checkedCases = new HashSet<string>();
    bool multi;
    Button versionPicker, multiButton, allButton, clearButton, inspectButton;
    readonly Label selectedCount = new DictionaryLabel();
    readonly DictionaryListBox cases = new DictionaryListBox();
    readonly DictionaryTextBox details = new DictionaryTextBox();
    readonly TextBox note = new TextBox();
    readonly Label notice;
    readonly Button captureButton, retryButton, rejectButton, passButton;
    internal readonly Label Summary = new DictionaryLabel();
    internal readonly List<Button> Footer = new List<Button>();
    readonly List<Dictionary<string, object>> runs = new List<Dictionary<string, object>>();
    readonly DictionaryScrollBar casesScroll;
    internal bool busy;
    bool silent;
    Process child;
    SampleCaptureDialog capture;
    internal Func<Task> PrepareRecording;
    internal Action<List<Dictionary<string, object>>> RunsChanged;
    internal string SelectedRunId = "";
    internal bool RecordingBusy { get { return capture != null && !capture.IsDisposed && capture.RecordingBusy; } }
    static string TextOf(Dictionary<string, object> data, string key) { object value; return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
    static object[] ArrayOf(Dictionary<string, object> data, string key) { object value; return data != null && data.TryGetValue(key, out value) && value is object[] ? (object[])value : new object[0]; }
    Dictionary<string, object> Current { get { return versions.SelectedIndex >= 0 && versions.SelectedIndex < runs.Count ? runs[versions.SelectedIndex] : null; } }
    internal TrainingReviewPanel(string root, Label notice) {
        this.root = root; this.notice = notice; DoubleBuffered = true; programRoot = Path.GetDirectoryName(Application.StartupPath); Size = new Size(574, 396); BackColor = Color.FromArgb(251, 250, 248); Font = new Font(HistoryCard.TextFamily, 11f);
        Summary.Bounds = new Rectangle(314, 48, 554, 26); Summary.Font = new Font(Font.FontFamily, 10f); Summary.ForeColor = Color.FromArgb(96, 100, 107);
        Controls.Add(Caption("候选版本", 10, 0, 78, 36));
        versionPicker = new DictionaryButton { Bounds = new Rectangle(96, 0, 468, 36), Font = Font, BackColor = BackColor, TextAlign = ContentAlignment.MiddleLeft, Outlined = true, AccessibleName = "选择候选版本" };
        versionPicker.Click += (o, e) => { var menu = new HistoryMenu(); for (int i = 0; i < runs.Count; i++) { int index = i; menu.Items.Add(TextOf(runs[i], "name") + " · " + TextOf(runs[i], "displayStatus"), null, (sender, args) => versions.SelectedIndex = index); } if (runs.Count == 0) menu.Items.Add("暂无候选").Enabled = false; menu.Closed += (sender, args) => { if (IsDisposed || !IsHandleCreated) menu.Dispose(); else try { BeginInvoke(new Action(() => menu.Dispose())); } catch (InvalidOperationException) { menu.Dispose(); } }; menu.Show(versionPicker, new Point(0, versionPicker.Height)); }; Controls.Add(versionPicker);
        versions.Font = Font; versions.DropDownStyle = ComboBoxStyle.DropDownList;
        multiButton = Toolbar("多选", 10, 88, () => { multi = !multi; checkedCases.Clear(); cases.Invalidate(); UpdateActions(); });
        allButton = Toolbar("全选", 106, 72, () => { foreach (Dictionary<string, object> item in cases.Items) checkedCases.Add(TextOf(item, "id")); cases.Invalidate(); UpdateActions(); });
        clearButton = Toolbar("清空", 186, 72, () => { checkedCases.Clear(); cases.Invalidate(); UpdateActions(); });
        inspectButton = Toolbar("查看对照", 452, 112, ShowDetail);
        selectedCount.Bounds = new Rectangle(270, 50, 174, 28); selectedCount.Font = Font; selectedCount.ForeColor = Color.FromArgb(96, 100, 107); Controls.Add(selectedCount);
        versions.SelectedIndexChanged += (o, e) => { if (!silent) { checkedCases.Clear(); multi = false; SelectedRunId = TextOf(Current, "id"); ShowCases(); } };
        cases.Bounds = new Rectangle(10, 88, 538, 88); cases.DrawMode = DrawMode.OwnerDrawFixed; cases.ItemHeight = 56; cases.CardFeedback = true; cases.IntegralHeight = false; cases.BorderStyle = BorderStyle.None; cases.BackColor = HistoryCard.Canvas; cases.AccessibleName = "独立新录音复测列表"; Controls.Add(cases);
        cases.DrawItem += (o, e) => { if (e.Index < 0) return; var item = (Dictionary<string, object>)cases.Items[e.Index]; e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            HistoryCard.Draw(e.Graphics, e.Bounds, e.Index + 1, Font, (e.State & DrawItemState.Selected) != 0, multi && checkedCases.Contains(TextOf(item, "id")), cases.HoverIndex == e.Index, cases.PressedIndex == e.Index, cases.Focused && cases.SelectedIndex == e.Index);
            int inset = multi ? 42 : 12;
            if (multi) HistoryCard.Check(e.Graphics, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 17, 18, 18), checkedCases.Contains(TextOf(item, "id")), cases.HoverIndex == e.Index, cases.PressedIndex == e.Index, cases.Focused && cases.SelectedIndex == e.Index, true);
            using (var brush = new SolidBrush(Color.FromArgb(20, 25, 31))) using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) e.Graphics.DrawString(TextOf(item, "correctText"), Font, brush, new Rectangle(e.Bounds.X + inset, e.Bounds.Y + 6, e.Bounds.Width - inset - 12, 24), format);
            using (var font = new Font(Font.FontFamily, 9.5f)) HistoryStatusTag.Draw(e.Graphics, TextOf(item, "status") == "evaluated" ? "已对照" : "待识别", font, new Point(e.Bounds.X + inset, e.Bounds.Y + 27)); };
        cases.SelectedIndexChanged += (o, e) => UpdateActions();
        cases.MouseClick += (o, e) => { int index = cases.IndexFromPoint(e.Location); if (multi && e.Button == MouseButtons.Left && index >= 0 && HistoryCard.Bounds(cases.GetItemRectangle(index)).Contains(e.Location)) ToggleCase(index); };
        cases.MouseDoubleClick += (o, e) => { if (!multi && e.Button == MouseButtons.Left && cases.IndexFromPoint(e.Location) >= 0 && HistoryCard.Bounds(cases.GetItemRectangle(cases.IndexFromPoint(e.Location))).Contains(e.Location)) ShowDetail(); };
        cases.KeyDown += (o, e) => { if (multi && e.KeyCode == Keys.Space && cases.SelectedIndex >= 0) { ToggleCase(cases.SelectedIndex); e.Handled = e.SuppressKeyPress = true; } else if (!multi && e.KeyCode == Keys.Enter) { ShowDetail(); e.Handled = e.SuppressKeyPress = true; } };
        casesScroll = new DictionaryScrollBar(() => cases.Items.Count, () => Math.Max(1, cases.Height / cases.ItemHeight), () => cases.TopIndex, value => { if (cases.Items.Count > 0) cases.TopIndex = value; }); casesScroll.Bounds = new Rectangle(552, 88, 12, 88); casesScroll.BackColor = BackColor; casesScroll.CardTrack = true; casesScroll.TabStop = false; Controls.Add(casesScroll);
        cases.ViewChanged = () => { casesScroll.TabStop = casesScroll.Overflows; casesScroll.Invalidate(); };
        Controls.Add(Caption("识别对照（只读）", 10, 184, 554, 26));
        var field = new DictionaryField { Bounds = new Rectangle(10, 212, 554, 80) }; details.Bounds = new Rectangle(12, 8, 514, 64); details.Multiline = true; details.ReadOnly = true; details.BorderStyle = BorderStyle.None; details.Font = new Font(Font.FontFamily, 11f); details.BackColor = Color.FromArgb(246, 245, 243); details.AccessibleName = "本条新录音的原模型与候选结果及正确文字"; field.Controls.Add(details); Controls.Add(field);
        var textScroll = new DictionaryScrollBar(() => (int)DictionaryScrollBar.SendMessage(details.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero), () => Math.Max(1, details.Height / details.Font.Height), () => (int)DictionaryScrollBar.SendMessage(details.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero), value => { int top = (int)DictionaryScrollBar.SendMessage(details.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero); DictionaryScrollBar.SendMessage(details.Handle, 0x00B6, IntPtr.Zero, new IntPtr(value - top)); });
        textScroll.Bounds = new Rectangle(534, 8, 12, 64); textScroll.BackColor = details.BackColor; textScroll.TabStop = false; field.Controls.Add(textScroll); details.ViewChanged = () => { textScroll.TabStop = textScroll.Overflows; textScroll.Invalidate(); };
        Controls.Add(Caption("备注（打回时必填）", 10, 300, 554, 26));
        var noteField = new DictionaryField { Bounds = new Rectangle(10, 328, 554, 36) }; note.Bounds = new Rectangle(12, 6, 530, 24); note.BorderStyle = BorderStyle.None; note.Font = Font; note.BackColor = details.BackColor; note.MaxLength = 2000; note.AccessibleName = "验证备注，打回必须填写原因"; noteField.Controls.Add(note); Controls.Add(noteField);
        captureButton = ActionButton("录新音", 314, 124, async () => await RecordCase()); retryButton = ActionButton("重试识别", 450, 124, async () => await RetryCases()); rejectButton = ActionButton("打回", 586, 124, async () => await Decide("rejected")); passButton = ActionButton("通过", 746, 124, async () => await Decide("passed")); ((DictionaryButton)passButton).Primary = true;
        rejectButton.ForeColor = Color.FromArgb(166, 48, 43); ShowCases();
    }
    Button Toolbar(string text, int x, int width, Action action) { var button = new DictionaryButton { Text = text, Bounds = new Rectangle(x, 48, width, 32), Font = Font, Outlined = true, BackColor = BackColor }; button.Click += (o, e) => action(); Controls.Add(button); return button; }
    void ToggleCase(int index) { string id = TextOf((Dictionary<string, object>)cases.Items[index], "id"); if (!checkedCases.Remove(id)) checkedCases.Add(id); cases.Invalidate(cases.GetItemRectangle(index)); UpdateActions(); }
    async Task RetryCases() { if (Current != null && (TextOf(Current, "status") == "passed" || TextOf(Current, "status") == "rejected")) { await Act("reopen", null); return; } if (multi) { if (checkedCases.Count == 0) return; await Act("evaluate", new Dictionary<string, object> { { "caseIds", new List<string>(checkedCases).ToArray() } }); } else await Act("evaluate", null); }
    Label Caption(string text, int x, int y, int width, int height) { return new DictionaryLabel { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font(Font.FontFamily, 10f), ForeColor = Color.FromArgb(96, 100, 107) }; }
    Button ActionButton(string text, int x, int width, Action click) { var button = new DictionaryButton { Text = text, Bounds = new Rectangle(x, 506, width, 52), Font = new Font(Font.FontFamily, 12f), BackColor = BackColor, ForeColor = Color.FromArgb(20, 25, 31), AccessibleName = text }; button.Click += (o, e) => click(); Footer.Add(button); return button; }
    internal void StopCapture() { if (capture != null && !capture.IsDisposed) capture.StopSampleRecording(); }
    internal bool ResolveCapture() { if (capture != null && !capture.IsDisposed && capture.Visible) { capture.Close(); return !capture.Visible; } return true; }
    async Task<Dictionary<string, object>> Execute(Dictionary<string, object> request) {
        request["root"] = root;
        var info = new ProcessStartInfo(Path.Combine(programRoot, ".venv", "Scripts", "python.exe"), "\"" + Path.Combine(programRoot, "app", "training_review.py") + "\" --request") { WorkingDirectory = programRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using (var process = new Process { StartInfo = info }) {
            child = process; process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) await input.WriteAsync(new JavaScriptSerializer().Serialize(request));
            var reading = Task.WhenAll(output, errors); if (await Task.WhenAny(reading, Task.Delay(180000)) != reading) { if (!process.HasExited) process.Kill(); throw new IOException("复测超过等待时间，原音已保留，可重试识别"); }
            await reading; var response = HistoryStore.Parse(await output); object ok; if (!response.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) throw new IOException(TextOf(response, "error"));
            child = null; return response;
        }
    }
    void Apply(Dictionary<string, object> response) {
        if (IsDisposed) return; string selected = SelectedRunId, draftNote = note.Text;
        bool keepNote = Current != null && TextOf(Current, "status") == "awaiting_retest";
        runs.Clear(); foreach (object item in ArrayOf(response, "runs")) runs.Add((Dictionary<string, object>)item);
        silent = true; versions.Items.Clear(); foreach (var run in runs) versions.Items.Add(TextOf(run, "name") + " · " + TextOf(run, "displayStatus"));
        versions.SelectedIndex = runs.FindIndex(item => TextOf(item, "id") == selected); if (versions.SelectedIndex < 0 && runs.Count > 0) versions.SelectedIndex = 0; silent = false; SelectedRunId = TextOf(Current, "id"); ShowCases();
        if (keepNote && SelectedRunId == selected && TextOf(Current, "status") == "awaiting_retest") note.Text = draftNote;
        if (ArrayOf(response, "errors").Length > 0) notice.Text = "部分验证记录读取失败：" + string.Join("；", Array.ConvertAll(ArrayOf(response, "errors"), Convert.ToString));
        if (RunsChanged != null) RunsChanged(runs);
    }
    internal async Task Reload() { if (busy || IsDisposed) return; busy = true; UpdateActions(); try { Apply(await Execute(new Dictionary<string, object> { { "action", "list" } })); } catch (Exception error) { if (!IsDisposed) notice.Text = "验证记录未读取：" + error.Message; } finally { busy = false; child = null; if (!IsDisposed) UpdateActions(); } }
    async Task Act(string action, Dictionary<string, object> extra) {
        if (busy || Current == null) return; var request = extra ?? new Dictionary<string, object>(); request["action"] = action; request["id"] = TextOf(Current, "id"); busy = true; UpdateActions(); notice.Text = action == "capture" || action == "evaluate" ? "正在对新录音运行原模型和候选对照，原音已保存…" : "正在记录验证结论…";
        string failure = null;
        try { Apply(await Execute(request)); if (!IsDisposed) notice.Text = action == "reopen" ? "已撤回结论，返回待复测；原音、对照与历次结论保留。" : action == "decide" ? "验证结论已保存。通过后交给agent校验、启用并准备回滚。" : "识别对照已完成，请核对新录音和正确全文。"; }
        catch (Exception error) { failure = error.Message; }
        if (failure != null && !IsDisposed) { try { Apply(await Execute(new Dictionary<string, object> { { "action", "list" } })); } catch { } if (!IsDisposed) notice.Text = "操作未完成，原音保留：" + failure; }
        busy = false; child = null; if (!IsDisposed) UpdateActions();
    }

    async Task RecordCase() {
        if (busy || Current == null) return;
        using (var dialog = new SampleCaptureDialog(Path.Combine(root, "user-data", "training-review", "captures", Guid.NewGuid().ToString("N")))) {
            capture = dialog; dialog.PrepareRecording = PrepareRecording;
            try { dialog.ShowDialog(FindForm()); if (dialog.SavedSample != null) await Act("capture", new Dictionary<string, object> { { "audio", dialog.SavedAudioPath }, { "correctText", dialog.SavedSample.correctText } }); }
            finally { capture = null; }
        }
    }
    async Task Decide(string decision) {
        if (decision == "rejected" && string.IsNullOrWhiteSpace(note.Text)) { notice.Text = "请填写打回原因，方便agent重新训练。"; note.Focus(); return; }
        if (MessageBox.Show(FindForm(), decision == "passed" ? "确认已逐条核对本版本的新录音复测？\n这里只记录个人验证通过，正式启用仍由agent完成通用回归和回滚准备。" : "将此版本打回，保留原音和失败原因，由agent重新训练新版本。", "验证结论", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        await Act("decide", new Dictionary<string, object> { { "decision", decision }, { "note", note.Text } });
    }
    void ShowCases() {
        cases.Items.Clear(); var run = Current; foreach (object item in ArrayOf(run, "cases")) cases.Items.Add((Dictionary<string, object>)item); if (cases.Items.Count > 0) cases.SelectedIndex = cases.Items.Count - 1;
        versionPicker.Text = run == null ? "暂无候选" : TextOf(run, "name") + " · " + TextOf(run, "displayStatus"); versionPicker.AccessibleName = "候选版本：" + versionPicker.Text;
        checkedCases.RemoveWhere(id => !Array.Exists(ArrayOf(run, "cases"), value => TextOf((Dictionary<string, object>)value, "id") == id));
        Summary.Text = run == null ? "等待agent登记训练后的候选版本" : TextOf(run, "displayStatus") + " · " + cases.Items.Count + " 条独立新录音";
        var decision = run != null && run.ContainsKey("decision") ? run["decision"] as Dictionary<string, object> : null; note.Text = TextOf(decision, "note"); note.ReadOnly = run == null || TextOf(run, "status") != "awaiting_retest"; ShowDetail(); UpdateActions();
    }
    void ShowDetail() {
        var item = cases.SelectedItem as Dictionary<string, object>;
        details.Text = item == null ? (Current == null ? "训练由agent完成后登记候选、来源批次和训练报告。\r\n登记后在这里录新音，对照结果，再通过或打回。" : "请录制登记训练完成之后的新句子；训练原音不能用作复测。") : "原模型：" + TextOf(item, "baselineText") + "\r\n候选：" + TextOf(item, "candidateText") + "\r\n正确全文：" + TextOf(item, "correctText");
    }
    void UpdateActions() {
        bool held = Current != null && Current.ContainsKey("sourceWithdrawn") && Current["sourceWithdrawn"] is bool && (bool)Current["sourceWithdrawn"];
        bool pending = !busy && !held && Current != null && TextOf(Current, "status") == "awaiting_retest";
        versions.Enabled = !busy; versionPicker.Enabled = !busy && runs.Count > 0; multiButton.Enabled = !busy && cases.Items.Count > 0; multiButton.Text = multi ? "完成" : "多选"; allButton.Visible = clearButton.Visible = multi; allButton.Enabled = clearButton.Enabled = !busy && cases.Items.Count > 0; inspectButton.Visible = !multi; inspectButton.Enabled = !busy && cases.SelectedIndex >= 0; selectedCount.Text = multi ? "已选 " + checkedCases.Count + " 条" : ""; captureButton.Enabled = !busy && !held && !multi && Current != null && (pending || TextOf(Current, "status") == "passed"); rejectButton.Enabled = pending && !multi;
        bool evaluated = cases.Items.Count > 0; bool unevaluated = false; foreach (Dictionary<string, object> item in cases.Items) { evaluated &= TextOf(item, "status") == "evaluated"; unevaluated |= TextOf(item, "status") != "evaluated"; }
        bool selectedPending = false; foreach (Dictionary<string, object> item in cases.Items) if (checkedCases.Contains(TextOf(item, "id")) && TextOf(item, "status") != "evaluated") selectedPending = true;
        bool canReopen = !busy && !multi && Current != null && (TextOf(Current, "status") == "passed" || TextOf(Current, "status") == "rejected");
        passButton.Enabled = pending && evaluated && !multi; retryButton.Text = canReopen ? "撤回结论" : multi ? "重试所选" : "重试识别"; retryButton.Enabled = canReopen || (pending && (multi ? selectedPending : unevaluated));
    }
    internal void CheckForTest(string work, string source, string archive) {
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception("REVIEW-TEST: " + message); };
        check(Top + 328 + 36 <= notice.Top, "verification note stays above feedback area");
        check(versions.Items.Count == 0 && !captureButton.Enabled && !passButton.Enabled, "no candidate means no false verification actions");
        string id = Guid.NewGuid().ToString("N");
        var response = HistoryStore.Parse(new JavaScriptSerializer().Serialize(new { runs = new[] { new { id = id, name = "个人微调 · 测试版本", status = "awaiting_retest", displayStatus = "待复测", archives = new[] { new { path = archive, sources = new[] { source } } }, cases = new[] { new { id = "test-case", correctText = "今天不用 Codex，先跑测试。", baselineText = "今天不用扣desks，先跑测试。", candidateText = "今天不用 Codex，先跑测试。", status = "evaluated", durationSeconds = 1.0 } } } }, errors = new string[0] }));
        Action refresh = () => Apply(HistoryStore.Parse(new JavaScriptSerializer().Serialize(response)));
        refresh(); check(passButton.Enabled && captureButton.Enabled && details.Text.Contains("原模型") && details.Text.Contains("正确全文"), "fresh comparison and explicit pass affordance");
        note.Text = "需要保留的打回原因"; refresh();
        check(note.Text == "需要保留的打回原因", "refresh of the same pending version retains unsaved verification note");
        var refreshedRun = (Dictionary<string, object>)ArrayOf(response, "runs")[0];
        refreshedRun["status"] = "rejected"; refreshedRun["decision"] = new Dictionary<string, object> { { "note", "已保存的正式结论" } }; refresh();
        check(note.ReadOnly && note.Text == "已保存的正式结论", "saved conclusion replaces draft note after status change");
        refreshedRun["status"] = "awaiting_retest"; refreshedRun.Remove("decision"); refresh();
        note.Text = "不能串到其他版本"; refreshedRun["id"] = Guid.NewGuid().ToString("N"); refresh();
        check(note.Text == "" && SelectedRunId != id, "draft note never leaks to another candidate version");
        note.Clear(); rejectButton.PerformClick(); check(notice.Text.Contains("填写打回原因"), "reject requires an actionable reason");
        var form = FindForm(); using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-verification.png")); }
        var item = (Dictionary<string, object>)cases.Items[0]; item["status"] = "pending"; UpdateActions(); check(!passButton.Enabled && retryButton.Enabled, "pending recognition blocks approval and exposes retry");
        var second = new Dictionary<string, object>(item); second["id"] = "test-case-2"; second["correctText"] = "另一个新句子"; second["status"] = "evaluated"; cases.Items.Add(second);
        string previousDetail = details.Text; cases.SelectedIndex = 1; check(details.Text == previousDetail, "single selection does not change opened comparison"); inspectButton.PerformClick(); check(details.Text.Contains("另一个新句子"), "explicit inspection opens selected comparison");
        multiButton.PerformClick(); allButton.PerformClick(); check(checkedCases.Count == 2 && !passButton.Enabled && !rejectButton.Enabled && retryButton.Enabled, "verification multi-select retries pending cases without bulk approval");
        clearButton.PerformClick(); check(checkedCases.Count == 0 && !retryButton.Enabled, "empty verification selection disables retry"); ToggleCase(0); check(retryButton.Enabled && checkedCases.SetEquals(new[] { "test-case" }), "whole-card selection determines retry scope");
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-verification-batch.png")); }
        multiButton.PerformClick();
        foreach (object value in ArrayOf(Current, "cases")) ((Dictionary<string, object>)value)["status"] = "evaluated";
        Current["status"] = "passed"; Current["displayStatus"] = "验证通过"; Current["decision"] = new Dictionary<string, object> { { "note", "原验证结论保留" } }; ShowCases();
        check(retryButton.Enabled && retryButton.Text == "撤回结论" && note.ReadOnly && note.Text.Contains("原验证结论"), "passed conclusion has a reversible action and retains its note");
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-verification-reopen.png")); }
        Current["status"] = "rejected"; ShowCases(); check(retryButton.Enabled && retryButton.Text == "撤回结论", "rejected conclusion can also return to pending");
        Current["status"] = "awaiting_retest"; Current["sourceWithdrawn"] = true; ShowCases(); check(!passButton.Enabled && !captureButton.Enabled && !retryButton.Enabled, "withdrawn source prevents verification advancement");
        Apply(HistoryStore.Parse("{\"runs\":[],\"errors\":[]}"));
        Console.WriteLine("REVIEW-TEST PASS: reversible conclusions and withdrawn-source guard / scoped multi-select retry / explicit inspection / candidate gating / fresh before-after display / explicit approval / rejection reason / failed recognition retry / offscreen layout");
    }
    protected override void Dispose(bool disposing) { if (disposing) { versions.Dispose(); if (capture != null && !capture.IsDisposed) capture.Dispose(); if (child != null) try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { } } base.Dispose(disposing); }
}

sealed class HistoryPanel : Panel
{
    static readonly Color Face = Color.FromArgb(251, 250, 248), Field = Color.FromArgb(246, 245, 243), Ink = Color.FromArgb(20, 25, 31), Dim = Color.FromArgb(96, 100, 107);
    readonly HistoryStore store;
    readonly CheckBox enabled = new DictionaryCheckBox(), selected = new DictionaryCheckBox();
    readonly DictionaryListBox rows = new DictionaryListBox();
    readonly TextBox search = new TextBox();
    readonly DictionaryTextBox original = new DictionaryTextBox(), correct = new DictionaryTextBox();
    readonly ComboBox filter = new ComboBox(), stage = new ComboBox();
    readonly Label summary = new DictionaryLabel(), notice = new DictionaryLabel(), empty = new DictionaryLabel();
    readonly System.Media.SoundPlayer player = new System.Media.SoundPlayer();
    readonly System.Windows.Forms.Timer playback = new System.Windows.Forms.Timer { Interval = 100 };
    readonly Stopwatch clock = new Stopwatch();
    readonly List<HistoryEntry> entries = new List<HistoryEntry>();
    readonly Button play, save, export;
    Button undoButton, batchToggle, filterButton, clearSearch;
    readonly Panel batchBar = new Panel();
    readonly Label markedLabel = new DictionaryLabel();
    readonly HashSet<string> marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    sealed class Change { internal string File, Before, After; }
    readonly List<List<Change>> undo = new List<List<Change>>();
    bool batchMode;
    string latestBatch = "";
    readonly Panel libraryPage = new Panel(), editorPage = new Panel(), materialsPage = new Panel();
    readonly DictionaryListBox materialRows = new DictionaryListBox();
    readonly Label materialSummary = new DictionaryLabel(), editorSummary = new DictionaryLabel();
    readonly HistoryStatusTag editorTag = new HistoryStatusTag();
    Button libraryNav, materialsNav, reviewNav, supplement, openRecording;
    readonly TrainingReviewPanel reviewPage;
    readonly ComboBox batchChoice = new ComboBox();
    readonly TextBox batchName = new TextBox();
    readonly DictionaryField batchNameField = new DictionaryField();
    readonly Label batchNameLabel = new DictionaryLabel();
    Button pendingBatch, archivedBatch, batchPicker, materialAll, materialClear;
    bool browsingArchives;
    bool ArchiveTab { get { return browsingArchives || ArchivedView; } }
    sealed class BatchView { internal string Path, Name, Status, Lifecycle; internal bool Withdrawn; internal List<HistoryEntry> Entries; public override string ToString() { return (Withdrawn ? "[已撤回] " : "") + Name + " · " + Entries.Count + " 条 · " + Status; } }
    readonly List<BatchView> batches = new List<BatchView>();
    readonly HashSet<string> archiveSelected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> archiveSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> trainingMarks = new Dictionary<string, string>();
    readonly Dictionary<string, string> batchMarks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    bool batchLoading;
    bool ArchivedView { get { return batchChoice.SelectedIndex > 0 && batchChoice.SelectedIndex <= batches.Count; } }
    Label pageTitle;
    readonly DictionaryHelpIcon trainingInfo = new DictionaryHelpIcon();
    readonly ToolTip trainingHelp = new ToolTip();
    const string TrainingExplanation = "新建批次：点击卡片勾选这次交给agent的录音，再归档。\n已有批次：快照保留，可撤回或恢复归档。\n空格勾选；Enter回到原录音纠错。";
    readonly List<Button> materialsFooter = new List<Button>();
    CheckBox compare;
    int page;
    sealed class Visit { internal int Page, Filter, Top, MaterialTop, Stage; internal string File, Query, TopFile, MaterialTopFile, ReviewRun, BatchPath; internal bool Compare, Archives; }
    readonly List<Visit> visits = new List<Visit>();
    int visitIndex = -1;
    internal Action NavigationChanged;
    static string TopFileOf(ListBox list) { return list.Items.Count == 0 ? "" : ((HistoryEntry)list.Items[list.TopIndex]).FileName; }
    static void RestoreTop(ListBox list, string file, int fallback) {
        if (list.Items.Count == 0) return;
        for (int i = 0; i < list.Items.Count; i++) if (((HistoryEntry)list.Items[i]).FileName == file) { list.TopIndex = i; return; }
        list.TopIndex = Math.Min(fallback, list.Items.Count - 1);
    }
    Visit CaptureVisit() { return new Visit { Page = page, Archives = ArchiveTab, BatchPath = ArchivedView ? batches[batchChoice.SelectedIndex - 1].Path : "", ReviewRun = reviewPage == null ? "" : reviewPage.SelectedRunId, Filter = filter.SelectedIndex, Query = search.Text, Top = rows.Items.Count == 0 ? 0 : rows.TopIndex, MaterialTop = materialRows.Items.Count == 0 ? 0 : materialRows.TopIndex, TopFile = TopFileOf(rows), MaterialTopFile = TopFileOf(materialRows), File = current == null ? "" : current.FileName, Compare = compare.Checked, Stage = stage.SelectedIndex }; }
    void UpdateVisit() { if (visitIndex >= 0) visits[visitIndex] = CaptureVisit(); }
    void RecordVisit() {
        var next = CaptureVisit();
        if (visitIndex >= 0 && visits[visitIndex].Page == next.Page && visits[visitIndex].File == next.File) { visits[visitIndex] = next; }
        else { if (visitIndex + 1 < visits.Count) visits.RemoveRange(visitIndex + 1, visits.Count - visitIndex - 1); visits.Add(next); if (visits.Count > 64) visits.RemoveAt(0); visitIndex = visits.Count - 1; }
        if (NavigationChanged != null) NavigationChanged();
    }
    int VisitTarget(int direction) { for (int i = visitIndex + direction; i >= 0 && i < visits.Count; i += direction) if (visits[i].Page != 1 || entries.Exists(item => item.FileName == visits[i].File)) return i; return -1; }
    internal bool CanGoBack { get { return VisitTarget(-1) >= 0; } }
    internal bool CanGoForward { get { return VisitTarget(1) >= 0; } }
    internal void GoVisit(int direction) {
        int target = VisitTarget(direction); if (loading || target < 0 || !Resolve()) return; UpdateVisit(); Stop(); var visit = visits[target];
        batchMode = false; marked.Clear(); UpdateMarked(); silent = true; filter.SelectedIndex = lastFilter = visit.Filter; search.Text = lastSearch = visit.Query; silent = false; Filter();
        var entry = entries.Find(item => item.FileName == visit.File);
        if (visit.Page == 1 && entry != null && !rows.Items.Contains(entry)) { silent = true; filter.SelectedIndex = lastFilter = 0; search.Text = lastSearch = ""; silent = false; Filter(); }
        if (entry != null && rows.Items.Contains(entry)) { silent = true; rows.SelectedItem = entry; silent = false; ShowEntry(entry); }
        stage.SelectedIndex = visit.Stage; compare.Checked = visit.Compare; ShowOriginal(visit.Compare);
        RestoreTop(rows, visit.TopFile, visit.Top);
        reviewPage.SelectedRunId = visit.ReviewRun; SetPage(visit.Page); int restoredBatch = batches.FindIndex(item => item.Path == visit.BatchPath); batchChoice.SelectedIndex = restoredBatch < 0 ? 0 : restoredBatch + 1; browsingArchives = visit.Archives; ShowBatch(); RestoreTop(materialRows, visit.MaterialTopFile, visit.MaterialTop); visitIndex = target; notice.Text = ""; if (NavigationChanged != null) NavigationChanged();
    }
    readonly HistoryMenu moreMenu = new HistoryMenu();
    readonly HistoryMenu bulkMenu = new HistoryMenu();
    readonly ToolStripMenuItem deleteMenu = new ToolStripMenuItem("删除这条");
    Label originalLabel, correctLabel;
    DictionaryField searchField;
    DictionaryScrollBar rowsScroll, materialScroll;
    HistoryEntry current;
    bool silent, dirty, loading, exporting;
    long usageBytes;
    int lastFilter;
    string lastSearch = "";
    double playingUntil;
    internal string RuntimeNotice = "";
    internal Func<Task> PrepareRecording;
    SampleCaptureDialog capture;
    internal bool RecordingBusy { get { return (capture != null && !capture.IsDisposed && capture.RecordingBusy) || (reviewPage != null && reviewPage.RecordingBusy); } }
    internal void StopCapture() { if (capture != null && !capture.IsDisposed) capture.StopSampleRecording(); if (reviewPage != null) reviewPage.StopCapture(); }
    async void Supplement() {
        if (!Resolve()) return; Stop();
        try { using (var dialog = new SampleCaptureDialog(Path.GetDirectoryName(store.Root))) {
            capture = dialog; dialog.PrepareRecording = PrepareRecording; dialog.ShowDialog(FindForm());
        } } finally { capture = null; }
        await Reload();
    }
    internal HistoryPanel(string root) {
        store = new HistoryStore(root); DoubleBuffered = true; BackColor = Face; Font = new Font(HistoryCard.TextFamily, 11f); Size = new Size(878, 560);
        reviewPage = new TrainingReviewPanel(root, notice); reviewPage.PrepareRecording = async () => { if (PrepareRecording != null) await PrepareRecording(); }; reviewPage.RunsChanged = UpdateTrainingMarks;
        libraryNav = Button("录音", 10, 4, 248, 42, () => Navigate(0)); Controls.Add(libraryNav);
        materialsNav = Button("训练", 10, 58, 248, 42, () => Navigate(2)); Controls.Add(materialsNav);
        reviewNav = Button("验证", 10, 112, 248, 42, () => Navigate(3)); Controls.Add(reviewNav);
        foreach (var nav in new[] { libraryNav, materialsNav, reviewNav }) { nav.TextAlign = ContentAlignment.MiddleLeft; nav.Font = new Font(Font.FontFamily, 15f); ((DictionaryButton)nav).Outlined = false; }
        pageTitle = Label("录音", 314, 4, 554, 38, 20f); pageTitle.Font = new Font(Font.FontFamily, 20f, FontStyle.Bold); pageTitle.ForeColor = Ink; Controls.Add(pageTitle);
        trainingInfo.Size = new Size(24, 24); trainingInfo.BackColor = Color.Transparent; trainingInfo.ForeColor = Dim; trainingInfo.AccessibleName = "训练说明"; trainingInfo.AccessibleDescription = TrainingExplanation;
        using (var bitmap = new Bitmap(1, 1)) using (var graphics = Graphics.FromImage(bitmap)) { graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; trainingInfo.Location = new Point(pageTitle.Left + (int)Math.Ceiling(graphics.MeasureString("训练", pageTitle.Font).Width) + 10, pageTitle.Top + 9); }
        Controls.Add(trainingInfo); trainingHelp.InitialDelay = 350; trainingHelp.AutoPopDelay = 15000; trainingHelp.SetToolTip(trainingInfo, TrainingExplanation);
        Action showTrainingHelp = () => { if (FindForm() != null && FindForm().Bounds.IntersectsWith(Screen.FromControl(this).WorkingArea)) trainingHelp.Show(TrainingExplanation, trainingInfo, 0, trainingInfo.Height + 6, 15000); };
        trainingInfo.Enter += (o, e) => showTrainingHelp(); trainingInfo.Click += (o, e) => showTrainingHelp(); trainingInfo.Leave += (o, e) => trainingHelp.Hide(trainingInfo);
        trainingInfo.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { showTrainingHelp(); e.Handled = e.SuppressKeyPress = true; } };

        summary.Bounds = new Rectangle(314, 48, 554, 26); summary.Font = new Font(Font.FontFamily, 10f); summary.ForeColor = Dim; Controls.Add(summary);
        undoButton = Button("撤销上次", 10, 506, 156, 52, Undo); undoButton.Enabled = false; undoButton.BackColor = Face; ((DictionaryButton)undoButton).Outlined = false; Controls.Add(undoButton);
        foreach (var panel in new Panel[] { libraryPage, editorPage, materialsPage, reviewPage }) { panel.Bounds = new Rectangle(304, 88, 574, 396); panel.BackColor = Face; Controls.Add(panel); }
        enabled.Bounds = new Rectangle(10, 416, 248, 36); enabled.Text = "保存听写录音"; enabled.AccessibleName = "保存录音，从下一轮听写生效"; Controls.Add(enabled);
        enabled.CheckedChanged += (o, e) => { if (silent) return; try { store.SetEnabled(enabled.Checked); notice.Text = enabled.Checked ? "已开启，下轮听写开始保存录音。" : "已关闭，已有录音仍保留。"; }
            catch (Exception error) { silent = true; enabled.Checked = !enabled.Checked; silent = false; notice.Text = "设置未保存：" + error.Message; } };
        supplement = Button("补录一句", 314, 506, 156, 52, Supplement); supplement.BackColor = Face; supplement.Font = new Font(Font.FontFamily, 15f); ((DictionaryButton)supplement).Outlined = false; ((DictionaryButton)supplement).Glyph = "plus"; Controls.Add(supplement);
        openRecording = Button("打开录音", 746, 506, 124, 52, () => Navigate(1)); ((DictionaryButton)openRecording).Primary = true; Controls.Add(openRecording);

        filter.DropDownStyle = ComboBoxStyle.DropDownList; filter.Items.AddRange(new object[] { "全部录音", "待核对", "已保存", "已忽略" }); filter.SelectedIndex = 0;
        filter.Bounds = new Rectangle(10, 0, 180, 32); filter.Font = new Font(Font.FontFamily, 11f); filter.AccessibleName = "录音列表筛选"; libraryPage.Controls.Add(filter); filter.Visible = false;
        filter.SelectedIndexChanged += (o, e) => { if (silent) return; if (Resolve()) { lastFilter = filter.SelectedIndex; Filter(); } else { silent = true; filter.SelectedIndex = lastFilter; silent = false; } };
        searchField = new DictionaryField { Bounds = new Rectangle(10, 0, 334, 42), Glyph = "search" };
        search.Bounds = new Rectangle(40, 10, 278, 24); search.Font = Font; search.BackColor = Field; search.BorderStyle = BorderStyle.None; search.AccessibleName = "搜索录音文字或日期";
        search.TextChanged += (o, e) => { if (silent) return; if (Resolve()) { lastSearch = search.Text; Filter(); } else { silent = true; search.Text = lastSearch; silent = false; } }; searchField.Controls.Add(search); libraryPage.Controls.Add(searchField);
        clearSearch = Button("", 296, 5, 32, 32, () => { search.Clear(); search.Focus(); }); clearSearch.AccessibleName = "清空录音搜索"; clearSearch.BackColor = Field;
        ((DictionaryButton)clearSearch).Glyph = "close"; ((DictionaryButton)clearSearch).Outlined = false; clearSearch.Visible = false; searchField.Controls.Add(clearSearch);
        search.TextChanged += (o, e) => { bool hasQuery = search.TextLength > 0; clearSearch.Visible = hasQuery; search.Width = hasQuery ? 248 : 278; };
        batchToggle = Button("多选", 460, 0, 104, 42, () => SetBatchMode(!batchMode)); libraryPage.Controls.Add(batchToggle);
        filterButton = Button("筛选", 352, 0, 100, 42, () => { }); libraryPage.Controls.Add(filterButton);
        var filterMenu = new HistoryMenu(); filterMenu.Renderer = new MicrophoneMenuRenderer(); filterMenu.ShowImageMargin = false; filterMenu.Font = Font;
        for (int i = 0; i < filter.Items.Count; i++) { int index = i; filterMenu.Items.Add(Convert.ToString(filter.Items[i]), null, (o, e) => { filter.SelectedIndex = index; filterButton.Text = filter.SelectedIndex == 0 ? "筛选" : filter.Text; }); }
        filterMenu.Items.Add(new ToolStripSeparator()); filterMenu.Items.Add("查看统计", null, (o, e) => MessageBox.Show(FindForm(), entries.Count + " 条录音；已核对 " + entries.FindAll(item => item.Flag("reviewed")).Count + " 条；已选材料 " + entries.FindAll(item => item.Flag("selectedForTraining")).Count + " 条。\n原始材料约 " + (usageBytes / 1048576.0).ToString("0.0") + " MiB。\n日常听写档案上限 1 GiB；补录和批次另占空间。", "录音统计")); filterMenu.Items.Add("刷新录音", null, async (o, e) => { if (Resolve()) await Reload(); }); filterMenu.Items.Add("使用说明", null, (o, e) => OpenGuide());
        filterButton.Click += (o, e) => filterMenu.Show(filterButton, new Point(0, filterButton.Height)); filterButton.Disposed += (o, e) => filterMenu.Dispose();
        rows.Bounds = new Rectangle(10, 56, 538, 280); rows.DrawMode = DrawMode.OwnerDrawFixed; rows.ItemHeight = 76; rows.IntegralHeight = false; rows.BorderStyle = BorderStyle.None; rows.BackColor = HistoryCard.Canvas; rows.CardFeedback = true;
        rows.AccessibleName = "录音列表，单击选中，双击或Enter打开，多选时点击整卡勾选"; libraryPage.Controls.Add(rows); rows.DrawItem += DrawRow;
        rows.SelectedIndexChanged += (o, e) => { if (silent) return; var next = rows.SelectedItem as HistoryEntry; if (next == current) return; if (!Resolve()) { silent = true; rows.SelectedItem = current; silent = false; return; } ShowEntry(next); };
        rows.MouseClick += (o, e) => { if (e.Button != MouseButtons.Left) return; int index = rows.IndexFromPoint(e.Location); if (index < 0 || !HistoryCard.Bounds(rows.GetItemRectangle(index)).Contains(e.Location)) return; if (batchMode) { ToggleMarked((HistoryEntry)rows.Items[index]); } };
        rows.MouseDoubleClick += (o, e) => { if (!batchMode && e.Button == MouseButtons.Left && rows.IndexFromPoint(e.Location) >= 0 && HistoryCard.Bounds(rows.GetItemRectangle(rows.IndexFromPoint(e.Location))).Contains(e.Location)) Navigate(1); };
        rows.KeyDown += (o, e) => { if (batchMode && e.KeyCode == Keys.Space && current != null) { ToggleMarked(current); e.Handled = e.SuppressKeyPress = true; } else if (!batchMode && e.KeyCode == Keys.Enter) { Navigate(1); e.Handled = e.SuppressKeyPress = true; } };
        rowsScroll = new DictionaryScrollBar(() => rows.Items.Count, () => Math.Max(1, rows.Height / rows.ItemHeight), () => rows.TopIndex, value => { if (rows.Items.Count > 0) rows.TopIndex = value; });
        rowsScroll.Bounds = new Rectangle(552, 56, 12, 280); rowsScroll.BackColor = Face; rowsScroll.CardTrack = true; rows.ViewChanged = () => { rowsScroll.TabStop = rowsScroll.Overflows; rowsScroll.Invalidate(); }; libraryPage.Controls.Add(rowsScroll);
        empty.Bounds = new Rectangle(22, 90, 518, 130); empty.ForeColor = Dim; libraryPage.Controls.Add(empty);
        markedLabel.Bounds = new Rectangle(10, 344, 554, 24); markedLabel.ForeColor = Dim; libraryPage.Controls.Add(markedLabel);
        batchBar.Bounds = new Rectangle(314, 506, 554, 52); batchBar.BackColor = Face; Controls.Add(batchBar);
        AddBatchButton("全选", 0, 100, () => { foreach (HistoryEntry item in rows.Items) marked.Add(item.FileName); UpdateMarked(); });
        AddBatchButton("清空", 108, 100, () => { marked.Clear(); UpdateMarked(); });
        var bulk = Button("批量操作", 430, 0, 124, 52, () => { }); ((DictionaryButton)bulk).Primary = true; batchBar.Controls.Add(bulk);
        bulkMenu.Renderer = new MicrophoneMenuRenderer(); bulkMenu.ShowImageMargin = false; bulkMenu.Font = Font;
        foreach (string action in new[] { "select", "unselect", "pending", "ignore" }) { string value = action; bulkMenu.Items.Add(action == "select" ? "留作训练" : action == "unselect" ? "取消训练" : action == "pending" ? "重新核对" : "忽略", null, (o, e) => ChangeEntries(value, true)); }
        bulkMenu.Items.Add(new ToolStripSeparator()); var removeItem = bulkMenu.Items.Add("删除录音", null, async (o, e) => await DeleteMarked()); removeItem.Tag = Color.FromArgb(166, 48, 43);
        bulk.Click += (o, e) => bulkMenu.Show(bulk, new Point(0, bulk.Height)); bulk.Disposed += (o, e) => bulkMenu.Dispose();
        editorTag.Location = new Point(314, 48); editorTag.Visible = false; Controls.Add(editorTag);
        editorSummary.Bounds = new Rectangle(314, 48, 554, 26); editorSummary.Font = new Font(Font.FontFamily, 10f); editorSummary.ForeColor = Dim; Controls.Add(editorSummary);
        play = Button("播放录音", 10, 0, 132, 42, Play); ((DictionaryButton)play).Glyph = "play"; editorPage.Controls.Add(play);
        correctLabel = Label("修改后的文字（可编辑）", 10, 56, 554, 26, 11f); editorPage.Controls.Add(correctLabel);
        compare = new DictionaryCheckBox { Text = "对照原文", Bounds = new Rectangle(414, 54, 150, 32), BackColor = Face };
        compare.CheckedChanged += (o, e) => ShowOriginal(compare.Checked); editorPage.Controls.Add(compare); compare.Visible = false;
        originalLabel = Label("原识别文字（只读）", 10, 58, 280, 26, 11f); originalLabel.Visible = false; editorPage.Controls.Add(originalLabel);
        stage.Bounds = new Rectangle(314, 54, 250, 30); stage.Font = Font; stage.DropDownStyle = ComboBoxStyle.DropDownList; stage.FlatStyle = FlatStyle.Flat; stage.AccessibleName = "原识别阶段";
        stage.Items.AddRange(new object[] { "原来的结果", "最早的草稿", "救援前的文字" }); stage.SelectedIndex = 0; stage.SelectedIndexChanged += (o, e) => OriginalText(); stage.Visible = false; editorPage.Controls.Add(stage);
        AddText(original, new Rectangle(10, 88, 554, 72), true, "原始识别，只读"); editorPage.Controls.Add(original.Parent); original.Parent.Visible = false;
        AddText(correct, new Rectangle(10, 88, 554, 228), false, "修改后的文字，可编辑，按录音修正完整内容"); editorPage.Controls.Add(correct.Parent);
        selected.Bounds = new Rectangle(10, 332, 554, 36); selected.Text = "留作训练"; selected.AccessibleName = "留作训练，明确选择并保存后生效"; selected.Font = new Font(Font.FontFamily, 12f); editorPage.Controls.Add(selected);
        correct.TextChanged += (o, e) => { if (!silent && current != null) { dirty = true; } };
        selected.CheckedChanged += (o, e) => { if (!silent && current != null) { dirty = true; } };
        save = Button("保存", 746, 506, 124, 52, SaveAndNext); ((DictionaryButton)save).Primary = true; save.Font = new Font(Font.FontFamily, 15f); Controls.Add(save);
        var more = Button("选项", 440, 0, 124, 42, () => { }); more.Click += (o, e) => moreMenu.Show(more, new Point(0, more.Height)); editorPage.Controls.Add(more);
        pendingBatch = Button("待归档", 10, 0, 104, 36, () => { browsingArchives = false; batchChoice.SelectedIndex = 0; ShowBatch(); });
        archivedBatch = Button("已归档", 122, 0, 104, 36, () => { browsingArchives = true; if (batches.Count > 0 && !ArchivedView) batchChoice.SelectedIndex = 1; ShowBatch(); });
        materialAll = Button("全选", 350, 0, 100, 36, () => { foreach (HistoryEntry item in materialRows.Items) archiveSelected.Add(item.FileName); materialRows.Invalidate(); BatchSummary(); });
        materialClear = Button("清空", 458, 0, 106, 36, () => { archiveSelected.Clear(); materialRows.Invalidate(); BatchSummary(); });
        materialsPage.Controls.AddRange(new Control[] { pendingBatch, archivedBatch, materialAll, materialClear });
        batchNameLabel.Text = "批次名"; batchNameLabel.Font = Font; batchNameLabel.ForeColor = Dim; batchNameLabel.Bounds = new Rectangle(10, 52, 68, 30); materialsPage.Controls.Add(batchNameLabel);
        batchNameField.Bounds = new Rectangle(82, 48, 482, 42); batchName.Bounds = new Rectangle(12, 10, 454, 24); batchName.Font = Font; batchName.BackColor = Field; batchName.BorderStyle = BorderStyle.None; batchName.MaxLength = 80; batchName.Text = "训练 " + DateTime.Now.ToString("MM-dd HH:mm"); batchName.AccessibleName = "新批次名称"; batchNameField.Controls.Add(batchName); materialsPage.Controls.Add(batchNameField);
        batchPicker = Button("选择批次", 10, 48, 554, 42, () => {
            var menu = new HistoryMenu(); foreach (var batch in batches) { var chosen = batch; menu.Items.Add((batch.Withdrawn ? "[已撤回] " : "") + batch.Name, null, (o, e) => { browsingArchives = true; batchChoice.SelectedIndex = batches.IndexOf(chosen) + 1; }); }
            if (batches.Count == 0) menu.Items.Add("暂无归档").Enabled = false; menu.Closed += (o, e) => { if (IsDisposed || !IsHandleCreated) menu.Dispose(); else try { BeginInvoke(new Action(() => menu.Dispose())); } catch (InvalidOperationException) { menu.Dispose(); } }; menu.Show(batchPicker, new Point(0, batchPicker.Height));
        }); batchPicker.TextAlign = ContentAlignment.MiddleLeft; materialsPage.Controls.Add(batchPicker);
        batchChoice.Font = Font; batchChoice.Visible = batchChoice.TabStop = false; batchChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        batchChoice.SelectedIndexChanged += (o, e) => { if (!batchLoading) { browsingArchives = batchChoice.SelectedIndex > 0; ShowBatch(); } };
        materialSummary.Bounds = new Rectangle(314, 48, 554, 26); materialSummary.Font = new Font(Font.FontFamily, 10f); materialSummary.ForeColor = Dim; Controls.Add(materialSummary);
        materialRows.Bounds = new Rectangle(10, 104, 538, 256); materialRows.DrawMode = DrawMode.OwnerDrawFixed; materialRows.ItemHeight = 76; materialRows.IntegralHeight = false; materialRows.BorderStyle = BorderStyle.None; materialRows.BackColor = HistoryCard.Canvas; materialRows.CardFeedback = true; materialRows.AccessibleName = "新批次二次确认，归档批次为只读快照"; materialRows.DrawItem += DrawRow; materialsPage.Controls.Add(materialRows);
        materialRows.MouseClick += (o, e) => { if (e.Button != MouseButtons.Left) return; int index = materialRows.IndexFromPoint(e.Location); if (index < 0 || !HistoryCard.Bounds(materialRows.GetItemRectangle(index)).Contains(e.Location)) return; if (!ArchiveTab) { var item = (HistoryEntry)materialRows.Items[index]; if (!archiveSelected.Remove(item.FileName)) archiveSelected.Add(item.FileName); materialRows.Invalidate(materialRows.GetItemRectangle(index)); BatchSummary(); } };
        materialRows.KeyDown += (o, e) => { if (e.KeyCode == Keys.Space && !ArchiveTab && materialRows.SelectedItem != null) { var item = (HistoryEntry)materialRows.SelectedItem; if (!archiveSelected.Remove(item.FileName)) archiveSelected.Add(item.FileName); materialRows.Invalidate(materialRows.GetItemRectangle(materialRows.SelectedIndex)); BatchSummary(); e.Handled = e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Enter && !ArchiveTab) { OpenMaterial(); e.Handled = e.SuppressKeyPress = true; } };
        materialScroll = new DictionaryScrollBar(() => materialRows.Items.Count, () => Math.Max(1, materialRows.Height / materialRows.ItemHeight), () => materialRows.TopIndex,
            value => { if (materialRows.Items.Count > 0) materialRows.TopIndex = value; }); materialScroll.Bounds = new Rectangle(552, 104, 12, 256); materialScroll.BackColor = Face; materialScroll.CardTrack = true; materialScroll.TabStop = false; materialsPage.Controls.Add(materialScroll);
        materialRows.ViewChanged = () => { materialScroll.TabStop = materialScroll.Overflows; materialScroll.Invalidate(); };
        var openBatch = Button("打开批次", 314, 506, 124, 52, OpenBatches); materialsFooter.Add(openBatch); Controls.Add(openBatch);
        var guide = Button("交接说明", 450, 506, 124, 52, OpenGuide); materialsFooter.Add(guide); Controls.Add(guide);
        export = Button("归档批次", 746, 506, 124, 52, async () => { if (ArchivedView) await ToggleArchive(); else await Export(); }); ((DictionaryButton)export).Primary = true; export.Font = new Font(Font.FontFamily, 15f); Controls.Add(export);
        Controls.Add(reviewPage.Summary); foreach (var button in reviewPage.Footer) Controls.Add(button);
        notice.Bounds = new Rectangle(314, 462, 554, 22); notice.Font = new Font(Font.FontFamily, 10f); notice.ForeColor = Dim; notice.AutoEllipsis = true; Controls.Add(notice);
        BuildMoreMenu(); playback.Tick += (o, e) => { if (clock.Elapsed.TotalSeconds >= playingUntil) Stop(); }; ShowEntry(null); SetPage(0); RecordVisit();
    }
    void Navigate(int target) {
        if (target == 1 && current == null) return; if (!Resolve()) return; UpdateVisit(); Stop(); ShowEntry(current); if (target != page) notice.Text = "";
        if (batchMode) { batchMode = false; marked.Clear(); UpdateMarked(); }
        SetPage(target); RecordVisit(); if (target == 1) { correct.Focus(); correct.SelectionStart = correct.TextLength; } else if (target == 0) rows.Focus();
    }
    void SetPage(int target) {
        trainingInfo.Visible = target == 2; trainingInfo.TabStop = target == 2;
        openRecording.Visible = target == 0 && !batchMode; openRecording.Enabled = rows.Items.Count > 0;
        page = target; summary.Visible = target == 0; libraryPage.Visible = target == 0; editorPage.Visible = target == 1; materialsPage.Visible = target == 2; reviewPage.Visible = target == 3; reviewPage.Summary.Visible = target == 3; foreach (var button in reviewPage.Footer) button.Visible = target == 3;
        libraryNav.Visible = materialsNav.Visible = true;
        libraryNav.BackColor = target == 0 || target == 1 ? Color.FromArgb(233, 232, 230) : Face; materialsNav.BackColor = target == 2 ? Color.FromArgb(233, 232, 230) : Face; reviewNav.BackColor = target == 3 ? Color.FromArgb(233, 232, 230) : Face; ((DictionaryButton)reviewNav).Selected = target == 3; ((DictionaryButton)libraryNav).Selected = target == 0 || target == 1; ((DictionaryButton)materialsNav).Selected = target == 2;
        libraryNav.AccessibleDescription = target == 0 || target == 1 ? "已选中" : "未选中"; reviewNav.AccessibleDescription = target == 3 ? "已选中" : "未选中"; reviewNav.Invalidate(); materialsNav.AccessibleDescription = target == 2 ? "已选中" : "未选中"; libraryNav.Invalidate(); materialsNav.Invalidate();
        pageTitle.Text = target == 0 ? "录音" : target == 1 ? "听音改字" : target == 2 ? "训练" : "验证";
        editorSummary.Visible = target == 1; editorTag.Visible = target == 1 && current != null; materialSummary.Visible = target == 2; save.Visible = target == 1; export.Visible = target == 2; supplement.Visible = target == 0 && !batchMode;
        foreach (var button in materialsFooter) { button.Visible = target == 2; button.BackColor = Face; ((DictionaryButton)button).Outlined = false; }
        undoButton.Visible = undo.Count > 0 && target != 3;
        if (target == 3) { var loadingReview = reviewPage.Reload(); }
        batchBar.Visible = target == 0 && batchMode; markedLabel.Visible = target == 0 && batchMode; rows.Height = rowsScroll.Height = batchMode ? 280 : 318; batchToggle.Text = batchMode ? "完成" : "多选";
        UpdateMaterials();
    }
    void OpenMaterial() {
        var entry = materialRows.SelectedItem as HistoryEntry; if (entry == null || !Resolve()) return; UpdateVisit();
        silent = true; filter.SelectedIndex = lastFilter = 0; search.Text = lastSearch = ""; silent = false; Filter(); rows.SelectedItem = entry; ShowEntry(entry); SetPage(1); RecordVisit(); correct.Focus();
    }
    void UpdateTrainingMarks(List<Dictionary<string, object>> runs) {
        trainingMarks.Clear(); batchMarks.Clear();
        foreach (var run in runs) {
            string state = Convert.ToString(run["displayStatus"]); if (state == "待复测") state = "待验证"; if (state == "验收通过") state = "验证通过";
            foreach (object archiveValue in (object[])run["archives"]) { var archive = (Dictionary<string, object>)archiveValue; string path = Convert.ToString(archive["path"]); if (!batchMarks.ContainsKey(path)) batchMarks[path] = state;
                foreach (object source in (object[])archive["sources"]) if (!trainingMarks.ContainsKey(Convert.ToString(source))) trainingMarks[Convert.ToString(source)] = state;
            }
        }
        foreach (var item in entries) { string state; item.TrainingStatus = trainingMarks.TryGetValue(item.Kind + ":" + item.Text("id"), out state) ? state : ""; }
        rows.Invalidate(); UpdateMaterials();
    }
    void UpdateMaterials() {
        if (materialSummary == null) return;
        string selectedPath = ArchivedView ? batches[batchChoice.SelectedIndex - 1].Path : ""; batches.Clear(); string folder = Path.Combine(store.Root, "training-batches");
        if (Directory.Exists(folder)) foreach (string path in Directory.GetDirectories(folder)) {
            if (path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(path, "manifest.json"))) continue;
            try { var data = HistoryStore.Parse(File.ReadAllText(Path.Combine(path, "manifest.json"))); var batch = new BatchView { Path = path, Name = data.ContainsKey("name") ? Convert.ToString(data["name"]) : "批次 " + Path.GetFileName(path), Entries = new List<HistoryEntry>() }; string state; batch.Withdrawn = HistoryStore.ArchiveState(path, out batch.Lifecycle) == "withdrawn"; batch.Status = batch.Withdrawn ? "已撤回" : batchMarks.TryGetValue(Path.GetFullPath(path), out state) ? state : "待交给agent";
                foreach (object value in (object[])data["samples"]) { var sample = (Dictionary<string, object>)value; var meta = (Dictionary<string, object>)sample["metadata"]; batch.Entries.Add(new HistoryEntry { Kind = Convert.ToString(sample["sourceKind"]), FileName = Path.Combine(store.Root, Convert.ToString(sample["sourceKind"]), Convert.ToString(sample["sourceId"]) + ".json"), Data = meta, Original = new JavaScriptSerializer().Serialize(meta) }); }
                batches.Add(batch);
            } catch (Exception error) { notice.Text = "部分批次读取失败，原文件保留：" + error.Message; }
        }
        batches.Sort((a, b) => string.CompareOrdinal(b.Path, a.Path));
        batchLoading = true; batchChoice.Items.Clear(); batchChoice.Items.Add("新建批次 · 二次确认"); foreach (var batch in batches) batchChoice.Items.Add(batch); int chosen = batches.FindIndex(item => item.Path == selectedPath); batchChoice.SelectedIndex = chosen < 0 ? 0 : chosen + 1; batchLoading = false;
        ShowBatch();
    }
    void ShowBatch() {
        materialRows.BeginUpdate(); materialRows.Items.Clear();
        archiveSelected.RemoveWhere(file => !entries.Exists(item => item.FileName == file && item.Flag("selectedForTraining") && !item.Flag("ignored")));
        if (ArchivedView) foreach (var item in batches[batchChoice.SelectedIndex - 1].Entries) materialRows.Items.Add(item);
        else if (!ArchiveTab) foreach (var item in entries) if (item.Flag("selectedForTraining") && !item.Flag("ignored")) { materialRows.Items.Add(item); bool archived = batches.Exists(batch => !batch.Withdrawn && batch.Entries.Exists(old => old.Kind == item.Kind && old.Text("id") == item.Text("id"))); if (archiveSeen.Add(item.FileName) && !archived) archiveSelected.Add(item.FileName); }
        materialRows.EndUpdate();
        pendingBatch.BackColor = !ArchiveTab ? HistoryCard.SelectedPaper : Face; archivedBatch.BackColor = ArchiveTab ? HistoryCard.SelectedPaper : Face;
        pendingBatch.ForeColor = !ArchiveTab ? HistoryCard.MarkInk : Dim; archivedBatch.ForeColor = ArchiveTab ? HistoryCard.MarkInk : Dim;
        batchNameField.Visible = batchNameLabel.Visible = !ArchiveTab; batchPicker.Visible = ArchiveTab;
        batchPicker.Text = ArchivedView ? (batches[batchChoice.SelectedIndex - 1].Withdrawn ? "[已撤回] " : "") + batches[batchChoice.SelectedIndex - 1].Name : "暂无归档"; batchPicker.AccessibleName = batchPicker.Text; trainingHelp.SetToolTip(batchPicker, batchPicker.Text); batchPicker.Enabled = batches.Count > 0;
        materialAll.Enabled = materialClear.Enabled = !ArchiveTab && materialRows.Items.Count > 0;
        BatchSummary();
    }
    void BatchSummary() {
        double seconds = 0; int count = 0; foreach (HistoryEntry item in materialRows.Items) if (ArchivedView || archiveSelected.Contains(item.FileName)) { count++; seconds += item.Seconds; }
        materialSummary.Text = ArchivedView ? batches[batchChoice.SelectedIndex - 1].Status + " · " + count + " 条 · " + seconds.ToString("0.0") + " 秒 · 快照保留" : "本批已确认 " + count + " / " + materialRows.Items.Count + " 条 · " + seconds.ToString("0.0") + " 秒";
        if (ArchiveTab && !ArchivedView) materialSummary.Text = "还没有归档批次";
        export.Text = ArchivedView ? (batches[batchChoice.SelectedIndex - 1].Withdrawn ? "恢复归档" : "撤回归档") : "归档批次";
        ((DictionaryButton)export).Primary = !ArchivedView || batches[batchChoice.SelectedIndex - 1].Withdrawn; ((DictionaryButton)export).Outlined = ArchivedView;
        export.Enabled = !exporting && (ArchivedView || (!ArchiveTab && count > 0));
    }
    void BuildMoreMenu() {
        moreMenu.Renderer = new MicrophoneMenuRenderer(); moreMenu.ShowImageMargin = false; moreMenu.Font = Font;
        var originalItem = new ToolStripMenuItem("对照原文") { CheckOnClick = true }; originalItem.CheckedChanged += (o, e) => compare.Checked = originalItem.Checked; moreMenu.Items.Add(originalItem);
        var materialItem = new ToolStripMenuItem("留作训练") { CheckOnClick = true }; materialItem.CheckedChanged += (o, e) => selected.Checked = materialItem.Checked; moreMenu.Items.Add(materialItem);
        moreMenu.Opening += (o, e) => { materialItem.Checked = selected.Checked; originalItem.Checked = compare.Checked; };
        moreMenu.Items.Add("还原本次编辑", null, (o, e) => { ShowEntry(current); notice.Text = "已恢复上次保存内容。"; });
        moreMenu.Items.Add("跳过这条", null, (o, e) => SkipRecord());
        var state = new ToolStripMenuItem("管理这条录音"); state.DropDownItems.Add("重新核对 / 恢复", null, (o, e) => ChangeEntries("pending", false)); state.DropDownItems.Add("忽略", null, (o, e) => ChangeEntries("ignore", false));
        deleteMenu.Click += (o, e) => Remove(); state.DropDownItems.Add(deleteMenu); moreMenu.Items.Add(state);
        moreMenu.Items.Add("使用说明", null, (o, e) => OpenGuide());
    }
    void SkipRecord() { if (Resolve()) { UpdateVisit(); bool moved = NextRecord(); RecordVisit(); notice.Text = moved ? "已跳过，录音仍保留。" : "已经是最后一条。"; } }
    void OpenGuide() {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(store.Root), "docs", "录音纠错使用说明书.html")) { UseShellExecute = true }); }
        catch (Exception error) { notice.Text = "说明书未打开：" + error.Message; }
    }
    void OpenBatches() {
        try { string folder = ArchivedView ? batches[batchChoice.SelectedIndex - 1].Path : latestBatch.Length > 0 ? latestBatch : Path.Combine(store.Root, "training-batches"); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true }); }
        catch (Exception error) { notice.Text = "文件夹未打开：" + error.Message; }
    }
    void AddBatchButton(string text, int x, int width, Action click) { var button = Button(text, x, 0, width, 52, click); batchBar.Controls.Add(button); }
    void SetBatchMode(bool value) { if (!Resolve()) return; batchMode = value; marked.Clear(); SetPage(0); UpdateMarked(); }
    void ToggleMarked(HistoryEntry entry) { if (!marked.Remove(entry.FileName)) marked.Add(entry.FileName); UpdateMarked(rows.Items.IndexOf(entry)); }
    void UpdateMarked(int changedIndex = -1) { markedLabel.Text = batchMode ? "已勾选 " + marked.Count + " 条" : ""; foreach (Control button in batchBar.Controls) if (button.Left >= 264) button.Enabled = marked.Count > 0; if (changedIndex >= 0) rows.Invalidate(rows.GetItemRectangle(changedIndex)); else rows.Invalidate(); }
    void Remember(List<Change> changes) { if (changes.Count == 0) return; undo.Add(changes); if (undo.Count > 20) undo.RemoveAt(0); undoButton.Enabled = undoButton.Visible = true; }
    void ChangeEntries(string action, bool many) {
        if (!Resolve()) return;
        var targets = many ? entries.FindAll(item => marked.Contains(item.FileName)) : current == null ? new List<HistoryEntry>() : new List<HistoryEntry> { current };
        if (targets.Count == 0) { notice.Text = "请先勾选要处理的录音。"; return; }
        var changes = new List<Change>();
        try {
            foreach (var item in targets) {
                if (File.ReadAllText(item.FileName) != item.Original) throw new IOException("记录已被外部修改，请刷新后重试");
                if (action == "select" && (!item.Flag("reviewed") || item.Flag("ignored") || string.IsNullOrWhiteSpace(item.Text("correctText")))) throw new IOException("包含未核对或已忽略的录音，请先试听并保存；本次未处理");
            }
            foreach (var item in targets) { string before = item.Original; store.ChangeState(item, action); changes.Add(new Change { File = item.FileName, Before = before, After = item.Original }); }
            notice.Text = "已处理 " + changes.Count + " 条，可点「撤销上次」恢复。";
        } catch (Exception error) { notice.Text = "已处理 " + changes.Count + " 条，其余未改：" + error.Message + (changes.Count > 0 ? "；已处理部分可撤销。" : ""); }
        finally { Remember(changes); marked.Clear(); UpdateMarked(); UpdateSummary(); Filter(); }
    }
    void Undo() {
        if (!Resolve() || undo.Count == 0) return; var changes = undo[undo.Count - 1];
        try {
            foreach (var change in changes) if (!File.Exists(change.File) || File.ReadAllText(change.File) != change.After) throw new IOException("记录已被外部修改或删除，未覆盖；请刷新检查");
            for (int i = changes.Count - 1; i >= 0; i--) {
                var change = changes[i]; HistoryStore.Atomic(change.File, change.Before);
                var item = entries.Find(entry => entry.FileName == change.File); if (item != null) { item.Original = change.Before; item.Data = HistoryStore.Parse(change.Before); } changes.RemoveAt(i);
            }
            undo.RemoveAt(undo.Count - 1); notice.Text = "已撤销上次保存或状态操作；原音和已准备的批次保持原样。";
        } catch (Exception error) { notice.Text = "撤销未完成：" + error.Message; }
        finally { undoButton.Enabled = undoButton.Visible = undo.Count > 0; UpdateSummary(); Filter(); }
    }
    void ShowOriginal(bool visible) {
        originalLabel.Visible = stage.Visible = original.Parent.Visible = visible; correctLabel.Top = visible ? 184 : 56;
        var field = correct.Parent; field.Bounds = visible ? new Rectangle(10, 216, 554, 100) : new Rectangle(10, 88, 554, 228);
        correct.Bounds = new Rectangle(12, 10, field.Width - 40, field.Height - 20);
        foreach (Control control in field.Controls) if (control is DictionaryScrollBar) control.Height = field.Height - 20;
    }
    bool NextRecord() {
        Stop(); if (rows.SelectedIndex + 1 < rows.Items.Count) { rows.SelectedIndex++; return true; }
        ShowEntry(current); return false;
    }
    void SaveAndNext() {
        if (current == null) return; bool chosen = selected.Checked; if (!Save()) return; UpdateVisit();
        bool moved = NextRecord(); Filter(); if (!moved) SetPage(0); RecordVisit(); notice.Text = chosen ? "已保存，并留作训练。" : "已保存。";
    }
    internal Label Notice { get { return notice; } }
    internal bool Loading { get { return loading || exporting; } }
    Label Label(string text, int x, int y, int width, int height, float size) { return new DictionaryLabel { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font(Font.FontFamily, size), ForeColor = Dim }; }
    Button Button(string text, int x, int y, int width, int height, Action click) { var button = new DictionaryButton { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font(Font.FontFamily, 11f), BackColor = Color.White, ForeColor = Ink, Outlined = true }; button.Click += (o, e) => click(); return button; }
    void AddText(TextBox box, Rectangle bounds, bool readOnly, string name) {
        var field = new DictionaryField { Bounds = bounds }; box.Bounds = new Rectangle(12, 10, bounds.Width - 40, bounds.Height - 20);
        box.Multiline = true; box.ReadOnly = readOnly; box.MaxLength = 10000; box.BorderStyle = BorderStyle.None; box.Font = new Font(Font.FontFamily, 15f); box.BackColor = Field; box.ForeColor = Ink; box.AccessibleName = name;
        box.ScrollBars = ScrollBars.None; field.Controls.Add(box);
        var scroll = new DictionaryScrollBar(() => (int)DictionaryScrollBar.SendMessage(box.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero),
            () => Math.Max(1, box.Height / box.Font.Height), () => (int)DictionaryScrollBar.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero),
            value => { int line = (int)DictionaryScrollBar.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero); DictionaryScrollBar.SendMessage(box.Handle, 0x00B6, IntPtr.Zero, new IntPtr(value - line)); });
        scroll.Bounds = new Rectangle(bounds.Width - 20, 10, 12, bounds.Height - 20); scroll.BackColor = Field;
        ((DictionaryTextBox)box).ViewChanged = () => { scroll.TabStop = scroll.Overflows; scroll.Invalidate(); }; field.Controls.Add(scroll); Controls.Add(field);
    }
    internal async Task Reload() {
        if (loading || IsDisposed) return; loading = true; Enabled = false; if (NavigationChanged != null) NavigationChanged();
        try {
            string errors = ""; long bytes = 0; var loaded = await Task.Run(() => store.Load(out errors, out bytes)); if (IsDisposed) return;
            entries.Clear(); entries.AddRange(loaded); silent = true;
            try { enabled.Checked = store.Enabled(); } finally { silent = false; }
            usageBytes = bytes; UpdateSummary();
            notice.Text = errors.Length > 0 ? "部分记录读取失败（原文件保留）：" + errors : RuntimeNotice.Length > 0 ? RuntimeNotice : "";
            string statusFile = Path.Combine(store.Root, "history-status.json");
            if (errors.Length == 0 && File.Exists(statusFile)) { var status = HistoryStore.Parse(File.ReadAllText(statusFile)); object value; if (status.TryGetValue("notice", out value)) notice.Text = "上次留档提示：" + Convert.ToString(value); }
            notice.AccessibleDescription = notice.Text; Filter(); await reviewPage.Reload();
        } catch (Exception error) { notice.Text = "读取失败：" + error.Message; }
        finally { loading = false; if (!IsDisposed) { Enabled = true; if (visits.Count == 0) RecordVisit(); else if (NavigationChanged != null) NavigationChanged(); } }
    }
    void UpdateSummary() {
        int chosen = entries.FindAll(item => item.Flag("selectedForTraining")).Count;
        summary.Text = entries.Count + " 条录音";
        export.Enabled = chosen > 0; UpdateMaterials();
    }
    void Filter() {
        if (filterButton != null) filterButton.Text = filter.SelectedIndex == 0 ? "筛选" : filter.Text;
        var visible = entries.FindAll(item => {
            bool included = filter.SelectedIndex == 1 ? !item.Flag("reviewed") && !item.Flag("ignored") : filter.SelectedIndex == 2 ? item.Flag("reviewed") && !item.Flag("ignored") : filter.SelectedIndex == 3 ? item.Flag("ignored") : true;
            return included && (item.Text("correctText") + " " + item.Text("text") + " " + item.Text("rawText") + " " + item.Text("word") + " " + item.Text("createdUtc")).IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        });
        marked.Clear(); UpdateMarked(); string id = current == null ? "" : current.Text("id"); silent = true; rows.BeginUpdate(); rows.Items.Clear();
        foreach (var item in visible) rows.Items.Add(item); int index = visible.FindIndex(item => item.Text("id") == id);
        rows.SelectedIndex = index >= 0 ? index : rows.Items.Count > 0 ? 0 : -1; rows.EndUpdate(); silent = false;
        empty.Visible = rows.Items.Count == 0; empty.Text = entries.Count == 0 ? "还没有录音。\n开启左侧开关后用 Win+H 听写，或点「补录一句」。" : "这里没有录音。\n可在上方切换「全部录音」或其他筛选。"; ShowEntry(rows.SelectedItem as HistoryEntry); if (openRecording != null) openRecording.Enabled = rows.Items.Count > 0; if (current == null && page == 1) SetPage(0);
    }
    void DrawRow(object sender, DrawItemEventArgs e) {
        if (e.Index < 0) return; var list = (DictionaryListBox)sender; var item = (HistoryEntry)list.Items[e.Index]; e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        bool confirm = list == materialRows && !ArchiveTab, selection = (batchMode && list == rows) || confirm;
        bool markedRow = selection && (confirm ? archiveSelected.Contains(item.FileName) : marked.Contains(item.FileName));
        HistoryCard.Draw(e.Graphics, e.Bounds, e.Index + 1, Font, (e.State & DrawItemState.Selected) != 0, markedRow, list.HoverIndex == e.Index, list.PressedIndex == e.Index, list.Focused && list.SelectedIndex == e.Index);
        DateTime time; string stamp = DateTime.TryParse(item.Text("createdUtc"), out time) ? time.ToLocalTime().ToString("MM-dd HH:mm") : "";
        int inset = selection ? 42 : 12;
        if (selection) HistoryCard.Check(e.Graphics, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 29, 18, 18), markedRow, list.HoverIndex == e.Index, list.PressedIndex == e.Index, list.Focused && list.SelectedIndex == e.Index, true);
        using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }) {
            using (var brush = new SolidBrush(Dim)) using (var font = new Font(Font.FontFamily, 9.5f)) e.Graphics.DrawString(stamp + " · " + item.Seconds.ToString("0.0") + " 秒", font, brush, new Rectangle(e.Bounds.X + inset, e.Bounds.Y + 5, e.Bounds.Width - inset - 12, 22), format);
            using (var brush = new SolidBrush(Ink)) e.Graphics.DrawString(item.Text("correctText").Replace("\r", " ").Replace("\n", " "), Font, brush, new Rectangle(e.Bounds.X + inset, e.Bounds.Y + 26, e.Bounds.Width - inset - 12, 24), format);
            using (var font = new Font(Font.FontFamily, 9.5f)) {
                int tagWidth = HistoryStatusTag.Draw(e.Graphics, item.State, font, new Point(e.Bounds.X + inset, e.Bounds.Y + 48)); if (item.TrainingStatus.Length > 0) tagWidth += 8 + HistoryStatusTag.Draw(e.Graphics, item.TrainingStatus, font, new Point(e.Bounds.X + inset + tagWidth + 8, e.Bounds.Y + 48));
                using (var brush = new SolidBrush(Dim)) e.Graphics.DrawString(item.Kind == "voice-samples" ? "个人补录" : "日常听写", font, brush, new Rectangle(e.Bounds.X + inset + tagWidth + 10, e.Bounds.Y + 48, e.Bounds.Width - inset - tagWidth - 22, 22), format);
            }
        }
    }
    void ShowEntry(HistoryEntry entry) {
        Stop(); current = entry; silent = true;
        correct.MaxLength = entry != null && entry.Kind == "voice-samples" ? 2000 : 10000;
        correct.Text = entry == null ? "" : entry.Text("correctText"); selected.Checked = entry != null && entry.Flag("selectedForTraining"); silent = false; dirty = false;
        correct.Enabled = selected.Enabled = play.Enabled = save.Enabled = deleteMenu.Enabled = entry != null;
        editorTag.Text = entry == null ? "" : entry.State; editorTag.Visible = page == 1 && entry != null;
        editorSummary.SetBounds(editorTag.Right + 12, 48, 868 - editorTag.Right - 12, 26); editorSummary.Text = entry == null ? "" : entry.Seconds.ToString("0.0") + " 秒";
        OriginalText();
    }
    void OriginalText() {
        if (current == null) { original.Clear(); return; }
        string key = stage.SelectedIndex == 1 ? "firstText" : stage.SelectedIndex == 2 ? "rawText" : "text";
        original.Text = current.Text(key); if (original.Text.Length == 0) original.Text = current.Kind == "voice-samples" ? "主动补录没有原始识别结果。" : "本片段没有该阶段的记录。";
    }
    internal bool Save() {
        if (current == null) return true;
        try { string before = current.Original; store.Save(current, correct.Text, selected.Checked, false); Remember(new List<Change> { new Change { File = current.FileName, Before = before, After = current.Original } }); dirty = false; UpdateSummary(); notice.Text = (selected.Checked ? "已保存，已留作训练，尚未训练。" : "已保存，未选入训练。") + "在录音列表的「已保存」中可查看。"; rows.Invalidate(); return true; }
        catch (Exception error) { notice.Text = "保存失败：" + error.Message; return false; }
    }
    internal bool Resolve(DialogResult? answerForTest = null) {
        if (exporting) { notice.Text = "正在归档批次，请等待复制完成。"; return false; }
        if (capture != null && !capture.IsDisposed && capture.Visible) { capture.Close(); if (capture.Visible) return false; }
        if (reviewPage != null && !reviewPage.ResolveCapture()) return false;
        if (!dirty) return true;
        var answer = answerForTest ?? MessageBox.Show(FindForm(), "这条记录的文字或训练选择尚未保存，保存吗？", "听写记录", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false; if (answer == DialogResult.Yes) return Save(); ShowEntry(current); return true;
    }
    void Play() {
        if (playingUntil > 0) { Stop(); return; } if (current == null) return;
        try { HistoryStore.ValidateAudio(current); Stop(); player.Stream = new MemoryStream(File.ReadAllBytes(current.Audio)); player.Load(); player.Play(); clock.Restart(); playingUntil = current.Seconds; playback.Start(); ((DictionaryButton)play).Glyph = "stop"; play.Text = "停止"; }
        catch (Exception error) { Stop(); notice.Text = "试听失败：" + error.Message; }
    }
    internal void Stop() { playback.Stop(); player.Stop(); if (player.Stream != null) { player.Stream.Dispose(); player.Stream = null; } playingUntil = 0; if (play != null) { ((DictionaryButton)play).Glyph = "play"; play.Text = "播放"; play.Invalidate(); } }
    async Task DeleteMarked(bool confirmedForTest = false) {
        if (!batchMode || !Resolve()) return;
        var targets = new List<HistoryEntry>(); foreach (HistoryEntry item in rows.Items) if (marked.Contains(item.FileName)) targets.Add(item);
        if (targets.Count == 0) { notice.Text = "请先勾选要删除的录音。"; return; }
        try { foreach (var item in targets) store.ValidatePermanentRemoval(item); }
        catch (Exception error) { notice.Text = "未删除：" + error.Message; return; }
        if (!confirmedForTest) using (var confirmation = new DictionaryDeleteDialog("", true, targets.Count)) if (confirmation.ShowDialog(FindForm()) != DialogResult.OK) return;
        Stop(); int removed = 0; string failure = "";
        try { foreach (var item in targets) { store.PermanentlyRemove(item); removed++; } }
        catch (Exception error) { failure = error.Message; }
        marked.Clear(); undo.RemoveAll(group => group.Exists(change => targets.Exists(item => item.FileName == change.File))); UpdateMarked(); await Reload(); if (IsDisposed) return;
        notice.Text = failure.Length == 0 ? "已永久删除 " + removed + " 条原音和标注；归档批次、验证副本保留。" : "完整删除 " + removed + " 条；下一条可能部分清理失败，其余未处理：" + failure;
        notice.AccessibleDescription = notice.Text;
    }
    async void Remove() {
        if (current == null || !Resolve()) return; using (var confirmation = new DictionaryDeleteDialog("", true)) if (confirmation.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { Stop(); store.Remove(current); await Reload(); notice.Text = "已移出列表 · 原音及 .json.deleted 索引保留，可恢复；仍占用磁盘"; } catch (Exception error) { notice.Text = "删除失败：" + error.Message; }
    }
    async Task ToggleArchive() {
        if (!Resolve() || !ArchivedView) return;
        var batch = batches[batchChoice.SelectedIndex - 1]; bool withdraw = !batch.Withdrawn;
        try {
            store.SetArchiveState(batch.Path, batch.Lifecycle, withdraw); if (withdraw) batchName.Text = batch.Name; UpdateMaterials();
            foreach (var entry in entries) if (batch.Entries.Exists(old => old.Kind == entry.Kind && old.Text("id") == entry.Text("id"))) {
                if (withdraw && entry.Flag("selectedForTraining") && !entry.Flag("ignored") && !batches.Exists(other => !other.Withdrawn && other.Entries.Exists(old => old.Kind == entry.Kind && old.Text("id") == entry.Text("id")))) archiveSelected.Add(entry.FileName);
                else if (!withdraw) archiveSelected.Remove(entry.FileName);
            }
            await reviewPage.Reload(); if (IsDisposed) return;
            notice.Text = withdraw ? "已撤回归档；快照保留，现存录音可回待归档重新挑选。" : "已恢复归档；原音和文字快照未改变。";
        } catch (Exception error) { if (!IsDisposed) notice.Text = "状态未更新：" + error.Message; }
    }
    async Task Export() {
        if (!Resolve() || ArchiveTab) return;
        var chosen = entries.FindAll(item => item.Flag("selectedForTraining") && !item.Flag("ignored") && archiveSelected.Contains(item.FileName)); double seconds = 0; foreach (var item in chosen) seconds += item.Seconds;
        if (chosen.Count == 0) { notice.Text = "请先核对并选择本批次的录音。"; return; }
        if (MessageBox.Show(FindForm(), "批次：" + batchName.Text + "\n将复制 " + chosen.Count + " 条录音，约 " + (seconds / 60).ToString("0.0") + " 分钟，及当前正确文字。\n\n这一步只准备独立批次，不会训练或启用模型。后续见「交接说明」。", "归档批次", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        string name = batchName.Text; exporting = true; Enabled = false; export.Enabled = false; if (NavigationChanged != null) NavigationChanged();
        try { var confirmed = chosen.ConvertAll(item => new HistoryEntry { FileName = item.FileName, Original = item.Original }); latestBatch = await Task.Run(() => store.Export(confirmed, name)); if (IsDisposed) return; foreach (var item in chosen) archiveSelected.Remove(item.FileName); UpdateMaterials(); int index = batches.FindIndex(item => item.Path == latestBatch); if (index >= 0) batchChoice.SelectedIndex = index + 1; batchName.Text = "训练 " + DateTime.Now.ToString("MM-dd HH:mm"); if (IsDisposed) return; notice.Text = "批次已归档，交给agent训练；训练完成后到「验证」录新音复测。"; notice.AccessibleDescription = notice.Text; }
        catch (Exception error) { if (!IsDisposed) notice.Text = "批次未完成：" + error.Message; }
        finally { exporting = false; if (!IsDisposed) { Enabled = true; BatchSummary(); if (NavigationChanged != null) NavigationChanged(); } }
    }
    internal static void CheckForTest(string work) {
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception("HISTORY-TEST: " + message); };
        string root = Path.Combine(work, "history-check-" + Guid.NewGuid().ToString("N"));
        var store = new HistoryStore(root); check(!store.Enabled(), "recording defaults off");
        store.SetEnabled(true); check(store.Enabled(), "explicit recording opt in roundtrips"); store.SetEnabled(false);
        byte[] pcm = new byte[32000]; for (int i = 0; i < pcm.Length; i += 2) { short value = (short)(Math.Sin(i * .1) * 1500); pcm[i] = (byte)value; pcm[i + 1] = (byte)(value >> 8); }
        var manual = new VoiceSampleStore(root).Save("Codex", "今天不用 Codex，先跑测试。", pcm);
        string folder = Path.Combine(store.Root, "dictation-history"); Directory.CreateDirectory(folder);
        string id = Guid.NewGuid().ToString("N"); File.WriteAllBytes(Path.Combine(folder, id + ".wav"), VoiceSampleStore.Wave(pcm));
        var data = new Dictionary<string, object> { { "id", id }, { "wav", id + ".wav" }, { "createdUtc", DateTime.UtcNow.ToString("o") }, { "state", "collected" }, { "durationSeconds", 1.0 }, { "sampleRate", 16000 }, { "channels", 1 }, { "bitsPerSample", 16 }, { "firstText", "今天不用扣desks" }, { "rawText", "今天不用扣desks，先跑测试。" }, { "text", "今天不用扣desks，先跑测试。" }, { "correctText", "今天不用扣desks，先跑测试。" }, { "roundId", "fixture-round" } };
        string file = Path.Combine(folder, id + ".json"); File.WriteAllText(file, new JavaScriptSerializer().Serialize(data));
        string errors; long bytes; var entries = store.Load(out errors, out bytes); check(errors == "" && entries.Count == 2, "manual and daily recordings share review list");
        var daily = entries.Find(item => item.Kind == "dictation-history"); HistoryStore.ValidateAudio(daily);
        bool incompatible = false; try { store.Save(entries.Find(item => item.Kind == "voice-samples"), new string('x', 2001), true, false); } catch (IOException) { incompatible = true; }
        check(incompatible && new VoiceSampleStore(root).Load("Codex").Count == 1, "history editing preserves manual sample label contract");
        store.Save(daily, "今天不用 Codex，先跑测试。", false, false);
        check(!daily.Flag("selectedForTraining") && daily.Flag("reviewed") && daily.Text("rawText").Contains("扣desks"), "editing never auto-selects or overwrites original");
        bool failed = false; try { store.Export(); } catch (IOException) { failed = true; } check(failed, "empty selection is not exported");
        var stale = store.Load(out errors, out bytes).Find(item => item.Kind == "dictation-history");
        store.Save(daily, daily.Text("correctText"), true, false);
        failed = false; try { store.Save(stale, "must not overwrite", false, false); } catch (IOException) { failed = true; } check(failed, "concurrent edits cannot overwrite");
        string batch = store.Export(name: "个人训练测试"); string manifest = File.ReadAllText(Path.Combine(batch, "manifest.json"));
        var samples = (object[])HistoryStore.Parse(manifest)["samples"];
        check(Convert.ToString(HistoryStore.Parse(manifest)["name"]) == "个人训练测试", "custom batch name persists in immutable manifest");
        bool invalidName = false; try { store.Export(name: "bad\nname"); } catch (IOException) { invalidName = true; } check(invalidName, "invalid names are rejected before export");
        check(samples.Length == 1 && manifest.Contains("prepared_not_trained") && manifest.Contains("fixture-round") && !manifest.Contains(manual.id) && File.ReadAllText(Path.Combine(batch, "下一步.txt")).Contains("尚未训练"), "batch exports only explicit selections with source groups");
        var flat = HistoryStore.Parse(File.ReadAllLines(Path.Combine(batch, "samples.jsonl"))[0]);
        check(Convert.ToString(flat["correctText"]) == daily.Text("correctText") && Convert.ToString(flat["recognizedText"]).Contains("扣desks") && Convert.ToString(flat["rawRecognition"]).Contains("扣desks") && Convert.ToString(flat["sourceGroup"]) == "fixture-round", "agent JSONL keeps original recognition correct text and source grouping distinct");
        bool staleArchive = false; try { store.Export(new List<HistoryEntry> { new HistoryEntry { FileName = daily.FileName, Original = "stale" } }); } catch (IOException) { staleArchive = true; } check(staleArchive, "archive cannot snapshot text changed after second confirmation");

        store.Save(daily, "后续修订不改变原批次。", false, false); check(File.ReadAllText(Path.Combine(batch, "manifest.json")) == manifest, "batch label snapshot is immutable");
        store.Save(daily, "今天不用 Codex，先跑测试。", true, false);
        using (var dialog = new DictionaryDialog(root)) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000); dialog.Show(); Application.DoEvents();
            var backArrow = (DictionaryButton)typeof(DictionaryDialog).GetField("navBack", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            var forwardArrow = (DictionaryButton)typeof(DictionaryDialog).GetField("navForward", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
            check(!backArrow.Visible && !forwardArrow.Visible, "word workspace hides history-only arrows");
            var history = dialog.HistoryForTest; dialog.SelectHistoryForTest(); var wait = Stopwatch.StartNew();
            while (history.Loading && wait.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); }
            check(!history.Loading && history.rows.Items.Count == 2 && history.materialRows.Items.Count == 1, "history loads, counts and selection");
            check(history.libraryPage.Visible && !history.editorPage.Visible && !history.materialsPage.Visible && history.searchField.Visible && history.filter.Items.Count == 4 && !history.filter.Visible && !history.markedLabel.Visible && !history.save.Visible && !history.export.Visible, "library is a separate default page with no editor or training controls");
            check(history.save.Text == "保存" && !history.selected.Visible && history.moreMenu.Items.Count > 0, "simple save with optional material selection in context menu");
            check(!history.CanGoBack && !history.CanGoForward && backArrow.Visible && forwardArrow.Visible && !backArrow.Enabled && !forwardArrow.Enabled, "history workspace shows disabled arrows at initial boundary");
            string firstVisitFile = history.current.FileName;
            history.Navigate(1); history.Navigate(2); history.GoVisit(-1);
            check(history.page == 1 && history.current.FileName == firstVisitFile && history.CanGoForward, "back restores the exact recording");
            history.GoVisit(-1); check(history.page == 0 && !history.CanGoBack, "back returns to initial list and stops at boundary: page=" + history.page + " index=" + history.visitIndex + " count=" + history.visits.Count);
            history.GoVisit(1); check(history.page == 1, "forward returns to editor");
            history.Navigate(0); check(!history.CanGoForward, "new visit after back clears forward branch");
            history.search.Text = "Codex"; history.filter.SelectedIndex = 2; history.Navigate(1); history.compare.Checked = true; history.stage.SelectedIndex = 1; history.Navigate(2); history.GoVisit(-1);
            check(history.page == 1 && history.compare.Checked && history.stage.SelectedIndex == 1 && history.filter.SelectedIndex == 2 && history.search.Text == "Codex", "navigation restores query scope and original comparison");
            history.GoVisit(-1); check(history.page == 0 && history.filterButton.Text == "已保存" && history.clearSearch.Visible && history.search.Width == 248, "restored scope and search clear remain correct after hidden-page updates");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-search.png")); }
            history.search.Clear(); history.filter.SelectedIndex = 0; history.compare.Checked = false; history.stage.SelectedIndex = 0;
            int navigationFixtureCount = history.entries.Count; for (int i = 0; i < 12; i++) history.entries.Add(new HistoryEntry { FileName = "scroll-baseline-" + i, Kind = history.entries[0].Kind, Data = HistoryStore.Parse(history.entries[0].Original), Original = history.entries[0].Original }); history.Filter(); history.rows.TopIndex = 8;
            history.Navigate(1); history.Navigate(2); history.GoVisit(-1); history.GoVisit(-1);
            check(history.page == 0 && history.rows.TopIndex == 8, "back restores the actual scrolled list position");
            history.entries.RemoveRange(navigationFixtureCount, 12); history.Filter();
            navigationFixtureCount = history.entries.Count;
            for (int i = 0; i < 12; i++) history.entries.Add(new HistoryEntry { FileName = "scroll-fixture-" + i, Kind = history.entries[0].Kind, Data = HistoryStore.Parse(history.entries[0].Original), Original = history.entries[0].Original });
            history.Filter(); history.rows.TopIndex = 8; string anchorFile = TopFileOf(history.rows); history.Navigate(1); history.Navigate(2);
            history.entries.Insert(0, new HistoryEntry { FileName = "new-scroll-fixture", Kind = history.entries[0].Kind, Data = HistoryStore.Parse(history.entries[0].Original), Original = history.entries[0].Original }); history.Filter(); history.GoVisit(-1); history.GoVisit(-1);
            check(TopFileOf(history.rows) == anchorFile && history.rows.TopIndex == 9, "back anchors to the same recording after insertion");
            history.Navigate(1); history.Navigate(2); history.entries.RemoveAt(0); history.Filter(); history.GoVisit(-1); history.GoVisit(-1);
            check(TopFileOf(history.rows) == anchorFile && history.rows.TopIndex == 8, "back anchors to the same recording after removal");
            history.entries.RemoveRange(navigationFixtureCount, 12); history.Filter();



            history.search.Text = "no-match"; check(history.rows.Items.Count == 0 && history.searchField.Visible, "direct search works");
            history.clearSearch.PerformClick(); check(history.search.TextLength == 0 && !history.clearSearch.Visible && history.rows.Items.Count == 2 && history.rows.Top == 56, "clear button restores all recordings and hides itself");
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 50, 10, 0) });
            check(history.page == 0, "right click on recording must not enter editor");
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 50, 74, 0) });
            check(history.page == 0, "empty card gap does not activate a recording");
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 30, 0) });
            check(history.page == 0 && history.openRecording.Visible, "single click only selects and exposes explicit open action");
            typeof(Control).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 250, 30, 0) });
            check(history.page == 1, "double click explicitly opens recording");
            history.Navigate(1); check(history.editorPage.Visible && !history.libraryPage.Visible && !history.materialsPage.Visible && history.save.Visible && !history.export.Visible, "editor page isolates playback correction and save");
            int visibleButtons = 0; foreach (Control control in history.editorPage.Controls) if (control is Button && control.Visible) visibleButtons++;
            check(visibleButtons == 2 && history.selected.Visible && !history.compare.Visible, "editor exposes direct training selection and footer save");
            var materialAction = (ToolStripMenuItem)history.moreMenu.Items[1]; materialAction.Checked = history.selected.Checked;
            bool wasSelected = history.selected.Checked; materialAction.PerformClick(); check(history.selected.Checked != wasSelected && history.dirty, "context material choice remains explicit and awaits save"); materialAction.PerformClick();
            history.correct.Text = "今天不用 Codex，先跑测试。"; history.selected.Checked = false; check(history.Save() && !history.current.Flag("selectedForTraining"), "UI saves explicit deselection");
            history.filter.SelectedIndex = 2; check(history.rows.Items.Count == 1 && history.current.State == "已保存", "saved recordings remain visible without training selection; rows=" + history.rows.Items.Count + "; state=" + (history.current == null ? "null" : history.current.State) + "; filter=" + history.filter.SelectedIndex);
            history.filter.SelectedIndex = 0;
            history.selected.Checked = true; check(history.Save(), "UI selects without modifying original");
            history.stage.SelectedIndex = 1; check(history.original.Text == "今天不用扣desks", "first draft preserved"); history.stage.SelectedIndex = 0;
            history.filter.SelectedIndex = 2; check(history.rows.Items.Count == 1 && history.current.Flag("reviewed"), "saved filter uses real metadata");
            history.filter.SelectedIndex = 2; check(history.rows.Items.Count == 1 && history.current.Flag("reviewed") && history.entries.FindAll(item => item.Flag("reviewed") && !item.Flag("ignored")).Count == 1, "visible saved filter finds corrected recordings after reload");
            history.filter.SelectedIndex = 0; check(history.rows.Items.Count == 2, "all recordings remain accessible");
            history.ShowOriginal(true); check(history.original.Parent.Visible && history.correct.Text.Contains("Codex"), "optional original comparison preserves correction");
            check(history.correctLabel.Visible && history.correctLabel.Text.Contains("可编辑") && history.correctLabel.Bottom <= history.correct.Parent.Top && history.originalLabel.Text.Contains("只读"), "comparison gives both text fields explicit visible labels");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-comparison.png")); }
            history.ShowOriginal(false); check(history.correctLabel.Visible && history.correctLabel.Bottom <= history.correct.Parent.Top, "editable label also remains visible without comparison");
            history.filter.SelectedIndex = 0; history.rows.SelectedIndex = 0;
            history.Navigate(2); history.pendingBatch.PerformClick(); history.materialAll.PerformClick();
            history.exporting = true; history.export.Enabled = false; history.BatchSummary();
            check(!history.export.Enabled && !history.Resolve() && history.Export().IsCompleted, "in-flight archive cannot be re-enabled or started twice");
            history.Navigate(0); check(history.page == 2, "navigation waits until archive copying finishes");
            history.exporting = false; history.BatchSummary(); history.Navigate(1);
            string savedCorrection = history.correct.Text, savedRecord = history.current.Original; bool savedChoice = history.selected.Checked;
            history.correct.Text = "应当放弃的文字"; history.selected.Checked = !savedChoice;
            check(!history.Resolve(DialogResult.Cancel) && history.dirty && history.correct.Text == "应当放弃的文字", "cancel retains unsaved text and selection");
            check(history.Resolve(DialogResult.No) && !history.dirty && history.correct.Text == savedCorrection && history.selected.Checked == savedChoice && File.ReadAllText(history.current.FileName) == savedRecord, "discard restores saved text and training choice without writing");
            string beforeCorrection = history.current.Original; history.correct.Clear(); history.SaveAndNext();
            check(history.rows.SelectedIndex == 0 && history.current.Original == beforeCorrection && history.correct.Text == "", "failed save does not advance or lose the edit");
            history.correct.Text = "今天不用 Codex，先跑测试。"; history.selected.Checked = false; history.SaveAndNext();
            check(history.rows.SelectedIndex == 1 && history.notice.Text.StartsWith("已保存") && !history.selected.Checked && !store.Load(out errors, out bytes).Find(item => item.Kind == "dictation-history").Flag("selectedForTraining"), "save and next persists explicit choice without selecting the next clip");
            history.rows.SelectedIndex = 0; string beforeSkip = history.current.Original; history.SkipRecord();
            check(history.rows.SelectedIndex == 1 && store.Load(out errors, out bytes).Find(item => item.Kind == "dictation-history").Original == beforeSkip, "skip advances without changing recording, label or training choice");
            check(!history.NextRecord() && history.rows.SelectedIndex == 1, "last recording does not wrap or silently change selection");
            history.SaveAndNext(); check(history.page == 0 && history.libraryPage.Visible, "saving final recording returns directly to list"); history.Undo(); history.Navigate(1);
            history.rows.SelectedIndex = 0; history.selected.Checked = true; check(history.Save(), "selection remains explicit after guided review");
            history.Undo(); check(!history.current.Flag("selectedForTraining"), "undo restores previous training choice after save and next");
            history.ChangeEntries("pending", false); check(!history.current.Flag("reviewed") && !history.current.Flag("selectedForTraining") && history.current.Text("correctText").Contains("Codex"), "reopen keeps corrected label and original audio");
            history.Undo(); check(history.current.Flag("reviewed"), "undo reopens saved state");
            history.SetBatchMode(true); foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName); history.UpdateMarked();
            history.ChangeEntries("select", true); check(history.entries.FindAll(item => item.Flag("selectedForTraining")).Count == 0 && history.notice.Text.Contains("未核对"), "bulk selection rejects unreviewed records without partial changes");
            foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName);
            history.ChangeEntries("ignore", true); check(history.entries.TrueForAll(item => item.Flag("ignored")), "bulk ignore preserves all recordings");
            history.Undo(); check(history.entries.TrueForAll(item => !item.Flag("ignored")), "one undo restores entire batch");
            foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName);
            history.ChangeEntries("pending", true); check(history.entries.TrueForAll(item => !item.Flag("reviewed") && !item.Flag("selectedForTraining")), "bulk reopen clears training choices"); history.Undo();
            history.rows.SelectedIndex = 1; history.correct.Text = "今天不用 Codex，先跑测试。"; check(history.Save(), "second recording explicitly reviewed");
            foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName);
            history.ChangeEntries("select", true); check(history.entries.TrueForAll(item => item.Flag("selectedForTraining")), "bulk select reviewed records");
            foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName);
            history.ChangeEntries("unselect", true); check(history.entries.TrueForAll(item => !item.Flag("selectedForTraining") && item.Flag("reviewed")), "bulk deselect retains reviewed text"); history.Undo();
            history.SetBatchMode(false);
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history.png")); }
            history.Navigate(1);
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-editor.png")); }
            history.Navigate(2); check(history.materialsPage.Visible && !history.editorPage.Visible && !history.libraryPage.Visible && history.materialRows.Items.Count == 2 && history.export.Visible && !history.save.Visible, "materials page previews candidates with isolated batch preparation");
            check(history.trainingInfo.Visible && history.trainingHelp.GetToolTip(history.trainingInfo) == TrainingExplanation && history.materialRows.Height == 256 && history.materialsPage.Top + history.materialRows.Bottom <= history.notice.Top, "training help is beside title and freed space belongs to list");
            history.archivedBatch.PerformClick(); check(history.ArchiveTab && history.batchPicker.Visible && !history.batchNameField.Visible, "archived tab separates read-only batch browsing");
            string archivePath = history.batches[history.batchChoice.SelectedIndex - 1].Path, manifestBefore = File.ReadAllText(Path.Combine(archivePath, "manifest.json"));
            string lifecycleBefore = history.batches[history.batchChoice.SelectedIndex - 1].Lifecycle;
            Action<Task> finishArchive = task => { var timer = Stopwatch.StartNew(); while (!task.IsCompleted && timer.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); } check(task.IsCompleted && !task.IsFaulted, "archive status task completes"); };
            finishArchive(history.ToggleArchive()); check(history.batches[history.batchChoice.SelectedIndex - 1].Withdrawn && history.export.Text == "恢复归档", "archived batch can be withdrawn and exposes restore");
            check(history.archiveSelected.Count > 0 && File.ReadAllText(Path.Combine(archivePath, "manifest.json")) == manifestBefore, "withdrawal returns existing selected recordings without rewriting snapshot");
            bool lifecycleConflict = false; try { store.SetArchiveState(archivePath, lifecycleBefore, false); } catch (IOException) { lifecycleConflict = true; } check(lifecycleConflict, "stale archive status cannot overwrite a newer transition");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-archive-withdrawn.png")); }
            finishArchive(history.ToggleArchive()); check(!history.batches[history.batchChoice.SelectedIndex - 1].Withdrawn && history.export.Text == "撤回归档" && File.ReadAllText(Path.Combine(archivePath, "manifest.json")) == manifestBefore, "restore reuses exactly the original archive snapshot");
            var lifecycle = HistoryStore.Parse(File.ReadAllText(Path.Combine(archivePath, "lifecycle.json"))); check(((object[])lifecycle["history"]).Length == 2, "archive roundtrip preserves transition history");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-archives.png")); }
            history.pendingBatch.PerformClick(); history.batchName.Text = "个人微调 第一次"; history.UpdateMaterials(); check(!history.ArchiveTab && history.batchNameField.Visible && history.batchName.Text == "个人微调 第一次", "new batch has an editable name retained across refresh");
            history.materialAll.PerformClick(); check(history.archiveSelected.Count == history.materialRows.Items.Count, "training has select-all"); history.materialClear.PerformClick(); check(history.archiveSelected.Count == 0, "training has clear-selection");
            history.batchChoice.SelectedIndex = 0; history.archiveSelected.Clear(); history.UpdateMaterials(); check(history.archiveSelected.Count == 0 && !history.export.Enabled, "second confirmation is not silently reset by refresh");
            history.archiveSelected.Add(((HistoryEntry)history.materialRows.Items[0]).FileName); history.BatchSummary(); check(history.export.Enabled, "new archive only enables for explicitly confirmed recordings");
            check(history.batches.Count > 0, "existing batches are visible in selector"); history.batchChoice.SelectedIndex = 1; check(history.export.Enabled && history.export.Text == "撤回归档" && history.ArchivedView && history.materialSummary.Text.Contains("快照保留"), "immutable snapshot has a reversible archive status"); history.batchChoice.SelectedIndex = 0;

            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-materials.png")); }
            history.materialRows.SelectedIndex = 0;
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.materialRows, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 50, 10, 0) });
            check(history.page == 2, "right click on training material does not navigate");
            for (int i = 0; i < 10; i++) history.materialRows.Items.Add(history.entries[0]);
            check(history.materialScroll.Overflows, "long training material list exposes a real scrollbar"); history.materialScroll.CheckForTest();
            int bufferedBefore = history.materialRows.BufferedPaints; int paintNotices = 0; Action originalView = history.materialRows.ViewChanged; history.materialRows.ViewChanged += () => paintNotices++;
            for (int i = 0; i < 10; i++) { history.materialRows.Refresh(); Application.DoEvents(); }
            history.materialRows.ViewChanged = originalView; check(history.materialRows.BufferedItems > 0, "direct native owner-draw also renders a complete row offscreen"); check(history.materialRows.BufferedPaints >= bufferedBefore + 10, "each actual card paint is composed offscreen before presentation"); check(paintNotices <= 1, "stationary repaint does not repeatedly invalidate the scrollbar");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-scroll.png")); }
            history.UpdateMaterials(); history.materialRows.SelectedIndex = 0;
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.materialRows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 50, 10, 0) });
            check(history.page == 2, "click anywhere on a new-batch card toggles confirmation without opening editor");
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.materialRows, new object[] { new KeyEventArgs(Keys.Enter) });
            check(history.page == 1 && history.current != null, "Enter material returns to correction");
            history.Navigate(0); check(history.page == 0 && history.libraryPage.Visible && !history.editorPage.Visible && !history.materialsPage.Visible && !history.filter.Visible, "return restores simple library and hides other page controls");
            history.SetBatchMode(true); history.marked.Clear();
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 30, 0) });
            check(history.marked.Count == 1 && history.page == 0, "batch mode card body toggles selection");
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(history.rows, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 30, 0) });
            check(history.marked.Count == 0, "second card-body click unchecks");
            foreach (HistoryEntry item in history.rows.Items) history.marked.Add(item.FileName); history.UpdateMarked();
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-batch.png")); }
            history.bulkMenu.Fit(); history.bulkMenu.PerformLayout(); foreach (ToolStripItem item in history.bulkMenu.Items) if (!(item is ToolStripSeparator)) check(item.Width >= TextRenderer.MeasureText(item.Text, history.bulkMenu.Font).Width + 38, "action text fits renderer inset without truncation");
            using (var bitmap = new Bitmap(history.bulkMenu.Width, history.bulkMenu.Height)) { history.bulkMenu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-actions.png")); }
            history.SetBatchMode(false);
            history.Navigate(2); history.batchChoice.SelectedIndex = 1; string rememberedBatch = history.batches[0].Path;
            history.Navigate(3); var reviewWait = Stopwatch.StartNew();
            while (history.reviewPage.busy && reviewWait.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); }
            history.reviewPage.CheckForTest(work, history.current.Kind + ":" + history.current.Text("id"), history.batches[0].Path); history.GoVisit(-1); check(history.page == 2 && history.ArchivedView && history.batches[history.batchChoice.SelectedIndex - 1].Path == rememberedBatch, "back navigation remembers the concrete archived batch"); history.Navigate(0);
            string expected = history.current.Original; File.AppendAllText(history.current.FileName, " ");
            history.Undo(); check(history.notice.Text.Contains("外部修改") && File.ReadAllText(history.current.FileName) == expected + " ", "undo never overwrites external edits");
            File.WriteAllText(history.current.FileName, expected); history.Undo(); check(history.entries.TrueForAll(item => !item.Flag("selectedForTraining")), "undo can retry after conflict resolved");
            history.Navigate(1); history.search.Text = "no-match"; check(history.rows.Items.Count == 0 && history.empty.Visible && history.page == 0 && !history.save.Visible, "empty editor returns to list instead of exposing ineffective actions"); history.search.Clear();
            foreach (Control control in history.Controls) check(history.ClientRectangle.Contains(control.Bounds), "history controls stay inside client area: " + control.Text);
            foreach (var panel in new Panel[] { history.libraryPage, history.editorPage, history.materialsPage, history.reviewPage }) foreach (Control control in panel.Controls) check(panel.ClientRectangle.Contains(control.Bounds), "page controls stay inside their page: " + control.Text);
            check(history.libraryPage.Bounds == history.editorPage.Bounds && history.libraryPage.Bounds == history.materialsPage.Bounds && history.libraryPage.Left == 304, "all pages share the dictionary content grid");
            check(history.save.Bounds == history.export.Bounds && history.save.Top == 506 && history.save.Height == 52 && history.save.Width == 124, "primary actions match dictionary footer geometry");
            check(!history.markedLabel.Visible || history.rows.Bottom <= history.markedLabel.Top, "batch count never intercepts clicks in recording rows");
            check(history.correct.Parent.Width == 554 && history.original.Parent.Width == 554 && history.materialRows.Width + 16 == 554, "editor and materials share the same content width");
            check(history.editorPage.Top + history.correct.Parent.Bottom <= history.Notice.Top, "editor never overlaps the status region");
            Func<Color, double> luminance = color => { Func<byte, double> channel = v => v / 255.0 <= .04045 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + .055) / 1.055, 2.4); return .2126 * channel(color.R) + .7152 * channel(color.G) + .0722 * channel(color.B); };
            Func<Color, Color, double> contrast = (a, b) => (Math.Max(luminance(a), luminance(b)) + .05) / (Math.Min(luminance(a), luminance(b)) + .05);
            check(contrast(Ink, Field) >= 4.5 && contrast(Dim, Field) >= 4.5 && contrast(Dim, Face) >= 4.5 && contrast(Dim, Color.FromArgb(232, 231, 228)) >= 4.5, "body and secondary text meet 4.5 contrast benchmark");
            check(contrast(DictionaryField.BorderInk, Field) >= 3 && contrast(DictionaryButton.OutlineInk, Color.White) >= 3 && contrast(DictionaryScrollBar.IdleInk, Field) >= 3, "interactive boundaries and idle scrollbar meet 3 contrast benchmark");
            check(contrast(Dim, HistoryCard.NumberInk) >= 4.5 && contrast(Dim, HistoryCard.SelectedNumberInk) >= 4.5 && contrast(Ink, HistoryCard.SelectedPaper) >= 4.5, "text remains legible over the index and checked card background");
            check(contrast(Color.White, HistoryCard.MarkInk) >= 4.5, "custom checkmark has clear selected contrast");
            check(history.Font.Name == HistoryCard.TextFamily && history.reviewPage.Font.Name == HistoryCard.TextFamily && history.editorTag.Font.Name == HistoryCard.TextFamily && history.batchChoice.Font.Name == HistoryCard.TextFamily && history.stage.Font.Name == HistoryCard.TextFamily, "history body, verification and status labels use the same explicit Chinese UI font");
            check(history.rows.CardFeedback && history.materialRows.CardFeedback && history.rowsScroll.CardTrack && history.materialScroll.CardTrack, "list hover and visible scroll tracks are scoped to history cards");
            double tagMinimum = double.MaxValue;
            using (var palette = new Bitmap(520, 72)) using (var graphics = Graphics.FromImage(palette)) using (var font = new Font("Segoe UI Variable Text", 9.5f)) {
                graphics.Clear(Face); int x = 18;
                foreach (string state in new[] { "待核对", "已保存", "已选训练", "已忽略" }) {
                    Color ink, fill; HistoryStatusTag.Colors(state, out ink, out fill); tagMinimum = Math.Min(tagMinimum, contrast(ink, fill));
                    check(contrast(ink, fill) >= 4.5, "colored status tag remains readable: " + state);
                    x += HistoryStatusTag.Draw(graphics, state, font, new Point(x, 24)) + 12;
                }
                check(x < palette.Width, "all four status tags fit without clipping"); palette.Save(Path.Combine(work, "dictionary-status-tags.png"));
            }
            check(history.editorTag.Width < 150 && !history.editorTag.TabStop, "status label does not create an extra action or keyboard stop");
            File.WriteAllText(Path.Combine(work, "dictionary-ui-audit.txt"), "UI-AUDIT PASS\r\nText over watermark minimum: " + Math.Min(contrast(Dim, HistoryCard.NumberInk), contrast(Dim, HistoryCard.SelectedNumberInk)).ToString("0.00") + "\r\nChecked card/text: " + contrast(Ink, HistoryCard.SelectedPaper).ToString("0.00") + "\r\nWhite checkmark/blue: " + contrast(Color.White, HistoryCard.MarkInk).ToString("0.00") + "\r\nBody/field: " + contrast(Ink, Field).ToString("0.00") + "\r\nSecondary/field: " + contrast(Dim, Field).ToString("0.00") + "\r\nSecondary/page: " + contrast(Dim, Face).ToString("0.00") + "\r\nSecondary/selected: " + contrast(Dim, Color.FromArgb(232, 231, 228)).ToString("0.00") + "\r\nField edge/field: " + contrast(DictionaryField.BorderInk, Field).ToString("0.00") + "\r\nButton edge/white: " + contrast(DictionaryButton.OutlineInk, Color.White).ToString("0.00") + "\r\nScrollbar/field: " + contrast(DictionaryScrollBar.IdleInk, Field).ToString("0.00") + "\r\nColored tag minimum: " + tagMinimum.ToString("0.00") + "\r\nRight/left click, material scrollbar drag/Home/End/wheel, empty editor, save/undo and grouped actions: PASS\r\n");
            foreach (var item in history.entries) store.ChangeState(item, "pending");
            history.visits.Clear(); history.visitIndex = -1; history.Navigate(0); history.filter.SelectedIndex = 1; history.Filter(); history.RecordVisit();
            history.rows.SelectedIndex = 0; history.Navigate(1); history.SaveAndNext(); history.GoVisit(-1); history.GoVisit(-1);
            check(history.page == 0 && history.rows.Items.Contains(history.current) && history.rows.SelectedItem == history.current, "back after saving a filtered recording keeps current equal to visible selection");
            check(backArrow.ForeColor != backArrow.DisabledInk && forwardArrow.ForeColor != forwardArrow.DisabledInk, "navigation arrows have distinct active and inactive colors");
            foreach (Control control in dialog.Controls) if (control is Button && control.Text == "词语") ((Button)control).PerformClick();
            check(!backArrow.Visible && !forwardArrow.Visible, "returning to word workspace hides arrows again");
            dialog.Close();
        }
        string damaged = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(damaged, "{broken");
        store.Load(out errors, out bytes); check(errors.Length > 0, "corrupt records are reported");
        failed = false; try { store.Export(); } catch (IOException) { failed = true; } check(failed, "corrupt records block incomplete batches"); File.Move(damaged, damaged + ".bad");
        daily = store.Load(out errors, out bytes).Find(item => item.Kind == "dictation-history"); store.Remove(daily);
        check(File.Exists(daily.Audio) && File.Exists(daily.FileName + ".deleted") && store.Load(out errors, out bytes).Count == 1, "delete retains recoverable original audio and labels");
        string deleteRoot = Path.Combine(root, "delete-fixture"); var deleteSamples = new VoiceSampleStore(deleteRoot);
        for (int i = 0; i < 3; i++) deleteSamples.Save("删除测试", "完整句子" + i, pcm);
        var deleteStore = new HistoryStore(deleteRoot); var deleteEntries = deleteStore.Load(out errors, out bytes);
        deleteStore.Save(deleteEntries[0], deleteEntries[0].Text("correctText"), true, false); deleteStore.Save(deleteEntries[1], deleteEntries[1].Text("correctText"), true, false);
        string protectedBatch = deleteStore.Export();
        using (var owner = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Size = new Size(878, 560), ShowInTaskbar = false }) using (var panel = new HistoryPanel(deleteRoot)) {
            owner.Controls.Add(panel); owner.Show(); Action<Task> finish = task => { var timer = Stopwatch.StartNew(); while (!task.IsCompleted && timer.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(5); } check(task.IsCompleted && !task.IsFaulted, "delete task completes"); };
            finish(panel.Reload()); panel.SetBatchMode(true); panel.filter.SelectedIndex = 2;
            check(panel.rows.Items.Count == 2 && panel.bulkMenu.Items[panel.bulkMenu.Items.Count - 1].Text.Contains("删除录音"), "batch menu exposes deletion");
            foreach (var item in panel.entries) panel.marked.Add(item.FileName);
            var first = (HistoryEntry)panel.rows.Items[0]; var second = (HistoryEntry)panel.rows.Items[1];
            bool outsideBlocked = false; try { deleteStore.ValidatePermanentRemoval(new HistoryEntry { Kind = first.Kind, FileName = Path.Combine(deleteRoot, "outside", Path.GetFileName(first.FileName)), Data = first.Data, Original = first.Original }); } catch (IOException) { outsideBlocked = true; } check(outsideBlocked && File.Exists(first.Audio), "permanent deletion refuses a path outside source folder");
            File.AppendAllText(second.FileName, " "); finish(panel.DeleteMarked(true)); check(File.Exists(first.Audio) && File.Exists(first.FileName) && panel.notice.Text.StartsWith("未删除"), "whole-group conflict preflight prevents any deletion"); File.WriteAllText(second.FileName, second.Original);
            using (var locked = new FileStream(second.Audio, FileMode.Open, FileAccess.Read, FileShare.None)) finish(panel.DeleteMarked(true));
            check(!File.Exists(first.FileName) && !File.Exists(first.Audio) && File.Exists(second.FileName) && panel.notice.Text.StartsWith("完整删除 1 条"), "partial failure reports real complete count and stops");
            foreach (HistoryEntry item in panel.rows.Items) panel.marked.Add(item.FileName); finish(panel.DeleteMarked(true));
            check(deleteStore.Load(out errors, out bytes).Count == 1 && !File.Exists(second.FileName) && !File.Exists(second.Audio), "bulk delete removes source audio and metadata, excluding hidden unchecked scope");
            check(File.Exists(Path.Combine(protectedBatch, "manifest.json")) && Directory.GetFiles(Path.Combine(protectedBatch, "audio")).Length == 2, "permanent source deletion preserves immutable archive copies");
            using (var confirmation = new DictionaryDeleteDialog("", true, 2)) { check(confirmation.Text == "永久删除录音" && confirmation.AcceptButton == confirmation.CancelButton, "permanent confirmation defaults to cancellation"); confirmation.StartPosition = FormStartPosition.Manual; confirmation.Location = new Point(-20000, -20000); confirmation.Show(owner); using (var bitmap = new Bitmap(confirmation.Width, confirmation.Height)) { confirmation.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-history-delete-confirm.png")); } confirmation.Close(); }
            owner.Close();
        }
        Console.WriteLine("HISTORY-TEST PASS: archive withdrawal and restoration with immutable snapshots / custom named batch tabs / full-width search / explicit open / untruncated action menus / buffered native paint and direct draw / training title help and larger list / coalesced paint notifications / whole-card multi-select / permanent bulk deletion with conflict and partial-failure guards / opt-in / daily and manual review / original texts / explicit selection / deselection / conflict / immutable selected batch / source groups / corrupt records / safe delete / simple editor with one save action / optional controls in context / completion returns to list / record training verification modules / archive second confirmation and agent JSONL / separate library editor and materials pages / direct navigation and return / mouse button guards / material scrollbar drag and keyboard / empty editor guard / text and component contrast / filtered back selection consistency / stable scroll identity / search clear buttons / scoped navigation visibility / back and forward visit memory / forward branch reset / four semantic color tags with text / shared dictionary grid and footer / aligned content widths / no status overlap / contextual buttons with borders / four filters / bulk select and deselect / bulk ignore and reopen / grouped undo / review gate / visible saved list / saved counts and row states / simple default / optional comparison / save and next / failed-save retention / skip unchanged / offscreen layout");
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); using (var pen = new Pen(Color.FromArgb(220, 218, 214))) { e.Graphics.DrawLine(pen, 280, 4, 280, 490); e.Graphics.DrawLine(pen, 4, 490, 872, 490); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { if (capture != null && !capture.IsDisposed) capture.Dispose(); Stop(); batchChoice.Dispose(); trainingHelp.Dispose(); moreMenu.Dispose(); filter.Dispose(); playback.Dispose(); player.Dispose(); } base.Dispose(disposing); }
}

sealed class DictionaryHelpIcon : Control
{
    public DictionaryHelpIcon() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        AccessibleRole = AccessibleRole.PushButton; Cursor = Cursors.Help;
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color ink = Focused || ClientRectangle.Contains(PointToClient(MousePosition)) ? Color.FromArgb(45, 46, 50) : ForeColor;
        using (var pen = new Pen(ink, 1.2f)) {
            e.Graphics.DrawEllipse(pen, 3, 3, 15, 15);
            e.Graphics.DrawLine(pen, 10.5f, 6.5f, 10.5f, 11.5f);
            e.Graphics.DrawEllipse(pen, 10, 14, 1, 1);
        }
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

sealed class DictionaryLabel : Label
{
    public override Size GetPreferredSize(Size proposedSize) {
        using (var graphics = CreateGraphics()) {
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            SizeF measured = graphics.MeasureString(Text, Font);
            return new Size((int)Math.Ceiling(measured.Width) + 2, (int)Math.Ceiling(measured.Height) + 2);
        }
    }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var brush = new SolidBrush(ForeColor)) e.Graphics.DrawString(Text, Font, brush, ClientRectangle);
    }
}

// Request smooth system corners without adding a native frame to the custom UI.
class DictionaryWindow : Form
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    internal bool SystemCorners;
    internal bool HasNativeFrame { get { return (GetWindowLong(Handle, -16) & 0x00C40000) != 0; } }
    protected override CreateParams CreateParams {
        get { var value = base.CreateParams; value.Style &= ~0x00C40000; return value; }
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int preference = 2;
        SystemCorners = DwmSetWindowAttribute(Handle, 33, ref preference, 4) == 0;
        int border = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(Handle, 34, ref border, 4);
    }
}

sealed class DictionaryDeleteDialog : DictionaryWindow
{
    readonly Button confirm, cancel;
    public DictionaryDeleteDialog(string word, bool sample = false, int recordCount = 0) {
        Text = recordCount > 0 ? "永久删除录音" : "确认删除词语"; AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(480, 244); BackColor = Color.FromArgb(251, 250, 248); DoubleBuffered = true;
        var title = DictionaryDialog.Caption(recordCount > 0 ? "永久删除 " + recordCount + " 条录音？" : sample ? "删除这段录音？" : "删除这个词语？", 28, 24, Color.FromArgb(20, 25, 31));
        title.Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold); Controls.Add(title);
        var detail = DictionaryDialog.Caption(recordCount > 0 ? "删除所选原音与对应标注，无法撤销。\n已归档批次和验证记录中的副本仍保留。" : sample ? "将从样本列表移除这段录音。\n原始文件保留在本机，不会删除词条。" : "将移除「" + word + "」及其纠错写法。\n删除后 10 秒内可撤回，保存后生效。", 28, 78, Color.FromArgb(102, 106, 113));
        detail.AutoSize = false; detail.Size = new Size(424, 78); detail.Font = new Font(recordCount > 0 ? HistoryCard.TextFamily : "Segoe UI Variable Text", 12f);
        Controls.Add(detail);
        cancel = DictionaryDialog.MakeButton("取消", new Rectangle(228, 178, 100, 42), () => { DialogResult = DialogResult.Cancel; Close(); });
        confirm = DictionaryDialog.MakeButton("删除", new Rectangle(344, 178, 108, 42), () => { DialogResult = DialogResult.OK; Close(); });
        ((DictionaryButton)confirm).Primary = true;
        Controls.Add(cancel); Controls.Add(confirm); CancelButton = cancel; AcceptButton = cancel; ActiveControl = cancel;
    }
    internal static void CheckForTest(string work) {
        using (var dialog = new DictionaryDeleteDialog("Example")) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-delete-confirm.png"));
            }
            dialog.cancel.PerformClick();
            if (dialog.DialogResult != DialogResult.Cancel) throw new Exception("Delete cancel failed");
        }
        using (var dialog = new DictionaryDeleteDialog("Example")) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.Show(); dialog.confirm.PerformClick();
            if (dialog.DialogResult != DialogResult.OK) throw new Exception("Delete confirmation failed");
        }
    }
    internal static bool ConfirmForTest(Form owner, string word, bool accept) {
        using (var dialog = new DictionaryDeleteDialog(word))
        using (var timer = new System.Windows.Forms.Timer { Interval = 30 }) {
            bool borderless = true;
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            timer.Tick += (s, e) => { timer.Stop(); borderless = !dialog.HasNativeFrame;
                if (accept && borderless) dialog.confirm.PerformClick(); else dialog.cancel.PerformClick(); };
            timer.Start();
            bool result = dialog.ShowDialog(owner) == DialogResult.OK;
            if (!borderless) throw new Exception("Confirmation must remain borderless");
            return result;
        }
    }
}

sealed class DictionaryUndoBar : Panel
{
    readonly Label message = new DictionaryLabel();
    internal readonly Button UndoButton;
    public DictionaryUndoBar(Action undo) {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleRole = AccessibleRole.Alert;
        BackColor = Color.FromArgb(251, 250, 248); Size = new Size(420, 50);
        message.Bounds = new Rectangle(12, 13, 262, 27); message.Font = new Font("Segoe UI Variable Text", 11f);
        message.ForeColor = Color.FromArgb(40, 42, 46); message.BackColor = Color.White; Controls.Add(message);
        UndoButton = DictionaryDialog.MakeButton("撤回 · 10 秒", new Rectangle(280, 7, 128, 36), undo);
        UndoButton.Font = new Font("Segoe UI Variable Text", 11f, FontStyle.Bold); UndoButton.BackColor = Color.White;
        Controls.Add(UndoButton);
    }
    internal void UpdateNotice(string word, int seconds) {
        message.Text = "已删除「" + word + "」";
        message.AccessibleName = message.Text;
        UndoButton.Text = "撤回 · " + seconds + " 秒";
        UndoButton.AccessibleName = "撤回删除，还剩 " + seconds + " 秒";
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = MicrophoneMenuRenderer.Outline(new Rectangle(0, 0, Width - 1, Height - 1), 12)) {
            using (var fill = new SolidBrush(Color.White)) e.Graphics.FillPath(fill, path);
            using (var pen = new Pen(Color.FromArgb(221, 220, 217))) e.Graphics.DrawPath(pen, path);
        }
    }
}

// Personal dictionary editor: pick a word on the left, list the ways it gets
// misheard on the right. Saving rewrites dictionary.json (atomic replace); the
// recogniser already watches that file, so the next utterance uses the new list
// without a restart.
sealed class DictionaryDialog : DictionaryWindow
{
    internal const int HotwordLimit = 64;
    static readonly Color Face = Color.FromArgb(251, 250, 248);
    static readonly Color Ink = Color.FromArgb(20, 25, 31);
    static readonly Color Dim = Color.FromArgb(102, 106, 113);
    static readonly Color Field = Color.FromArgb(246, 245, 243);
    static readonly Color Edge = Color.FromArgb(220, 219, 216);

    string path;
    readonly string root;
    readonly List<WordGroup> groups = new List<WordGroup>();
    readonly List<int> visible = new List<int>();
    readonly DictionaryListBox list = new DictionaryListBox();
    readonly TextBox search = new TextBox();
    readonly DictionaryTextBox variants = new DictionaryTextBox();
    readonly TextBox entry = new TextBox();
    readonly Label status = new DictionaryLabel();
    readonly Label wordTitle = new DictionaryLabel();
    readonly Label counts = new DictionaryLabel();
    readonly Label scope = new DictionaryLabel();
    readonly TextBox previewInput = new TextBox();
    readonly TextBox previewOutput = new TextBox();
    readonly Label previewHits = new DictionaryLabel();
    readonly ToolTip help = new ToolTip();
    readonly Panel textPage = new Panel();
    internal Func<Task> PrepareRecording { get { return historyPage.PrepareRecording; } set { historyPage.PrepareRecording = value; } }
    internal bool RecordingBusy { get { return historyPage.RecordingBusy; } }
    internal void StopSampleRecording() { historyPage.StopCapture(); }
    sealed class DeletedWord { internal WordGroup Group; internal int Index; internal long Deadline; }
    readonly List<DeletedWord> deleted = new List<DeletedWord>();
    readonly Stopwatch undoClock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer undoTimer = new System.Windows.Forms.Timer { Interval = 100 };
    readonly DictionaryUndoBar undoBar;
    Func<string, bool> confirmDeletion;
    Button saveButton, addButton, deleteButton, previewButton, clearWordSearch;
    Process previewWorker;
    int previewRevision;
    string loadedText;
    bool loadFailed;
    int current = -1;
    bool silent, dirty;
    readonly HistoryPanel historyPage;
    readonly Button wordsWorkspace, historyWorkspace, navBack, navForward;
    readonly List<Control> hiddenWordControls = new List<Control>();
    Point dragFrom;

    public DictionaryDialog(string root, string previewRoot = null) {
        this.root = previewRoot ?? root;
        historyPage = new HistoryPanel(root) { Location = new Point(24, 100), Visible = false };
        path = Path.Combine(root, "dictionary.json");
        Text = "个人词典"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(926, 680); BackColor = Face; ForeColor = Ink;
        Font = new Font("Segoe UI Variable Text", 16.5f); DoubleBuffered = true; AccessibleName = "个人词典";

        var title = Caption("个人词典", 30, 20, Ink);
        title.Font = new Font("Microsoft YaHei UI", 24f, FontStyle.Bold); Controls.Add(title);
        var info = new DictionaryHelpIcon { Bounds = new Rectangle(title.Right + 12, 35, 22, 22), BackColor = Color.Transparent,
            ForeColor = Dim, AccessibleName = "词典与录音说明", TabStop = true };
        const string explanation = "填写错法可直接纠正文字；新规则在中文、纯英文和混说中生效，旧规则保留原设置。\n按列表顺序前 64 词参与近音救援，超出部分的错法纠正仍有效；纯中文暂无同等救援。\n听写记录集中试听、修正完整文字；需要主动补录时，在听写记录点补录。\n录音仅保存在本机，明确勾选才会进入训练材料；保存样本不等于模型已学会。\n删除词语不会删除已有录音。文字预览不测试声音。";
        info.AccessibleDescription = explanation;
        help.InitialDelay = 350; help.ReshowDelay = 100; help.AutoPopDelay = 15000; help.SetToolTip(info, explanation);
        info.Enter += (o, e) => { if (Bounds.IntersectsWith(Screen.FromControl(this).WorkingArea)) help.Show(explanation, info, 0, info.Height + 6, 15000); };
        info.Leave += (o, e) => help.Hide(info); info.MouseLeave += (o, e) => help.Hide(info); Controls.Add(info);
        counts.Bounds = new Rectangle(338, 72, 556, 32); counts.ForeColor = Dim;
        counts.Font = new Font(Font.FontFamily, 9f); Controls.Add(counts);
        var close = IconButton("close", "关闭词典", new Rectangle(854, 24, 40, 40), Close); Controls.Add(close);

        var searchField = new DictionaryField { Bounds = new Rectangle(34, 104, 248, 42), Glyph = "search" };
        search.Bounds = new Rectangle(40, 11, 194, 24); search.Font = new Font(Font.FontFamily, 12f);
        search.BorderStyle = BorderStyle.None; search.BackColor = Field; search.ForeColor = Ink;
        search.AccessibleName = "搜索词语或错法";
        search.TextChanged += (o, e) => { if (!silent) { Commit(); FilterList(); } };
        searchField.Controls.Add(search); Controls.Add(searchField);
        clearWordSearch = IconButton("close", "清空词语搜索", new Rectangle(210, 5, 32, 32), () => { search.Clear(); search.Focus(); }); clearWordSearch.BackColor = Field; clearWordSearch.Visible = false; searchField.Controls.Add(clearWordSearch);
        search.TextChanged += (o, e) => { bool hasQuery = search.TextLength > 0; clearWordSearch.Visible = hasQuery; search.Width = hasQuery ? 162 : 194; };
        var listClip = new Panel { Bounds = new Rectangle(34, 162, 248, 410), BackColor = Face };
        list.Bounds = new Rectangle(0, 0, 248, 410); list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 41; list.BorderStyle = BorderStyle.None; list.BackColor = Face; list.ForeColor = Ink;
        list.IntegralHeight = false; list.AccessibleName = "词语列表";
        list.DrawItem += PaintRow; list.SelectedIndexChanged += OnSelected; listClip.Controls.Add(list); Controls.Add(listClip);
        var listScroll = new DictionaryScrollBar(() => list.Items.Count, () => Math.Max(1, list.ClientSize.Height / list.ItemHeight),
            () => list.TopIndex, value => { if (list.Items.Count > 0) list.TopIndex = value; });
        listScroll.Bounds = new Rectangle(286, 164, 12, 402); listScroll.BackColor = Face;
        list.ViewChanged = () => { listScroll.TabStop = listScroll.Overflows; listScroll.Invalidate(); }; Controls.Add(listScroll);

        wordTitle.Bounds = new Rectangle(334, 102, 504, 46); wordTitle.Font = new Font(Font.FontFamily, 22f, FontStyle.Bold); Controls.Add(wordTitle);
        deleteButton = IconButton("trash", "删除词语", new Rectangle(854, 104, 40, 40), Remove); Controls.Add(deleteButton);
        scope.Bounds = new Rectangle(338, 151, 556, 24); scope.ForeColor = Dim; scope.Font = new Font(Font.FontFamily, 10f); Controls.Add(scope);
        textPage.Bounds = new Rectangle(338, 196, 556, 378);
        textPage.BackColor = Face; Controls.Add(textPage);

        var variantLabel = Caption("容易识别成", 0, 0, Dim); variantLabel.Font = new Font(Font.FontFamily, 12f); textPage.Controls.Add(variantLabel);
        var variantHint = Caption("每行一项 · 粘贴自动整理", 340, 3, Dim); variantHint.Font = new Font(Font.FontFamily, 10f); textPage.Controls.Add(variantHint);
        var field = new DictionaryField { Bounds = new Rectangle(0, 32, 556, 186) };
        var textClip = new Panel { Bounds = new Rectangle(16, 12, 512, 160), BackColor = Field };
        variants.Bounds = new Rectangle(0, 0, 512, 160); variants.Font = new Font(Font.FontFamily, 15f);
        variants.Multiline = true; variants.ScrollBars = ScrollBars.None; variants.BorderStyle = BorderStyle.None;
        variants.BackColor = Field; variants.ForeColor = Ink; variants.AcceptsReturn = true; variants.AccessibleName = "容易识别成，每行一种写法";
        variants.TextChanged += (o, e) => { if (!silent) { Commit(); MarkDirty(); } };
        variants.Pasted = message => status.Text = message;
        variants.Enter += (o, e) => field.Invalidate(); variants.Leave += (o, e) => field.Invalidate();
        textClip.Controls.Add(variants); field.Controls.Add(textClip);
        var textScroll = new DictionaryScrollBar(() => (int)DictionaryScrollBar.SendMessage(variants.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero),
            () => Math.Max(1, variants.ClientSize.Height / variants.Font.Height),
            () => (int)DictionaryScrollBar.SendMessage(variants.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero),
            value => { int line = (int)DictionaryScrollBar.SendMessage(variants.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero); DictionaryScrollBar.SendMessage(variants.Handle, 0x00B6, IntPtr.Zero, new IntPtr(value - line)); });
        textScroll.Bounds = new Rectangle(538, 12, 12, 160); textScroll.BackColor = Field;
        variants.ViewChanged = () => { textScroll.TabStop = textScroll.Overflows; textScroll.Invalidate(); };
        field.Controls.Add(textScroll); textPage.Controls.Add(field);
        var previewLabel = Caption("文字预览", 0, 236, Dim); previewLabel.Font = new Font(Font.FontFamily, 12f); textPage.Controls.Add(previewLabel);
        var previewField = new DictionaryField { Bounds = new Rectangle(0, 268, 556, 42) };
        previewInput.Bounds = new Rectangle(14, 11, 490, 24); previewInput.Font = new Font(Font.FontFamily, 12f);
        previewInput.BorderStyle = BorderStyle.None; previewInput.BackColor = Field; previewInput.ForeColor = Ink;
        previewInput.MaxLength = 10000; previewInput.AccessibleName = "输入一句文字试试替换";
        previewInput.TextChanged += (o, e) => InvalidatePreview(); previewField.Controls.Add(previewInput);
        previewButton = IconButton("arrow", "运行文字预览", new Rectangle(514, 2, 40, 38), async () => await PreviewAsync()); previewButton.BackColor = Field; previewField.Controls.Add(previewButton);
        previewInput.KeyDown += async (o, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (previewButton.Enabled) await PreviewAsync(); } };
        textPage.Controls.Add(previewField);
        previewOutput.Bounds = new Rectangle(4, 320, 548, 28); previewOutput.ReadOnly = true; previewOutput.BorderStyle = BorderStyle.None;
        previewOutput.BackColor = Face; previewOutput.Font = new Font(Font.FontFamily, 12f); previewOutput.ForeColor = Ink;
        previewOutput.AccessibleName = "纠正后的文字"; previewOutput.TabStop = false; previewOutput.TextChanged += (o, e) => previewOutput.TabStop = previewOutput.TextLength > 0; textPage.Controls.Add(previewOutput);
        previewHits.Bounds = new Rectangle(0, 352, 556, 26); previewHits.ForeColor = Dim;
        previewHits.Font = new Font(Font.FontFamily, 10f); previewHits.AutoEllipsis = true; textPage.Controls.Add(previewHits);

        entry.Bounds = new Rectangle(34, 616, 194, 30); entry.Visible = false; entry.Font = new Font(Font.FontFamily, 12f);
        entry.BorderStyle = BorderStyle.FixedSingle; entry.BackColor = Field; entry.ForeColor = Ink; entry.AccessibleName = "新词";
        entry.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter) { Add(); e.SuppressKeyPress = true; } }; Controls.Add(entry);
        addButton = MakeButton("添加词语", new Rectangle(24, 604, 156, 52), () => {
            if (entry.Visible) { Add(); return; }
            entry.Visible = true; addButton.Text = "添加"; ((DictionaryButton)addButton).Glyph = null; addButton.Bounds = new Rectangle(236, 604, 68, 52); entry.Focus();
        });
        ((DictionaryButton)addButton).Glyph = "plus";
        saveButton = MakeButton("保存", new Rectangle(770, 606, 124, 52), () => { if (dirty) Save(); }); ((DictionaryButton)saveButton).Primary = true;
        Controls.Add(addButton); Controls.Add(saveButton);
        status.Bounds = new Rectangle(338, 610, 420, 48); status.ForeColor = Dim; status.Font = new Font(Font.FontFamily, 10.5f); status.AutoEllipsis = true; Controls.Add(status);
        undoBar = new DictionaryUndoBar(() => UndoDelete()) { Location = new Point(334, 608), Visible = false }; Controls.Add(undoBar);
        undoTimer.Tick += (o, e) => UpdateUndoNotice();
        confirmDeletion = word => { using (var confirmation = new DictionaryDeleteDialog(word)) return confirmation.ShowDialog(this) == DialogResult.OK; };
        MouseDown += (o, e) => { if (e.Button == MouseButtons.Left) dragFrom = e.Location; };
        MouseMove += (o, e) => { if (e.Button == MouseButtons.Left) Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y); };
        Shown += (o, e) => {
            Reload(); Native.Cue(search.Handle, 0x1501, new IntPtr(1), "搜索词语或错法");
            Native.Cue(previewInput.Handle, 0x1501, new IntPtr(1), "输入文字，按 Enter 预览"); ActiveControl = list;
        };
        KeyPreview = true; KeyDown += (o, e) => { if (e.Alt && historyPage.Visible && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)) { historyPage.GoVisit(e.KeyCode == Keys.Left ? -1 : 1); e.SuppressKeyPress = true; return; } if (e.KeyCode == Keys.Escape) {
            if (RecordingBusy) StopSampleRecording(); else if (entry.Visible) EndAdd(); else Close(); e.SuppressKeyPress = true;
        } };
        wordsWorkspace = MakeButton("词语", new Rectangle(338, 26, 150, 40), () => SelectWorkspace(false));
        historyWorkspace = MakeButton("听写记录", new Rectangle(500, 26, 178, 40), () => SelectWorkspace(true));
        ((DictionaryButton)wordsWorkspace).Glyph = "edit"; ((DictionaryButton)historyWorkspace).Glyph = "mic";
        wordsWorkspace.Font = historyWorkspace.Font = new Font(Font.FontFamily, 12f);
        Controls.Add(wordsWorkspace); Controls.Add(historyWorkspace); Controls.Add(historyPage);
        navBack = IconButton("back", "后退到上次查看的位置，Alt+左箭头", new Rectangle(234, 26, 40, 40), () => historyPage.GoVisit(-1));
        navForward = IconButton("forward", "前进到下次查看的位置，Alt+右箭头", new Rectangle(278, 26, 40, 40), () => historyPage.GoVisit(1));
        foreach (var arrow in new[] { navBack, navForward }) { arrow.ForeColor = Ink; ((DictionaryButton)arrow).DisabledInk = Color.FromArgb(184, 184, 181); }
        Controls.Add(navBack); Controls.Add(navForward); historyPage.NavigationChanged = UpdateNavigation;
        SelectWorkspace(false); ActiveControl = list;
    }

    async void SelectWorkspace(bool history) {
        if (RecordingBusy || !historyPage.Resolve()) return;
        historyPage.Stop();
        if (history && !historyPage.Visible) {
            hiddenWordControls.Clear();
            foreach (Control control in Controls) if (control != historyPage && control.Top >= 100 && control.Visible) { hiddenWordControls.Add(control); control.Visible = false; }
        } else if (!history) { foreach (Control control in hiddenWordControls) control.Visible = true; hiddenWordControls.Clear(); }
        historyPage.Visible = history; counts.Visible = !history;
        ((DictionaryButton)wordsWorkspace).Primary = !history; ((DictionaryButton)historyWorkspace).Primary = history;
        wordsWorkspace.AccessibleDescription = history ? "未选中" : "已选中"; historyWorkspace.AccessibleDescription = history ? "已选中" : "未选中";
        wordsWorkspace.Invalidate(); historyWorkspace.Invalidate(); UpdateNavigation();
        if (history) { historyPage.BringToFront(); await historyPage.Reload(); }
    }
    void UpdateNavigation() {
        if (IsDisposed || Disposing) return;
        if (!IsHandleCreated) { navBack.Visible = navForward.Visible = false; return; }
        if (InvokeRequired) { try { BeginInvoke(new Action(UpdateNavigation)); } catch (InvalidOperationException) { } return; }
        if (navBack.IsDisposed || navForward.IsDisposed) return;
        navBack.Visible = navForward.Visible = historyPage.Visible; navBack.Enabled = historyPage.Visible && !historyPage.Loading && historyPage.CanGoBack; navForward.Enabled = historyPage.Visible && !historyPage.Loading && historyPage.CanGoForward; }
    internal void SetHistoryNotice(string value) { historyPage.RuntimeNotice = value; if (historyPage.Visible && value.Length > 0) historyPage.Notice.Text = value; }
    internal HistoryPanel HistoryForTest { get { return historyPage; } }
    internal void SelectHistoryForTest() { SelectWorkspace(true); }

    Button IconButton(string glyph, string name, Rectangle bounds, Action click) {
        var button = (DictionaryButton)MakeButton("", bounds, click); button.Glyph = glyph;
        button.AccessibleName = name; button.AccessibleDescription = name; return button;
    }
    void MarkDirty() { dirty = true; saveButton.Enabled = true; status.Text = "有未保存的修改"; UpdateCounts(); InvalidatePreview(); }

    void UpdateCounts() {
        int rules = 0; foreach (var group in groups) if (group.Variants.Count > 0) rules++;
        counts.Text = "词语 " + groups.Count + "  ·  纠错 " + rules + "  ·  救援 " + Math.Min(groups.Count, HotwordLimit) + "/" + HotwordLimit;
        if (groups.Count > HotwordLimit) counts.Text += "\n末尾 " + (groups.Count - HotwordLimit) + " 词不参与救援，错法纠正仍生效";
        var groupNow = current >= 0 && current < groups.Count ? groups[current] : null;
        scope.Text = groupNow == null ? "" : (groupNow.Variants.Count == 0 ? "未填错法 · 仅加入救援词表" :
            (groupNow.Context == "zh" ? "旧规则仅中文/混说" : "中文、纯英文、混说") + " · " + groupNow.Variants.Count + " 种错法");
    }

    void InvalidatePreview() {
        previewRevision++;
        previewOutput.Clear(); previewHits.Text = "";
    }

    void ClearSearch() { bool was = silent; silent = true; search.Clear(); silent = was; }

    static async Task<string> ExchangePreview(Process process, string payload) {
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
            await input.WriteAsync(payload);
        string result = await output;
        await errors;
        return result;
    }

    async Task PreviewAsync() {
        if (!previewButton.Enabled) return;
        Commit();
        if (loadFailed || !ValidateVariants()) { previewHits.Text = "请先修复词典读取或错法冲突，再预览"; return; }
        if (previewInput.Text.Length == 0) { previewHits.Text = "先输入一句想检查的文字"; previewInput.Focus(); return; }
        var serializer = new JavaScriptSerializer();
        string payload = "{\"dictionary\":" + SerializeDictionary() + ",\"text\":" + Quote(previewInput.Text) + "}";
        int revision = previewRevision;
        previewButton.Enabled = false; previewHits.Text = "正在预览…";
        try {
            var info = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "python.exe"), "\"" + Path.Combine(root, "app", "dictionary_rules.py") + "\"") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = new Process { StartInfo = info }) {
                previewWorker = process;
                process.Start();
                var exchange = ExchangePreview(process, payload);
                if (await Task.WhenAny(exchange, Task.Delay(2000)) != exchange) {
                    if (!process.HasExited) process.Kill();
                    try { await exchange; } catch { }
                    throw new TimeoutException();
                }
                string output = await exchange;
                if (IsDisposed || Disposing || revision != previewRevision) return;
                var reply = serializer.DeserializeObject(output) as Dictionary<string, object>;
                if (reply == null || !(reply["text"] is string) || !(reply["hits"] is object[])) throw new IOException();
                previewOutput.Text = (string)reply["text"];
                var descriptions = new List<string>();
                foreach (var item in (object[])reply["hits"]) {
                    var hit = (Dictionary<string, object>)item;
                    string description = (bool)hit["applied"] ? hit["from"] + " → " + hit["word"] : hit["word"] + "（旧规则仅中文，本句未生效）";
                    if (!descriptions.Contains(description)) descriptions.Add(description);
                }
                previewHits.Text = descriptions.Count == 0 ? "未命中错法，文字保持原样" : string.Join("；", descriptions.ToArray());
                previewOutput.AccessibleDescription = previewHits.Text;
                previewHits.AccessibleDescription = previewHits.Text;
            }
        } catch {
            if (!IsDisposed && !Disposing && revision == previewRevision) {
                previewOutput.Clear(); previewHits.Text = "预览暂不可用，请稍后重试；不影响保存";
            }
        } finally {
            previewWorker = null;
            if (!IsDisposed && !Disposing) previewButton.Enabled = true;
        }
    }

    void FilterList() {
        bool was = silent; silent = true;
        string query = search.Text.Trim();
        list.BeginUpdate(); list.Items.Clear(); visible.Clear();
        for (int i = 0; i < groups.Count; i++) {
            var group = groups[i];
            bool matches = query.Length == 0 || group.Word.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var variant in group.Variants) if (variant.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) matches = true;
            if (matches) { visible.Add(i); list.Items.Add(group.Word); }
        }
        list.SelectedIndex = visible.IndexOf(current); list.EndUpdate();
        silent = was;
        if (query.Length > 0) status.Text = "找到 " + visible.Count + " / " + groups.Count + " 个词；右侧保留当前编辑";
        list.Invalidate();
    }
    void EndAdd() { entry.Visible = false; entry.Text = ""; addButton.Text = "添加词语"; ((DictionaryButton)addButton).Glyph = "plus"; addButton.Bounds = new Rectangle(24, 604, 156, 52); }

    internal static Label Caption(string text, int x, int y, Color ink) {
        return new DictionaryLabel { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = ink,
            Font = new Font("Segoe UI Variable Text", 15f) };
    }

    internal static Button MakeButton(string text, Rectangle bounds, Action click) {
        var button = new DictionaryButton {
            Text = text, Bounds = bounds, BackColor = Face, ForeColor = Dim,
            AccessibleName = text, Font = new Font("Segoe UI Variable Text", 15f) };
        button.Click += (s, e) => click();
        return button;
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(Edge)) { e.Graphics.DrawLine(pen, 304, 104, 304, 590); e.Graphics.DrawLine(pen, 28, 590, 896, 590); }
    }

    void PaintRow(object sender, DrawItemEventArgs e) {
        if (e.Index < 0 || e.Index >= visible.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(Face)) e.Graphics.FillRectangle(back, e.Bounds);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected) using (var shape = MicrophoneMenuRenderer.Outline(new Rectangle(0, e.Bounds.Y + 1, 246, e.Bounds.Height - 2), 7))
            using (var back = new SolidBrush(list.Focused ? Color.FromArgb(230, 229, 227) : Color.FromArgb(233, 232, 230))) e.Graphics.FillPath(back, shape);
        if (selected) using (var brush = new SolidBrush(Dim)) e.Graphics.FillRectangle(brush, e.Bounds.X + 1, e.Bounds.Y + 8, 2, e.Bounds.Height - 16);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var brush = new SolidBrush(Ink))
        using (var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            e.Graphics.DrawString(groups[visible[e.Index]].Word, Font, brush, new Rectangle(10, e.Bounds.Y, 232, e.Bounds.Height), format);
    }

    void Reload() {
        InvalidatePreview();
        ClearSearch();
        groups.Clear();
        try {
            loadedText = File.Exists(path) ? File.ReadAllText(path) : null;
            var data = loadedText == null ? new Dictionary<string, object>() : new JavaScriptSerializer().DeserializeObject(loadedText) as Dictionary<string, object>;
            if (data == null) throw new FormatException("词典必须是 JSON 对象");
            loadFailed = false;
            object words = null, rules = null;
            if (data != null) { data.TryGetValue("hotwords", out words); data.TryGetValue("rules", out rules); }
            if (words is object[]) {
                foreach (var item in (object[])words) {
                    var word = (item as string);
                    if (word == null) continue;
                    word = word.Trim();
                    if (word.Length == 0 || Index(word) >= 0) continue;
                    groups.Add(new WordGroup { Word = word });
                }
            }
            if (rules is object[]) {
                foreach (var item in (object[])rules) {
                    var rule = item as Dictionary<string, object>;
                    if (rule == null) continue;
                    object target = null, source = null, context = null;
                    rule.TryGetValue("to", out target); rule.TryGetValue("from", out source);
                    rule.TryGetValue("context", out context);
                    var word = (target as string);
                    if (word == null || word.Trim().Length == 0) continue;
                    word = word.Trim();
                    int index = Index(word);
                    WordGroup group = index >= 0 ? groups[index] : new WordGroup { Word = word };
                    if (index < 0) groups.Add(group);
                    group.Context = (context as string) ?? "";
                    if (!(source is object[])) continue;
                    foreach (var item2 in (object[])source) {
                        var variant = (item2 as string);
                        if (variant == null) continue;
                        variant = variant.Trim();
                        if (variant.Length > 0 && !group.Variants.Contains(variant)) group.Variants.Add(variant);
                    }
                }
            }
        } catch (Exception) {
            loadFailed = true;
            status.Text = "词典读取失败，请先修复文件";
        }
        dirty = false; saveButton.Enabled = !loadFailed;
        RefreshList(-1);
    }

    int Index(string word) {
        for (int i = 0; i < groups.Count; i++)
            if (string.Equals(groups[i].Word, word, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    void ShowWord(int index) {
        bool was = silent;
        silent = true;
        current = index >= 0 && index < groups.Count ? index : -1;
        variants.Text = current >= 0 ? string.Join("\r\n", groups[current].Variants.ToArray()) : "";
        variants.ClearUndo(); variants.Enabled = current >= 0; deleteButton.Enabled = current >= 0;
        wordTitle.Text = current >= 0 ? groups[current].Word : "先添加一个词语";
        silent = was;
        UpdateCounts();
    }

    void RefreshList(int select) {
        int target = groups.Count == 0 ? -1
            : Math.Min(select < 0 ? Math.Max(current, 0) : select, groups.Count - 1);
        current = target;
        FilterList();
        if (target >= 0 && !visible.Contains(target)) target = visible.Count > 0 ? visible[0] : -1;
        bool was = silent; silent = true; list.SelectedIndex = visible.IndexOf(target); silent = was;
        ShowWord(target);
    }

    internal static string CleanVariants(string source, bool paste) {
        var pieces = new List<string>();
        var piece = new StringBuilder();
        var closers = new Stack<char>();
        const string opens = "\"'「“‘([（【", closes = "\"'」”’)]）】";
        for (int i = 0; i < source.Length; i++) {
            char c = source[i];
            bool newline = c == '\r' || c == '\n';
            if (paste && !newline) {
                if (closers.Count > 0 && c == closers.Peek()) { closers.Pop(); piece.Append(c); continue; }
                int quote = opens.IndexOf(c);
                if (quote >= 0 && source.IndexOf(closes[quote], i + 1) >= 0 &&
                    (quote >= 5 || piece.ToString().Trim().Length == 0 || closers.Count > 0)) {
                    closers.Push(closes[quote]); piece.Append(c); continue;
                }
            }
            if (newline || (paste && closers.Count == 0 && "。！？；，、,;|｜\t".IndexOf(c) >= 0)) {
                pieces.Add(piece.ToString()); piece.Length = 0; closers.Clear();
            } else piece.Append(c);
        }
        pieces.Add(piece.ToString());
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in pieces) {
            var compact = new StringBuilder(); bool space = false;
            foreach (char c in part.Trim()) {
                if (char.IsWhiteSpace(c)) { space = compact.Length > 0; continue; }
                if (space) compact.Append(' ');
                compact.Append(c); space = false;
            }
            string text = compact.ToString();
            if (paste && text.Length >= 2) {
                int quote = opens.Substring(0, 5).IndexOf(text[0]);
                if (quote >= 0 && text[text.Length - 1] == closes[quote]) text = text.Substring(1, text.Length - 2).Trim();
            }
            if (text.Length == 0) continue;
            if (seen.Add(text)) kept.Add(text);
        }
        return string.Join("\r\n", kept.ToArray());
    }

    void Commit() {
        if (current < 0 || current >= groups.Count) return;
        var edited = new List<string>();
        foreach (var line in CleanVariants(variants.Text, false).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)) edited.Add(line);
        groups[current].Variants.Clear(); groups[current].Variants.AddRange(edited);
    }

    void OnSelected(object sender, EventArgs e) {
        if (silent) return;
        Commit();  // keep edits to the word the user just left
        ShowWord(list.SelectedIndex >= 0 ? visible[list.SelectedIndex] : -1);
    }

    void Add() {
        if (RecordingBusy) return;
        var word = entry.Text.Trim();
        if (word.Length == 0) { status.Text = "先在上面填一个词"; entry.Focus(); return; }
        int existing = Index(word);
        if (existing >= 0) {
            Commit(); ClearSearch(); RefreshList(existing); EndAdd(); status.Text = "这个词已经在列表里"; return;
        }
        UpdateUndoNotice();
        foreach (var pending in deleted.ToArray()) if (string.Equals(pending.Group.Word, word, StringComparison.OrdinalIgnoreCase)) {
            RestoreDeleted(pending); EndAdd(); return;
        }
        Commit();
        groups.Add(new WordGroup { Word = word });
        MarkDirty(); EndAdd();
        ClearSearch();
        RefreshList(groups.Count - 1);
        variants.Focus();
        status.Text = "已添加 " + word + "，记得保存";
    }

    void Remove() {
        if (RecordingBusy) return;
        if (current < 0 || current >= groups.Count) return;
        Commit();
        var group = groups[current];
        if (!confirmDeletion(group.Word)) return;
        deleted.Add(new DeletedWord { Group = group, Index = current, Deadline = undoClock.ElapsedMilliseconds + 10000 });
        groups.RemoveAt(current);
        current = Math.Max(0, current - 1);
        MarkDirty();
        RefreshList(groups.Count == 0 ? -1 : Math.Min(current, groups.Count - 1));
        status.Text = "已移除 " + group.Word + "，记得保存";
        undoTimer.Start(); UpdateUndoNotice();
    }

    void UpdateUndoNotice() {
        long now = undoClock.ElapsedMilliseconds;
        deleted.RemoveAll(item => now >= item.Deadline);
        undoBar.Visible = deleted.Count > 0;
        status.Visible = deleted.Count == 0;
        if (deleted.Count == 0) { undoTimer.Stop(); return; }
        var latest = deleted[deleted.Count - 1];
        undoBar.UpdateNotice(latest.Group.Word, (int)Math.Ceiling((latest.Deadline - now) / 1000.0));
        undoBar.BringToFront();
    }

    bool UndoDelete() {
        UpdateUndoNotice();
        return deleted.Count > 0 && RestoreDeleted(deleted[deleted.Count - 1]);
    }

    bool RestoreDeleted(DeletedWord item) {
        if (undoClock.ElapsedMilliseconds >= item.Deadline || !deleted.Contains(item)) { UpdateUndoNotice(); return false; }
        if (Index(item.Group.Word) >= 0) { status.Text = "已有同名词语，无法覆盖撤回"; return false; }
        Commit();
        int index = Math.Min(item.Index, groups.Count);
        groups.Insert(index, item.Group); deleted.Remove(item);
        ClearSearch();
        MarkDirty(); RefreshList(index);
        status.Text = "已撤回删除，记得保存";
        UpdateUndoNotice();
        return true;
    }

    static string Quote(string value) {
        var text = new StringBuilder("\"");
        foreach (char c in value) {
            if (c == '"' || c == '\\') text.Append('\\').Append(c);
            else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
            else text.Append(c);
        }
        return text.Append('"').ToString();
    }

    bool ValidateVariants() {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups) foreach (var variant in group.Variants) {
            string previous;
            if (owners.TryGetValue(variant, out previous) && !string.Equals(previous, group.Word, StringComparison.OrdinalIgnoreCase)) {
                status.Text = "错法冲突：" + variant + " 同时属于 " + previous + " 和 " + group.Word;
                return false;
            }
            owners[variant] = group.Word;
        }
        return true;
    }

    string SerializeDictionary() {
        var hotwords = new List<string>();
        foreach (var group in groups) hotwords.Add(Quote(group.Word));
        var rules = new List<string>();
        foreach (var group in groups) {
            if (group.Variants.Count == 0) continue;
            var from = new List<string>();
            foreach (var variant in group.Variants) from.Add(Quote(variant));
            rules.Add("\n    { \"to\": " + Quote(group.Word) + ", \"from\": [" + string.Join(", ", from.ToArray()) + "]" +
                      (string.IsNullOrEmpty(group.Context) ? "" : ", \"context\": " + Quote(group.Context)) + " }");
        }
        return "{\n  \"hotwords\": [" + string.Join(", ", hotwords.ToArray()) + "],\n  \"rules\": [" +
                   string.Join(",", rules.ToArray()) + "\n  ]\n}\n";
    }

    bool Save() {
        Commit();
        if (!ValidateVariants()) return false;
        var body = SerializeDictionary();
        try {
            if (loadFailed) throw new IOException("原词典读取失败，已阻止覆盖");
            string existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing != loadedText) throw new IOException("词典已被外部修改，请重新打开后再编辑");
            var temp = path + ".tmp";
            File.WriteAllText(temp, body, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            loadedText = body;
            dirty = false; saveButton.Enabled = true;
            status.Text = groups.Count > HotwordLimit ? "词典已保存 · 末尾 " + (groups.Count - HotwordLimit) + " 词仅用于错法纠正" : "词典已保存 · 下一句生效";
            return true;
        } catch (Exception error) {
            status.Text = "保存失败：" + error.Message;
            return false;
        }
    }

    internal string DumpForTest() {
        var text = new StringBuilder("current=" + current + " text=" + variants.Text.Length);
        foreach (var group in groups) text.Append(" | " + group.Word + ":" + group.Variants.Count);
        return text.ToString();
    }

    // Round-trip check: saves through the same writer to a scratch file.
    internal void SaveToForTest(string target) {
        string original = path;
        string originalText = loadedText;
        path = target; loadedText = File.Exists(target) ? File.ReadAllText(target) : null;
        try { if (!Save()) throw new Exception(status.Text); } finally { path = original; loadedText = originalText; }
    }

    internal static void CheckForTest(string work) {
        Action<bool, string> check = (ok, reason) => { if (!ok) throw new Exception("DICTIONARY-TEST: " + reason); };
        HistoryCard.CheckForTest(work);
        using (var choice = new DictionaryCheckBox()) choice.CheckForTest();
        DictionaryDeleteDialog.CheckForTest(work);
        SampleCaptureDialog.CheckSamplesForTest(work);
        HistoryPanel.CheckForTest(work);
        check(CleanVariants("col death。koex。KOEX,cols；kos|koldex", true) == "col death\r\nkoex\r\ncols\r\nkos\r\nkoldex", "split / case-insensitive dedup");
        check(CleanVariants("C++。.NET。node.js。don't。git　 status", true) == "C++\r\n.NET\r\nnode.js\r\ndon't\r\ngit status", "symbols / apostrophe / fullwidth space");
        check(CleanVariants("\"a,b\"；「x|y」；(a,b)；foo!bar?", true) == "a,b\r\nx|y\r\n(a,b)\r\nfoo!bar?", "quoted separators / brackets / ASCII punctuation");
        check(CleanVariants("\"unfinished,second", true) == "\"unfinished\r\nsecond", "unbalanced quote does not swallow list");
        check(CleanVariants("a,b\r\nA,B\r\n(a,b)", false) == "a,b\r\n(a,b)", "save never re-splits existing lines");
        check(CleanVariants(" \r\n\t ", true) == "", "empty paste");

        string fixture = Path.Combine(work, "dictionary-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string file = Path.Combine(fixture, "dictionary.json");
        string original = "{\"hotwords\":[\"Example\",\"Other\"],\"rules\":[{\"to\":\"Example\",\"from\":[\"old phrase\"],\"context\":\"zh\"},{\"to\":\"Other\",\"from\":[\"different\"]}]}";
        File.WriteAllText(file, original);
        using (var dialog = new DictionaryDialog(fixture)) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.Show(); Application.DoEvents();
            foreach (Control control in dialog.Controls) if (control is DictionaryLabel && ((Label)control).AutoSize) {
                using (var graphics = control.CreateGraphics()) {
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    check(control.Width >= Math.Ceiling(graphics.MeasureString(control.Text, control.Font).Width), "label must fit actual glyphs without clipping or overlapping help");
                }
            }
            foreach (Control control in dialog.Controls) if (control is DictionaryHelpIcon)
                foreach (Control caption in dialog.Controls) if (caption.Text == "个人词典") check(caption.Right + 8 <= control.Left, "help icon must have clear space beyond title bounds");
            check(dialog.Region == null, "window corners must not use a jagged binary region");
            check(!dialog.HasNativeFrame, "dictionary must never add a system caption or resize frame");
            Console.WriteLine("DICTIONARY-SURFACE systemCorners=" + dialog.SystemCorners + " region=none size=" + dialog.ClientSize.Width + "x" + dialog.ClientSize.Height);
            var first = dialog.groups[0];
            dialog.variants.Text = "unsaved phrase";
            int confirmed = 0;
            dialog.confirmDeletion = word => { confirmed++; return DictionaryDeleteDialog.ConfirmForTest(dialog, word, false); };
            dialog.Remove();
            check(confirmed == 1 && dialog.groups.Count == 2 && dialog.deleted.Count == 0, "cancel deletion changes nothing");
            dialog.confirmDeletion = word => DictionaryDeleteDialog.ConfirmForTest(dialog, word, true);
            dialog.Remove();
            DictionaryScrollBar.SendMessage(dialog.Handle, 0x0086, new IntPtr(1), IntPtr.Zero);
            DictionaryScrollBar.SendMessage(dialog.Handle, 0x0085, new IntPtr(1), IntPtr.Zero);
            check(!dialog.HasNativeFrame && dialog.ClientSize == new Size(926, 680), "returning from delete confirmation must preserve borderless surface and exact layout");
            check(dialog.groups.Count == 1 && dialog.deleted.Count == 1 && first.Variants[0] == "unsaved phrase" && dialog.undoTimer.Enabled, "confirmed delete retains latest edits for ten seconds");
            using (var preview = new Bitmap(dialog.Width, dialog.Height)) {
                dialog.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                preview.Save(Path.Combine(work, "dictionary-delete-undo.png"));
            }
            check(dialog.Save(), "save while undo is available");
            dialog.undoBar.UndoButton.PerformClick();
            check(dialog.groups.Count == 2 && dialog.groups[0] == first && first.Context == "zh" && first.Variants[0] == "unsaved phrase" && dialog.dirty && !dialog.undoTimer.Enabled,
                "undo after save restores word, variants, context, order and unsaved state");
            check(dialog.Save(), "save restored deletion");
            dialog.Remove(); dialog.Remove();
            check(dialog.groups.Count == 0 && dialog.deleted.Count == 2, "consecutive deletion retains both pending actions");
            check(dialog.UndoDelete() && dialog.UndoDelete() && dialog.groups.Count == 2 && dialog.groups[0] == first && dialog.groups[1].Word == "Other", "consecutive undo preserves order and all words");
            dialog.Remove(); dialog.deleted[0].Deadline = dialog.undoClock.ElapsedMilliseconds - 1;
            dialog.UpdateUndoNotice();
            check(!dialog.UndoDelete() && dialog.deleted.Count == 0 && !dialog.undoTimer.Enabled && !dialog.undoBar.Visible, "expired deletion cannot be undone");
            File.WriteAllText(file, original); dialog.Reload();
            first = dialog.groups[0]; dialog.Remove(); dialog.entry.Text = "example"; dialog.Add();
            check(dialog.groups.Count == 2 && dialog.groups[0] == first && dialog.deleted.Count == 0, "adding a just-deleted word restores its data rather than creating an empty duplicate");
            File.WriteAllText(file, original); dialog.Reload();
            dialog.variants.Text = "keep  this\r\nold phrase\r\n";
            dialog.variants.ClearUndo(); dialog.variants.Select(dialog.variants.TextLength, 0);
            string before = dialog.variants.Text;
            dialog.variants.ReadPaste = () => "new。NEW。old phrase";
            dialog.variants.Paste();
            check(dialog.variants.Text == before + "new", "native paste keeps existing text and removes incoming duplicates");
            check(dialog.variants.CanUndo, "paste is undoable");
            dialog.variants.Undo(); check(dialog.variants.Text == before, "one undo restores exact original text");
            dialog.variants.Text = "old phrase"; dialog.variants.Select(0, 3);
            dialog.variants.ReadPaste = () => "new"; dialog.variants.Paste();
            check(dialog.variants.Text == "new phrase", "paste replaces selection, keeps suffix");
            dialog.list.SelectedIndex = 1;
            check(dialog.groups[0].Variants[0] == "new phrase", "switching words commits to previous word");
            dialog.variants.Text = "new phrase";
            check(!dialog.Save() && File.ReadAllText(file) == original, "conflicting variants block save without data loss");
            dialog.variants.Text = "different";
            check(dialog.Save(), "normal atomic save");
            string saved = File.ReadAllText(file);
            var data = new JavaScriptSerializer().DeserializeObject(saved) as Dictionary<string, object>;
            var rules = (object[])data["rules"];
            check(((Dictionary<string, object>)rules[0])["context"].ToString() == "zh" && !((Dictionary<string, object>)rules[1]).ContainsKey("context"), "context is preserved including absent context");
            File.WriteAllText(file, saved + " ");
            dialog.variants.Text = "changed";
            check(!dialog.Save() && File.ReadAllText(file) == saved + " " && dialog.dirty, "external modification is preserved, edits retained");
            File.WriteAllText(file, saved);
            dialog.path = Path.Combine(fixture, "missing", "dictionary.json"); dialog.loadedText = null;
            check(!dialog.Save() && dialog.dirty, "I/O failure retains unsaved edits");
            dialog.path = file; dialog.loadedText = saved;
            check(dialog.Save(), "recovery after save failure");
            for (int i = 0; i < 40; i++) dialog.groups.Add(new WordGroup { Word = "Word " + i });
            dialog.RefreshList(0); dialog.list.TopIndex = 12;
            check(dialog.list.TopIndex == 12, "native list scrolling");
            DictionaryScrollBar.SendMessage(dialog.list.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
            check(dialog.list.TopIndex < 12, "list mousewheel without native scrollbar");
            foreach (Control control in dialog.Controls) if (control is DictionaryScrollBar) ((DictionaryScrollBar)control).CheckForTest();
            var lines = new List<string>(); for (int i = 0; i < 40; i++) lines.Add("line " + i);
            dialog.variants.Text = string.Join("\r\n", lines.ToArray());
            DictionaryScrollBar.SendMessage(dialog.variants.Handle, 0x00B6, IntPtr.Zero, new IntPtr(15));
            check((int)DictionaryScrollBar.SendMessage(dialog.variants.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero) > 0, "native text scrolling");
            int top = (int)DictionaryScrollBar.SendMessage(dialog.variants.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
            DictionaryScrollBar.SendMessage(dialog.variants.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
            check((int)DictionaryScrollBar.SendMessage(dialog.variants.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero) < top, "text mousewheel without native scrollbar");
            foreach (Control control in dialog.Controls) {
                if (control is DictionaryHelpIcon) check(dialog.help.GetToolTip(control) == control.AccessibleDescription && control.AccessibleDescription.Contains("纯英文"), "help text matches accessible explanation");
                foreach (Control child in control.Controls) if (child is DictionaryScrollBar && ((DictionaryScrollBar)child).Overflows) ((DictionaryScrollBar)child).CheckForTest();
            }
            using (var preview = new Bitmap(dialog.Width, dialog.Height)) {
                dialog.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                preview.Save(Path.Combine(work, "dictionary-scroll-test.png"));
            }
            dialog.dirty = false; dialog.Close();
        }
        File.WriteAllText(file, original);
        string appRoot = Directory.GetParent(Path.GetDirectoryName(Application.ExecutablePath)).FullName;
        Action<Task> finishPreview = task => {
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted && clock.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
            check(task.IsCompleted, "preview must finish without blocking the window");
            task.GetAwaiter().GetResult();
        };
        using (var dialog = new DictionaryDialog(fixture, appRoot)) {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.Show(); Application.DoEvents();
            dialog.variants.Text = "edited spelling";
            dialog.search.Text = "DIFFERENT";
            check(dialog.visible.Count == 1 && dialog.visible[0] == 1 && dialog.current == 0 && dialog.variants.Text == "edited spelling", "filter preserves hidden draft and matches variants ignoring case");
            check(dialog.clearWordSearch.Visible, "word search exposes a clear action"); dialog.clearWordSearch.PerformClick(); check(dialog.search.TextLength == 0 && !dialog.clearWordSearch.Visible && dialog.visible.Count == 2, "word clear button restores full list"); dialog.search.Text = "DIFFERENT";
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(work, "dictionary-word-search.png")); }
            dialog.list.SelectedIndex = 0;
            check(dialog.current == 1 && dialog.groups[0].Variants[0] == "edited spelling", "filtered row selects its real word without losing edits");
            dialog.search.Text = "no matches";
            check(dialog.visible.Count == 0 && dialog.current == 1 && dialog.variants.Text == "different", "empty search result preserves editor");
            dialog.entry.Text = "EXAMPLE"; dialog.Add();
            check(dialog.groups.Count == 2 && dialog.current == 0 && dialog.search.Text == "", "duplicate add clears filter and selects existing word");
            dialog.groups.Add(new WordGroup { Word = "Only" }); dialog.RefreshList(2);
            dialog.variants.Text = "only typo";
            check(dialog.groups[2].Context == "", "adding variants to a hotword-only entry defaults to all languages");
            dialog.entry.Text = "JSON"; dialog.Add(); dialog.variants.Text = "js on";
            check(dialog.groups[3].Context == "" && dialog.scope.Text.Contains("纯英文"), "new rule defaults to all languages and shows scope");
            dialog.previewInput.Text = "js on";
            finishPreview(dialog.PreviewAsync());
            check(dialog.previewOutput.Text == "JSON" && dialog.previewOutput.TabStop && dialog.previewHits.Text.Contains("js on → JSON") && File.ReadAllText(file) == original, "preview uses unsaved rules through real Python without writing dictionary");
            dialog.previewInput.Text = "edited spelling"; finishPreview(dialog.PreviewAsync());
            check(dialog.previewOutput.Text == "edited spelling" && dialog.previewHits.Text.Contains("旧规则仅中文"), "preview explains legacy language restriction");
            dialog.previewInput.Text = "js on";
            var pending = dialog.PreviewAsync(); dialog.previewInput.Text = "changed"; finishPreview(pending);
            check(dialog.previewOutput.Text == "" && !dialog.previewOutput.TabStop, "outdated preview is discarded and empty output does not consume keyboard focus");
            dialog.search.Text = "json"; dialog.list.SelectedIndex = 0;
            dialog.confirmDeletion = word => true; dialog.Remove();
            check(dialog.Index("JSON") < 0 && dialog.UndoDelete() && dialog.groups[3].Variants[0] == "js on" && dialog.current == 3, "filtered deletion and undo preserve word, spelling, scope and index");
            while (dialog.groups.Count < 80) dialog.groups.Add(new WordGroup { Word = "Term" + dialog.groups.Count });
            dialog.RefreshList(3);
            check(dialog.counts.Text.Contains("词语 80") && dialog.counts.Text.Contains("64/64") && dialog.counts.Text.Contains("末尾 16"), "80 words show separate rescue limit without truncating storage");
            check(dialog.Save(), "save all 80 words");
            var stored = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file));
            check(((object[])stored["hotwords"]).Length == 80, "saving never drops words after limit");
            var savedRules = (object[])stored["rules"];
            check(!((Dictionary<string, object>)savedRules[3]).ContainsKey("context") && ((Dictionary<string, object>)savedRules[0])["context"].ToString() == "zh", "new rule writes no context while legacy context remains");
            dialog.previewInput.Text = "js on"; finishPreview(dialog.PreviewAsync());
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) {
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(work, "dictionary-v1-preview.png"));
            }
            dialog.dirty = false; dialog.Close();
        }
        using (var dialog = new DictionaryDialog(fixture)) {
            dialog.Reload(); dialog.previewInput.Text = "js on"; finishPreview(dialog.PreviewAsync());
            check(dialog.previewHits.Text.Contains("暂不可用") && dialog.previewButton.Enabled && dialog.Save(), "preview failure remains recoverable and does not block saving");
        }
        File.WriteAllText(file, "{broken");
        using (var dialog = new DictionaryDialog(fixture)) {
            dialog.Reload(); check(!dialog.Save() && File.ReadAllText(file) == "{broken", "malformed dictionary cannot be overwritten");
        }
        Console.WriteLine("TIDY-TEST PASS: paste / undo / conflicts / atomic save / legacy context / new bilingual rules / search mapping / hidden edits / duplicate add / filtered delete and undo / unsaved preview / stale preview / preview failure / 80 words and 64 rescue candidates / scrolling / system corners / delete confirmation / multi-clip recording samples / WAV and metadata / safe deletion / sample failure retention / compact tabs and icons");
    }

    internal static void CheckCaptureForTest(string work) { SampleCaptureDialog.CheckCaptureForTest(work); }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (!historyPage.Resolve()) { e.Cancel = true; return; }
        if (dirty) {
            var answer = MessageBox.Show(this, "还有未保存的修改，保存吗？", "个人词典",
                                         MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.Yes && !Save()) { e.Cancel = true; return; }
        }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) {
        if (disposing) {
            previewRevision++;
            if (previewWorker != null) { try { if (!previewWorker.HasExited) previewWorker.Kill(); } catch (InvalidOperationException) { } }
            help.Dispose(); undoTimer.Dispose(); deleted.Clear();
        }
        base.Dispose(disposing);
    }
}

sealed class Microphone : Form
{
    const int RenderScale = 4;
    readonly string root;
    readonly bool smokeTest;
    readonly bool streamTest, replay;
    internal const int RecognitionRoundBytes = 32000 * 180;
    readonly System.Windows.Forms.Timer capture = new System.Windows.Forms.Timer { Interval = 40 };
    readonly System.Windows.Forms.Timer textReveal = new System.Windows.Forms.Timer { Interval = 15 };
    readonly List<TextReveal> reveals = new List<TextReveal>();
    readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 16 };
    // Idle focus watch: the capsule reports whether text would land in a real input box
    // even before recording starts. Cheap when the target pair has not changed.
    readonly System.Windows.Forms.Timer focusWatch = new System.Windows.Forms.Timer { Interval = 400 };
    IntPtr idleWindow, idleFocus;
    int idleAttempts;
    bool idleWatching;
    readonly SemaphoreSlim pipeline = new SemaphoreSlim(1, 1);
    readonly bool animationsEnabled = Native.AnimationsEnabled();
    // Read by the skins so a system that asks for less motion still gets every
    // state, only without the movement.
    internal static bool ReducedMotion;
    Process worker;
    Recorder recorder;
    InteractionGuard guard;
    TextDelivery delivery;
    InPlaceText textBridge;
    ulong bridgeSegment;
    readonly ContextMenuStrip menu;
    MenuDismissHook menuGuard;
    internal int menuDismissRequests;
    // Theme state. The skin is pure drawing: switching it never touches the
    // recogniser, the microphone or the text that is already on screen.
    ThemeId theme = ThemeId.Graphite;
    ThemeId themeFrom = ThemeId.Graphite;
    double themeAt;
    double chaosVisualTime, chaosPreviousTime;
    ThemePicker picker;
    const double ThemeTransitionMs = 180;
    DictionaryDialog dictionary;
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
    long roundSampleOffset;
    string state = "loading";
    IntPtr target, targetFocus;
    Task targetProbe;
    double nextTargetProbe;
    int targetEpoch;
    int audioRoute, appliedSegments;
    bool roundBoundary;
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
        root = path; smokeTest = test == "--smoke-test"; replay = test == "--replay"; streamTest = test == "--stream-test" || replay; Text = "VoxFix Desktop"; AccessibleName = "VoxFix Desktop 麦克风";
        shortcutCycleTest = test == "--hotkey-cycle-test";
        memoryTest = test == "--memory-test" || test == "--focus-test" || test == "--idle-focus-test" || shortcutCycleTest;
        probeOnly = test == "--focus-test" || test == "--idle-focus-test";
        if (memoryTest || streamTest || smokeTest) playEarcon = (name, wait) => { };
        copyText = streamTest || memoryTest ? new Action<string>(text => copiedInTest = text) : new Action<string>(Clipboard.SetText);
        ReducedMotion = !animationsEnabled;
        theme = themeFrom = ThemeStore.Load(root);
        picker = new ThemePicker(theme, new Font("Microsoft YaHei UI", 9.5f), id => ApplyTheme(id));
        menu = CreateMenu(() => { var ignored = Dismiss(); }, ShowDictionary, Close, picker);
        // WinForms relies on deactivation to auto-close a menu, and this window never activates.
        // So the first real click outside the menu surface closes it explicitly.
        menu.Opened += (s, e) => {
            if (menuGuard != null) return;
            menuGuard = new MenuDismissHook(() => {
                // The theme picker lives in its own drop down window, so a click on
                // one of its rows is still inside the menu and must not dismiss it.
                Rectangle surface = menu.Bounds;
                if (picker != null && picker.Drop.Visible) surface = Rectangle.Union(surface, picker.Drop.Bounds);
                return surface;
            }, () => {
                menuDismissRequests++;
                if (closing || !IsHandleCreated) return;
                try { BeginInvoke(new Action(CloseMenu)); } catch (InvalidOperationException) { }
            });
        };
        menu.Closed += (s, e) => { if (menuGuard != null) { menuGuard.Dispose(); menuGuard = null; } };
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(128, 96);
        StartPosition = FormStartPosition.Manual;
        RestorePosition();
        DoubleBuffered = true; Cursor = Cursors.Hand;
        AccessibleDescription = "正在加载本地模型…";
        capture.Tick += async (s, e) => await Pump();
        textReveal.Tick += (s, e) => {
            try { RevealText(); } catch (Exception error) { Fail(error.Message); }
        };
        focusWatch.Tick += async (s, e) => await WatchIdleFocus();
        animation.Tick += async (s, e) => {
            animation.Interval = theme == ThemeId.Chaos ? 15 : 16;
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
            double visualNow = motionClock.Elapsed.TotalMilliseconds;
            chaosVisualTime += Math.Max(0, Math.Min(64, visualNow - chaosPreviousTime)) * (state == "loading" ? 1 : motion.Activity);
            chaosPreviousTime = visualNow;
            shape.Update(MemoryPill, motionClock.Elapsed.TotalMilliseconds, !animationsEnabled);
            copyFeedback = Feedback(copyFeedback, recoveryCopied ? 1 : 0);
            hoverFeedback = Feedback(hoverFeedback, hovered ? 1 : 0);
            pressFeedback = Feedback(pressFeedback, pressed && !pressCancelled && !dragged ? 1 : 0);
            readyFeedback = Feedback(readyFeedback, CopyReady ? 1 : 0);
            if (state == "idle" || state == "error") {
                if (!startOnReveal && RevealProgress >= 1 && !pressed && shape.Settled && motion.Activity < .001f && motion.Voice < .001f &&
                    copyFeedback == (recoveryCopied ? 1 : 0) && hoverFeedback == (hovered ? 1 : 0) &&
                    pressFeedback == (pressed && !pressCancelled && !dragged ? 1 : 0) && readyFeedback == (CopyReady ? 1 : 0) &&
                    themeFrom == theme) animation.Stop();
            }
            RefreshSurface();
        };
        MouseEnter += (s, e) => { hovered = true; animation.Start(); };
        MouseLeave += (s, e) => { hovered = false; pressCancelled = true; if (!Capture) pressed = false; animation.Start(); };
        MouseCaptureChanged += (s, e) => { if (!Capture) { pressed = false; animation.Start(); } };
        MouseDown += (s, e) => { CloseMenu(); if (e.Button == MouseButtons.Left) { down = Cursor.Position; origin = Location; dragged = false; pressed = true; pressCancelled = false; heldTriggered = false; pressedAt = motionClock.Elapsed.TotalMilliseconds; Capture = true; animation.Start(); } };
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
        VisibleChanged += (s, e) => { UpdateShortcutState(); if (!Visible) CloseMenu(); };
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
        FormClosing += (s, e) => {
            if (dictionary != null && !dictionary.IsDisposed) { dictionary.Close(); if (!dictionary.IsDisposed) { e.Cancel = true; return; } }
            CloseMenu(); SavePosition();
        };
        FormClosed += (s, e) => {
            closing = true; CloseMenu();
            ChaosGpu.Release();
            DropTextBridge();
            if (menuGuard != null) { menuGuard.Dispose(); menuGuard = null; }
            if (shortcut != null) shortcut.Dispose();
            capture.Stop(); capture.Dispose();
            textReveal.Stop(); textReveal.Dispose(); reveals.Clear();
            focusWatch.Stop(); focusWatch.Dispose();
            if (guard != null) guard.Dispose();
            if (recorder != null) recorder.Dispose();
            EndListening(true);
            animation.Stop(); animation.Dispose();
            delivery = null;
            StopWorker();
            if (worker != null) worker.Dispose();
            if (picker != null) { picker.Dispose(); picker = null; }
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
        if (shortcut != null) shortcut.Available = closing || (!Visible && !opening) ? 0 : 1;
    }
    async void HandleShortcut(int action) {
        try {
            if (closing) return;
            if (dictionary != null && !dictionary.IsDisposed && dictionary.RecordingBusy) { if (action == 1 || action == 2) dictionary.StopSampleRecording(); return; }
            CloseMenu();
            if (action == 2) { if (Visible || opening) await Dismiss(); return; }
            if (action != 1) return;
            if (state == "recording") { startOnReveal = false; await EndRecording(); }
            else if (opening || startOnReveal) {
                opening = startOnReveal = false; dismissRequested = true; UpdateShortcutState();
            }
            else Reveal(true);
        } catch (Exception error) { Fail(error.Message); }
    }
    // White card, one icon per row, and every row sizes to its own text so the
    // menu never carries a blank column on the right.
    internal static ContextMenuStrip CreateMenu(Action dismiss, Action dictionary, Action exit, ThemePicker picker = null) {
        var menu = new ContextMenuStrip();
        menu.ShowImageMargin = false; menu.ShowCheckMargin = false; menu.ShowItemToolTips = false;
        menu.BackColor = MicrophoneMenuRenderer.Surface;
        menu.ForeColor = MicrophoneMenuRenderer.Ink;
        menu.Font = new Font("Microsoft YaHei UI", 9.5f);
        menu.Padding = new Padding(4);
        menu.ImageScalingSize = new Size(16, 16);
        menu.Renderer = new MicrophoneMenuRenderer();
        menu.HandleCreated += (s, e) => MicrophoneMenuRenderer.SmoothCorners(menu);
        menu.Opened += (s, e) => MicrophoneMenuRenderer.SmoothCorners(menu);
        menu.Items.Add(Entry(menu.Font, "收起", "chevron", MicrophoneMenuRenderer.Ink, dismiss));
        menu.Items.Add(Entry(menu.Font, "词典", "book", MicrophoneMenuRenderer.Ink, dictionary));
        if (picker != null) menu.Items.Add(picker.Entry);
        menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 2, 0, 2) });
        menu.Items.Add(Entry(menu.Font, "退出", "power", MicrophoneMenuRenderer.Danger, exit));
        return menu;
    }

    static ToolStripMenuItem Entry(Font font, string text, string icon, Color ink, Action click) {
        var item = new ToolStripMenuItem(text);
        item.Image = MenuIcons.Get(icon, ink);
        item.Tag = ink;
        item.AutoSize = false;
        item.Padding = Padding.Empty;
        // Measure with the font the row will actually be painted in, so the card
        // never leaves a gap and never clips the label.
        int width = 30 + TextRenderer.MeasureText(text, font).Width + 14;
        item.Size = new Size(width, 34);
        item.Click += (s, e) => click();
        return item;
    }

    // Theme switching is a drawing change only. The recogniser keeps running, the
    // microphone keeps its buffer and no text is dropped.
    void ApplyTheme(ThemeId id, bool persist = true) {
        if (picker != null) picker.SetCurrent(id);
        if (id == theme) return;
        themeFrom = theme; theme = id; themeAt = motionClock.Elapsed.TotalMilliseconds;
        if (!animationsEnabled) themeFrom = id;
        if (persist) ThemeStore.Save(root, id);
        animation.Start();
        RefreshSurface();
    }
    // 0 while the old skin still owns the surface, 1 once the new one has taken over.
    double ThemeMix() {
        if (themeFrom == theme) return 1;
        double elapsed = motionClock.Elapsed.TotalMilliseconds - themeAt;
        if (!animationsEnabled || elapsed >= ThemeTransitionMs) { themeFrom = theme; return 1; }
        return DictationMotion.Ease(elapsed / ThemeTransitionMs);
    }

    void ShowDictionary() {
        if (dictionary == null || dictionary.IsDisposed) {
            dictionary = new DictionaryDialog(root);
            dictionary.PrepareRecording = async () => {
                if (state == "recording") await EndRecording();
                if (recorder != null || state == "busy" || state == "loading") throw new IOException("语音输入正在准备或收尾，请稍后录制样本。");
            };
        }
        dictionary.SetHistoryNotice(lastHistoryNotice);
        if (!dictionary.Visible) dictionary.Show(); else dictionary.BringToFront();
        dictionary.Activate();
    }
    void CloseMenu() { if (menu.Visible) menu.Close(); }
    async Task Dismiss() {
        CloseMenu();
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
            return !inputFocused && (state == "idle" || state == "error" || state == "busy");
        }
    }
    bool CopyReady { get { return !restarting && recovery.Length > 0 && !transcribing && !speechDetected && state != "busy" && state != "loading"; } }
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
        if (theme == ThemeId.Chaos) {
            float folds = 16 * (1 - (float)Math.Max(0, Math.Min(1, shape.Value)));
            size = new SizeF(size.Width + folds, size.Height + folds);
        }
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
    string lastHistoryNotice = "";
    void ApplyReply(Dictionary<string, object> reply) {
        if (reply.ContainsKey("history_notice")) { lastHistoryNotice = Convert.ToString(reply["history_notice"]); if (dictionary != null && !dictionary.IsDisposed) dictionary.SetHistoryNotice(lastHistoryNotice); }
        if (reply.ContainsKey("level")) level = Convert.ToSingle(reply["level"]);
        if (reply.ContainsKey("speaking")) speechDetected = Convert.ToBoolean(reply["speaking"]);
        if (reply.ContainsKey("transcribing")) transcribing = Convert.ToBoolean(reply["transcribing"]);
        if (reply.ContainsKey("segments") && delivery != null) {
            int index = 0;
            foreach (Dictionary<string, object> segment in (System.Collections.IEnumerable)reply["segments"]) {
                if (index++ < appliedSegments) continue;
                QueueSegment(segment, true);
                appliedSegments = index;
            }
        } else ApplyText(Convert.ToString(reply["text"]));
        if (delivery != null && reply.ContainsKey("preview_segment") && reply["preview_segment"] != null)
            QueueSegment((Dictionary<string, object>)reply["preview_segment"], false);
        string preview = reply.ContainsKey("preview") ? Convert.ToString(reply["preview"]) : "";
        if (draft != preview) { draft = preview; UpdateMemoryShape(); }
        UpdateMemoryShape();
    }
    static string SegmentKey(Dictionary<string, object> segment) {
        return Convert.ToString(segment["route"]) + ":" + Convert.ToString(segment.ContainsKey("preview_start") ? segment["preview_start"] : segment["start"]);
    }
    void QueueSegment(Dictionary<string, object> segment, bool final) {
        if (delivery == null || delivery.Cancelled || closing) return;
        string key = SegmentKey(segment);
        var pending = reveals.Find(value => value.Owner == delivery && value.Key == key);
        if (pending != null && pending.Final && !final) return;
        if (pending == null) {
            pending = new TextReveal { Key = key, Owner = delivery, Started = motionClock.Elapsed.TotalMilliseconds };
            reveals.Add(pending);
        }
        pending.Segment = new Dictionary<string, object>(segment); pending.Final = final;
        RevealText();
        if (reveals.Count > 0) textReveal.Start();
    }
    void RevealText() {
        if (closing || delivery == null || delivery.Cancelled) { reveals.Clear(); textReveal.Stop(); return; }
        while (reveals.Count > 0) {
            var pending = reveals[0];
            if (pending.Owner != delivery) { reveals.RemoveAt(0); continue; }
            double now = motionClock.Elapsed.TotalMilliseconds;
            if (now < pending.Next) return;
            int route = Convert.ToInt32(pending.Segment["route"]);
            bool memory = delivery.Paused.Length != 0 || backupRoutes.Contains(route);
            DraftText previous; delivery.Drafts.TryGetValue(pending.Key, out previous);
            string wanted = Convert.ToString(pending.Segment["text"]).TrimStart();
            string shown = previous == null ? "" : previous.Text;
            if (previous != null && previous.Separator.Length > 0 && shown.StartsWith(previous.Separator, StringComparison.Ordinal)) shown = shown.Substring(previous.Separator.Length);
            // Only a host without a draft interface reads previews here; a writable one
            // still goes through ApplySegment so its draft can be corrected in place.
            bool append = !memory && (pending.AppendOnly || (previous != null && previous.AppendOnly));
            if (append && previous != null) {
                if (pending.Final) {
                    // Keeping the visible prefix avoids retyping for punctuation the final
                    // only inserts; a real rewrite falls through to the diff below, which
                    // deletes and retypes on this channel.
                    wanted = FinalAppendTarget(shown, wanted) ?? wanted;
                } else {
                    wanted = PreviewAppendTarget(previous, shown, wanted);
                }
            }
            if (!append && !pending.Final && wanted.Length < shown.Length) { reveals.RemoveAt(0); continue; }
            // Each new burst (a preview commit or the final tail) types from its own start;
            // an old clock would let the catch-up window dump the whole remainder at once.
            if (append && wanted != pending.Target) { pending.Target = wanted; pending.Started = now; }
            // Unpublished preview time is not visible typing lag.
            bool starting = pending.Final && shown.Length == 0;
            if (starting) pending.Started = now;
            string next = memory || !animationsEnabled ? wanted
                : starting ? TextReveal.First(wanted)
                : append ? TextReveal.Append(shown, wanted, now - pending.Started)
                : TextReveal.Step(shown, wanted, now - pending.Started);
            var update = new Dictionary<string, object>(pending.Segment); update["text"] = next;
            bool complete = next == wanted;
            if (append) AppendReveal(pending, previous, next, complete);
            else ApplySegment(update, pending.Final && complete);
            // Remember the full preview, not the slice that is on screen, so the next
            // decode is compared against everything the recogniser produced.
            if (!append && !pending.Final) {
                DraftText current;
                if (delivery.Drafts.TryGetValue(pending.Key, out current) && current.Candidate.Length == 0) current.Candidate = PlainText(wanted);
            }
            // Unknown interfaces reveal finals by guarded append, never simulated keystroke corrections.
            if (!append && !memory && pending.Final && !complete) {
                delivery.Drafts.TryGetValue(pending.Key, out previous);
                if (previous != null && !previous.InPlace && delivery.Paused.Length == 0) {
                    pending.AppendOnly = true; AppendReveal(pending, previous, next, false);
                }
            }
            if (complete || memory) reveals.RemoveAt(0);
            else { pending.Next = now + (append ? TextReveal.AppendStepMs : 35); return; }
        }
        textReveal.Stop();
    }
    // Text that punctuation or gesture marks may sit between; previews carry their own
    // punctuation, so comparisons run on the letters alone.
    static bool Plain(char value) { return !char.IsPunctuation(value) && !char.IsWhiteSpace(value); }
    static string PlainText(string text) {
        var plain = new StringBuilder(text.Length);
        foreach (char value in text) if (Plain(value)) plain.Append(value);
        return plain.ToString();
    }
    // Index in the punctuated final just past the already shown plain characters.
    static int IndexAfterPlain(string text, int plain) {
        int seen = 0, index = 0;
        while (index < text.Length && seen < plain) { if (Plain(text[index])) seen++; index++; }
        return index;
    }
    // On this channel a draft is published as soon as it exists and corrected by deleting
    // the changed tail, so the first character arrives with the first decode.
    static string PreviewAppendTarget(DraftText draft, string shown, string candidate) {
        string plain = PlainText(candidate);
        draft.Candidate = plain;
        return plain.Length == 0 ? shown : plain;
    }
    // The final may punctuate inside text that is already visible; that mark cannot be
    // inserted retroactively, so only the part after the visible prefix is appended.
    static string FinalAppendTarget(string shown, string final) {
        // The tail is typed slice by slice, so it is usually already a prefix of the final.
        if (final.StartsWith(shown, StringComparison.Ordinal)) return final;
        string plain = PlainText(final), committed = PlainText(shown);
        if (!plain.StartsWith(committed, StringComparison.Ordinal)) return null;
        return shown + final.Substring(IndexAfterPlain(final, committed.Length));
    }
    void AppendReveal(TextReveal pending, DraftText previous, string text, bool complete) {
        text = previous.Separator + text;
        string shown = previous.Text;
        int common = CommonPrefix(shown, text);
        int remove = shown.Length - common;
        if (remove > 0) {
            if (!pending.Final && remove > TextReveal.ReviseLimit) return;
            if (!DeleteTail(remove, pending.Final ? text : "")) return;
            delivery.Delivered = delivery.Delivered.Substring(0, delivery.Delivered.Length - remove);
            delivery.Latest = delivery.Accounted = delivery.Latest.Substring(0, delivery.Latest.Length - remove);
        }
        string suffix = text.Substring(common);
        if (suffix.Length > 0) ApplyText(delivery.Latest + suffix);
        previous.Text = text;
        if (complete && pending.Final) delivery.Drafts.Remove(pending.Key);
    }
    // Only for hosts that answer "no text store". The caret is at the end of text this
    // round wrote, and every delete re-checks foreground and focus; anything unexpected
    // stops the rewrite and leaves the final in the copy cache.
    bool DeleteTail(int count, string final) {
        if (Native.GetForegroundWindow() != target || targetFocus == IntPtr.Zero || Native.Focus(target) != targetFocus || EditingGestureDown()) {
            Pause("输入位置已变化，已停止修订并保留最终识别结果。");
            if (final.Length > 0) KeepRecovery("\n[该句最终识别] " + final);
            return false;
        }
        var inputs = new Native.Input[count * 2];
        for (int i = 0; i < count; i++) { inputs[i * 2] = Native.KeyInput(8, false); inputs[i * 2 + 1] = Native.KeyInput(8, true); }
        uint sent = Native.SendToTarget(target, targetFocus, inputs, () => delivery.Paused.Length == 0 && !EditingGestureDown());
        if (sent != (uint)inputs.Length) {
            Pause("改字未完整送出，已停止修订并保留最终识别结果。");
            if (final.Length > 0) KeepRecovery("\n[该句最终识别] " + final);
            return false;
        }
        return true;
    }
    async Task DrainReveals() {
        while (reveals.Count > 0 && !closing && delivery != null && !delivery.Cancelled) {
            RevealText();
            if (reveals.Count > 0) await Task.Delay(15);
        }
    }
    void ApplySegment(Dictionary<string, object> segment, bool final) {
        if (delivery == null || delivery.Cancelled) return;
        int route = Convert.ToInt32(segment["route"]);
        string key = SegmentKey(segment);
        string text = Convert.ToString(segment["text"]).TrimStart();
        bool memory = backupRoutes.Contains(route) || delivery.Paused.Length != 0;
        DraftText previous;
        delivery.Drafts.TryGetValue(key, out previous);
        if (previous == null) {
            if (!final && memory) return;
            string separator = delivery.Latest.Length > 0 && text.Length > 0 &&
                delivery.Latest[delivery.Latest.Length - 1] < 128 && Char.IsLetterOrDigit(delivery.Latest[delivery.Latest.Length - 1]) &&
                text[0] < 128 && Char.IsLetterOrDigit(text[0]) ? " " : "";
            if (final && memory) {
                string addition = Convert.ToString(segment["text"]);
                if (roundBoundary) addition = separator + text;
                if (addition.Length > 0) roundBoundary = false;
                ApplyText(delivery.Latest + addition, memory); return;
            }
            previous = new DraftText { Separator = separator, Segment = ++bridgeSegment }; delivery.Drafts.Add(key, previous);
        }
        if (text.Length > 0) text = previous.Separator + text;
        if (!final && (text.Length < previous.Text.Length || OnlyDraftPunctuationChanged(previous.Text, text))) return;
        if (text.Length > 0) roundBoundary = false;
        if (memory) {
            if (final) {
                if (text.StartsWith(previous.Text, StringComparison.Ordinal)) ApplyText(delivery.Latest + text.Substring(previous.Text.Length), true);
                else KeepRecovery("\n[该句最终识别] " + text);
            }
        } else {
            ReviseText(previous, text, final);
        }
        if (final) delivery.Drafts.Remove(key);
    }
    void DropTextBridge() {
        if (textBridge != null) { textBridge.Dispose(); textBridge = null; }
    }
    void ReviseText(DraftText previous, string text, bool final) {
        if (delivery.Latest != delivery.Accounted || !delivery.Latest.EndsWith(previous.Text, StringComparison.Ordinal) ||
            !delivery.Delivered.EndsWith(previous.Text, StringComparison.Ordinal))
            throw new IOException("无法确认本轮草稿位置，已停止修订。");
        if (Native.GetForegroundWindow() != target || targetFocus == IntPtr.Zero || Native.Focus(target) != targetFocus || EditingGestureDown()) {
            Pause("输入位置已变化，已停止修订并保留最终识别结果。");
            if (final) KeepRecovery("\n[该句最终识别] " + text); return;
        }
        if (text.Length == 0 && previous.Text.Length == 0) return;
        if (textBridge == null || !textBridge.Matches(target, targetFocus)) {
            DropTextBridge(); textBridge = new InPlaceText(Handle, target, targetFocus);
        }
        int status = textBridge.Update(previous.Segment, previous.Text, text, final);
        if (status == 1) {
            int oldLength = previous.Text.Length;
            delivery.Delivered = delivery.Delivered.Substring(0, delivery.Delivered.Length - oldLength) + text;
            delivery.Latest = delivery.Accounted = delivery.Latest.Substring(0, delivery.Latest.Length - oldLength) + text;
            previous.Text = text; previous.InPlace = true; previous.AppendOnly = false;
        } else if (status == 2 && !previous.InPlace) {
            if (textBridge.NoTextStore) {
                // The host answered with no writable text store: this segment can only be
                // appended. The bridge never took ownership of it, so it stays for the
                // host-level decision and is dropped with the round.
                previous.AppendOnly = true;
                // A segment that never showed anything can still arrive whole; a partially
                // shown one is finished by RevealText, which knows what is already visible.
                if (final) {
                    if (previous.Text.Length == 0) ApplyText(delivery.Latest + text);
                    else KeepRecovery("\n[该句最终识别] " + text);
                }
            } else if (final) {
                // First call installs the hook before the receiver exists; a final that
                // lands in that window is appended instead of retried.
                ApplyText(delivery.Latest + text);
            }
        } else if (status != 2 || final) {
            Pause("输入位置已变化或改字未确认，已停止修订并保留最终识别结果。");
            if (final) KeepRecovery("\n[该句最终识别] " + text);
        }
    }
    internal static int CommonPrefix(string previous, string text) {
        int length = 0;
        while (length < previous.Length && length < text.Length && previous[length] == text[length]) length++;
        return length;
    }
    internal static bool OnlyDraftPunctuationChanged(string previous, string text) {
        if (previous.Length == 0 || previous == text) return false;
        int common = CommonPrefix(previous, text);
        foreach (string tail in new[] { previous.Substring(common), text.Substring(common) })
            foreach (char value in tail) if ("，。！？；：、!?".IndexOf(value) < 0) return false;
        return true;
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
                if (recovery.Length != 0 || recoveryCopied) throw new Exception("New round must clear old cache and copied flag after backend starts");
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
        textReveal.Stop();
        if (delivery != null && !delivery.Cancelled) foreach (var pending in reveals) {
            if (pending.Owner != delivery || !pending.Final) continue;
            DraftText previous; delivery.Drafts.TryGetValue(pending.Key, out previous);
            string text = Convert.ToString(pending.Segment["text"]).TrimStart();
            string shown = previous == null ? "" : previous.Text;
            if (previous != null && shown.StartsWith(previous.Separator, StringComparison.Ordinal)) shown = shown.Substring(previous.Separator.Length);
            KeepRecovery(text.StartsWith(shown, StringComparison.Ordinal) ? text.Substring(shown.Length) : "\n[该句最终识别] " + text);
        }
        reveals.Clear();
        DropTextBridge();
        if (!smokeTest && !streamTest && !memoryTest) try {
            File.WriteAllText(Path.Combine(root, "work", "recognition-error.txt"), DateTime.UtcNow.ToString("o") + " / " + Assembly.GetExecutingAssembly().GetName().Version + " / " + state + " / audioBytes=" + sentBytes + " / " + message);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
        startOnReveal = false;
        capture.Stop();
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
    // These hosts reuse their HWND across internal focus changes; UIA may expose only the shell.
    static bool CoarseClass(IntPtr window) {
        return CoarseWindowClass(Native.ClassOf(window));
    }
    static bool CoarseWindowClass(string name) {
        return name.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)
            || name.StartsWith("Qt", StringComparison.Ordinal)
            || name == "MozillaWindowClass"
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
    static TargetKind ConfirmReadOnlyTarget(TargetKind primary, Func<TargetKind> confirm) {
        if (primary != TargetKind.ReadOnly) return primary;
        return confirm() == TargetKind.Editable ? TargetKind.Editable : primary;
    }
    static TargetKind Classify(IntPtr window, IntPtr focus, IntPtr own) {
        if (window == IntPtr.Zero || window == own) return TargetKind.ReadOnly;
        // Chromium may expose only a read-only document until UIA activates its input tree.
        if (CoarseClass(window))
            return ConfirmReadOnlyTarget(ClassifyCoarse(window), () => ClassifyAutomation(window, focus));
        return ClassifyAutomation(window, focus);
    }
    static TargetKind ClassifyAutomation(IntPtr window, IntPtr focus) {
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
        if (dictionary != null && !dictionary.IsDisposed && dictionary.RecordingBusy) return;
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
                Status("recording", editable ? "正在录音，草稿即时输入 · 点击麦克风结束" : "正在录音，未上屏文字将直接显示为复制按钮 · 右键可结束录音"); capture.Start();
                if (dismissRequested) await EndRecording();
            } catch (Exception error) { Fail(error.Message); }
            return;
        }
        await EndRecording();
    }
    async Task BeginStream() {
        textReveal.Stop(); reveals.Clear();
        DropTextBridge();
        draft = ""; speechDetected = transcribing = false;
        stalledWindows.Clear();
        recordingId = Guid.NewGuid().ToString("N"); delivery = new TextDelivery(); sentBytes = 0; roundSampleOffset = 0;
        audioRoute = appliedSegments = 0; backupRoutes.Clear();
        roundBoundary = false;
        targetEpoch++; nextTargetProbe = 0;
        await StartRecognitionRound();
        recovery = ""; recoveryCopied = false; copyFeedback = readyFeedback = 0;
        UpdateMemoryShape(); RefreshSurface();
    }
    async Task<Dictionary<string, object>> Exchange(object request) {
        await worker.StandardInput.WriteLineAsync(json.Serialize(request));
        await worker.StandardInput.FlushAsync();
        var reply = await Reply();
        if (reply.ContainsKey("error")) {
            if (delivery != null && reply.ContainsKey("segments")) ApplyReply(reply);
            if (reply.ContainsKey("preview")) draft = Convert.ToString(reply["preview"]);
            throw new IOException(Convert.ToString(reply["error"]));
        }
        if (!reply.ContainsKey("id") || Convert.ToString(reply["id"]) != recordingId) throw new IOException("识别结果编号不匹配，已停止输入。");
        return reply;
    }
    async Task StartRecognitionRound() {
        var reply = await Exchange(new { op = "start", id = recordingId, preview = true, sample_offset = roundSampleOffset });
        if (Convert.ToString(reply["event"]) != "started") throw new IOException("无法开始语音识别。");
        sentBytes = appliedSegments = 0;
    }
    async Task Feed(byte[] audio) {
        int route = audioRoute, offset = 0;
        while (offset < audio.Length && !closing) {
            if (sentBytes == RecognitionRoundBytes) {
                ApplyReply(await Exchange(new { op = "stop", id = recordingId }));
                await DrainReveals();
                roundSampleOffset += sentBytes / 2;
                await StartRecognitionRound();
                roundBoundary = true;
            }
            int length = Math.Min(Math.Min(32000, audio.Length - offset), RecognitionRoundBytes - sentBytes);
            var reply = await Exchange(new { op = "audio", id = recordingId, route = route, pcm = Convert.ToBase64String(audio, offset, length) });
            sentBytes += length; offset += length;
            if (!closing) ApplyReply(reply);
        }
    }
    void ApplyText(string text, bool forceMemory = false) {
        if (delivery == null || delivery.Cancelled || closing) return;
        delivery.Accept(text);
        if (forceMemory || delivery.Paused.Length != 0) { KeepRecovery(delivery.TakePending()); return; }
        SendPendingText();
    }
    void SendPendingText() {
        if (delivery.Paused.Length != 0) { KeepRecovery(delivery.TakePending()); return; }
        string suffix = delivery.Pending;
        if (suffix.Length == 0 || closing) return;
        if (Native.GetForegroundWindow() != target || targetFocus == IntPtr.Zero || Native.Focus(target) != targetFocus || EditingGestureDown()) {
            Pause("输入位置已变化，继续录音并保存未上屏文字。");
            KeepRecovery(delivery.TakePending()); return;
        }
        var inputs = Native.TextInputs(suffix);
        uint sent = Native.SendToTarget(target, targetFocus, inputs, () => delivery.Paused.Length == 0 && !EditingGestureDown());
        if (sent != inputs.Length) {
            delivery.Sent(suffix.Substring(0, Math.Min(suffix.Length, (int)((sent + 1) / 2))));
            Pause("文字未完整送出，继续录音并保存剩余文字。");
            KeepRecovery(delivery.TakePending()); return;
        }
        delivery.Sent(suffix);
    }
    void Pause(string reason, bool interaction = false) {
        if (closing || delivery == null) return;
        DropTextBridge();
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
        capture.Stop(); Status("busy", "正在补全尾字…");
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
                textReveal.Stop(); reveals.Clear();
                await Exchange(new { op = "cancel", id = recordingId });
                return;
            }
            await Feed(tail);
            var result = await Exchange(new { op = "stop", id = recordingId });
            if (!closing) {
                ApplyReply(result);
                await DrainReveals();
            }
        } catch (Exception error) { Fail(error.Message); }
        finally {
            if (acquired) pipeline.Release();
            DropTextBridge();
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
        capture.Stop();
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
                    new Dictionary<string, object> { { "route", 100 }, { "start", 0 }, { "text", "T" } },
                    new Dictionary<string, object> { { "route", 101 }, { "start", 1600 }, { "text", "N" } }
                } }
            };
            ApplyReply(delayed); await Task.Delay(30);
            ApplyReply(delayed); await Task.Delay(30);
            if (second.Text != "DF N" || !recovery.EndsWith("T") || MemoryPill)
                throw new Exception("Late old audio escaped fallback or copy control disappeared: text=" + second.Text + "; cache=" + recovery + "; capsule=" + MemoryPill);
            delivery = null; Status("idle", "输入框恢复测试通过");
        }
    }
    static async Task AwaitTestText(TextBox box, string expected) {
        var clock = Stopwatch.StartNew();
        while (box.Text != expected && clock.ElapsedMilliseconds < 1000) await Task.Delay(10);
    }
    async Task CheckFastDraft(TextBox box) {
        DropTextBridge();
        box.Text = "原文："; box.SelectionStart = box.TextLength;
        delivery = new TextDelivery(); recovery = ""; backupRoutes.Clear(); appliedSegments = 0;
        var first = new Dictionary<string, object> { { "route", 0 }, { "start", 0 }, { "text", "开头" } };
        var firstClock = Stopwatch.StartNew();
        while (delivery.Delivered.Length == 0 && firstClock.ElapsedMilliseconds < 1000) { ApplySegment(first, false); await Task.Delay(10); }
        firstClock.Stop();
        if (delivery.Delivered != "开头") throw new Exception("First draft bridge: " + (textBridge == null ? "closed" : textBridge.Diagnostic) + "; paused=" + delivery.Paused);
        first["text"] = "开头短句"; ApplySegment(first, false);
        await AwaitTestText(box, "原文：开头短句");
        first["text"] = "开头好句"; ApplySegment(first, false);
        await AwaitTestText(box, "原文：开头好句");
        if (box.Text != "原文：开头好句") throw new Exception("Small tail revisions must stay responsive");
        first["text"] = "开头好句！"; ApplySegment(first, false);
        if (delivery.Delivered != "开头好句") throw new Exception("Punctuation-only previews must not churn the target");
        var reply = new Dictionary<string, object> { { "segments", new object[] { first } } };
        ApplyReply(reply); ApplyReply(reply);
        await AwaitTestText(box, "原文：开头好句！");
        if (box.Text != "原文：开头好句！") throw new Exception("Final punctuation must arrive once");
        var second = new Dictionary<string, object> { { "route", 0 }, { "start", 100 }, { "text", "这是一段已经显示的长正文，金额15元，后面这些正确内容一直保留。" } };
        string visible = box.Text + (string)second["text"];
        ApplySegment(second, false); await AwaitTestText(box, visible);
        second["text"] = "这是一段原位纠正的长正文，金额1.5元，后面这些正确内容一直保留。";
        ApplySegment(second, false); ApplySegment(second, false);
        await Task.Delay(30);
        string revised = "原文：开头好句！" + second["text"];
        if (box.Text != revised || delivery.Latest != revised.Substring(3) || box.SelectionStart != box.TextLength)
            throw new Exception("Contextual corrections must replace in place, preserve the correct suffix and keep the caret at the end: box=" + box.Text + "; latest=" + delivery.Latest + "; caret=" + box.SelectionStart + "/" + box.TextLength + "; paused=" + delivery.Paused + "; bridge=" + (textBridge == null ? "closed" : textBridge.Diagnostic));
        reply["segments"] = new object[] { first, second };
        ApplyReply(reply); ApplyReply(reply);
        string expected = "原文：开头好句！" + second["text"];
        await AwaitTestText(box, expected);
        if (box.Text != expected || box.SelectionStart != box.TextLength || delivery.Drafts.Count != 0)
            throw new Exception("Final must correct the current segment exactly once, keep the prefix and leave the caret at the end");
        DropTextBridge(); delivery = new TextDelivery(); recovery = ""; box.Text = "原文："; box.SelectionStart = box.TextLength;
        ApplySegment(new Dictionary<string, object> { { "route", 0 }, { "start", 200 }, { "text", "hello" } }, true);
        roundBoundary = true;
        ApplySegment(new Dictionary<string, object> { { "route", 0 }, { "start", 300 }, { "text", "world" } }, false);
        ApplySegment(new Dictionary<string, object> { { "route", 0 }, { "start", 300 }, { "text", "world" } }, true);
        await AwaitTestText(box, "原文：hello world");
        if (box.Text != "原文：hello world") throw new Exception("English boundaries must survive a preview across rounds");
        string unchanged = box.Text; backupRoutes.Add(7);
        var offTarget = new Dictionary<string, object> { { "route", 7 }, { "start", 400 }, { "text", "离框草稿" } };
        ApplySegment(offTarget, false); offTarget["text"] = "离框定稿"; ApplySegment(offTarget, true);
        await Task.Delay(30);
        if (box.Text != unchanged || recovery != "离框定稿") throw new Exception("Off-target drafts must wait; finals must be cached");
        delivery.Cancelled = true; offTarget["route"] = 0; offTarget["text"] = "迟到结果"; ApplySegment(offTarget, true);
        if (delivery.Latest.Contains("迟到")) throw new Exception("Cancelled rounds must ignore late finals");
        DropTextBridge(); delivery = new TextDelivery(); recovery = ""; backupRoutes.Clear(); appliedSegments = 0;
        File.WriteAllText(Path.Combine(root, "work", "microphone-fast-draft-test.txt"), "FAST-DRAFT-TEST PASS: first draft bridge=" + firstClock.ElapsedMilliseconds + "ms / contextual corrections in place / correct suffix retained / caret at voice tail / no injected correction keys / punctuation coalescing / final exact text and deduplication / English round boundary / off-target cache / cancellation");
    }
    async Task CheckTextReveal(TextBox box) {
        if (TextReveal.Step("", "甲乙丙丁", 0) != "甲" || TextReveal.Step("", "甲乙丙丁", 240) != "甲乙丙丁" ||
            TextReveal.Step("", "\ud83d\ude00e\u0301中", 0) != "\ud83d\ude00" || TextReveal.Step("\ud83d\ude00", "\ud83d\ude00e\u0301中", 0) != "\ud83d\ude00e\u0301")
            throw new Exception("Reveal must advance text elements and bound accumulated lag");
        if (TextReveal.Append("", "甲乙丙丁", 0) != "甲" || TextReveal.Append("", "甲乙丙丁", 1800) != "甲乙丙丁" ||
            TextReveal.First("甲乙丙丁") != "甲" || TextReveal.First("\ud83d\ude00e\u0301中") != "\ud83d\ude00")
            throw new Exception("Append reveal must type one text element per beat and still finish a long segment");
        reveals.Clear(); textReveal.Stop();
        box.Text = "原文："; box.SelectionStart = box.TextLength;
        delivery = new TextDelivery(); recovery = ""; backupRoutes.Clear(); appliedSegments = 0;
        var frames = new List<string>(); var clock = Stopwatch.StartNew();
        EventHandler changed = (s, e) => frames.Add(clock.ElapsedMilliseconds + "\t" + box.Text);
        box.TextChanged += changed;
        try {
            var segment = new Dictionary<string, object> { { "route", 0 }, { "start", 0 }, { "text", "逐字出现跟随说话" } };
            QueueSegment(segment, false);
            if (animationsEnabled && box.Text == "原文：逐字出现跟随说话") throw new Exception("Draft must not jump out as a whole block");
            await Task.Delay(120);
            segment["text"] = "连贯出现跟随说话节奏，金额1.5元。";
            QueueSegment(segment, true);
            QueueSegment(new Dictionary<string, object> { { "route", 0 }, { "start", 100 }, { "text", "下一句不能越过前句。" } }, true);
            await DrainReveals();
            string expected = "原文：连贯出现跟随说话节奏，金额1.5元。下一句不能越过前句。";
            if (box.Text != expected || box.SelectionStart != box.TextLength || delivery.Drafts.Count != 0 ||
                delivery.Delivered != expected.Substring(3) || (animationsEnabled && frames.Count < 4) || clock.ElapsedMilliseconds > 1500)
                throw new Exception("Paced finals must coalesce, remain ordered, finish promptly and account only applied text");
            DropTextBridge(); textBridge = new InPlaceText(IntPtr.Zero, target, targetFocus);
            segment["start"] = 150; segment["text"] = "兼容通道逐字追加。";
            int beforeFrames = frames.Count;
            QueueSegment(segment, true); await DrainReveals();
            expected += "兼容通道逐字追加。";
            await AwaitTestText(box, expected);
            long compatSpan = frames.Count > beforeFrames ? long.Parse(frames[frames.Count - 1].Split('\t')[0]) - long.Parse(frames[beforeFrames].Split('\t')[0]) : 0;
            if (box.Text != expected || (animationsEnabled && (frames.Count - beforeFrames < 5 || compatSpan < 400 || compatSpan > 1400)) || delivery.Drafts.Count != 0)
                throw new Exception("Unsupported text interfaces must type finalized text a beat at a time: actual=" + box.Text + "; expected=" + expected + "; frames=" + (frames.Count - beforeFrames) + "; spanMs=" + compatSpan + "; drafts=" + delivery.Drafts.Count + "; paused=" + delivery.Paused);
            segment["start"] = 155; segment["text"] = "等待草稿时还没有任何文字真正显示出来";
            QueueSegment(segment, false);
            if (box.Text != expected) throw new Exception("Unsupported previews must not be committed as final text");
            // Replay a final arriving after a busy UI delayed the unpublished preview queue.
            Thread.Sleep(280);
            string delayedFinal = "等待结束以后也必须逐步显示这一段定稿。";
            segment["text"] = delayedFinal; beforeFrames = frames.Count;
            QueueSegment(segment, true);
            if (animationsEnabled && box.Text == expected + delayedFinal)
                throw new Exception("Unpublished preview wait must not make a compatibility final appear as one whole block");
            await DrainReveals(); expected += delayedFinal; await AwaitTestText(box, expected);
            long delayedSpan = frames.Count > beforeFrames ? long.Parse(frames[frames.Count - 1].Split('\t')[0]) - long.Parse(frames[beforeFrames].Split('\t')[0]) : 0;
            if (box.Text != expected || (animationsEnabled && (frames.Count - beforeFrames < 10 || delayedSpan < 800 || delayedSpan > 2600)))
                throw new Exception("Delayed compatibility final must be typed out, not pasted as one block: frames=" + (frames.Count - beforeFrames) + "; spanMs=" + delayedSpan);
            // A host with no text store publishes the draft immediately and fixes it by
            // deleting the changed tail, so the first character arrives with the first
            // decode instead of waiting for a confirmation.
            segment["start"] = 170; segment["text"] = "今天下午要去公园";
            QueueSegment(segment, false); await DrainReveals(); await Task.Delay(60);
            await AwaitTestText(box, expected + "今天下午要去公园");
            if (box.Text != expected + "今天下午要去公园")
                throw new Exception("First decode must be published on an append-only host: actual=" + box.Text);
            segment["text"] = "今天下午要去公司";
            QueueSegment(segment, false); await DrainReveals(); await Task.Delay(60);
            await AwaitTestText(box, expected + "今天下午要去公司");
            if (box.Text != expected + "今天下午要去公司")
                throw new Exception("A changed tail must be deleted and retyped: actual=" + box.Text);
            int beforeStreamFinal = frames.Count;
            segment["text"] = "今天下午，要去公司。";
            QueueSegment(segment, true); await DrainReveals(); await Task.Delay(60);
            expected += "今天下午要去公司。"; await AwaitTestText(box, expected);
            if (box.Text != expected || delivery.Drafts.Count != 0 || (animationsEnabled && frames.Count - beforeStreamFinal < 1))
                throw new Exception("Final must keep the visible prefix and append only the rest: actual=" + box.Text + "; expected=" + expected);
            // A final that rewrites visible words is corrected the same way, because this
            // channel is allowed to delete text it wrote this round.
            segment["start"] = 180; segment["text"] = "今天上午开会";
            QueueSegment(segment, false); await DrainReveals(); await Task.Delay(60);
            await AwaitTestText(box, expected + "今天上午开会");
            segment["text"] = "明天中午开会，讨论。";
            QueueSegment(segment, true); await DrainReveals(); await Task.Delay(60);
            expected += "明天中午开会，讨论。"; await AwaitTestText(box, expected);
            if (box.Text != expected || delivery.Drafts.Count != 0 || recovery.Length != 0)
                throw new Exception("A rewritten final must replace the segment instead of being cached: actual=" + box.Text + "; recovery=" + recovery);
            segment["start"] = 160; segment["text"] = "只读切换后剩余文字必须留在缓存。";
            QueueSegment(segment, true); await Task.Delay(45);
            box.ReadOnly = true; string beforeReadOnly = box.Text;
            await DrainReveals();
            string accepted = beforeReadOnly.Substring(expected.Length);
            if (box.Text != beforeReadOnly || accepted + recovery != (string)segment["text"] || delivery.Paused.Length == 0)
                throw new Exception("Read-only transition must stop paced append and retain the exact unsent final suffix: shown=" + accepted.Length + "; recovery=" + recovery.Length + "; final=" + ((string)segment["text"]).Length + "; paused=" + delivery.Paused);
            box.ReadOnly = false; recovery = ""; backupRoutes.Clear(); audioRoute = 0;
            DropTextBridge();
            box.Text = "左侧原文替换这段右侧原文"; box.Select(4, 4);
            delivery = new TextDelivery();
            segment["start"] = 175; segment["text"] = "新的逐字语音";
            // Warm the bridge before this assertion so it exercises range ownership, not append fallback.
            textBridge = new InPlaceText(Handle, target, targetFocus);
            textBridge.Update(++bridgeSegment, "", "", false); await Task.Delay(40);
            QueueSegment(segment, true); await DrainReveals();
            if (box.Text != "左侧原文新的逐字语音右侧原文" || box.SelectionStart != 10 || box.SelectionLength != 0)
                throw new Exception("Paced selection replacement must retain both sides and leave the caret at the voice tail");
            box.SelectionStart = box.TextLength;
            segment["start"] = 200; segment["text"] = "用户编辑后这些尾字应保留在缓存";
            QueueSegment(segment, false); await Task.Delay(45);
            Pause("测试用户编辑", true); string stopped = box.Text;
            QueueSegment(segment, true); await DrainReveals();
            if (box.Text != stopped || recovery.Length == 0) throw new Exception("Pending reveal escaped user-edit protection or lost final text");
            delivery.Paused = ""; segment["route"] = audioRoute; segment["start"] = 300;
            QueueSegment(segment, true); delivery.Cancelled = true; RevealText();
            if (reveals.Count != 0) throw new Exception("Cancelled recording retained queued text");
            File.WriteAllLines(Path.Combine(root, "work", "text-reveal-frames.txt"), frames.ToArray());
            File.WriteAllText(Path.Combine(root, "work", "text-reveal-test.txt"), "TEXT-REVEAL-TEST PASS: progressive frames=" + frames.Count + " / append typing spanMs=" + compatSpan + " / delayed compatibility final spanMs=" + delayedSpan + " / coalesced latest candidate and visible correction / ordered final segments / one element per beat on append-only hosts / read-only transition retains exact unsent suffix / paced selection replacement / caret and delivery ledger / user-edit cache / cancellation / Unicode text elements / bounded in-place catch-up / reduced motion follows system setting");
        } finally {
            box.TextChanged -= changed; textReveal.Stop(); reveals.Clear(); DropTextBridge();
            delivery = new TextDelivery(); recovery = ""; backupRoutes.Clear(); appliedSegments = 0;
        }
    }
    // Diagnostic: replay a wav through the production pipeline into the focused host.
    // Only a WeChat Qt window is accepted, and nothing is sent or clicked afterwards.
    static string ProcessNameOf(IntPtr window) {
        try { uint pid; Native.WindowProcessId(window, out pid); using (var process = Process.GetProcessById((int)pid)) return process.ProcessName; }
        catch (Exception) { return ""; }
    }
    async Task RunReplay() {
        string path = Path.Combine(root, "work", "replay.wav");
        if (!File.Exists(path)) throw new IOException("缺少回放音频：" + path);
        // Wait for the user to bring WeChat forward instead of stealing the foreground.
        var wait = Stopwatch.StartNew();
        IntPtr window = IntPtr.Zero; string name = "";
        while (wait.ElapsedMilliseconds < 120000) {
            window = Native.GetForegroundWindow();
            name = ProcessNameOf(window);
            if (window != IntPtr.Zero && window != Handle && Native.CharacterMessageTarget(Native.ClassOf(window), name)) break;
            await Task.Delay(250);
        }
        if (window == IntPtr.Zero || window == Handle || !Native.CharacterMessageTarget(Native.ClassOf(window), name))
            throw new Exception("回放只写微信前台窗口，当前是 " + Native.ClassOf(window) + "/" + name);
        target = window; targetFocus = Native.Focus(window);
        if (targetFocus == IntPtr.Zero) throw new Exception("微信输入框没有焦点，回放未开始。");
        byte[] file = File.ReadAllBytes(path);
        int at = 12, data = -1, length = 0;
        while (at + 8 <= file.Length) {
            string id = Encoding.ASCII.GetString(file, at, 4);
            int size = BitConverter.ToInt32(file, at + 4);
            if (id == "data") { data = at + 8; length = Math.Min(size, file.Length - data); break; }
            if (size < 0) break;
            at += 8 + size + (size & 1);
        }
        if (data < 0 || length < 3200) throw new IOException("回放音频没有可用的 PCM 数据。");
        await BeginStream();
        guard = new InteractionGuard(OwnsSurface, reason => Pause("回放期间检测到输入：" + reason));
        var frames = new List<string>(); var clock = Stopwatch.StartNew();
        int fed = 0;
        for (int offset = data; offset < data + length; offset += 3200) {
            int size = Math.Min(3200, data + length - offset);
            var part = new byte[size]; Array.Copy(file, offset, part, 0, size);
            await Feed(part); fed += size;
            double due = fed / 2.0 / 16000 * 1000;
            if (due > clock.ElapsedMilliseconds) await Task.Delay((int)(due - clock.ElapsedMilliseconds));
            if (delivery == null || delivery.Cancelled) break;
            frames.Add(clock.ElapsedMilliseconds + "\t" + delivery.Latest);
        }
        ApplyReply(await Exchange(new { op = "stop", id = recordingId }));
        await DrainReveals();
        frames.Add(clock.ElapsedMilliseconds + "\t" + delivery.Latest);
        File.WriteAllLines(Path.Combine(root, "work", "replay-frames.txt"), frames.ToArray());
        File.WriteAllText(Path.Combine(root, "work", "replay-test.txt"), "REPLAY " + name + " / focus=" + targetFocus.ToInt64().ToString("X") +
            " / paused=[" + delivery.Paused + "] / recovery=[" + recovery + "] / text=" + delivery.Latest);
        Close();
    }
    async Task RunStreamTest() {
        if (replay) { await RunReplay(); return; }
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
            bool editable = false;
            for (int attempt = 0; attempt < 3 && !editable; attempt++) {
                editable = await Task.Run(() => HasEditableTarget());
                if (!editable) await Task.Delay(100);
            }
            if (!editable) throw new Exception("Editable field was not detected");
            if (!Native.CharacterMessageTarget("Qt51514QWindowIcon", "Weixin") || !Native.CharacterMessageTarget("Qt51514QWindowIcon", "WeChat") ||
                Native.CharacterMessageTarget("Chrome_WidgetWin_1", "WeChatAppEx") || Native.CharacterMessageTarget("Qt51514QWindowIcon", "Other"))
                throw new Exception("Character-message compatibility must be limited to WeChat Qt windows");
            string directText = "微信输入测试：你好，中文 ABC 123";
            var directInputs = Native.TextInputs(directText);
            if (Native.SendCharacterMessages(target, targetFocus, directInputs) != directInputs.Length || box.Text != "原文：" + directText)
                throw new Exception("Character-message path lost Chinese or punctuation");
            string unchangedDirect = box.Text;
            if (Native.SendCharacterMessages(target, IntPtr.Zero, Native.TextInputs("不得输入")) != 0 || box.Text != unchangedDirect)
                throw new Exception("Character-message path ignored a lost focus");
            int allowedChecks = 0;
            if (Native.SendCharacterMessages(target, targetFocus, Native.TextInputs("甲乙"), () => ++allowedChecks == 1) != 2 || box.Text != unchangedDirect + "甲")
                throw new Exception("Character-message path must stop and report exactly the accepted prefix");
            if (Native.SendCharacterMessages(target, targetFocus, Native.TextInputs("\r\n")) != 0 || box.Text != unchangedDirect + "甲")
                throw new Exception("Character-message path must not interpret recognized newlines as send keys");
            box.Text = "原文："; box.SelectionStart = box.TextLength;
            box.ReadOnly = true;
            if (await Task.Run(() => HasEditableTarget())) throw new Exception("Read-only field accepted as editable");
            box.ReadOnly = false;
            await BeginStream(); Status("busy", "流式输入测试");
            guard = new InteractionGuard(OwnsSurface, reason => Pause("Test user input: " + reason));
            var draftSegment = new Dictionary<string, object> { { "route", 0 }, { "start", 0 }, { "text", "金额15元" } };
            await CheckFastDraft(box);
            await CheckTextReveal(box);
            guard.Dispose(); guard = null;
            // Reset only the dedicated test target before the real-model portion.
            if (guard != null) { guard.Dispose(); guard = null; }
            await Exchange(new { op = "cancel", id = recordingId });
            box.Text = "原文："; box.SelectionStart = box.TextLength;
            await BeginStream();
            guard = new InteractionGuard(OwnsSurface, reason => Pause("Test user input: " + reason));
            byte[] source = File.ReadAllBytes(Path.Combine(root, "work", "p0", "wav", "new-short-0.wav"));
            // The real-time long-session contract is tested over the worker protocol;
            // this desktop test needs one endpoint and actual Unicode delivery.
            byte[] wav = new byte[source.Length + 32000]; Array.Copy(source, wav, source.Length);
            var clock = Stopwatch.StartNew(); int first = -1; bool beforeEndpoint = false;
            for (int offset = 44; offset < wav.Length; offset += 3200) {
                int length = Math.Min(3200, wav.Length - offset); var part = new byte[length]; Array.Copy(wav, offset, part, 0, length);
                await Feed(part);
                if (delivery.Delivered.Length > 0 && first < 0) { first = (int)clock.ElapsedMilliseconds; beforeEndpoint = appliedSegments == 0; }
                await Task.Delay(Math.Max(1, (int)((offset - 44 + length) / 32) - (int)clock.ElapsedMilliseconds));
            }
            if (first < 0 || first >= clock.ElapsedMilliseconds) throw new Exception("No text before stop: latest=" + delivery.Latest + "; paused=" + delivery.Paused + "; foreground=" + Native.GetForegroundWindow() + "; target=" + target + "; focus=" + Native.Focus(target) + "; expectedFocus=" + targetFocus);
            if (!beforeEndpoint) throw new Exception("Draft must arrive before the VAD endpoint");
            if (!delivery.Delivered.Contains("你好")) throw new Exception("Endpoint text incomplete: delivered=" + delivery.Delivered.Length + "; latest=" + delivery.Latest.Length + "; paused=" + delivery.Paused);
            var final = await Exchange(new { op = "stop", id = recordingId }); ApplyReply(final);
            await Task.Delay(100);
            if (!box.Text.Contains("你好") || box.Text != "原文：" + delivery.Delivered || delivery.Paused.Length != 0) throw new Exception("Streaming injection/guard failed: " + box.Text + " / " + delivery.Paused);
            draftSegment["start"] = 999999; draftSegment["text"] = "没有到账";
            ApplySegment(draftSegment, false); await Task.Delay(50);
            string original = box.Text;
            ApplyText(delivery.Latest); await Task.Delay(50);
            if (box.Text != original) throw new Exception("Final result was duplicated");
            var input = Native.TextInputs("Z");
            for (int i = 0; i < input.Length; i++) input[i].data.keyboard.extra = UIntPtr.Zero;
            Native.SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Native.Input))); await Task.Delay(50);
            if (delivery.Paused.Length == 0) throw new Exception("User edits did not pause injection");
            original = box.Text; ApplyText(delivery.Latest + "不得输入"); await Task.Delay(50);
            if (box.Text != original) throw new Exception("Input continued after user editing");
            draftSegment["text"] = "已经到账。";
            ApplySegment(draftSegment, false); ApplySegment(draftSegment, true); await Task.Delay(50);
            if (box.Text != original || !recovery.Contains("已经到账。")) throw new Exception("Revision after user editing changed the target or lost the final");
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
            await BeginStream();
            delivery.Accept("已上屏正文"); delivery.Sent("已上屏正文");
            KeepRecovery("缓存保留"); string keptRecovery = recovery, priorId = recordingId;
            TextDelivery keptDelivery = delivery; audioRoute = 7; backupRoutes.Add(5); int keptEpoch = targetEpoch;
            sentBytes = RecognitionRoundBytes - 100;
            await Feed(new byte[3200]);
            var boundary = await Exchange(new { op = "stop", id = recordingId }); ApplyReply(boundary);
            if (recordingId != priorId || roundSampleOffset != RecognitionRoundBytes / 2 || sentBytes != 3100 || Convert.ToInt32(boundary["samples"]) != 1550 ||
                delivery != keptDelivery || delivery.Delivered != "已上屏正文" || recovery != keptRecovery ||
                audioRoute != 7 || !backupRoutes.Contains(5) || targetEpoch != keptEpoch)
                throw new Exception("Round rollover must preserve every audio byte, source group, sample offset, visible text, cache, route and edit protection");
            await StartRecognitionRound();
            await FinishRecording(new byte[0]);
            File.WriteAllText(Path.Combine(root, "work", "VoxFix Desktop-stream-test.txt"), "PASS: fast drafts / contextual corrections in place / correct following text retained / caret at voice tail / no injected correction keys / exact numbers and negation / original prefix preserved / first draft " + first + "ms / real SenseVoice / direct text bridge and append compatibility / final deduplication / interaction and focus guards / cache copy and cancellation / round rollover retains unaligned packet tail, delivery, cache, route and edit epoch / no transcript window");
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
        // Supersample the vector outline so translucent edges cover fractional screen pixels.
        using (var surface = new Bitmap(128 * RenderScale, 96 * RenderScale, System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
            double mix = ThemeMix();
            using (var ink = Graphics.FromImage(surface)) {
                ink.ScaleTransform(RenderScale, RenderScale);
                if (mix >= 1) DrawContent(ink, theme);
                else DrawContent(ink, themeFrom);
            }
            // A theme switch hands the material over in place: the outgoing skin
            // stays put while the incoming one fades over it.
            if (mix > 0 && mix < 1) {
                using (var layer = new Bitmap(128 * RenderScale, 96 * RenderScale, System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
                    using (var layerInk = Graphics.FromImage(layer)) {
                        layerInk.ScaleTransform(RenderScale, RenderScale);
                        DrawContent(layerInk, theme);
                    }
                    using (var ink = Graphics.FromImage(surface)) {
                        using (var attributes = new System.Drawing.Imaging.ImageAttributes()) {
                            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)mix });
                            ink.DrawImage(layer, new Rectangle(0, 0, 128 * RenderScale, 96 * RenderScale), 0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                }
            }
            var saved = g.Save();
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(surface, new Rectangle(0, 0, 128, 96), 0, 0, surface.Width, surface.Height, GraphicsUnit.Pixel);
            g.Restore(saved);
        }
    }
    void DrawContent(Graphics g, ThemeId skin) {
        float progress = RevealProgress;
        if (progress >= 1) { DrawSkin(g, skin); return; }
        using (var bitmap = new Bitmap(128, 96)) {
            using (var content = Graphics.FromImage(bitmap)) DrawSkin(content, skin);
            DrawReveal(g, bitmap, progress);
        }
    }
    void DrawSkin(Graphics g, ThemeId skin) {
        DrawShape(g, shape.Value, restarting ? "recording" : state, recoveryCopied, copyFeedback, hoverFeedback, pressFeedback,
            motion.Activity, motion.Voice, motion.Level, animationsEnabled ? (skin == ThemeId.Chaos ? chaosVisualTime : motionClock.Elapsed.TotalMilliseconds) : 0,
            restarting || (state == "recording" && !inputFocused && recovery.Length == 0),
            !restarting && (transcribing || speechDetected || state == "busy"), readyFeedback,
            // Keep one microphone visible throughout the idle morph in either direction.
            (state == "idle" || state == "error") && recovery.Length == 0 && !transcribing && !restarting, skin);
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
        float activity = 0, float voice = 0, float level = 0, double now = 0, bool pending = false, bool transcribing = false, float ready = 1, bool stashed = false, ThemeId theme = ThemeId.Graphite) {
        g.Clear(Color.Transparent);
        var context = new ThemeContext {
            State = state, Shape = shape, Activity = activity, Voice = voice, Level = level,
            Hover = hover, Press = press, Success = success, Ready = ready, Pending = pending,
            Transcribing = transcribing, Stashed = stashed, Copied = copied, Now = now,
            Error = state == "error", Reduced = ReducedMotion
        };
        DrawThemeLayer(g, shape, context, theme);
    }
    // Skin layer only: silhouette, material and the listening mark. It is kept apart
    // from the shared copy affordance so a theme switch can cross fade the skin
    // without drawing the copy control twice.
    internal static void DrawThemeLayer(Graphics g, double shape, ThemeContext context, ThemeId theme, bool affordance = true) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // A capsule keeps the circle's area, including spring overshoot and reversal.
        SizeF size = ShapeSize(shape);
        float scale = 1 - .025f * context.Press;
        var surfaceState = g.Save();
        g.TranslateTransform(64, 48); g.ScaleTransform(scale, scale); g.TranslateTransform(-64, -48);
        float width = size.Width, height = size.Height;
        float blend = (float)Math.Max(0, Math.Min(1, shape));
        using (var outline = theme == ThemeId.Graphite ? GraphiteOutline(width, height) : ThemeArt.Outline(theme, width, height, shape, context)) {
            if (theme == ThemeId.Graphite) DrawMaterial(g, outline, outline.GetBounds(), context.Hover, context.Press, context.Activity, context.Now, context.Error);
            else ThemeArt.PaintBody(g, theme, outline, width, height, context);
            var saved = g.Save(); g.SetClip(outline);
            if (affordance && blend > 0) using (var pill = new Bitmap(106 * RenderScale, 42 * RenderScale)) using (var ink = Graphics.FromImage(pill)) {
                ink.ScaleTransform(RenderScale, RenderScale);
                if (theme == ThemeId.Chaos) ChaosArt.Status(ink, context);
                else DrawMemory(ink, context.Copied, context.Success, context.Hover, context.Press, false, context.Activity, context.Level, context.Now,
                    context.State == "busy" || context.State == "loading", context.Pending, context.Voice, context.Transcribing, context.Ready, false, context.Stashed);
                DrawFade(g, pill, 11, 27, Math.Max(0, 2 * blend - 1), 106, 42, theme == ThemeId.Ink);
            }
            var motif = g.Save();
            float motifSize = Math.Min(width, height);
            g.TranslateTransform(64 - 29 * (float)shape - motifSize / 2, 48 - motifSize / 2);
            g.ScaleTransform(motifSize / 72, motifSize / 72);
            using (var circle = new Bitmap(72 * RenderScale, 72 * RenderScale)) using (var ink = Graphics.FromImage(circle)) {
                ink.ScaleTransform(RenderScale, RenderScale);
                if (theme == ThemeId.Graphite) DrawListening(ink, context.State, context.Activity, context.Voice, context.Level, context.Now, false, 1.65f - .05f * blend);
                else ThemeArt.PaintMotif(ink, theme, context, 1.65f - .05f * blend);
                DrawFade(g, circle, 0, 0, context.Stashed || context.Pending || context.State == "busy" ? 1 : context.Activity + (1 - context.Activity) * Math.Max(0, 1 - 2 * blend), 72, 72);
            }
            g.Restore(motif);
            g.Restore(saved);
        }
        g.Restore(surfaceState);
    }
    internal static GraphicsPath GraphiteOutline(float width, float height) {
        var outline = new GraphicsPath();
        float x = 64 - width / 2, y = 48 - height / 2;
        float diameter = Math.Min(width, height);
        if (width >= height) {
            outline.AddArc(x + .5f, y + .5f, diameter - 1, diameter - 1, 90, 180);
            outline.AddArc(x + width - diameter + .5f, y + .5f, diameter - 1, diameter - 1, 270, 180);
        } else {
            outline.AddArc(x + .5f, y + .5f, diameter - 1, diameter - 1, 180, 180);
            outline.AddArc(x + .5f, y + height - diameter + .5f, diameter - 1, diameter - 1, 0, 180);
        }
        outline.CloseFigure();
        return outline;
    }
    static void DrawFade(Graphics g, Bitmap image, int x, int y, float opacity, int width = 0, int height = 0, bool darkInk = false) {
        if (opacity <= 0) return;
        using (var attributes = new System.Drawing.Imaging.ImageAttributes()) {
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
            if (darkInk) { matrix.Matrix00 = .17f; matrix.Matrix11 = .29f; matrix.Matrix22 = .38f; }
            attributes.SetColorMatrix(matrix);
            g.DrawImage(image, new Rectangle(x, y, width == 0 ? image.Width : width, height == 0 ? image.Height : height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
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
        using (var line = new Pen(Color.FromArgb((int)(232 * (1 - success) * (1 - activity) * (pending || transcribing ? 0 : 1)), 232, 234, 233), 1.5f)) {
            line.LineJoin = LineJoin.Round; line.StartCap = line.EndCap = LineCap.Round;
            g.DrawLines(line, new[] { new PointF(14, 12), new PointF(12, 12), new PointF(12, 22), new PointF(14, 22) });
            using (var page = new GraphicsPath()) {
                page.AddArc(15, 14, 3, 3, 180, 90); page.AddArc(20, 14, 3, 3, 270, 90);
                page.AddArc(20, 22, 3, 3, 0, 90); page.AddArc(15, 22, 3, 3, 90, 90); page.CloseFigure(); g.DrawPath(line, page);
            }
        }
        using (var line = new Pen(Color.FromArgb((int)(255 * success * (1 - activity) * (pending || transcribing ? 0 : 1)), 220, 236, 226), 1.8f)) {
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
        for (int i = 0; i < 2; i++) {
            float phase = milliseconds == 0 ? i * .5f : (float)((milliseconds / 1400 + i * .5) % 1);
            float diameter = 48 + 21 * phase;
            using (var ring = new Pen(Color.FromArgb((int)(100 * activity * (1 - phase)), 233, 241, 255), 1.3f))
                g.DrawEllipse(ring, 36 - diameter / 2, 36 - diameter / 2, diameter, diameter);
        }
        // Short start/stop work must not flash a waveform or reset the listening rings.
        float bars = voice;
        Color inkColor = Color.FromArgb(245, 246, 250);
        var glyph = g.Save();
        g.TranslateTransform(36, 36); g.ScaleTransform(glyphScale, glyphScale); g.TranslateTransform(-36, -36);
        float barOpacity = Math.Max(0, bars * 2 - 1);
        if (barOpacity > .001f) DrawVoice(g, 36, 36, barOpacity, level, milliseconds, false, inkColor);
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
            uint sent = Native.SendToTarget(window, focus, inputs);
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
        if (args.Length > 0 && args[0] == "--stop-hotkey-host") {
            IntPtr host = Native.FindWindow(null, HotkeyHost.WindowTitle);
            if (host != IntPtr.Zero) Native.PostMessage(host, 0x10, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        if (args.Length > 0 && (args[0] == "--hotkey-host" || args[0] == "--hotkey-host-test")) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args[0] == "--hotkey-host-test") {
                try { HotkeyHost.Check(root); }
                catch (Exception error) { File.WriteAllText(Path.Combine(root, "work", "hotkey-host-test.txt"), "FAIL: " + error); Environment.ExitCode = 1; }
                return;
            }
            bool hostCreated;
            using (var hostMutex = new Mutex(true, "Local\\VoxFix Desktop hotkey host-" + Native.DesktopName(), out hostCreated))
                if (hostCreated) using (var host = new HotkeyHost(root)) Application.Run(host);
            return;
        }
        if (args.Length > 0 && args[0] == "--dictionary-capture-test") {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(Path.Combine(root, "work"));
            try { DictionaryDialog.CheckCaptureForTest(Path.Combine(root, "work")); }
            catch (Exception error) { File.WriteAllText(Path.Combine(root, "work", "dictionary-capture-test.txt"), "FAIL: " + error); Environment.ExitCode = 1; }
            return;
        }
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
        if (args.Length > 0 && args[0] == "--menu-test") {
            // Renders the menu offscreen with the first row hovered, so the
            // layout can be checked without a window ever reaching the desktop.
            Directory.CreateDirectory(Path.Combine(root, "work"));
            using (var host = new Form {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
                Size = new Size(160, 160), BackColor = Color.White }) {
                using (var picker = new ThemePicker(ThemeId.Ink, new Font("Microsoft YaHei UI", 9.5f), id => { }))
                using (var preview = Microphone.CreateMenu(() => { }, () => { }, () => { }, picker)) {
                    host.Show();
                    preview.Show(host, new Point(0, 0));
                    Application.DoEvents();
                    preview.Items[0].Select();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(preview.Width, preview.Height)) {
                        preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(root, "work", "menu.png"));
                    }
                    picker.Drop.Show(new Point(-20000, -20000));
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(picker.Drop.Width, picker.Drop.Height)) {
                        picker.Drop.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(root, "work", "theme-picker.png"));
                    }
                    picker.Drop.Close();
                    Console.WriteLine("MENU-TEST OK " + preview.Width + "x" + preview.Height +
                                      " rows=" + preview.Items.Count +
                                      " picker=" + picker.Drop.Width + "x" + picker.Drop.Height);
                    preview.Close();
                }
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--theme-test") {
            ThemeArt.CheckInk();
            ChaosArt.Check(root);
            // Renders every skin in the states that matter, offscreen, so the
            // whole family can be reviewed on one sheet.
            Directory.CreateDirectory(Path.Combine(root, "work"));
            ThemeId[] skins = { ThemeId.Graphite, ThemeId.Ink, ThemeId.Chaos };
            string[] labels = { "空闲", "聆听", "讲话", "缓存可复制", "转写中", "已复制", "错误" };
            using (var sheet = new Bitmap(128 * labels.Length, 96 * skins.Length + 20 * skins.Length))
            using (var ink = Graphics.FromImage(sheet))
            using (var font = new Font("Microsoft YaHei UI", 9f)) {
                ink.Clear(Color.FromArgb(38, 43, 50));
                for (int t = 0; t < skins.Length; t++) for (int s = 0; s < labels.Length; s++) {
                    double shape = s >= 3 ? 1 : 0;
                    string state = s == 6 ? "error" : s == 4 ? "recording" : s == 2 ? "recording" : "idle";
                    float activity = s == 1 || s == 2 || s == 4 ? 1 : 0;
                    float voice = s == 2 ? 1 : 0;
                    using (var cell = new Bitmap(128, 96)) using (var layer = Graphics.FromImage(cell)) {
                        Microphone.DrawShape(layer, shape, state, s == 5, s == 5 ? 1 : 0, 0, 0,
                            activity, voice, .7f, 600, false, s == 4, s >= 3 ? 1 : 0, false, skins[t]);
                        ink.DrawImageUnscaled(cell, s * 128, t * 116);
                    }
                    using (var label = new SolidBrush(Color.FromArgb(212, 218, 224)))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center })
                        ink.DrawString(ThemeCatalog.Of(skins[t]).Name + " · " + labels[s], font, label,
                            new RectangleF(s * 128, t * 116 + 96, 128, 18), format);
                }
                sheet.Save(Path.Combine(root, "work", "theme-sheet.png"));
            }
            Console.WriteLine("THEME-TEST OK skins=" + skins.Length);
            return;
        }
        if (args.Length > 0 && args[0] == "--dictionary-test") {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            // Renders the editor offscreen: the window is parked outside the
            // desktop bounds, so nothing flashes and no focus visibly moves.
            Directory.CreateDirectory(Path.Combine(root, "work"));
            try { using (var dialog = new DictionaryDialog(root)) {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-20000, -20000);
                dialog.Show();
                Application.DoEvents();
                using (var preview = new Bitmap(dialog.Width, dialog.Height)) {
                    dialog.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(root, "work", "dictionary-dialog.png"));
                }
                string scratch = Path.Combine(root, "work", "dictionary-roundtrip.json");
                Console.WriteLine("DICTIONARY-TEST " + dialog.DumpForTest());
                dialog.SaveToForTest(scratch);
                dialog.Close();
            }
                DictionaryDialog.CheckForTest(Path.Combine(root, "work"));
                File.WriteAllText(Path.Combine(root, "work", "dictionary-test.txt"), "PASS: dictionary offscreen render / roundtrip / new bilingual rules / legacy context / search mapping / hidden edits / duplicate add / filtered delete and undo / unsaved shared-rule preview / stale preview / preview failure / 80 words with 64 rescue candidates / native paste and undo / save protections / scrolling / system corners / ten-second undo / multi-clip recording samples / PCM WAV and full transcripts / failed-save retention / recoverable deletion / compact tabs and vector icons");
            } catch (Exception error) {
                Environment.ExitCode = 1;
                File.WriteAllText(Path.Combine(root, "work", "dictionary-test.txt"), "FAIL: " + error);
                Console.WriteLine(error.ToString());
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
                        // A consumed modifier release must not let the next Win+H reach Windows.
                        foreach (int modifier in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) {
                            var dispatcher = (Control)typeof(DictationShortcut).GetField("dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hook);
                            var keys = (ShortcutKeys)typeof(DictationShortcut).GetField("keys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hook);
                            dispatcher.Invoke(new Action(() => { bool ignored; keys.Process(modifier, true, 0, out ignored); }));
                            if (Native.SendInput((uint)press.Length, press, Marshal.SizeOf(typeof(Native.Input))) != press.Length) throw new Exception("Modifier recovery injection failed");
                            if (!WaitShortcut(invoked) || Volatile.Read(ref last) != 1 || Volatile.Read(ref calls) != 3) throw new Exception("Stale modifier must not leak Win+H to Windows: " + modifier);
                            Interlocked.Exchange(ref calls, 2);
                        }
                        // Ctrl+C belongs to the target app: the hook must never capture it,
                        // even with a stale copy bit set and cached text ready.
                        hook.Available = 3;
                        var copy = new[] { Native.KeyInput(0xA2, false), Native.KeyInput(0x43, false), Native.KeyInput(0x43, true), Native.KeyInput(0xA2, true) };
                        for (int i = 0; i < copy.Length; i++) copy[i].data.keyboard.extra = UIntPtr.Zero;
                        if (Native.SendInput((uint)copy.Length, copy, Marshal.SizeOf(typeof(Native.Input))) != copy.Length) throw new Exception("Ctrl+C test injection failed");
                        var passed = Stopwatch.StartNew();
                        while (Volatile.Read(ref edits) < 1 && passed.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(5); }
                        if (Volatile.Read(ref edits) != 1 || Volatile.Read(ref calls) != 2) throw new Exception("Ctrl+C must always pass through to the target app");
                        // Replay the actual mouse callback without scrolling or clicking the user's page.
                        var mouseCallback = (Delegate)typeof(InteractionGuard).GetField("mouse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(editGuard);
                        int guarded = Volatile.Read(ref edits);
                        IntPtr mouseData = Marshal.AllocHGlobal(32);
                        try {
                            Marshal.Copy(new byte[32], 0, mouseData, 32);
                            foreach (int message in new[] { 0x20A, 0x20E, 0x200 }) {
                                mouseCallback.DynamicInvoke(0, new IntPtr(message), mouseData);
                                if (Volatile.Read(ref edits) != guarded) throw new Exception("Scrolling must not pause text delivery when the caret stays in the input box");
                            }
                            foreach (int message in new[] { 0x201, 0x204, 0x207, 0x20B }) mouseCallback.DynamicInvoke(0, new IntPtr(message), mouseData);
                            if (Volatile.Read(ref edits) != guarded + 4) throw new Exception("Mouse button presses must still guard text delivery");
                        } finally { Marshal.FreeHGlobal(mouseData); }
                        hook.Available = 1;
                        for (int i = 0; i < 10; i++) {
                            if (Native.SendInput((uint)copy.Length, copy, Marshal.SizeOf(typeof(Native.Input))) != copy.Length)
                                throw new Exception("Repeated Ctrl+C test injection failed");
                            var wait = Stopwatch.StartNew();
                            while (Volatile.Read(ref edits) < guarded + 5 + i && wait.ElapsedMilliseconds < 1000) {
                                Application.DoEvents(); Thread.Sleep(5);
                            }
                            if (Volatile.Read(ref edits) != guarded + 5 + i || Volatile.Read(ref calls) != 2)
                                throw new Exception("Ctrl+C must reach the target app on every press");
                        }
                    } finally {
                        var release = new[] { Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true), Native.KeyInput(0x1B, true), Native.KeyInput(0x43, true), Native.KeyInput(0xA2, true) };
                        Native.SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(Native.Input)));
                    }
                  }
                }
                File.WriteAllText(Path.Combine(root, "work", "microphone-hotkey-test.txt"), "PASS: dedicated hook thread / real SendInput Win+H / six stale modifiers recovered without leaking Win+H / key repeat suppressed / real Escape / 11 Ctrl+C presses always reach the target app with no captured action / shortcuts bypass edit guard / replayed wheel and movement preserve delivery / replayed mouse buttons still guard delivery / Win release masked / clean unhook; no model, audio or clipboard access");
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
            if (shortcutKeys.Process(0x43, true, 3, out swallowed) != 0 || swallowed) throw new Exception("Ctrl+C must pass through even when cached text is ready");
            shortcutKeys.Process(0x43, false, 0, out swallowed);
            if (shortcutKeys.Process(0x43, true, 1, out swallowed) != 0 || swallowed) throw new Exception("Ctrl+C without cached text must pass");
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
                var menuSurface = new Rectangle(100, 100, 96, 92);
                if (!MenuDismissHook.Outside(menuSurface, new Point(60, 140)) || MenuDismissHook.Outside(menuSurface, new Point(120, 120)))
                    throw new Exception("Only clicks outside the menu surface may dismiss it");
                if (shell.menu.Items.Count != 5 || shell.menu.Items[0].Text != "收起" ||
                    shell.menu.Items[1].Text != "词典" ||
                    shell.menu.Items[2].Text != "主题" ||
                    !(shell.menu.Items[3] is ToolStripSeparator) || shell.menu.Items[4].Text != "退出")
                    throw new Exception("Menu must contain dismiss, dictionary, theme, separator and exit");
                foreach (ToolStripItem item in shell.menu.Items)
                    if (!(item is ToolStripSeparator) && item.Image == null)
                        throw new Exception("Every menu row must carry an icon");
                if (shell.picker == null || shell.picker.Drop.Items.Count != ThemeCatalog.All.Length)
                    throw new Exception("Theme picker must offer every catalogued skin");
                foreach (ToolStripItem item in shell.picker.Drop.Items)
                    if (item is ThemeChoiceItem && ((ThemeChoiceItem)item).Current != (((ThemeChoiceItem)item).Id == shell.theme))
                        throw new Exception("Theme picker must mark exactly the skin in use");
                // Every skin must have its own silhouette: a circle and a capsule drawn
                // in that skin must differ from the graphite pair.
                foreach (ThemeDefinition definition in ThemeCatalog.All) {
                    ThemeId skin = definition.Id;
                    double skinCircleArea = 0, skinCapsuleArea = 0;
                    using (var circle = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(circle)) {
                        DrawShape(ink, 0, "idle", false, 0, 0, 0, theme: skin);
                        for (int x = 0; x < 128; x++) for (int y = 0; y < 96; y++) skinCircleArea += circle.GetPixel(x, y).A / 255.0;
                    }
                    using (var capsule = new Bitmap(128, 96)) using (var ink = Graphics.FromImage(capsule)) {
                        DrawShape(ink, 1, "idle", false, 0, 0, 0, theme: skin);
                        for (int x = 0; x < 128; x++) for (int y = 0; y < 96; y++) skinCapsuleArea += capsule.GetPixel(x, y).A / 255.0;
                    }
                    if (skinCircleArea < 800 || skinCapsuleArea < 800)
                        throw new Exception("Skin " + skin + " must draw a visible body in both shapes");
                    if (Math.Abs(skinCircleArea - skinCapsuleArea) / Math.Max(skinCircleArea, skinCapsuleArea) > .35)
                        throw new Exception("Skin " + skin + " must keep the circle and the capsule comparable in size");
                }
                if (ThemeCatalog.All.Length != 3 || ThemeCatalog.Parse("chaos") != ThemeId.Chaos || ThemeCatalog.Parse("resonance") != ThemeId.Graphite ||
                    ThemeCatalog.Parse("refraction") != ThemeId.Graphite || ThemeCatalog.Parse("weave") != ThemeId.Graphite)
                    throw new Exception("The catalog must contain graphite, ink and chaos");
                // Switching the skin is a drawing change only: text, state and the
                // recogniser must survive it untouched.
                string keptRecovery = shell.recovery, keptState = shell.state;
                bool keptDelivery = shell.delivery != null;
                shell.ApplyTheme(ThemeId.Graphite, false);
                shell.ApplyTheme(ThemeId.Ink, false);
                if (shell.recovery != keptRecovery || shell.state != keptState || (shell.delivery != null) != keptDelivery)
                    throw new Exception("Switching the skin must not touch the cache, the state or the delivery");
                if (shell.theme != ThemeId.Ink)
                    throw new Exception("A skin switch must land on the chosen skin");
                if (shell.ThemeMix() >= 1)
                    throw new Exception("A skin switch must hand the surface over instead of snapping");
                shell.ApplyTheme(ThemeId.Graphite, false);
                // Persisted choice must survive a reload, and a broken file must fall back.
                shell.themeFrom = ThemeId.Graphite;
                string themeFolder = Path.Combine(root, "work", "theme-test");
                Directory.CreateDirectory(themeFolder);
                try {
                    ThemeStore.Save(themeFolder, ThemeId.Chaos);
                    if (ThemeStore.Load(themeFolder) != ThemeId.Chaos) throw new Exception("The chaos skin must be remembered");
                    ThemeStore.Save(themeFolder, ThemeId.Ink);
                    if (ThemeStore.Load(themeFolder) != ThemeId.Ink) throw new Exception("The chosen skin must be remembered");
                    File.WriteAllText(ThemeStore.PathOf(themeFolder), "{ broken");
                    if (ThemeStore.Load(themeFolder) != ThemeId.Graphite) throw new Exception("A broken skin file must fall back to the default");
                    File.WriteAllText(ThemeStore.PathOf(themeFolder), "{\"theme\":\"ink\"}");
                    if (ThemeStore.Load(themeFolder) != ThemeId.Ink) throw new Exception("A skin file must be read back");
                } finally { if (Directory.Exists(themeFolder)) Directory.Delete(themeFolder, true); }
                shell.menu.Show(shell, new Point(0, 0));
                if (!shell.menu.Visible || shell.menuGuard == null) throw new Exception("Open menu must install the outside-click guard");
                shell.menu.Items[0].Select();
                using (var preview = new Bitmap(shell.menu.Width, shell.menu.Height)) {
                    shell.menu.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
                    preview.Save(Path.Combine(root, "work", "microphone-menu.png"));
                }
                if (shell.menu.Region != null || shell.picker.Drop.Region != null)
                    throw new Exception("Menus must use smooth system corners instead of binary regions");
                // Replay the real low-level mouse callback instead of clicking the user's desktop.
                var menuCallback = (Delegate)typeof(MenuDismissHook).GetField("callback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(shell.menuGuard);
                IntPtr probe = Marshal.AllocHGlobal(32);
                try {
                    Marshal.Copy(new byte[32], 0, probe, 32);
                    Point inside = shell.menu.Bounds.Location; inside.Offset(8, 8);
                    Marshal.WriteInt32(probe, 0, inside.X); Marshal.WriteInt32(probe, 4, inside.Y);
                    menuCallback.DynamicInvoke(0, new IntPtr(0x201), probe);
                    Application.DoEvents();
                    if (!shell.menu.Visible || shell.menuDismissRequests != 0) throw new Exception("A click inside the menu must keep it open");
                    var away = new Point(shell.menu.Bounds.Left - 40, shell.menu.Bounds.Top - 40);
                    Marshal.WriteInt32(probe, 0, away.X); Marshal.WriteInt32(probe, 4, away.Y);
                    menuCallback.DynamicInvoke(0, new IntPtr(0x201), probe);
                    for (int i = 0; i < 40 && shell.menu.Visible; i++) { Application.DoEvents(); Thread.Sleep(5); }
                    if (shell.menu.Visible || shell.menuDismissRequests != 1 || shell.menuGuard != null)
                        throw new Exception("A click outside the menu must close it and release the guard");
                } finally { Marshal.FreeHGlobal(probe); }
                shell.menu.Close();
                if (shell.menu.Visible || shell.menuGuard != null) throw new Exception("Closing the menu must release the outside-click guard");
                shell.menu.Show(shell, new Point(0, 0));
                shell.menu.Items[0].PerformClick();
                if (shell.menu.Visible || shell.menuGuard != null) throw new Exception("Dismissing must close the open menu");
                if (shell.Visible || shell.focusWatch.Enabled || shell.animation.Enabled) throw new Exception("Dismiss must hide and stop idle timers");
                shell.Reveal();
                if (!shell.Visible || shell.Location != remembered) throw new Exception("Recall must retain position");
                if (Native.GetForegroundWindow() != foreground) throw new Exception("Reveal stole the input focus");
                shell.revealAt = shell.motionClock.Elapsed.TotalMilliseconds - 180;
                if (shell.RevealProgress != 1) throw new Exception("Entrance must finish in 180ms");
                shell.state = "idle"; shell.recovery = "已定稿的缓存";
                shell.copyText = text => { throw new ExternalException("Clipboard busy"); };
                shell.CopyRecovery();
                if (shell.recoveryCopied) throw new Exception("Failed clipboard write must keep the cache uncopied for a retry");
                shell.copyText = text => shell.copiedInTest = text;
                shell.HandleShortcut(3);
                if (shell.copiedInTest.Length != 0) throw new Exception("No shortcut action may copy the cache");
                shell.CopyRecovery();
                if (shell.copiedInTest != shell.recovery || !shell.recoveryCopied) throw new Exception("Copy button must copy and keep the ready cache");
                shell.copiedInTest = "";
                shell.CopyRecovery();
                if (shell.copiedInTest != shell.recovery || !shell.recoveryCopied) throw new Exception("Repeated copy button presses must re-copy the same cache");
                shell.copiedInTest = "";
                shell.KeepRecovery("新缓存");
                if (shell.recoveryCopied) throw new Exception("New cached text must rearm the copy button");
                var ordinaryCopyKeys = new ShortcutKeys();
                ordinaryCopyKeys.Process(0xA2, true, 1, out swallowed);
                for (int i = 0; i < 10; i++) {
                    if (ordinaryCopyKeys.Process(0x43, true, 3, out swallowed) != 0 || swallowed)
                        throw new Exception("Ordinary Ctrl+C must pass while cached segments exist");
                    ordinaryCopyKeys.Process(0x43, false, 1, out swallowed);
                    shell.HandleShortcut(3);
                    if (shell.copiedInTest.Length != 0) throw new Exception("Late shortcut messages must not overwrite ordinary copies");
                    shell.KeepRecovery("后续片段");
                }
                shell.copyText = text => { throw new ExternalException("Clipboard busy"); };
                shell.CopyRecovery();
                if (shell.recoveryCopied) throw new Exception("Failed manual recopy must keep the cache uncopied");
                shell.copyText = text => shell.copiedInTest = text;
                shell.copiedInTest = "";
                shell.CopyRecovery();
                if (shell.copiedInTest != shell.recovery || !shell.recoveryCopied) throw new Exception("Copy button must allow manual recopy");
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
                string copyTestRecovery = shell.recovery;
                shell.startOnReveal = true; shell.menu.Items[0].PerformClick();
                if (shell.startOnReveal || shell.TakeRevealStart()) throw new Exception("Dismiss must cancel queued recording");
                if (shell.recovery != copyTestRecovery) throw new Exception("Dismiss must preserve fallback cache");
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
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.delivery = new TextDelivery { Latest = "前段。", Accounted = "前段。", Delivered = "前段。", Paused = "测试缓存" };
                var segment = new Dictionary<string, object> { { "route", 0 }, { "start", 1600 }, { "text", "草稿不送出" } };
                shell.ApplySegment(segment, false);
                if (shell.delivery.Latest != "前段。" || shell.recovery.Length != 0) throw new Exception("Off-target preview changed the delivery ledger");
                segment["text"] = "后续定稿。";
                var reply = new Dictionary<string, object> { { "segments", new object[] { segment } }, { "preview", "" } };
                shell.ApplyReply(reply); shell.ApplyReply(reply);
                if (shell.recovery != "后续定稿。" || shell.delivery.Delivered != "前段。" || shell.delivery.Invalidated)
                    throw new Exception("Final-only cache must preserve previous text and deduplicate finals");
            }
            if (!Deliverable(TargetKind.Unknown)) throw new Exception("Unknown target must stay deliverable");
            if (Deliverable(DecideTarget(false, false)) || Deliverable(DecideTarget(true, false)))
                throw new Exception("Visible non-text or read-only targets must stay in fallback");
            if (DecideTarget(true, true) != TargetKind.Editable || !Deliverable(TargetKind.Editable))
                throw new Exception("Writable text host must deliver");
            int focusedState = (int)(AccessibleStates.Focused | AccessibleStates.Focusable);
            foreach (string host in new[] { "Qt51514QWindowIcon", "Qt6QWindowIcon", "MozillaWindowClass", "Chrome_WidgetWin_1" })
                if (!CoarseWindowClass(host)) throw new Exception("Internal focus hosts must use focused accessibility and keep polling: " + host);
            if (CoarseWindowClass("WindowsForms10.EDIT.app.0.fixture"))
                throw new Exception("Native input fields must retain direct focus classification");
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
            if (ConfirmReadOnlyTarget(TargetKind.ReadOnly, () => TargetKind.Editable) != TargetKind.Editable)
                throw new Exception("A focused writable UIA input must override the coarse read-only document");
            foreach (var confirmation in new[] { TargetKind.ReadOnly, TargetKind.Unknown })
                if (ConfirmReadOnlyTarget(TargetKind.ReadOnly, () => confirmation) != TargetKind.ReadOnly)
                    throw new Exception("Only a confirmed editable input may override a coarse read-only target");
            foreach (var primary in new[] { TargetKind.Unknown, TargetKind.Editable })
                if (ConfirmReadOnlyTarget(primary, () => { throw new Exception("Unneeded UIA query"); }) != primary)
                    throw new Exception("Deliverable MSAA targets must not query UIA");
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
                shell.Status("busy", "正在开始录音…");
                if (!shell.MemoryPill) throw new Exception("Starting without a delivery session must not collapse the capsule");
                shell.SetInputFocus(true);
                if (shell.MemoryPill) throw new Exception("Starting in an input box must keep the circle");
            }
            foreach (float speech in new[] { 0f, .25f, .75f, 1f })
            using (var listening = new Bitmap(72, 72)) using (var stopping = new Bitmap(72, 72)) {
                using (var ink = Graphics.FromImage(listening)) DrawListening(ink, "recording", 1, speech, .6f, 600, false);
                using (var ink = Graphics.FromImage(stopping)) DrawListening(ink, "busy", 1, speech, .6f, 600, false);
                for (int x = 0; x < 72; x++) for (int y = 0; y < 72; y++)
                    if (listening.GetPixel(x, y) != stopping.GetPixel(x, y))
                        throw new Exception("Start/stop must preserve the current glyph and ring phase instead of flashing a processing waveform");
            }
            using (var idle = new Bitmap(128, 96)) using (var starting = new Bitmap(128, 96)) {
                using (var ink = Graphics.FromImage(idle)) DrawShape(ink, 1, "idle", false, 0, 0, 0, stashed: true);
                using (var ink = Graphics.FromImage(starting)) DrawShape(ink, 1, "busy", false, 0, 0, 0, transcribing: true);
                for (int x = 24; x < 49; x++) for (int y = 32; y < 65; y++)
                    if (idle.GetPixel(x, y) != starting.GetPixel(x, y))
                        throw new Exception("An empty capsule must retain its microphone without flashing a copy icon while starting");
            }
            var morph = new ShapeMotion();
            var edgeMetrics = new List<string>();
            using (var shell = new Microphone(root, "--stream-test")) {
                shell.theme = shell.themeFrom = ThemeId.Graphite;
                shell.state = "idle";
                foreach (double phase in new[] { 0.0, .5, 1.0 })
                using (var actual = new Bitmap(128, 96, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (var reference = new Bitmap(512, 384, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (var expected = new Bitmap(128, 96, System.Drawing.Imaging.PixelFormat.Format32bppPArgb)) {
                    shell.shape.Value = phase;
                    using (var ink = Graphics.FromImage(actual)) shell.DrawCurrent(ink);
                    using (var ink = Graphics.FromImage(reference)) { ink.ScaleTransform(4, 4); shell.DrawContent(ink); }
                    using (var ink = Graphics.FromImage(expected)) {
                        ink.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        ink.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        ink.DrawImage(reference, new Rectangle(0, 0, 128, 96), 0, 0, 512, 384, GraphicsUnit.Pixel);
                    }
                    long difference = 0;
                    for (int x = 0; x < 128; x++) for (int y = 0; y < 96; y++)
                        difference += Math.Abs(actual.GetPixel(x, y).A - expected.GetPixel(x, y).A);
                    double error = difference / (128.0 * 96);
                    edgeMetrics.Add("shape=" + phase + " alpha coverage error=" + error);
                    if (error > .1) throw new Exception("Circle, capsule and intermediate edges must retain subpixel vector coverage");
                }
            }
            File.WriteAllLines(Path.Combine(root, "work", "smooth-edge-rendering-test.txt"), edgeMetrics.ToArray());
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
            File.WriteAllText(Path.Combine(root, "work", "microphone-self-test.txt"), "PASS: Win+H and Escape / Ctrl+C never captured and always reaching the target app / context menu closed by an outside click (real hook callback replayed), a menu item, Esc, dismiss and exit / outside-click predicate / automatic recall start / model readiness / no modifier-release gate / recording no-op / dismiss cancels pending start / reveal fade / repeat recall / no activation / dismiss timers / position parsing and monitor clamp / x64 INPUT / Unicode / coarse-host delivery / read-only and non-text veto / idle shape follows focus / append-only text / English spaces / duplicate and revision guards / audio levels / Windows dictation earcon resources / quiet microphone / fine five-bar waveform in circle and capsule / speech hold / silence return / interruption / reduced motion / embedded wallpaper through morph and overshoot / centered status ink / circle and capsule icon proportions / no midpoint icon disappearance / outward listening and processing rings / unified white glyphs without inner discs / state rendering / smooth critically damped spring / velocity-preserving retargeting / long press restart guard / direct copy pill without dot / button-only memory copy (test sink) / clear old cache on successful new round / append only undelivered text / focus changes keep recording / clipboard busy and retry; input devices=" + Recorder.waveInGetNumDevs());
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
                if (args.Length > 0 && args[0] == "--background") return;
                IntPtr existing = Native.FindWindow(null, "VoxFix Desktop");
                if (existing != IntPtr.Zero) Native.PostMessage(existing, Native.ShowMicrophone, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Microphone(root, args.Length > 0 ? args[0] : ""));
        }
    }
}
