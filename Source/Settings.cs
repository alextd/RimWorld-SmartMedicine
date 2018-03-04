using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SmartMedicine
{
	class Settings : ModSettings
	{
		//TODO: save per map
		public bool useDoctorMedicine;
		public bool usePatientMedicine;
		public bool useCloseMedicine;
		public int distanceToUseEqualOnGround;

		public bool useColonistMedicine;
		public bool useAnimalMedicine;
		public bool useOtherEvenIfFar;
		public int distanceToUseFromOther;

		public bool minimalMedicineForNonUrgent;
		public bool noMedicineForNonUrgent;
		public bool downgradeExcessiveMedicine;
		public float goodEnoughDowngradeFactor;

		public bool stockUpOnMedicine;
		public int stockUpCapacity;
		//public List<ThingDef> stockUpList;
		public List<int> stockUpListByIndex;

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
					if (Widgets.ButtonImage(rectIcon, td.uiIcon))
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

			Scribe_Values.Look(ref minimalMedicineForNonUrgent, "minimalMedicineForNonUrgent", true);
			Scribe_Values.Look(ref noMedicineForNonUrgent, "noMedicineForNonUrgent", false);
			Scribe_Values.Look(ref downgradeExcessiveMedicine, "downgradeExcessiveMedicine", true);
			Scribe_Values.Look(ref goodEnoughDowngradeFactor, "goodEnoughDowngradeFactor", 0.9f);

			Scribe_Values.Look(ref stockUpOnMedicine, "stockUpOnMedicine", false);
			Scribe_Values.Look(ref stockUpCapacity, "stockUpCapacity", 10);
			//Scribe_Collections.Look(ref stockUpList, "stockUpList");	//why doesn't this work for List<ThingDef>
			Scribe_Collections.Look(ref stockUpListByIndex, "stockUpList");

			if (stockUpListByIndex == null)
				stockUpListByIndex = new List<int>();
		}
	}
}