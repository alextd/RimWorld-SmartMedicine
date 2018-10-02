﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
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

		/*
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref hediffCare, "hediffCare");
		}
		*/

		public static Dictionary<Hediff, MedicalCareCategory> Get()
		{
			return Current.Game.GetComponent<MedForHediffComp>().hediffCare;
		}

		public static bool PriorityCare(Pawn patient, out MedicalCareCategory care)
		{
			care = MedicalCareCategory.NoCare;
			bool found = false;
			var hediffCare = Get();
			foreach(Hediff h in patient.health.hediffSet.hediffs)
			{
				if (h.TendableNow() && hediffCare.TryGetValue(h, out MedicalCareCategory heCare))
				{
					care = heCare > care ? heCare : care;
					found = true;
				}
			}
			return found;
		}
	}

	[HarmonyPatch(typeof(Hediff), "PostRemoved")]
	public static class RemoveHediffHook
	{
		//public virtual void PostRemoved()
		public static void Prefix(Hediff __instance)
		{
			MedForHediffComp.Get().Remove(__instance);
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
	public static class HediffRowCarePatch
	{
		//private static void DrawHediffRow(Rect rect, Pawn pawn, IEnumerable<Hediff> diffs, ref float curY)
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase mb)
		{
			//Find local Hediff
			LocalVariableInfo localHediffInfo = mb.GetMethodBody().LocalVariables.First(lv => lv.LocalType == typeof(Hediff));
			MethodInfo LabelInfo = AccessTools.Method(typeof(Widgets), "Label", new Type[] {typeof(Rect), typeof(string)});
			int labelCount = 0;

			MethodInfo DrawHediffCareInfo = AccessTools.Method(typeof(HediffRowCarePatch), nameof(DrawHediffCare));

			MethodInfo LabelButtonInfo = AccessTools.Method(typeof(HediffRowCarePatch), nameof(LabelButton));

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
		static HediffRowCarePatch()
		{
			//MedicalCareUtility		private static Texture2D[] careTextures;
			careTexturesField = AccessTools.Field(typeof(MedicalCareUtility), "careTextures");
		}

		public static void DrawHediffCare(Hediff hediff, ref Rect iconRect)
		{
			if(MedForHediffComp.Get().TryGetValue(hediff, out MedicalCareCategory heCare))
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
				list.Add(new FloatMenuOption("Default care", delegate
				{
					MedForHediffComp.Get().Remove(hediff);
				}));

				for (int i = 0; i < 5; i++)
				{
					MedicalCareCategory mc = (MedicalCareCategory)i;
					list.Add(new FloatMenuOption(mc.GetLabel(), delegate
					{
						MedForHediffComp.Get()[hediff] = mc;
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
			if(MedForHediffComp.Get().TryGetValue(__instance, out MedicalCareCategory hediffCare))
			{
				__result += 10f;// a lot
				return false;
			}
			return true;
		}
	}

	//Haul job needs to deliver to frames even if construction blocked
	[StaticConstructorOnStartup]
	public static class JobFailUseMedForHediff
	{
		static JobFailUseMedForHediff()
		{
			//AccessTools.Inner
			HarmonyMethod transpiler = new HarmonyMethod(typeof(JobFailUseMedForHediff), nameof(Transpiler));
			HarmonyInstance harmony = HarmonyInstance.Create("uuugggg.rimworld.SmartMedicine.main");
			MethodInfo AllowsMedicineInfo = AccessTools.Method(typeof(MedicalCareUtility), "AllowsMedicine");


			//Find the compiler-created method in JobDriver_TendPatient that calls AllowsMedicine
			List<Type> nestedTypes = new List<Type>(typeof(JobDriver_TendPatient).GetNestedTypes(BindingFlags.NonPublic));
			while (!nestedTypes.NullOrEmpty())
			{
				Type type = nestedTypes.Pop();
				nestedTypes.AddRange(type.GetNestedTypes(BindingFlags.NonPublic));

				foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
				{
					if (method.DeclaringType != type) continue;

					DynamicMethod dm = DynamicTools.CreateDynamicMethod(method, "-unused");

					if (Harmony.ILCopying.MethodBodyReader.GetInstructions(dm.GetILGenerator(), method).
						Any(ilcode => ilcode.operand == AllowsMedicineInfo))
					{
						Log.Message($"transpiling {method} for AllowsMedicine");
						harmony.Patch(method, null, null, transpiler);
					}
				}
			}
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			FieldInfo medCareInfo = AccessTools.Field(typeof(Pawn_PlayerSettings), "medCare");
			MethodInfo AllowsMedicineInfo = AccessTools.Method(typeof(MedicalCareUtility), "AllowsMedicine");

			MethodInfo AllowsMedicineForHediffInfo = AccessTools.Method(typeof(JobFailUseMedForHediff), "AllowsMedicineForHediff");

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
				if (instList[i].opcode == OpCodes.Call && instList[i].operand == AllowsMedicineInfo)
					instList[i].operand = AllowsMedicineForHediffInfo;

				yield return instList[i];

				if (i+2 < instList.Count && 
					instList[i + 2].opcode == OpCodes.Ldfld && instList[i + 2].operand == medCareInfo)
					i += 2;
			}
		}

		public static bool AllowsMedicineForHediff(Pawn deliveree, ThingDef med)
		{
			if (MedForHediffComp.PriorityCare(deliveree, out MedicalCareCategory heCare))
			{
				return heCare.AllowsMedicine(med);
			}

			return deliveree.playerSettings.medCare.AllowsMedicine(med);
		}
	}
}