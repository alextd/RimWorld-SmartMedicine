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

		//Filter time-sensitive injuries
		public static void FilterForUrgentInjuries(List<Hediff> hediffs)
		{
			if (Settings.Get().noMedicineForNonUrgent)
			{
				hediffs.RemoveAll(h => !h.IsUrgent());
			}
			else if (Settings.Get().minimalMedicineForNonUrgent)
			{
				if (hediffs.Any(h => h.IsUrgent()))
					hediffs.RemoveAll(h => !h.IsUrgent());
			}
		}

		public static bool IsUrgent(this Hediff h)
		{
			return !(h is Hediff_Injury)
					|| (h as Hediff_Injury).Bleeding
					|| (h as Hediff_Injury).TryGetComp<HediffComp_Infecter>() != null
					|| (h as Hediff_Injury).TryGetComp<HediffComp_GetsOld>() != null;
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

			//job.count is not set properly so here we go again:
			int count = Medicine.GetMedicineCountToFullyHeal(patient);
			int needCount = Mathf.Min(medicineToDrop.stackCount, count);
			Thing droppedMedicine = null;
			if (medicineToDrop.holdingOwner.Owner is Pawn_InventoryTracker holder)
			{
				Log.Message(holder.pawn + " dropping " + medicineToDrop);
				holder.innerContainer.TryDrop(medicineToDrop, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}
			else if (medicineToDrop.holdingOwner.Owner is Pawn_CarryTracker carrier)
			{
				Log.Message(carrier.pawn + " dropping " + medicineToDrop);
				carrier.innerContainer.TryDrop(medicineToDrop, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}

			if (droppedMedicine != null)
			{
				Log.Message(healer + " now tending with " + droppedMedicine);
				job.targetB = droppedMedicine;
				if (droppedMedicine.IsForbidden(healer))
					Log.Message(droppedMedicine + " is Forbidden, job will restart");

				Log.Message("Okay, doing reservations");
				if (!healer.Reserve(job.targetB.Thing, job, FindBestMedicine.maxPawns, needCount, null))
					Verse.Log.Warning("Needed medicine " + droppedMedicine + " for " + healer + " was dropped onto a reserved stack. Job will fail and try again, so ignore the error please.");
			}
		}
	}


	[HarmonyPatch(typeof(HealthAIUtility))]
	[HarmonyPatch("FindBestMedicine")]
	[StaticConstructorOnStartup]
	public static class FindBestMedicine
	{
		public const int maxPawns = 100;
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

			public static bool operator>(MedicineEvaluator l, MedicineEvaluator r)
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
		private static bool Prefix(Pawn healer, Pawn patient, ref Thing __result)
		{
			if (patient.playerSettings == null || patient.playerSettings.medCare <= MedicalCareCategory.NoMeds ||
				!healer.Faction.IsPlayer)
				return true;

			Log.Message(healer + " is tending to " + patient);

			float sufficientQuality = maxMedicineQuality + 1; // nothing is sufficient!
			if (Settings.Get().downgradeExcessiveMedicine)
			{
				sufficientQuality = CalculateSufficientQuality(healer, patient);
				Log.Message("Sufficient medicine for best treatment is " + sufficientQuality + "(" +Settings.Get().goodEnoughDowngradeFactor + ")");
			}
			if (Settings.Get().minimalMedicineForNonUrgent)
			{
				if (patient.health.hediffSet.hediffs.All(h => !h.TendableNow || !h.IsUrgent()))
				{
					sufficientQuality = minMedicineQuality;
					Log.Message("Sufficient medicine for non-urgent care is " + sufficientQuality);
				}
			}
			
			int count = Medicine.GetMedicineCountToFullyHeal(patient);

			//Ground
			Map map = patient.Map;
			TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
			Predicate<Thing> validator = (Thing t) =>
			map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams)
			&& !t.IsForbidden(healer) && patient.playerSettings.medCare.AllowsMedicine(t.def) && healer.CanReserve(t, FindBestMedicine.maxPawns, count);
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
				{ thing = t,
					pawn = null,
					rating = MedicineRating(t, sufficientQuality),
					distance = DistanceTo(t, healer, patient)
				});

			List<MedicineEvaluator> groundEvaluators = allMeds.ListFullCopy();

			//Add best from each pawn
			foreach (Pawn p in pawns)
			{
				Thing t = FindBestMedicineInInventory(p, patient, sufficientQuality, p == healer);
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

		private static float CalculateSufficientQuality(Pawn doctor, Pawn patient)
		{
			// (doctorQuality * medQuality + bedOffset) * seldTend is clamped to 1,
			// solve for medQuality,
			// medQuality = (1 / selfTend - bedOffset) / doctorQuality
			// this quality is sufficient.
			float doctorQuality = doctor?.GetStatValue(StatDefOf.MedicalTendQuality, true) ?? 0.75f;
			float bedOffset = patient.CurrentBed()?.GetStatValue(StatDefOf.MedicalTendQualityOffset, true) ?? 0f;
			float selfTend = doctor != patient ? 1.0f : 0.7f;
			return (1 / selfTend - bedOffset) / doctorQuality * Settings.Get().goodEnoughDowngradeFactor;
		}

		private static Thing FindBestMedicineInInventory(Pawn pawn, Pawn patient, float sufficientQuality, bool isHealer)
		{
			if (pawn == null || pawn.inventory == null || patient == null || patient.playerSettings == null)
				return null;

			List<Thing> items = new List<Thing>(pawn.inventory.innerContainer.InnerListForReading);
			if (isHealer && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				items.Add(pawn.carryTracker.CarriedThing);
			return items.Where(t => t.def.IsMedicine && patient.playerSettings.medCare.AllowsMedicine(t.def))
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
			return DistanceTo(t, t1) +  DistanceTo(t, t2);
		}
	}
}