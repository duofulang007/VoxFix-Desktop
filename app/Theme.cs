// Themes share the existing input and cache semantics.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

enum ThemeId { Graphite = 0, Ink = 2, Chaos = 3 }

sealed class ThemeDefinition
{
    internal ThemeId Id;
    internal string Key;      // persisted short name
    internal string Name;     // shown in the picker
    internal string Role;     // one line describing what the theme feels like
}

static class ThemeCatalog
{
    static readonly ThemeDefinition[] all = new ThemeDefinition[] {
        new ThemeDefinition { Id = ThemeId.Graphite, Key = "graphite", Name = "石墨", Role = "克制的默认外观，日常长时间使用" },
        new ThemeDefinition { Id = ThemeId.Ink, Key = "ink", Name = "雾息", Role = "气凝胶柔雾，声音驱动的轻盈起伏" },
        new ThemeDefinition { Id = ThemeId.Chaos, Key = "chaos", Name = "混沌", Role = "青蓝流体网面，随声音浮动的微光" },
    };
    internal static ThemeDefinition[] All { get { return all; } }
    internal static ThemeDefinition Of(ThemeId id) {
        foreach (ThemeDefinition item in all) if (item.Id == id) return item;
        return all[0];
    }
    internal static ThemeId Parse(string key) {
        if (string.IsNullOrEmpty(key)) return ThemeId.Graphite;
        string value = key.Trim().ToLowerInvariant();
        foreach (ThemeDefinition item in all) if (item.Key == value) return item.Id;
        return ThemeId.Graphite;
    }
}

// Everything a skin is allowed to react to. Only real product signals land here:
// the recording state, the measured audio level, the recogniser's speaking and
// transcribing flags, the delivery gate and the pointer feedback.
sealed class ThemeContext
{
    internal string State = "idle";
    internal double Shape;            // 0 = circle, 1 = capsule
    internal float Activity;          // 0..1 sustained listening
    internal float Voice;             // 0..1 sound detected right now
    internal float Level;             // 0..1 measured input level
    internal float Hover, Press, Success, Ready;
    internal bool Pending, Transcribing, Stashed, Error, Copied;
    internal double Now;              // milliseconds
    internal bool Reduced;            // system asks for less motion
    internal bool Processing { get { return State == "busy" || Transcribing; } }
    internal float Pill { get { return (float)Math.Max(0, Math.Min(1, Shape)); } }
}

static class ThemeStore
{
    internal static string PathOf(string root) { return Path.Combine(root, "user-data", "theme.json"); }
    internal static ThemeId Load(string root) {
        try {
            string path = PathOf(root);
            if (!File.Exists(path)) return ThemeId.Graphite;
            string text = File.ReadAllText(path).Trim();
            if (text.Length == 0) return ThemeId.Graphite;
            if (text.StartsWith("{")) {
                var data = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
                object value;
                if (data != null && data.TryGetValue("theme", out value)) return ThemeCatalog.Parse(Convert.ToString(value));
                return ThemeId.Graphite;
            }
            return ThemeCatalog.Parse(text);
        } catch (IOException) { return ThemeId.Graphite; }
        catch (UnauthorizedAccessException) { return ThemeId.Graphite; }
        catch (ArgumentException) { return ThemeId.Graphite; }
    }
    internal static void Save(string root, ThemeId id) {
        try {
            string path = PathOf(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            HistoryStore.Atomic(path, "{\n  \"theme\": \"" + ThemeCatalog.Of(id).Key + "\"\n}\n");
        } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// Silhouette and material art for every skin.
static class ThemeArt
{
    internal static void CheckInk() {
        using (var impulse = new Bitmap(16, 16, PixelFormat.Format32bppPArgb)) {
            impulse.SetPixel(8, 8, Color.White); Soften(impulse);
            if (impulse.GetPixel(8, 8).A >= 255 || impulse.GetPixel(7, 8).A == 0 ||
                impulse.GetPixel(7, 8).A != impulse.GetPixel(9, 8).A)
                throw new Exception("INK-TEST: feathering must spread symmetrically without hard edges");
            for (int x = 0; x < 16; x++) for (int y = 0; y < 16; y++) {
                Color pixel = impulse.GetPixel(x, y);
                if (pixel.A > 0 && (pixel.R != 255 || pixel.G != 255 || pixel.B != 255))
                    throw new Exception("INK-TEST: transparent feathering must not create dark fringes");
            }
        }
        using (var speaking = new Bitmap(72, 72)) using (var transcribing = new Bitmap(72, 72)) {
            var ctx = new ThemeContext { State = "recording", Activity = 1, Voice = 1, Level = .7f, Now = 600 };
            using (var g = Graphics.FromImage(speaking)) PaintMotif(g, ThemeId.Ink, ctx, 1.65f);
            ctx.Transcribing = true;
            using (var g = Graphics.FromImage(transcribing)) PaintMotif(g, ThemeId.Ink, ctx, 1.65f);
            for (int x = 0; x < 72; x++) for (int y = 0; y < 72; y++)
                if (speaking.GetPixel(x, y) != transcribing.GetPixel(x, y))
                    throw new Exception("INK-TEST: streaming transcription must not suppress speech waveform");
        }
        using (var idle = new Bitmap(128, 96)) using (var listening = new Bitmap(128, 96)) {
            using (var g = Graphics.FromImage(idle)) Microphone.DrawShape(g, 0, "idle", false, 0, 0, 0, now: 350, theme: ThemeId.Ink);
            using (var g = Graphics.FromImage(listening)) Microphone.DrawShape(g, 0, "recording", false, 0, 0, 0, activity: 1, now: 350, theme: ThemeId.Ink);
            int changed = 0;
            for (int x = 0; x < 128; x++) for (int y = 0; y < 96; y++)
                if (idle.GetPixel(x, y) != listening.GetPixel(x, y)) changed++;
            if (changed < 100) throw new Exception("INK-TEST: quiet listening must remain visibly distinct from pause");
        }
        using (var frame = new Bitmap(512, 384, PixelFormat.Format32bppPArgb)) using (var g = Graphics.FromImage(frame)) {
            g.ScaleTransform(4, 4);
            for (int i = 0; i < 4; i++) Microphone.DrawShape(g, .5, "recording", false, 0, 0, 0, 1, 1, .7f, i * 33, theme: ThemeId.Ink);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) Microphone.DrawShape(g, .5, "recording", false, 0, 0, 0, 1, 1, .7f, i * 33, theme: ThemeId.Ink);
            double ms = clock.Elapsed.TotalMilliseconds / 60;
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "work", "ink-edge-performance.txt"),
                "PASS: 60 warm frames, 4x surface, mean draw ms=" + ms.ToString("F3", CultureInfo.InvariantCulture));
            if (ms > 33) throw new Exception("INK-TEST: feathering exceeds the 30fps draw budget");
        }
    }
    internal static GraphicsPath Outline(ThemeId theme, float width, float height, double shape, ThemeContext ctx) {
        if (theme == ThemeId.Chaos) return Microphone.GraphiteOutline(width, height);
        var path = new GraphicsPath();
        var points = new PointF[120];
        double pill = Math.Max(0, Math.Min(1, shape));
        if (pill >= .999) { path.Dispose(); return Microphone.GraphiteOutline(width, height); }
        double time = ctx.Reduced ? 0 : ctx.Now / 1000;
        double voice = ctx.Reduced || ctx.State != "recording" ? 0 : Math.Min(1, ctx.Level) * ctx.Voice;
        double size = .85 + .13 * pill;
        for (int i = 0; i < points.Length; i++) {
            double a = i * Math.PI * 2 / points.Length;
            double hand = (1.9 * Math.Sin(2 * a + .65) + 1.3 * Math.Cos(3 * a - .6 + time * .8 * ctx.Activity)) / 25;
            double speech = voice * (4.8 * Math.Sin(3 * a + time * 5.5) + 3 * Math.Sin(2 * a - time * 3.8)) / 25;
            double r = size * (1 + (hand + speech) * (1 - .9 * pill));
            double x = Math.Cos(a) * r, y = Math.Sin(a) * r;
            // Rotate only the round blot; the capsule keeps the copy target inside its outline.
            double rotation = -.22 * (1 - pill), c = Math.Cos(rotation), sn = Math.Sin(rotation);
            double capsuleX = (width - height) / 2 * Math.Sign(Math.Cos(a)) + (height - 1) / 2 * Math.Cos(a);
            double capsuleY = (height - 1) / 2 * Math.Sin(a);
            points[i] = new PointF(64 + (float)((1 - pill) * width / 2 * (x * c - y * sn) + pill * capsuleX),
                48 + (float)((1 - pill) * height / 2 * (x * sn + y * c) + pill * capsuleY));
        }
        path.AddPolygon(points); path.CloseFigure(); return path;
    }

    internal static void PaintBody(Graphics g, ThemeId theme, GraphicsPath outline, float width, float height, ThemeContext ctx) {
        if (theme == ThemeId.Chaos) { ChaosArt.Paint(g, outline, ctx); return; }
        double time = ctx.Reduced ? 0 : ctx.Now / 1000;
        float breath = ctx.Activity * (ctx.Reduced ? 1 : .65f + .35f * (float)Math.Sin(time * 4.5));
        using (var wash = new Bitmap(256, 192, PixelFormat.Format32bppPArgb)) using (var ink = Graphics.FromImage(wash)) {
            ink.ScaleTransform(2, 2); ink.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new PathGradientBrush(outline)) {
                fill.CenterPoint = new PointF(61, 44);
                fill.CenterColor = Color.FromArgb((int)(210 + 35 * breath), 218, 239, 250);
                fill.SurroundColors = new[] { Color.FromArgb(0, 173, 206, 229) };
                fill.FocusScales = new PointF(.45f, .45f);
                ink.FillPath(fill, outline);
            }
            using (var glow = new GraphicsPath()) {
                glow.AddEllipse(42, 29, 39, 30);
                using (var light = new PathGradientBrush(glow)) {
                    light.CenterColor = Color.FromArgb((int)(35 + 55 * breath), 255, 255, 255);
                    light.SurroundColors = new[] { Color.Transparent };
                    var saved = ink.Save(); ink.SetClip(outline); ink.FillPath(light, glow); ink.Restore(saved);
                }
            }
            if (ctx.Error) using (var tint = new SolidBrush(Color.FromArgb(45, 167, 85, 74))) ink.FillPath(tint, outline);
            Soften(wash);
            var surface = g.Save(); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(wash, new Rectangle(0, 0, 128, 96), 0, 0, wash.Width, wash.Height, GraphicsUnit.Pixel); g.Restore(surface);
        }
    }

    static void Soften(Bitmap image) {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try {
            int width = image.Width, height = image.Height;
            var source = new byte[data.Stride * height]; var target = new byte[source.Length];
            Marshal.Copy(data.Scan0, source, 0, source.Length);
            // Two separable box passes approximate a soft Gaussian; premultiplied RGB avoids dark fringes.
            for (int pass = 0; pass < 4; pass++) {
                bool horizontal = pass % 2 == 0;
                int lines = horizontal ? height : width, length = horizontal ? width : height;
                int step = horizontal ? 4 : data.Stride;
                for (int line = 0; line < lines; line++) for (int channel = 0; channel < 4; channel++) {
                    int origin = (horizontal ? line * data.Stride : line * 4) + channel, sum = 0;
                    for (int k = 0; k < 3; k++) sum += source[origin + k * step];
                    for (int p = 0; p < length; p++) {
                        if (p + 3 < length) sum += source[origin + (p + 3) * step];
                        if (p >= 4) sum -= source[origin + (p - 4) * step];
                        target[origin + p * step] = (byte)((sum + 3) / 7);
                    }
                }
                var swap = source; source = target; target = swap;
            }
            Marshal.Copy(source, 0, data.Scan0, source.Length);
        } finally { image.UnlockBits(data); }
    }

    internal static void PaintMotif(Graphics g, ThemeId theme, ThemeContext ctx, float glyphScale) {
        if (theme == ThemeId.Chaos) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var saved = g.Save(); g.TranslateTransform(36, 36); g.ScaleTransform(glyphScale * .85f, glyphScale * .85f);
        double time = ctx.Reduced ? 0 : ctx.Now / 1000;
        bool busy = ctx.State == "busy" || ctx.State == "loading";
        float voice = ctx.State == "recording" ? Math.Max(0, Math.Min(1, ctx.Voice)) : 0;
        Color color = Color.FromArgb(41, 72, 94);
        if (voice < .999f && !busy) using (var pen = new Pen(Color.FromArgb((int)(255 * (1 - voice)), color), 1.8f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            using (var mic = new GraphicsPath()) {
                mic.AddArc(-3.3f, -9, 6.6f, 6.6f, 180, 180);
                mic.AddArc(-3.3f, -3.1f, 6.6f, 6.6f, 0, 180); mic.CloseFigure(); g.DrawPath(pen, mic);
            }
            g.DrawBezier(pen, -6.5f, -1, -7, 9, 7, 9, 6.5f, -1);
            g.DrawLine(pen, 0, 6.5f, 0, 11); g.DrawLine(pen, -3.5f, 11, 3.5f, 11);
        }
        if (voice > .001f) using (var wave = new GraphicsPath()) {
            var points = new PointF[65];
            for (int i = 0; i < points.Length; i++) {
                float x = -16 + i * .5f, envelope = (float)Math.Pow(Math.Cos(x / 16 * Math.PI / 2), 2);
                float amplitude = 2 + 12 * Math.Max(0, Math.Min(1, ctx.Level));
                points[i] = new PointF(x, envelope * amplitude * (float)Math.Sin(x * .42 - time * 9));
            }
            wave.AddLines(points);
            using (var glow = new Pen(Color.FromArgb((int)(65 * voice), 255, 255, 255), 4)) g.DrawPath(glow, wave);
            using (var pen = new Pen(Color.FromArgb((int)(255 * voice), color), 1.8f)) { pen.StartCap = pen.EndCap = LineCap.Round; g.DrawPath(pen, wave); }
        }
        // Quiet listening remains legible without inventing a waveform for silence.
        if (busy || ctx.Activity > .001f) for (int i = -1; i <= 1; i++) {
            float pulse = ctx.Reduced ? 1 : .55f + .45f * (float)Math.Sin(time * 6 - i * .9);
            float opacity = busy ? 1 : ctx.Activity * (1 - voice);
            using (var brush = new SolidBrush(Color.FromArgb((int)(220 * pulse * opacity), color))) {
                float y = busy ? 0 : 17;
                g.FillEllipse(brush, i * 5 - 1.1f, y - 1.1f, 2.2f, 2.2f);
            }
        }
        g.Restore(saved);
    }

    internal static void Preview(Graphics g, ThemeId theme, RectangleF box, double seconds) {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var chip = MicrophoneMenuRenderer.Outline(new Rectangle((int)box.X, (int)box.Y,
            Math.Max(1, (int)box.Width), Math.Max(1, (int)box.Height)), 9))
        using (var fill = new SolidBrush(Color.FromArgb(243, 245, 248))) g.FillPath(fill, chip);
        double morph = .5 + .5 * Math.Sin(seconds * .85);
        var ctx = new ThemeContext {
            State = "recording", Shape = morph, Activity = 1,
            Voice = (float)(.5 + .5 * Math.Sin(seconds * 1.9)),
            Level = .45f, Now = seconds * 1000, Reduced = Microphone.ReducedMotion
        };
        float scale = box.Width / 118f;
        g.TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2);
        g.ScaleTransform(scale, scale);
        g.TranslateTransform(-64, -48);
        Microphone.DrawThemeLayer(g, morph, ctx, theme, false);
        g.Restore(saved);
    }
}

// ChatAni's cyan wire surface, depth/rim light and audio displacement, adapted for GDI+.
static class ChaosArt
{
    static readonly double[][] sphere;
    static readonly int[][] edges;
    static readonly double[][] gpuSphere;
    static readonly int[][] gpuEdges;
    static ChaosArt() {
        MakeMesh(4, out sphere, out edges);
        MakeMesh(6, out gpuSphere, out gpuEdges);
    }
    static void MakeMesh(int levels, out double[][] points, out int[][] edges) {
        double golden = (1 + Math.Sqrt(5)) / 2;
        var vertices = new List<double[]> { new[] {-1.0, golden, 0}, new[] {1.0, golden, 0},
            new[] {-1.0, -golden, 0}, new[] {1.0, -golden, 0}, new[] {0.0, -1, golden}, new[] {0.0, 1, golden},
            new[] {0.0, -1, -golden}, new[] {0.0, 1, -golden}, new[] {golden, 0, -1.0}, new[] {golden, 0, 1.0},
            new[] {-golden, 0, -1.0}, new[] {-golden, 0, 1.0} };
        foreach (var v in vertices) Normalize(v);
        var faces = new List<int[]> { new[] {0,11,5}, new[] {0,5,1}, new[] {0,1,7}, new[] {0,7,10}, new[] {0,10,11},
            new[] {1,5,9}, new[] {5,11,4}, new[] {11,10,2}, new[] {10,7,6}, new[] {7,1,8}, new[] {3,9,4},
            new[] {3,4,2}, new[] {3,2,6}, new[] {3,6,8}, new[] {3,8,9}, new[] {4,9,5}, new[] {2,4,11},
            new[] {6,2,10}, new[] {8,6,7}, new[] {9,8,1} };
        var midpoints = new Dictionary<long, int>();
        for (int level = 0; level < levels; level++) {
            var next = new List<int[]>();
            foreach (var face in faces) {
                int a = face[0], b = face[1], c = face[2];
                int ab = Midpoint(vertices, midpoints, a, b), bc = Midpoint(vertices, midpoints, b, c), ca = Midpoint(vertices, midpoints, c, a);
                next.Add(new[] {a,ab,ca}); next.Add(new[] {b,bc,ab}); next.Add(new[] {c,ca,bc}); next.Add(new[] {ab,bc,ca});
            }
            faces = next;
        }
        var seen = new HashSet<long>(); var lines = new List<int[]>();
        foreach (var face in faces) for (int i = 0; i < 3; i++) {
            int a = face[i], b = face[(i + 1) % 3];
            if (seen.Add(EdgeKey(a, b))) lines.Add(new[] {a,b});
        }
        points = vertices.ToArray(); edges = lines.ToArray();
    }
    static long EdgeKey(int a, int b) { return ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b); }
    static void Normalize(double[] v) {
        double length = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        for (int i = 0; i < 3; i++) v[i] /= length;
    }
    static int Midpoint(List<double[]> vertices, Dictionary<long, int> cache, int a, int b) {
        long key = EdgeKey(a, b); int index;
        if (cache.TryGetValue(key, out index)) return index;
        var v = new[] {vertices[a][0] + vertices[b][0], vertices[a][1] + vertices[b][1], vertices[a][2] + vertices[b][2]};
        Normalize(v); index = vertices.Count; vertices.Add(v); cache.Add(key, index); return index;
    }
    internal static double Frequency(ThemeContext ctx) {
        if (ctx.Reduced) return 0;
        double audio = ctx.State == "recording" ? Math.Max(0, Math.Min(1, ctx.Level * ctx.Voice)) : 0;
        // FFT averages and our normalized level are different units; ordinary speech stays rounded.
        return 12 * audio * (1 - .35 * ctx.Pill) + Math.Sin(ctx.Now / 1000) * 5;
    }
    internal static void Paint(Graphics g, GraphicsPath outline, ThemeContext ctx) {
        float pill = ctx.Pill, cx = 64 - 29 * pill, radius = 30 - 15 * pill;
        bool recording = ctx.State == "recording", busy = ctx.State == "busy" || ctx.State == "loading";
        double time = ctx.Reduced ? 0 : ctx.Now / 1000;
        double audio = recording ? Math.Max(0, Math.Min(1, ctx.Level)) * Math.Max(0, Math.Min(1, ctx.Voice)) : 0;
        // Match ChatAni's noise displacement; adapt FFT amplitude to our smoothed input level.
        double frequency = Frequency(ctx);
        double strength = .1 + frequency * .01;
        float light = .55f + .25f * ctx.Activity + .2f * (float)audio + .08f * ctx.Hover;
        Color cyan = ctx.Error ? Color.FromArgb(255, 173, 99) : Color.FromArgb(0, 217, 244);
        using (var bodyPath = new GraphicsPath()) {
            bodyPath.AddPath(outline, false);
            float inset = .89f + .11f * pill;
            using (var placement = new Matrix(inset, 0, 0, inset, 64 * (1 - inset), 48 * (1 - inset))) bodyPath.Transform(placement);
        using (var body = new PathGradientBrush(bodyPath)) {
            body.CenterPoint = new PointF(64, 48);
            body.CenterColor = Color.FromArgb(245, 3, 19, 31);
            body.SurroundColors = new[] { Color.FromArgb((int)(198 * pill), 3, 20, 32) };
            body.FocusScales = new PointF(.58f + .2f * pill, .58f + .2f * pill);
            g.FillPath(body, bodyPath);
        } }
        using (var glowPath = new GraphicsPath()) {
            glowPath.AddEllipse(cx - radius * 1.12f, 48 - radius * 1.12f, radius * 2.24f, radius * 2.24f);
            using (var glow = new PathGradientBrush(glowPath)) {
                glow.CenterColor = Color.FromArgb((int)(60 * light), cyan);
                glow.SurroundColors = new[] { Color.FromArgb(0, cyan) };
                g.FillPath(glow, glowPath);
            }
        }
        if (!ChaosGpu.Paint(g, ctx, gpuSphere, gpuEdges)) {
        var points = new PointF[sphere.Length];
        var tones = new double[points.Length];
        double yaw = time * .12, tilt = time * .06, extent = 0;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), ct = Math.Cos(tilt), st = Math.Sin(tilt);
        for (int n = 0; n < points.Length; n++) {
            double x = sphere[n][0], y = sphere[n][1], z = sphere[n][2];
            double noise = Simplex(x * 2 + time * .5, y * 2 + time * .5, z * 2 + time * .5);
            double r = radius * (1 + strength * noise), rx = x * cy + z * sy, rz = z * cy - x * sy;
            double px = rx * ct - y * st, py = rx * st + y * ct;
            double perspective = 3 / (3 - rz * r / radius);
            points[n] = new PointF((float)(px * r * perspective), (float)(py * r * perspective));
            extent = Math.Max(extent, Math.Sqrt(points[n].X * points[n].X + points[n].Y * points[n].Y));
            double mix = Math.Max(0, Math.Min(1, (noise * strength + .2) / .7));
            mix = mix * mix * (3 - 2 * mix);
            tones[n] = Math.Min(1, (mix + Math.Pow(1 - z, 2) * .5) * (1 + Math.Max(0, frequency) * .01));
        }
        // Fit the folds as one object, rather than clipping their tips at the circle or copy label.
        double fit = Math.Min(1, (40 - 20 * pill) / extent);
        for (int n = 0; n < points.Length; n++)
            points[n] = new PointF(cx + points[n].X * (float)fit, 48 + points[n].Y * (float)fit);
        var paths = new GraphicsPath[16];
        try {
            for (int i = 0; i < paths.Length; i++) paths[i] = new GraphicsPath();
            foreach (var edge in edges) Edge(paths, points, tones, edge[0], edge[1]);
            for (int i = 0; i < paths.Length; i++) {
                double tone = i / (double)(paths.Length - 1);
                Color color = ctx.Error ? Color.FromArgb(255, (int)(110 + 145 * tone), (int)(65 + 108 * tone), 85)
                    : Color.FromArgb(0, (int)(26 + 178 * tone), (int)(51 + 204 * tone));
                if (!ctx.Error && ctx.Activity < .999f) {
                    double live = Math.Max(0, Math.Min(1, ctx.Activity)), off = 1 - live;
                    double gray = .2126 * color.R + .7152 * color.G + .0722 * color.B;
                    color = Color.FromArgb((int)(color.R * live + gray * .44 * off), (int)(color.G * live + gray * .55 * off), (int)(color.B * live + gray * .67 * off));
                }
                int alpha = (int)((36 + 80 * tone) * light);
                using (var bloom = new Pen(Color.FromArgb(alpha / 10, color), .8f)) g.DrawPath(bloom, paths[i]);
                using (var wire = new Pen(Color.FromArgb(Math.Min(255, alpha), color), .23f - .05f * pill)) g.DrawPath(wire, paths[i]);
            }
        } finally { foreach (var path in paths) if (path != null) path.Dispose(); }
        }
        // These tiny state marks appear only when needed; the sphere itself never carries a mic.
        if (ctx.Error || busy) {
            using (var pen = new Pen(Color.FromArgb(230, cyan), 1.4f)) {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (ctx.Error) { g.DrawLine(pen, cx, 42, cx, 49); g.DrawLine(pen, cx, 53, cx, 53.2f); }
                else {
                    float angle = ctx.Reduced ? -90 : (float)(time * 110 % 360);
                    g.DrawArc(pen, cx - 5, 43, 10, 10, angle, 235);
                }
            }
        }
    }
    static void Edge(GraphicsPath[] paths, PointF[] points, double[] tones, int a, int b) {
        int tone = (int)Math.Max(0, Math.Min(paths.Length - 1, (tones[a] + tones[b]) * paths.Length / 2));
        paths[tone].StartFigure(); paths[tone].AddLine(points[a], points[b]);
    }
    // Scalar port of the Ashima / Ian McEwan GLSL simplex noise used by ChatAni.
    static double Permute(double x) { return Mod289((x * 34 + 1) * x); }
    static double Mod289(double x) { return x - Math.Floor(x / 289) * 289; }
    static double Corner(double ix, double iy, double iz, double x, double y, double z) {
        double m = .6 - x * x - y * y - z * z;
        if (m <= 0) return 0;
        double p = Permute(Permute(Permute(iz) + iy) + ix);
        double j = p - 49 * Math.Floor(p / 49), gx = Math.Floor(j / 7), gy = j - 7 * gx;
        gx = gx * (2.0 / 7) - 13.0 / 14; gy = gy * (2.0 / 7) - 13.0 / 14;
        double gz = 1 - Math.Abs(gx) - Math.Abs(gy);
        if (gz <= 0) { gx -= Math.Floor(gx) * 2 + 1; gy -= Math.Floor(gy) * 2 + 1; }
        double norm = 1.79284291400159 - .85373472095314 * (gx * gx + gy * gy + gz * gz);
        m *= m; return m * m * norm * (gx * x + gy * y + gz * z);
    }
    internal static double Simplex(double x, double y, double z) {
        double skew = (x + y + z) / 3, ix = Math.Floor(x + skew), iy = Math.Floor(y + skew), iz = Math.Floor(z + skew);
        double unskew = (ix + iy + iz) / 6;
        x = x - ix + unskew; y = y - iy + unskew; z = z - iz + unskew;
        double gx = x >= y ? 1 : 0, gy = y >= z ? 1 : 0, gz = z >= x ? 1 : 0;
        double ax = Math.Min(gx, 1 - gz), ay = Math.Min(gy, 1 - gx), az = Math.Min(gz, 1 - gy);
        double bx = Math.Max(gx, 1 - gz), by = Math.Max(gy, 1 - gx), bz = Math.Max(gz, 1 - gy);
        ix = Mod289(ix); iy = Mod289(iy); iz = Mod289(iz);
        return 42 * (Corner(ix, iy, iz, x, y, z)
            + Corner(ix + ax, iy + ay, iz + az, x - ax + 1.0 / 6, y - ay + 1.0 / 6, z - az + 1.0 / 6)
            + Corner(ix + bx, iy + by, iz + bz, x - bx + 1.0 / 3, y - by + 1.0 / 3, z - bz + 1.0 / 3)
            + Corner(ix + 1, iy + 1, iz + 1, x - .5, y - .5, z - .5));
    }
    internal static void Check(string root) {
        string folder = Path.Combine(root, "work"); Directory.CreateDirectory(folder);
        using (var paused = new Bitmap(128, 96)) using (var listening = new Bitmap(128, 96)) {
            using (var g = Graphics.FromImage(paused)) Microphone.DrawShape(g, 0, "idle", false, 0, 0, 0, now: 600, theme: ThemeId.Chaos);
            using (var g = Graphics.FromImage(listening)) Microphone.DrawShape(g, 0, "recording", false, 0, 0, 0, activity: 1, now: 600, theme: ThemeId.Chaos);
            long off = 0, on = 0;
            for (int y = 26; y < 70; y++) for (int x = 42; x < 86; x++) { off += paused.GetPixel(x, y).G; on += listening.GetPixel(x, y).G; }
            if (on < off * 1.5) throw new Exception("CHAOS-TEST: quiet listening must be clearly brighter than paused");
        }
        if (sphere.Length != 2562 || edges.Length != 7680) throw new Exception("CHAOS-TEST: closed four-level icosphere topology");
        if (Frequency(new ThemeContext { State = "recording", Voice = 1, Level = 1, Now = Math.PI * 500 }) > 17.00001)
            throw new Exception("CHAOS-TEST: ordinary normalized levels must not become explosive FFT values");
        using (var frame = new Bitmap(512, 384, PixelFormat.Format32bppPArgb))
        using (var other = new Bitmap(512, 384, PixelFormat.Format32bppPArgb))
        using (var g = Graphics.FromImage(frame)) using (var h = Graphics.FromImage(other)) {
            g.ScaleTransform(4, 4); h.ScaleTransform(4, 4);
            var ctx = new ThemeContext { State = "recording", Activity = 1, Voice = 1, Level = .7f, Now = 600 };
            Microphone.DrawThemeLayer(g, 0, ctx, ThemeId.Chaos);
            ctx.Transcribing = true; Microphone.DrawThemeLayer(h, 0, ctx, ThemeId.Chaos);
            if (Different(frame, other) != 0) throw new Exception("CHAOS-TEST: parallel transcription must not hide audio motion");
            h.Clear(Color.Transparent); ctx.Level = 0; Microphone.DrawThemeLayer(h, 0, ctx, ThemeId.Chaos);
            if (Different(frame, other) < 200) throw new Exception("CHAOS-TEST: measured sound must change the sphere");
            g.Clear(Color.Transparent); h.Clear(Color.Transparent); ctx.Reduced = true;
            Microphone.DrawThemeLayer(g, 0, ctx, ThemeId.Chaos);
            ctx.Now = 9000; Microphone.DrawThemeLayer(h, 0, ctx, ThemeId.Chaos);
            if (Different(frame, other) != 0) throw new Exception("CHAOS-TEST: reduced motion must freeze rotation and displacement");
            if (Math.Abs(Simplex(.2, .3, .4) - Simplex(.20001, .3, .4)) > .001)
                throw new Exception("CHAOS-TEST: noise must remain continuous across adjacent surface points");
            ctx.Reduced = false; ctx.Level = 1;
            for (int n = 0; n < 24; n++) {
                g.Clear(Color.Transparent); ctx.Now = n * 173; ctx.Shape = n % 3 * .5;
                Microphone.DrawThemeLayer(g, ctx.Shape, ctx, ThemeId.Chaos);
                for (int x = 0; x < frame.Width; x++)
                    if (frame.GetPixel(x, 0).A != 0 || frame.GetPixel(x, frame.Height - 1).A != 0)
                        throw new Exception("CHAOS-TEST: loud deformation clips the vertical surface edge");
                for (int y = 0; y < frame.Height; y++)
                    if (frame.GetPixel(0, y).A != 0 || frame.GetPixel(frame.Width - 1, y).A != 0)
                        throw new Exception("CHAOS-TEST: loud deformation clips the horizontal surface edge");
            }
            ctx.Reduced = false; ctx.Level = .7f;
            var clock = Stopwatch.StartNew();
            for (int n = 0; n < 60; n++) {
                g.Clear(Color.Transparent); ctx.Now = n * 33; ctx.Shape = n % 3 * .5;
                Microphone.DrawThemeLayer(g, ctx.Shape, ctx, ThemeId.Chaos);
            }
            double ms = clock.Elapsed.TotalMilliseconds / 60;
            if (ms > 33) throw new Exception("CHAOS-TEST: mesh exceeds 30fps frame budget: " + ms);
            File.WriteAllText(Path.Combine(folder, "chaos-theme-test.txt"), "PASS: audio response / parallel transcription / reduced motion / continuous simplex folds / loud circle-morph-capsule edge safety / 60 frames at 4x, mean ms=" + ms.ToString("F3", CultureInfo.InvariantCulture));
        }
        using (var sheet = new Bitmap(768, 384)) using (var g = Graphics.FromImage(sheet)) {
            for (int row = 0; row < 2; row++) for (int col = 0; col < 6; col++) {
                var saved = g.Save(); g.TranslateTransform(col * 128, row * 192);
                using (var background = new SolidBrush(row == 0 ? Color.White : Color.FromArgb(24, 29, 36)))
                    g.FillRectangle(background, 0, 0, 128, 192);
                var ctx = new ThemeContext { State = "recording", Activity = 1, Voice = 1, Level = col / 5f, Now = col * 330 };
                Microphone.DrawThemeLayer(g, 0, ctx, ThemeId.Chaos);
                g.TranslateTransform(0, 96); ctx.Shape = col / 5.0;
                Microphone.DrawThemeLayer(g, ctx.Shape, ctx, ThemeId.Chaos);
                g.Restore(saved);
            }
            sheet.Save(Path.Combine(folder, "chaos-motion-sheet.png"));
        }
        string preview = Path.Combine(folder, "chaos-preview"); Directory.CreateDirectory(preview);
        using (var frame = new Bitmap(256, 192)) using (var g = Graphics.FromImage(frame)) {
            g.ScaleTransform(2, 2);
            for (int n = 0; n < 72; n++) {
                g.Clear(Color.FromArgb(24, 29, 36));
                float level = n >= 15 && n <= 55 ? .6f * (float)Math.Sin((n - 15) * Math.PI / 40) : 0;
                var ctx = new ThemeContext { State = "recording", Activity = 1, Voice = 1, Level = level, Now = 10000 + n * 1000.0 / 24 };
                Microphone.DrawThemeLayer(g, 0, ctx, ThemeId.Chaos);
                frame.Save(Path.Combine(preview, n.ToString("D3") + ".png"));
            }
        }
    }
    static int Different(Bitmap a, Bitmap b) {
        int changed = 0;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) changed++;
        return changed;
    }
    internal static void Status(Graphics g, ThemeContext ctx) {
        bool busy = ctx.State == "busy" || ctx.State == "loading";
        string text = ctx.Error && ctx.Stashed ? "重试" : busy || ctx.Transcribing ? "转写中" : ctx.Pending ? "监听中"
            : ctx.Stashed ? "未开启" : ctx.Copied ? "已复制" : "复制";
        using (var font = new Font("Microsoft YaHei UI", 9.5f))
        using (var ink = new SolidBrush(Color.FromArgb(218, 249, 255)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(text, font, ink, new RectangleF(45, 0, 57, 42), format);
        using (var pen = new Pen(Color.FromArgb((int)(28 + 45 * ctx.Hover), 82, 221, 239), .7f))
            g.DrawLine(pen, 44, 12, 44, 30);
    }
}

// The menu row that opens the picker. It draws its own chevron so the sub menu
// reads as a sub menu in every renderer state.
sealed class ThemeMenuEntry : ToolStripMenuItem
{
    internal ThemeMenuEntry(Font font) {
        Text = "主题";
        Image = MenuIcons.Get("theme", MicrophoneMenuRenderer.Ink);
        Tag = MicrophoneMenuRenderer.Ink;
        AutoSize = false;
        Padding = Padding.Empty;
        AccessibleName = "主题";
        // The extra room on the right holds the chevron.
        Size = new Size(30 + TextRenderer.MeasureText(Text, font).Width + 30, 34);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(MicrophoneMenuRenderer.Ink, 1.4f)) {
            pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            float x = Width - 14, y = Height / 2f;
            e.Graphics.DrawLines(pen, new[] { new PointF(x - 2.8f, y - 3.2f), new PointF(x, y), new PointF(x - 2.8f, y + 3.2f) });
        }
    }
}

// One row of the picker: a live miniature of the real skin, its name, one line
// describing it and a marker for the skin in use.
sealed class ThemeChoiceItem : ToolStripMenuItem
{
    internal ThemeDefinition Definition;
    internal ThemeId Id;
    internal bool Current;
    internal double Phase;
    readonly Font nameFont, roleFont;

    internal ThemeChoiceItem(ThemeDefinition definition, ThemeId current, Font font) {
        Definition = definition; Id = definition.Id; Current = definition.Id == current;
        Text = ""; AutoSize = false; Padding = Padding.Empty;
        nameFont = new Font(font, FontStyle.Regular);
        roleFont = new Font(font.FontFamily, 8f, FontStyle.Regular);
        AccessibleName = definition.Name + "，" + definition.Role;
        Size = new Size(266, 50);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float top = (Height - 40) / 2f;
        ThemeArt.Preview(g, Id, new RectangleF(8, top, 40, 40), Phase);
        using (var brush = new SolidBrush(Current ? Color.FromArgb(24, 54, 92) : MicrophoneMenuRenderer.Ink))
            g.DrawString(Definition.Name, nameFont, brush, new PointF(56, top + 4));
        using (var brush = new SolidBrush(Color.FromArgb(104, 113, 122)))
            g.DrawString(Definition.Role, roleFont, brush, new RectangleF(56, top + 23, Width - 56 - 28, 14));
        if (Current) {
            float cx = Width - 18, cy = Height / 2f;
            using (var brush = new SolidBrush(Color.FromArgb(56, 118, 190))) g.FillEllipse(brush, cx - 3, cy - 3, 6, 6);
            using (var pen = new Pen(Color.FromArgb(88, 56, 118, 190), 1f)) g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
        }
    }
    protected override void Dispose(bool disposing) {
        if (disposing) { nameFont.Dispose(); roleFont.Dispose(); }
        base.Dispose(disposing);
    }
}

// The theme picker itself: a drop down on the microphone menu whose rows preview
// each skin in place. It never takes focus away from the window the user is
// typing in and it never starts a recording.
sealed class ThemePicker : IDisposable
{
    internal readonly ThemeMenuEntry Entry;
    internal readonly ToolStripDropDown Drop;
    readonly List<ThemeChoiceItem> items = new List<ThemeChoiceItem>();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 33 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Font font;
    internal Action<ThemeId> Picked;
    internal ThemeId Current;

    internal ThemePicker(ThemeId current, Font menuFont, Action<ThemeId> picked) {
        Current = current; Picked = picked;
        font = new Font(menuFont.FontFamily, menuFont.Size, FontStyle.Regular);
        Entry = new ThemeMenuEntry(font);
        Drop = new ToolStripDropDown();
        Drop.Padding = new Padding(4);
        Drop.Font = font;
        Drop.Renderer = new MicrophoneMenuRenderer();
        Drop.BackColor = MicrophoneMenuRenderer.Surface;
        Entry.DropDown = Drop;
        foreach (ThemeDefinition definition in ThemeCatalog.All) {
            var row = new ThemeChoiceItem(definition, current, font);
            row.Click += (s, e) => { if (Picked != null) Picked(row.Id); };
            items.Add(row);
            Drop.Items.Add(row);
        }
        Entry.DropDownOpening += (s, e) => { if (!timer.Enabled) timer.Start(); };
        Entry.DropDownClosed += (s, e) => { timer.Stop(); };
        Drop.HandleCreated += (s, e) => MicrophoneMenuRenderer.SmoothCorners(Drop);
        Drop.Opened += (s, e) => MicrophoneMenuRenderer.SmoothCorners(Drop);
        timer.Tick += (s, e) => {
            double seconds = clock.Elapsed.TotalSeconds;
            foreach (ThemeChoiceItem row in items) { row.Phase = seconds; row.Invalidate(); }
        };
    }
    internal void SetCurrent(ThemeId id) {
        Current = id;
        foreach (ThemeChoiceItem row in items) {
            bool value = row.Id == id;
            if (row.Current == value) continue;
            row.Current = value; row.Invalidate();
        }
    }
    public void Dispose() {
        timer.Stop(); timer.Dispose();
        foreach (ThemeChoiceItem row in items) row.Dispose();
        items.Clear();
        font.Dispose();
    }
}
