using System;
using System.IO;
using System.Text.Json;

namespace MdCardModTool;

public static class AppSettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string AppDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");

	public static string SettingsPath => Path.Combine(AppDataRoot, "settings-v1.json");

	public static AppSettings Load()
	{
		try
		{
			if (File.Exists(SettingsPath))
			{
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
			}
		}
		catch
		{
		}
		return new AppSettings();
	}

	public static void Save(AppSettings settings)
	{
		Directory.CreateDirectory(AppDataRoot);
		string text = SettingsPath + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(settings, JsonOptions));
		File.Move(text, SettingsPath, overwrite: true);
	}
}
