using System.Reflection;
using Verse;
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
    }
}