using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;
using Harmony;
using UnityEngine;
using TD.Utilities;

namespace SmartMedicine
{
	public class PriorityCareComp : GameComponent
	{
		public Dictionary<Hediff, MedicalCareCategory> hediffCare;
		public PriorityCareComp(Game game)
		{
			hediffCare = new Dictionary<Hediff, MedicalCareCategory>();
		}

		/*
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref hediffCare, "hediffCare");
		}
		*/

		public static Dictionary<Hediff, MedicalCareCategory> Get()
		{
			return Current.Game.GetComponent<PriorityCareComp>().hediffCare;
		}

		public static bool MaxPriorityCare(Pawn patient, out MedicalCareCategory care) => MaxPriorityCare(patient.health.hediffSet.hediffs, out care);
		public static bool MaxPriorityCare(List<Hediff> hediffs, out MedicalCareCategory care)
		{
			care = MedicalCareCategory.NoCare;
			bool found = false;
			var hediffCare = Get();
			foreach (Hediff h in hediffs)
			{
				if (h.TendableNow() && hediffCare.TryGetValue(h, out MedicalCareCategory heCare))
				{
					care = heCare > care ? heCare : care;
					found = true;
				}
			}
			return found;
		}
		
		public static bool AllPriorityCare(Pawn patient) => AllPriorityCare(patient.health.hediffSet.hediffs);
		public static bool AllPriorityCare(List<Hediff> hediffs)
		{
			var hediffCare = Get();
			foreach(Hediff h in hediffs)
			{
				if (!hediffCare.ContainsKey(h))
					return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Hediff), "PostRemoved")]
	public static class RemoveHediffHook
	{
		//public virtual void PostRemoved()
		public static void Prefix(Hediff __instance)
		{
			Log.Message($"removing {__instance} from priorityCare");
			PriorityCareComp.Get().Remove(__instance);
		}
	}
	
	[HarmonyPatch(typeof(HealthCardUtility), "EntryClicked")]
	public static class SuppressRightClickHediff
	{
		//private static void EntryClicked(IEnumerable<Hediff> diffs, Pawn pawn)
		public static bool Prefix()
		{
			//suppress right click for popup 
			return Event.current.button != 1;
		}
	}

	[StaticConstructorOnStartup]
	[HarmonyPatch(typeof(HealthCardUtility), "DrawHediffRow")]
	public static class HediffRowPriorityCare
	{
		//private static void DrawHediffRow(Rect rect, Pawn pawn, IEnumerable<Hediff> diffs, ref float curY)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase mb)
		{
			//Find local Hediff
			LocalVariableInfo localHediffInfo = mb.GetMethodBody().LocalVariables.First(lv => lv.LocalType == typeof(Hediff));
			MethodInfo LabelInfo = AccessTools.Method(typeof(Widgets), "Label", new Type[] {typeof(Rect), typeof(string)});
			int labelCount = 0;

			MethodInfo DrawHediffCareInfo = AccessTools.Method(typeof(HediffRowPriorityCare), nameof(DrawHediffCare));

			MethodInfo LabelButtonInfo = AccessTools.Method(typeof(HediffRowPriorityCare), nameof(LabelButton));

			List<CodeInstruction> instList = instructions.ToList();
			for (int i = 0; i < instList.Count; i++)
			{
				if(instList[i].opcode == OpCodes.Call && instList[i].operand == LabelInfo)
				{
					if (labelCount == 2)//Third label is hediff label
					{
						yield return new CodeInstruction(OpCodes.Ldloc_S, localHediffInfo.LocalIndex);//hediff
						yield return new CodeInstruction(OpCodes.Call, LabelButtonInfo);
					}
					else
					{
						labelCount++;
						yield return instList[i];
					}
				}
				//Find double curY for curY +=
				//IL_03bd: ldarg.3      // curY
				//IL_03be: ldarg.3      // curY
				else if (instList[i].opcode == OpCodes.Ldarg_3 &&
					instList[i + 1].opcode == OpCodes.Ldarg_3)//curY
				{
					CodeInstruction iconRectInst = null;
					for (int j = i - 1; j >= 0; j--)
					{
						//First previous local var should be the icon rect
						if (instList[j].opcode == OpCodes.Ldloca_S)
						{
							iconRectInst = instList[j];
							break;
						}
					}
					//Insert my hediff care icon
					yield return new CodeInstruction(OpCodes.Ldloc_S, localHediffInfo.LocalIndex) { labels = instList[i].labels };//hediff
					yield return iconRectInst;//rect
					yield return new CodeInstruction(OpCodes.Call, DrawHediffCareInfo);
					instList[i].labels = null;
					yield return instList[i];
				}
				else 
					yield return instList[i];
			}
		}
		
		private static FieldInfo careTexturesField;
		static HediffRowPriorityCare()
		{
			//MedicalCareUtility		private static Texture2D[] careTextures;
			careTexturesField = AccessTools.Field(typeof(MedicalCareUtility), "careTextures");
		}

		public static void DrawHediffCare(Hediff hediff, ref Rect iconRect)
		{
			if(PriorityCareComp.Get().TryGetValue(hediff, out MedicalCareCategory heCare))
			{
				Texture2D tex = ((Texture2D[])careTexturesField.GetValue(null))[(int)heCare];
				GUI.DrawTexture(iconRect, tex);
				iconRect.x -= iconRect.width;
			}
		}

		public static void LabelButton(Rect rect, string text,  Hediff hediff)
		{
			Widgets.Label(rect, text);
			if (hediff.TendableNow(true) && Event.current.button == 1 && Widgets.ButtonInvisible(rect))
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();

				//Default care
				list.Add(new FloatMenuOption("TD.DefaultCare".Translate(), delegate
				{
					PriorityCareComp.Get().Remove(hediff);
				}));

				for (int i = 0; i < 5; i++)
				{
					MedicalCareCategory mc = (MedicalCareCategory)i;
					list.Add(new FloatMenuOption(mc.GetLabel(), delegate
					{
						PriorityCareComp.Get()[hediff] = mc;
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
		}
	}

	[HarmonyPatch(typeof(Hediff), "TendPriority", MethodType.Getter)]
	public static class PriorityHediff
	{
		public static bool Prefix(Hediff __instance, ref float __result)
		{
			if(PriorityCareComp.Get().TryGetValue(__instance, out MedicalCareCategory hediffCare))
			{
				MedicalCareCategory defaultCare = __instance.pawn.GetCare();

				int diff = ((int)hediffCare) - ((int)defaultCare);
				__result += diff*5;//Raise priority for higher meds, lower for lower meds.
				return false;
			}
			return true;
		}
	}

	//Haul job needs to deliver to frames even if construction blocked
	[StaticConstructorOnStartup]
	public static class PriorityCareJobFail
	{
		static PriorityCareJobFail()
		{
			HarmonyMethod transpiler = new HarmonyMethod(typeof(PriorityCareJobFail), nameof(Transpiler));
			HarmonyInstance harmony = HarmonyInstance.Create("uuugggg.rimworld.SmartMedicine.main");

			MethodInfo AllowsMedicineInfo = AccessTools.Method(typeof(MedicalCareUtility), "AllowsMedicine");

			Predicate<MethodInfo> check = delegate (MethodInfo method)
			{
				DynamicMethod dm = DynamicTools.CreateDynamicMethod(method, "-unused");

				return Harmony.ILCopying.MethodBodyReader.GetInstructions(dm.GetILGenerator(), method)
					.Any(ilcode => ilcode.operand == AllowsMedicineInfo);
			};

			harmony.PatchGeneratedMethod(typeof(JobDriver_TendPatient), check, transpiler: transpiler);
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo medCareInfo = AccessTools.Field(typeof(Pawn_PlayerSettings), "medCare");
			MethodInfo AllowsMedicineInfo = AccessTools.Method(typeof(MedicalCareUtility), "AllowsMedicine");

			MethodInfo AllowsMedicineForHediffInfo = AccessTools.Method(typeof(PriorityCareJobFail), "AllowsMedicineForHediff");

			//
			//IL_007d: ldfld        class RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0' RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0'/'<MakeNewToils>c__AnonStorey1'::'<>f__ref$0'
			//IL_0082: ldfld class RimWorld.JobDriver_TendPatient RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0'::$this
			//IL_0087: call instance class Verse.Pawn RimWorld.JobDriver_TendPatient::get_Deliveree()
			//After Deliveree Pawn

			//IL_008c: ldfld class RimWorld.Pawn_PlayerSettings Verse.Pawn::playerSettings
			//IL_0091: ldfld valuetype RimWorld.MedicalCareCategory RimWorld.Pawn_PlayerSettings::medCare
			//Skip medCare

			//IL_0096: ldarg.0      // this
			//IL_0097: ldfld        class RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0' RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0'/'<MakeNewToils>c__AnonStorey1'::'<>f__ref$0'
			//IL_009c: ldfld class RimWorld.JobDriver_TendPatient RimWorld.JobDriver_TendPatient/'<MakeNewToils>c__Iterator0'::$this
			//IL_00a1: call instance class Verse.Thing RimWorld.JobDriver_TendPatient::get_MedicineUsed()
			//IL_00a6: ldfld class Verse.ThingDef Verse.Thing::def

			//IL_00ab: call         bool RimWorld.MedicalCareUtility::AllowsMedicine(valuetype RimWorld.MedicalCareCategory, class Verse.ThingDef)
			//Call my method instead that checks both

			//IL_00b0: brtrue IL_00b7

			List <CodeInstruction> instList = instructions.ToList();
			for (int i = 0; i < instList.Count; i++)
			{
				//pawn.AllowsMedicineForHediff, not pawn.playerSettings.medCare.AllowsMedicine
				if (instList[i].opcode == OpCodes.Call && instList[i].operand == AllowsMedicineInfo)
					instList[i].operand = AllowsMedicineForHediffInfo;

				yield return instList[i];

				//Remove .playerSettings.medCare, just using pawn
				if (i+2 < instList.Count && 
					instList[i + 2].opcode == OpCodes.Ldfld && instList[i + 2].operand == medCareInfo)
					i += 2;
			}
		}

		public static bool AllowsMedicineForHediff(Pawn deliveree, ThingDef med)
		{
			if (PriorityCareComp.MaxPriorityCare(deliveree, out MedicalCareCategory heCare))
			{
				//This is uses to allow higher medicine above normal limit below.
				//this is NOT used to stop the job is PriorityCare is lowered
				if (heCare.AllowsMedicine(med)) return true;
			}

			//Not required but hey why dont I patch this in for Pharmacist
			MedicalCareCategory care = deliveree.GetCare();

			return care.AllowsMedicine(med);
		}
	}

	[HarmonyPatch(typeof(Hediff), "TendableNow")]
	public static class PriorityCareTendableNow
	{
		//public virtual bool TendableNow(bool ignoreTimer = false);
		public static bool Prefix(ref bool __result, Hediff __instance, bool ignoreTimer)
		{
			if (ignoreTimer) return true;

			if (PriorityCareComp.Get().TryGetValue(__instance, out MedicalCareCategory heCare) && heCare == MedicalCareCategory.NoCare)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}
}
