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
		public bool downgradeExcessiveMedicine = true;
		public float goodEnoughDowngradeFactor = 1.0f;

		public bool stockUp = true;
		public float stockUpEnough = 1.5f;
		public bool stockUpReturn = false;

		public bool fieldTendingForLackOfBed = false;
		public bool fieldTendingAlways = false;

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
			options.CheckboxLabeled("SettingDoctorInv".Translate(), ref useDoctorMedicine);
			options.CheckboxLabeled("SettingPatientInv".Translate(), ref usePatientMedicine);
			if (useDoctorMedicine || usePatientMedicine)
			{
				options.CheckboxLabeled("SettingNearby".Translate(), ref useCloseMedicine, "SettingNearbyDesc".Translate());
				if (useCloseMedicine)
				{
					options.SliderLabeled("SettingNearbyDist".Translate(), ref distanceToUseEqualOnGround, "SpacesFormat".Translate(), 0, 99, "SettingNearbyDistDesc".Translate());
				}
			}
			options.Gap();


			options.CheckboxLabeled("SettingOtherInv".Translate(), ref useColonistMedicine);
			options.CheckboxLabeled("SettingAnimalInv".Translate(), ref useAnimalMedicine);
			if (useColonistMedicine || useAnimalMedicine)
			{
				options.CheckboxLabeled("SettingOtherAnyDist".Translate(), ref useOtherEvenIfFar, "SettingOtherAnyDistDesc".Translate());
				if (!useOtherEvenIfFar)
					options.SliderLabeled("SettingOtherDist".Translate(), ref distanceToUseFromOther, "SpacesFormat".Translate(), 0, 99);
			}
			options.Gap();


			options.CheckboxLabeled("SettingMinimal".Translate(), ref minimalMedicineForNonUrgent,
				"SettingMinimalDesc".Translate());
			if (minimalMedicineForNonUrgent) noMedicineForNonUrgent = false;

			options.CheckboxLabeled("SettingNoMed".Translate(), ref noMedicineForNonUrgent,
				"SettingNoMedDesc".Translate());
			if (noMedicineForNonUrgent) minimalMedicineForNonUrgent = false;

			options.CheckboxLabeled("SettingDowngrade".Translate(), ref downgradeExcessiveMedicine, "SettingDowngradeDesc".Translate());
			if (downgradeExcessiveMedicine)
			{
				goodEnoughDowngradeFactor *= 100;
				options.SliderLabeled("SettingGoodEnough".Translate(), ref goodEnoughDowngradeFactor, "{0:0}%", 0, 100, "For example, if Herbal Medicine does 90% as good a job as Normal, use Herbal instead");
				goodEnoughDowngradeFactor /= 100;
			}
			options.Gap();

			options.CheckboxLabeled("SettingStockUp".Translate(), ref stockUp);
			options.Label("SettingStockUpDesc".Translate());
			options.SliderLabeled("Stock Up only if there's enough available:", ref stockUpEnough, "{0:P0}", 0, 5, "For example, at 150%, Stockpiles need to have enough for everyone to stock up to 100%, plus 50% extra");
			options.CheckboxLabeled("Return items if not enough available", ref stockUpReturn);
			options.Gap();


			options.CheckboxLabeled("SettingFieldTendingNoBeds".Translate(), ref fieldTendingForLackOfBed, "SettingFieldTendingNoBedsDesc".Translate());
			if (fieldTendingForLackOfBed)
				fieldTendingAlways = false; 

			options.CheckboxLabeled("SettingFieldTendingAlways".Translate(), ref fieldTendingAlways, "SettingFieldTendingAlwaysDesc".Translate());
			if (fieldTendingAlways)
				fieldTendingForLackOfBed = false;

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
			Scribe_Values.Look(ref downgradeExcessiveMedicine, "downgradeExcessiveMedicine", true);
			Scribe_Values.Look(ref goodEnoughDowngradeFactor, "goodEnoughDowngradeFactor", 1.0f);
			
			Scribe_Values.Look(ref stockUp, "stockUp", true);
			Scribe_Values.Look(ref stockUpEnough, "stockUpEnough", 1.5f);
			Scribe_Values.Look(ref stockUpReturn, "stockUpReturn", false);

			Scribe_Values.Look(ref fieldTendingForLackOfBed, "fieldTendingForLackOfBed", false);
			Scribe_Values.Look(ref fieldTendingAlways, "fieldTendingAlways", false);
		}
	}
}