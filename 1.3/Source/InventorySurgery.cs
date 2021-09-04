﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace SmartMedicine
{
	[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
	[HarmonyPriority(Priority.Last)]	//Patch applied last, means the prefix goes first.
	public static class HackityGetBill
	{
		public static Bill bill;
		//private static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
		public static void Prefix(Bill bill)
		{
			HackityGetBill.bill = bill;
		}
	}

	[HarmonyPatch(typeof(WorkGiver_DoBill), "AddEveryMedicineToRelevantThings")]
	[DefOf]
	public static class InventorySurgery
	{
		public static RecipeDef Anesthetize;

		//private static void AddEveryMedicineToRelevantThings(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map)
		public static void Postfix(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Map map)
		{
			if(HackityGetBill.bill == null)
			{
				Verse.Log.Warning($"Smart Medicine Inventory Surgery not going to work for {pawn}; mod conflict in AddEveryMedicineToRelevantThings or TryFindBestBillIngredients?");
				return;
			}
			Predicate<Thing> baseValidator = (Thing t) => HackityGetBill.bill.IsFixedOrAllowedIngredient(t) && HackityGetBill.bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t));
			Log.Message($"AddEveryMedicineToRelevantThings ({pawn}, {billGiver}, {HackityGetBill.bill})");
			MedicalCareCategory medicalCareCategory = (MedicalCareCategory)AccessTools.Method(typeof(WorkGiver_DoBill), "GetMedicalCareCategory").Invoke(null, new object[] { billGiver });

			Log.Message($"inventory: ({pawn.inventory.GetDirectlyHeldThings().ToStringSafeEnumerable()})");
			foreach (Thing t in pawn.inventory.GetDirectlyHeldThings())
			{
				if (medicalCareCategory.AllowsMedicine(t.def) && baseValidator(t))
				{
					Log.Message($"{pawn} considering {t} for surgery on {billGiver}");
					relevantThings.Add(t);
				}
			}

			//Tiny addition to use minimal medicine for Anesthetize bill. TODO: Make this a def extension so any recipe could use it, though no one will so why really
			int statAdjust = (Settings.Get().minimalMedicineForNonUrgent && HackityGetBill.bill.recipe == Anesthetize ? 1 : -1);
			relevantThings.SortBy(
				(Thing x) => statAdjust * x.GetStatValue(StatDefOf.MedicalPotency),
				//Check if item is in inventory or spawned in map: inventory "distance" is 0
				(Thing x) => x.Spawned ? x.Position.DistanceToSquared(billGiver.Position) : 0);

			HackityGetBill.bill = null;
		}
	}

	[HarmonyPatch(typeof(Toils_JobTransforms), "ExtractNextTargetFromQueue")]
	public static class ExtractQueueDrop
	{
		//public static Toil ExtractNextTargetFromQueue(TargetIndex ind, bool failIfCountFromQueueTooBig = true)
		public static void Postfix (Toil __result, TargetIndex ind)
		{
			__result.AddFinishAction(() =>
			{
				Pawn actor = __result.actor;
				Job job = actor.jobs.curJob;
				if (job.def != JobDefOf.DoBill) return;

				Thing thing = job.GetTarget(ind).Thing;
				Log.Message($"ExtractNextTargetFromQueue Finish: {actor}, {job}, {thing}, {job.count}");

				if (thing?.ParentHolder is Pawn_InventoryTracker)
				{
					thing.holdingOwner.TryDrop(thing, ThingPlaceMode.Direct, job.count, out Thing droppedThing);
					job.SetTarget(ind, droppedThing);
				}
			});
		}
	}
}
