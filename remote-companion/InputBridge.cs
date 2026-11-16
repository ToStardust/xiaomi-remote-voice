using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static partial class Program
{
    sealed record WindowOption(long Id, uint Pid, string App, string Title, string Status);

    static List<WindowOption> ListInputWindows()
    {
        var results = new List<WindowOption>();
        EnumWindowsCallback callback = (window, _) => {
            try {
                if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0) return true;
                GetWindowThreadProcessId(window, out uint pid);
                if (pid == Environment.ProcessId) return true;
                using var process = Process.GetProcessById((int)pid);
                string? profile = ProfilePath(window);
                if (profile == null || process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true;
                var title = new StringBuilder(512); GetWindowText(window, title, title.Capacity);
                if (string.IsNullOrWhiteSpace(title.ToString())) return true;
                string status = IsCodex(window) ? "内置快捷键" : File.Exists(profile) ? "已配置" : "未配置";
                results.Add(new WindowOption(window.ToInt64(), pid, process.ProcessName, title.ToString(), status));
            } catch { }
            return true;
        };
        EnumWindows(callback, IntPtr.Zero); GC.KeepAlive(callback);
        return results;
    }

    static void RunInputBridge()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length < 3) return;
        string resultPath = args[2];
        try {
            object result;
            if (args[1] == "list" && args.Length == 3) {
                result = new { ok = true, windows = ListInputWindows() };
            } else if (args[1] == "mouse-get" && args.Length == 3) {
                result = new { ok = true, seconds = ReadMouseIdleSeconds() };
            } else if (args[1] == "mouse-set" && args.Length == 4 && int.TryParse(args[3], out int seconds) && seconds is >= 15 and <= 3600) {
                WriteMouseIdleSeconds(seconds); result = new { ok = true, seconds };
            } else if (args[1] == "selftest" && args.Length == 3) {
                var clock = new IdleClock(1000);
                bool passed = ValidMouseIdle(0) == 60 && ValidMouseIdle(600) == 600 && !clock.Expired(60999, 60, false) && clock.Expired(61000, 60, false) && !clock.Expired(999999, 60, true);
                clock.Activity(61000); passed &= !clock.Expired(61001, 60, false) && clock.Expired(121000, 60, false);
                result = new { ok = passed };
            } else if (args.Length >= 5 && long.TryParse(args[3], out long handle) && uint.TryParse(args[4], out uint pid)) {
                var match = ListInputWindows().FirstOrDefault(x => x.Id == handle && x.Pid == pid);
                if (match == null) result = new { ok = false, error = "窗口已关闭或变化，请刷新列表。" };
                else {
                    var window = new IntPtr(handle);
                    string? file = ProfilePath(window);
                    if (file == null) result = new { ok = false, error = "无法识别目标应用。" };
                    else if (IsCodex(window)) result = new { ok = false, error = "Codex 已使用内置快捷键，无需设置。" };
                    else if (args[1] == "learn" && args.Length == 5) {
                        bool saved = LearnInput(window, file, showFailure: false);
                        result = new { ok = saved, error = saved ? "" : "未记录输入框，请重试。" };
                    } else if (args[1] == "shortcut" && args.Length == 7 && int.TryParse(args[5], out int key) && int.TryParse(args[6], out int mods) &&
                               ((key >= 65 && key <= 90) || (key >= 48 && key <= 57) || (key >= 112 && key <= 123)) && mods >= 1 && mods <= 7) {
                        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                        File.WriteAllText(file, JsonSerializer.Serialize(new InputProfile("hotkey", -2, key.ToString(), mods.ToString(), Array.Empty<int>())));
                        result = new { ok = true };
                    } else result = new { ok = false, error = "无效操作。" };
                }
            } else result = new { ok = false, error = "无效参数。" };
            File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
        } catch { try { File.WriteAllText(resultPath, JsonSerializer.Serialize(new { ok = false, error = "设置操作失败，请重试。" })); } catch { } }
    }
}
