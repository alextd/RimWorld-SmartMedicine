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

	static class Log
	{
		[System.Diagnostics.Conditional("DEBUG")]
		public static void Message(string x)
		{
			Verse.Log.Message(x);
		}
	}
	
	[HarmonyPatch(typeof(Medicine))]
	[HarmonyPatch("GetMedicineCountToFullyHeal")]
	static class GetMedicineCountToFullyHeal
	{
		//Insert FilterForUrgentHediffs when counting needed medicine
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo SortByTendPriorityInfo = AccessTools.Method(
				typeof(TendUtility), nameof(TendUtility.SortByTendPriority));
			MethodInfo filterMethodInfo = AccessTools.Method(
				typeof(GetMedicineCountToFullyHeal), nameof(FilterForUrgentInjuries));
			FieldInfo filterMethodParameter = AccessTools.Field(typeof(Medicine), "tendableHediffsInTendPriorityOrder");

			List <CodeInstruction> instructionList = instructions.ToList();
			for (int i = 0; i < instructionList.Count; i++)
			{
				CodeInstruction instruction = instructionList[i];
				if (instruction.opcode == OpCodes.Ldsfld)
				{
					i++;
					CodeInstruction nextInstruction = instructionList[i];
					if (nextInstruction.opcode == OpCodes.Call && nextInstruction.operand == SortByTendPriorityInfo)
					{
						//insert before the sort call
						yield return new CodeInstruction(OpCodes.Ldsfld, filterMethodParameter);
						yield return new CodeInstruction(OpCodes.Call, filterMethodInfo);
					}
					yield return instruction;
					yield return nextInstruction;
				}
				else
					yield return instruction;
			}
		}

		//Filter time-sensitive injuries
		public static void FilterForUrgentInjuries(List<Hediff> hediffs)
		{
			if (!Settings.Get().noMedicineForNonUrgent)
				return;

			hediffs.RemoveAll(h => !(!(h is Hediff_Injury)
				|| (h as Hediff_Injury).Bleeding
				|| (h as Hediff_Injury).TryGetComp<HediffComp_Infecter>() != null
				|| (h as Hediff_Injury).TryGetComp<HediffComp_GetsOld>() != null));
		}

		//Filter for medicine unneeded 


	}

	[HarmonyPatch(typeof(HealthAIUtility))]
	[HarmonyPatch("FindBestMedicine")]
	static class FindBestMedicine
	{
		//private class MedicineEvaluator
		//{
		//	Thing medicine;
		//	Thing holder;
		//	float quality;
		//	int distance;
		//}
		private static void LogMedicine(string label, Thing t, int distance = -1)
		{
			LogMedicine(label, t, null, distance);
		}

		private static void LogMedicine(string label, Thing t, Pawn p, int distance = -1)
		{
			if (t == null)
				return;
			Log.Message(label + ": " + t + "@" + MedicineQuality(t) + (distance > -1 ? " (dist: " + distance + ")":"") + (p == null?"":" by "+ p));
		}
		private static void Postfix(Pawn healer, Pawn patient, ref Thing __result)
		{
			//This is what the game would do
			Thing chosenMedicine = __result;
			float chosenQuality = chosenMedicine != null ? MedicineQuality(chosenMedicine) : 0.0f;
			int chosenDistance = chosenMedicine != null ? DistanceTo(patient, chosenMedicine) :  int.MaxValue;
			LogMedicine("Chosen", chosenMedicine, chosenDistance);


			if (patient.playerSettings == null || patient.playerSettings.medCare <= MedicalCareCategory.NoMeds
				|| !healer.Faction.IsPlayer)
				return;

			//Try to find better, or closer.
			Thing medicine = null;
			Pawn medicineHolder = null;
			if (Settings.Get().useDoctorMedicine)
			{
				medicine = FindBestMedicineInInventory(healer, patient);
				if(medicine!= null) medicineHolder = healer;
			}
			if (Settings.Get().usePatientMedicine && medicine == null)
			{
				medicine = FindBestMedicineInInventory(patient, patient);
				if (medicine != null) medicineHolder = patient;
			}

			TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
			if (Settings.Get().useColonistMedicine || Settings.Get().useAnimalMedicine)
			{
				List<Pawn> holders = healer.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).
					Where(p => (Settings.Get().useColonistMedicine && p.IsFreeColonist)
					|| (Settings.Get().useAnimalMedicine && !p.IsColonist)).ToList();

				foreach (Pawn p in holders)
				{
					Log.Message(p + "?");
				}
				holders.RemoveAll(p => p == healer || p == patient || !healer.Map.reachability.CanReach(healer.Position, p, PathEndMode.ClosestTouch, traverseParams));

				if (!Settings.Get().useOtherEvenIfFar)
					holders.RemoveAll(p => DistanceToEither(p, healer, patient) > Settings.Get().distanceToUseEqualOnGround);

				foreach (Pawn p in holders)
				{
					Log.Message(p + " available");
				}

				float bestQuality = medicine != null ? MedicineQuality(medicine) : 0;
				int bestDistance = medicineHolder != null ? 0 : int.MaxValue;  //medicine on doctor/patient is either 0 from doctor or patient

				foreach (Pawn p in holders)
				{
					Thing pMedicine = FindBestMedicineInInventory(p, patient);
					if (pMedicine == null) continue;

					float pQuality = MedicineQuality(pMedicine);
					int pDistance = DistanceToEither(p, healer, patient);
					LogMedicine("Inventory", pMedicine, p, pDistance);

					if (pQuality > bestQuality || (pQuality == bestQuality && pDistance < bestDistance))
					{
						medicine = pMedicine;
						medicineHolder = p;
						bestQuality = pQuality;
						bestDistance = pDistance;
						//TODO: Have pawn walk to hand off medicine, lol never gonna happen
					}
				}
			}

			if (medicine != null)
			{
				float medQuality = MedicineQuality(medicine);
				LogMedicine("Picked", medicine, medicineHolder, DistanceToEither(medicineHolder, healer, patient));

				Log.Message("checking if chosen medicine is better");
				if (chosenQuality > medQuality)
					return;

				Log.Message("checking if chosen medicine is equal");
				if (chosenQuality == medQuality)
				{ 
					//Okay, find closest
					if (medicineHolder != healer && medicineHolder != patient)
					{
						Log.Message("checking if holder is much closer than chosen");
						if (chosenDistance - Settings.Get().distanceToUseEqualOnGround *2 <= DistanceToEither(medicineHolder, healer, patient))
							return;
					}
			
					List<Thing> groundMedicines = healer.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Where(t =>
					healer.Map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams) &&
					!t.IsForbidden(healer) && patient.playerSettings.medCare.AllowsMedicine(t.def) && healer.CanReserve(t, 1, -1, null, false)).ToList();

					foreach (Thing t in groundMedicines)
					{
						LogMedicine("Ground", t, DistanceToEither(t, healer, patient));
					}
					Log.Message("checking close to healer (and too far from patient)");
					Thing closeMedicine = CloseMedOnGround(groundMedicines, medQuality, healer);
					if (Settings.Get().useCloseMedicine && closeMedicine != null && DistanceTo(healer, closeMedicine) <= Settings.Get().distanceToUseEqualOnGround)
					{
						__result = closeMedicine;
						return;
					}

					Log.Message("checking close to patient");
					if (Settings.Get().useCloseMedicine && chosenDistance <= Settings.Get().distanceToUseEqualOnGround)
						return;
				}

				LogMedicine("Using", medicine, medicineHolder);

				// because The Toil to get this medicine is FailOnDespawnedNullOrForbidden
				// And Medicine in inventory or carried is despawned
				// You can't set the job to use already carried medicine.
				// Editing the toil would be more difficult.
				// But we can drop it so the normal job picks it back it  ¯\_(ツ)_/¯ 

				//Drop it!
				int count = Medicine.GetMedicineCountToFullyHeal(patient);
				if (healer.carryTracker.CarriedThing != null)
					count -= healer.carryTracker.CarriedThing.stackCount;
				count = Mathf.Min(medicine.stackCount, count);
				Thing droppedMedicine;
				medicineHolder.inventory.innerContainer.TryDrop(medicine, ThingPlaceMode.Direct, count, out droppedMedicine);

				//Whoops dropped onto forbidden / reserved stack
				if(!droppedMedicine.IsForbidden(healer) && healer.CanReserve(droppedMedicine, 1, -1, null, false))
				   __result = droppedMedicine;

				//Use it!
				//Log.Message("using inventory " + medicine);
				//healer.carryTracker.innerContainer.TryAddOrTransfer(medicine, count);
				//__result = medicine;
			}
		}

		private static Thing FindBestMedicineInInventory(Pawn pawn, Pawn patient)
		{
			if (pawn == null || pawn.inventory == null || patient == null || patient.playerSettings == null)
				return null;

			return pawn.inventory.innerContainer.InnerListForReading
				.Where(t => t.def.IsMedicine && patient.playerSettings.medCare.AllowsMedicine(t.def))
				.MaxByWithFallback(t => MedicineQuality(t));
		}

		private static float MedicineQuality(Thing t)
		{
			return t.def.GetStatValueAbstract(StatDefOf.MedicalPotency, null);
		}

		private static Thing CloseMedOnGround(List<Thing> groundMedicines, float medQuality, Pawn pawn)
		{
			List<Thing> equalMedicines = groundMedicines.Where(t => MedicineQuality(t) == medQuality).ToList();
			if (equalMedicines.Count == 0)
				return null;

			return equalMedicines.MinBy(t => DistanceTo(pawn, t));
		}

		private static int DistanceTo(Thing t1, Thing t2)
		{
			return (t1.Position - t2.Position).LengthManhattan;
		}

		private static int DistanceToEither(Thing t, Thing t1, Thing t2)
		{
			return Math.Min(DistanceTo(t, t1), DistanceTo(t, t2));
		}
		
	}
}