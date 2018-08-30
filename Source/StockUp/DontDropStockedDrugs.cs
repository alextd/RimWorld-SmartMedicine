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
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo IsDrugInfo = AccessTools.Property(typeof(ThingDef), "IsDrug").GetGetMethod();

			MethodInfo StockingUpInfo = AccessTools.Method(typeof(StockUpUtility), nameof(StockUpUtility.StockingUpOn),
				new Type[] { typeof(Pawn), typeof(Thing) });

			List<CodeInstruction> instList = instructions.ToList();
			for (int i = 0; i < instList.Count(); i++)
			{
				CodeInstruction inst = instList[i];
				yield return inst;
				
				if (inst.opcode == OpCodes.Brfalse
					&& instList[i - 1].opcode == OpCodes.Callvirt && instList[i - 1].operand == IsDrugInfo)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);//pawn
					yield return new CodeInstruction(OpCodes.Ldloc_3);//thing
					yield return new CodeInstruction(OpCodes.Call, StockingUpInfo);//pawn.StockingUpOn(thing)
					yield return new CodeInstruction(OpCodes.Brtrue, inst.operand);
				}
			}
		}
	}
}