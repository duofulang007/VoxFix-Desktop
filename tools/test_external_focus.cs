using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

class ExternalFocusTest {
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [STAThread] static void Main(string[] args) {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string scratch = Path.Combine(root, "work", "external-focus-fixture"); Directory.CreateDirectory(scratch);
        Application.EnableVisualStyles();
        if (args.Length > 0 && args[0] == "host") { Host(scratch); return; }
        string output = Path.Combine(root, "work", "external-focus-test.txt");
        try { Run(root, scratch, output); Environment.Exit(0); }
        catch (Exception error) { File.WriteAllText(output, "FAIL: " + error); Environment.Exit(1); }
    }
    static void Host(string scratch) {
        using (var form = new Form { Text = "VoxFix isolated external inputs", Width = 420, Height = 260, TopMost = true })
        using (var edit = new TextBox { Dock = DockStyle.Top })
        using (var rich = new RichTextBox { Dock = DockStyle.Top, Height = 70 })
        using (var read = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = "Read-only fixture" })
        using (var button = new Button { Dock = DockStyle.Bottom, Text = "Non-input fixture" })
        using (var timer = new Timer { Interval = 1400 }) {
            form.Controls.Add(rich); form.Controls.Add(read); form.Controls.Add(edit); form.Controls.Add(button);
            Control[] controls = { edit, button, rich, read, edit }; int stage = 0;
            Action select = () => {
                if (stage == controls.Length) { form.Close(); return; }
                uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), current = GetCurrentThreadId();
                bool attached = foreground != current && AttachThreadInput(current, foreground, true);
                try { form.Activate(); SetForegroundWindow(form.Handle); controls[stage].Focus(); }
                finally { if (attached) AttachThreadInput(current, foreground, false); }
                string expected = stage == 1 || stage == 3 ? "ReadOnly" : "Editable";
                File.WriteAllText(Path.Combine(scratch, "stage.txt"), stage + "|" + form.Handle.ToInt64() + "|" + controls[stage].Handle.ToInt64() + "|" + expected);
            };
            form.Shown += (s, e) => { select(); timer.Start(); };
            timer.Tick += (s, e) => { stage++; select(); };
            Application.Run(form);
        }
    }
    static void Run(string root, string scratch, string output) {
        Directory.CreateDirectory(Path.Combine(scratch, "user-data"));
        File.WriteAllText(Path.Combine(scratch, "user-data", "theme.json"), "{\"theme\":\"chaos\"}");
        File.WriteAllText(Path.Combine(scratch, "stage.txt"), "");
        var assembly = Assembly.LoadFrom(Path.Combine(root, "bin", "VoxFix Desktop.exe"));
        var type = assembly.GetType("Microphone"); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string failure = null; int checkedStage = -1;
        using (var shell = (Form)Activator.CreateInstance(type, new object[] { scratch, "--idle-focus-test" }))
        using (var timer = new Timer { Interval = 150 }) {
            Process host = null; bool polling = false;
            shell.Shown += (s, e) => {
                type.GetField("state", flags).SetValue(shell, "idle");
                ((Timer)type.GetField("focusWatch", flags).GetValue(shell)).Stop();
                host = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "host") { UseShellExecute = false });
                timer.Start();
            };
            timer.Tick += async (s, e) => {
                if (polling) return; polling = true;
                try {
                    if (host.HasExited) { shell.Close(); return; }
                    string[] parts = File.ReadAllText(Path.Combine(scratch, "stage.txt")).Split('|');
                    if (parts.Length != 4) return;
                    int stage = int.Parse(parts[0]); if (stage == checkedStage) return;
                    IntPtr window = new IntPtr(long.Parse(parts[1])), focus = new IntPtr(long.Parse(parts[2]));
                    if (GetForegroundWindow() != window) {
                        var name = new StringBuilder(256); GetClassName(GetForegroundWindow(), name, name.Capacity);
                        throw new Exception("External input interrupted the fixture: foregroundClass=" + name);
                    }
                    var task = (Task)type.GetMethod("ClassifySafe", flags).Invoke(shell, new object[] { window, focus });
                    await task;
                    string actual = task.GetType().GetProperty("Result").GetValue(task, null).ToString();
                    if (GetForegroundWindow() != window) throw new Exception("External focus changed during classification");
                    if (actual != parts[3]) throw new Exception("stage=" + stage + " expected=" + parts[3] + " actual=" + actual);
                    checkedStage = stage;
                } catch (IOException) { }
                catch (Exception error) { failure = error.ToString(); shell.Close(); }
                finally { polling = false; }
            };
            try { Application.Run(shell); }
            finally { if (host != null) { if (!host.HasExited) host.Kill(); host.Dispose(); } }
        }
        if (failure != null) throw new Exception(failure);
        if (checkedStage != 4) throw new Exception("External focus cases incomplete: " + checkedStage);
        File.WriteAllText(output, "PASS: cross-process Edit / Button rejection / RichEdit / read-only rejection / editable focus return / production ClassifySafe / chaos rendering / no audio / no text injected");
    }
}
