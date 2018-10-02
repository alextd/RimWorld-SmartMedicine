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
				__result = (patient.GetPosture() != PawnPosture.Standing)
					|| (patient.Drafted && patient.jobs.curDriver is JobDriver_Wait	//Tend while idle + drafted
					&& !patient.stances.FullBodyBusy && !patient.stances.Staggered);
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
			foreach (CodeInstruction instruction in instructions)
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


	[HarmonyPatch(typeof(FeedPatientUtility))]
	[HarmonyPatch("ShouldBeFed")]
	static class ShouldBeFed_Patch
	{
		public static bool InBed_Patch(Pawn pawn)
		{
			return pawn.InBed() || Settings.Get().FieldTendingActive(pawn);
		}
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo InBedInfo = AccessTools.Method(
				typeof(RestUtility), nameof(RestUtility.InBed));

			MethodInfo InBed_PatchInfo = AccessTools.Method(
				typeof(ShouldBeFed_Patch), nameof(InBed_Patch));
			
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Call && instruction.operand == InBedInfo)
					instruction.operand = InBed_PatchInfo;
				yield return instruction;
			}
		}
	}
}
