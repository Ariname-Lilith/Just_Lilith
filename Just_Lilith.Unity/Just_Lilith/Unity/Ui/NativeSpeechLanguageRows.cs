using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Speech;
using Just_Lilith.Unity.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Just_Lilith.Unity.Ui;

internal sealed class NativeSpeechLanguageRows : IDisposable
{
	private const string Prefix = "Just_Lilith.NativeLanguage.TTS.";

	private const string ToggleItemType = "UI.TraySettingNew.SettingItems.SettingToggleItem, Assembly-CSharp";

	private const string SliderItemType = "UI.TraySettingNew.SettingItems.SettingSliderItem, Assembly-CSharp";

	private const string TmpType = "TMPro.TMP_Text, Unity.TextMeshPro";

	private static readonly string[] LocalizationTypes = new string[2] { "AutoBindLocalizeStringEvent, Assembly-CSharp", "UnityEngine.Localization.Components.LocalizeStringEvent, Unity.Localization" };

	private readonly Transform _itemRoot;

	private readonly Transform _voiceRow;

	private readonly Transform _speedRow;

	private readonly GameObject _staging;

	private readonly int _createdFrame;

	private readonly List<GameObject> _rows = new List<GameObject>();

	private readonly List<UnityEngine.Object> _removedLocalizers = new List<UnityEngine.Object>();

	private readonly List<(Toggle Toggle, bool Enabled, object Text)> _services = new List<(Toggle, bool, object)>();

	private readonly List<(Toggle Toggle, SpeechLanguage Language)> _languages = new List<(Toggle, SpeechLanguage)>();

	private readonly List<(Toggle Toggle, string? Style)> _references = new List<(Toggle, string)>();

	private readonly List<(object Text, string Label)> _labels = new List<(object, string)>();

	private readonly Action<bool> _selectServiceEnabled;

	private readonly Action<SpeechLanguage> _selectLanguage;

	private readonly Action<string?> _selectReference;

	private ToggleGroup? _serviceGroup;

	private ToggleGroup? _languageGroup;

	private ToggleGroup? _referenceGroup;

	private SpeechSettings _settings = new SpeechSettings();

	private TtsServiceState _serviceState = new TtsServiceState(TtsServicePhase.Off, "TTS 服务已关闭");

	private bool _loaded;

	private bool _suppress;

	private bool _disposed;

	private NativeSpeechLanguageRows(Transform itemRoot, Transform voiceRow, Transform speedRow, Transform home, Action<bool> selectServiceEnabled, Action<SpeechLanguage> selectLanguage, Action<string?> selectReference)
	{
		_itemRoot = itemRoot;
		_voiceRow = voiceRow;
		_speedRow = speedRow;
		_selectServiceEnabled = selectServiceEnabled;
		_selectLanguage = selectLanguage;
		_selectReference = selectReference;
		_staging = RectObject("Just_Lilith.NativeLanguage.TTS.InactiveStaging", home);
		_staging.SetActive(value: false);
		_createdFrame = Time.frameCount;
	}

	internal static bool TryCreate(object view, Transform itemRoot, Transform home, Action<bool> selectServiceEnabled, Action<SpeechLanguage> selectLanguage, Action<string?> selectReference, out NativeSpeechLanguageRows? rows, out string diagnostic)
	{
		rows = null;
		diagnostic = "state=awaiting_native_language_rows";
		Type type = ResolveType("UI.TraySettingNew.SettingItems.SettingToggleItem, Assembly-CSharp");
		Type type2 = ResolveType("UI.TraySettingNew.SettingItems.SettingSliderItem, Assembly-CSharp");
		Type type3 = ResolveType("TMPro.TMP_Text, Unity.TextMeshPro");
		Type[] array = LocalizationTypes.Select(ResolveType).ToArray();
		if (type == null || type2 == null || type3 == null || array.Any((Type type4) => type4 == null))
		{
			diagnostic = "state=native_radio_types_unresolved";
			return false;
		}
		if (!HasExpectedLanguageConfig(view))
		{
			diagnostic = "state=native_language_config_not_matched";
			return false;
		}
		object obj = FindUniqueComponent(itemRoot, type);
		object obj2 = FindUniqueComponent(itemRoot, type2);
		if (!(obj is Component component) || component == null || !(obj2 is Component component2) || component2 == null)
		{
			return false;
		}
		Transform transform = DirectChild(component.transform, itemRoot);
		Transform transform2 = DirectChild(component2.transform, itemRoot);
		Component component3 = Read(obj, "_nameText") as Component;
		GameObject gameObject = Read(obj, "_togglePrefab") as GameObject;
		Transform transform3 = Read(obj, "_toggleRoot") as Transform;
		if (transform == null || transform2 == null || component3 == null || gameObject == null || transform3 == null || transform.GetComponent<RectTransform>() == null || component3.GetComponent<RectTransform>() == null || transform3.GetComponent<RectTransform>() == null || gameObject.GetComponentInChildren<Toggle>(includeInactive: true) == null)
		{
			diagnostic = "state=native_radio_template_incomplete";
			return false;
		}
		NativeSpeechLanguageRows created = new NativeSpeechLanguageRows(itemRoot, transform, transform2, home, selectServiceEnabled, selectLanguage, selectReference);
		try
		{
			Transform transform4 = created.CreateRow("Service", "TTS服务", transform, component3, transform3, type3, array);
			GridLayoutGroup component4 = transform4.GetComponent<GridLayoutGroup>();
			component4.constraintCount = 2;
			component4.cellSize = new Vector2(component4.cellSize.x * 2f, component4.cellSize.y);
			created._serviceGroup = transform4.gameObject.AddComponent<ToggleGroup>();
			created._serviceGroup.allowSwitchOff = false;
			foreach (SpeechServiceChoice serviceChoice in SpeechRadioSelection.ServiceChoices)
			{
				bool enabled = serviceChoice.Enabled;
				Toggle toggle = created.CreateOption(transform4, gameObject, serviceChoice.Label, created._serviceGroup, type3, array);
				created._services.Add((toggle, enabled, created.TakeDynamicServiceLabel(toggle)));
				toggle.onValueChanged.AddListener((Action<bool>)delegate(bool selected)
				{
					created.OnServiceEnabled(selected, enabled);
				});
			}
			Transform transform5 = created.CreateRow("Language", "TTS语音", transform, component3, transform3, type3, array);
			created._languageGroup = transform5.gameObject.AddComponent<ToggleGroup>();
			created._languageGroup.allowSwitchOff = false;
			foreach (SpeechLanguageChoice languageChoice in SpeechRadioSelection.LanguageChoices)
			{
				SpeechLanguage language = languageChoice.Language;
				Toggle toggle2 = created.CreateOption(transform5, gameObject, languageChoice.Label, created._languageGroup, type3, array);
				created._languages.Add((toggle2, language));
				toggle2.onValueChanged.AddListener((Action<bool>)delegate(bool selected)
				{
					created.OnLanguage(selected, language);
				});
			}
			Transform transform6 = created.CreateRow("Reference.First", "参考音频", transform, component3, transform3, type3, array);
			Transform transform7 = created.CreateRow("Reference.Second", "", transform, component3, transform3, type3, array);
			created._referenceGroup = transform6.gameObject.AddComponent<ToggleGroup>();
			created._referenceGroup.allowSwitchOff = false;
			for (int num = 0; num < SpeechRadioSelection.ReferenceChoices.Count; num++)
			{
				SpeechReferenceChoice speechReferenceChoice = SpeechRadioSelection.ReferenceChoices[num];
				string style = speechReferenceChoice.Style;
				Toggle toggle3 = created.CreateOption((num < 4) ? transform6 : transform7, gameObject, speechReferenceChoice.Label, created._referenceGroup, type3, array);
				created._references.Add((toggle3, style));
				toggle3.onValueChanged.AddListener((Action<bool>)delegate(bool selected)
				{
					created.OnReference(selected, style);
				});
			}
			created.Apply(new SpeechSettings(), loaded: false, new TtsServiceState(TtsServicePhase.Off, "TTS 服务已关闭"));
			rows = created;
			diagnostic = "state=prepared; native_tab=3; anchor=dialogue_display_speed; radio_template=game_voice; rows=4; service_group=independent_2; reference_group=shared_8; native_rows_preserved=true";
			return true;
		}
		catch
		{
			created.Dispose();
			throw;
		}
	}

	internal bool IsValidFor(Transform itemRoot)
	{
		if (!_disposed && itemRoot != null && _itemRoot == itemRoot && _voiceRow != null && _voiceRow.parent == itemRoot && _voiceRow.gameObject.activeInHierarchy && _speedRow != null && _speedRow.parent == itemRoot && _speedRow.gameObject.activeInHierarchy && _rows.Count == 4)
		{
			return _rows.All((GameObject row) => row != null && (row.transform.parent == itemRoot || (_staging != null && row.transform.parent == _staging.transform)));
		}
		return false;
	}

	internal void MaintainPlacement()
	{
		if (!IsValidFor(_itemRoot) || Time.frameCount <= _createdFrame || _removedLocalizers.Any((UnityEngine.Object component) => component != null))
		{
			return;
		}
		bool flag = false;
		for (int num = 0; num < _rows.Count; num++)
		{
			GameObject gameObject = _rows[num];
			if (gameObject.transform.parent != _itemRoot)
			{
				gameObject.transform.SetParent(_itemRoot, worldPositionStays: false);
				flag = true;
			}
			int num2 = _speedRow.GetSiblingIndex() + 1 + num;
			if (gameObject.transform.GetSiblingIndex() != num2)
			{
				gameObject.transform.SetSiblingIndex(num2);
				flag = true;
			}
			if (!gameObject.activeSelf)
			{
				gameObject.SetActive(value: true);
				flag = true;
			}
		}
		RefreshLabels();
		if (flag)
		{
			Apply(_settings, _loaded, _serviceState);
			RebuildParent();
		}
	}

	internal void Apply(SpeechSettings settings, bool loaded, TtsServiceState serviceState)
	{
		_settings = settings;
		_serviceState = serviceState;
		_loaded = loaded;
		if (_disposed)
		{
			return;
		}
		_suppress = true;
		try
		{
			string b = SpeechRadioSelection.SelectedReference(settings);
			SpeechServiceRadioState speechServiceRadioState = SpeechRadioSelection.ServicePresentation(serviceState.Phase, loaded);
			if (_serviceGroup != null)
			{
				_serviceGroup.allowSwitchOff = true;
			}
			foreach (var service in _services)
			{
				if (!(service.Toggle == null))
				{
					service.Toggle.interactable = (service.Enabled ? speechServiceRadioState.EnableInteractable : speechServiceRadioState.DisableInteractable);
					Toggle item = service.Toggle;
					bool? selectedEnabled = speechServiceRadioState.SelectedEnabled;
					int isOnWithoutNotify;
					if (selectedEnabled.HasValue)
					{
						bool valueOrDefault = selectedEnabled == true;
						isOnWithoutNotify = ((service.Enabled == valueOrDefault) ? 1 : 0);
					}
					else
					{
						isOnWithoutNotify = 0;
					}
					item.SetIsOnWithoutNotify((byte)isOnWithoutNotify != 0);
				}
			}
			if (_serviceGroup != null)
			{
				_serviceGroup.allowSwitchOff = !speechServiceRadioState.SelectedEnabled.HasValue;
			}
			foreach (var language in _languages)
			{
				if (!(language.Toggle == null))
				{
					language.Toggle.interactable = loaded;
					language.Toggle.SetIsOnWithoutNotify(language.Language == settings.Language);
				}
			}
			foreach (var reference in _references)
			{
				if (!(reference.Toggle == null))
				{
					reference.Toggle.interactable = loaded;
					reference.Toggle.SetIsOnWithoutNotify(string.Equals(reference.Style, b, StringComparison.Ordinal));
				}
			}
			RefreshLabels();
		}
		finally
		{
			_suppress = false;
		}
	}

	private void OnServiceEnabled(bool selected, bool enabled)
	{
		if (selected && !_suppress && !_disposed && SpeechRadioSelection.CanRequestServiceEnabled(_serviceState.Phase, enabled, _loaded))
		{
			_selectServiceEnabled(enabled);
			Apply(_settings, _loaded, _serviceState);
		}
	}

	private void OnLanguage(bool selected, SpeechLanguage language)
	{
		if (selected && !_suppress && _loaded && !_disposed && language != _settings.Language)
		{
			_selectLanguage(language);
			Apply(_settings, _loaded, _serviceState);
		}
	}

	private void OnReference(bool selected, string? style)
	{
		if (selected && !_suppress && _loaded && !_disposed && !string.Equals(style, SpeechRadioSelection.SelectedReference(_settings), StringComparison.Ordinal))
		{
			_selectReference(style);
			Apply(_settings, _loaded, _serviceState);
		}
	}

	private Transform CreateRow(string suffix, string caption, Transform nativeRow, Component title, Transform nativeOptions, Type tmpType, Type?[] localizerTypes)
	{
		GameObject gameObject = RectObject("Just_Lilith.NativeLanguage.TTS." + suffix, _staging.transform);
		gameObject.SetActive(value: false);
		_rows.Add(gameObject);
		RectTransform component = nativeRow.GetComponent<RectTransform>();
		RectTransform component2 = gameObject.GetComponent<RectTransform>();
		CopyRect(component, component2);
		RectTransform component3 = title.GetComponent<RectTransform>();
		GridLayoutGroup gridLayoutGroup = nativeOptions.GetComponent<GridLayoutGroup>() ?? throw new InvalidOperationException("The native voice options have no GridLayoutGroup.");
		float num = Mathf.Max(component3.rect.height, gridLayoutGroup.cellSize.y);
		component2.sizeDelta = new Vector2(component.sizeDelta.x, num);
		LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = component.rect.width;
		layoutElement.minHeight = num;
		layoutElement.preferredHeight = num;
		layoutElement.flexibleWidth = 1f;
		if (!string.IsNullOrEmpty(caption))
		{
			GameObject gameObject2 = UnityEngine.Object.Instantiate(title.gameObject, gameObject.transform, worldPositionStays: false);
			gameObject2.name = "Title";
			StripCloneLocalization(gameObject2.transform, localizerTypes);
			object obj = GetComponent(gameObject2, tmpType) ?? throw new InvalidOperationException("Native title TMP component was not cloned.");
			_labels.Add((obj, caption));
			WriteText(obj, caption);
		}
		GameObject gameObject3 = RectObject("Options", gameObject.transform);
		RectTransform component4 = gameObject3.GetComponent<RectTransform>();
		CopyRect(nativeOptions.GetComponent<RectTransform>(), component4);
		component4.sizeDelta = new Vector2(component4.sizeDelta.x, num);
		GridLayoutGroup gridLayoutGroup2 = gameObject3.AddComponent<GridLayoutGroup>();
		gridLayoutGroup2.padding = new RectOffset(gridLayoutGroup.padding.left, gridLayoutGroup.padding.right, gridLayoutGroup.padding.top, gridLayoutGroup.padding.bottom);
		gridLayoutGroup2.childAlignment = gridLayoutGroup.childAlignment;
		gridLayoutGroup2.startCorner = GridLayoutGroup.Corner.UpperLeft;
		gridLayoutGroup2.startAxis = GridLayoutGroup.Axis.Horizontal;
		gridLayoutGroup2.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		gridLayoutGroup2.constraintCount = 4;
		gridLayoutGroup2.spacing = Vector2.zero;
		gridLayoutGroup2.cellSize = new Vector2((component4.sizeDelta.x - (float)gridLayoutGroup2.padding.horizontal) / 4f, num - (float)gridLayoutGroup2.padding.vertical);
		return gameObject3.transform;
	}

	private Toggle CreateOption(Transform parent, GameObject prefab, string caption, ToggleGroup group, Type tmpType, Type?[] localizerTypes)
	{
		GameObject gameObject = UnityEngine.Object.Instantiate(prefab, parent, worldPositionStays: false);
		gameObject.name = "ReferenceOrLanguage-" + caption;
		gameObject.SetActive(value: false);
		StripCloneLocalization(gameObject.transform, localizerTypes);
		Toggle toggle = gameObject.GetComponentInChildren<Toggle>(includeInactive: true) ?? throw new InvalidOperationException("Native circular Toggle was not cloned.");
		toggle.onValueChanged = new Toggle.ToggleEvent();
		toggle.group = null;
		toggle.SetIsOnWithoutNotify(value: false);
		toggle.group = group;
		toggle.interactable = false;
		List<object> list = FindComponents(gameObject.transform, tmpType);
		if (list.Count != 1 || !(list[0] is Component component))
		{
			throw new InvalidOperationException("Native radio label is ambiguous.");
		}
		Vector2 cellSize = parent.GetComponent<GridLayoutGroup>().cellSize;
		RectTransform component2 = toggle.GetComponent<RectTransform>();
		float x = component2.sizeDelta.x;
		float x2 = prefab.GetComponent<RectTransform>().sizeDelta.x;
		float num = Mathf.Max(0f, (x2 - x) / 2f);
		Vector2 anchorMin = (component2.anchorMax = new Vector2(0f, 0.5f));
		component2.anchorMin = anchorMin;
		component2.anchoredPosition = new Vector2(num + x / 2f, 0f);
		component.transform.SetParent(toggle.transform, worldPositionStays: false);
		RectTransform component3 = component.GetComponent<RectTransform>();
		anchorMin = (component3.anchorMax = new Vector2(1f, 0.5f));
		component3.anchorMin = anchorMin;
		component3.pivot = new Vector2(0f, 0.5f);
		component3.anchoredPosition = new Vector2(2f, 0f);
		component3.sizeDelta = new Vector2(Mathf.Max(1f, cellSize.x - num - x - 2f), cellSize.y);
		if (component is Graphic graphic)
		{
			graphic.raycastTarget = true;
		}
		_labels.Add((list[0], caption));
		WriteText(list[0], caption);
		gameObject.SetActive(value: true);
		return toggle;
	}

	private void StripCloneLocalization(Transform root, Type?[] types)
	{
		foreach (Type type in types)
		{
			foreach (object item in FindComponents(root, type))
			{
				if (item is Behaviour behaviour)
				{
					behaviour.enabled = false;
				}
				if (item is UnityEngine.Object obj)
				{
					_removedLocalizers.Add(obj);
					UnityEngine.Object.Destroy(obj);
				}
			}
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		GameObject selected = EventSystem.current?.currentSelectedGameObject;
		if (selected != null && _rows.Any((GameObject row) => row != null && (selected == row || selected.transform.IsChildOf(row.transform))))
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		foreach (Toggle item in _services.Select<(Toggle, bool, object), Toggle>(((Toggle Toggle, bool Enabled, object Text) item) => item.Toggle).Concat(_languages.Select<(Toggle, SpeechLanguage), Toggle>(((Toggle Toggle, SpeechLanguage Language) item) => item.Toggle)).Concat(_references.Select<(Toggle, string), Toggle>(((Toggle Toggle, string Style) item) => item.Toggle)))
		{
			if (!(item == null))
			{
				item.onValueChanged = new Toggle.ToggleEvent();
				item.group = null;
			}
		}
		foreach (GameObject row in _rows)
		{
			if (!(row == null))
			{
				row.SetActive(value: false);
				if (_staging != null)
				{
					row.transform.SetParent(_staging.transform, worldPositionStays: false);
				}
				UnityEngine.Object.Destroy(row);
			}
		}
		if (_staging != null)
		{
			UnityEngine.Object.Destroy(_staging);
		}
		_rows.Clear();
		_services.Clear();
		_languages.Clear();
		_references.Clear();
		_labels.Clear();
		_removedLocalizers.Clear();
		RebuildParent();
	}

	private object TakeDynamicServiceLabel(Toggle toggle)
	{
		int num = _labels.FindIndex(((object Text, string Label) tuple) => tuple.Text is Component component && component != null && component.transform.IsChildOf(toggle.transform));
		if (num < 0)
		{
			throw new InvalidOperationException("Owned service radio label is missing.");
		}
		object item = _labels[num].Text;
		_labels.RemoveAt(num);
		return item;
	}

	private void RefreshLabels()
	{
		foreach (var (obj, value) in _labels)
		{
			if (!(obj is UnityEngine.Object obj2) || obj2 != null)
			{
				WriteText(obj, value);
			}
		}
		SpeechServiceRadioState speechServiceRadioState = SpeechRadioSelection.ServicePresentation(_serviceState.Phase, _loaded);
		foreach (var service in _services)
		{
			if (!(service.Text is UnityEngine.Object obj3) || obj3 != null)
			{
				WriteText(service.Text, service.Enabled ? speechServiceRadioState.EnableLabel : speechServiceRadioState.DisableLabel);
			}
		}
	}

	private void RebuildParent()
	{
		if (_itemRoot != null)
		{
			RectTransform component = _itemRoot.GetComponent<RectTransform>();
			if ((object)component != null)
			{
				LayoutRebuilder.MarkLayoutForRebuild(component);
			}
		}
	}

	private static bool HasExpectedLanguageConfig(object view)
	{
		object obj = Read(view, "_config");
		object obj2 = ((obj == null) ? null : Read(obj, "_settingItems"));
		if (obj2 == null)
		{
			return false;
		}
		object[] source = (from entry in SnapshotList(obj2)
			where Convert.ToInt32(Read(entry, "tab")) == 3
			select entry).ToArray();
		object[] array = source.Where((object entry) => Read(entry, "uiTypeId") as string == "Item_Slider").ToArray();
		object[] array2 = source.Where((object entry) => Read(entry, "uiTypeId") as string == "Item_Toggle").ToArray();
		if (array.Length == 1 && Read(array[0], "itemId") as string == "dialogue_display_speed" && array2.Length == 1)
		{
			return Read(array2[0], "itemId") as string == "game_voice";
		}
		return false;
	}

	private static List<object> SnapshotList(object list)
	{
		Type type = list.GetType();
		PropertyInfo property = type.GetProperty("Count");
		MethodInfo method = type.GetMethod("get_Count", Type.EmptyTypes);
		PropertyInfo property2 = type.GetProperty("Item");
		MethodInfo methodInfo = type.GetMethods().FirstOrDefault((MethodInfo methodInfo2) => methodInfo2.Name == "get_Item" && methodInfo2.GetParameters().Length == 1);
		if ((property == null && method == null) || (property2 == null && methodInfo == null))
		{
			return new List<object>();
		}
		int num = Convert.ToInt32(property?.GetValue(list) ?? method.Invoke(list, null));
		if ((num < 0 || num > 256) ? true : false)
		{
			return new List<object>();
		}
		List<object> list2 = new List<object>(num);
		for (int num2 = 0; num2 < num; num2++)
		{
			object[] array = new object[1] { num2 };
			object obj = property2?.GetValue(list, array) ?? methodInfo.Invoke(list, array);
			if (obj != null)
			{
				list2.Add(obj);
			}
		}
		return list2;
	}

	private static object? Read(object instance, string name)
	{
		return RuntimeValueAccessor.Find(instance.GetType(), name)?.GetValue(instance);
	}

	private static object? FindUniqueComponent(Transform root, Type type)
	{
		List<object> list = FindComponents(root, type);
		if (list.Count != 1)
		{
			return null;
		}
		return list[0];
	}

	private static List<object> FindComponents(Transform root, Type type)
	{
		List<object> found = new List<object>();
		Visit(root);
		return found;
		void Visit(Transform current)
		{
			object component = GetComponent(current.gameObject, type);
			if (component != null)
			{
				found.Add(component);
			}
			for (int i = 0; i < current.childCount; i++)
			{
				Visit(current.GetChild(i));
			}
		}
	}

	private static object? GetComponent(GameObject root, Type type)
	{
		return typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public).First((MethodInfo method) => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0).MakeGenericMethod(type)
			.Invoke(root, null);
	}

	private static Transform? DirectChild(Transform child, Transform parent)
	{
		while (child.parent != null && child.parent != parent)
		{
			child = child.parent;
		}
		if (!(child.parent == parent))
		{
			return null;
		}
		return child;
	}

	private static Type? ResolveType(string name)
	{
		Type type = Type.GetType(name, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		string[] pieces = name.Split(',');
		return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly assembly) => string.Equals(assembly.GetName().Name, pieces[1].Trim(), StringComparison.OrdinalIgnoreCase))?.GetType(pieces[0].Trim(), throwOnError: false, ignoreCase: false);
	}

	private static void WriteText(object text, string value)
	{
		PropertyInfo property = text.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		if ((object)property == null || !property.CanWrite)
		{
			throw new InvalidOperationException("Native TMP text is not writable.");
		}
		if (!string.Equals(property.GetValue(text) as string, value, StringComparison.Ordinal))
		{
			property.SetValue(text, value);
		}
	}

	private static GameObject RectObject(string name, Transform parent)
	{
		GameObject gameObject = new GameObject(name, Il2CppType.Of<RectTransform>());
		gameObject.layer = parent.gameObject.layer;
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		return gameObject;
	}

	private static void CopyRect(RectTransform source, RectTransform target)
	{
		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.anchoredPosition = source.anchoredPosition;
		target.sizeDelta = source.sizeDelta;
		target.localScale = source.localScale;
		target.localRotation = source.localRotation;
	}
}
