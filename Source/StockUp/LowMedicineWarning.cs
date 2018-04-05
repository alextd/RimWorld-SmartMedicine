using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using RimWorld;
using Verse;

namespace SmartMedicine.StockUp
{
	[HarmonyPatch(typeof(Alert_LowMedicine), "MedicineCount")]
	static class LowMedicineWarning
	{
		//private int MedicineCount(Map map)
		static void Postfix(Map map, ref int __result)
		{
			int invCount = 0;

			foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
				foreach (Thing thing in pawn.inventory.innerContainer)
					if (ThingRequestGroup.Medicine.Includes(thing.def))
						invCount += thing.stackCount;

			__result += invCount;
		}
	}
}
