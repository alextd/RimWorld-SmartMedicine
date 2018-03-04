using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Harmony;

namespace SmartMedicine
{
	[HarmonyPatch(typeof(WorkGiver_Tend))]
	[HarmonyPatch("GoodLayingStatusForTend")]
	static class GoodLayingStatusForTend_Patch
	{
		public static void Postfix(Pawn patient, ref bool __result)
		{
			if (patient.RaceProps.Humanlike && 
				((Settings.Get().fieldTendingForLackOfBed && RestUtility.FindPatientBedFor(patient) == null) ||
				Settings.Get().fieldTendingAlways))
				__result = true;
		}
	}
}
