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
		public static JobDef StockUpOnMedicine;
		public static JobDef StockDownOnMedicine;
	}

	public class WorkGiver_StockUpOnMedicine : WorkGiver_Scanner
	{
		public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			return pawn.Map.listerThings.AllThings.Where(t => t.def.EverHaulable && pawn.StockingUpOn(t));
		}

		public override bool ShouldSkip(Pawn pawn)
		{
			return pawn.IsAtFullStock();
		}

		public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
		{
			if (MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing) == 0)
			{
				JobFailReason.Is("TooHeavy".Translate());
				return false;
			}
			int needCount = pawn.Needs(thing);
			return needCount > 0 && pawn.CanReserve(thing, FindBestMedicine.maxPawns, needCount, null, forced);
		}

		public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
		{
			int needCount = pawn.Needs(thing);
			if (needCount == 0) return null;

			needCount = Math.Min(needCount, MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
			return new Job(SmartMedicineJobDefOf.StockUpOnMedicine, thing) { count = needCount };
		}

		public override Job NonScanJob(Pawn pawn)
		{
			Thing toReturn = pawn.ThingToReturn();
			if (toReturn == null) return null;

			int dropCount = -pawn.Needs(toReturn);
			if (StoreUtility.TryFindBestBetterStoreCellFor(toReturn, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 dropLoc, true))
				return new Job(SmartMedicineJobDefOf.StockDownOnMedicine, toReturn, dropLoc) { count = dropCount };
			return null;
		}
	}
}