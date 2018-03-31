using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using TD.Utilities;

namespace SmartMedicine
{
	public class SmartMedicineGameComp : GameComponent
	{
		public ExDictionary<Pawn, ExDictionary<ThingDef, int>> settings = new ExDictionary<Pawn, ExDictionary<ThingDef, int>>() { keyMode = LookMode.Reference };

		public SmartMedicineGameComp(Game game) { }

		public override void ExposeData()
		{
			base.ExposeData();
			settings.ExposeData();
		}

		public static Dictionary<Pawn, ExDictionary<ThingDef, int>> Get()
		{
			return Current.Game.GetComponent<SmartMedicineGameComp>().settings;
		}
	}

	[StaticConstructorOnStartup]
	public static class StockUpUtility
	{
		public static Dictionary<ThingDef, int> StockUpSettings(this Pawn pawn)
		{
			Dictionary<Pawn, ExDictionary<ThingDef, int>> settings = SmartMedicineGameComp.Get();
			if (!settings.TryGetValue(pawn, out ExDictionary<ThingDef, int> pawnSettings))
				settings[pawn] = pawnSettings = new ExDictionary<ThingDef, int>();
			return pawnSettings;
		}

		public static bool StockingUpOn(this Pawn pawn, Thing thing) => pawn.StockingUpOn(thing.def);

		public static bool StockingUpOn(this Pawn pawn, ThingDef thingDef)
		{
			if (pawn.inventory == null) return false;

			return pawn.StockUpSettings().ContainsKey(thingDef);
		}

		public static void SetStockCount(this Pawn pawn, Thing thing, int count) => pawn.SetStockCount(thing.def, count);
		public static void SetStockCount(this Pawn pawn, ThingDef thingDef, int count)
		{
			pawn.StockUpSettings()[thingDef] = count;
		}

		public static int StockUpCount(this Pawn pawn, Thing thing) => pawn.StockUpCount(thing.def);
		public static int StockUpCount(this Pawn pawn, ThingDef thingDef)
		{
			return pawn.StockingUpOn(thingDef) ? pawn.StockUpSettings()[thingDef] : 0;
		}

		public static int Needs(this Pawn pawn, Thing thing) => pawn.Needs(thing.def);

		public static int Needs(this Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.StockingUpOn(thingDef)) return 0;

			int capacity = pawn.StockUpCount(thingDef);

			int invCount = pawn.inventory.innerContainer
				.Where(t => t.def == thingDef)
				.Select(t => t.stackCount)
				.Aggregate(0, (a, b) => a + b);
			return capacity - invCount;
		}

		public static bool Missing(this Pawn pawn, Thing thing) => pawn.Missing(thing.def);
		public static bool Missing(this Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.StockingUpOn(thingDef) || pawn.StockUpCount(thingDef) == 0) return false;
			return !pawn.inventory.innerContainer.Contains(thingDef);
		}

		public static Thing ThingToReturn(this Pawn pawn)
		{
			if (pawn.inventory == null) return null;

			ThingDef td = pawn.StockUpSettings().FirstOrDefault(kvp => pawn.Needs(kvp.Key) < 0).Key;
			if (td == null) return null;

			return pawn.inventory.innerContainer.FirstOrDefault(t => t.def == td);
		}

		public static bool IsAtFullStock(this Pawn pawn)
		{
			return !pawn.StockUpSettings().Any(kvp => Needs(pawn, kvp.Key) != 0);
		}
	}
}
