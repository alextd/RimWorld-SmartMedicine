using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Harmony;


namespace InventoryMedicine
{

    static class Log
    {
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Message(string x)
        {
            Verse.Log.Message(x);
        }
    }
    
    [HarmonyPatch(typeof(Medicine))]
    [HarmonyPatch("GetMedicineCountToFullyHeal")]
    static class GetMedicineCountToFullyHeal
    {
        //Insert FilterForUrgentHediffs when counting needed medicine
        public static IEnumerable<CodeInstruction> Transpiler(MethodBase mBase, IEnumerable<CodeInstruction> instructions)
        {
            foreach(LocalVariableInfo info in mBase.GetMethodBody().LocalVariables)
            {
                Log.Message(info.ToString());
            }

            MethodInfo SortByTendPriorityInfo = AccessTools.Method(
                typeof(TendUtility), nameof(TendUtility.SortByTendPriority));
            MethodInfo filterMethodInfo = AccessTools.Method(
                typeof(GetMedicineCountToFullyHeal), nameof(FilterForUrgentInjuries));

            List<CodeInstruction> instructionList = instructions.ToList();
            for (int i = 0; i < instructionList.Count; i++)
            {
                CodeInstruction instruction = instructionList[i];
                if (instruction.opcode == OpCodes.Ldsfld)
                {
                    CodeInstruction lastLoad = new CodeInstruction(OpCodes.Ldsfld, instruction.operand);

                    i++;
                    CodeInstruction nextInstruction = instructionList[i];
                    if (nextInstruction.opcode == OpCodes.Call && nextInstruction.operand == SortByTendPriorityInfo)
                    {
                        //insert before the sort call
                        yield return lastLoad;
                        yield return new CodeInstruction(OpCodes.Call, filterMethodInfo);
                    }
                    yield return instruction;
                    yield return nextInstruction;
                }
                else
                    yield return instruction;
            }
        }

        //Filter time-sensitive injuries
        public static void FilterForUrgentInjuries(List<Hediff> hediffs)
        {
            int removed = hediffs.RemoveAll(h => !(!(h is Hediff_Injury) || (h as Hediff_Injury).Bleeding
                || (h as Hediff_Injury).TryGetComp<HediffComp_Infecter>() != null
                || (h as Hediff_Injury).TryGetComp<HediffComp_GetsOld>() != null));
        }

        //Filter for medicine unneeded 


    }

    [HarmonyPatch(typeof(HealthAIUtility))]
    [HarmonyPatch("FindBestMedicine")]
    static class FindBestMedicine
    {
        private static bool Prefix(Pawn healer, Pawn patient, ref Thing __result)
        {
            if (patient.playerSettings == null || patient.playerSettings.medCare <= MedicalCareCategory.NoMeds
                || !healer.Faction.IsPlayer)
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
                Log.Message("Ground medicine = " + t + "@" + MedicineQuality(t) + " (dist: " + DistanceTo(healer, t) + ")");
            }

            if(medicine == null && groundMedicines.NullOrEmpty())
            {
                float bestQuality = float.MinValue;
                int bestCost = int.MaxValue;
                foreach (Pawn p in healer.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Where(p => p != patient && p != healer
                && healer.Map.reachability.CanReach(patient.Position, p, PathEndMode.ClosestTouch, traverseParams)))
                {
                    Thing pMedicine = FindBestMedicineInInventory(p, patient);
                    if (pMedicine == null) continue;

                    float pQuality = MedicineQuality(pMedicine);
                    int pCost = DistanceTo(healer, p);

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
                Log.Message("Inventory medicine = " + medicine + "@" + medQuality+", holder = "+ medicineHolder);

                //Higher quality on ground
                Log.Message("checking better on ground");
                if (BetterMedOnGround(groundMedicines, medQuality))
                    return true;

                Log.Message("checking equal near patient");
                //Close enough to patient to let normal thing do it
                if (CloseMedOnGround(groundMedicines, medQuality, patient) != null)
                    return true;

                Log.Message("checking equal near healer");
                //Close enough to just grab
                Thing closeMedicine = CloseMedOnGround(groundMedicines, medQuality, healer);
                if (closeMedicine != null)
                {
                    __result = closeMedicine;
                    return false;
                }

                Log.Message("Using medicine = " + medicine + "@" + medQuality);

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
                //Log.Message("using inventory " + medicine);
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
            if (groundMedicines.Count == 0)
                return null;

            Thing closeMed = groundMedicines.Where(t => MedicineQuality(t) == medQuality).MinBy(t => DistanceTo(pawn, t));
            if (DistanceTo(pawn, closeMed) <= Settings.Get().distanceToUseEqualOnGround)
                return closeMed;

            return null;
        }

        private static int DistanceTo(Thing t1, Thing t2)
        {
            return (t1.Position - t2.Position).LengthManhattan;
        }
        
    }
}