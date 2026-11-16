using System.IO.Pipes;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static partial class Program
{
    const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    const int VK_CONTROL = 0x11, WM_KEYUP = 0x101, WM_SYSKEYUP = 0x105;
    const uint LLKHF_INJECTED = 0x10;
    static bool SharedBuild => Environment.GetEnvironmentVariable("VIBE_REMOTE_SHARE") == "1";
    static string MouseMutexName => SharedBuild ? @"Local\XiaomiRemoteMouseMode-share" : @"Local\XiaomiRemoteMouseMode";
    static string MouseStopName => SharedBuild ? @"Local\XiaomiRemoteMouseStop-share" : @"Local\XiaomiRemoteMouseStop";
    static string PipeName => SharedBuild ? "XiaomiRemoteMouse-share-v1" : "XiaomiRemoteMouse-v3";
    static IntPtr mouseHook;
    static HookProc? mouseHookCallback;

    static bool MouseModeActive() { if (!EventWaitHandle.TryOpenExisting(MouseStopName, out var e)) return false; e.Dispose(); return true; }
    static void StopMouseMode() { if (EventWaitHandle.TryOpenExisting(MouseStopName, out var e)) { using (e) e.Set(); for (int i = 0; i < 20 && MouseModeActive(); i++) Thread.Sleep(25); } }
    static void SwitchChat(byte arrow) { Key(VK_CONTROL); Key(VK_MENU); Thread.Sleep(25); Tap(arrow); Thread.Sleep(25); Key(VK_MENU, true); Key(VK_CONTROL, true); }
    static void MovePointer(int dx, int dy) { if (!GetCursorPos(out POINT p)) return; var b = SystemInformation.VirtualScreen; SetCursorPos(Math.Clamp(p.X + dx, b.Left, b.Right - 1), Math.Clamp(p.Y + dy, b.Top, b.Bottom - 1)); }
    static void ClickMouse() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }

    static void ToggleMouseMode()
    {
        if (MouseModeActive()) { StopMouseMode(); return; }
        using var mutex = new Mutex(true, MouseMutexName, out bool acquired);
        if (!acquired) return;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, MouseStopName);
        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var cancel = new CancellationTokenSource();
        int idleSeconds = ReadMouseIdleSeconds();
        using var notice = new MouseNotice(idleSeconds);
        notice.FormClosed += (_, _) => Application.ExitThread();
        var down = new Dictionary<string, long>();
        long okDown = 0, firstClickUntil = 0, nextSettingsRead = 0;
        var idle = new IdleClock(Environment.TickCount64);
        bool okHeld = false;
        using var timer = new System.Windows.Forms.Timer { Interval = 35 };
        timer.Tick += (_, _) => {
            long now = Environment.TickCount64;
            if (now >= nextSettingsRead) { nextSettingsRead = now + 1000; int updated = ReadMouseIdleSeconds(); if (updated != idleSeconds) { idleSeconds = updated; notice.SetIdleSeconds(updated); } }
            bool held = down.Count > 0 || okDown != 0;
            if (held) idle.Activity(now);
            if (stop.WaitOne(0) || idle.Expired(now, idleSeconds, held)) { Application.ExitThread(); return; }
            if (okDown != 0 && !okHeld && now - okDown >= 600) { okHeld = true; Application.ExitThread(); return; }
            if (firstClickUntil != 0 && now >= firstClickUntil) { firstClickUntil = 0; ClickMouse(); }
            foreach (var entry in down.ToArray()) {
                if (now - entry.Value < 300) continue;
                int step = now - entry.Value > 1200 ? 15 : 9;
                if (entry.Key == "left") MovePointer(-step, 0); else if (entry.Key == "right") MovePointer(step, 0);
                else if (entry.Key == "up") MovePointer(0, -step); else if (entry.Key == "down") MovePointer(0, step);
            }
        };
        void Handle(string line) {
            if (line.Length > 20) return;
            string[] parts = line.Split(' ');
            if (parts.Length != 2 || (parts[1] != "1" && parts[1] != "0")) return;
            bool pressed = parts[1] == "1"; string button = parts[0]; long now = Environment.TickCount64;
            if (button is not ("ok" or "left" or "right" or "up" or "down")) return;
            idle.Activity(now);
            if (button == "ok") {
                if (pressed) { if (okDown == 0) { okDown = now; okHeld = false; } }
                else if (okDown != 0) { okDown = 0; if (!okHeld) { if (firstClickUntil != 0 && now <= firstClickUntil) { firstClickUntil = 0; ClickMouse(); Thread.Sleep(60); ClickMouse(); } else firstClickUntil = now + 300; } }
            } else if (button is "left" or "right" or "up" or "down") {
                if (pressed && !down.ContainsKey(button)) { down[button] = now; if (button == "left") MovePointer(-8, 0); else if (button == "right") MovePointer(8, 0); else if (button == "up") MovePointer(0, -8); else MovePointer(0, 8); }
                else if (!pressed) down.Remove(button);
            }
        }
        _ = Task.Run(async () => {
            try {
                await pipe.WaitForConnectionAsync(cancel.Token);
                using var reader = new StreamReader(pipe);
                while (!cancel.IsCancellationRequested) {
                    string? line = await reader.ReadLineAsync(cancel.Token);
                    if (line == null) break;
                    if (!notice.IsDisposed && notice.IsHandleCreated) notice.BeginInvoke(() => Handle(line));
                }
                if (!cancel.IsCancellationRequested && !notice.IsDisposed && notice.IsHandleCreated) notice.BeginInvoke(Application.ExitThread);
            } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
              catch { if (!notice.IsDisposed && notice.IsHandleCreated) notice.BeginInvoke(Application.ExitThread); }
        });
        mouseHookCallback = MouseHook;
        mouseHook = SetWindowsHookEx(WH_KEYBOARD_LL, mouseHookCallback, GetModuleHandle(null), 0);
        try { notice.Show(); timer.Start(); Application.Run(); }
        finally { cancel.Cancel(); pipe.Dispose(); timer.Stop(); if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)] struct KeyboardHookData { public uint vkCode, scanCode, flags, time; public UIntPtr extraInfo; }
    static IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam) {
        if (code >= 0 && (wParam.ToInt64() == WM_KEYDOWN || wParam.ToInt64() == WM_SYSKEYDOWN)) {
            var k = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            if ((k.flags & LLKHF_INJECTED) == 0 && k.vkCode == VK_ESCAPE) Application.ExitThread();
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }
    sealed class MouseNotice : Form {
        int idleSeconds;
        float S => DeviceDpi / 96f;
        public MouseNotice(int seconds) {
            idleSeconds = seconds; Text = "遥控鼠标模式"; StartPosition = FormStartPosition.Manual;
            TopMost = true; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(19, 29, 45); DoubleBuffered = true;
        }
        void LayoutOverlay() {
            Size = new Size((int)(360 * S), (int)(132 * S));
            var area = Screen.PrimaryScreen!.WorkingArea; Left = area.Right - Width - (int)(20 * S); Top = area.Bottom - Height - (int)(20 * S);
            int radius = (int)(16 * S), diameter = radius * 2;
            using var path = new GraphicsPath(); var r = ClientRectangle; r.Width--; r.Height--;
            path.AddArc(r.Left, r.Top, diameter, diameter, 180, 90); path.AddArc(r.Right - diameter, r.Top, diameter, diameter, 270, 90);
            path.AddArc(r.Right - diameter, r.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(r.Left, r.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
            Region?.Dispose(); Region = new Region(path); Invalidate();
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); LayoutOverlay(); }
        public static void SavePreview(string file) {
            using var preview = new MouseNotice(60); preview.CreateControl(); preview.LayoutOverlay();
            using var bitmap = new Bitmap(preview.ClientSize.Width, preview.ClientSize.Height);
            preview.DrawToBitmap(bitmap, preview.ClientRectangle); bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); LayoutOverlay(); }
        public void SetIdleSeconds(int seconds) { idleSeconds = seconds; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float s = S;
            using var accent = new SolidBrush(Color.FromArgb(61, 201, 225));
            using var title = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
            using var body = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            using var small = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            g.FillEllipse(accent, 18*s, 21*s, 9*s, 9*s);
            TextRenderer.DrawText(g, "鼠标模式", title, new Rectangle((int)(35*s), (int)(12*s), (int)(240*s), (int)(29*s)), Color.FromArgb(240, 247, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "方向键  短按微调 · 长按移动", body, new Rectangle((int)(18*s), (int)(43*s), (int)(325*s), (int)(23*s)), Color.FromArgb(200, 214, 231), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "确认键  单击 / 双击 · 长按退出", body, new Rectangle((int)(18*s), (int)(68*s), (int)(325*s), (int)(23*s)), Color.FromArgb(200, 214, 231), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            string duration = idleSeconds % 60 == 0 ? $"{idleSeconds / 60} 分钟" : $"{idleSeconds} 秒";
            TextRenderer.DrawText(g, $"空闲 {duration} 自动退出  ·  Esc 退出", small, new Rectangle((int)(18*s), (int)(101*s), (int)(325*s), (int)(20*s)), Color.FromArgb(130, 154, 180), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using var pen = new Pen(Color.FromArgb(153, 174, 196), Math.Max(1.2f*s, 1));
            g.DrawLine(pen, 330*s, 18*s, 340*s, 28*s); g.DrawLine(pen, 340*s, 18*s, 330*s, 28*s);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && e.X >= 316*S && e.Y <= 40*S) Close(); }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
