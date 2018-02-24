using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace InventoryMedicine
{
    class Settings : ModSettings
    {
        //TODO: save per map
        public bool useDoctorMedicine = true;
        public bool usePatientMedicine = true;
        public bool useColonistMedicine = true;
        public bool useAnimalMedicine = true;
        public bool useCloseMedicine = true;
        public bool useOtherOnlyIfNeeded = true;
        public bool minimalMedicineForNonUrgent = false;
        public bool noMedicineForNonUrgent = true;
        public bool downgradeExcessiveMedicine = true;
        public int distanceToUseEqualOnGround = 5;

        private string dummy;


        public static Settings Get()
        {
            return LoadedModManager.GetMod<InventoryMedicine.Mod>().GetSettings<Settings>();
        }

        //// buffers;

        public void DoWindowContents(Rect rect)
        {
            var options = new Listing_Standard();
            options.Begin(rect);
            //TODO: Find a better GUI for this textbox
            //options.TextFieldNumericLabeled<int>("How far to walk to find nearby medicine", ref distanceToUseEqualOnGround, ref dummy, 0, 999);
            options.CheckboxLabeled("Use medicine from doctor's inventory", ref useDoctorMedicine);
            options.CheckboxLabeled("Use medicine from patient's inventory", ref usePatientMedicine);
            options.CheckboxLabeled("Drop medicine from nearby colonist's inventory", ref useColonistMedicine);
            options.CheckboxLabeled("Drop medicine from nearby animal's inventory", ref useCloseMedicine);
            options.CheckboxLabeled("... But only if nothing is available", ref useOtherOnlyIfNeeded);

            options.CheckboxLabeled("Use minimal medicine for non-urgent care", ref minimalMedicineForNonUrgent,
                "Urgent care is injuries with bleeding, infection chance, or permanent effects");
            if (minimalMedicineForNonUrgent) noMedicineForNonUrgent = false;

            options.CheckboxLabeled("No medicine for non-urgent care", ref noMedicineForNonUrgent,
                "Same as above, but without medicine, each injury is healed one at a time");
            if (noMedicineForNonUrgent) minimalMedicineForNonUrgent = false;

            options.CheckboxLabeled("Downgrade medicine if equal", ref downgradeExcessiveMedicine, "Calculate if lesser medicine will do just as well, due to doctor skill, bionics, medical beds, etc");

            options.End();
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref useDoctorMedicine, "useDoctorMedicine", true);
            Scribe_Values.Look(ref usePatientMedicine, "usePatientMedicine", true);
            Scribe_Values.Look(ref useColonistMedicine, "useColonistMedicine", true);
            Scribe_Values.Look(ref useAnimalMedicine, "useAnimalMedicine", true);
            Scribe_Values.Look(ref useCloseMedicine, "useCloseMedicine", true);
            Scribe_Values.Look(ref useOtherOnlyIfNeeded, "useOtherOnlyIfNeeded ", true); 
            Scribe_Values.Look(ref minimalMedicineForNonUrgent, "minimalMedicineForNonUrgent", false);
            Scribe_Values.Look(ref noMedicineForNonUrgent, "noMedicineForNonUrgent", true);
            Scribe_Values.Look(ref downgradeExcessiveMedicine, "downgradeExcessiveMedicine", true);
            Scribe_Values.Look(ref distanceToUseEqualOnGround, "distanceToUseEqualOnGround", 5);
        }

    }
}
