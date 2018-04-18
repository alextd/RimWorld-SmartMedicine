using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using TD.Utilities;
using Harmony;

namespace SmartMedicine
{
	public class SmartMedicineGameComp : GameComponent
	{
		public ExDictionary<Pawn, ExDictionary<ThingDef, int>> settings = new ExDictionary<Pawn, ExDictionary<ThingDef, int>>() { keyMode = LookMode.Reference };
		public Pawn copiedPawn;

		public SmartMedicineGameComp(Game game) { }

		public override void ExposeData()
		{
			base.ExposeData();
			settings.ExposeData();
		}

		public static SmartMedicineGameComp Get()
		{
			return Current.Game.GetComponent<SmartMedicineGameComp>();
		}

		public static Dictionary<Pawn, ExDictionary<ThingDef, int>> Settings()
		{
			return Current.Game.GetComponent<SmartMedicineGameComp>().settings;
		}
	}

	[StaticConstructorOnStartup]
	public static class StockUpUtility
	{
		public static Dictionary<ThingDef, int> StockUpSettings(this Pawn pawn)
		{
			var settings = SmartMedicineGameComp.Settings();
			if (!settings.TryGetValue(pawn, out ExDictionary<ThingDef, int> pawnSettings))
				settings[pawn] = pawnSettings = new ExDictionary<ThingDef, int>();
			return pawnSettings;
		}

		public static void StockUpCopySettings(this Pawn pawn)
		{
			SmartMedicineGameComp.Get().copiedPawn = pawn;
		}

		public static void StockUpPasteSettings(this Pawn pawn)
		{
			Dictionary<Pawn, ExDictionary<ThingDef, int>> settings = SmartMedicineGameComp.Settings();
			if (settings.ContainsKey(SmartMedicineGameComp.Get().copiedPawn))
				settings[pawn] = new ExDictionary<ThingDef, int>(settings[SmartMedicineGameComp.Get().copiedPawn]);
		}

		public static Pawn CopiedPawn()
		{
			return SmartMedicineGameComp.Get().copiedPawn;
		}

		public static void StockUpClearSettings(this Pawn pawn)
		{
			SmartMedicineGameComp.Settings().Remove(pawn);
			StockUpCopySettings(null);
		}
		public static bool StockingUpOnAnything(this Pawn pawn)
		{
			if (!Settings.Get().stockUp || pawn.inventory == null) return false;

			return pawn.StockUpSettings().Count > 0;
		}

		public static bool StockingUpOn(this Pawn pawn, Thing thing) => pawn.StockingUpOn(thing.def);

		public static bool StockingUpOn(this Pawn pawn, ThingDef thingDef)
		{
			if (!Settings.Get().stockUp || pawn.inventory == null || pawn.inventory.UnloadEverything) return false;

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

		public static int StockUpNeeds(this Pawn pawn, Thing thing) => pawn.StockUpNeeds(thing.def);

		public static int StockUpNeeds(this Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.StockingUpOn(thingDef)) return 0;

			int capacity = pawn.StockUpCount(thingDef);

			int invCount = pawn.HasItemCount(thingDef);

			if (invCount > capacity) return capacity - invCount;

			if (!EnoughAvailable(thingDef, pawn.Map))
				return Settings.Get().stockUpReturn ? -invCount : 0;

			return capacity - invCount;
		}

		public static int StockUpWants(this Pawn pawn, Thing thing) => pawn.StockUpWants(thing.def);

		public static int StockUpWants(this Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.StockingUpOn(thingDef)) return 0;

			return pawn.StockUpCount(thingDef) - pawn.HasItemCount(thingDef);
		}

		public static int HasItemCount(this Pawn pawn, ThingDef thingDef)
		{
			return pawn.inventory.innerContainer
				.Where(t => t.def == thingDef)
				.Select(t => t.stackCount)
				.Aggregate(0, (a, b) => a + b);
		}

		public static bool StockUpMissing(this Pawn pawn, Thing thing) => pawn.StockUpMissing(thing.def);
		public static bool StockUpMissing(this Pawn pawn, ThingDef thingDef)
		{
			if (!pawn.StockingUpOn(thingDef) || pawn.StockUpCount(thingDef) == 0) return false;
			return !pawn.inventory.innerContainer.Contains(thingDef);
		}

		public static void StockUpStop(this Pawn pawn, Thing thing) => pawn.StockUpStop(thing.def);
		public static void StockUpStop(this Pawn pawn, ThingDef thingDef)
		{
			pawn.StockUpSettings().Remove(thingDef);
			if(CopiedPawn() == pawn && !pawn.StockingUpOnAnything())
				StockUpCopySettings(null);
		}

		public static Thing StockUpThingToReturn(this Pawn pawn)
		{
			if (!Settings.Get().stockUp || pawn.inventory == null) return null;

			ThingDef td = pawn.StockUpSettings().FirstOrDefault(kvp => pawn.StockUpNeeds(kvp.Key) < 0).Key;
			if (td == null) return null;

			return pawn.inventory.innerContainer.FirstOrDefault(t => t.def == td);
		}

		public static bool StockUpIsFull(this Pawn pawn)
		{
			if (!Settings.Get().stockUp) return true;

			return !pawn.StockUpSettings().Any(kvp => pawn.StockUpNeeds(kvp.Key) != 0);
		}

		public static bool EnoughAvailable(Thing thing) => EnoughAvailable(thing.def, thing.Map);
		public static bool EnoughAvailable(ThingDef thingDef, Map map)
		{
			float enough = Settings.Get().stockUpEnough;
			if (enough == 0.0f) return true;

			float stockUpCount = 0;
			float available = map.resourceCounter.GetCount(thingDef);
			foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
			{
				stockUpCount += p.StockUpCount(thingDef);
				available += p.HasItemCount(thingDef);
			}
			
			return available >= stockUpCount * enough;
		}
	}

	[HarmonyPatch(typeof(Pawn), "Destroy")]
	public static class Destroy_Pawn_Patch
	{
		//public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		public static void Postfix(Pawn __instance)
		{
			__instance.StockUpClearSettings();
		}
	}
}
