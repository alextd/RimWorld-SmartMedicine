using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace SmartMedicine
{
	[HarmonyPatch(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients")]
	[HarmonyPriority(Priority.Last)]  //Patch applied last, means the prefix goes first.
	public static class HackityGetBill
	{
		public static Bill bill;
		//private static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
		public static void Prefix(Bill bill)
		{
			HackityGetBill.bill = bill;
		}
	}
	[DefOf]
	public static class InventorySurgeryDefOf
	{
		public static RecipeDef Anesthetize;
	}

	[HarmonyPatch(typeof(WorkGiver_DoBill), "AddEveryMedicineToRelevantThings")]
	public static class InventorySurgery
	{
		//private static MedicalCareCategory GetMedicalCareCategory(Thing billGiver)
		public delegate MedicalCareCategory GetMedicalCareCategoryDel(Thing billGiver);
		public static GetMedicalCareCategoryDel GetMedicalCareCategory =
			AccessTools.MethodDelegate<GetMedicalCareCategoryDel>(AccessTools.Method(typeof(WorkGiver_DoBill), "GetMedicalCareCategory"));

		//private static void AddEveryMedicineToRelevantThings(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Predicate<Thing> baseValidator, Map map)
		public static void Postfix(Pawn pawn, Thing billGiver, List<Thing> relevantThings, Map map)
		{
			if (HackityGetBill.bill == null)
			{
				Verse.Log.Warning($"Smart Medicine Inventory Surgery not going to work for {pawn}; mod conflict in AddEveryMedicineToRelevantThings or TryFindBestBillIngredients?");
				return;
			}
			Predicate<Thing> baseValidator = (Thing t) => HackityGetBill.bill.IsFixedOrAllowedIngredient(t) && HackityGetBill.bill.recipe.ingredients.Any((IngredientCount ingNeed) => ingNeed.filter.Allows(t));
			Log.Message($"AddEveryMedicineToRelevantThings ({pawn}, {billGiver}, {HackityGetBill.bill})");
			MedicalCareCategory medicalCareCategory = GetMedicalCareCategory(billGiver);

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
			int statAdjust = (Mod.settings.minimalMedicineForNonUrgent && HackityGetBill.bill.recipe == InventorySurgeryDefOf.Anesthetize ? 1 : -1);
			relevantThings.SortBy(
				(Thing x) => statAdjust * x.GetStatValue(StatDefOf.MedicalPotency),
				//Check if item is in inventory or spawned in map: inventory "distance" is 0
				(Thing x) => x.Spawned ? x.Position.DistanceToSquared(billGiver.Position) : 0);

			HackityGetBill.bill = null;
		}
	}


	// Drop the medicine so that you can then pick it up. Ya really.
	[HarmonyPatch(typeof(JobDriver_DoBill), nameof(JobDriver_DoBill.CollectIngredientsToils))]
	public static class InsertToilDropInventoryThing
	{
		//public static IEnumerable<Toil> CollectIngredientsToils(TargetIndex ingredientInd, TargetIndex billGiverInd,
		//  TargetIndex ingredientPlaceCellInd, bool subtractNumTakenFromJobCount = false, bool failIfStackCountLessThanJobCount = true, bool placeInBillGiver = false)

		public static IEnumerable<Toil> Postfix(IEnumerable<Toil> result, JobDriver_DoBill __instance, TargetIndex ingredientInd)
		{
			bool first = true;
			foreach (Toil t in result)
			{
				//Insert new Toil "DropTargetThingIfInInventory" before "GotoThing"
				//because the goddamn pawn doesn't know how to start carrying the thing from their own inventory god fucking damnit
				//Gotothing fails on despawned so it's probably pretty safe to drop it as it would only otherwise fail in the inventory.
				//It literally can't be a preInitAction either on GotoThing since it checks for failure before that
				if (t.debugName == "GotoThing" && first)
				{
					first = false;
					Toil toil = ToilMaker.MakeToil("SmartMedicineDropTargetThingIfInInventory");
					toil.initAction = delegate
					{
						Pawn actor = toil.actor;
						Job curJob = actor.jobs.curJob;
						Thing thing = curJob.GetTarget(ingredientInd).Thing;
						if (actor.inventory.Contains(thing))
						{
							int count = Mathf.Min(curJob.count, actor.carryTracker.AvailableStackSpace(thing.def), thing.stackCount);

							if(actor.inventory.innerContainer.TryDrop(thing, actor.Position, actor.Map, ThingPlaceMode.Near, count, out var droppedThing))
								curJob.SetTarget(ingredientInd, droppedThing);
						}
					};
					toil.defaultCompleteMode = ToilCompleteMode.Instant;
					yield return toil;
				}

				yield return t;

			}
		}
	}
}
