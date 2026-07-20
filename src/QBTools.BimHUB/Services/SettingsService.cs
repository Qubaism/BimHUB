using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.IO;
using System.Text.Json;

namespace QBTools.BimHUB.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public SettingsService()
    {
        string applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        _settingsFilePath = Path.Combine(
            applicationData,
            "QBTools",
            "BimHUB",
            "settings.json");
    }

    public BimHubSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new BimHubSettings();
            }

            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<BimHubSettings>(json, SerializerOptions)
                ?? new BimHubSettings();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return new BimHubSettings();
        }
    }

    public void Save(BimHubSettings settings)
    {
        string settingsDirectory = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(settingsDirectory);

        string temporaryFilePath = $"{_settingsFilePath}.tmp";
        string json = JsonSerializer.Serialize(settings, SerializerOptions);

        File.WriteAllText(temporaryFilePath, json);
        RuntimeCompatibility.MoveFileReplacing(temporaryFilePath, _settingsFilePath);
    }
}
