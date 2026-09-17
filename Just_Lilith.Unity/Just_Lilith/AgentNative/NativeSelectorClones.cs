using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using Just_Lilith.Unity.Ui;
using TMPro;
using UI.TraySetting;
using UI.TraySettingNew;
using UI.TraySettingNew.SpecialItems;
using UnityEngine;
using UnityEngine.UI;

namespace Just_Lilith.AgentNative;

internal static class NativeSelectorClones
{
	internal static readonly Type RowsType = typeof(LlmConfigurationView).Assembly.GetType("Just_Lilith.Unity.Ui.NativeLilithConversationRows", throwOnError: true);

	private static readonly FieldInfo Rows = AccessTools.Field(RowsType, "_rows");

	private static readonly FieldInfo Actions = AccessTools.Field(RowsType, "_actions");

	private static readonly FieldInfo Staging = AccessTools.Field(RowsType, "_staging");

	private static readonly FieldInfo ExtraButtons = AccessTools.Field(RowsType, "_additionalButtons");

	private const string Prefix = "Just_Lilith.Agent.NativeLanguage.";

	private static TraySettingGameLanguageButton? _source;

	private static GameObject? _host;

	private static string _arrowSuffix = "";

	private static float _nextRetry;

	private static string? _lastFailure;

	internal static void Install(Harmony harmony)
	{
		Patch("TryUseGameLanguageSelectorStyle", "ClonePrefix");
		Patch("SetCaptions", "CaptionsPrefix");
		void Patch(string name, string handler)
		{
			harmony.Patch(AccessTools.Method(RowsType, name), new HarmonyMethod(AccessTools.Method(typeof(NativeSelectorClones), handler)));
		}
	}

	public static bool ClonePrefix(object __instance, int[] __0, ref bool __result)
	{
		__result = TryClone(__instance, __0);
		return false;
	}

	internal static bool TryClone(object rowsObject, int[] columns)
	{
		IList rows = (IList)Rows.GetValue(rowsObject);
		if (columns.All((int i) => Root(rows[i]).name.StartsWith("Just_Lilith.Agent.NativeLanguage.", StringComparison.Ordinal)))
		{
			return true;
		}
		if (Time.unscaledTime < _nextRetry)
		{
			return false;
		}
		try
		{
			GameObject gameObject = (GameObject)Staging.GetValue(rowsObject);
			TraySettingGameLanguageButton traySettingGameLanguageButton = EnsureSource(gameObject.transform);
			Action[] array = (Action[])Actions.GetValue(rowsObject);
			IDictionary dictionary = (IDictionary)ExtraButtons.GetValue(rowsObject);
			for (int num = 0; num < columns.Length; num++)
			{
				int num2 = columns[num];
				GameObject gameObject2 = Root(rows[num2]);
				if (gameObject2.name.StartsWith("Just_Lilith.Agent.NativeLanguage.", StringComparison.Ordinal))
				{
					continue;
				}
				GameObject gameObject3 = UnityEngine.Object.Instantiate(traySettingGameLanguageButton._container.gameObject, gameObject.transform, worldPositionStays: false);
				gameObject3.name = "Just_Lilith.Agent.NativeLanguage." + num2;
				gameObject3.SetActive(value: false);
				AccessTools.Method(RowsType, "StripLocalization").Invoke(null, new object[1] { gameObject3.transform });
				TextMeshProUGUI component = Corresponding(traySettingGameLanguageButton._container, traySettingGameLanguageButton._label.transform, gameObject3.transform).GetComponent<TextMeshProUGUI>();
				System.Collections.Generic.List<Button> list = gameObject3.GetComponentsInChildren<Button>(includeInactive: true).ToList();
				if (component == null || list.Count == 0)
				{
					UnityEngine.Object.Destroy(gameObject3);
					throw new InvalidOperationException("Native selector clone is missing its label/button.");
				}
				foreach (Button item in list)
				{
					item.onClick = new Button.ButtonClickedEvent();
					Action action = array[num2];
					item.onClick.AddListener((Action)delegate
					{
						action();
					});
					item.interactable = true;
				}
				foreach (TraySettingGameLanguageButton componentsInChild in gameObject3.GetComponentsInChildren<TraySettingGameLanguageButton>(includeInactive: true))
				{
					componentsInChild._panel = null;
					componentsInChild._blocker = null;
					componentsInChild.enabled = false;
					UnityEngine.Object.Destroy(componentsInChild);
				}
				Type type = rows[num2].GetType();
				rows[num2] = Activator.CreateInstance(type, gameObject3, list[0], component);
				dictionary[num2] = list.Skip(1).ToList();
				gameObject2.SetActive(value: false);
				UnityEngine.Object.Destroy(gameObject2);
				Plugin.LogSource.LogInfo("Agent selector cloned from original game-language prefab; column=" + num2 + "; font=" + component.font.name + "; size=" + component.fontSize + "; native_arrow=" + _arrowSuffix);
			}
			_lastFailure = null;
			return true;
		}
		catch (Exception ex)
		{
			_nextRetry = Time.unscaledTime + 1f;
			if (_lastFailure != ex.Message)
			{
				Plugin.LogSource.LogWarning("Agent native clone pending: " + ex.Message);
			}
			_lastFailure = ex.Message;
			return false;
		}
	}

	public static void CaptionsPrefix(object __instance, string[] __0)
	{
		IList list = (IList)Rows.GetValue(__instance);
		if (list.Count >= 2 && Root(list[0]).name.StartsWith("Just_Lilith.Agent.NativeLanguage.", StringComparison.Ordinal))
		{
			__0[0] = Label(__0[0], "模型：", "Agent模型：");
			__0[1] = Label(__0[1], "推理：", "推理强度：");
		}
	}

	private static string Label(string value, string oldPrefix, string newPrefix)
	{
		if (value.StartsWith(oldPrefix, StringComparison.Ordinal))
		{
			string text = value;
			int length = oldPrefix.Length;
			value = newPrefix + text.Substring(length, text.Length - length);
		}
		if (!string.IsNullOrEmpty(_arrowSuffix) && !value.EndsWith(_arrowSuffix, StringComparison.Ordinal))
		{
			value += _arrowSuffix;
		}
		return value;
	}

	internal static GameObject Root(object tuple)
	{
		return (GameObject)tuple.GetType().GetField("Item1").GetValue(tuple);
	}

	internal static TraySettingGameLanguageButton EnsureSource(Transform scope)
	{
		if (_source != null && _source._container != null && _source._label != null)
		{
			return _source;
		}
		GameObject gameObject = null;
		TraySettingNewView traySettingNewView = null;
		TraySettingConfig.SettingItemEntry settingItemEntry = null;
		foreach (TraySettingNewView item in Resources.FindObjectsOfTypeAll<TraySettingNewView>())
		{
			Il2CppSystem.Collections.Generic.List<TraySettingConfig.SettingUiTypeEntry> list = item._config?._uiTypes;
			if (list == null)
			{
				continue;
			}
			foreach (TraySettingConfig.SettingUiTypeEntry item2 in list)
			{
				GameObject uiObject = item2.uiObject;
				if (uiObject != null && uiObject.GetComponentInChildren<SettingLanguageSetItem>(includeInactive: true) != null)
				{
					gameObject = uiObject;
					break;
				}
			}
			if (gameObject != null)
			{
				break;
			}
		}
		if (gameObject == null)
		{
			foreach (TraySettingNewView item3 in Resources.FindObjectsOfTypeAll<TraySettingNewView>())
			{
				TraySettingConfig config = item3._config;
				if (config?._settingItems == null || config._uiTypes == null)
				{
					continue;
				}
				foreach (TraySettingConfig.SettingItemEntry settingItem in config._settingItems)
				{
					if ((settingItem.itemId + " " + settingItem.titleLocalizationKey).IndexOf("language", StringComparison.OrdinalIgnoreCase) < 0)
					{
						continue;
					}
					foreach (TraySettingConfig.SettingUiTypeEntry uiType in config._uiTypes)
					{
						if (uiType.uiTypeId == settingItem.uiTypeId && uiType.uiObject != null)
						{
							gameObject = uiType.uiObject;
							traySettingNewView = item3;
							settingItemEntry = settingItem;
							break;
						}
					}
					if (gameObject != null)
					{
						break;
					}
				}
				if (gameObject != null)
				{
					break;
				}
			}
		}
		if (gameObject == null)
		{
			foreach (SettingLanguageSetItem item4 in Resources.FindObjectsOfTypeAll<SettingLanguageSetItem>())
			{
				if (item4 != null && !item4.transform.root.name.StartsWith("Just_Lilith.Agent.NativeLanguage.", StringComparison.Ordinal))
				{
					gameObject = item4.gameObject;
					break;
				}
			}
		}
		if (gameObject == null)
		{
			throw new InvalidOperationException("Game-language prefab has not loaded yet.");
		}
		Canvas componentInParent = scope.GetComponentInParent<Canvas>(includeInactive: true);
		if (componentInParent == null)
		{
			throw new InvalidOperationException("Settings canvas has not loaded yet.");
		}
		if (_host != null)
		{
			UnityEngine.Object.Destroy(_host);
		}
		_host = new GameObject("Just_Lilith.Agent.NativeLanguage.TemplateHost");
		_host.AddComponent<RectTransform>().SetParent(componentInParent.transform, worldPositionStays: false);
		_host.SetActive(value: false);
		CanvasGroup canvasGroup = _host.AddComponent<CanvasGroup>();
		canvasGroup.alpha = 0f;
		canvasGroup.interactable = false;
		canvasGroup.blocksRaycasts = false;
		GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, _host.transform, worldPositionStays: false);
		foreach (TraySettingGameLanguageButton componentsInChild in gameObject2.GetComponentsInChildren<TraySettingGameLanguageButton>(includeInactive: true))
		{
			componentsInChild._panel = null;
			componentsInChild._blocker = null;
			componentsInChild._itemLabels = new Il2CppSystem.Collections.Generic.List<TextMeshProUGUI>();
			componentsInChild._itemIndices = new Il2CppSystem.Collections.Generic.List<int>();
		}
		gameObject2.SetActive(value: true);
		_host.SetActive(value: true);
		if (traySettingNewView != null && settingItemEntry != null)
		{
			traySettingNewView.BindAndInitItem(gameObject2, settingItemEntry);
		}
		SettingLanguageSetItem componentInChildren = gameObject2.GetComponentInChildren<SettingLanguageSetItem>(includeInactive: true);
		if (componentInChildren == null)
		{
			throw new InvalidOperationException("Native language factory produced no language row.");
		}
		componentInChildren.Init();
		componentInChildren.BindLanguageButton();
		TraySettingGameLanguageButton traySettingGameLanguageButton = componentInChildren._gameLanguageButton ?? gameObject2.GetComponentInChildren<TraySettingGameLanguageButton>(includeInactive: true);
		if (traySettingGameLanguageButton == null)
		{
			throw new InvalidOperationException("Initialized native language row has no controller.");
		}
		traySettingGameLanguageButton.BindControls();
		traySettingGameLanguageButton.RefreshLabel();
		if (traySettingGameLanguageButton._container == null || traySettingGameLanguageButton._label == null)
		{
			throw new InvalidOperationException("Initialized native language controller has no container/label.");
		}
		_source = traySettingGameLanguageButton;
		Match match = Regex.Match(_source._label.text ?? "", "\\s*[▼▾⏷]$", RegexOptions.CultureInvariant);
		_arrowSuffix = (match.Success ? match.Value : "");
		_host.SetActive(value: false);
		return _source;
	}

	internal static Transform Corresponding(Transform originalRoot, Transform original, Transform clonedRoot)
	{
		System.Collections.Generic.Stack<int> stack = new System.Collections.Generic.Stack<int>();
		Transform transform = original;
		while (transform != originalRoot)
		{
			if (transform == null)
			{
				throw new InvalidOperationException("Native label is outside the selector container.");
			}
			stack.Push(transform.GetSiblingIndex());
			transform = transform.parent;
		}
		Transform transform2 = clonedRoot;
		foreach (int item in stack)
		{
			transform2 = transform2.GetChild(item);
		}
		return transform2;
	}
}
