using HarmonyLib;
using Verse;

namespace Soyuz.Patches
{
    [HarmonyPatch(typeof(Hediff_Pregnant), nameof(Hediff_Pregnant.Tick))]
    public class Hediff_Pregnant_Tick_Patch
    {
        public static void Prefix(Hediff_Pregnant __instance)
        {
            var pawn = __instance.pawn;
            if (true
                && pawn.IsSkippingTicks()
                && pawn.IsValidWildlifeOrWorldPawn())
            {
                int deltaT = pawn.GetDeltaT();
                __instance.ageTicks += deltaT - 1;
                __instance.GestationProgress += deltaT / (pawn.RaceProps.gestationPeriodDays * 60000f);
            }
        }
    }
}