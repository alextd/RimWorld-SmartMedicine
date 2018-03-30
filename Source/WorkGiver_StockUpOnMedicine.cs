using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
			if(toReturn == null) return null;
			
			int dropCount = -pawn.Needs(toReturn);
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

		public static bool StockingUpOn(this Pawn pawn, Thing thing) => StockingUpOn(pawn, thing.def);

		public static bool StockingUpOn(this Pawn pawn, ThingDef thingDef)
		{
			if (!Settings.Get().stockUpOnMedicine) return false;

			if (pawn.inventory == null) return false;

			if (!medList.Contains(thingDef)) return false;

			//if (!Settings.Get().stockUpList.Contains(thingDef)) capacity = 0;
			if (!Settings.Get().stockUpListByIndex.Contains(StockUpUtility.medList.IndexOf(thingDef))) return false;

			return pawn.workSettings?.WorkGiversInOrderNormal.Any(wg => wg is WorkGiver_StockUpOnMedicine) ?? false;
		}

		public static int StockUpCount(this Pawn pawn, Thing thing) => pawn.StockUpCount(thing.def);
		public static int StockUpCount(this Pawn pawn, ThingDef thingDef)
		{
			return Settings.Get().stockUpCapacity;
		}

		public static int Needs(this Pawn pawn, Thing thing) => Needs(pawn, thing.def);

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

		public static Thing ThingToReturn(this Pawn pawn)
		{
			if (pawn.inventory == null) return null;

			ThingDef thingDef = medList.FirstOrDefault(td => Needs(pawn, td) < 0);
			if ( thingDef == null) return null;

			return pawn.inventory.innerContainer.FirstOrDefault(t => t.def == thingDef);
		}

		public static bool IsAtFullStock(this Pawn pawn)
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

	//ITab_Pawn_Gear
	//private void DrawThingRow(ref float y, float width, Thing thing, bool inventory = false)
	[HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
	public static class DrawThingRow_Patch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il, MethodBase mb)
		{
			IList<LocalVariableInfo> locals = mb.GetMethodBody().LocalVariables;
			int textIndex = locals.First(l => l.LocalType == typeof(string)).LocalIndex;
			
			MethodInfo SelPawnForGearInfo = AccessTools.Property(typeof(ITab_Pawn_Gear), "SelPawnForGear").GetGetMethod(true);

			MethodInfo AddStockTextInfo = AccessTools.Method(typeof(DrawThingRow_Patch), nameof(DrawThingRow_Patch.AddStockText));

			bool setStr = false;
			foreach (CodeInstruction i in instructions)
			{
				yield return i;

				//stloc.s str1
				if (!setStr && i.opcode == OpCodes.Stloc_S && i.operand is LocalBuilder lb && lb.LocalIndex == textIndex)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//this
					yield return new CodeInstruction(OpCodes.Call, SelPawnForGearInfo);//this.SelPawnForGearInfo
					yield return new CodeInstruction(OpCodes.Ldarg_3);//thing
					yield return new CodeInstruction(OpCodes.Ldloc_S, textIndex);//text
					yield return new CodeInstruction(OpCodes.Call, AddStockTextInfo);//AddStockText(pawn, thing, text)
					yield return new CodeInstruction(OpCodes.Stloc_S, textIndex);//text = AddStockText(pawn, thing, text);

					setStr = true;
				}
			}
		}

		public static string AddStockText(Pawn pawn, Thing thing, string text)
		{
			if (!pawn.StockingUpOn(thing)) return text;

			return text + String.Format(" / {0}", pawn.StockUpCount(thing));
		}
	}
}
