using System.IO;
using System.Text.Json;

namespace ClipboardSync.App.Services;

public sealed class AppSettings
{
    public bool SyncImages { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path, JsonSerializer.Serialize(this));
    }

    private static string DirectoryPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBoard");

    private static string Path => System.IO.Path.Combine(DirectoryPath, "settings.json");
}
