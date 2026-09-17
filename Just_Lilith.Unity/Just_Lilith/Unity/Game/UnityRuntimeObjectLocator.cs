using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Just_Lilith.Unity.Game;

internal static class UnityRuntimeObjectLocator
{
	private static readonly MethodInfo? GenericFindIncludingInactive = typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(delegate(MethodInfo method)
	{
		if (method.Name != "FindObjectOfType" || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
		{
			return false;
		}
		ParameterInfo[] parameters = method.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
	});

	private static readonly MethodInfo? GenericFindActiveOnly = typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo method) => method.Name == "FindObjectOfType" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0);

	private static readonly MethodInfo? GenericFindAllIncludingInactive = typeof(UnityEngine.Object).GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(delegate(MethodInfo method)
	{
		if (method.Name != "FindObjectsOfType" || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
		{
			return false;
		}
		ParameterInfo[] parameters = method.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
	});

	public static bool TryFind(string assemblyQualifiedTypeName, out LocatedUnityObject located)
	{
		if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
		{
			throw new ArgumentException("A generated runtime type name is required.", "assemblyQualifiedTypeName");
		}
		string fullName = assemblyQualifiedTypeName.Split(',')[0].Trim();
		Type type = ResolveGeneratedType(assemblyQualifiedTypeName, fullName);
		if ((object)type != null)
		{
			if (TryInvokeFind(GenericFindIncludingInactive, type, new object[1] { true }, out object candidate) && IsAlive(candidate))
			{
				located = new LocatedUnityObject(candidate, type, "typed_generic_including_inactive");
				return true;
			}
			if (TryInvokeFind(GenericFindActiveOnly, type, Array.Empty<object>(), out candidate) && IsAlive(candidate))
			{
				located = new LocatedUnityObject(candidate, type, "typed_generic_active_only");
				return true;
			}
		}
		located = default(LocatedUnityObject);
		return false;
	}

	public static IReadOnlyList<LocatedUnityObject> FindAll(string assemblyQualifiedTypeName)
	{
		if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
		{
			throw new ArgumentException("A generated runtime type name is required.", "assemblyQualifiedTypeName");
		}
		string fullName = assemblyQualifiedTypeName.Split(',')[0].Trim();
		Type type = ResolveGeneratedType(assemblyQualifiedTypeName, fullName);
		if ((object)type == null || !TryInvokeFind(GenericFindAllIncludingInactive, type, new object[1] { true }, out object candidate) || !(candidate is IEnumerable enumerable))
		{
			return Array.Empty<LocatedUnityObject>();
		}
		List<LocatedUnityObject> list = new List<LocatedUnityObject>();
		try
		{
			foreach (object item in enumerable)
			{
				if (IsAlive(item))
				{
					list.Add(new LocatedUnityObject(item, type, "typed_generic_all_including_inactive"));
				}
			}
			return list;
		}
		catch (Exception)
		{
			return Array.Empty<LocatedUnityObject>();
		}
	}

	private static Type? ResolveGeneratedType(string assemblyQualifiedTypeName, string fullName)
	{
		Type type = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
		if ((object)type != null)
		{
			return type;
		}
		try
		{
			int num = assemblyQualifiedTypeName.IndexOf(',');
			if (num < 0)
			{
				return null;
			}
			int num2 = num + 1;
			string assemblyName = assemblyQualifiedTypeName.Substring(num2, assemblyQualifiedTypeName.Length - num2).Trim().Split(',')[0].Trim();
			return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault((Assembly assembly) => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))?.GetType(fullName, throwOnError: false, ignoreCase: false);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool TryInvokeFind(MethodInfo? definition, Type targetType, object[] arguments, out object? candidate)
	{
		candidate = null;
		if ((object)definition == null)
		{
			return false;
		}
		try
		{
			candidate = definition.MakeGenericMethod(targetType).Invoke(null, arguments);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static bool IsAlive(object? candidate)
	{
		if (!(candidate is UnityEngine.Object obj))
		{
			return candidate != null;
		}
		return obj != null;
	}
}
