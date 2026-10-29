using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The reference shader runs on an offscreen OpenGL surface; no browser or runtime package.
sealed class ChaosGpu : NativeWindow, IDisposable
{
    const int Width = 512, Height = 384;
    IntPtr dc, context;
    int program, timeUniform, frequencyUniform;
    readonly Bitmap image = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
    GCHandle verticesPin, indicesPin;
    int indexCount;
    bool verifiedSurface;
    internal string Renderer;
    static ChaosGpu shared;
    static bool unavailable;
    internal static void Release() { if (shared != null) { shared.Dispose(); shared = null; } }

    [StructLayout(LayoutKind.Sequential)] struct NativePixelFormat {
        public ushort size, version;
        public uint flags;
        public byte pixelType, colorBits, redBits, redShift, greenBits, greenShift, blueBits, blueShift, alphaBits, alphaShift;
        public byte accumBits, accumRed, accumGreen, accumBlue, accumAlpha, depthBits, stencilBits, auxBuffers, layerType, reserved;
        public uint layerMask, visibleMask, damageMask;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateShader(uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void ShaderSource(int shader, int count, IntPtr strings, IntPtr lengths);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void ShaderAction(int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Status(int obj, uint name, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateProgram();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Attach(int program, int shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int UniformLocation(int program, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Uniform(int location, float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Blend(uint sourceRgb, uint destRgb, uint sourceAlpha, uint destAlpha);
    ShaderAction useProgram, deleteProgram;
    Uniform uniform;

    static T Load<T>(string name) where T : class {
        IntPtr pointer = wglGetProcAddress(name);
        long value = pointer.ToInt64();
        if (value == 0 || value == 1 || value == 2 || value == 3 || value == -1) throw new InvalidOperationException("OpenGL entry point missing: " + name);
        return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
    }
    internal static bool Paint(Graphics g, ThemeContext state, double[][] points, int[][] edges) {
        if (unavailable) return false;
        try {
            if (shared == null) shared = new ChaosGpu(points, edges);
            shared.Render(g, state); return true;
        } catch (InvalidOperationException error) {
            if (shared != null) { shared.Dispose(); shared = null; }
            unavailable = true;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "work", "chaos-gpu.txt");
            try { File.WriteAllText(path, "FALLBACK: " + error.Message); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
    }
    ChaosGpu(double[][] points, int[][] edges) {
        try {
            CreateHandle(new CreateParams { Caption = "VoxFix chaos offscreen", X = -20000, Y = -20000, Width = Width, Height = Height,
                ClassStyle = 0x20, Style = unchecked((int)0x80000000) });
            dc = GetDC(Handle);
            var format = new NativePixelFormat { size = (ushort)Marshal.SizeOf(typeof(NativePixelFormat)), version = 1,
                flags = 0x4 | 0x20 | 0x1, colorBits = 32, alphaBits = 8, depthBits = 24 };
            int number = ChoosePixelFormat(dc, ref format);
            if (number == 0 || !SetPixelFormat(dc, number, ref format)) throw new InvalidOperationException("OpenGL pixel format unavailable");
            context = wglCreateContext(dc);
            if (context == IntPtr.Zero || !wglMakeCurrent(dc, context)) throw new InvalidOperationException("OpenGL context unavailable");
            Renderer = Marshal.PtrToStringAnsi(glGetString(0x1F01));
            var create = Load<CreateProgram>("glCreateProgram");
            var attach = Load<Attach>("glAttachShader");
            var link = Load<ShaderAction>("glLinkProgram");
            var status = Load<Status>("glGetProgramiv");
            useProgram = Load<ShaderAction>("glUseProgram"); deleteProgram = Load<ShaderAction>("glDeleteProgram");
            uniform = Load<Uniform>("glUniform1f");
            program = create();
            int vertex = Compile("VoxFix.ChaosVertex", 0x8B31), fragment = 0;
            try {
                fragment = Compile("VoxFix.ChaosFragment", 0x8B30);
                attach(program, vertex); attach(program, fragment); link(program);
                int ok; status(program, 0x8B82, out ok);
                if (ok == 0) throw new InvalidOperationException("Reference shader link failed");
            } finally {
                var delete = Load<ShaderAction>("glDeleteShader"); delete(vertex); if (fragment != 0) delete(fragment);
            }
            var location = Load<UniformLocation>("glGetUniformLocation");
            timeUniform = location(program, "u_time"); frequencyUniform = location(program, "u_frequency");
            if (timeUniform < 0 || frequencyUniform < 0) throw new InvalidOperationException("Reference shader uniforms unavailable");
            var vertices = new float[points.Length * 3];
            for (int n = 0; n < points.Length; n++) for (int c = 0; c < 3; c++) vertices[n * 3 + c] = (float)points[n][c];
            var indices = new uint[edges.Length * 2];
            for (int n = 0; n < edges.Length; n++) { indices[n * 2] = (uint)edges[n][0]; indices[n * 2 + 1] = (uint)edges[n][1]; }
            verticesPin = GCHandle.Alloc(vertices, GCHandleType.Pinned); indicesPin = GCHandle.Alloc(indices, GCHandleType.Pinned);
            indexCount = indices.Length;
            glEnableClientState(0x8074); glEnableClientState(0x8075);
            glVertexPointer(3, 0x1406, 0, verticesPin.AddrOfPinnedObject()); glNormalPointer(0x1406, 0, verticesPin.AddrOfPinnedObject());
            glEnable(0x0BE2); Load<Blend>("glBlendFuncSeparate")(0x0302, 0x0303, 1, 0x0303);
            glDisable(0x0B44); glEnable(0x0B71);
            glDrawBuffer(0x0405); glReadBuffer(0x0405);
            glViewport(0, 0, Width, Height); glClearColor(0, 0, 0, 0);
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "work", "chaos-gpu.txt"),
                "GPU: " + Renderer + " / ChatAni GLSL / vertices=" + points.Length + " / edges=" + edges.Length);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        } catch { Dispose(); throw; }
    }
    static int Compile(string resource, uint type) {
        string source;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) {
            if (stream == null) throw new InvalidOperationException("Missing reference shader " + resource);
            using (var reader = new StreamReader(stream)) source = reader.ReadToEnd();
        }
        int shader = Load<CreateShader>("glCreateShader")(type);
        IntPtr text = Marshal.StringToHGlobalAnsi(source), pointers = Marshal.AllocHGlobal(IntPtr.Size);
        try {
            Marshal.WriteIntPtr(pointers, text); Load<ShaderSource>("glShaderSource")(shader, 1, pointers, IntPtr.Zero);
            Load<ShaderAction>("glCompileShader")(shader);
            int ok; Load<Status>("glGetShaderiv")(shader, 0x8B81, out ok);
            if (ok == 0) throw new InvalidOperationException("Reference shader compilation failed: " + resource);
            return shader;
        } catch { Load<ShaderAction>("glDeleteShader")(shader); throw; }
        finally { Marshal.FreeHGlobal(text); Marshal.FreeHGlobal(pointers); }
    }
    void Render(Graphics g, ThemeContext state) {
        if (!wglMakeCurrent(dc, context)) throw new InvalidOperationException("Cannot activate the offscreen renderer");
        try {
            float time = state.Reduced ? 0 : (float)(state.Now / 1000);
            double audio = state.State == "recording" && !state.Reduced ? Math.Max(0, Math.Min(1, state.Level * state.Voice)) : 0;
            float frequency = (float)ChaosArt.Frequency(state);
            glClear(0x4000 | 0x0100);
            glMatrixMode(0x1701); glLoadIdentity();
            double top = .1 * Math.Tan(28 * Math.PI / 180);
            glFrustum(-top * Width / Height, top * Width / Height, -top, top, .1, 10);
            glMatrixMode(0x1700); glLoadIdentity(); glTranslatef(0, 0, -3);
            float scale = (float)(.98 / (1 + .1 * audio));
            glScalef(scale, scale, scale); glRotatef(time * .06f * 180 / (float)Math.PI, 0, 0, 1); glRotatef(time * .12f * 180 / (float)Math.PI, 0, 1, 0);
            useProgram(program); uniform(timeUniform, time); uniform(frequencyUniform, frequency);
            glDrawElements(1, indexCount, 0x1405, indicesPin.AddrOfPinnedObject());
            var data = image.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { glReadPixels(0, 0, Width, Height, 0x80E1, 0x1401, data.Scan0); } finally { image.UnlockBits(data); }
            image.RotateFlip(RotateFlipType.RotateNoneFlipY);
            if (!verifiedSurface) {
                int visible = 0;
                for (int y = 0; y < Height; y += 8) for (int x = 0; x < Width; x += 8)
                    if (image.GetPixel(x, y).A > 0) visible++;
                if (visible < 20) throw new InvalidOperationException("OpenGL returned an empty surface");
                verifiedSurface = true;
            }
            float size = 1 - .55f * state.Pill, cx = 64 - 29 * state.Pill;
            var target = new RectangleF(cx - 64 * size, 48 - 48 * size, 128 * size, 96 * size);
            float live = Math.Max(0, Math.Min(1, state.Activity)), paused = 1 - live;
            if (state.Error || paused > .001f) using (var attributes = new ImageAttributes()) {
                var matrix = state.Error ? new ColorMatrix { Matrix00 = 0, Matrix10 = 1, Matrix11 = .65f, Matrix22 = .3f }
                    : new ColorMatrix {
                        Matrix00 = live + .0935f * paused, Matrix10 = .3147f * paused, Matrix20 = .0318f * paused,
                        Matrix01 = .1169f * paused, Matrix11 = live + .3934f * paused, Matrix21 = .0397f * paused,
                        Matrix02 = .1424f * paused, Matrix12 = .4792f * paused, Matrix22 = live + .0484f * paused
                    };
                attributes.SetColorMatrix(matrix);
                g.DrawImage(image, new[] { new PointF(target.Left, target.Top), new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom) },
                    new RectangleF(0, 0, Width, Height), GraphicsUnit.Pixel, attributes);
            } else g.DrawImage(image, target);
        } finally { wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); }
    }
    public void Dispose() {
        if (context != IntPtr.Zero) {
            if (wglMakeCurrent(dc, context) && program != 0 && deleteProgram != null) deleteProgram(program);
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(context); context = IntPtr.Zero;
        }
        if (dc != IntPtr.Zero) { ReleaseDC(Handle, dc); dc = IntPtr.Zero; }
        if (Handle != IntPtr.Zero) DestroyHandle();
        if (verticesPin.IsAllocated) verticesPin.Free(); if (indicesPin.IsAllocated) indicesPin.Free(); image.Dispose();
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr dc, ref NativePixelFormat format);
    [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr dc, int number, ref NativePixelFormat format);
    [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr dc);
    [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);
    [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] static extern IntPtr wglGetProcAddress(string name);
    [DllImport("opengl32.dll")] static extern IntPtr glGetString(uint name);
    [DllImport("opengl32.dll")] static extern void glEnableClientState(uint array);
    [DllImport("opengl32.dll")] static extern void glVertexPointer(int size, uint type, int stride, IntPtr pointer);
    [DllImport("opengl32.dll")] static extern void glNormalPointer(uint type, int stride, IntPtr pointer);
    [DllImport("opengl32.dll")] static extern void glEnable(uint what);
    [DllImport("opengl32.dll")] static extern void glDisable(uint what);
    [DllImport("opengl32.dll")] static extern void glDrawBuffer(uint mode);
    [DllImport("opengl32.dll")] static extern void glReadBuffer(uint mode);
    [DllImport("opengl32.dll")] static extern void glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] static extern void glClear(uint bits);
    [DllImport("opengl32.dll")] static extern void glMatrixMode(uint mode);
    [DllImport("opengl32.dll")] static extern void glLoadIdentity();
    [DllImport("opengl32.dll")] static extern void glFrustum(double left, double right, double bottom, double top, double near, double far);
    [DllImport("opengl32.dll")] static extern void glTranslatef(float x, float y, float z);
    [DllImport("opengl32.dll")] static extern void glScalef(float x, float y, float z);
    [DllImport("opengl32.dll")] static extern void glRotatef(float angle, float x, float y, float z);
    [DllImport("opengl32.dll")] static extern void glDrawElements(uint mode, int count, uint type, IntPtr indices);
    [DllImport("opengl32.dll")] static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, IntPtr pixels);
}
