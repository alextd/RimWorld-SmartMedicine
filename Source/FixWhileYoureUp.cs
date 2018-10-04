using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Verse;
using RimWorld;
using Harmony;

namespace SmartMedicine
{
	[StaticConstructorOnStartup]
	static class FixWhileYoureUp
	{
		static FixWhileYoureUp()
		{
			if(AccessTools.TypeByName("WhileYoureUp.Utils") is Type whileYoureUpUtils)
			{
				if(AccessTools.Method(whileYoureUpUtils, "MyDistance") is MethodInfo patchThis)
				{
					HarmonyInstance.Create("uuugggg.rimworld.SmartMedicine.main").Patch(patchThis,
						new HarmonyMethod(typeof(FixWhileYoureUp), nameof(Prefix)));
				}
			}
		}

		//public static int MyDistance(IntVec3 a, LocalTargetInfo b, Map m, TraverseParms t)
		public static void Prefix(ref LocalTargetInfo b)
		{
			if (!b.Thing?.Spawned ?? false)
				b = b.Thing.PositionHeld;
		}
	}
}
