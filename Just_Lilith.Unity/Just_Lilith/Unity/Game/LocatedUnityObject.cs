using System;

namespace Just_Lilith.Unity.Game;

internal readonly struct LocatedUnityObject(object instance, Type reflectedType, string strategy)
{
	public object Instance { get; } = instance;

	public Type ReflectedType { get; } = reflectedType;

	public string Strategy { get; } = strategy;
}
