using System;
using System.IO;
using System.Text.Json;

namespace MdCardModTool;

public static class AppSettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	public static string AppDataRoot => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MDCardModTool");

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
		string temporary = SettingsPath + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
		File.Move(temporary, SettingsPath, overwrite: true);
	}
}
