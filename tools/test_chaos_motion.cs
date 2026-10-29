using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

class ChaosMotionTest {
    [STAThread] static void Main() {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string output = Path.Combine(root, "work", "chaos-motion-test.txt");
        try { Run(root, output); Environment.Exit(0); }
        catch (Exception error) { File.WriteAllText(output, "FAIL: " + error); Environment.Exit(1); }
    }
    static void Run(string root, string output) {
        string scratch = Path.Combine(root, "work", "chaos-motion-fixture");
        Directory.CreateDirectory(Path.Combine(scratch, "user-data"));
        File.WriteAllText(Path.Combine(scratch, "user-data", "theme.json"), "{\"theme\":\"chaos\"}");
        var assembly = Assembly.LoadFrom(Path.Combine(root, "bin", "VoxFix Desktop.exe"));
        var type = assembly.GetType("Microphone");
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Application.EnableVisualStyles();
        var clock = Stopwatch.StartNew(); var intervals = new List<double>();
        double last = -1, lastPhase = -1; int visits = 0, movingFrames = 0, stillFrames = 0, liveFrames = 0;
        string failure = null;
        using (var form = (Form)Activator.CreateInstance(type, new object[] { scratch, "--idle-focus-test" })) {
            var animation = (Timer)type.GetField("animation", flags).GetValue(form);
            var shape = type.GetField("shape", flags).GetValue(form);
            var value = shape.GetType().GetField("Value", flags);
            var motion = type.GetField("motion", flags).GetValue(form);
            var activity = motion.GetType().GetField("Activity", flags);
            var phase = type.GetField("chaosVisualTime", flags);
            if (!(bool)type.GetField("probeOnly", flags).GetValue(form)) throw new Exception("Motion fixture must not capture audio");
            animation.Tick += (s, e) => {
                double now = clock.Elapsed.TotalMilliseconds;
                double position = (double)value.GetValue(shape);
                double currentPhase = (double)phase.GetValue(form);
                float active = (float)activity.GetValue(motion);
                if (lastPhase >= 0 && active == 0) {
                    if (currentPhase != lastPhase) failure = "Paused sphere continued to move";
                    stillFrames++;
                }
                if (lastPhase >= 0 && active == 1) {
                    if (currentPhase <= lastPhase) failure = "Listening sphere lost its flowing feedback";
                    liveFrames++;
                }
                lastPhase = currentPhase;
                if (now > 600 && position > .02 && position < .98) {
                    if (last > 0 && now - last < 150) intervals.Add(now - last);
                    movingFrames++;
                }
                last = now;
            };
            form.Shown += (s, e) => {
                ((Timer)type.GetField("focusWatch", flags).GetValue(form)).Stop();
                type.GetField("state", flags).SetValue(form, "idle");
                type.GetField("delivery", flags).SetValue(form, Activator.CreateInstance(assembly.GetType("TextDelivery"), true));
                form.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - 240, Screen.PrimaryScreen.WorkingArea.Bottom - 160);
            };
            using (var toggle = new Timer { Interval = 450 }) {
                toggle.Tick += (s, e) => {
                    visits++;
                    if (visits == 5) {
                        type.GetField("state", flags).SetValue(form, "recording");
                        type.GetField("level", flags).SetValue(form, .3f);
                        type.GetField("speechDetected", flags).SetValue(form, true);
                    }
                    if (visits == 9) {
                        type.GetField("state", flags).SetValue(form, "idle");
                        type.GetField("level", flags).SetValue(form, 0f);
                        type.GetField("speechDetected", flags).SetValue(form, false);
                    }
                    type.GetMethod("SetInputFocus", flags).Invoke(form, new object[] { visits % 2 == 1 });
                    if (visits == 12) form.Close();
                };
                toggle.Start(); Application.Run(form);
            }
            if (type.GetField("worker", flags).GetValue(form) != null || type.GetField("recorder", flags).GetValue(form) != null)
                throw new Exception("Motion fixture opened a model or microphone");
        }
        intervals.Sort();
        if (failure != null || stillFrames < 10 || liveFrames < 10) throw new Exception(failure ?? "Missing paused/listening phase coverage");
        if (intervals.Count < 12) throw new Exception("Too few actual presented transition frames: " + intervals.Count);
        double median = intervals[intervals.Count / 2], p95 = intervals[(int)((intervals.Count - 1) * .95)];
        string detail = "actual layered circle/capsule transitions / paused phase frozen / listening phase continuous / no model or microphone / movingFrames=" + movingFrames
            + " / medianMs=" + median.ToString("F2", CultureInfo.InvariantCulture) + " / p95Ms=" + p95.ToString("F2", CultureInfo.InvariantCulture);
        if (median > 27 || p95 > 45) throw new Exception("Uneven transition cadence: " + detail);
        File.WriteAllText(output, "PASS: " + detail);
    }
}
