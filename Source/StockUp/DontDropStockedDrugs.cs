using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using RimWorld;
using Verse;

namespace SmartMedicine.StockUp
{
	//Job JobGiver_DropUnusedInventory.TryGiveJob(Pawn pawn)
	[HarmonyPatch(typeof(JobGiver_DropUnusedInventory), "TryGiveJob")]
	public static class DontDropStockedDrugs
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase mb)
		{
			MethodInfo IsDrugInfo = AccessTools.Property(typeof(ThingDef), "IsDrug").GetGetMethod();

			bool foundDrugInfo = false;
			bool nextLineIsBranch = false;

			MethodInfo StockingUpInfo = AccessTools.Method(typeof(StockUpUtility), nameof(StockUpUtility.StockingUpOn),
				new Type[] { typeof(Pawn), typeof(Thing)});

			foreach (CodeInstruction i in instructions)
			{
				yield return i;
				
				if (i.opcode == OpCodes.Callvirt && i.operand == IsDrugInfo)
				{
					if (!foundDrugInfo) foundDrugInfo = true;
					else nextLineIsBranch = true;
				}
				else if (i.opcode == OpCodes.Brfalse && nextLineIsBranch)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);//pawn
					yield return new CodeInstruction(OpCodes.Ldloc_3);//thing
					yield return new CodeInstruction(OpCodes.Call, StockingUpInfo);//pawn.StockingUpOn(thing)
					yield return new CodeInstruction(OpCodes.Brtrue, i.operand);
					nextLineIsBranch = false;
				}
			}
		}
	}
}
