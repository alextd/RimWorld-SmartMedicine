using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Harmony;

namespace SmartMedicine
{
	public class Building_TempTendSpot : Building_Bed
	{
		public override string GetInspectString()
		{
			return DescriptionDetailed.TrimEndNewlines();
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (Gizmo g in base.GetGizmos())
				yield return g;

			yield return new Command_Action()
			{
				defaultLabel = "DesignatorCancel".Translate(),
				defaultDesc = "TD.RemoveTempTendDesc".Translate(),
				icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true),
				hotKey = KeyBindingDefOf.Designator_Cancel,
				action = () => this.Destroy()
			};
		}
	}

	[HarmonyPatch(typeof(WorkGiver_Tend))]
	[HarmonyPatch("GoodLayingStatusForTend")]
	static class GoodLayingStatusForTend_Patch
	{
		//public static bool GoodLayingStatusForTend(Pawn patient, Pawn doctor)
		public static void Postfix(Pawn patient, ref bool __result)
		{
			if (!__result) Log.Message($"StatusFor {patient} is {__result}");
			if (!__result && Settings.Get().FieldTendingActive(patient))
				__result = (patient.GetPosture() != PawnPosture.Standing)
					|| (patient.Drafted && patient.jobs.curDriver is JobDriver_Wait	//Tend while idle + drafted
					&& !patient.stances.FullBodyBusy && !patient.stances.Staggered);
		}
	}

	[HarmonyPatch(typeof(WorkGiver_Tend), "HasJobOnThing")]
	public static class NeedTendBeforeStatusForTend
	{
		//public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo GoodLayingStatusForTendInfo = AccessTools.Method(typeof(WorkGiver_Tend), "GoodLayingStatusForTend");

			List<CodeInstruction> instList = instructions.ToList();
			for (int i = 0; i < instList.Count(); i++)
			{
				//Swap:
				//IL_004d: ldloc.0      // pawn1
				//IL_004e: ldarg.1      // pawn
				//IL_004f: call         bool RimWorld.WorkGiver_Tend::GoodLayingStatusForTend(class Verse.Pawn, class Verse.Pawn)
				//IL_0054: brfalse IL_007f

				//With:
				//IL_0059: ldloc.0      // pawn1
				//IL_005a: call bool RimWorld.HealthAIUtility::ShouldBeTendedNowByPlayer(class Verse.Pawn)
				//IL_005f: brfalse IL_007f
				if (instList[i].opcode == OpCodes.Ldloc_0 &&
					instList[i + 1].opcode == OpCodes.Ldarg_1 &&
					instList[i + 2].opcode == OpCodes.Call && instList[i+2].operand == GoodLayingStatusForTendInfo)
				{
					List<Label> secondLabels = instList[i + 4].labels;
					instList[i + 4].labels = instList[i].labels;
					instList[i].labels = secondLabels;

					yield return instList[i + 4];
					yield return instList[i + 5];
					yield return instList[i + 6];

					yield return instList[i + 0];
					yield return instList[i + 1];
					yield return instList[i + 2];
					yield return instList[i + 3];
					i += 6;
				}
				else
					yield return instList[i];
			}
		}
	}

	[DefOf]
	[HarmonyPatch(typeof(JobGiver_PatientGoToBed))]
	[HarmonyPatch("TryIssueJobPackage")]
	public static class UseTempSleepSpot
	{
		public static ThingDef TempSleepSpot;

		public static ThinkResult LayDownInPlace(Pawn pawn, JobGiver_PatientGoToBed giver)
		{
			if (Settings.Get().FieldTendingActive(pawn))
			{
				Building_Bed tempTendSpot = pawn.Position.GetThingList(pawn.Map).FirstOrDefault(t => t.def == TempSleepSpot) as Building_Bed;
				if (tempTendSpot == null &&
					!GenSpawn.WouldWipeAnythingWith(pawn.Position, Rot4.North, TempSleepSpot, pawn.Map, t => true))
				{
					tempTendSpot = ThingMaker.MakeThing(TempSleepSpot) as Building_Bed;

					GenSpawn.Spawn(tempTendSpot, pawn.Position, pawn.Map, WipeMode.FullRefund);
					tempTendSpot.Medical = true;

					Log.Message($"Creating bed {tempTendSpot} for {pawn} at {pawn.Position}");
				}

				return new ThinkResult(new Job(JobDefOf.LayDown, tempTendSpot), giver);
			}
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
				typeof(UseTempSleepSpot), nameof(LayDownInPlace));

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

	[HarmonyPatch(typeof(JobDriver_LayDown), "MakeNewToils")]
	public static class CleanUpTempBeds
	{
		public static void Prefix(JobDriver_LayDown __instance)
		{
			__instance.AddFinishAction(delegate ()
			{
				if (__instance.Bed?.def == UseTempSleepSpot.TempSleepSpot)
					if(!__instance.Bed?.Destroyed ?? false)
						__instance.Bed.Destroy();
			});
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
