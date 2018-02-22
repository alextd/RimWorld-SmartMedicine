using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using Harmony;
using Verse.AI;


namespace InventoryMedicine
{

    [StaticConstructorOnStartup]
    static class HarmonyPatches
    {
        static float distanceToUseEqualOnGround = 5 * 20.0f; //About 5 squares.

        public static void Message(string x)
        {
#if DEBUG
            Log.Message(x);
#endif
        }


        static HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("uuugggg.rimworld.inventorymedicine.main");

            harmony.Patch(AccessTools.Method(typeof(HealthAIUtility), "FindBestMedicine"),
                new HarmonyMethod(typeof(HarmonyPatches), nameof(FindBestMedicine_Prefix)), null, null);

        }

        private static bool FindBestMedicine_Prefix(Pawn healer, Pawn patient, ref Thing __result)
        {
            if (patient.playerSettings == null || patient.playerSettings.medCare <= MedicalCareCategory.NoMeds)
                return true;

            Thing medicine = FindBestMedicineInInventory(healer, patient);
            Pawn medicineHolder = healer;
            if (medicine == null)
            {
                medicine = FindBestMedicineInInventory(patient, patient);
                medicineHolder = patient;
            }

            TraverseParms traverseParams = TraverseParms.For(healer, Danger.Deadly, TraverseMode.ByPawn, false);
            List<Thing> groundMedicines = healer.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine).Where(t =>
            healer.Map.reachability.CanReach(patient.Position, t, PathEndMode.ClosestTouch, traverseParams) &&
            !t.IsForbidden(healer) && patient.playerSettings.medCare.AllowsMedicine(t.def) && healer.CanReserve(t, 1, -1, null, false)).ToList();

            foreach (Thing t in groundMedicines)
            {
                Message("Ground medicine = " + t + "@" + MedicineQuality(t) + " (" + PathToCost(healer, t) + ")");
            }

            if(medicine == null && groundMedicines.NullOrEmpty())
            {
                float bestQuality = float.MinValue;
                float bestCost = float.MaxValue;
                foreach (Pawn p in healer.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Where(p => p != patient && p != healer)) //p.HostFaction == null
                {
                    Thing pMedicine = FindBestMedicineInInventory(p, patient);
                    if (pMedicine == null) continue;

                    float pQuality = MedicineQuality(pMedicine);
                    float pCost = PathToCost(healer, p);

                    if(pQuality > bestQuality || (pQuality == bestQuality && pCost < bestCost))
                    {
                        medicine = pMedicine;
                        medicineHolder = p;
                        bestQuality = pQuality;
                        bestCost = pCost;
                        //TODO: Have pawn walk to hand off medicine, lol never gonna happen
                    }
                }
            }

            if (medicine != null)
            {
                float medQuality = MedicineQuality(medicine);
                Message("Inventory medicine = " + medicine + "@" + medQuality+", holder = "+ medicineHolder);
                //Higher quality on ground
                Message("checking better on ground");
                if (BetterMedOnGround(groundMedicines, medQuality))
                    return true;

                Message("checking equal near patient");
                //Close enough to patient to let normal thing do it
                if (CloseMedOnGround(groundMedicines, medQuality, patient) != null)
                    return true;

                Message("checking equal near healer");
                //Close enough to just grab
                Thing closeMedicine = CloseMedOnGround(groundMedicines, medQuality, healer);
                if (closeMedicine != null)
                {
                    __result = closeMedicine;
                    return false;
                }

                Message("Using medicine = " + medicine + "@" + medQuality);

                // because The Toil to get this medicine is FailOnDespawnedNullOrForbidden
                // And Medicine in inventory or carried is despawned
                // You can't set the job to use already carried medicine.
                // Editing the toil would be more difficult.
                // But we can drop it so the normal job picks it back it  ¯\_(ツ)_/¯ 

                //Drop it!
                int count = Medicine.GetMedicineCountToFullyHeal(patient);
                if (medicineHolder.carryTracker.CarriedThing != null)
                    count -= medicineHolder.carryTracker.CarriedThing.stackCount;
                count = Mathf.Min(medicine.stackCount, count);
                Thing droppedMedicine;
                medicineHolder.inventory.innerContainer.TryDrop(medicine, ThingPlaceMode.Direct, count, out droppedMedicine);
                __result = droppedMedicine;
                return false;

                //Use it!
                //Message("using inventory " + medicine);
                //healer.carryTracker.innerContainer.TryAddOrTransfer(medicine);
                //__result = medicine;
                //return false;
            }
            return true;
        }

        private static Thing FindBestMedicineInInventory(Pawn pawn, Pawn patient)
        {
            if (pawn == null || pawn.inventory == null || patient == null || patient.playerSettings == null)
                return null;

            return pawn.inventory.innerContainer.InnerListForReading
                .Where(t => t.def.IsMedicine && patient.playerSettings.medCare.AllowsMedicine(t.def))
                .MaxByWithFallback(t => MedicineQuality(t));
        }

        private static float MedicineQuality(Thing t)
        {
            return t.def.GetStatValueAbstract(StatDefOf.MedicalPotency, null);
        }

        private static bool BetterMedOnGround(List<Thing> groundMedicines, float medQuality)
        {
            return groundMedicines.Any(t => MedicineQuality(t) > medQuality);
        }

        private static Thing CloseMedOnGround(List<Thing> groundMedicines, float medQuality, Pawn pawn)
        {
            return groundMedicines.Where(t => MedicineQuality(t) == medQuality && (PathToCost(pawn, t) <= distanceToUseEqualOnGround)).
                MaxByWithFallback(t => PathToCost(pawn, t));
        }

        private static float PathToCost(Pawn p, Thing t)
        {
            PawnPath pawnPath = p.Map.pathFinder.FindPath(p.Position, t, p);
            float cost = pawnPath.TotalCost;
            pawnPath.ReleaseToPool();
            if (cost < 0) return float.MaxValue;
            return cost;
        }
        
    }
}