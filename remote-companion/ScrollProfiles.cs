using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

internal static partial class Program
{
    sealed record ScrollProfile(double X, double Y);
    static string? ScrollProfilePath(IntPtr window) { var p = ProfilePath(window); return p == null ? null : p + ".scroll"; }
    static void LearnScroll()
    {
        var window = GetForegroundWindow(); var file = ScrollProfilePath(window);
        if (file == null) { MessageBox.Show("请先切换到目标应用。", "遥控器翻页"); return; }
        using var notice = new LearnNotice("请在 12 秒内点击要翻页的内容区域");
        notice.Show(); bool previous = (GetAsyncKeyState(1) & 0x8000) != 0; long end = Environment.TickCount64 + 12000;
        while (Environment.TickCount64 < end && !notice.IsDisposed) {
            Application.DoEvents(); bool pressed = (GetAsyncKeyState(1) & 0x8000) != 0;
            if (pressed && !previous && GetCursorPos(out POINT p) && GetWindowRect(window, out RECT r)) {
                var child = WindowFromPoint(p); int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (child != IntPtr.Zero && GetAncestor(child, GA_ROOT) == window && w > 0 && h > 0 && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom) {
                    try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, JsonSerializer.Serialize(new ScrollProfile((p.X - r.Left) / (double)w, (p.Y - r.Top) / (double)h))); notice.SetMessage("翻页区域已记录：双击左右键翻页"); long until = Environment.TickCount64 + 1400; while (Environment.TickCount64 < until && !notice.IsDisposed) { Application.DoEvents(); Thread.Sleep(25); } notice.Close(); return; }
                    catch { break; }
                }
            }
            previous = pressed; Thread.Sleep(25);
        }
        notice.Close(); MessageBox.Show("没有记录翻页区域，请回到目标应用后重新操作。", "遥控器翻页");
    }
    static void ScrollLearned(int amount)
    {
        var window = GetForegroundWindow(); var file = ScrollProfilePath(window);
        if (file == null || !File.Exists(file)) { ScrollCodex(amount); return; }
        try {
            var profile = JsonSerializer.Deserialize<ScrollProfile>(File.ReadAllText(file));
            if (profile == null || profile.X < 0 || profile.X > 1 || profile.Y < 0 || profile.Y > 1 || !GetWindowRect(window, out RECT r)) return;
            int x = r.Left + (int)((r.Right - r.Left) * profile.X), y = r.Top + (int)((r.Bottom - r.Top) * profile.Y);
            var child = WindowFromPoint(new POINT { X = x, Y = y });
            if (child == IntPtr.Zero || GetAncestor(child, GA_ROOT) != window) return;
            try {
                var node = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                for (int i = 0; node != null && i < 20; i++, node = TreeWalker.ControlViewWalker.GetParent(node)) {
                    if (node.TryGetCurrentPattern(ScrollPattern.Pattern, out object pat) && ((ScrollPattern)pat).Current.VerticallyScrollable) { ((ScrollPattern)pat).Scroll(ScrollAmount.NoAmount, amount > 0 ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement); return; }
                }
            } catch { }
            IntPtr wp = (IntPtr)unchecked((int)((uint)(ushort)(short)amount << 16));
            IntPtr lp = (IntPtr)unchecked((int)(((uint)(ushort)y << 16) | (ushort)x));
            PostMessage(child, WM_MOUSEWHEEL, wp, lp);
        } catch { }
    }
}
