using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Just_Lilith.Core.Llm;

public static class ReasoningPolicy
{
	private static readonly IReadOnlyDictionary<string, string[]> Profiles = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		["o1"] = new string[3] { "low", "medium", "high" },
		["o1-mini"] = new string[3] { "low", "medium", "high" },
		["o1-pro"] = new string[1] { "high" },
		["o3"] = new string[3] { "low", "medium", "high" },
		["o3-mini"] = new string[3] { "low", "medium", "high" },
		["o3-pro"] = new string[1] { "high" },
		["o4-mini"] = new string[3] { "low", "medium", "high" },
		["gpt-5"] = new string[4] { "minimal", "low", "medium", "high" },
		["gpt-5.1"] = new string[4] { "none", "low", "medium", "high" },
		["gpt-5.2"] = new string[5] { "none", "low", "medium", "high", "xhigh" },
		["gpt-5.4"] = new string[5] { "none", "low", "medium", "high", "xhigh" },
		["gpt-5.5"] = new string[5] { "none", "low", "medium", "high", "xhigh" },
		["gpt-5.6"] = new string[6] { "none", "low", "medium", "high", "xhigh", "max" },
		["gpt-5.6-sol"] = new string[6] { "none", "low", "medium", "high", "xhigh", "max" },
		["gpt-5.6-terra"] = new string[6] { "none", "low", "medium", "high", "xhigh", "max" },
		["gpt-5.6-luna"] = new string[6] { "none", "low", "medium", "high", "xhigh", "max" },
		["gpt-6-astra"] = new string[5] { "low", "medium", "high", "xhigh", "max" },
		["gpt-5.2-pro"] = new string[3] { "medium", "high", "xhigh" }
	};

	public static IReadOnlyList<string> CustomEfforts { get; } = Array.AsReadOnly(new string[7] { "none", "minimal", "low", "medium", "high", "xhigh", "max" });

	public static ReasoningDecision Evaluate(string modelId, ReasoningMode mode, string? customEffort = null)
	{
		string[] array = FindProfile(modelId);
		IReadOnlyList<string> readOnlyList2;
		if (array != null)
		{
			IReadOnlyList<string> readOnlyList = Array.AsReadOnly(array);
			readOnlyList2 = readOnlyList;
		}
		else
		{
			readOnlyList2 = CustomEfforts;
		}
		IReadOnlyList<string> readOnlyList3 = readOnlyList2;
		switch (mode)
		{
		case ReasoningMode.Omit:
			return new ReasoningDecision(array != null, null, readOnlyList3, "不发送推理强度参数，由服务端决定。");
		case ReasoningMode.AutoLowest:
			if (array != null)
			{
				return new ReasoningDecision(KnownModel: true, array[0], readOnlyList3, "自动采用该模型支持的最低档：" + array[0]);
			}
			return new ReasoningDecision(KnownModel: false, null, readOnlyList3, "该模型或中转别名的推理能力未知；自动模式不发送推理参数。可按服务商说明手动指定。");
		default:
			throw new LlmException("reasoning_mode", "推理强度模式不正确。");
		case ReasoningMode.Custom:
		{
			string text = customEffort?.Trim().ToLowerInvariant() ?? "";
			if (!readOnlyList3.Contains<string>(text, StringComparer.Ordinal))
			{
				throw new LlmException("reasoning_effort", "该模型未声明支持所选推理强度，请选择可用档位。");
			}
			return new ReasoningDecision(array != null, text, readOnlyList3, (array == null) ? ("手动发送 " + text + "；该别名能力未确认，服务端可能拒绝。") : ("使用指定推理强度：" + text));
		}
		}
	}

	private static string[]? FindProfile(string? modelId)
	{
		if (modelId == null)
		{
			return null;
		}
		if (Profiles.TryGetValue(modelId, out string[] value))
		{
			return value;
		}
		if (modelId.Length > 11)
		{
			if (modelId[modelId.Length - 11] == '-')
			{
				int length = modelId.Length;
				int num = length - 10;
				if (!DateTime.TryParseExact(modelId.Substring(num, length - num), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var _))
				{
					return null;
				}
				if (!Profiles.TryGetValue(modelId.Substring(0, modelId.Length - 11), out string[] value2))
				{
					return null;
				}
				return value2;
			}
		}
		return null;
	}
}
