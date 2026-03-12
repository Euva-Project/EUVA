// SPDX-License-Identifier: GPL-3.0-or-later


using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EUVA.UI.Theming;


public sealed class EuvaSettings
{


    private static EuvaSettings? _default;


    public static EuvaSettings Default => _default ??= Load();


    [JsonPropertyName("lastThemePath")]
    public string LastThemePath { get; set; } = string.Empty;

    [JsonPropertyName("aiProvider")]
    public string AiProvider { get; set; } = "Custom";

    [JsonPropertyName("aiApiKeyEncrypted")]
    public string AiApiKeyEncrypted { get; set; } = string.Empty;

    [JsonPropertyName("aiBaseUrl")]
    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";

    [JsonPropertyName("aiModelName")]
    public string AiModelName { get; set; } = "gpt-4o";

    [JsonPropertyName("aiCustomPrompt")]
    public string AiCustomPrompt { get; set; } = "Analyze this decompiled C code. Identify the roles of generic variables (v1, a2) and struct fields (field_1). Return ONLY a mapping of old names to new names. Do not use JSON, markdown, or explanations. Return strictly in this format: old_name=new_name. One per line.";



    private static string SettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EUVA", "settings.json");


    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EuvaSettings] Save failed: {ex.Message}");
        }
    }

    private static EuvaSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var loaded = JsonSerializer.Deserialize<EuvaSettings>(
                    File.ReadAllText(SettingsFilePath));
                if (loaded != null) return loaded;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EuvaSettings] Load failed: {ex.Message}");
        }
        return new EuvaSettings();
    }
}
