using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Just_Lilith.Core.Agent;
using Just_Lilith.Unity.Ui;

namespace Just_Lilith.AgentNative;

internal static class Patches
{
	private static readonly FieldInfo AgentField = AccessTools.Field(typeof(LlmConfigurationController), "_agent");

	private static readonly FieldInfo SettingsField = AccessTools.Field(typeof(LlmConfigurationController), "_agentSettings");

	private static readonly MethodInfo SetStatus = AccessTools.Method(typeof(LlmConfigurationController), "SetGlobalStatus");

	private static (CodexAgentChannel Channel, AgentSettings Settings) Context(LlmConfigurationController controller)
	{
		return (Channel: (CodexAgentChannel)AgentField.GetValue(controller), Settings: (AgentSettings)SettingsField.GetValue(controller));
	}

	public static void ToggleAgentPostfix(LlmConfigurationController __instance)
	{
		var (channel, settings) = Context(__instance);
		AgentCatalog.Get(channel, settings);
	}

	public static bool GetAgentModelChoicesPrefix(LlmConfigurationController __instance, ref IReadOnlyList<string> __result)
	{
		(CodexAgentChannel Channel, AgentSettings Settings) tuple = Context(__instance);
		CodexAgentChannel item = tuple.Channel;
		AgentSettings item2 = tuple.Settings;
		__result = AgentCatalog.Get(item, item2).ModelOptions;
		return false;
	}

	public static void ReadAgentUiStatePostfix(LlmConfigurationController __instance, ref AgentUiState __result)
	{
		(CodexAgentChannel Channel, AgentSettings Settings) tuple = Context(__instance);
		CodexAgentChannel item = tuple.Channel;
		AgentSettings item2 = tuple.Settings;
		CatalogSnapshot catalogSnapshot = AgentCatalog.Get(item, item2);
		string model = (catalogSnapshot.ContainsModel(item2.Model) ? item2.Model : "");
		IReadOnlyList<string> readOnlyList = catalogSnapshot.ReasoningFor(model);
		string text = (readOnlyList.Contains<string>(item2.ReasoningEffort, StringComparer.Ordinal) ? item2.ReasoningEffort : catalogSnapshot.DefaultReasoningFor(model));
		__result = new AgentUiState(__result.Enabled, __result.Busy, __result.Ready, __result.Label, __result.Detail, __result.ThreadId, model, string.IsNullOrWhiteSpace(text) ? "-" : text, catalogSnapshot.ModelOptions, readOnlyList);
	}

	public static bool SelectAgentModelPrefix(LlmConfigurationController __instance, string model)
	{
		(CodexAgentChannel Channel, AgentSettings Settings) tuple = Context(__instance);
		CodexAgentChannel item = tuple.Channel;
		AgentSettings item2 = tuple.Settings;
		CatalogSnapshot catalogSnapshot = AgentCatalog.Get(item, item2);
		if (!catalogSnapshot.ModelOptions.Contains<string>(model, StringComparer.Ordinal))
		{
			return false;
		}
		item2.SetModel(model);
		IReadOnlyList<string> readOnlyList = catalogSnapshot.ReasoningFor(model);
		if (readOnlyList.Count != 0 && !readOnlyList.Contains<string>(item2.ReasoningEffort, StringComparer.Ordinal))
		{
			string text = catalogSnapshot.DefaultReasoningFor(model);
			if (!string.IsNullOrWhiteSpace(text))
			{
				item2.SetReasoningEffort(text);
			}
		}
		SetStatus.Invoke(__instance, new object[1] { string.IsNullOrWhiteSpace(model) ? "Agent 模型已设为提供方默认，下一轮生效。" : ("Agent 模型已设为 " + model + "，下一轮生效。") });
		return false;
	}

	public static bool SelectAgentReasoningPrefix(LlmConfigurationController __instance, string reasoning)
	{
		var (channel, agentSettings) = Context(__instance);
		if (!AgentCatalog.Get(channel, agentSettings).ReasoningFor(agentSettings.Model).Contains<string>(reasoning, StringComparer.Ordinal))
		{
			return false;
		}
		agentSettings.SetReasoningEffort(reasoning);
		SetStatus.Invoke(__instance, new object[1] { "Agent 推理强度已设为 " + reasoning + "，下一轮生效。" });
		return false;
	}

	public static bool OpenAgentModelMenuPrefix(LlmConfigurationView __instance)
	{
		return Open(__instance, 0, model: true);
	}

	public static bool OpenAgentReasoningMenuPrefix(LlmConfigurationView __instance)
	{
		return Open(__instance, 1, model: false);
	}

	private static bool Open(LlmConfigurationView view, int column, bool model)
	{
		try
		{
			if ((bool)AccessTools.Field(typeof(LlmConfigurationView), "_chatBusy").GetValue(view) || (bool)AccessTools.Field(typeof(LlmConfigurationView), "_connectionBusy").GetValue(view))
			{
				return false;
			}
			object value = AccessTools.Field(typeof(LlmConfigurationView), "_callbacks").GetValue(view);
			if (!(((Delegate)AccessTools.Property(value.GetType(), "AgentStatusRequested").GetValue(value)).DynamicInvoke() is AgentUiState agentUiState))
			{
				return false;
			}
			string name = (model ? "AgentModelSelectedRequested" : "AgentReasoningSelectedRequested");
			Delegate choose = (Delegate)AccessTools.Property(value.GetType(), name).GetValue(value);
			IReadOnlyList<string> rawChoices = (model ? agentUiState.ModelOptions : agentUiState.ReasoningOptions);
			string selected = (model ? agentUiState.Model : agentUiState.ReasoningEffort);
			NativeLanguageDropdown.Show(view, column, rawChoices, selected, delegate(string text)
			{
				choose.DynamicInvoke(text);
			});
		}
		catch (Exception data)
		{
			Plugin.LogSource.LogError(data);
		}
		return false;
	}

	public static void MaintainNativeAgentPostfix(LlmConfigurationView __instance)
	{
		object value = AccessTools.Field(typeof(LlmConfigurationView), "_agentConfigurationRows").GetValue(__instance);
		if (value != null)
		{
			NativeSelectorClones.TryClone(value, new int[2] { 0, 1 });
		}
	}

	public static void CloseAgentChoiceMenuPostfix()
	{
		NativeLanguageDropdown.Close();
	}
}
