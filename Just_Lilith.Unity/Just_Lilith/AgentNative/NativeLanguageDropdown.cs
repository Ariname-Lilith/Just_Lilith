using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Just_Lilith.Unity.Ui;
using TMPro;
using UI.TraySetting;
using UnityEngine;
using UnityEngine.UI;

namespace Just_Lilith.AgentNative;

internal static class NativeLanguageDropdown
{
	private static GameObject? _panel;

	private static GameObject? _blocker;

	public static void Show(LlmConfigurationView view, int column, IReadOnlyList<string> rawChoices, string selected, Action<string> choose)
	{
		Close();
		object obj = AccessTools.Field(typeof(LlmConfigurationView), "_agentConfigurationRows").GetValue(view) ?? throw new InvalidOperationException("Agent selector rows are unavailable.");
		if (!NativeSelectorClones.TryClone(obj, new int[2] { 0, 1 }))
		{
			return;
		}
		RectTransform component = NativeSelectorClones.Root(((IList)AccessTools.Field(NativeSelectorClones.RowsType, "_rows").GetValue(obj))[column]).GetComponent<RectTransform>();
		Canvas componentInParent = component.GetComponentInParent<Canvas>(includeInactive: true);
		if (componentInParent == null)
		{
			throw new InvalidOperationException("Native selector has no canvas.");
		}
		TraySettingGameLanguageButton traySettingGameLanguageButton = NativeSelectorClones.EnsureSource(component);
		traySettingGameLanguageButton.OpenPanel();
		try
		{
			if (traySettingGameLanguageButton._panel == null)
			{
				traySettingGameLanguageButton.BuildPanel(componentInParent);
			}
			traySettingGameLanguageButton.RefreshItems();
			if (traySettingGameLanguageButton._panel == null)
			{
				throw new InvalidOperationException("Native language panel was not built.");
			}
			_panel = UnityEngine.Object.Instantiate(traySettingGameLanguageButton._panel.gameObject, componentInParent.transform, worldPositionStays: false);
			if (traySettingGameLanguageButton._blocker != null)
			{
				_blocker = UnityEngine.Object.Instantiate(traySettingGameLanguageButton._blocker.gameObject, componentInParent.transform, worldPositionStays: false);
			}
			_panel.name = "Just_Lilith.Agent.NativeLanguage.Menu";
			if (_blocker != null)
			{
				_blocker.name = "Just_Lilith.Agent.NativeLanguage.Blocker";
			}
			ConfigureItems(traySettingGameLanguageButton, rawChoices, selected, choose);
		}
		finally
		{
			traySettingGameLanguageButton.ClosePanel();
		}
		if (_blocker != null)
		{
			foreach (Button componentsInChild in _blocker.GetComponentsInChildren<Button>(includeInactive: true))
			{
				componentsInChild.onClick = new Button.ButtonClickedEvent();
				componentsInChild.onClick.AddListener((Action)Close);
			}
			_blocker.transform.SetAsLastSibling();
			_blocker.SetActive(value: true);
		}
		_panel.SetActive(value: true);
		_panel.transform.SetAsLastSibling();
		RectTransform container = traySettingGameLanguageButton._container;
		RectTransform panel = traySettingGameLanguageButton._panel;
		try
		{
			traySettingGameLanguageButton._container = component;
			traySettingGameLanguageButton._panel = _panel.GetComponent<RectTransform>();
			traySettingGameLanguageButton.PositionPanel(componentInParent);
		}
		finally
		{
			traySettingGameLanguageButton._container = container;
			traySettingGameLanguageButton._panel = panel;
		}
		Plugin.LogSource.LogInfo("Agent native dropdown opened; column=" + column + "; options=" + Math.Max(1, rawChoices.Count));
	}

	public static void Close()
	{
		if (_panel != null)
		{
			_panel.SetActive(value: false);
			UnityEngine.Object.Destroy(_panel);
		}
		if (_blocker != null)
		{
			_blocker.SetActive(value: false);
			UnityEngine.Object.Destroy(_blocker);
		}
		_panel = null;
		_blocker = null;
	}

	private static void ConfigureItems(TraySettingGameLanguageButton source, IReadOnlyList<string> rawChoices, string selected, Action<string> choose)
	{
		string[] array = ((rawChoices.Count != 0) ? rawChoices.ToArray() : new string[1] { "" });
		List<(Button, TextMeshProUGUI)> list = new List<(Button, TextMeshProUGUI)>();
		Color? color = null;
		Color? color2 = null;
		for (int i = 0; i < source._itemLabels.Count; i++)
		{
			TextMeshProUGUI textMeshProUGUI = source._itemLabels[i];
			TextMeshProUGUI component = NativeSelectorClones.Corresponding(source._panel, textMeshProUGUI.transform, _panel.transform).GetComponent<TextMeshProUGUI>();
			Button componentInParent = component.GetComponentInParent<Button>(includeInactive: true);
			if (componentInParent == null)
			{
				throw new InvalidOperationException("Native language item has no button.");
			}
			list.Add((componentInParent, component));
			if (source._itemIndices[i] == source._currentIndex)
			{
				color = textMeshProUGUI.color;
				continue;
			}
			Color valueOrDefault = color2.GetValueOrDefault();
			if (!color2.HasValue)
			{
				valueOrDefault = textMeshProUGUI.color;
				color2 = valueOrDefault;
			}
		}
		if (list.Count == 0)
		{
			throw new InvalidOperationException("Native language panel has no items.");
		}
		int count = list.Count;
		float itemHeight = TraySettingGameLanguageButton.ItemHeight;
		Vector2 anchoredPosition = list[0].Item1.GetComponent<RectTransform>().anchoredPosition;
		Vector2 vector = ((count > 1) ? (list[1].Item1.GetComponent<RectTransform>().anchoredPosition - anchoredPosition) : new Vector2(0f, 0f - itemHeight));
		(Button, TextMeshProUGUI) tuple = list[list.Count - 1];
		for (int j = list.Count; j < array.Length; j++)
		{
			GameObject gameObject = UnityEngine.Object.Instantiate(tuple.Item1.gameObject, tuple.Item1.transform.parent, worldPositionStays: false);
			TextMeshProUGUI component2 = NativeSelectorClones.Corresponding(tuple.Item1.transform, tuple.Item2.transform, gameObject.transform).GetComponent<TextMeshProUGUI>();
			list.Add((gameObject.GetComponent<Button>(), component2));
		}
		for (int k = 0; k < list.Count; k++)
		{
			(Button, TextMeshProUGUI) tuple2 = list[k];
			tuple2.Item1.gameObject.SetActive(k < array.Length);
			if (k >= array.Length)
			{
				continue;
			}
			tuple2.Item1.GetComponent<RectTransform>().anchoredPosition = anchoredPosition + vector * k;
			string value = array[k];
			tuple2.Item2.text = (string.IsNullOrWhiteSpace(value) ? "-" : value);
			Color? color3 = ((string.Equals(value, selected, StringComparison.Ordinal) || (string.IsNullOrWhiteSpace(value) && selected == "-")) ? color : color2);
			if (color3.HasValue)
			{
				tuple2.Item2.color = color3.Value;
			}
			tuple2.Item1.interactable = true;
			tuple2.Item1.onClick = new Button.ButtonClickedEvent();
			tuple2.Item1.onClick.AddListener((Action)delegate
			{
				try
				{
					choose(value);
				}
				finally
				{
					Close();
				}
			});
		}
		RectTransform component3 = _panel.GetComponent<RectTransform>();
		float num = Math.Min((float)count * itemHeight, TraySettingGameLanguageButton.PanelMaxHeight);
		float num2 = Math.Max(0f, component3.rect.height - num);
		component3.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, num2 + Math.Min((float)array.Length * itemHeight, TraySettingGameLanguageButton.PanelMaxHeight));
		ScrollRect componentInChildren = _panel.GetComponentInChildren<ScrollRect>(includeInactive: true);
		if (componentInChildren != null && componentInChildren.content != null)
		{
			componentInChildren.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (float)array.Length * itemHeight);
			componentInChildren.verticalNormalizedPosition = 1f;
		}
	}
}
