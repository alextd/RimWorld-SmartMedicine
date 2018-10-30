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
		//Stupid re-write because I want count.
		//public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
		{
			Pawn patient = t as Pawn;
			if (Medicine.GetMedicineCountToFullyHeal(patient) > 0)
			{
				List<ThingCount> meds = FindBestMedicine.Find(pawn, patient, out int medCount);
				Job job = new Job(JobDefOf.TendPatient, patient, meds.FirstOrDefault().Thing);
				job.count = medCount;
				if (meds.Count() > 1)
				{
					job.targetQueueA = meds.Skip(1).Select(med => new LocalTargetInfo(med.Thing)).ToList();
					job.countQueue = meds.Skip(1).Select(med => med.Count).ToList();
				}
				__result = job;
			}
			else
				__result = new Job(JobDefOf.TendPatient, patient);

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
				typeof(GetMedicineCountToFullyHeal_Patch), nameof(FilterInjuriesForMedCount));
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
		public static void FilterInjuriesForMedCount(List<Hediff> hediffs)
		{
			Log.Message($"Filtering ({hediffs.ToStringSafeEnumerable()})");
			if (PriorityCareComp.MaxPriorityCare(hediffs, out MedicalCareCategory maxPriorityCare))
			{
				MedicalCareCategory defaultCare = hediffs.First().pawn.GetCare();

				//ignore defaultCare if none uses default
				if (PriorityCareComp.AllPriorityCare(hediffs))
					defaultCare = maxPriorityCare;
				
				//Find highest care
				MedicalCareCategory highestCare = defaultCare > maxPriorityCare ? defaultCare : maxPriorityCare;
				Log.Message($"maxPriorityCare is {maxPriorityCare}, defaultCare is {defaultCare}, highestCare is {highestCare}");

				//remove anything less than that
				//Should check if medicine is available, but you just set to use it so this will assume you have it
				hediffs.RemoveAll(delegate (Hediff h)
				{
					if (PriorityCareComp.Get().TryGetValue(h, out MedicalCareCategory heCare))
					{
						return heCare < highestCare;
					}
					return defaultCare < highestCare;
				});
			}

			if (Settings.Get().noMedicineForNonUrgent)
			{
				hediffs.RemoveAll(h => !h.IsUrgent());
			}
			else if (Settings.Get().minimalMedicineForNonUrgent && __beep_beep_MinimalMedicineAvailable)
			{
				if (hediffs.Any(h => h.IsUrgent()))
					hediffs.RemoveAll(h => !h.IsUrgent());
			}
			Log.Message($"Filtered to ({hediffs.ToStringSafeEnumerable()})");
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


	// because The Toil to get this medicine is FailOnDespawnedNullOrForbidden
	// And Medicine in inventory is despawned
	// You can't set the job to use already carried medicine.
	// Editing the toil would be more difficult.
	// But we can drop it in Notify_Starting so the normal job picks it back it  ¯\_(ツ)_/¯ 
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
			if (medicineToDrop == null) return;

			int needCount = Mathf.Min(medicineToDrop.stackCount, job.count);

			Log.Message($"{healer} Starting Tend with {medicineToDrop}");

			job.targetB = DropIt(medicineToDrop, needCount, healer, job);

			List<LocalTargetInfo> sharedMedicines = job.targetQueueA;
			List<int> sharedMedicineCounts = job.countQueue;
			if (sharedMedicines != null)
			{
				for (int i = 0; i < sharedMedicines.Count(); i++)
				{
					Log.Message($"{healer} queuing medicine {sharedMedicines[i].Thing}");
					DropIt(sharedMedicines[i].Thing, sharedMedicineCounts[i], healer, job);
				}
			}
		}

		public static Thing DropIt(Thing medicineToUse, int needCount, Pawn healer, Job job)
		{
			if (medicineToUse == null || medicineToUse.holdingOwner == null) return null;

			//Well apparently inventory items can be forbidden
			medicineToUse.SetForbidden(false, false);

			Thing droppedMedicine = null;
			if (medicineToUse.holdingOwner.Owner is Pawn_InventoryTracker holder)
			{
				Log.Message($"{holder.pawn} dropping {medicineToUse}x{needCount}");
				holder.innerContainer.TryDrop(medicineToUse, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}
			else if (medicineToUse.holdingOwner.Owner is Pawn_CarryTracker carrier)
			{
				Log.Message($"{carrier.pawn} dropping carried {medicineToUse}x{needCount}");
				carrier.innerContainer.TryDrop(medicineToUse, ThingPlaceMode.Direct, needCount, out droppedMedicine);
			}

			if (droppedMedicine != null)
			{
				medicineToUse = droppedMedicine;
				Log.Message($"{healer} now tending with {droppedMedicine}");
				if (droppedMedicine.IsForbidden(healer))
					Log.Message($"{droppedMedicine} is Forbidden, job will restart");
			}

			if (healer.ReserveAsMuchAsPossible(medicineToUse, job, FindBestMedicine.maxPawns, needCount) == 0)
				Verse.Log.Warning($"Needed medicine {medicineToUse} for {healer} seemed to be in a reserved stack. Job will fail but should try again, so ignore the error please.");

			return medicineToUse;
		}
	}


	//[HarmonyPatch(typeof(Toils_Tend), "PickupMedicine")]
	//[HarmonyPatch("<PickupMedicine>c__AnonStorey0", "<>m__0")]
	static class PickupMedicine_Patch
	{
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
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo FindBestMedicineInfo = AccessTools.Method(typeof(HealthAIUtility), nameof(HealthAIUtility.FindBestMedicine));
			MethodInfo FindInfo = AccessTools.Method(typeof(FindBestMedicine), nameof(FindBestMedicine.Find));


			//MethodInfo FirstOrDefaultInfo = AccessTools.Method(typeof(Enumerable), "FirstOrDefault", new Type[] { typeof(IEnumerable<ThingCount>) }, new Type[] { typeof(IEnumerable<ThingCount>) });
			//MethodInfo FirstOrDefaultInfo = AccessTools.FirstMethod(typeof(Enumerable), mi => mi.Name == "FirstOrDefault" && mi.GetParameters().Count() == 1);
			//FirstOrDefaultInfo = FirstOrDefaultInfo.MakeGenericMethod(new Type[] { typeof(IEnumerable<ThingCount>) });
			//MethodInfo ThingCountThingInfo = AccessTools.Property(typeof(ThingCount), "Thing").GetGetMethod();

			//
			MethodInfo GetTheBloodyThingInfo = AccessTools.Method(typeof(MakeNewToils_Patch), "GetTheBloodyThing");

			FieldInfo jobFieldInfo = AccessTools.Field(
				typeof(JobDriver), nameof(JobDriver.job));
			FieldInfo jobCountInfo = AccessTools.Field(
				typeof(Job), nameof(Job.count));
			List<CodeInstruction> iList = instructions.ToList();
			List<CodeInstruction> jobInstructions = new List<CodeInstruction>();
			for (int i = 0; i < iList.Count(); i++)
			{
				if(iList[i].opcode == OpCodes.Ldfld && iList[i].operand == jobFieldInfo)
				{
					jobInstructions.AddRange(iList.GetRange(i - 3, 4));
					break;
				}
			}

			foreach (CodeInstruction i in instructions)
			{
				if (i.opcode == OpCodes.Call && i.operand == FindBestMedicineInfo)
				{
					foreach (CodeInstruction jobI in jobInstructions)
						yield return jobI;
					yield return new CodeInstruction(OpCodes.Ldflda, jobCountInfo);
					yield return new CodeInstruction(OpCodes.Call, FindInfo);
					yield return new CodeInstruction(OpCodes.Call, GetTheBloodyThingInfo);
					//yield return new CodeInstruction(OpCodes.Call, FirstOrDefaultInfo);
					//yield return new CodeInstruction(OpCodes.Call, ThingCountThingInfo);
				}
				else yield return i;
			}
		}
		public static Thing GetTheBloodyThing(List<ThingCount> x)
		{
			return x.FirstOrDefault().Thing;
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
				Log.Message($"{label} {thing} @{rating} (dist: {distance}) (pawn: {pawn})");
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
			if (patient.GetCare() <= MedicalCareCategory.NoMeds || Medicine.GetMedicineCountToFullyHeal(patient) <= 0)
				return true;

			__result = Find(healer, patient, out int dummy).FirstOrDefault().Thing;
			return false;
		}

		//public static Thing FindBestMedicine(Pawn healer, Pawn patient)
		public static List<ThingCount> Find(Pawn healer, Pawn patient, out int totalCount)
		{
			totalCount = 0;
			Log.Message($"{healer} is tending to {patient}");

			float sufficientQuality = maxMedicineQuality + 1; // nothing is sufficient!
			if (Settings.Get().minimalMedicineForNonUrgent)
			{
				if (patient.health.hediffSet.hediffs.All(h => !h.TendableNow() || !h.IsUrgent()))
				{
					sufficientQuality = minMedicineQuality;
					Log.Message($"Sufficient medicine for non-urgent care is {sufficientQuality}");
				}
			}

			MedicalCareCategory defaultCare = patient.GetCare();

			//Care setting
			MedicalCareCategory finalCare = MedicalCareCategory.NoCare;
			var hediffCare = PriorityCareComp.Get();
			List<Hediff> hediffsToTend = HediffsToTend(patient);
			Log.Message($"Tending ({hediffsToTend.ToStringSafeEnumerable()})");
			foreach(Hediff h in hediffsToTend)
			{
				MedicalCareCategory toUse = defaultCare;
				if (hediffCare.TryGetValue(h, out MedicalCareCategory heCare))
					toUse = heCare;

				finalCare = toUse > finalCare ? toUse : finalCare;
			}
			Log.Message($"defaultCare = {defaultCare}, care = {finalCare}");

			//Android Droid support;
			Predicate<Thing> validatorDroid = t => true;
			Type extMechanicalPawn = AccessTools.TypeByName("Androids.MechanicalPawnProperties");
			bool isDroid = extMechanicalPawn != null && (patient.def.modExtensions?.Any(e => extMechanicalPawn.IsAssignableFrom(e.GetType())) ?? false);
			if (isDroid)
			{
				Log.Message($"{patient} is a droid");
				Type extRepair = AccessTools.TypeByName("Androids.DroidRepairProperties");
				validatorDroid = t => t.def.modExtensions?.Any(e => extRepair.IsAssignableFrom(e.GetType())) ?? false;
			}

			//Med
			Predicate<Thing> validatorMed = t => finalCare.AllowsMedicine(t.def) && validatorDroid(t);

			//Ground
			Map map = patient.Map;
			TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
			Predicate<Thing> validator = (Thing t) => validatorMed(t)
				&& map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams)
				&& !t.IsForbidden(healer) && healer.CanReserve(t, FindBestMedicine.maxPawns, 1);//can reserve at least 1
			Func<Thing, float> priorityGetter = (Thing t) => MedicineRating(t, sufficientQuality);
			List<Thing> groundMedicines = patient.Map.listerThings.ThingsInGroup(isDroid?ThingRequestGroup.HaulableEver:ThingRequestGroup.Medicine).FindAll(t => validator(t));

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

			List<ThingCount> result = new List<ThingCount>();
			if (bestMed.thing != null)
			{
				allMeds.RemoveLast();
				if (Settings.Get().useCloseMedicine && bestMed.pawn != null)
				{
					bestMed.DebugLog("Best: ");
					Log.Message($"checking nearby:");
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
					Log.Message($"No minimal medicine available");
				}
				int count = Medicine.GetMedicineCountToFullyHeal(patient);

				totalCount = count;
				Log.Message($"Medicine count = {count}");

				bestMed.DebugLog("Best Med on " + (bestMed.pawn == null ? "ground" : "hand") + ":");
				
				int usedCount = Mathf.Min(bestMed.thing.stackCount, count);
				result.Add(new ThingCount(bestMed.thing, usedCount));
				count -= usedCount;
				//Find some more needed nearby
				if (count > 0)
				{
					List<MedicineEvaluator> equalMedicines = allMeds.FindAll(eval => eval.rating == bestMed.rating);
					equalMedicines.SortBy(eval => DistanceTo(bestMed.pawn ?? bestMed.thing, eval.pawn ?? eval.thing));
					Thing droppedMedicine = null;
					Log.Message($"But needs {count} more");
					while (count > 0 && equalMedicines.Count > 0)
					{
						MedicineEvaluator closeMed = equalMedicines.First();
						equalMedicines.RemoveAt(0);

						closeMed.DebugLog("More: ");

						if (DistanceTo(droppedMedicine ?? bestMed.pawn ?? bestMed.thing, closeMed.pawn ?? closeMed.thing) > 8f) //8f as defined in CheckForGetOpportunityDuplicate
							break;

						usedCount = Mathf.Min(closeMed.thing.stackCount, count);
						closeMed.DebugLog("Using: ({usedCount})");

						result.Add(new ThingCount(closeMed.thing, usedCount));
						count -= usedCount;
					}
				}
			}
			return result;
		}

		public static List<Hediff> HediffsToTend(Pawn patient)
		{
			List<Hediff> toTend = new List<Hediff>();
			TendUtility.GetOptimalHediffsToTendWithSingleTreatment(patient, true, toTend);
			return toTend;
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
