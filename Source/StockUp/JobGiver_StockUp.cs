using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Harmony;

namespace SmartMedicine
{
	[DefOf]
	public static class SmartMedicineJobDefOf
	{
		public static JobDef StockUp;
		public static JobDef StockDown;
	}

	public class JobGiver_StockUp : ThinkNode_JobGiver
	{
		public static bool Skip(Pawn pawn)
		{
			Log.Message($"Skip need tend?");
			if (pawn.Map.mapPawns.AllPawnsSpawned.Any(p => HealthAIUtility.ShouldBeTendedNowByPlayer(p) && pawn.CanReserveAndReach(p, PathEndMode.ClosestTouch, Danger.Deadly)))
				return true;

			if (pawn.Map.mapPawns.AllPawnsSpawned.Any(p => p is IBillGiver billGiver && billGiver.BillStack.AnyShouldDoNow && pawn.CanReserveAndReach(p, PathEndMode.ClosestTouch, Danger.Deadly)))
				return true;

			return false;
		}
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (pawn.StockUpIsFull()) return null;
			Log.Message($"{pawn} needs stocking up");

			if (Skip(pawn))
				return null;

			Log.Message($"any things?");
			Predicate<Thing> validator = (Thing t) => pawn.StockingUpOn(t) && pawn.StockUpNeeds(t) > 0 && pawn.CanReserve(t, FindBestMedicine.maxPawns, 1) && !t.IsForbidden(pawn);
			Thing thing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999, validator);
			if (thing != null)
			{
				int pickupCount = Math.Min(pawn.StockUpNeeds(thing), MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
				Log.Message($"{pawn} stock thing is {thing}, count {pickupCount}");
				if (pickupCount > 0)
					return new Job(SmartMedicineJobDefOf.StockUp, thing) { count = pickupCount};
			}

			Log.Message($"{pawn} looking to return");
			Thing toReturn = pawn.StockUpThingToReturn();
			if (toReturn == null) return null;
			Log.Message($"returning {toReturn}");

			int dropCount = -pawn.StockUpNeeds(toReturn);
			Log.Message($"dropping {dropCount}");
			if (StoreUtility.TryFindBestBetterStoreCellFor(toReturn, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 dropLoc, true))
				return new Job(SmartMedicineJobDefOf.StockDown, toReturn, dropLoc) { count = dropCount };
			Log.Message($"nowhere to store");
			return null;
		}
	}

	//private void CleanupCurrentJob(JobCondition condition, bool releaseReservations, bool cancelBusyStancesSoft = true)
	[HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
	public static class CleanupCurrentJob_Patch
	{
		public static void Prefix(Pawn_JobTracker __instance, Pawn ___pawn)
		{
			if (__instance.curJob?.def == JobDefOf.TendPatient)
			{
				Pawn pawn = ___pawn;
				if (!pawn.Destroyed && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				{
					if (StockUpUtility.StockingUpOn(pawn, pawn.carryTracker.CarriedThing))
						pawn.inventory.innerContainer.TryAddOrTransfer(pawn.carryTracker.CarriedThing);
				}
			}
		}
	}
}