using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Just_Lilith.Unity.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Just_Lilith.Unity.Ui;

internal sealed class NativeHotkeySettingsInjector : IDisposable
{
	private const int HotkeysTabValue = 7;

	private const string NativeViewTypeName = "UI.TraySettingNew.TraySettingNewView, Assembly-CSharp";

	private const string NativeRowTypeName = "UI.TraySettingNew.SpecialItems.SettingHotkeyRow, Assembly-CSharp";

	private const string NativeBigButtonTypeName = "UI.TraySettingNew.SettingItems.SettingBigButtonItem, Assembly-CSharp";

	private const string OwnedRowName = "Just_Lilith.NativeHotkey.Chat";

	private readonly ChatHotkeyController _hotkey;

	private GameObject? _row;

	private Button? _button;

	private object? _buttonText;

	private ManualLogSource? _log;

	private float _nextScan;

	private string _lastText = "";

	private string _lastDiagnostic = "";

	private bool _disposed;

	public NativeHotkeySettingsInjector(ChatHotkeyController hotkey)
	{
		_hotkey = hotkey;
	}

	public void AttachLogger(ManualLogSource logger)
	{
		_log = logger;
	}

	public void Maintain()
	{
		if (_disposed)
		{
			return;
		}
		if (_row != null)
		{
			RefreshRow();
		}
		if (Time.unscaledTime < _nextScan)
		{
			return;
		}
		_nextScan = Time.unscaledTime + 0.15f;
		try
		{
			if (!TryFindActiveHotkeysRoot(out Transform itemRoot))
			{
				RemoveOwnedRow();
				Report("state=awaiting_hotkeys_page");
				return;
			}
			if (_row != null && _row.transform.parent == itemRoot)
			{
				_row.transform.SetAsLastSibling();
				RefreshRow();
				return;
			}
			RemoveOwnedRow();
			if (!TryCreateRow(itemRoot))
			{
				Report("state=template_unavailable");
				return;
			}
			Report($"state=mounted; parent={HierarchyPath(itemRoot)}; sibling={_row.transform.GetSiblingIndex()}");
		}
		catch (Exception ex)
		{
			RemoveOwnedRow();
			Report("state=error; type=" + ex.GetType().Name);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			RemoveOwnedRow();
		}
	}

	private bool TryFindActiveHotkeysRoot(out Transform itemRoot)
	{
		itemRoot = null;
		foreach (LocatedUnityObject item in UnityRuntimeObjectLocator.FindAll("UI.TraySettingNew.TraySettingNewView, Assembly-CSharp"))
		{
			try
			{
				RuntimeValueAccessor runtimeValueAccessor = RuntimeValueAccessor.Find(item.ReflectedType, "_currentTab");
				RuntimeValueAccessor runtimeValueAccessor2 = RuntimeValueAccessor.Find(item.ReflectedType, "_settingItemRoot");
				RuntimeValueAccessor runtimeValueAccessor3 = RuntimeValueAccessor.Find(item.ReflectedType, "IsVisible");
				if (runtimeValueAccessor != null && runtimeValueAccessor2 != null)
				{
					object obj = runtimeValueAccessor3?.GetValue(item.Instance);
					if ((!(obj is bool) || (bool)obj) && Convert.ToInt32(runtimeValueAccessor.GetValue(item.Instance)) == 7 && runtimeValueAccessor2.GetValue(item.Instance) is Transform transform && !(transform == null) && transform.gameObject.activeInHierarchy)
					{
						itemRoot = transform;
						return true;
					}
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private bool TryCreateRow(Transform itemRoot)
	{
		Type type = ResolveRuntimeType("UI.TraySettingNew.SpecialItems.SettingHotkeyRow, Assembly-CSharp");
		if (type == null)
		{
			return false;
		}
		if (!(FindComponent(itemRoot, type, skipOwnedRow: true) is Component component) || component == null)
		{
			return false;
		}
		Transform transform = component.transform;
		while (transform.parent != null && transform.parent != itemRoot && transform.parent.IsChildOf(itemRoot))
		{
			transform = transform.parent;
		}
		if (transform.parent != itemRoot)
		{
			return false;
		}
		GameObject gameObject = UnityEngine.Object.Instantiate(transform.gameObject, itemRoot, worldPositionStays: false);
		gameObject.name = "Just_Lilith.NativeHotkey.Chat";
		gameObject.transform.SetAsLastSibling();
		object obj = FindComponent(gameObject.transform, type, skipOwnedRow: false);
		if (obj is Behaviour behaviour)
		{
			behaviour.enabled = false;
		}
		Type type2 = ResolveRuntimeType("UI.TraySettingNew.SettingItems.SettingBigButtonItem, Assembly-CSharp");
		object obj2 = ((type2 == null) ? null : FindComponent(gameObject.transform, type2, skipOwnedRow: false));
		if (obj2 is Behaviour behaviour2)
		{
			behaviour2.enabled = false;
		}
		Button button = ((obj2 == null) ? null : (RuntimeValueAccessor.Find(obj2.GetType(), "_button")?.GetValue(obj2) as Button));
		if ((object)button == null)
		{
			button = gameObject.GetComponentInChildren<Button>(includeInactive: true);
		}
		if (button == null)
		{
			UnityEngine.Object.Destroy(gameObject);
			return false;
		}
		object obj3 = ((obj2 == null) ? null : RuntimeValueAccessor.Find(obj2.GetType(), "_buttonText")?.GetValue(obj2));
		if (obj3 == null)
		{
			obj3 = FindWritableText(gameObject.transform);
		}
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener((Action)OnRowClicked);
		button.interactable = true;
		gameObject.SetActive(value: true);
		if (obj is UnityEngine.Object obj4)
		{
			UnityEngine.Object.Destroy(obj4);
		}
		_row = gameObject;
		_button = button;
		_buttonText = obj3;
		_lastText = "";
		RefreshRow();
		RectTransform component2 = itemRoot.GetComponent<RectTransform>();
		if ((object)component2 != null)
		{
			LayoutRebuilder.ForceRebuildLayoutImmediate(component2);
		}
		return true;
	}

	private void OnRowClicked()
	{
		try
		{
			if (_hotkey.IsCapturing)
			{
				_hotkey.CancelCapture();
			}
			else
			{
				_hotkey.BeginCapture();
			}
			RefreshRow();
		}
		catch (Exception ex)
		{
			Report("state=callback_error_contained; callback=chat_hotkey_row; type=" + ex.GetType().Name);
		}
	}

	private void RefreshRow()
	{
		if (_row == null)
		{
			_row = null;
			_button = null;
			_buttonText = null;
			_lastText = "";
			return;
		}
		string rowText = _hotkey.RowText;
		if (!string.Equals(rowText, _lastText, StringComparison.Ordinal) || !string.Equals(ReadRuntimeText(_buttonText), rowText, StringComparison.Ordinal))
		{
			WriteRuntimeText(_buttonText, rowText);
			_lastText = rowText;
			RectTransform component = _row.GetComponent<RectTransform>();
			if ((object)component != null)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(component);
			}
		}
	}

	private void RemoveOwnedRow()
	{
		if (_row != null)
		{
			try
			{
				_button?.onClick.RemoveAllListeners();
			}
			catch
			{
			}
			try
			{
				_row.SetActive(value: false);
			}
			catch
			{
			}
			try
			{
				UnityEngine.Object.Destroy(_row);
			}
			catch
			{
			}
		}
		_row = null;
		_button = null;
		_buttonText = null;
		_lastText = "";
	}

	private static object? FindComponent(Transform root, Type componentType, bool skipOwnedRow)
	{
		if (!skipOwnedRow || !IsOwned(root))
		{
			object componentByManagedType = GetComponentByManagedType(root.gameObject, componentType);
			if (componentByManagedType != null)
			{
				return componentByManagedType;
			}
		}
		for (int i = 0; i < root.childCount; i++)
		{
			Transform child = root.GetChild(i);
			if (!skipOwnedRow || !IsOwned(child))
			{
				object obj = FindComponent(child, componentType, skipOwnedRow);
				if (obj != null)
				{
					return obj;
				}
			}
		}
		return null;
	}

	private static bool IsOwned(Transform transform)
	{
		Transform transform2 = transform;
		while (transform2 != null)
		{
			if (string.Equals(transform2.name, "Just_Lilith.NativeHotkey.Chat", StringComparison.Ordinal))
			{
				return true;
			}
			transform2 = transform2.parent;
		}
		return false;
	}

	private static object? GetComponentByManagedType(GameObject root, Type componentType)
	{
		try
		{
			return typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo method) => method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)?.MakeGenericMethod(componentType).Invoke(root, null);
		}
		catch
		{
			return null;
		}
	}

	private static object? FindWritableText(Transform root)
	{
		try
		{
			foreach (Component component in root.GetComponents<Component>())
			{
				if (!(component == null))
				{
					PropertyInfo property = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
					if ((object)property != null && property.CanWrite && property.PropertyType == typeof(string))
					{
						return component;
					}
				}
			}
		}
		catch
		{
		}
		for (int i = 0; i < root.childCount; i++)
		{
			object obj2 = FindWritableText(root.GetChild(i));
			if (obj2 != null)
			{
				return obj2;
			}
		}
		return null;
	}

	private static void WriteRuntimeText(object? text, string value)
	{
		if (text == null)
		{
			return;
		}
		try
		{
			text.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.SetValue(text, value);
		}
		catch
		{
		}
	}

	private static string? ReadRuntimeText(object? text)
	{
		if (text == null)
		{
			return null;
		}
		try
		{
			return text.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.GetValue(text) as string;
		}
		catch
		{
			return null;
		}
	}

	private static Type? ResolveRuntimeType(string assemblyQualifiedName)
	{
		Type type = Type.GetType(assemblyQualifiedName, throwOnError: false);
		if (type != null)
		{
			return type;
		}
		try
		{
			int num = assemblyQualifiedName.IndexOf(',');
			string name = ((num < 0) ? assemblyQualifiedName : assemblyQualifiedName.Substring(0, num).Trim());
			return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly assembly) => string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase))?.GetType(name, throwOnError: false, ignoreCase: false);
		}
		catch
		{
			return null;
		}
	}

	private void Report(string diagnostic)
	{
		if (string.Equals(_lastDiagnostic, diagnostic, StringComparison.Ordinal))
		{
			return;
		}
		_lastDiagnostic = diagnostic;
		try
		{
			_log?.LogInfo("module=UI; entry=native_chat_hotkey_row; " + diagnostic);
		}
		catch
		{
		}
	}

	private static string HierarchyPath(Transform transform)
	{
		Stack<string> stack = new Stack<string>();
		Transform transform2 = transform;
		while (transform2 != null)
		{
			stack.Push(transform2.name);
			transform2 = transform2.parent;
		}
		return string.Join("/", stack);
	}
}
