using System;
using System.Reflection;

namespace Just_Lilith.Unity.Game;

internal sealed class RuntimeValueAccessor
{
	private readonly PropertyInfo? _property;

	private readonly FieldInfo? _field;

	private RuntimeValueAccessor(PropertyInfo property)
	{
		_property = property;
	}

	private RuntimeValueAccessor(FieldInfo field)
	{
		_field = field;
	}

	public object? GetValue(object instance)
	{
		ArgumentNullException.ThrowIfNull(instance, "instance");
		if ((object)_property == null)
		{
			return _field.GetValue(instance);
		}
		return _property.GetValue(instance);
	}

	public static RuntimeValueAccessor? Find(Type reflectedType, string name)
	{
		ArgumentNullException.ThrowIfNull(reflectedType, "reflectedType");
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("A member name is required.", "name");
		}
		PropertyInfo property = reflectedType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if ((object)property?.GetMethod != null)
		{
			return new RuntimeValueAccessor(property);
		}
		FieldInfo field = reflectedType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if ((object)field != null)
		{
			return new RuntimeValueAccessor(field);
		}
		return null;
	}
}
