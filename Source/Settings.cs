using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SmartMedicine
{
	class Settings : ModSettings
	{
		//TODO: save per map
		public bool useDoctorMedicine = true;
		public bool usePatientMedicine = true;
		public bool useColonistMedicine = true;
		public bool useAnimalMedicine = true;
		public bool useCloseMedicine = true;
		public bool useOtherEvenIfFar = true;
		public bool minimalMedicineForNonUrgent = false;
		public bool noMedicineForNonUrgent = false;
		public bool downgradeExcessiveMedicine = true;
		public float goodEnoughDowngradeFactor = 1.0f;
		public int distanceToUseEqualOnGround = 6;
		public int distanceToUseFromOther = 12;


		public static Settings Get()
		{
			return LoadedModManager.GetMod<SmartMedicine.Mod>().GetSettings<Settings>();
		}

		public void DoWindowContents(Rect rect)
		{
			var options = new Listing_Standard();
			options.Begin(rect);
			options.CheckboxLabeled("Use medicine from doctor's inventory", ref useDoctorMedicine);
			options.CheckboxLabeled("Use medicine from patient's inventory", ref usePatientMedicine);
			if (useDoctorMedicine || usePatientMedicine)
			{
				options.CheckboxLabeled("... But use nearby medicine if same quality", ref useCloseMedicine);
				if (useCloseMedicine)
				{
					options.SliderLabeled("How far to walk to find nearby medicine", ref distanceToUseEqualOnGround, "{0:0} spaces", 0, 99);
				}
			}
			options.Gap();

			options.CheckboxLabeled("Drop medicine from nearby colonist's inventory", ref useColonistMedicine);
			options.CheckboxLabeled("Drop medicine from nearby animal's inventory", ref useAnimalMedicine);
			if (useColonistMedicine || useAnimalMedicine)
			{
				options.CheckboxLabeled("... Even if they are far away", ref useOtherEvenIfFar, "Check to use better medicine from someone else, but at the cost of walking to it");
				if (!useOtherEvenIfFar)
					options.SliderLabeled("How far to walk to get it", ref distanceToUseFromOther, "{0:0} spaces", 0, 99);
			}
			options.Gap();

			options.CheckboxLabeled("Use minimal medicine for non-urgent care", ref minimalMedicineForNonUrgent,
				"Urgent care is any disease, or injuries with bleeding, infection chance, or permanent effects - save valuable medicine for these only");
			if (minimalMedicineForNonUrgent) noMedicineForNonUrgent = false;

			options.CheckboxLabeled("No medicine for non-urgent care", ref noMedicineForNonUrgent,
				"Same as above, but without medicine, each injury is treated one at a time");
			if (noMedicineForNonUrgent) minimalMedicineForNonUrgent = false;

			options.CheckboxLabeled("Downgrade medicine if sufficient", ref downgradeExcessiveMedicine, "Calculate if lesser medicine will do just as well, due to doctor skill, bionics, medical beds, etc");
			if (downgradeExcessiveMedicine)
			{
				goodEnoughDowngradeFactor *= 100;
				options.SliderLabeled("... include medicine that is good enough:", ref goodEnoughDowngradeFactor, "{0:0}%", 0, 100);
				goodEnoughDowngradeFactor /= 100;
			}

			options.End();
		}

		public override void ExposeData()
		{
			Scribe_Values.Look(ref useDoctorMedicine, "useDoctorMedicine", true);
			Scribe_Values.Look(ref usePatientMedicine, "usePatientMedicine", true);
			Scribe_Values.Look(ref useColonistMedicine, "useColonistMedicine", true);
			Scribe_Values.Look(ref useAnimalMedicine, "useAnimalMedicine", true);
			Scribe_Values.Look(ref useCloseMedicine, "useCloseMedicine", true);
			Scribe_Values.Look(ref useOtherEvenIfFar, "useOtherEvenIfFar", false);
			Scribe_Values.Look(ref minimalMedicineForNonUrgent, "minimalMedicineForNonUrgent", true);
			Scribe_Values.Look(ref noMedicineForNonUrgent, "noMedicineForNonUrgent", false);
			Scribe_Values.Look(ref downgradeExcessiveMedicine, "downgradeExcessiveMedicine", true);
			Scribe_Values.Look(ref distanceToUseEqualOnGround, "distanceToUseEqualOnGround", 6);
			Scribe_Values.Look(ref distanceToUseFromOther, "distanceToUseFromOther", 12);
			Scribe_Values.Look(ref goodEnoughDowngradeFactor, "goodEnoughDowngradeFactor", 1.0f);
		}
	}
}