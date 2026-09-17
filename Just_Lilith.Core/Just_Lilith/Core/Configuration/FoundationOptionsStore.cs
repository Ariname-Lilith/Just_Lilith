using System;
using System.IO;
using System.Text.Json;

namespace Just_Lilith.Core.Configuration;

public static class FoundationOptionsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static FoundationOptions LoadOrCreate(string path)
	{
		if (!File.Exists(path))
		{
			FoundationOptions foundationOptions = new FoundationOptions();
			Save(path, foundationOptions);
			return foundationOptions;
		}
		FoundationOptions? obj = JsonSerializer.Deserialize<FoundationOptions>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("The foundation configuration is empty.");
		Validate(obj);
		return obj;
	}

	public static void Save(string path, FoundationOptions options)
	{
		Validate(options);
		string fullPath = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
		string text = fullPath + ".pending-" + Guid.NewGuid().ToString("N");
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(options, JsonOptions));
			File.Move(text, fullPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private static void Validate(FoundationOptions options)
	{
		if (options.SchemaVersion != 1)
		{
			throw new InvalidDataException("Unsupported foundation configuration schema; the file was not replaced.");
		}
	}
}
