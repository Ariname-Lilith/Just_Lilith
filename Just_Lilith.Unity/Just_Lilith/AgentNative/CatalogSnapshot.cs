using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Just_Lilith.AgentNative;

internal sealed record CatalogSnapshot(IReadOnlyList<string> ModelOptions, IReadOnlyDictionary<string, CatalogModel> Models, string DefaultModel)
{
	public bool ContainsModel(string model)
	{
		if (!string.IsNullOrWhiteSpace(model))
		{
			return Models.ContainsKey(model);
		}
		return true;
	}

	public IReadOnlyList<string> ReasoningFor(string model)
	{
		string key = (string.IsNullOrWhiteSpace(model) ? DefaultModel : model);
		if (!Models.TryGetValue(key, out CatalogModel value))
		{
			return Array.Empty<string>();
		}
		return value.Reasoning;
	}

	public string DefaultReasoningFor(string model)
	{
		string key = (string.IsNullOrWhiteSpace(model) ? DefaultModel : model);
		if (!Models.TryGetValue(key, out CatalogModel value))
		{
			return "";
		}
		return value.DefaultReasoning;
	}

	internal static CatalogSnapshot FromModels(IReadOnlyDictionary<string, CatalogModel> models)
	{
		string defaultModel = models.Values.FirstOrDefault((CatalogModel value) => value.IsDefault)?.Model ?? models.Keys.FirstOrDefault() ?? "";
		List<string> list = new List<string>();
		list.Add("");
		list.AddRange(models.Keys);
		return new CatalogSnapshot(list, models, defaultModel);
	}

	internal static void ReadPage(JsonElement response, IDictionary<string, CatalogModel> models)
	{
		if (!response.TryGetProperty("data", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object || (item.TryGetProperty("hidden", out var value2) && value2.ValueKind == JsonValueKind.True))
			{
				continue;
			}
			string text = ReadString(item, "model");
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			List<string> list = new List<string>();
			if (item.TryGetProperty("supportedReasoningEfforts", out var value3) && value3.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item2 in value3.EnumerateArray())
				{
					if (item2.ValueKind == JsonValueKind.Object)
					{
						string text2 = ReadString(item2, "reasoningEffort");
						if (!string.IsNullOrWhiteSpace(text2) && !list.Contains<string>(text2, StringComparer.Ordinal))
						{
							list.Add(text2);
						}
					}
				}
			}
			string text3 = ReadString(item, "defaultReasoningEffort");
			if (!list.Contains<string>(text3, StringComparer.Ordinal))
			{
				text3 = "";
			}
			bool isDefault = item.TryGetProperty("isDefault", out var value4) && value4.ValueKind == JsonValueKind.True;
			models[text] = new CatalogModel(text, isDefault, text3, list);
		}
	}

	private static string ReadString(JsonElement value, string property)
	{
		if (!value.TryGetProperty(property, out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value2.GetString() ?? "";
	}
}
