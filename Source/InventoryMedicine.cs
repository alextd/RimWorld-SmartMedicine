using System.Reflection;
using Verse;
using UnityEngine;
using Harmony;

namespace InventoryMedicine
{
    public class Mod : Verse.Mod
    {
        public Mod(ModContentPack content) : base(content)
        {
            // initialize settings
            GetSettings<Settings>();

            // prefix implied PawnColumnWorker_WorkType generation 
            // prefix get/set workPriorities
#if DEBUG
            HarmonyInstance.DEBUG = true;
#endif
            HarmonyInstance harmony = HarmonyInstance.Create("uuugggg.rimworld.inventorymedicine.main");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            GetSettings<Settings>().DoWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Smart Medicine";
        }
    }
}