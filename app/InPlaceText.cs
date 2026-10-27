using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// A per-thread Windows text bridge; no keyboard layout or input-method registration.
sealed class InPlaceText : IDisposable
{
    static readonly string Version = typeof(InPlaceText).Assembly.GetName().Version.ToString();
    static readonly string WindowClass = "VoxFix.TextBridge." + Version;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool IsWow64Process(IntPtr process, out bool wow64);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string type, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetWindowsHookEx(int type, IntPtr callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] struct CopyData { public UIntPtr tag; public uint length; public IntPtr data; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr sender, ref CopyData packet, uint flags, uint timeout, out UIntPtr result);
    readonly IntPtr owner, target, focus;
    readonly uint thread, token;
    IntPtr module, hook, receiver;
    bool attempted, unsupported, disposed;
    int bootstrapAt;
    internal string Diagnostic { get { return "attempted=" + attempted + "; unavailable=" + unsupported + "; hook=" + hook + "; receiver=" + receiver + "; class=" + Native.ClassOf(focus) + "; owner=" + owner.ToInt64().ToString("X16") + "; thread=" + thread; } }
    internal InPlaceText(IntPtr owner, IntPtr target, IntPtr focus) {
        this.owner = owner; this.target = target; this.focus = focus;
        thread = Native.GetWindowThreadProcessId(focus, IntPtr.Zero);
        token = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
    }
    internal bool Matches(IntPtr window, IntPtr input) { return target == window && focus == input && !disposed; }
    string ReceiverTitle { get { return thread + ":" + owner.ToInt64().ToString("X16") + ":" + token.ToString("X8"); } }
    bool ReceiverMatches() { return IsWindow(receiver) && Native.ClassOf(receiver) == WindowClass && String.Equals(Native.TitleOf(receiver), ReceiverTitle, StringComparison.OrdinalIgnoreCase); }
    // 1 applied; 2 unavailable and untouched; 4 user/context changed; 5 outcome uncertain.
    internal int Update(ulong segment, string previous, string text, bool final) {
        if (disposed || unsupported || previous.Length > 8192 || text.Length > 8192) return 2;
        if (thread == 0 || owner == IntPtr.Zero || target == IntPtr.Zero || focus == IntPtr.Zero) { unsupported = true; return 2; }
        if (Native.GetWindowThreadProcessId(focus, IntPtr.Zero) != thread) return 4;
        if (Native.GetForegroundWindow() != target || Native.Focus(target) != focus) return 4;
        if (!attempted) {
            attempted = true;
            uint pid; GetWindowThreadProcessId(focus, out pid);
            IntPtr process = OpenProcess(0x1000, false, pid);
            bool wow64;
            if (process == IntPtr.Zero) { unsupported = true; return 2; }
            try { if (!IsWow64Process(process, out wow64) || wow64) { unsupported = true; return 2; } }
            finally { CloseHandle(process); }
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoxFix.TextBridge." + Version + ".dll");
            module = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
            if (module == IntPtr.Zero) { unsupported = true; return 2; }
            IntPtr callback = GetProcAddress(module, "BridgeHook");
            if (callback == IntPtr.Zero) { unsupported = true; return 2; }
            hook = SetWindowsHookEx(3, callback, module, thread);
            if (hook == IntPtr.Zero) { unsupported = true; return 2; }
            Bootstrap();
            return 2;
        }
        if (receiver != IntPtr.Zero && !ReceiverMatches()) {
            receiver = IntPtr.Zero;
            if (previous.Length != 0) return 4;
        }
        if (receiver == IntPtr.Zero) {
            receiver = FindWindowEx(new IntPtr(-3), IntPtr.Zero, WindowClass, ReceiverTitle);
            if (receiver == IntPtr.Zero) {
                if (unchecked(Environment.TickCount - bootstrapAt) >= 500) Bootstrap();
                return 2;
            }
        }
        byte[] bytes;
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.Unicode)) {
            writer.Write(1u); writer.Write(final ? 1u : 0u); writer.Write((uint)text.Length); writer.Write((uint)previous.Length);
            writer.Write(target.ToInt64()); writer.Write(focus.ToInt64()); writer.Write(segment);
            writer.Write(Encoding.Unicode.GetBytes(text + previous)); bytes = stream.ToArray();
        }
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try {
            var packet = new CopyData { tag = new UIntPtr(0x56465831), length = (uint)bytes.Length, data = pin.AddrOfPinnedObject() };
            UIntPtr result;
            if (SendMessageTimeout(receiver, 0x4a, owner, ref packet, 3, 80, out result) == IntPtr.Zero) return 5;
            uint status = result.ToUInt32(); return status == 1 || status == 2 || status == 4 || status == 5 ? (int)status : 5;
        } finally { pin.Free(); }
    }
    void Bootstrap() {
        bootstrapAt = Environment.TickCount;
        Native.PostMessage(focus, RegisterWindowMessage(WindowClass + ".Bootstrap"), owner, new IntPtr(unchecked((int)token)));
    }
    public void Dispose() {
        if (disposed) return; disposed = true;
        if (receiver == IntPtr.Zero && attempted) {
            receiver = FindWindowEx(new IntPtr(-3), IntPtr.Zero, WindowClass, ReceiverTitle);
        }
        if (receiver != IntPtr.Zero && ReceiverMatches()) {
            var packet = new CopyData { tag = new UIntPtr(0x56465832) }; UIntPtr result;
            SendMessageTimeout(receiver, 0x4a, owner, ref packet, 3, 80, out result);
        }
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
    }
}
