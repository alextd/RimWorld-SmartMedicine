using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

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
		protected override Job TryGiveJob(Pawn pawn)
		{
			if (pawn.StockUpIsFull()) return null;
			
			if (pawn.Map.mapPawns.AllPawnsSpawned.Any(p => HealthAIUtility.ShouldBeTendedNow(p) && pawn.CanReserveAndReach(p, PathEndMode.ClosestTouch, Danger.Deadly)))
				return null;
			
			IEnumerable<Thing> things = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
			Predicate<Thing> validator = (Thing t) => pawn.StockingUpOn(t) && pawn.StockUpNeeds(t) > 0 && pawn.CanReserve(t, FindBestMedicine.maxPawns, 1) && !t.IsForbidden(pawn);
			Thing thing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForGroup(ThingRequestGroup.HaulableEver), PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999, validator);
			if (thing != null)
			{
				int pickupCount = Math.Min(pawn.StockUpNeeds(thing), MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
				if (pickupCount > 0)
					return new Job(SmartMedicineJobDefOf.StockUp, thing) { count = pickupCount};
			}
			
			Thing toReturn = pawn.StockUpThingToReturn();
			if (toReturn == null) return null;

			int dropCount = -pawn.StockUpNeeds(toReturn);
			if (StoreUtility.TryFindBestBetterStoreCellFor(toReturn, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 dropLoc, true))
				return new Job(SmartMedicineJobDefOf.StockDown, toReturn, dropLoc) { count = dropCount };
			return null;
		}
	}
}