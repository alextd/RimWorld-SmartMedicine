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
	public class SurgeryUnlimitedGameComponent : GameComponent
	{
		public bool surgeryUnlimited;

		public SurgeryUnlimitedGameComponent(Game game) { }

		public static SurgeryUnlimitedGameComponent Get()
		{
			return Current.Game.GetComponent<SurgeryUnlimitedGameComponent>();
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref surgeryUnlimited, "surgergyUnlimited");
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
			Widgets.CheckboxLabeled(botRect, "Surgery uses best medicine available", ref SurgeryUnlimitedGameComponent.Get().surgeryUnlimited);
		}
	}

	[HarmonyPatch(typeof(WorkGiver_DoBill), "GetMedicalCareCategory")]
	static class SurgeryUnlimited
	{
		//private static MedicalCareCategory GetMedicalCareCategory(Thing billGiver)
		public static void Postfix(ref MedicalCareCategory __result)
		{
			if (!SurgeryUnlimitedGameComponent.Get().surgeryUnlimited) return;

			__result = MedicalCareCategory.Best;
		}
	}
}
