using System.IO;
using System.Text.Json;

namespace LabelPrintClient.Services;

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LabelPrintClient");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public string ServerAddress { get; set; } = "";
    public string DefaultPrinter { get; set; } = "p750w";
    public string DefaultSize { get; set; } = "m";
    public bool RunAtStartup { get; set; } = true;

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        var json = File.ReadAllText(SettingsFile);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerAddress);
}
