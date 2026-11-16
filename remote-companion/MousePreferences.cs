using System.IO;
using System.Text.Json;

internal static partial class Program
{
    sealed record MousePreferences(int IdleSeconds);
    static string MousePreferencesPath => Path.Combine(Environment.GetEnvironmentVariable("VIBE_REMOTE_CONFIG_HOME") ?? Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vibecoding-voice"), "remote-mouse-settings.json");
    static int ValidMouseIdle(int value) => value is >= 15 and <= 3600 ? value : 60;
    static int ReadMouseIdleSeconds()
    {
        try { return ValidMouseIdle(JsonSerializer.Deserialize<MousePreferences>(File.ReadAllText(MousePreferencesPath))?.IdleSeconds ?? 60); }
        catch { return 60; }
    }
    static void WriteMouseIdleSeconds(int seconds)
    {
        if (seconds is < 15 or > 3600) throw new ArgumentOutOfRangeException(nameof(seconds));
        Directory.CreateDirectory(Path.GetDirectoryName(MousePreferencesPath)!);
        string temp = MousePreferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new MousePreferences(seconds))); File.Move(temp, MousePreferencesPath, true); }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }
    sealed class IdleClock
    {
        public long LastActivity { get; private set; }
        public IdleClock(long now) => LastActivity = now;
        public void Activity(long now) => LastActivity = now;
        public bool Expired(long now, int seconds, bool held) => !held && now - LastActivity >= ValidMouseIdle(seconds) * 1000L;
    }
}
