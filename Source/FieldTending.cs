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
			if (!__result && Settings.Get().FieldTendingActive(patient))
				__result = patient.Drafted || patient.GetPosture() != PawnPosture.Standing;
		}
	}

	[HarmonyPatch(typeof(JobGiver_PatientGoToBed))]
	[HarmonyPatch("TryIssueJobPackage")]
	static class PatientGoToBed_Patch
	{
		public static ThinkResult LayDownInPlace(Pawn pawn, JobGiver_PatientGoToBed giver)
		{
			if (Settings.Get().FieldTendingActive(pawn))
				return new ThinkResult(new Job(JobDefOf.LayDown, pawn.Position), giver);
			else return ThinkResult.NoJob;
		}
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			//After:
			//IL_0049: ldarg.1      // pawn
			//IL_004a: call         class RimWorld.Building_Bed RimWorld.RestUtility::FindPatientBedFor(class Verse.Pawn)
			//IL_004f: stloc.0      // patientBedFor

			//Find:
			//IL_0056: call valuetype Verse.AI.ThinkResult Verse.AI.ThinkResult::get_NoJob()
			//IL_005b: ret
			MethodInfo FindPatientBedForInfo = AccessTools.Method(
				typeof(RestUtility), nameof(RestUtility.FindPatientBedFor));
			MethodInfo get_NoJobInfo = AccessTools.Property(
				typeof(ThinkResult), nameof(ThinkResult.NoJob)).GetGetMethod(false);

			MethodInfo LayDownInPlaceInfo = AccessTools.Method(
				typeof(PatientGoToBed_Patch), nameof(LayDownInPlace));

			bool lookedForBed = false;
			foreach(CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Call && instruction.operand == FindPatientBedForInfo)
					lookedForBed = true;

				if (lookedForBed &&
					instruction.opcode == OpCodes.Call && instruction.operand == get_NoJobInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					instruction.operand = LayDownInPlaceInfo;
				}
				yield return instruction;
			}
		}
	}
}
