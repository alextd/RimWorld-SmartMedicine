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

namespace SmartMedicine.SurgeryUnlimited
{
	public class SurgeryUnlimitedGameComponent : GameComponent
	{
		public bool surgeryUnlimitedDefault;
		public HashSet<Pawn> surgeryUnlimited;

		public SurgeryUnlimitedGameComponent(Game game)
		{
			surgeryUnlimitedDefault = Settings.Get().defaultUnlimitedSurgery;
			surgeryUnlimited = new HashSet<Pawn>();
		}

		public static SurgeryUnlimitedGameComponent Get()
		{
			return Current.Game?.GetComponent<SurgeryUnlimitedGameComponent>();
		}

		public void Set(Pawn pawn, bool val)
		{
			if (val && !surgeryUnlimited.Contains(pawn))
				surgeryUnlimited.Add(pawn);
			if (!val && surgeryUnlimited.Contains(pawn))
				surgeryUnlimited.Remove(pawn);
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref surgeryUnlimitedDefault, "surgeryUnlimitedDefault", Settings.Get().defaultUnlimitedSurgery);
			Scribe_Collections.Look(ref surgeryUnlimited, "surgeryUnlimitedList", LookMode.Reference);
		}
	}

	[HarmonyPatch(typeof(Dialog_MedicalDefaults), "DoWindowContents")]
	static class SurgeryUnlimitedSetting
	{
		//public override void DoWindowContents(Rect inRect)
		public static void Postfix(Rect inRect)
		{
			Rect botRect = inRect.BottomPartPixels(Text.LineHeight);
			botRect.y -= 40f;//CloseButSize
			Widgets.CheckboxLabeled(botRect, "TD.DefaultSurgeryUnlimited".Translate(), ref SurgeryUnlimitedGameComponent.Get().surgeryUnlimitedDefault);
		}
	}

	[HarmonyPatch(typeof(WorkGiver_DoBill), "GetMedicalCareCategory")]
	static class SurgeryUnlimitedBillCategory
	{
		//private static MedicalCareCategory GetMedicalCareCategory(Thing billGiver)
		public static void Postfix(ref MedicalCareCategory __result, Thing billGiver)
		{
			if (billGiver is Pawn p && SurgeryUnlimitedGameComponent.Get().surgeryUnlimited.Contains(p))
				__result = MedicalCareCategory.Best;
		}
	}

	[HarmonyPatch(typeof(Pawn_PlayerSettings), "ResetMedicalCare")]
	//public void ResetMedicalCare()
	public static class SurgeryUnlimitedDefault
	{
		public static void Postfix(Pawn_PlayerSettings __instance, Pawn ___pawn)
		{
			if (Scribe.mode == LoadSaveMode.LoadingVars) return;
			Pawn pawn = ___pawn;

			SurgeryUnlimitedGameComponent comp = SurgeryUnlimitedGameComponent.Get();
			comp?.Set(pawn, comp.surgeryUnlimitedDefault);
		}
	}

	[HarmonyPatch(typeof(HealthCardUtility), "DrawOverviewTab")]
	public static class SurgergyUnlimitedPawnSettings
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo SetFontInfo = AccessTools.Property(typeof(Text), "Font").GetSetMethod();

			MethodInfo DrawSurgeryOptionInfo = AccessTools.Method(typeof(SurgergyUnlimitedPawnSettings), nameof(SurgergyUnlimitedPawnSettings.DrawSurgeryOption));

			//Insert before
			//IL_0324: ldc.i4.1     
			//IL_0325: call void Verse.Text::set_Font(valuetype Verse.GameFont)
			List<CodeInstruction> list = instructions.ToList();
			for (int i = 0; i < list.Count; i++)
			{
				CodeInstruction inst = list[i];

				if (inst.opcode == OpCodes.Ldc_I4_1 && i + 1 < list.Count &&
					list[i + 1].opcode == OpCodes.Call && list[i + 1].operand == SetFontInfo)//Text.Small
				{
					//Draw pawn surgery option
					//Rect leftRect, Pawn pawn, float curY
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Ldarga_S, 2);
					yield return new CodeInstruction(OpCodes.Call, DrawSurgeryOptionInfo);
				}

				yield return inst;
			}
		}

		public static void DrawSurgeryOption(Rect leftRect, Pawn pawn, ref float curY)
		{
			if (pawn.playerSettings != null && !pawn.Dead && Current.ProgramState == ProgramState.Playing)
			{
				bool selfTend = pawn.playerSettings.selfTend;
				Rect rect2 = new Rect(0f, curY, leftRect.width, 24f);
				SurgeryUnlimitedGameComponent comp = SurgeryUnlimitedGameComponent.Get();
				bool surgeryUnlimited = comp.surgeryUnlimited.Contains(pawn);
				Widgets.CheckboxLabeled(rect2, "TD.PawnSettingSurgeryUnlimited".Translate(), ref surgeryUnlimited);
				comp.Set(pawn, surgeryUnlimited);

				curY += 28f;
			}
		}

	}
}
