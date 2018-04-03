﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using UnityEngine;
using Harmony;
using RimWorld;

namespace SmartMedicine
{
	public class Mod : Verse.Mod
	{
		public Mod(ModContentPack content) : base(content)
		{
			// initialize settings
			GetSettings<Settings>();
#if DEBUG
			HarmonyInstance.DEBUG = true;
#endif
			HarmonyInstance harmony = HarmonyInstance.Create("uuugggg.rimworld.SmartMedicine.main");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			{
				Type nestedType = AccessTools.Inner(typeof(Toils_Tend), "<PickupMedicine>c__AnonStorey0");
				harmony.Patch(AccessTools.Method(nestedType, "<>m__0"),
					null, null, new HarmonyMethod(typeof(PickupMedicine_Patch), "Transpiler"));
			}

			{
				Type nestedType = AccessTools.Inner(typeof(JobDriver_TendPatient), "<MakeNewToils>c__Iterator0");
				nestedType = AccessTools.Inner(nestedType, "<MakeNewToils>c__AnonStorey1");
				harmony.Patch(AccessTools.Method(nestedType, "<>m__2"),
					null, null, new HarmonyMethod(typeof(MakeNewToils_Patch), "Transpiler"));
			}
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			base.DoSettingsWindowContents(inRect);
			GetSettings<Settings>().DoWindowContents(inRect);
		}

		public override string SettingsCategory()
		{
			return "SmartMedicine".Translate();
		}
	}
}