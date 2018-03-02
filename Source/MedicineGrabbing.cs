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

	[HarmonyPatch(typeof(HealthAIUtility))]
	[HarmonyPatch("FindBestMedicine")]
	[StaticConstructorOnStartup]
	static class FindBestMedicine
	{
		struct MedicineEvaluator
		{
			public Thing thing;
			public Pawn pawn;
			public float rating;
			public int distance;

			[System.Diagnostics.Conditional("DEBUG")]
			public void DebugLog()
			{
				Log.Message(thing + "@" + rating + " (dist: " + distance + ")" + (pawn == null ? "" : " by " + pawn));
			}
			public static bool operator>(MedicineEvaluator l, MedicineEvaluator r)
			{
				return l.rating > r.rating
					|| (l.rating == r.rating && l.distance < r.distance);
			}
			public static bool operator <(MedicineEvaluator l, MedicineEvaluator r)
			{
				return l.rating < r.rating
					|| (l.rating == r.rating && l.distance > r.distance);
			}
		}
		static float maxMedicineQuality = 10.0f;
		static float minMedicineQuality = 00.0f;

		static FindBestMedicine()
		{
			IEnumerable<float> medQualities = DefDatabase<ThingDef>.AllDefs
					.Where(td => td.IsWithinCategory(ThingCategoryDefOf.Medicine))
					.Select(m => m.GetStatValueAbstract(StatDefOf.MedicalPotency, null));
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

			//Ground
			Map map = patient.Map;
			TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
			Predicate<Thing> validator = (Thing t) =>
			map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams)
			&& !t.IsForbidden(healer) && patient.playerSettings.medCare.AllowsMedicine(t.def) && healer.CanReserve(t, 1, -1, null, false);
			Func<Thing, float> priorityGetter = (Thing t) => MedicineRating(t, sufficientQuality);
			List<Thing> groundMedicines = patient.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Where(t => validator(t)).ToList();

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
				Thing t = FindBestMedicineInInventory(p, patient, sufficientQuality);
				if (t == null) continue;
				allMeds.Add(new MedicineEvaluator()
				{
					thing = t,
					pawn = p,
					rating = MedicineRating(t, sufficientQuality),
					distance = DistanceTo(p, healer, patient) - 1
					// Nudge the distance to prefer inventory on ties
				});
			}

			//Find best
			MedicineEvaluator bestMed = new MedicineEvaluator()
			{
				thing = null,
				pawn = null,
				rating = 0.0f,
				distance = 0
			};

			foreach (MedicineEvaluator tryMed in allMeds)
			{
				tryMed.DebugLog();
				if (tryMed > bestMed)
					bestMed = tryMed;
			}
			
			if (bestMed.thing != null)
			{
				if (Settings.Get().useCloseMedicine && bestMed.pawn != null)
				{
					Log.Message("checking closeby instead");

					List<MedicineEvaluator> equalMedicines = groundEvaluators.Where(eval => eval.rating == bestMed.rating).ToList();
					if (equalMedicines.Count > 0)
					{
						if (bestMed.pawn != healer && bestMed.pawn != patient)
						{
							MedicineEvaluator closeMed = equalMedicines.MinBy(eval => DistanceTo(eval.thing, bestMed.pawn));
							if (DistanceTo(closeMed.thing, bestMed.pawn) <= Settings.Get().distanceToUseEqualOnGround) 
								bestMed = closeMed;
						}
						else
						{
							MedicineEvaluator closeMed = equalMedicines.MinBy(eval => eval.distance);
							if (closeMed.distance <= Settings.Get().distanceToUseEqualOnGround * 2) //*2, there and back
								bestMed = closeMed;
						}
					}
				}

				// because The Toil to get this medicine is FailOnDespawnedNullOrForbidden
				// And Medicine in inventory or carried is despawned
				// You can't set the job to use already carried medicine.
				// Editing the toil would be more difficult.
				// But we can drop it so the normal job picks it back it  ¯\_(ツ)_/¯ 

				if (bestMed.pawn != null)
				{
					Log.Message("Best Med on hand:");
					bestMed.DebugLog();

					//Drop it!
					int count = Medicine.GetMedicineCountToFullyHeal(patient);
					if (healer.carryTracker.CarriedThing != null)
						count -= healer.carryTracker.CarriedThing.stackCount;
					count = Mathf.Min(bestMed.thing.stackCount, count);
					Thing droppedMedicine;
					bestMed.pawn.inventory.innerContainer.TryDrop(bestMed.thing, ThingPlaceMode.Direct, count, out droppedMedicine);

					//Whoops dropped onto forbidden / reserved stack
					if (!droppedMedicine.IsForbidden(healer) && healer.CanReserve(droppedMedicine, 1, -1, null, false))
						__result = droppedMedicine;
					else
						return true;
				}
				else
				{
					Log.Message("Best Med on ground:");
					bestMed.DebugLog();
					__result = bestMed.thing;
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

		private static Thing FindBestMedicineInInventory(Pawn pawn, Pawn patient, float sufficientQuality)
		{
			if (pawn == null || pawn.inventory == null || patient == null || patient.playerSettings == null)
				return null;

			return pawn.inventory.innerContainer.InnerListForReading
				.Where(t => t.def.IsMedicine && patient.playerSettings.medCare.AllowsMedicine(t.def))
				.MaxByWithFallback(t => MedicineRating(t, sufficientQuality));
		}

		private static float MedicineRating(Thing t, float sufficientQuality)
		{
			float medQuality = t.def.GetStatValueAbstract(StatDefOf.MedicalPotency, null);
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