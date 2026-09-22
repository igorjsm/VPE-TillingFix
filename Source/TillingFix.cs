using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace VPETillingFix
{
    /// <summary>
    /// VPE's tilled soil is a terrain "constructed" through vanilla's JobDriver_ConstructFinishFrame.
    /// VEF already redirects its work type, skill requirement and XP to Plants, but the per-tick work
    /// still reads StatDefOf.ConstructionSpeed. This transpiles that tick action so that, for tilled
    /// soil frames only, the work speed comes from PlantWorkSpeed and no stuff factor is applied.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TillingFix
    {
        private const string LogPrefix = "[VPE Tilling Fix] ";
        private const string TilledSoilDefName = "VCE_TilledSoil";

        private static readonly TerrainDef tilledSoil;

        // Filled by the transpiler, which Harmony may rerun whenever another mod patches the same method.
        private static int speedStatsReplaced;
        private static int stuffReadsReplaced;

        static TillingFix()
        {
            tilledSoil = DefDatabase<TerrainDef>.GetNamedSilentFail(TilledSoilDefName);
            if (tilledSoil == null)
            {
                Log.Warning(LogPrefix + $"TerrainDef '{TilledSoilDefName}' not found. Is Vanilla Plants Expanded active? Patch not applied.");
                return;
            }

            List<MethodInfo> targets = FindTickActions();
            if (targets.Count == 0)
            {
                Log.Warning(LogPrefix + "Could not find the construction tick action in JobDriver_ConstructFinishFrame. Patch not applied.");
                return;
            }

            var harmony = new Harmony("igorjsm.VPETillingFix");
            // Run before other transpilers: Vanilla Skills Expanded replaces the ConstructionSpeed read with
            // a call to its own StatUtility.ConstructionStatForFrame (VSE_FloorSpeed for terrain). Going first
            // lets us see the original ldsfld, and our interceptor then receives whatever stat they produce.
            var transpiler = new HarmonyMethod(typeof(TillingFix), nameof(Transpiler)) { priority = Priority.First };
            foreach (MethodInfo target in targets)
            {
                try
                {
                    speedStatsReplaced = 0;
                    stuffReadsReplaced = 0;
                    harmony.Patch(target, transpiler: transpiler);
                }
                catch (Exception e)
                {
                    Log.Error(LogPrefix + $"Failed to patch {target.DeclaringType?.Name}.{target.Name}: {e}");
                    continue;
                }

                if (speedStatsReplaced == 0)
                {
                    IEnumerable<string> others = Harmony.GetPatchInfo(target)?.Transpilers
                        .Where(p => p.owner != harmony.Id)
                        .Select(p => $"{p.owner} (priority {p.priority})") ?? Enumerable.Empty<string>();
                    Log.Warning(LogPrefix + $"{target.DeclaringType?.Name}.{target.Name} was patched but no ConstructionSpeed read was replaced. Other transpilers on it: {string.Join(", ", others)}");
                    continue;
                }

                Log.Message(LogPrefix + $"Tilling now uses PlantWorkSpeed ({speedStatsReplaced} speed stat read(s) and {stuffReadsReplaced} stuff read(s) redirected in {target.DeclaringType?.Name}.{target.Name}).");
            }
        }

        /// <summary>The compiler-generated lambda(s) of MakeNewToils that read ConstructionSpeed.</summary>
        private static List<MethodInfo> FindTickActions()
        {
            FieldInfo constructionSpeed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.ConstructionSpeed));
            var result = new List<MethodInfo>();
            foreach (Type nested in typeof(JobDriver_ConstructFinishFrame).GetNestedTypes(AccessTools.all))
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(nested))
                {
                    if (method.IsAbstract || method.GetMethodBody() == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (PatchProcessor.ReadMethodBody(method).Any(op => Equals(op.Value, constructionSpeed)))
                        {
                            result.Add(method);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning(LogPrefix + $"Could not read {nested.Name}.{method.Name}: {e.Message}");
                    }
                }
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            List<CodeInstruction> codes = instructions.ToList();

            // The lambda lives in a closure class that holds the job driver in "<>4__this".
            FieldInfo driverField = AccessTools.Field(original.DeclaringType, "<>4__this");
            if (driverField == null || driverField.FieldType != typeof(JobDriver_ConstructFinishFrame))
            {
                Log.Warning(LogPrefix + $"{original.DeclaringType?.Name} has no job driver field; leaving it unchanged.");
                return codes;
            }

            FieldInfo constructionSpeed = AccessTools.Field(typeof(StatDefOf), nameof(StatDefOf.ConstructionSpeed));
            MethodInfo getStuff = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Stuff));
            MethodInfo speedStatFor = AccessTools.Method(typeof(TillingFix), nameof(SpeedStatFor));
            MethodInfo stuffFor = AccessTools.Method(typeof(TillingFix), nameof(StuffFor));

            var result = new List<CodeInstruction>(codes.Count + 12);
            foreach (CodeInstruction code in codes)
            {
                result.Add(code);
                MethodInfo interceptor = null;
                if (code.LoadsField(constructionSpeed))
                {
                    interceptor = speedStatFor;
                    speedStatsReplaced++;
                }
                else if (code.Calls(getStuff))
                {
                    interceptor = stuffFor;
                    stuffReadsReplaced++;
                }

                if (interceptor != null)
                {
                    // value on stack -> interceptor(value, this.<>4__this)
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Ldfld, driverField));
                    result.Add(new CodeInstruction(OpCodes.Call, interceptor));
                }
            }
            return result;
        }

        public static StatDef SpeedStatFor(StatDef original, JobDriver_ConstructFinishFrame driver)
        {
            return IsTilling(driver) ? StatDefOf.PlantWorkSpeed : original;
        }

        public static ThingDef StuffFor(ThingDef stuff, JobDriver_ConstructFinishFrame driver)
        {
            return IsTilling(driver) ? null : stuff;
        }

        private static bool IsTilling(JobDriver_ConstructFinishFrame driver)
        {
            return driver?.job?.targetA.Thing is Frame frame && frame.def.entityDefToBuild == tilledSoil;
        }
    }
}
