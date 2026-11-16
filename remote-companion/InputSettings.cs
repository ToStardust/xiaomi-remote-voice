using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static partial class Program
{
    sealed class InputSettingsForm : Form
    {
        readonly ListView apps = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        readonly Label info = new() { Dock = DockStyle.Top, Height = 65, Padding = new Padding(12, 8, 12, 4), Text = "选择已打开的软件。可以学习输入框，也可以指定该软件已有的“聚焦输入框”快捷键。" };
        public InputSettingsForm()
        {
            Text = "遥控器输入框设置"; Width = 760; Height = 460; StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(620, 340);
            apps.Columns.Add("软件", 160); apps.Columns.Add("窗口", 340); apps.Columns.Add("输入框", 145);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
            var refresh = new Button { Text = "刷新列表", Width = 105, Height = 30 };
            var learn = new Button { Text = "学习或重新学习", Width = 140, Height = 30 };
            var shortcut = new Button { Text = "设置聚焦快捷键", Width = 140, Height = 30 };
            var close = new Button { Text = "关闭", Width = 80, Height = 30 };
            actions.Controls.AddRange(new Control[] { refresh, learn, shortcut, close });
            Controls.Add(apps); Controls.Add(info); Controls.Add(actions);
            refresh.Click += (_, _) => RefreshWindows();
            learn.Click += (_, _) => StartLearning();
            shortcut.Click += (_, _) => SetShortcut();
            close.Click += (_, _) => Close();
            Shown += (_, _) => RefreshWindows();
        }

        void RefreshWindows()
        {
            apps.Items.Clear();
            EnumWindowsCallback callback = (window, _) => {
                try {
                    if (!IsWindowVisible(window) || window == Handle || GetWindowTextLength(window) == 0) return true;
                    GetWindowThreadProcessId(window, out uint pid);
                    if (pid == Environment.ProcessId) return true;
                    using var process = Process.GetProcessById((int)pid);
                    string? profile = ProfilePath(window);
                    if (profile == null || process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true;
                    var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
                    if (string.IsNullOrWhiteSpace(title.ToString())) return true;
                    string status = IsCodex(window) ? "内置快捷键" : File.Exists(profile) ? "已配置" : "未配置";
                    var item = new ListViewItem(process.ProcessName) { Tag = window };
                    item.SubItems.Add(title.ToString()); item.SubItems.Add(status); apps.Items.Add(item);
                } catch { }
                return true;
            };
            EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback);
        }

        void SetShortcut()
        {
            if (apps.SelectedItems.Count != 1 || apps.SelectedItems[0].Tag is not IntPtr window) { MessageBox.Show(this, "请先选择一个已打开的软件窗口。", Text); return; }
            if (IsCodex(window)) { MessageBox.Show(this, "Codex 使用内置快捷键，不需要设置。", Text); return; }
            string? file = ProfilePath(window);
            if (file == null) { MessageBox.Show(this, "该窗口已关闭，请刷新列表后重试。", Text); return; }
            using var dialog = new ShortcutForm();
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var profile = new InputProfile("hotkey", -2, dialog.VirtualKey.ToString(), dialog.Modifiers.ToString(), Array.Empty<int>());
                File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(profile));
                MessageBox.Show(this, "已保存。请确保目标软件中已将同一快捷键绑定到聚焦输入框的命令。", Text);
                RefreshWindows();
            } catch { MessageBox.Show(this, "保存失败，请重试。", Text); }
        }

        void StartLearning()
        {
            if (apps.SelectedItems.Count != 1 || apps.SelectedItems[0].Tag is not IntPtr window) { MessageBox.Show(this, "请先选择一个已打开的软件窗口。", Text); return; }
            if (IsCodex(window)) { MessageBox.Show(this, "Codex 已使用内置快捷键聚焦输入框，无需重新学习。", Text); return; }
            string? file = ProfilePath(window);
            if (file == null) { MessageBox.Show(this, "该窗口已关闭，请刷新列表后重试。", Text); RefreshWindows(); return; }
            Hide();
            if (IsIconic(window)) ShowWindow(window, 9);
            SetForegroundWindow(window);
            Thread.Sleep(200);
            if (GetForegroundWindow() != window) { Show(); MessageBox.Show(this, "无法切换到目标窗口，请先打开该窗口并重试。", Text); return; }
            LearnInput(window, file);
            Close();
        }
    }

    sealed class ShortcutForm : Form
    {
        readonly TextBox capture = new() { ReadOnly = true, Dock = DockStyle.Top, Height = 28, PlaceholderText = "点击此处并按下组合键" };
        readonly Button save = new() { Text = "保存", Width = 90, Enabled = false };
        public int VirtualKey { get; private set; }
        public int Modifiers { get; private set; }
        public ShortcutForm()
        {
            Text = "设置聚焦快捷键"; Width = 480; Height = 180; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var help = new Label { Dock = DockStyle.Top, Height = 50, Text = "请先在目标软件中设置聚焦命令的快捷键，然后在下方按相同的组合键。", Padding = new Padding(8) };
            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(8) };
            var cancel = new Button { Text = "取消", Width = 90, DialogResult = DialogResult.Cancel };
            save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            panel.Controls.Add(save); panel.Controls.Add(cancel);
            Controls.Add(capture); Controls.Add(help); Controls.Add(panel);
            capture.KeyDown += (_, e) => {
                e.SuppressKeyPress = true;
                Keys code = e.KeyCode;
                bool allowed = (code >= Keys.A && code <= Keys.Z) || (code >= Keys.D0 && code <= Keys.D9) || (code >= Keys.F1 && code <= Keys.F12);
                int mods = (e.Control ? 1 : 0) | (e.Alt ? 2 : 0) | (e.Shift ? 4 : 0);
                if (!allowed || mods == 0) { save.Enabled = false; capture.Text = "请使用 Ctrl、Alt、Shift 与字母、数字或 F 键的组合"; return; }
                VirtualKey = (int)code; Modifiers = mods;
                capture.Text = (e.Control ? "Ctrl + " : "") + (e.Alt ? "Alt + " : "") + (e.Shift ? "Shift + " : "") + code;
                save.Enabled = true;
            };
        }
    }

    delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")] static extern int GetWindowTextLength(IntPtr hwnd);
}
