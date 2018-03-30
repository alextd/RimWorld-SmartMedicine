using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Harmony;

namespace SmartMedicine
{
	public class WorkGiver_StockUpOnMedicine : WorkGiver_Scanner
	{
		public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

		public override ThingRequest PotentialWorkThingRequest
		{
			get
			{
				return ThingRequest.ForGroup(ThingRequestGroup.Medicine);
			}
		}

		public override bool ShouldSkip(Pawn pawn)
		{
			return StockUpUtility.IsFull(pawn);
		}

		public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
		{
			if (MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing) == 0)
			{
				JobFailReason.Is("TooHeavy".Translate());
				return false;
			}
			int needCount = StockUpUtility.Needs(pawn, thing);
			return needCount > 0 && pawn.CanReserve(thing, FindBestMedicine.maxPawns, needCount, null, forced);
		}

		public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
		{
			int needCount = StockUpUtility.Needs(pawn, thing);
			if (needCount == 0) return null;

			needCount = Math.Min(needCount, MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
			return new Job(SmartMedicineJobDefOf.StockUpOnMedicine, thing) { count = needCount };
		}

		public override Job NonScanJob(Pawn pawn)
		{
			Thing toReturn = StockUpUtility.ThingToReturn(pawn);
			if(toReturn == null) return null;
			
			int dropCount = -StockUpUtility.Needs(pawn, toReturn);
			if (StoreUtility.TryFindBestBetterStoreCellFor(toReturn, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out IntVec3 dropLoc, true))
				return new Job(SmartMedicineJobDefOf.StockDownOnMedicine, toReturn, dropLoc) { count = dropCount };
			return null;
		}
	}


	[StaticConstructorOnStartup]
	public static class StockUpUtility
	{
		public static List<ThingDef> medList;

		static StockUpUtility()
		{
			medList = DefDatabase<ThingDef>.AllDefs.Where(td => td.IsWithinCategory(ThingCategoryDefOf.Medicine)).ToList();
			medList.SortBy(td => - td.GetStatValueAbstract(StatDefOf.MedicalPotency));
		}

		public static bool StockingUpOn(Pawn pawn, Thing thing)
		{
			if (!Settings.Get().stockUpOnMedicine) return false;
			//Once each pawn gets their own count:
			//return true;

			return pawn.workSettings?.WorkGiversInOrderNormal.Any(wg => wg is WorkGiver_StockUpOnMedicine) ?? false;
		}

		public static int Needs(Pawn pawn, Thing thing) => Needs(pawn, thing.def);

		public static int Needs(Pawn pawn, ThingDef thingDef)
		{
			if (pawn.inventory == null) return 0;
			if (!Settings.Get().stockUpOnMedicine) return 0;

			int capacity = Settings.Get().stockUpCapacity;
			//if (!Settings.Get().stockUpList.Contains(thingDef)) capacity = 0;
			if (!Settings.Get().stockUpListByIndex.Contains(StockUpUtility.medList.IndexOf(thingDef))) capacity = 0;

			int invCount = pawn.inventory.innerContainer
				.Where(t => t.def == thingDef)
				.Select(t => t.stackCount)
				.Aggregate(0, (a, b) => a + b);
			return capacity - invCount;
		}

		public static Thing ThingToReturn(Pawn pawn)
		{
			if (pawn.inventory == null) return null;

			ThingDef thingDef = medList.FirstOrDefault(td => Needs(pawn, td) < 0);
			if ( thingDef == null) return null;

			return pawn.inventory.innerContainer.FirstOrDefault(t => t.def == thingDef);
		}

		public static bool IsFull(Pawn pawn)
		{
			return !medList.Any(td => Needs(pawn, td) != 0);
		}
	}

	[DefOf]
	public static class SmartMedicineJobDefOf
	{
		public static JobDef StockUpOnMedicine;
		public static JobDef StockDownOnMedicine;
	}
	
	//private void CleanupCurrentJob(JobCondition condition, bool releaseReservations, bool cancelBusyStancesSoft = true)
	[HarmonyPatch(typeof(Pawn_JobTracker), "CleanupCurrentJob")]
	public static class CleanupCurrentJob_Patch
	{
		public static void Prefix(Pawn_JobTracker __instance)
		{
			if (__instance.curJob?.def == JobDefOf.TendPatient)
			{
				FieldInfo pawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
				Pawn pawn = (Pawn)pawnField.GetValue(__instance);
				if (!pawn.Destroyed && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
				{
					if (StockUpUtility.StockingUpOn(pawn, pawn.carryTracker.CarriedThing))
						pawn.inventory.innerContainer.TryAddOrTransfer(pawn.carryTracker.CarriedThing);
				}
			}
		}
	}
}
