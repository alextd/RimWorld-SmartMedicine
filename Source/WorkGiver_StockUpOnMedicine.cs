using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace SmartMedicine
{
	public class WorkGiver_StockUpOnMedicine : WorkGiver_Scanner
	{
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

		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			Log.Message(pawn + " HasJobOnThing " + t + "(" + forced + ")");
			return StockUpUtility.Needs(pawn, t.def) > 0 && pawn.CanReserve(t) &&
				MassUtility.CountToPickUpUntilOverEncumbered(pawn, t) > 0;

		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			Log.Message(pawn + " JobOnThing " + t);

			int missing = StockUpUtility.Needs(pawn, t.def);
			missing = Math.Min(missing, MassUtility.CountToPickUpUntilOverEncumbered(pawn, t));
			if (missing == 0) return null;

			return new Job(SmartMedicineJobDefOf.StockUpOnMedicine, t)
			{ count = missing };
		}
	}

	[StaticConstructorOnStartup]
	public static class StockUpUtility
	{
		private static int capacity = 10;
		private static float maxQuality = 1.0f;
		private static List<ThingDef> medList;

		static StockUpUtility()
		{
			medList = DefDatabase<ThingDef>.AllDefs
					.Where(td => td.IsWithinCategory(ThingCategoryDefOf.Medicine) && 
					td.GetStatValueAbstract(StatDefOf.MedicalPotency) <= maxQuality).ToList();
		}

		public static int Needs(Pawn pawn, ThingDef thingDef)
		{
			Thing invThing = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == thingDef);
			return capacity - (invThing?.stackCount ?? 0);
		}

		public static bool IsFull(Pawn pawn)
		{
			foreach(ThingDef td in medList)
			{
				if (Needs(pawn, td) > 0)
					return false;
			}
			return true;
		}
	}

	[DefOf]
	public static class SmartMedicineJobDefOf
	{
		public static JobDef StockUpOnMedicine;
	}
}
