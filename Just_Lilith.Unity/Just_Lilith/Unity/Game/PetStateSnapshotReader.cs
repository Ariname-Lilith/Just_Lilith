using System;
using System.Linq;
using System.Reflection;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Just_Lilith.Core.Contracts;
using Just_Lilith.Core.Llm;

namespace Just_Lilith.Unity.Game;

internal sealed class PetStateSnapshotReader : IPetStateSnapshotSource
{
	private const string StateManagerType = "LilithStateManager, Assembly-CSharp";

	private const string CharacterControllerType = "CharacterController, Assembly-CSharp";

	private const string SleepSystemType = "LilithSleepSystem, Assembly-CSharp";

	private readonly ManualLogSource _log;

	private bool _readyLogged;

	private bool _unavailableLogged;

	public PetStateSnapshotReader(ManualLogSource log)
	{
		_log = log ?? throw new ArgumentNullException("log");
	}

	public PetStateSnapshot CapturePetState()
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		LocatedUnityObject located = default(LocatedUnityObject);
		bool flag = false;
		try
		{
			flag = UnityRuntimeObjectLocator.TryFind("LilithStateManager, Assembly-CSharp", out located);
		}
		catch
		{
			flag = false;
		}
		LocatedUnityObject located2 = default(LocatedUnityObject);
		bool flag2 = false;
		try
		{
			flag2 = UnityRuntimeObjectLocator.TryFind("CharacterController, Assembly-CSharp", out located2);
		}
		catch
		{
			flag2 = false;
		}
		bool isEnabled;
		if (flag)
		{
			if (!_readyLogged)
			{
				_readyLogged = true;
				_unavailableLogged = false;
				ManualLogSource log = _log;
				BepInExInfoLogInterpolatedStringHandler bepInExInfoLogInterpolatedStringHandler = new BepInExInfoLogInterpolatedStringHandler(81, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("module=PetState; state=ready; locator=");
					bepInExInfoLogInterpolatedStringHandler.AppendFormatted(located.Strategy);
					bepInExInfoLogInterpolatedStringHandler.AppendLiteral("; source=LilithStateManager.GetCurrentState");
				}
				log.LogInfo(bepInExInfoLogInterpolatedStringHandler);
			}
		}
		else if (flag2)
		{
			if (!_unavailableLogged)
			{
				_unavailableLogged = true;
				ManualLogSource log2 = _log;
				BepInExWarningLogInterpolatedStringHandler bepInExWarningLogInterpolatedStringHandler = new BepInExWarningLogInterpolatedStringHandler(71, 1, out isEnabled);
				if (isEnabled)
				{
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("module=PetState; state=degraded; locator=");
					bepInExWarningLogInterpolatedStringHandler.AppendFormatted(located2.Strategy);
					bepInExWarningLogInterpolatedStringHandler.AppendLiteral("; fallback=CharacterController");
				}
				log2.LogWarning(bepInExWarningLogInterpolatedStringHandler);
			}
		}
		else if (!_unavailableLogged)
		{
			_unavailableLogged = true;
			_log.LogWarning("module=PetState; state=unavailable; fallback=field_level_unknown");
		}
		return PetStateContextBuilder.CreateSnapshot(new PetStateObservation(utcNow, flag ? ReadCurrentStateTypeName(located) : null, ReadNameWithFallback(flag, located, flag2, located2, "ActionType"), ReadStaticName("LilithSleepSystem, Assembly-CSharp", "GetDrowsyLevel"), null, flag2 ? ReadName(located2, "ControlMode") : null, ReadNameWithFallback(flag, located, flag2, located2, "ClothingState"), ReadNameWithFallback(flag, located, flag2, located2, "ExpressionAccessoryState"), ReadBoolWithFallback(flag, located, flag2, located2, "IsIdle"), ReadBoolWithFallback(flag, located, flag2, located2, "IsSleep"), ReadBoolWithFallback(flag, located, flag2, located2, "IsLieDown"), ReadBoolWithFallback(flag, located, flag2, located2, "IsSit"), ReadBoolWithFallback(flag, located, flag2, located2, "IsSofaSit"), ReadBoolWithFallback(flag, located, flag2, located2, "IsInteracting"), ReadBoolWithFallback(flag, located, flag2, located2, "IsDrag"), ReadBoolWithFallback(flag, located, flag2, located2, "IsGround"), flag ? ReadBoolMethod(located, "IsFalling") : ((bool?)null), ReadBoolWithFallback(flag, located, flag2, located2, "IsWalkLeft"), ReadBoolWithFallback(flag, located, flag2, located2, "IsWalkRight"), ReadBoolWithFallback(flag, located, flag2, located2, "IsStateMachineLocked"), ReadBoolWithFallback(flag, located, flag2, located2, "IsActionAnimationPlaying"), ReadBoolWithFallback(flag, located, flag2, located2, "IsAnyActionAnimPlaying"), ReadBoolWithFallback(flag, located, flag2, located2, "IsYawnAnimPlaying")));
	}

	private static bool? ReadBoolWithFallback(bool hasManager, LocatedUnityObject manager, bool hasCharacter, LocatedUnityObject character, string name)
	{
		return RuntimeValueFallback.First(hasManager ? ReadBool(manager, name) : ((bool?)null), () => (!hasCharacter) ? ((bool?)null) : ReadBool(character, name));
	}

	private static string? ReadNameWithFallback(bool hasManager, LocatedUnityObject manager, bool hasCharacter, LocatedUnityObject character, string name)
	{
		return RuntimeValueFallback.First(hasManager ? ReadName(manager, name) : null, () => (!hasCharacter) ? null : ReadName(character, name));
	}

	private static bool? ReadBool(LocatedUnityObject located, string name)
	{
		try
		{
			return (RuntimeValueAccessor.Find(located.ReflectedType, name)?.GetValue(located.Instance) is bool value) ? new bool?(value) : ((bool?)null);
		}
		catch
		{
			return null;
		}
	}

	private static bool? ReadBoolMethod(LocatedUnityObject located, string name)
	{
		try
		{
			return (located.ReflectedType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(located.Instance, Array.Empty<object>()) is bool value) ? new bool?(value) : ((bool?)null);
		}
		catch
		{
			return null;
		}
	}

	private static string? ReadName(LocatedUnityObject located, string name)
	{
		try
		{
			return (RuntimeValueAccessor.Find(located.ReflectedType, name)?.GetValue(located.Instance))?.ToString();
		}
		catch
		{
			return null;
		}
	}

	private static string? ReadCurrentStateTypeName(LocatedUnityObject located)
	{
		try
		{
			object obj = located.ReflectedType.GetMethod("GetCurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(located.Instance, Array.Empty<object>());
			IntPtr intPtr = ((obj is Il2CppObjectBase il2CppObjectBase) ? il2CppObjectBase.Pointer : IntPtr.Zero);
			string text = null;
			if (intPtr != IntPtr.Zero)
			{
				IntPtr intPtr2 = IL2CPP.il2cpp_object_get_class(intPtr);
				if (intPtr2 != IntPtr.Zero)
				{
					text = IL2CPP.il2cpp_class_get_name_(intPtr2);
				}
				if (PetStateIdentityResolver.IsKnownTypeName(text))
				{
					return text;
				}
				string text2 = PetStateIdentityResolver.ResolveTypeName(intPtr, (string memberName) => ReadNativePointer(located, memberName));
				if (text2 != null)
				{
					return text2;
				}
			}
			string text3 = obj?.GetType().Name;
			if (PetStateIdentityResolver.IsKnownTypeName(text3))
			{
				return text3;
			}
			return text ?? text3;
		}
		catch
		{
			return null;
		}
	}

	private static IntPtr? ReadNativePointer(LocatedUnityObject located, string name)
	{
		try
		{
			object obj = RuntimeValueAccessor.Find(located.ReflectedType, name)?.GetValue(located.Instance);
			IntPtr? result;
			if (!(obj is Il2CppObjectBase il2CppObjectBase))
			{
				if (!(obj is IntPtr intPtr) || !(intPtr != IntPtr.Zero))
				{
					goto IL_0072;
				}
				result = intPtr;
			}
			else
			{
				if (!(il2CppObjectBase.Pointer != IntPtr.Zero))
				{
					goto IL_0072;
				}
				result = il2CppObjectBase.Pointer;
			}
			goto IL_007d;
			IL_007d:
			return result;
			IL_0072:
			result = null;
			goto IL_007d;
		}
		catch
		{
			return null;
		}
	}

	private static string? ReadStaticName(string assemblyQualifiedTypeName, string methodName)
	{
		try
		{
			return (ResolveLoadedType(assemblyQualifiedTypeName)?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null))?.Invoke(null, Array.Empty<object>())?.ToString();
		}
		catch
		{
			return null;
		}
	}

	private static Type? ResolveLoadedType(string assemblyQualifiedTypeName)
	{
		Type type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
		if ((object)type != null)
		{
			return type;
		}
		int num = assemblyQualifiedTypeName.IndexOf(',');
		if (num < 0)
		{
			return null;
		}
		string name = assemblyQualifiedTypeName.Substring(0, num).Trim();
		int num2 = num + 1;
		string assemblyName = assemblyQualifiedTypeName.Substring(num2, assemblyQualifiedTypeName.Length - num2).Trim().Split(',')[0].Trim();
		return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly assembly) => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))?.GetType(name, throwOnError: false, ignoreCase: false);
	}
}
