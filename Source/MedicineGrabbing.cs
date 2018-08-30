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
using TD.Utilities;

namespace SmartMedicine
{
	[HarmonyPatch(typeof(WorkGiver_Tend))]
	[HarmonyPatch("JobOnThing")]
	public static class JobOnThing_Patch
	{
		public static int medCount = 0;
		//Stupid re-write because I want count.
		//public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
		{
			medCount = 0;
			Pawn patient = t as Pawn;
			Thing thing = null;
			if (Medicine.GetMedicineCountToFullyHeal(patient) > 0)
				thing = HealthAIUtility.FindBestMedicine(pawn, patient);

			Log.Message("JobOnThing count = " + medCount);
			__result = new Job(JobDefOf.TendPatient, patient, thing)
			{ count = medCount };

			return false;
		}
	}

	[HarmonyPatch(typeof(Medicine))]
	[HarmonyPatch("GetMedicineCountToFullyHeal")]
	static class GetMedicineCountToFullyHeal_Patch
	{
		//Insert FilterForUrgentHediffs when counting needed medicine
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo SortByTendPriorityInfo = AccessTools.Method(
				typeof(TendUtility), nameof(TendUtility.SortByTendPriority));
			MethodInfo filterMethodInfo = AccessTools.Method(
				typeof(GetMedicineCountToFullyHeal_Patch), nameof(FilterForUrgentInjuries));
			FieldInfo filterMethodParameter = AccessTools.Field(typeof(Medicine), "tendableHediffsInTendPriorityOrder");

			List<CodeInstruction> instructionList = instructions.ToList();
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

		// beep beep warning static bool hacks
		public static bool __beep_beep_MinimalMedicineAvailable = true;
		//Filter time-sensitive injuries
		public static void FilterForUrgentInjuries(List<Hediff> hediffs)
		{
			if (Settings.Get().noMedicineForNonUrgent)
			{
				hediffs.RemoveAll(h => !h.IsUrgent());
			}
			else if (Settings.Get().minimalMedicineForNonUrgent && __beep_beep_MinimalMedicineAvailable)
			{
				if (hediffs.Any(h => h.IsUrgent()))
					hediffs.RemoveAll(h => !h.IsUrgent());
			}
			__beep_beep_MinimalMedicineAvailable = true;
		}

		public static bool IsUrgent(this Hediff h)
		{
			return !(h is Hediff_Injury)
					|| (h as Hediff_Injury).Bleeding
					|| (h as Hediff_Injury).TryGetComp<HediffComp_Infecter>() != null
					|| (h as Hediff_Injury).TryGetComp<HediffComp_GetsPermanent>() != null;
		}
	}


	[HarmonyPatch(typeof(JobDriver_TendPatient))]
	[HarmonyPatch("Notify_Starting")]
	static class TendPatient_Notify_Starting_Patch
	{
		public static void Prefix(JobDriver_TendPatient __instance)
		{
			Job job = __instance.job;
			Pawn healer = __instance.pawn;
			Pawn patient = job.targetA.Thing as Pawn;
			Thing medicineToDrop = job.targetB.Thing;

			Log.Message(healer + " Starting Tend with  (" + medicineToDrop + ")");

			if (medicineToDrop == null || medicineToDrop.holdingOwner == null) return;

			//Well apparently inventory items can be forbidden
			medicineToDrop.SetForbidden(false, false);

			int needCount = Mathf.Min(medicineToDrop.stackCount, job.count);
			Thing droppedMedicine = null;
			if (medicineToDrop.holdingOwner.Owner is Pawn_InventoryTracker holder)
			{
				Log.Message(holder.pawn + " dropping " + medicineToDrop + "x" + needCount);
				holder.innerContainer.TryDrop(medicineToDrop, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}
			else if (medicineToDrop.holdingOwner.Owner is Pawn_CarryTracker carrier)
			{
				Log.Message(carrier.pawn + " dropping carried " + medicineToDrop + "x" + needCount);
				carrier.innerContainer.TryDrop(medicineToDrop, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}

			if (droppedMedicine != null)
			{
				Log.Message(healer + " now tending with " + droppedMedicine);
				job.targetB = droppedMedicine;
				if (droppedMedicine.IsForbidden(healer))
					Log.Message(droppedMedicine + " is Forbidden, job will restart");
			}

			Log.Message("Okay, doing reservations");
			if (healer.ReserveAsMuchAsPossible(job.targetB.Thing, job, FindBestMedicine.maxPawns, needCount) == 0)
				Verse.Log.Warning("Needed medicine " + job.targetB.Thing + " for " + healer + " seemed to be in a reserved stack. Job will fail but should try again, so ignore the error please.");
		}
	}


	//[HarmonyPatch(typeof(Toils_Tend), "PickupMedicine")]
	//[HarmonyPatch("<PickupMedicine>c__AnonStorey0", "<>m__0")]
	static class PickupMedicine_Patch
	{
		//Insert FilterForUrgentHediffs when counting needed medicine
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo GetMedicineCountToFullyHealInfo = AccessTools.Method(
				typeof(Medicine), nameof(Medicine.GetMedicineCountToFullyHeal));
			MethodInfo GetCarriedThingInfo = AccessTools.Property(
				typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.CarriedThing)).GetGetMethod();
			FieldInfo countInfo = AccessTools.Field(
				typeof(Job), nameof(Job.count));

			bool branchNext = false;
			bool branched = false;
			foreach (CodeInstruction i in instructions)
			{
				if (branchNext)
				{
					yield return new CodeInstruction(OpCodes.Pop);//carriedthing
					yield return new CodeInstruction(OpCodes.Ldc_I4_0);//false
					branchNext = false;
					branched = true;
				}

				if (i.opcode == OpCodes.Call && i.operand == GetMedicineCountToFullyHealInfo)
				{
					yield return new CodeInstruction(OpCodes.Pop);//pawn

					yield return new CodeInstruction(OpCodes.Ldloc_1);//job
					yield return new CodeInstruction(OpCodes.Ldfld, countInfo);//job.count
				}
				else
					yield return i;

				if (!branched && i.opcode == OpCodes.Callvirt && i.operand == GetCarriedThingInfo)
				{
					branchNext = true;
				}
			}
		}
	}


	//[HarmonyPatch(typeof(JobDriver_TendPatient), "MakeNewToils")]
	static class MakeNewToils_Patch
	{
		//Insert FilterForUrgentHediffs when counting needed medicine
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			LocalBuilder localJobInfo = generator.DeclareLocal(typeof(Job));
			FieldInfo medCountInfo = AccessTools.Field(typeof(JobOnThing_Patch), "medCount");

			FieldInfo jobFieldInfo = AccessTools.Field(
				typeof(JobDriver), nameof(JobDriver.job));
			FieldInfo jobCountInfo = AccessTools.Field(
				typeof(Job), nameof(Job.count));

			MethodInfo JumpToToilInfo = AccessTools.Method(
				typeof(JobDriver), nameof(JobDriver.JumpToToil));

			foreach (CodeInstruction i in instructions)
			{
				yield return i;
				if (i.opcode == OpCodes.Ldfld && i.operand == jobFieldInfo)
				{
					yield return new CodeInstruction(OpCodes.Stloc, localJobInfo);
					yield return new CodeInstruction(OpCodes.Ldloc, localJobInfo);
				}
				if (i.opcode == OpCodes.Call && i.operand == JumpToToilInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldloc, localJobInfo);
					yield return new CodeInstruction(OpCodes.Ldsfld, medCountInfo);
					yield return new CodeInstruction(OpCodes.Stfld, jobCountInfo);
				}
			}
		}
	}


	[HarmonyPatch(typeof(HealthAIUtility))]
	[HarmonyPatch("FindBestMedicine")]
	[HarmonyBefore(new string[] { "fluffy.rimworld.pharmacist" })]
	[StaticConstructorOnStartup]
	public static class FindBestMedicine
	{
		public const int maxPawns = 10;
		struct MedicineEvaluator : IComparable
		{
			public Thing thing;
			public Pawn pawn;
			public float rating;
			public int distance;

			[System.Diagnostics.Conditional("DEBUG")]
			public void DebugLog(string label = null)
			{
				Log.Message((label ?? "") + thing + "@" + rating + " (dist: " + distance + ")" + (pawn == null ? "" : " by " + pawn));
			}

			public int CompareTo(object o)
			{
				if (o is MedicineEvaluator other)
					return this > other ? 1 : this < other ? -1 : 0;
				return 0;
			}

			public static bool operator >(MedicineEvaluator l, MedicineEvaluator r)
			{
				return l.rating > r.rating
					|| (l.rating == r.rating && l.distance < r.distance)
					|| (l.distance == r.distance && l.pawn != null && r.pawn == null);
			}
			public static bool operator <(MedicineEvaluator l, MedicineEvaluator r)
			{
				return l.rating < r.rating
					|| (l.rating == r.rating && l.distance > r.distance)
					|| (l.distance == r.distance && l.pawn == null && r.pawn != null);
			}
		}
		static float maxMedicineQuality = 10.0f;
		static float minMedicineQuality = 00.0f;

		static FindBestMedicine()
		{
			IEnumerable<float> medQualities = DefDatabase<ThingDef>.AllDefs
					.Where(td => td.IsWithinCategory(ThingCategoryDefOf.Medicine))
					.Select(m => m.GetStatValueAbstract(StatDefOf.MedicalPotency));
			maxMedicineQuality = medQualities.Max();
			minMedicineQuality = medQualities.Min();
		}

		//FindBestMedicine Replacement
		private static bool Prefix(Pawn healer, Pawn patient, ref Thing __result)
		{
			if (patient.playerSettings == null || patient.playerSettings.medCare <= MedicalCareCategory.NoMeds || Medicine.GetMedicineCountToFullyHeal(patient) <= 0)
				return true;

			Log.Message(healer + " is tending to " + patient);

			float sufficientQuality = maxMedicineQuality + 1; // nothing is sufficient!
			if (Settings.Get().minimalMedicineForNonUrgent)
			{
				if (patient.health.hediffSet.hediffs.All(h => !h.TendableNow() || !h.IsUrgent()))
				{
					sufficientQuality = minMedicineQuality;
					Log.Message("Sufficient medicine for non-urgent care is " + sufficientQuality);
				}
			}

			//Med
			Predicate<Thing> validatorMed = t => patient.playerSettings.medCare.AllowsMedicine(t.def);
			try
			{
				((Action)(() =>
				{
					MedicalCareCategory pharmacistAdvice = Pharmacist.PharmacistUtility.TendAdvice(patient);
					validatorMed = t => pharmacistAdvice.AllowsMedicine(t.def);
				}))();
			}
			catch (Exception) { }

			//Ground
			Map map = patient.Map;
			TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
			Predicate<Thing> validator = (Thing t) => validatorMed(t)
				&& map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams)
				&& !t.IsForbidden(healer) && healer.CanReserve(t, FindBestMedicine.maxPawns, 1);//can reserve at least 1
			Func<Thing, float> priorityGetter = (Thing t) => MedicineRating(t, sufficientQuality);
			List<Thing> groundMedicines = patient.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).FindAll(t => validator(t));

			//Pawns
			Predicate<Pawn> validatorHolder = (Pawn p) =>
			map.reachability.CanReach(patient.Position, p, PathEndMode.ClosestTouch, traverseParams);

			List<Pawn> pawns = healer.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).ListFullCopy();

			if (!Settings.Get().useDoctorMedicine)
				pawns.Remove(healer);
			if (!Settings.Get().usePatientMedicine)
				pawns.Remove(patient);
			if (!Settings.Get().useColonistMedicine)
				pawns.RemoveAll(p => p.IsFreeColonist && p != healer && p != patient);
			if (!Settings.Get().useAnimalMedicine)
				pawns.RemoveAll(p => !p.IsColonist);

			int minDistance = DistanceTo(healer, patient);
			if (!Settings.Get().useOtherEvenIfFar)
				pawns.RemoveAll(p => DistanceTo(p, healer, patient) > minDistance + Settings.Get().distanceToUseFromOther * 2); //*2, there and back

			pawns.RemoveAll(p => !validatorHolder(p));

			//Evaluate them all
			List<MedicineEvaluator> allMeds = new List<MedicineEvaluator>();

			//Add each ground
			foreach (Thing t in groundMedicines)
				allMeds.Add(new MedicineEvaluator()
				{
					thing = t,
					pawn = null,
					rating = MedicineRating(t, sufficientQuality),
					distance = DistanceTo(t, healer, patient)
				});

			List<MedicineEvaluator> groundEvaluators = allMeds.ListFullCopy();

			//Add best from each pawn
			foreach (Pawn p in pawns)
			{
				Thing t = FindBestMedicineInInventory(p, patient, validatorMed, sufficientQuality, p == healer);
				if (t == null) continue;
				allMeds.Add(new MedicineEvaluator()
				{
					thing = t,
					pawn = p,
					rating = MedicineRating(t, sufficientQuality),
					distance = DistanceTo(p, healer, patient)
				});
			}

			//Find best
			allMeds.Sort();


			foreach (MedicineEvaluator tryMed in allMeds)
			{
				tryMed.DebugLog();
			}

			MedicineEvaluator bestMed = allMeds.LastOrDefault();

			if (bestMed.thing != null)
			{
				allMeds.RemoveLast();
				if (Settings.Get().useCloseMedicine && bestMed.pawn != null)
				{
					bestMed.DebugLog("Best: ");
					Log.Message("checking nearby:");
					List<MedicineEvaluator> equalMedicines = groundEvaluators.FindAll(eval => eval.rating == bestMed.rating);
					if (equalMedicines.Count > 0)
					{
						MedicineEvaluator closeMed = equalMedicines.MinBy(eval => eval.distance);
						closeMed.DebugLog("Nearby med on the way there: ");
						if (closeMed.distance <= minDistance + Settings.Get().distanceToUseEqualOnGround * 2) //*2, there and back
							bestMed = closeMed;
					}
				}

				//Check if any minimal medicine exists
				if (allMeds.Count == 0 || bestMed.rating == allMeds.MinBy(m => m.rating).rating)
				{
					GetMedicineCountToFullyHeal_Patch.__beep_beep_MinimalMedicineAvailable = false;
					Log.Message("No minimal medicine available");
				}
				int count = Medicine.GetMedicineCountToFullyHeal(patient);

				JobOnThing_Patch.medCount = count;
				Log.Message("FindBestMedicine count = " + count);

				if (bestMed.pawn == null)
				{
					bestMed.DebugLog("Best Med on ground:");
					__result = bestMed.thing;
				}
				else
				{
					// because The Toil to get this medicine is FailOnDespawnedNullOrForbidden
					// And Medicine in inventory is despawned
					// You can't set the job to use already carried medicine.
					// Editing the toil would be more difficult.
					// But we can drop it so the normal job picks it back it  ¯\_(ツ)_/¯ 
					bestMed.DebugLog("Best Med on hand: ");

					//Drop it!
					int dropCount = Mathf.Min(bestMed.thing.stackCount, count);
					count -= dropCount;
					__result = bestMed.thing;
					//	return true;

					//Todo: mid-job getting new medicine fails since this isn't dropped mid-toil. Sokay since it just fails and restarts.
					//if healer job is tend maybe?

					//Find some more needed nearby
					//bestMed is dropped in Notify_Start, but these aren't tracked there so they are dropped now.

					//In a very odd case that you right-click assign a tend and a second pawn's medicine is needed, he might drop his entire inventory
					// That's a vanilla thing though that calls JobOnThing over and over instead of HasJobOnThing
					if (count > 0)
					{
						List<MedicineEvaluator> equalMedicines = allMeds.FindAll(eval => eval.rating == bestMed.rating);
						equalMedicines.SortBy(eval => DistanceTo(bestMed.pawn, eval.pawn ?? eval.thing));
						Thing droppedMedicine = null;
						Log.Message("But needs " + count + " more");
						while (count > 0 && equalMedicines.Count > 0)
						{
							MedicineEvaluator closeMed = equalMedicines.First();
							equalMedicines.RemoveAt(0);

							closeMed.DebugLog("More: ");

							if (DistanceTo(droppedMedicine ?? bestMed.pawn, closeMed.pawn ?? closeMed.thing) > 8f) //8f as defined in CheckForGetOpportunityDuplicate
								return false;

							dropCount = Mathf.Min(closeMed.thing.stackCount, count);
							closeMed.DebugLog("Using: (" + dropCount + ")");

							closeMed.pawn?.inventory.innerContainer.TryDrop(closeMed.thing, ThingPlaceMode.Near, dropCount, out droppedMedicine);
							if (droppedMedicine != null && !droppedMedicine.IsForbidden(healer) && healer.CanReserve(droppedMedicine, maxPawns, count))
								count -= dropCount;
							//else return false;
						}
					}
				}

				//Use it!
				//Log.Message("using inventory " + medicine);
				//healer.carryTracker.innerContainer.TryAddOrTransfer(medicine, count);
				//__result = medicine;
			}
			return __result == null;
		}

		private static Thing FindBestMedicineInInventory(Pawn pawn, Pawn patient, Predicate<Thing> validatorMed, float sufficientQuality, bool isHealer)
		{
			if (pawn == null || pawn.inventory == null || patient == null || patient.playerSettings == null)
				return null;

			List<Thing> items = new List<Thing>(pawn.inventory.innerContainer.InnerListForReading);
			if (isHealer && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				items.Add(pawn.carryTracker.CarriedThing);
			return items.Where(t => t.def.IsMedicine && validatorMed(t))
			.MaxByWithFallback(t => MedicineRating(t, sufficientQuality));
		}

		private static float MedicineRating(Thing t, float sufficientQuality)
		{
			float medQuality = t.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
			if (medQuality >= sufficientQuality)
				medQuality = (maxMedicineQuality - medQuality) + sufficientQuality;
			//Flips the desireability to be AT LEAST the sufficient
			return medQuality;
		}

		private static int DistanceTo(Thing t1, Thing t2)
		{
			return (t1.Position - t2.Position).LengthManhattan;
		}

		private static int DistanceTo(Thing t, Thing t1, Thing t2)
		{
			return DistanceTo(t, t1) + DistanceTo(t, t2);
		}
	}
}
