using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Just_Lilith.Unity.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Just_Lilith.Unity.Ui;

internal sealed class NativeLilithConversationRows : IDisposable
{
	private const string BigButtonTypeName = "UI.TraySettingNew.SettingItems.SettingBigButtonItem, Assembly-CSharp";

	private const string Prefix = "Just_Lilith.NativeLilith.";

	private const string LanguageItemTypeName = "UI.TraySettingNew.SpecialItems.SettingLanguageSetItem, Assembly-CSharp";

	private const string LanguageButtonTypeName = "UI.TraySetting.TraySettingGameLanguageButton, Assembly-CSharp";

	private static readonly string[] LocalizationTypeNames = new string[2] { "AutoBindLocalizeStringEvent, Assembly-CSharp", "UnityEngine.Localization.Components.LocalizeStringEvent, Unity.Localization" };

	private const float ButtonSpacing = 5f;

	private const float BottomInset = 12f;

	private readonly Transform _itemRoot;

	private readonly RectTransform _panel;

	private readonly RectTransform _templateButton;

	private readonly bool _liveTemplate;

	private readonly GameObject _footer;

	private readonly RectTransform _footerRect;

	private readonly GameObject _staging;

	private readonly int _createdFrame;

	private readonly int _footerRow;

	private readonly bool _anchorBelowWardrobe;

	private readonly List<(GameObject Root, Button Button, object? Label)> _rows = new List<(GameObject, Button, object)>();

	private readonly Action[] _actions;

	private readonly float[] _columnWeights;

	private readonly HashSet<int> _circularColumns = new HashSet<int>();

	private readonly Dictionary<int, List<Button>> _additionalButtons = new Dictionary<int, List<Button>>();

	private readonly List<UnityEngine.Object> _ownedVisuals = new List<UnityEngine.Object>();

	private bool _disposed;

	private string _journeyName = "新建…";

	private string[]? _captions;

	internal void SetCaptions(params string[] captions)
	{
		if (captions.Length != _rows.Count)
		{
			throw new ArgumentException("Caption count must match footer columns.");
		}
		_captions = captions;
		RefreshLabels();
	}

	internal void SetInteractivity(params bool[] values)
	{
		if (values.Length != _rows.Count)
		{
			throw new ArgumentException("Interactivity count must match footer columns.");
		}
		for (int i = 0; i < _rows.Count; i++)
		{
			if (_rows[i].Button != null)
			{
				_rows[i].Button.interactable = values[i];
			}
			if (!_additionalButtons.TryGetValue(i, out List<Button> value))
			{
				continue;
			}
			foreach (Button item in value)
			{
				if (item != null)
				{
					item.interactable = values[i];
				}
			}
		}
	}

	private NativeLilithConversationRows(Transform itemRoot, RectTransform panel, RectTransform templateButton, bool liveTemplate, Transform home, Action[] actions, float[] columnWeights, bool anchorBelowWardrobe, int footerRow)
	{
		_itemRoot = itemRoot;
		_panel = panel;
		_templateButton = templateButton;
		_liveTemplate = liveTemplate;
		_actions = actions;
		_columnWeights = columnWeights;
		_anchorBelowWardrobe = anchorBelowWardrobe;
		_footerRow = footerRow;
		_footer = new GameObject("Just_Lilith.NativeLilith.Footer", Il2CppType.Of<RectTransform>());
		_footer.layer = panel.gameObject.layer;
		_footer.transform.SetParent(panel, worldPositionStays: false);
		_footer.SetActive(value: false);
		_footerRect = _footer.GetComponent<RectTransform>();
		_footerRect.anchorMin = (_footerRect.anchorMax = Vector2.zero);
		_footerRect.pivot = Vector2.zero;
		_staging = new GameObject("Just_Lilith.NativeLilith.InactiveStaging", Il2CppType.Of<RectTransform>());
		_staging.layer = home.gameObject.layer;
		_staging.transform.SetParent(home, worldPositionStays: false);
		_staging.SetActive(value: false);
		_createdFrame = Time.frameCount;
	}

	internal static bool TryCreate(Transform itemRoot, Transform home, Action first, Action second, Action third, Action fourth, int footerRow, out NativeLilithConversationRows? result, out string diagnostic)
	{
		return TryCreateCore(itemRoot, home, new Action[4] { first, second, third, fourth }, new float[4] { 1f, 1f, 1f, 1f }, anchorBelowWardrobe: false, footerRow, out result, out diagnostic);
	}

	internal static bool TryCreate(Transform itemRoot, Transform home, Action first, Action second, Action third, float[] columnWeights, bool anchorBelowWardrobe, int footerRow, out NativeLilithConversationRows? result, out string diagnostic)
	{
		return TryCreateCore(itemRoot, home, new Action[3] { first, second, third }, columnWeights, anchorBelowWardrobe, footerRow, out result, out diagnostic);
	}

	private static bool TryCreateCore(Transform itemRoot, Transform home, Action[] actions, float[] columnWeights, bool anchorBelowWardrobe, int footerRow, out NativeLilithConversationRows? result, out string diagnostic)
	{
		result = null;
		diagnostic = "state=awaiting_native_wardrobe";
		int num = actions.Length;
		bool flag = ((num < 1 || num > 4) ? true : false);
		if (flag || columnWeights.Length != actions.Length || columnWeights.Any((float weight) => weight <= 0f))
		{
			throw new ArgumentException("Footer actions and positive column weights must match.");
		}
		Type type = ResolveType("UI.TraySettingNew.SettingItems.SettingBigButtonItem, Assembly-CSharp");
		if (type == null || itemRoot == null || home == null)
		{
			diagnostic = "state=awaiting_native_type_or_root";
			return false;
		}
		Transform transform = FindWardrobe(itemRoot, type);
		if (anchorBelowWardrobe && transform == null)
		{
			diagnostic = "state=awaiting_live_wardrobe_anchor";
			return false;
		}
		Transform transform2 = transform ?? FindWardrobePrefab(itemRoot, type);
		if (transform2 == null)
		{
			return false;
		}
		object obj = FindComponent(transform2, type);
		Button button = ((obj == null) ? null : (RuntimeValueAccessor.Find(obj.GetType(), "_button")?.GetValue(obj) as Button));
		if ((object)button == null)
		{
			button = transform2.GetComponentInChildren<Button>(includeInactive: true);
		}
		RectTransform rectTransform = button?.GetComponent<RectTransform>();
		if (rectTransform == null)
		{
			diagnostic = "state=awaiting_native_button";
			return false;
		}
		RectTransform rectTransform2 = itemRoot.parent?.GetComponent<RectTransform>();
		if (rectTransform2 == null)
		{
			diagnostic = "state=awaiting_native_panel";
			return false;
		}
		NativeLilithConversationRows nativeLilithConversationRows = new NativeLilithConversationRows(itemRoot, rectTransform2, rectTransform, transform != null, home, actions.ToArray(), columnWeights.ToArray(), anchorBelowWardrobe, footerRow);
		try
		{
			for (int num2 = 0; num2 < actions.Length; num2++)
			{
				nativeLilithConversationRows.CreateRow(transform2, type, num2);
			}
			result = nativeLilithConversationRows;
			diagnostic = "state=prepared; native_tab=4; template=" + ((transform == null) ? "wardrobe_prefab" : "live_wardrobe") + "; placement=" + (anchorBelowWardrobe ? "below_wardrobe" : "bottom") + "; columns=" + actions.Length;
			return true;
		}
		catch
		{
			nativeLilithConversationRows.Dispose();
			throw;
		}
	}

	internal bool IsValidFor(Transform itemRoot)
	{
		if (!_disposed && itemRoot != null && _itemRoot == itemRoot && _footer != null && _footer.transform.parent == _panel && _rows.Count == _actions.Length)
		{
			return _rows.All<(GameObject, Button, object)>(((GameObject Root, Button Button, object Label) row) => row.Root != null && (row.Root.transform.parent == _footer.transform || row.Root.transform.parent == _staging.transform));
		}
		return false;
	}

	internal void SetJourneyName(string name)
	{
		string text = (string.IsNullOrWhiteSpace(name) ? "新建幻境1" : name);
		_journeyName = ((text.Length > 3) ? (text.Substring(0, 2) + "…") : text);
		RefreshLabels();
	}

	internal bool MaintainPlacement()
	{
		if (!IsValidFor(_itemRoot))
		{
			return false;
		}
		if (Time.frameCount <= _createdFrame)
		{
			return false;
		}
		UpdateFooterGeometry();
		bool flag = false;
		foreach (var row in _rows)
		{
			if (row.Root.transform.parent != _footer.transform)
			{
				row.Root.transform.SetParent(_footer.transform, worldPositionStays: false);
				flag = true;
			}
			if (!row.Root.activeSelf)
			{
				row.Root.SetActive(value: true);
				flag = true;
			}
		}
		if (!_footer.activeSelf)
		{
			_footer.SetActive(value: true);
			flag = true;
		}
		if (flag)
		{
			_footer.transform.SetAsLastSibling();
		}
		for (int i = 0; i < _rows.Count; i++)
		{
			int num = i;
			if (_rows[i].Root.transform.GetSiblingIndex() != num)
			{
				_rows[i].Root.transform.SetSiblingIndex(num);
				flag = true;
			}
		}
		RefreshLabels();
		if (_footer.activeInHierarchy)
		{
			return _rows.All<(GameObject, Button, object)>(((GameObject Root, Button Button, object Label) row) => row.Root.activeInHierarchy);
		}
		return false;
	}

	private void UpdateFooterGeometry()
	{
		RectTransform component = _itemRoot.GetComponent<RectTransform>();
		if ((object)component == null)
		{
			return;
		}
		Rect rect = _templateButton.rect;
		float num;
		float a;
		float b;
		if (_liveTemplate)
		{
			num = _panel.InverseTransformPoint(_templateButton.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f))).x;
			a = _panel.InverseTransformPoint(_templateButton.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f))).x;
			float y = _panel.InverseTransformPoint(_templateButton.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f))).y;
			float y2 = _panel.InverseTransformPoint(_templateButton.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f))).y;
			b = y - y2;
		}
		else
		{
			float x = _panel.InverseTransformPoint(component.TransformPoint(new Vector3(component.rect.center.x, component.rect.center.y, 0f))).x;
			float num2 = Mathf.Min(rect.width, component.rect.width - 12f);
			num = x + _templateButton.anchoredPosition.x - num2 / 2f;
			a = num + num2;
			b = rect.height;
		}
		float y3 = _panel.InverseTransformPoint(component.TransformPoint(new Vector3(component.rect.xMin, component.rect.yMin, 0f))).y;
		float y4 = _panel.InverseTransformPoint(_templateButton.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f))).y;
		Rect rect2 = _panel.rect;
		num = Mathf.Max(num, rect2.xMin + 4f);
		a = Mathf.Min(a, rect2.xMax - 4f);
		float num3 = Mathf.Max(4f, a - num);
		b = Mathf.Max(24f, b);
		float num4 = (_anchorBelowWardrobe ? (y4 - (float)(_footerRow + 1) * (b + 5f)) : (y3 + 12f + (float)_footerRow * (b + 5f)));
		_footerRect.anchoredPosition = new Vector2(num - rect2.xMin, num4 - rect2.yMin);
		_footerRect.sizeDelta = new Vector2(num3, b);
		float num5 = Mathf.Max(1f, num3 - (float)(_rows.Count - 1) * 5f);
		float num6 = _columnWeights.Sum();
		float num7 = 0f;
		for (int i = 0; i < _rows.Count; i++)
		{
			RectTransform component2 = _rows[i].Root.GetComponent<RectTransform>();
			if (component2 == null)
			{
				continue;
			}
			float num8 = ((i == _rows.Count - 1) ? Mathf.Max(1f, num5 - num7 + (float)i * 5f) : Mathf.Max(1f, num5 * _columnWeights[i] / num6));
			Vector2 anchorMin = (component2.anchorMax = new Vector2(0f, 0.5f));
			component2.anchorMin = anchorMin;
			component2.pivot = new Vector2(0f, 0.5f);
			component2.sizeDelta = new Vector2(num8, b);
			component2.anchoredPosition = new Vector2(num7, 0f);
			num7 += num8 + 5f;
			RectTransform component3 = _rows[i].Button.GetComponent<RectTransform>();
			if (component3 != null && component3 != component2)
			{
				if (_circularColumns.Contains(i))
				{
					float num9 = Mathf.Max(22f, Mathf.Min(b, num8) - 6f);
					anchorMin = (component3.anchorMax = new Vector2(0.5f, 0.5f));
					component3.anchorMin = anchorMin;
					component3.pivot = new Vector2(0.5f, 0.5f);
					component3.sizeDelta = new Vector2(num9, num9);
					component3.anchoredPosition = Vector2.zero;
				}
				else
				{
					component3.anchorMin = new Vector2(0f, component3.anchorMin.y);
					component3.anchorMax = new Vector2(1f, component3.anchorMax.y);
					component3.sizeDelta = new Vector2(0f, b);
					component3.anchoredPosition = Vector2.zero;
				}
			}
		}
	}

	internal bool TryUseGameLanguageSelectorStyle(params int[] columns)
	{
		Type type = ResolveType("UI.TraySettingNew.SpecialItems.SettingLanguageSetItem, Assembly-CSharp");
		Type type2 = ResolveType("UI.TraySetting.TraySettingGameLanguageButton, Assembly-CSharp");
		if (type == null || type2 == null)
		{
			return false;
		}
		object obj = FindComponent(_staging.transform.parent, type);
		if (obj == null)
		{
			return false;
		}
		object obj2 = RuntimeValueAccessor.Find(obj.GetType(), "_gameLanguageButton")?.GetValue(obj);
		if (obj2 == null)
		{
			return false;
		}
		RectTransform rectTransform = RuntimeValueAccessor.Find(obj2.GetType(), "_container")?.GetValue(obj2) as RectTransform;
		if (rectTransform == null)
		{
			return false;
		}
		for (int i = 0; i < columns.Length; i++)
		{
			int num = columns[i];
			if (num < 0 || num >= _rows.Count)
			{
				throw new ArgumentOutOfRangeException("columns");
			}
			GameObject gameObject = UnityEngine.Object.Instantiate(rectTransform.gameObject, _staging.transform, worldPositionStays: false);
			gameObject.name = "Just_Lilith.NativeLilith.GameLanguageSelector" + num;
			gameObject.SetActive(value: false);
			StripLocalization(gameObject.transform);
			if (FindComponent(gameObject.transform, type2) is Behaviour behaviour)
			{
				behaviour.enabled = false;
				UnityEngine.Object.Destroy(behaviour);
			}
			List<Button> list = FindButtons(gameObject.transform);
			object obj3 = FindWritableText(gameObject.transform);
			if (list.Count == 0 || obj3 == null)
			{
				UnityEngine.Object.Destroy(gameObject);
				return false;
			}
			Action action = _actions[num];
			foreach (Button item in list)
			{
				item.onClick = new Button.ButtonClickedEvent();
				item.onClick.AddListener((Action)delegate
				{
					Invoke(action);
				});
				item.interactable = true;
			}
			(GameObject Root, Button Button, object? Label) tuple = _rows[num];
			tuple.Button.onClick = new Button.ButtonClickedEvent();
			tuple.Root.SetActive(value: false);
			UnityEngine.Object.Destroy(tuple.Root);
			_rows[num] = (gameObject, list[0], obj3);
			_additionalButtons[num] = list.Skip(1).ToList();
			FitJourneyCaption(obj3);
		}
		return true;
	}

	internal void ConfigureCircularColumn(int index, string glyph, Color normal, Color disabled)
	{
		if (index < 0 || index >= _rows.Count)
		{
			throw new ArgumentOutOfRangeException("index");
		}
		(GameObject, Button, object) tuple = _rows[index];
		Image image = tuple.Item2.image ?? (tuple.Item2.targetGraphic as Image) ?? throw new InvalidOperationException("Circular footer button has no Image.");
		Texture2D texture2D = new Texture2D(64, 64, TextureFormat.RGBA32, mipChain: false);
		texture2D.name = "Just_Lilith.NativeLilith.CircleTexture" + index;
		Color32[] array = new Color32[4096];
		for (int i = 0; i < 64; i++)
		{
			for (int j = 0; j < 64; j++)
			{
				float num = (float)j - 31.5f;
				float num2 = (float)i - 31.5f;
				array[i * 64 + j] = ((num * num + num2 * num2 <= 976.5625f) ? new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue) : new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, 0));
			}
		}
		texture2D.SetPixels32(array);
		texture2D.Apply(updateMipmaps: false, makeNoLongerReadable: true);
		Sprite sprite = Sprite.Create(texture2D, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
		sprite.name = "Just_Lilith.NativeLilith.CircleSprite" + index;
		_ownedVisuals.Add(sprite);
		_ownedVisuals.Add(texture2D);
		image.sprite = sprite;
		image.type = Image.Type.Simple;
		image.preserveAspect = true;
		image.color = Color.white;
		tuple.Item2.targetGraphic = image;
		ColorBlock colors = tuple.Item2.colors;
		colors.normalColor = normal;
		colors.highlightedColor = new Color(Mathf.Min(1f, normal.r + 0.12f), Mathf.Min(1f, normal.g + 0.12f), Mathf.Min(1f, normal.b + 0.12f), normal.a);
		colors.pressedColor = new Color(Mathf.Max(0f, normal.r - 0.12f), Mathf.Max(0f, normal.g - 0.12f), Mathf.Max(0f, normal.b - 0.12f), normal.a);
		colors.selectedColor = colors.highlightedColor;
		colors.disabledColor = disabled;
		colors.colorMultiplier = 1f;
		tuple.Item2.colors = colors;
		if (tuple.Item3 is Graphic graphic)
		{
			graphic.color = Color.white;
		}
		WriteText(tuple.Item3, glyph);
		_circularColumns.Add(index);
	}

	internal bool TryGetColumnRect(RectTransform canvas, int index, out Rect bounds)
	{
		bounds = default(Rect);
		if (!(canvas == null) && index >= 0 && index < _rows.Count)
		{
			RectTransform component = _rows[index].Root.GetComponent<RectTransform>();
			if ((object)component != null)
			{
				Vector3[] array = new Vector3[4];
				component.GetWorldCorners(array);
				Vector3 vector = canvas.InverseTransformPoint(array[0]);
				Vector3 vector2 = canvas.InverseTransformPoint(array[2]);
				bounds = Rect.MinMaxRect(vector.x, vector.y, vector2.x, vector2.y);
				return true;
			}
		}
		return false;
	}

	private static List<Button> FindButtons(Transform root)
	{
		List<Button> list = new List<Button>();
		Button component = root.GetComponent<Button>();
		if ((object)component != null)
		{
			list.Add(component);
		}
		for (int i = 0; i < root.childCount; i++)
		{
			list.AddRange(FindButtons(root.GetChild(i)));
		}
		return list;
	}

	private static void StripLocalization(Transform root)
	{
		string[] localizationTypeNames = LocalizationTypeNames;
		for (int i = 0; i < localizationTypeNames.Length; i++)
		{
			Type type = ResolveType(localizationTypeNames[i]);
			if (type == null)
			{
				continue;
			}
			foreach (object item in FindComponents(root, type))
			{
				if (item is Behaviour behaviour)
				{
					behaviour.enabled = false;
				}
				if (item is UnityEngine.Object obj)
				{
					UnityEngine.Object.Destroy(obj);
				}
			}
		}
	}

	private static List<object> FindComponents(Transform root, Type type)
	{
		List<object> list = new List<object>();
		object component = GetComponent(root.gameObject, type);
		if (component != null)
		{
			list.Add(component);
		}
		for (int i = 0; i < root.childCount; i++)
		{
			list.AddRange(FindComponents(root.GetChild(i), type));
		}
		return list;
	}

	private void CreateRow(Transform template, Type type, int index)
	{
		GameObject gameObject = UnityEngine.Object.Instantiate(template.gameObject, _staging.transform, worldPositionStays: false);
		gameObject.name = "Just_Lilith.NativeLilith.Column" + index;
		gameObject.SetActive(value: false);
		object obj = FindComponent(gameObject.transform, type);
		if (obj is Behaviour behaviour)
		{
			behaviour.enabled = false;
		}
		Button button = ((obj == null) ? null : (RuntimeValueAccessor.Find(obj.GetType(), "_button")?.GetValue(obj) as Button));
		if ((object)button == null)
		{
			button = gameObject.GetComponentInChildren<Button>(includeInactive: true);
		}
		if (button == null)
		{
			UnityEngine.Object.Destroy(gameObject);
			throw new InvalidOperationException("Wardrobe clone has no Button.");
		}
		object obj2 = ((obj == null) ? null : RuntimeValueAccessor.Find(obj.GetType(), "_buttonText")?.GetValue(obj));
		if (obj2 == null)
		{
			obj2 = FindWritableText(gameObject.transform);
		}
		if (obj2 == null)
		{
			UnityEngine.Object.Destroy(gameObject);
			throw new InvalidOperationException("Wardrobe clone has no caption.");
		}
		button.onClick = new Button.ButtonClickedEvent();
		Action action = _actions[index];
		button.onClick.AddListener((Action)delegate
		{
			Invoke(action);
		});
		button.interactable = true;
		_rows.Add((gameObject, button, obj2));
		if (index == 1)
		{
			FitJourneyCaption(obj2);
		}
		WriteText(obj2, Caption(index));
	}

	private static void FitJourneyCaption(object label)
	{
		try
		{
			Type type = label.GetType();
			type.GetProperty("fontSizeMin")?.SetValue(label, 10f);
			type.GetProperty("fontSizeMax")?.SetValue(label, 14f);
			type.GetProperty("enableAutoSizing")?.SetValue(label, true);
		}
		catch
		{
		}
	}

	private static void Invoke(Action action)
	{
		try
		{
			action();
		}
		catch
		{
		}
	}

	private void RefreshLabels()
	{
		for (int i = 0; i < _rows.Count; i++)
		{
			string text = Caption(i);
			if (!string.Equals(ReadText(_rows[i].Label), text, StringComparison.Ordinal))
			{
				WriteText(_rows[i].Label, text);
			}
		}
	}

	private string Caption(int index)
	{
		if (_captions != null)
		{
			return _captions[index];
		}
		return index switch
		{
			0 => "Persona", 
			1 => "幻境：" + _journeyName, 
			2 => "幻境记忆", 
			_ => "回忆", 
		};
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		GameObject selected = EventSystem.current?.currentSelectedGameObject;
		if (selected != null && _rows.Any<(GameObject, Button, object)>(((GameObject Root, Button Button, object Label) row) => row.Root != null && (selected == row.Root || selected.transform.IsChildOf(row.Root.transform))))
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
		foreach (List<Button> value in _additionalButtons.Values)
		{
			foreach (Button item in value)
			{
				if (item != null)
				{
					item.onClick = new Button.ButtonClickedEvent();
				}
			}
		}
		_additionalButtons.Clear();
		foreach (var row in _rows)
		{
			if (row.Button != null)
			{
				row.Button.onClick = new Button.ButtonClickedEvent();
			}
			if (!(row.Root == null))
			{
				row.Root.SetActive(value: false);
				if (_staging != null)
				{
					row.Root.transform.SetParent(_staging.transform, worldPositionStays: false);
				}
				UnityEngine.Object.Destroy(row.Root);
			}
		}
		_rows.Clear();
		foreach (UnityEngine.Object ownedVisual in _ownedVisuals)
		{
			if (ownedVisual != null)
			{
				UnityEngine.Object.Destroy(ownedVisual);
			}
		}
		_ownedVisuals.Clear();
		_circularColumns.Clear();
		if (_footer != null)
		{
			_footer.SetActive(value: false);
			UnityEngine.Object.Destroy(_footer);
		}
		if (_staging != null)
		{
			UnityEngine.Object.Destroy(_staging);
		}
	}

	private static Transform? FindWardrobe(Transform itemRoot, Type type)
	{
		Transform transform = null;
		for (int i = 0; i < itemRoot.childCount; i++)
		{
			Transform child = itemRoot.GetChild(i);
			if (child.name.StartsWith("Just_Lilith.NativeLilith.", StringComparison.Ordinal))
			{
				continue;
			}
			object obj = FindComponent(child, type);
			if (obj != null)
			{
				if ((object)transform == null)
				{
					transform = child;
				}
				string? text = ReadText(RuntimeValueAccessor.Find(obj.GetType(), "_buttonText")?.GetValue(obj));
				if (text != null && text.Contains("衣柜", StringComparison.Ordinal))
				{
					return child;
				}
			}
		}
		return transform;
	}

	private static Transform? FindWardrobePrefab(Transform itemRoot, Type type)
	{
		Transform transform = (itemRoot.parent?.Find("SettingPlanelPrefabs"))?.Find("Item_ButtonBIg");
		if (!(transform != null) || FindComponent(transform, type) == null)
		{
			return null;
		}
		return transform;
	}

	private static object? FindComponent(Transform root, Type type)
	{
		object component = GetComponent(root.gameObject, type);
		if (component != null)
		{
			return component;
		}
		for (int i = 0; i < root.childCount; i++)
		{
			component = FindComponent(root.GetChild(i), type);
			if (component != null)
			{
				return component;
			}
		}
		return null;
	}

	private static object? GetComponent(GameObject root, Type type)
	{
		try
		{
			return typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0)?.MakeGenericMethod(type).Invoke(root, null);
		}
		catch
		{
			return null;
		}
	}

	private static object? FindWritableText(Transform root)
	{
		foreach (Component component in root.GetComponents<Component>())
		{
			if (component != null && component.GetType().GetProperty("text")?.PropertyType == typeof(string))
			{
				return component;
			}
		}
		for (int i = 0; i < root.childCount; i++)
		{
			object obj = FindWritableText(root.GetChild(i));
			if (obj != null)
			{
				return obj;
			}
		}
		return null;
	}

	private static string? ReadText(object? target)
	{
		try
		{
			return target?.GetType().GetProperty("text")?.GetValue(target) as string;
		}
		catch
		{
			return null;
		}
	}

	private static void WriteText(object? target, string value)
	{
		try
		{
			target?.GetType().GetProperty("text")?.SetValue(target, value);
		}
		catch
		{
		}
	}

	private static Type? ResolveType(string fullName)
	{
		Type type = Type.GetType(fullName, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		try
		{
			return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly a) => a.GetName().Name == "Assembly-CSharp")?.GetType(fullName.Split(',')[0], throwOnError: false, ignoreCase: false);
		}
		catch
		{
			return null;
		}
	}
}
