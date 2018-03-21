using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

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

		public bool stockUpOnMedicine = false;
		public int stockUpCapacity = 6;
		//public List<ThingDef> stockUpList;
		public List<int> stockUpListByIndex = new List<int>();

		public bool fieldTendingForLackOfBed = false;
		public bool fieldTendingAlways = false;

		public bool FieldTendingActive(Pawn patient)
		{
			return patient.RaceProps.Humanlike && 
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
			options.CheckboxLabeled("Use medicine from doctor's inventory", ref useDoctorMedicine);
			options.CheckboxLabeled("Use medicine from patient's inventory", ref usePatientMedicine);
			if (useDoctorMedicine || usePatientMedicine)
			{
				options.CheckboxLabeled("... But use nearby medicine (if same quality)", ref useCloseMedicine, "This also checks for medicine on the way to the patient (roughly)");
				if (useCloseMedicine)
				{
					options.SliderLabeled("How far to walk to find nearby medicine", ref distanceToUseEqualOnGround, "{0:0} spaces", 0, 99, "This is only an override for inventory medicine - if there is no inventory medicine, they will walk to the ends of the map to pick up medicine");
				}
			}
			options.Gap();


			options.CheckboxLabeled("Drop medicine from nearby colonist's inventory", ref useColonistMedicine);
			options.CheckboxLabeled("Drop medicine from nearby animal's inventory", ref useAnimalMedicine);
			if (useColonistMedicine || useAnimalMedicine)
			{
				options.CheckboxLabeled("... No matter how far away", ref useOtherEvenIfFar, "Check to use better medicine from someone else far away, but at the cost of walking to it");
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
				options.SliderLabeled("... include medicine that is good enough:", ref goodEnoughDowngradeFactor, "{0:0}%", 0, 100, "For example, if Herbal Medicine does 90% as good a job as Normal, use Herbal instead");
				goodEnoughDowngradeFactor /= 100;
			}
			options.Gap();


			options.CheckboxLabeled("Doctors stock up medicine in their inventory", ref stockUpOnMedicine, "A new job (WorkGiver) for doctors: gather medicine to their inventory, and keep it around for tending");
			if(stockUpOnMedicine)
			{
				options.SliderLabeled("How much medicine to hold:", ref stockUpCapacity, "{0}", 0, 75);
				float iconSize = Text.LineHeight * 2;
				Rect rowIcon = options.GetRect(iconSize);
				Widgets.Label(rowIcon, "Stock up on these Medicines:");

				//foreach (ThingDef td in StockUpUtility.medList)
				for (int i = 0; i < StockUpUtility.medList.Count; i++)
				{
					ThingDef td = StockUpUtility.medList[i];

					//bool included = stockUpList.Contains(td);
					bool included = stockUpListByIndex.Contains(i);
					Rect rectIcon = rowIcon.RightPartPixels(iconSize);
					rowIcon.xMax -= iconSize + 3;
					
					Widgets.DrawHighlightIfMouseover(rectIcon);
					Widgets.ThingIcon(rectIcon, td);
					if (Widgets.ButtonInvisible(rectIcon, false))
					{
						if (included)
							//stockUpList.Add(td);
							stockUpListByIndex.Remove(i);
						else
							//stockUpList.Remove(td);
							stockUpListByIndex.Add(i);
					}
					if (!included) Widgets.DrawTextureFitted(rectIcon, Widgets.CheckboxOffTex, 1.0f);
				}
			}
			options.Gap();


			options.CheckboxLabeled("Doctors will treat patients if no beds are available", ref fieldTendingForLackOfBed, "The patient must be resting, downed, or drafted to target");
			if (fieldTendingForLackOfBed)
				fieldTendingAlways = false; 

			options.CheckboxLabeled("Doctors can always tend, with or without a bed", ref fieldTendingAlways, "Colonists will prioritize going to a bed, but you may draft patients to keep them in place.");
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

			Scribe_Values.Look(ref stockUpOnMedicine, "stockUpOnMedicine", false);
			Scribe_Values.Look(ref stockUpCapacity, "stockUpCapacity", 6);
			//Scribe_Collections.Look(ref stockUpList, "stockUpList");	//why doesn't this work for List<ThingDef>
			Scribe_Collections.Look(ref stockUpListByIndex, "stockUpList");

			Scribe_Values.Look(ref fieldTendingForLackOfBed, "fieldTendingForLackOfBed", false);
			Scribe_Values.Look(ref fieldTendingAlways, "fieldTendingAlways", false);

			if (stockUpListByIndex == null)
				stockUpListByIndex = new List<int>();
		}
	}
}