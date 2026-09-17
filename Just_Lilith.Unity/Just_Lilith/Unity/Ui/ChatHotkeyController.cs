using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace Just_Lilith.Unity.Ui;

internal sealed class ChatHotkeyController : IDisposable
{
	private const string PreferenceKey = "local.just_lilith.hotkey.chat.v1";

	private const string LegacyPreferenceKey = "local.lilith.companion.hotkey.chat.v1";

	private const float CaptureDelaySeconds = 0.15f;

	private const float CaptureTimeoutSeconds = 8f;

	private const int VirtualKeyEscape = 27;

	private const int VirtualKeyBackspace = 8;

	private readonly bool[] _captureWasDown = new bool[256];

	private readonly NativeHotkeySettingsInjector _settingsInjector;

	private ChatHotkeyBinding _binding = ChatHotkeyBinding.Default;

	private MethodInfo? _getAsyncKeyState;

	private ManualLogSource? _log;

	private float _nextNativeResolve;

	private float _captureReadyAt;

	private float _captureDeadline;

	private bool _loaded;

	private bool _capturing;

	private bool _keyStateInitialized;

	private bool _keyWasDown;

	private bool _disposed;

	public bool IsCapturing => _capturing;

	public ChatHotkeyBinding Binding
	{
		get
		{
			EnsureLoaded();
			return _binding;
		}
	}

	public string BindingText => Binding.ToDisplayString();

	public string RowText
	{
		get
		{
			if (!_capturing)
			{
				return "唤出聊天框：" + BindingText;
			}
			return "唤出聊天框：请按新快捷键（Esc 取消，退格恢复 F7）";
		}
	}

	public ChatHotkeyController()
	{
		_settingsInjector = new NativeHotkeySettingsInjector(this);
	}

	public void AttachLogger(ManualLogSource logger)
	{
		_log = logger;
		_settingsInjector.AttachLogger(logger);
	}

	public ChatHotkeySample Poll()
	{
		if (_disposed)
		{
			return default(ChatHotkeySample);
		}
		EnsureLoaded();
		ChatHotkeySample result = default(ChatHotkeySample);
		try
		{
			if (_capturing)
			{
				TickCapture();
				result = new ChatHotkeySample(edgeObserved: false, isDown: false, "configured_hotkey_capture");
			}
			else
			{
				result = PollBinding();
			}
		}
		catch (Exception ex)
		{
			SafeLog("module=UI; state=chat_hotkey_poll_error; type=" + ex.GetType().Name);
		}
		try
		{
			_settingsInjector.Maintain();
		}
		catch (Exception ex2)
		{
			SafeLog("module=UI; state=chat_hotkey_injector_error; type=" + ex2.GetType().Name);
		}
		return result;
	}

	public void BeginCapture()
	{
		if (_disposed)
		{
			return;
		}
		EnsureLoaded();
		CancelNativeHotkeyCapture();
		ResolveNativeKeyboard(force: true);
		Array.Clear(_captureWasDown, 0, _captureWasDown.Length);
		if (_getAsyncKeyState != null)
		{
			for (int i = 8; i <= 254; i++)
			{
				if ((ChatHotkeyBinding.IsBindableVirtualKey(i) || IsModifierVirtualKey(i)) && TryReadNativeKey(i, out var down, out var _))
				{
					_captureWasDown[i] = down;
				}
			}
		}
		_capturing = true;
		_captureReadyAt = Time.unscaledTime + 0.15f;
		_captureDeadline = Time.unscaledTime + 8f;
		_keyStateInitialized = false;
		SafeLog("module=UI; state=chat_hotkey_capture_started");
	}

	public void CancelCapture()
	{
		if (_capturing)
		{
			_capturing = false;
			_keyStateInitialized = false;
			SafeLog("module=UI; state=chat_hotkey_capture_cancelled");
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_capturing = false;
			_settingsInjector.Dispose();
		}
	}

	private ChatHotkeySample PollBinding()
	{
		ResolveNativeKeyboard();
		if (_getAsyncKeyState != null && TryReadNativeKey(_binding.VirtualKey, out var down, out var pressedSinceSample))
		{
			bool flag = ReadNativeModifiers() == _binding.Modifiers;
			if (!_keyStateInitialized)
			{
				_keyStateInitialized = true;
				_keyWasDown = down;
				return new ChatHotkeySample(edgeObserved: false, down & flag, "desktop_configured_hotkey");
			}
			bool edgeObserved = ((down && !_keyWasDown) || (!down & pressedSinceSample)) & flag;
			_keyWasDown = down;
			return new ChatHotkeySample(edgeObserved, down & flag, "desktop_configured_hotkey");
		}
		KeyCode keyCode = ToUnityKeyCode(_binding.VirtualKey);
		if (keyCode == KeyCode.None)
		{
			return default(ChatHotkeySample);
		}
		bool flag2 = ReadUnityModifiers() == _binding.Modifiers;
		return new ChatHotkeySample(Input.GetKeyDown(keyCode) & flag2, Input.GetKey(keyCode) & flag2, "unity_configured_hotkey");
	}

	private void TickCapture()
	{
		float unscaledTime = Time.unscaledTime;
		if (IsNativeHotkeyCaptureActive())
		{
			CancelCapture();
		}
		else if (unscaledTime >= _captureDeadline)
		{
			CancelCapture();
		}
		else
		{
			if (unscaledTime < _captureReadyAt)
			{
				return;
			}
			ResolveNativeKeyboard();
			int virtualKey;
			if (_getAsyncKeyState != null)
			{
				if (NativeCaptureEdge(27))
				{
					CancelCapture();
					return;
				}
				if (NativeCaptureEdge(8))
				{
					CommitBinding(ChatHotkeyBinding.Default, "reset_to_default");
					return;
				}
				ChatHotkeyModifiers modifiers = ReadNativeModifiers();
				for (int i = 8; i <= 254; i++)
				{
					bool flag = !ChatHotkeyBinding.IsBindableVirtualKey(i);
					if (!flag)
					{
						bool flag2 = ((i == 8 || i == 27) ? true : false);
						flag = flag2;
					}
					if (!flag && NativeCaptureEdge(i))
					{
						ChatHotkeyBinding binding = new ChatHotkeyBinding(modifiers, i);
						if (binding.IsValid)
						{
							CommitBinding(binding, "captured");
							break;
						}
						SafeLog($"module=UI; state=chat_hotkey_capture_rejected; key={i:X2}");
					}
				}
			}
			else if (Input.GetKeyDown(KeyCode.Escape))
			{
				CancelCapture();
			}
			else if (Input.GetKeyDown(KeyCode.Backspace))
			{
				CommitBinding(ChatHotkeyBinding.Default, "reset_to_default");
			}
			else if (TryScanUnityPressedKey(out virtualKey))
			{
				ChatHotkeyBinding binding2 = new ChatHotkeyBinding(ReadUnityModifiers(), virtualKey);
				if (binding2.IsValid)
				{
					CommitBinding(binding2, "captured_unity_fallback");
					return;
				}
				SafeLog($"module=UI; state=chat_hotkey_capture_rejected; key={virtualKey:X2}");
			}
		}
	}

	private bool NativeCaptureEdge(int virtualKey)
	{
		if (!TryReadNativeKey(virtualKey, out var down, out var pressedSinceSample))
		{
			return false;
		}
		bool result = (down && !_captureWasDown[virtualKey]) || (!down & pressedSinceSample);
		_captureWasDown[virtualKey] = down;
		return result;
	}

	private void CommitBinding(ChatHotkeyBinding binding, string reason)
	{
		if (!binding.IsValid)
		{
			SafeLog($"module=UI; state=chat_hotkey_capture_rejected; key={binding.VirtualKey:X2}");
			return;
		}
		_binding = binding;
		_capturing = false;
		_keyStateInitialized = false;
		try
		{
			PlayerPrefs.SetInt("local.just_lilith.hotkey.chat.v1", binding.Encode());
			PlayerPrefs.Save();
			SafeLog("module=UI; state=chat_hotkey_saved; binding=" + binding.ToDisplayString() + "; reason=" + reason);
		}
		catch (Exception ex)
		{
			SafeLog("module=UI; state=chat_hotkey_save_error; type=" + ex.GetType().Name);
		}
	}

	private void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			_binding = ChatHotkeyPreferenceMigration.Load("local.just_lilith.hotkey.chat.v1", "local.lilith.companion.hotkey.chat.v1", (string key) => PlayerPrefs.HasKey(key), (string key) => PlayerPrefs.GetInt(key, ChatHotkeyBinding.Default.Encode()), delegate(string key, int value)
			{
				PlayerPrefs.SetInt(key, value);
			}, delegate
			{
				PlayerPrefs.Save();
			}, delegate(Exception error)
			{
				SafeLog("module=UI; state=chat_hotkey_migration_save_error; type=" + error.GetType().Name);
			});
		}
		catch
		{
			_binding = ChatHotkeyBinding.Default;
		}
		SafeLog("module=UI; state=chat_hotkey_loaded; binding=" + _binding.ToDisplayString());
	}

	private void ResolveNativeKeyboard(bool force = false)
	{
		if (_getAsyncKeyState != null || (!force && Time.unscaledTime < _nextNativeResolve))
		{
			return;
		}
		_nextNativeResolve = Time.unscaledTime + 1f;
		try
		{
			_getAsyncKeyState = Type.GetType("WindowsNativeAPI, Assembly-CSharp", throwOnError: false)?.GetMethod("GetAsyncKeyState", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(int) }, null);
		}
		catch
		{
			_getAsyncKeyState = null;
		}
	}

	private static bool IsNativeHotkeyCaptureActive()
	{
		try
		{
			object obj = Type.GetType("HotkeyService, Assembly-CSharp", throwOnError: false)?.GetProperty("CapturingAction", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			if (obj == null)
			{
				return false;
			}
			object obj2 = obj.GetType().GetProperty("HasValue", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);
			return obj2 is bool && (bool)obj2;
		}
		catch
		{
			return false;
		}
	}

	private static void CancelNativeHotkeyCapture()
	{
		try
		{
			Type.GetType("HotkeyService, Assembly-CSharp", throwOnError: false)?.GetMethod("CancelCapture", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null)?.Invoke(null, null);
		}
		catch
		{
		}
	}

	private bool TryReadNativeKey(int virtualKey, out bool down, out bool pressedSinceSample)
	{
		down = false;
		pressedSinceSample = false;
		try
		{
			if (_getAsyncKeyState == null)
			{
				return false;
			}
			int num = Convert.ToInt32(_getAsyncKeyState.Invoke(null, new object[1] { virtualKey }));
			down = (num & 0x8000) != 0;
			pressedSinceSample = (num & 1) != 0;
			return true;
		}
		catch
		{
			_getAsyncKeyState = null;
			return false;
		}
	}

	private ChatHotkeyModifiers ReadNativeModifiers()
	{
		ChatHotkeyModifiers chatHotkeyModifiers = ChatHotkeyModifiers.None;
		if (IsNativeDown(17) || IsNativeDown(162) || IsNativeDown(163))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Control;
		}
		if (IsNativeDown(18) || IsNativeDown(164) || IsNativeDown(165))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Alt;
		}
		if (IsNativeDown(16) || IsNativeDown(160) || IsNativeDown(161))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Shift;
		}
		if (IsNativeDown(91) || IsNativeDown(92))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Windows;
		}
		return chatHotkeyModifiers;
	}

	private bool IsNativeDown(int virtualKey)
	{
		bool down;
		bool pressedSinceSample;
		return TryReadNativeKey(virtualKey, out down, out pressedSinceSample) & down;
	}

	private static ChatHotkeyModifiers ReadUnityModifiers()
	{
		ChatHotkeyModifiers chatHotkeyModifiers = ChatHotkeyModifiers.None;
		if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Control;
		}
		if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Alt;
		}
		if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Shift;
		}
		if (Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows))
		{
			chatHotkeyModifiers |= ChatHotkeyModifiers.Windows;
		}
		return chatHotkeyModifiers;
	}

	private static bool TryScanUnityPressedKey(out int virtualKey)
	{
		for (int i = 8; i <= 254; i++)
		{
			if (ChatHotkeyBinding.IsBindableVirtualKey(i))
			{
				KeyCode keyCode = ToUnityKeyCode(i);
				if (keyCode != KeyCode.None && Input.GetKeyDown(keyCode))
				{
					virtualKey = i;
					return true;
				}
			}
		}
		virtualKey = 0;
		return false;
	}

	private static KeyCode ToUnityKeyCode(int virtualKey)
	{
		if (virtualKey >= 65 && virtualKey <= 90 && Enum.TryParse<KeyCode>(((char)virtualKey).ToString(), out var result))
		{
			return result;
		}
		if (virtualKey >= 48 && virtualKey <= 57 && Enum.TryParse<KeyCode>("Alpha" + (virtualKey - 48), out var result2))
		{
			return result2;
		}
		if (virtualKey >= 96 && virtualKey <= 105 && Enum.TryParse<KeyCode>("Keypad" + (virtualKey - 96), out var result3))
		{
			return result3;
		}
		if (virtualKey >= 112 && virtualKey <= 126 && Enum.TryParse<KeyCode>("F" + (virtualKey - 111), out var result4))
		{
			return result4;
		}
		return virtualKey switch
		{
			8 => KeyCode.Backspace, 
			9 => KeyCode.Tab, 
			13 => KeyCode.Return, 
			19 => KeyCode.Pause, 
			27 => KeyCode.Escape, 
			32 => KeyCode.Space, 
			33 => KeyCode.PageUp, 
			34 => KeyCode.PageDown, 
			35 => KeyCode.End, 
			36 => KeyCode.Home, 
			37 => KeyCode.LeftArrow, 
			38 => KeyCode.UpArrow, 
			39 => KeyCode.RightArrow, 
			40 => KeyCode.DownArrow, 
			45 => KeyCode.Insert, 
			46 => KeyCode.Delete, 
			106 => KeyCode.KeypadMultiply, 
			107 => KeyCode.KeypadPlus, 
			109 => KeyCode.KeypadMinus, 
			110 => KeyCode.KeypadPeriod, 
			111 => KeyCode.KeypadDivide, 
			_ => KeyCode.None, 
		};
	}

	private static bool IsModifierVirtualKey(int key)
	{
		if ((uint)(key - 16) <= 2u || (uint)(key - 91) <= 1u || (uint)(key - 160) <= 5u)
		{
			return true;
		}
		return false;
	}

	private void SafeLog(string message)
	{
		try
		{
			_log?.LogInfo(message);
		}
		catch
		{
		}
	}
}
