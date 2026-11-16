using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

internal static partial class Program
{
    sealed record InputProfile(string Process, int Type, string Id, string Class, int[] Path);
    static string RemoteConfigHome => Environment.GetEnvironmentVariable("VIBE_REMOTE_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vibecoding-voice");

    static string? ProfilePath(IntPtr foreground)
    {
        GetWindowThreadProcessId(foreground, out uint pid);
        try {
            using var process = Process.GetProcessById((int)pid);
            string identity = process.MainModule?.FileName ?? "";
            if (identity.Length == 0 || process.ProcessName.Equals("VibeCoding Voice", StringComparison.OrdinalIgnoreCase)) return null;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToLowerInvariant())));
            return Path.Combine(RemoteConfigHome, "remote-input-profiles", hash + ".json");
        } catch { return null; }
    }

    static void FocusLearnedInput(bool forceLearn, bool interactive = true)
    {
        IntPtr window = GetForegroundWindow();
        if (!forceLearn && IsCodex(window)) { FocusCodex(); return; }
        string? file = ProfilePath(window);
        if (file == null) { if (interactive) MessageBox.Show("无法识别当前窗口，请切换到目标应用后重试。", "遥控器输入框"); return; }
        if (!forceLearn && File.Exists(file)) {
            try {
                var profile = JsonSerializer.Deserialize<InputProfile>(File.ReadAllText(file));
                if (profile != null && RestoreInput(window, profile)) return;
            } catch { }
            if (interactive) MessageBox.Show("之前记录的输入框无法安全定位，请打开“遥控器输入框设置”重新学习。", "遥控器输入框");
            return;
        }
        if (!interactive) { if (IsCodex(window)) FocusCodex(); return; }
        LearnInput(window, file);
    }

    static bool IsEditable(AutomationElement element)
    {
        try {
            if (element.Current.IsPassword || !element.Current.IsEnabled || !element.Current.IsKeyboardFocusable) return false;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object p)) return !((ValuePattern)p).Current.IsReadOnly;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern)) {
                var readOnly = ((TextPattern)textPattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                if (readOnly is bool value) return !value;
            }
            return element.Current.ControlType == ControlType.Edit;
        } catch { return false; }
    }

    static AutomationElement? FindQqInputAt(IntPtr window, POINT point)
    {
        GetWindowThreadProcessId(window, out uint pid);
        using var process = Process.GetProcessById((int)pid);
        if (!process.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase)) return null;
        try {
            var root = AutomationElement.FromHandle(window);
            var candidates = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "qq-msg-editor"));
            foreach (AutomationElement e in candidates) {
                if (!e.Current.BoundingRectangle.Contains(new System.Windows.Point(point.X, point.Y))) continue;
                if (IsEditable(e)) return e;
                var found = e.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.IsKeyboardFocusableProperty, true));
                foreach (AutomationElement child in found) if (IsEditable(child)) return child;
            }
        } catch { }
        return null;
    }

    static AutomationElement? FindFocusedInputAt(IntPtr window, POINT point)
    {
        try {
            var root = AutomationElement.FromHandle(window);
            // Request the foreground application's accessibility tree before looking
            // up its focused control; Chromium-based apps may create it lazily.
            var focused = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.HasKeyboardFocusProperty, true));
            var walker = TreeWalker.ControlViewWalker;
            for (int i = 0; focused != null && i < 12; i++, focused = walker.GetParent(focused)) {
                if (IsEditable(focused) && focused.Current.BoundingRectangle.Contains(new System.Windows.Point(point.X, point.Y))) return focused;
                if (Automation.Compare(focused, root)) break;
            }
        } catch { }
        return null;
    }

    static bool RestoreInput(IntPtr window, InputProfile profile)
    {
        if (profile.Type == -1 && IsCodex(window)) { FocusCodex(); return true; }
        if (profile.Type == -2) return SendFocusShortcut(window, profile);
        if (IsQqWindow(window) && HasQqEditorClass(profile.Class)) return RestoreQqInput(window, profile);
        try {
            var root = AutomationElement.FromHandle(window);
            var node = root;
            var walker = TreeWalker.ControlViewWalker;
            foreach (int index in profile.Path) {
                node = walker.GetFirstChild(node);
                for (int i = 0; i < index && node != null; i++) node = walker.GetNextSibling(node);
                if (node == null) break;
            }
            if (node != null) {
                var cur = node.Current;
                if (cur.ControlType.Id == profile.Type && cur.AutomationId == profile.Id && cur.ClassName == profile.Class && IsEditable(node) && !cur.IsOffscreen) { node.SetFocus(); return true; }
            }
            return RestoreGenericInput(root, profile);
        } catch { return false; }
    }

    static bool SendFocusShortcut(IntPtr window, InputProfile profile)
    {
        if (GetForegroundWindow() != window || !int.TryParse(profile.Id, out int vk) || vk < 1 || vk > 255) return false;
        if (!int.TryParse(profile.Class, out int modifiers) || (modifiers & ~7) != 0 || modifiers == 0) return false;
        var keys = new List<byte>();
        if ((modifiers & 1) != 0) keys.Add(0x11);
        if ((modifiers & 2) != 0) keys.Add(0x12);
        if ((modifiers & 4) != 0) keys.Add(0x10);
        foreach (byte k in keys) Key(k);
        try { Thread.Sleep(30); Tap((byte)vk); }
        finally { for (int i = keys.Count - 1; i >= 0; i--) Key(keys[i], true); }
        return true;
    }

    static string[] StableClassTokens(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(t => !t.Contains("focus", StringComparison.OrdinalIgnoreCase) &&
                    !t.Contains("active", StringComparison.OrdinalIgnoreCase) &&
                    !t.Contains("empty", StringComparison.OrdinalIgnoreCase) &&
                    !t.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
                    !t.Contains("selected", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    static bool RestoreGenericInput(AutomationElement root, InputProfile profile)
    {
        var type = ControlType.LookupById(profile.Type);
        if (type == null) return false;
        var tokens = StableClassTokens(profile.Class);
        if (string.IsNullOrEmpty(profile.Id) && tokens.Length == 0) return false;
        var candidates = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        AutomationElement? match = null;
        foreach (AutomationElement candidate in candidates) {
            var c = candidate.Current;
            if (c.IsOffscreen || !IsEditable(candidate)) continue;
            if (!string.IsNullOrEmpty(profile.Id) && c.AutomationId != profile.Id) continue;
            var currentTokens = c.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(token => !currentTokens.Contains(token, StringComparer.Ordinal))) continue;
            if (match != null) return false;
            match = candidate;
        }
        if (match == null) return false;
        match.SetFocus(); return true;
    }

    static bool IsQqWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out uint pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName.Equals("QQ", StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }

    static bool HasQqEditorClass(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("ExEditor-qq-msg-editor", StringComparer.Ordinal);

    static bool RestoreQqInput(IntPtr window, InputProfile profile)
    {
        try {
            var root = AutomationElement.FromHandle(window);
            var type = ControlType.LookupById(profile.Type);
            if (type == null) return false;
            var nodes = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type));
            AutomationElement? target = null;
            foreach (AutomationElement candidate in nodes) {
                var c = candidate.Current;
                if (!HasQqEditorClass(c.ClassName) || c.IsOffscreen || !IsEditable(candidate)) continue;
                if (target != null) return false;
                target = candidate;
            }
            if (target == null) return false;
            target.SetFocus();
            return true;
        } catch { return false; }
    }

    static bool LearnInput(IntPtr window, string file, bool showFailure = true)
    {
        using var mutex = new Mutex(true, SharedBuild ? @"Local\XiaomiRemoteInputLearning-share" : @"Local\XiaomiRemoteInputLearning", out bool acquired);
        if (!acquired) return false;
        using var notice = new LearnNotice();
        notice.Show();
        POINT? clicked = null;
        HookProc captureClick = (code, message, data) => {
            if (code >= 0 && message.ToInt64() == 0x201) clicked = Marshal.PtrToStructure<POINT>(data);
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        IntPtr clickHook = SetWindowsHookEx(14, captureClick, GetModuleHandle(null), 0);
        if (clickHook == IntPtr.Zero) { notice.Close(); if (showFailure) MessageBox.Show("无法监听点击，请重试。", "遥控器输入框"); return false; }
        string reason = "等待点击超时";
        long end = Environment.TickCount64 + 20000;
        try {
        while (Environment.TickCount64 < end && !notice.IsDisposed) {
            Application.DoEvents();
            if (clicked is POINT point) {
                clicked = null;
                IntPtr child = WindowFromPoint(point);
                if (child != IntPtr.Zero && GetAncestor(child, GA_ROOT) == window) {
                    Thread.Sleep(180);
                    try {
                        var root = AutomationElement.FromHandle(window);
                        var node = AutomationElement.FocusedElement;
                        var walker = TreeWalker.ControlViewWalker;
                        if (node == null || !IsEditable(node)) node = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
                        for (int i = 0; node != null && i < 12 && !IsEditable(node); i++) node = walker.GetParent(node);
                        if (node == null || !IsEditable(node)) node = FindFocusedInputAt(window, point);
                        if (node == null || !IsEditable(node)) node = FindQqInputAt(window, point);
                        if (node == null || !IsEditable(node)) { reason = "点击已收到，但系统未暴露可编辑输入框"; LogInputDiagnostic(window, point, reason); if (SaveCodexInputShortcut(window, file, notice)) return true; break; }
                        var current = node.Current;
                        var indices = new List<int>();
                        var cursor = node;
                        while (!Automation.Compare(cursor, root) && indices.Count < 35) {
                            var parent = walker.GetParent(cursor);
                            if (parent == null) break;
                            var sibling = walker.GetFirstChild(parent);
                            int index = 0;
                            while (sibling != null && !Automation.Compare(sibling, cursor) && index < 500) { sibling = walker.GetNextSibling(sibling); index++; }
                            if (sibling == null || index >= 500) break;
                            indices.Add(index); cursor = parent;
                        }
                        if (!Automation.Compare(cursor, root)) { reason = "输入框不属于原窗口的控件树"; LogInputDiagnostic(window, point, reason); if (SaveCodexInputShortcut(window, file, notice)) return true; break; }
                        indices.Reverse();
                        var profile = new InputProfile("current", current.ControlType.Id, current.AutomationId, current.ClassName, indices.ToArray());
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        File.WriteAllText(file, JsonSerializer.Serialize(profile));
                        notice.SetMessage("输入框已记录；下次按菜单键可聚焦");
                        long showUntil = Environment.TickCount64 + 1200;
                        while (Environment.TickCount64 < showUntil && !notice.IsDisposed) { Application.DoEvents(); Thread.Sleep(25); }
                        notice.Close();
                        return true;
                    } catch (Exception ex) { reason = "识别异常：" + ex.GetType().Name; LogInputDiagnostic(window, point, reason); if (SaveCodexInputShortcut(window, file, notice)) return true; break; }
                } else { reason = "点击不在开始学习时的窗口"; LogInputDiagnostic(window, point, reason); }
            }
            Thread.Sleep(25);
        }
        } finally { UnhookWindowsHookEx(clickHook); GC.KeepAlive(captureClick); }
        notice.Close();
        if (showFailure) MessageBox.Show("没有记录输入框（" + reason + "）。请回到目标应用，再按菜单键并点击输入框。", "遥控器输入框");
        return false;
    }

    static void LogInputDiagnostic(IntPtr window, POINT point, string reason)
    {
        try {
            GetWindowThreadProcessId(window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            var e = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            var details = new List<string>();
            for (int i = 0; e != null && i < 6; i++, e = TreeWalker.ControlViewWalker.GetParent(e)) {
                var c = e.Current;
                details.Add($"type={c.ControlType.ProgrammaticName} focusable={c.IsKeyboardFocusable} enabled={c.IsEnabled} password={c.IsPassword} value={e.TryGetCurrentPattern(ValuePattern.Pattern, out _)} text={e.TryGetCurrentPattern(TextPattern.Pattern, out _)}");
            }
            string dir = RemoteConfigHome;
            File.AppendAllText(Path.Combine(dir, "remote-input-diagnostic.log"), $"{DateTime.Now:O} process={process.ProcessName} reason={reason} {string.Join(" | ", details)}{Environment.NewLine}");
        } catch { }
    }

    static bool SaveCodexInputShortcut(IntPtr window, string file, LearnNotice notice)
    {
        if (!IsCodex(window)) return false;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(new InputProfile("current", -1, "", "", Array.Empty<int>())));
            notice.SetMessage("已记录 Codex 输入框快捷键");
            long until = Environment.TickCount64 + 1800;
            while (Environment.TickCount64 < until && !notice.IsDisposed) { Application.DoEvents(); Thread.Sleep(25); }
            notice.Close(); return true;
        } catch { return false; }
    }

    sealed class LearnNotice : Form
    {
        readonly Label message;
        public LearnNotice(string messageText = "请在 20 秒内点击这个应用的输入框") {
            Text = "遥控器学习"; Width = 430; Height = 95;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen!.WorkingArea;
            Left = area.Right - Width - 20; Top = area.Top + 20;
            TopMost = true; ShowInTaskbar = false;
            message = new Label { Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Text = messageText, Font = new System.Drawing.Font("Microsoft YaHei UI", 12) };
            Controls.Add(message);
        }
        public void SetMessage(string text) => message.Text = text;
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
}
