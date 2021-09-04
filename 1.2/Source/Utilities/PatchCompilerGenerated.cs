﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TD.Utilities
{
	static class PatchCompilerGenerated
	{
		public static void PatchGeneratedMethod(this Harmony harmony, Type masterType, Predicate<MethodInfo> check,
			HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod transpiler = null)
		{
			//Find the compiler-created method nested in masterType that passes the check, Patch it
			List<Type> nestedTypes = new List<Type>(masterType.GetNestedTypes(BindingFlags.NonPublic));
			while (nestedTypes.Any())
			{
				Type type = nestedTypes.Pop();
				nestedTypes.AddRange(type.GetNestedTypes(BindingFlags.NonPublic));

				foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
				{
					if (method.DeclaringType != type) continue;

					if (check(method))
					{
						harmony.Patch(method, prefix, postfix, transpiler);
					}
				}
			}
		}
	}
}
