using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Just_Lilith.Unity.Ui;

namespace Just_Lilith.AgentNative;

[BepInPlugin("local.just_lilith.agent_native", "Just_Lilith Agent Native Selectors", "0.2.0")]
[BepInDependency("local.just_lilith", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BasePlugin
{
	public const string PluginId = "local.just_lilith.agent_native";

	public const string PluginName = "Just_Lilith Agent Native Selectors";

	public const string PluginVersion = "0.2.0";

	internal static ManualLogSource LogSource;

	private Harmony? _harmony;

	public override void Load()
	{
		LogSource = base.Log;
		_harmony = new Harmony("local.just_lilith.agent_native");
		NativeSelectorClones.Install(_harmony);
		Patch("ToggleAgent", null, "ToggleAgentPostfix");
		PatchView("MaintainNativeAgent", null, "MaintainNativeAgentPostfix");
		Patch("ReadAgentUiState", null, "ReadAgentUiStatePostfix");
		Patch("GetAgentModelChoices", "GetAgentModelChoicesPrefix");
		Patch("SelectAgentModel", "SelectAgentModelPrefix");
		Patch("SelectAgentReasoning", "SelectAgentReasoningPrefix");
		PatchView("OpenAgentModelMenu", "OpenAgentModelMenuPrefix", null);
		PatchView("OpenAgentReasoningMenu", "OpenAgentReasoningMenuPrefix", null);
		PatchView("CloseAgentChoiceMenu", null, "CloseAgentChoiceMenuPostfix");
		base.Log.LogInfo("Agent native selectors loaded; catalog source=Codex app-server model/list.");
	}

	private void Patch(string target, string? prefix = null, string? postfix = null)
	{
		_harmony.Patch(AccessTools.Method(typeof(LlmConfigurationController), target), (prefix == null) ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), prefix)), (postfix == null) ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), postfix)));
	}

	private void PatchView(string target, string? prefix, string? postfix)
	{
		_harmony.Patch(AccessTools.Method(typeof(LlmConfigurationView), target), (prefix == null) ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), prefix)), (postfix == null) ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), postfix)));
	}
}
