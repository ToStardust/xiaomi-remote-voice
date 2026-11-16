using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Automation;

internal static partial class Program
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const int VK_MENU = 0x12, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
    const int WM_MOUSEWHEEL = 0x20A, GA_ROOT = 2;
    const uint KEYUP = 2;
    static IntPtr hook;
    static HookProc? hookCallback;
    static bool altDown;
    static bool finished;
    static string? marker;
    static System.Windows.Forms.Timer? timeout;
    static readonly object gate = new();

    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        string mode = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").ToLowerInvariant();
        if (mode == "watchdog") { Watchdog(); return; }
        if (mode == "focus") { FocusLearnedInput(false); return; }
        if (mode == "focuslearn") { FocusLearnedInput(true); return; }
        if (mode == "settings") { Application.Run(new InputSettingsForm()); return; }
        if (mode == "focusbridge") { RunInputBridge(); return; }
        if (mode == "mouse") { ToggleMouseMode(); return; }
        if (mode == "mousepreview") { var args = Environment.GetCommandLineArgs(); if (args.Length == 2) MouseNotice.SavePreview(args[1]); return; }
        if (mode == "previous") { if (MouseModeActive()) MovePointer(0, -36); else SwitchChat(VK_LEFT); return; }
        if (mode == "next") { if (MouseModeActive()) MovePointer(0, 36); else SwitchChat(VK_RIGHT); return; }
        if (mode == "scrollup") { ScrollCodex(600); return; }
        if (mode == "scrolldown") { ScrollCodex(-600); return; }
        if (mode != "switcher") return;
        using var mutex = new Mutex(true, SharedBuild ? @"Local\XiaomiRemoteAppSwitcher-share" : @"Local\XiaomiRemoteAppSwitcher", out bool acquired);
        if (!acquired) return;
        try { RunSwitcher(); }
        finally { ReleaseAlt(); }
    }

    static bool IsCodex(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint pid);
        try {
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    static void Key(byte key, bool up = false) => keybd_event(key, 0, up ? KEYUP : 0, UIntPtr.Zero);
    static void Tap(byte key) { Key(key); Thread.Sleep(25); Key(key, true); }
    static void ReleaseAlt() { lock (gate) { if (altDown) { Key(VK_MENU, true); altDown = false; } if (marker != null) { try { File.Delete(marker); } catch { } marker = null; } } }

    static void FocusCodex()
    {
        if (!IsCodex(GetForegroundWindow())) return;
        Key(VK_MENU); Thread.Sleep(25); Tap((byte)'L'); Thread.Sleep(25); Key(VK_MENU, true);
    }

    static void ScrollCodex(int amount)
    {
        IntPtr foreground = GetForegroundWindow();
        if (!IsCodex(foreground) || !GetWindowRect(foreground, out RECT rect)) return;
        try {
            var attempt = Task.Run(() => FindAccessibleScroller(foreground));
            if (attempt.Wait(1800) && attempt.Result is AutomationElement scroller &&
                scroller.TryGetCurrentPattern(ScrollPattern.Pattern, out object pattern)) {
                var scroll = (ScrollPattern)pattern;
                if (scroll.Current.VerticallyScrollable) {
                    scroll.Scroll(ScrollAmount.NoAmount, amount > 0 ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement);
                    return;
                }
            }
        } catch { /* A non-exposing app may use the window-message fallback. */ }
        int x = rect.Left + (rect.Right - rect.Left) * 65 / 100;
        int y = rect.Top + (rect.Bottom - rect.Top) * 42 / 100;
        IntPtr child = WindowFromPoint(new POINT { X = x, Y = y });
        if (child == IntPtr.Zero || GetAncestor(child, GA_ROOT) != foreground) return;
        IntPtr wp = (IntPtr)unchecked((int)((uint)(ushort)(short)amount << 16));
        IntPtr lp = (IntPtr)unchecked((int)(((uint)(ushort)y << 16) | (ushort)x));
        PostMessage(child, WM_MOUSEWHEEL, wp, lp);
    }

    static AutomationElement? FindAccessibleScroller(IntPtr foreground)
    {
        try {
            var root = AutomationElement.FromHandle(foreground);
            var names = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, "对话"),
                new PropertyCondition(AutomationElement.NameProperty, "Conversation"));
            var conversation = root.FindFirst(TreeScope.Descendants, names);
            if (conversation == null) return null;
            var scroller = conversation.TryGetCurrentPattern(ScrollPattern.Pattern, out _) ? conversation :
                conversation.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            return scroller;
        } catch (ElementNotAvailableException) { return null; }
          catch (InvalidOperationException) { return null; }
          catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    static void RunSwitcher()
    {
        StopMouseMode();
        string watchdog = Path.Combine(AppContext.BaseDirectory, "watchdog.exe");
        if (!File.Exists(watchdog)) return;
        marker = Path.Combine(Path.GetTempPath(), "xiaomi-switcher-" + Guid.NewGuid().ToString("N") + ".lock");
        File.WriteAllText(marker, "active");
        try {
            var start = new ProcessStartInfo(watchdog) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(marker);
            if (Process.Start(start) == null) return;
        } catch { return; }
        hookCallback = Hook;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookCallback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) return;
        Application.ApplicationExit += (_, _) => ReleaseAlt();
        timeout = new System.Windows.Forms.Timer { Interval = 15000 };
        timeout.Tick += (_, _) => Finish(cancel: true);
        try {
            // Wait until the remote's triggering keys have been released.
            Thread.Sleep(150);
            lock (gate) { Key(VK_MENU); altDown = true; }
            Thread.Sleep(40); Tap(VK_TAB);
            timeout.Start();
            Application.Run();
        } finally {
            timeout.Stop(); timeout.Dispose();
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            ReleaseAlt();
        }
    }

    static void Watchdog()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length != 3 || !int.TryParse(args[1], out int pid)) return;
        string expectedPrefix = Path.Combine(Path.GetTempPath(), "xiaomi-switcher-");
        string file = Path.GetFullPath(args[2]);
        if (!file.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return;
        try { using var parent = Process.GetProcessById(pid); parent.WaitForExit(20000); } catch { }
        if (!File.Exists(file)) return;
        Key(VK_MENU, true);
        try { File.Delete(file); } catch { }
    }

    static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !finished && (wParam.ToInt64() == WM_KEYDOWN || wParam.ToInt64() == WM_SYSKEYDOWN)) {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == VK_RETURN) { Finish(false); return (IntPtr)1; }
            if (vk == VK_ESCAPE) { Finish(true); return (IntPtr)1; }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    static void Finish(bool cancel)
    {
        if (finished) return;
        finished = true;
        timeout?.Stop();
        if (cancel) { Key(VK_ESCAPE); Key(VK_ESCAPE, true); }
        ReleaseAlt();
        if (!cancel) {
            var focus = new System.Windows.Forms.Timer { Interval = 350 };
            focus.Tick += (_, _) => { focus.Stop(); focus.Dispose(); FocusLearnedInput(false, interactive: false); Application.ExitThread(); };
            focus.Start();
        } else Application.ExitThread();
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, int flags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
