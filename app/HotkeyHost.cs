using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Keep Win+H owned even when the model/UI process has exited.
sealed class HotkeyHost : Form
{
    internal const string WindowTitle = "VoxFix Desktop hotkey host";
    readonly Func<IntPtr> findApp;
    readonly Action<bool> launchApp;
    readonly string errorFile;
    readonly DictationShortcut hook;
    readonly System.Windows.Forms.Timer starting = new System.Windows.Forms.Timer { Interval = 100 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    bool launching, wakeAfterStart, disposed;
    double deadline;

    internal HotkeyHost(string root) : this(root,
        () => Native.FindWindow(null, "VoxFix Desktop"),
        wake => {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "bin", "VoxFix Desktop.exe"), wake ? "" : "--background") {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            })) { }
        }, true) { }

    HotkeyHost(string root, Func<IntPtr> find, Action<bool> launch, bool warm) {
        findApp = find; launchApp = launch;
        errorFile = Path.Combine(root, "work", "hotkey-host-error.txt");
        Text = WindowTitle; ShowInTaskbar = false;
        IntPtr window = Handle;
        hook = new DictationShortcut(action => {
            if (action != 1 || IsDisposed) return;
            try { BeginInvoke(new Action(() => EnsureApp(true))); } catch (InvalidOperationException) { }
        });
        starting.Tick += (s, e) => {
            IntPtr app = findApp();
            if (app == IntPtr.Zero && clock.Elapsed.TotalMilliseconds < deadline) return;
            starting.Stop(); launching = false;
            bool wake = wakeAfterStart; wakeAfterStart = false;
            if (wake) EnsureApp(true);
        };
        if (warm) EnsureApp(false);
    }

    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

    void EnsureApp(bool wake) {
        IntPtr app = findApp();
        if (app != IntPtr.Zero) {
            starting.Stop(); launching = wakeAfterStart = false;
            if (wake) Native.PostMessage(app, Native.ShortcutMessage, new IntPtr(1), IntPtr.Zero);
            return;
        }
        if (launching) { wakeAfterStart |= wake; return; }
        try {
            launchApp(wake);
            launching = true; deadline = clock.Elapsed.TotalMilliseconds + 10000;
            starting.Start();
        } catch (Exception error) {
            try { Directory.CreateDirectory(Path.GetDirectoryName(errorFile)); File.AppendAllText(errorFile, DateTime.UtcNow.ToString("o") + " " + error.Message + Environment.NewLine); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing && !disposed) { disposed = true; starting.Stop(); starting.Dispose(); if (hook != null) hook.Dispose(); }
        base.Dispose(disposing);
    }

    sealed class TestTarget : Form {
        internal int Calls;
        protected override void WndProc(ref Message message) {
            if (message.Msg == Native.ShortcutMessage) { Calls++; return; }
            base.WndProc(ref message);
        }
    }

    internal static void Check(string root) {
        Directory.CreateDirectory(Path.Combine(root, "work"));
        Action<bool, string> check = (ok, message) => { if (!ok) throw new Exception(message); };
        Action<Func<bool>> wait = ready => {
            var elapsed = Stopwatch.StartNew();
            while (!ready() && elapsed.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(5); }
            check(ready(), "Hotkey host test timed out");
        };
        Action press = () => {
            var keys = new[] { Native.KeyInput(0x5B, false), Native.KeyInput(0x48, false), Native.KeyInput(0x48, true), Native.KeyInput(0x5B, true) };
            check(Native.SendInput((uint)keys.Length, keys, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Input))) == keys.Length, "Win+H injection failed");
        };
        int launches = 0; bool lastWake = false; IntPtr app = IntPtr.Zero;
        using (var target = new TestTarget()) {
            IntPtr handle = target.Handle;
            using (var host = new HotkeyHost(root, () => app, wake => { launches++; lastWake = wake; app = handle; }, false)) {
                press(); wait(() => launches == 1); check(lastWake, "Missing app must launch on Win+H");
                press(); wait(() => target.Calls == 1); check(launches == 1, "Existing app must receive one command without relaunch");
                app = IntPtr.Zero; press(); wait(() => launches == 2); check(lastWake, "Closed app must be recoverable on the next Win+H");
            }
            app = IntPtr.Zero;
            using (var host = new HotkeyHost(root, () => app, wake => { launches++; lastWake = wake; }, true)) {
                check(launches == 3 && !lastWake, "Login must warm the app without recording");
                host.EnsureApp(true); host.EnsureApp(true);
                check(launches == 3 && host.wakeAfterStart, "Starting app must coalesce wake requests");
                host.deadline = -1; wait(() => launches == 4);
                check(lastWake, "Timed-out startup must retry a queued wake");
                app = handle; wait(() => !host.launching);
                check(target.Calls == 1, "A wake launch must not receive a duplicate toggle after startup");
                check(!host.launching && !host.starting.Enabled, "Idle host must stop startup polling");
                host.Close();
            }
            app = IntPtr.Zero; int attempts = 0;
            string fixture = Path.Combine(root, "work", "host-failure-" + Guid.NewGuid().ToString("N"));
            using (var host = new HotkeyHost(fixture, () => app, wake => {
                if (++attempts == 1) throw new IOException("Synthetic launch failure");
                app = handle;
            }, false)) {
                host.EnsureApp(true);
                check(!host.launching && File.Exists(host.errorFile), "Failed launch must report an error without disabling the hook");
                press(); wait(() => attempts == 2);
                check(app == handle, "The next Win+H must retry after a launch error");
            }
        }
        File.WriteAllText(Path.Combine(root, "work", "hotkey-host-test.txt"), "PASS: real Win+H hook / missing-app launch / existing-app command / exit recovery / login warm without recording / startup coalescing / startup timeout / launch-error retry / idle timer stopped; isolated native target, no models, microphone or clipboard");
    }
}
