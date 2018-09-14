using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using TD.Utilities;

namespace SmartMedicine
{
	class Settings : ModSettings
	{
		//TODO: save per map
		public bool useDoctorMedicine = true;
		public bool usePatientMedicine = true;
		public bool useCloseMedicine = true;
		public int distanceToUseEqualOnGround = 6;

		public bool useColonistMedicine = true;
		public bool useAnimalMedicine = true;
		public bool useOtherEvenIfFar = false;
		public int distanceToUseFromOther = 12;

		public bool minimalMedicineForNonUrgent = false;
		public bool noMedicineForNonUrgent = false;

		public bool stockUp = true;
		public float stockUpEnough = 1.5f;
		public bool stockUpReturn = false;

		public bool fieldTendingForLackOfBed = false;
		public bool fieldTendingAlways = false;

		public bool defaultUnlimitedSurgery = false;

		public bool FieldTendingActive(Pawn patient)
		{
			return patient.IsFreeColonist && 
				(fieldTendingAlways || 
				(fieldTendingForLackOfBed && RestUtility.FindPatientBedFor(patient) == null));
		}

		public static Settings Get()
		{
			return LoadedModManager.GetMod<SmartMedicine.Mod>().GetSettings<Settings>();
		}

		public void DoWindowContents(Rect wrect)
		{
			var options = new Listing_Standard();
			options.Begin(wrect);
			options.CheckboxLabeled("TD.SettingDoctorInv".Translate(), ref useDoctorMedicine);
			options.CheckboxLabeled("TD.SettingPatientInv".Translate(), ref usePatientMedicine);
			if (useDoctorMedicine || usePatientMedicine)
			{
				options.CheckboxLabeled("TD.SettingNearby".Translate(), ref useCloseMedicine, "TD.SettingNearbyDesc".Translate());
				if (useCloseMedicine)
				{
					options.SliderLabeled("TD.SettingNearbyDist".Translate(), ref distanceToUseEqualOnGround, "TD.SpacesFormat".Translate(), 0, 99, "TD.SettingNearbyDistDesc".Translate());
				}
			}
			options.Gap();


			options.CheckboxLabeled("TD.SettingOtherInv".Translate(), ref useColonistMedicine);
			options.CheckboxLabeled("TD.SettingAnimalInv".Translate(), ref useAnimalMedicine);
			if (useColonistMedicine || useAnimalMedicine)
			{
				options.CheckboxLabeled("TD.SettingOtherAnyDist".Translate(), ref useOtherEvenIfFar, "TD.SettingOtherAnyDistDesc".Translate());
				if (!useOtherEvenIfFar)
					options.SliderLabeled("TD.SettingOtherDist".Translate(), ref distanceToUseFromOther, "TD.SpacesFormat".Translate(), 0, 99);
			}
			options.Gap();


			options.CheckboxLabeled("TD.SettingMinimal".Translate(), ref minimalMedicineForNonUrgent,
				"TD.SettingMinimalDesc".Translate());
			if (minimalMedicineForNonUrgent) noMedicineForNonUrgent = false;

			options.CheckboxLabeled("TD.SettingNoMed".Translate(), ref noMedicineForNonUrgent,
				"TD.SettingNoMedDesc".Translate());
			if (noMedicineForNonUrgent) minimalMedicineForNonUrgent = false;
			options.Gap();

			options.CheckboxLabeled("TD.SettingStockUp".Translate(), ref stockUp);
			options.Label("TD.SettingStockUpDesc".Translate());
			options.SliderLabeled("TD.SettingStockUpEnough".Translate(), ref stockUpEnough, "{0:P0}", 0, 5, "TD.SettingStockUpEnoughDesc".Translate());
			options.CheckboxLabeled("TD.SettingStockUpReturn".Translate(), ref stockUpReturn);
			options.Gap();


			options.CheckboxLabeled("TD.SettingFieldTendingNoBeds".Translate(), ref fieldTendingForLackOfBed, "TD.SettingFieldTendingNoBedsDesc".Translate());
			if (fieldTendingForLackOfBed)
				fieldTendingAlways = false; 

			options.CheckboxLabeled("TD.SettingFieldTendingAlways".Translate(), ref fieldTendingAlways, "TD.SettingFieldTendingAlwaysDesc".Translate());
			if (fieldTendingAlways)
				fieldTendingForLackOfBed = false;
			options.Gap();

			options.CheckboxLabeled("TD.SettingGlobalSurgeryUnlimited".Translate(), ref defaultUnlimitedSurgery);

			options.End();
		}
		
		public override void ExposeData()
		{
			Scribe_Values.Look(ref useDoctorMedicine, "useDoctorMedicine", true);
			Scribe_Values.Look(ref usePatientMedicine, "usePatientMedicine", true);
			Scribe_Values.Look(ref useCloseMedicine, "useCloseMedicine", true);
			Scribe_Values.Look(ref distanceToUseEqualOnGround, "distanceToUseEqualOnGround", 6);

			Scribe_Values.Look(ref useColonistMedicine, "useColonistMedicine", true);
			Scribe_Values.Look(ref useAnimalMedicine, "useAnimalMedicine", true);
			Scribe_Values.Look(ref useOtherEvenIfFar, "useOtherEvenIfFar", false);
			Scribe_Values.Look(ref distanceToUseFromOther, "distanceToUseFromOther", 12);

			Scribe_Values.Look(ref minimalMedicineForNonUrgent, "minimalMedicineForNonUrgent", false);
			Scribe_Values.Look(ref noMedicineForNonUrgent, "noMedicineForNonUrgent", false);
			
			Scribe_Values.Look(ref stockUp, "stockUp", true);
			Scribe_Values.Look(ref stockUpEnough, "stockUpEnough", 1.5f);
			Scribe_Values.Look(ref stockUpReturn, "stockUpReturn", false);

			Scribe_Values.Look(ref fieldTendingForLackOfBed, "fieldTendingForLackOfBed", false);
			Scribe_Values.Look(ref fieldTendingAlways, "fieldTendingAlways", false);
			Scribe_Values.Look(ref defaultUnlimitedSurgery, "defaultUnlimitedSurgery", false);
		}
	}
}