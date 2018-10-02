using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Harmony;
using UnityEngine;

namespace SmartMedicine
{
	public class MedForHediffComp : GameComponent
	{
		public Dictionary<Hediff, MedicalCareCategory> hediffCare;
		public MedForHediffComp(Game game)
		{
			hediffCare = new Dictionary<Hediff, MedicalCareCategory>();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref hediffCare, "hediffCare");
		}

		public static Dictionary<Hediff, MedicalCareCategory> Get()
		{
			return Current.Game.GetComponent<MedForHediffComp>().hediffCare;
		}

		public static bool MaxCare(List<Hediff> hediffs, out MedicalCareCategory care)
		{
			care = MedicalCareCategory.NoCare;
			bool found = false;
			var hediffCare = Get();
			foreach(Hediff h in hediffs)
			{
				if (hediffCare.TryGetValue(h, out MedicalCareCategory heCare))
				{
					care = heCare > care ? heCare : care;
					found = true;
				}
			}
			return found;
		}
	}
	
	[HarmonyPatch(typeof(HealthCardUtility), "EntryClicked")]
	public static class SetMedForHediff
	{
		//private static void EntryClicked(IEnumerable<Hediff> diffs, Pawn pawn)
		public static bool Prefix(IEnumerable<Hediff> diffs, Pawn pawn)
		{
			if (Event.current.button == 1 &&
				diffs.Any(h => h.TendableNow(true)))
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				for (int i = 0; i < 5; i++)
				{
					MedicalCareCategory mc = (MedicalCareCategory)i;
					list.Add(new FloatMenuOption(mc.GetLabel(), delegate
					{
						foreach (Hediff h in diffs)
							MedForHediffComp.Get()[h] = mc;
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Hediff), "TendPriority", MethodType.Getter)]
	public static class PriorityHediff
	{
		public static bool Prefix(Hediff __instance, ref float __result)
		{
			if(MedForHediffComp.Get().ContainsKey(__instance))
			{
				__result = 10f;// alot
				return false;
			}
			return true;
		}
	}
}
