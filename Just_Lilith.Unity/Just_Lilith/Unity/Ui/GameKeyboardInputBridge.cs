using System;
using System.Reflection;

namespace Just_Lilith.Unity.Ui;

internal sealed class GameKeyboardInputBridge
{
	private const int VirtualKeyF7 = 118;

	private MethodInfo? _begin;

	private MethodInfo? _end;

	private PropertyInfo? _active;

	private PropertyInfo? _instance;

	private MethodInfo? _grabForeground;

	private MethodInfo? _getAsyncKeyState;

	private bool _resolved;

	private bool _owned;

	private bool _f7WasDown;

	private bool _f7StateInitialized;

	public bool IsF7Down { get; private set; }

	public bool ConsumeF7Pressed()
	{
		try
		{
			if (_getAsyncKeyState == null)
			{
				_getAsyncKeyState = Type.GetType("WindowsNativeAPI, Assembly-CSharp", throwOnError: false)?.GetMethod("GetAsyncKeyState", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(int) }, null);
				if (_getAsyncKeyState == null)
				{
					return false;
				}
			}
			int num = Convert.ToInt32(_getAsyncKeyState.Invoke(null, new object[1] { 118 }));
			bool flag = (num & 0x8000) != 0;
			bool flag2 = (num & 1) != 0;
			IsF7Down = flag;
			if (!_f7StateInitialized)
			{
				_f7StateInitialized = true;
				_f7WasDown = flag;
				return false;
			}
			bool result = (flag && !_f7WasDown) || (!flag & flag2);
			_f7WasDown = flag;
			return result;
		}
		catch
		{
			_getAsyncKeyState = null;
			return false;
		}
	}

	public bool SetNeeded(bool needed)
	{
		try
		{
			if (!needed)
			{
				if (_owned)
				{
					_end?.Invoke(null, null);
				}
				_owned = false;
				return true;
			}
			if (!_resolved)
			{
				Type type = Type.GetType("TransparentWindowNew, Assembly-CSharp", throwOnError: false);
				if ((object)type == null)
				{
					return false;
				}
				_begin = type.GetMethod("BeginKeyboardInput", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
				_end = type.GetMethod("EndKeyboardInput", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
				_active = type.GetProperty("IsKeyboardInputActive", BindingFlags.Static | BindingFlags.Public);
				_instance = type.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
				_grabForeground = type.GetMethod("GrabForegroundForOverlay", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
				_resolved = true;
			}
			if ((object)_begin == null || (object)_end == null || (object)_active == null)
			{
				return false;
			}
			object value;
			if (_owned)
			{
				value = _active.GetValue(null);
				if (value is bool && (bool)value)
				{
					return true;
				}
				_begin.Invoke(null, null);
				TryGrabForeground();
				return true;
			}
			value = _active.GetValue(null);
			if (value is bool && (bool)value)
			{
				return true;
			}
			_begin.Invoke(null, null);
			_owned = true;
			TryGrabForeground();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void TryGrabForeground()
	{
		try
		{
			object obj = _instance?.GetValue(null);
			if (obj != null)
			{
				_grabForeground?.Invoke(obj, null);
			}
		}
		catch
		{
		}
	}
}
