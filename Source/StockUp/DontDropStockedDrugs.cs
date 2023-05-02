using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SmartMedicine.StockUp
{
	//What was 1.3 transpiler:
	//Job JobGiver_DropUnusedInventory.TryGiveJob(Pawn pawn)
	//[HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "TryGiveJob")]


	//Now in 1.4 prefix on new helper method:
	//public static bool ShouldKeepDrugInInventory(Pawn pawn, Thing drug)
	[HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "ShouldKeepDrugInInventory")]
	public static class DontDropStockedDrugs
	{
		public static bool Prefix(ref bool __result, Pawn pawn, Thing drug)
		{
			if(pawn.StockingUpOn(drug))
			{
				__result = true;
				return false;
			}
			return true;
		}
	}
}