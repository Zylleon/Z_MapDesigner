﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Harmony;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld.Planet;

namespace ZMapDesigner
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        public const string Id = "rimworld.zmapdesigner";
        static HarmonyPatches()
        {
            HarmonyInstance harmony = HarmonyInstance.Create("rimworld.zmapdesigner");

            //HarmonyInstance.Create(Id).PatchAll();
            Verse.Log.Message("Map Designer initilized");

            MethodInfo targetmethod = AccessTools.Method(typeof(RimWorld.GenStep_Terrain), "TerrainFrom");
            HarmonyMethod prefixmethod = new HarmonyMethod(typeof(ZMapDesigner.HarmonyPatches).GetMethod("TerrainFrom_Prefix"));
            harmony.Patch(targetmethod, prefixmethod, null);


            harmony.PatchAll();
        }


        public static bool TerrainFrom_Prefix(IntVec3 c, Map map, float elevation, float fertility, RiverMaker river, bool preferSolid, ref TerrainDef __result)
        {
            if (map.Biome.HasModExtension<ZMDBiomeModExtension>())
            {
                if (map.Biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings.mountainSettings != null)
                {
                    __result = Gensteps_Vanilla.MapUtility.TerrainFrom(c, map, elevation, fertility, river, preferSolid);
                    return false;
                }
            }
            return true;
        }
    }



    #region biome map settings

    // adapted from RF-Archipelagos
    [HarmonyPatch(typeof(RimWorld.Planet.World))]
    [HarmonyPatch(nameof(RimWorld.Planet.World.CoastDirectionAt))]
    static class BiomeMapSettings_Beach
    {
        static bool Prefix(int tileID, ref Rot4 __result, ref World __instance)
        {
            var world = Traverse.Create(__instance);
            WorldGrid worldGrid = world.Field("grid").GetValue<WorldGrid>();
            if (!worldGrid[tileID].biome.HasModExtension<ZMDBiomeModExtension>())
            {
                return true;
            }
            if (worldGrid[tileID].biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings.coast)
            {
                return true;
            }
            __result = Rot4.Invalid;
            return false;
        }
    }


    [HarmonyPatch(typeof(RimWorld.Planet.World))]
    [HarmonyPatch(nameof(RimWorld.Planet.World.HasCaves))]
    static class BiomeMapSettings_Caves
    {
        static bool Prefix(int tile, ref bool __result, ref World __instance)
        {
            var world = Traverse.Create(__instance);
            WorldGrid worldGrid = world.Field("grid").GetValue<WorldGrid>();
            if (!worldGrid[tile].biome.HasModExtension<ZMDBiomeModExtension>())
            {
                return true;
            }
            bool? hasCaves = worldGrid[tile].biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings?.caves;
            if (hasCaves == true)
            {
                __result = true;
                return false;
            }
            if (hasCaves == false)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }


    [HarmonyPatch(typeof(RimWorld.GenStep_ElevationFertility))]
    [HarmonyPatch(nameof(RimWorld.GenStep_ElevationFertility.Generate))]
    static class BiomeMapSettings_ElevationFertility
    {
        static bool Prefix(Map map, GenStepParams parms)
        {
            if(map.Biome.HasModExtension<ZMDBiomeModExtension>())
            {
                if(map.Biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings.mountainSettings != null)
                {
                    Gensteps_Vanilla.GenStep_ZMDElevationFertility genStep = new Gensteps_Vanilla.GenStep_ZMDElevationFertility();
                    genStep.Generate(map, parms);
                    return false;
                }
            }
            return true;
        }
    }

    #region ruins

    [HarmonyPatch(typeof(RimWorld.GenStep_ScatterRuinsSimple))]
    [HarmonyPatch(nameof(RimWorld.GenStep_ScatterRuinsSimple.Generate))]
    static class BiomeMapSettings_Ruins
    {
        static bool Prefix(Map map, GenStepParams parms)
        {
            if (map.Biome.HasModExtension<ZMDBiomeModExtension>())
            {
                if (map.Biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings.ruins == false)
                {
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Verse.GenStep_Scatterer))]
    [HarmonyPatch(nameof(Verse.GenStep_Scatterer.Generate))]
    static class Scatterer_WaterBiomeFix                // simple ruins, 1st piece of shrines
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo waterBiome = AccessTools.Field(type: typeof(GenStep_Scatterer), name: nameof(GenStep_Scatterer.allowInWaterBiome));

            CodeInstruction[] codeInstructions = instructions as CodeInstruction[] ?? instructions.ToArray();
            foreach (CodeInstruction instruction in codeInstructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand == waterBiome)
                {
                    yield return new CodeInstruction(opcode: OpCodes.Ldarg_1);
                    yield return new CodeInstruction(opcode: OpCodes.Call, operand: AccessTools.Method(type: typeof(Scatterer_WaterBiomeFix), name: nameof(Scatterer_WaterBiomeFix.AllowedInWaterBiome)));
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        static bool AllowedInWaterBiome(GenStep_Scatterer step, Map map)
        {
            if(map.Biome.HasModExtension<ZMDBiomeModExtension>())
            {
                return true;
            }
            return step.allowInWaterBiome;
        }

    }


    #endregion


    //[HarmonyPatch(typeof(RimWorld.GenStep_RocksFromGrid))]
    //[HarmonyPatch(nameof(RimWorld.GenStep_RocksFromGrid.Generate))]
    //static class RocksFromGrid_WaterBiomeFix
    //{
    //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    //    {
    //        MethodInfo tileInfo = AccessTools.Property(type: typeof(Verse.Map), name: nameof(Verse.Map.TileInfo)).GetGetMethod();
    //        MethodInfo waterCovered = AccessTools.Property(type: typeof(RimWorld.Planet.Tile), name: nameof(RimWorld.Planet.Tile.WaterCovered)).GetGetMethod();

    //        CodeInstruction[] codeInstructions = instructions as CodeInstruction[] ?? instructions.ToArray();
    //        foreach (CodeInstruction instruction in codeInstructions)
    //        {
    //            //if (instruction.opcode == OpCodes.Callvirt && instruction.operand == waterCovered)
    //            if (instruction.opcode == OpCodes.Callvirt)
    //            {
    //                if (instruction.operand == tileInfo)
    //                {
    //                    Log.Message("found tile info");
    //                }
    //                else if (instruction.operand == waterCovered)
    //                {
    //                    Log.Message("found watercovered");
    //                    //yield return instruction;

    //                    //yield return new CodeInstruction(opcode: OpCodes.Ldarg_1);
    //                    yield return new CodeInstruction(opcode: OpCodes.Call, operand: AccessTools.Method(type: typeof(RocksFromGrid_WaterBiomeFix), name: nameof(RocksFromGrid_WaterBiomeFix.IsWaterCovered)));
    //                }
    //                else
    //                {
    //                    yield return instruction;
    //                }
    //            }
    //            else
    //            {
    //                yield return instruction;
    //            }
    //        }
    //    }

    //    static bool IsWaterCovered(GenStep_Scatterer step, Map map)
    //    {
    //        Log.Message("Pretend it's not water covered");
    //        if (map.Biome.HasModExtension<ZMDBiomeModExtension>())
    //        {
    //            return false;
    //        }
    //        return map.TileInfo.WaterCovered;
    //    }
    //}




    [HarmonyPatch(typeof(Verse.GenStep_ScatterThings))]
    [HarmonyPatch(nameof(Verse.GenStep_ScatterThings.Generate))]
    static class SteamGeysers_WaterBiomeFix
    {
        static bool Prefix(Map map, GenStepParams parms)
        {
            if (map.Biome.HasModExtension<ZMDBiomeModExtension>())
            {
                if (map.Biome.GetModExtension<ZMDBiomeModExtension>().biomeMapSettings.geysers == false)
                {
                    return false;
                }
            }
            return true;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo waterBiome = AccessTools.Field(type: typeof(GenStep_Scatterer), name: nameof(GenStep_Scatterer.allowInWaterBiome));

            CodeInstruction[] codeInstructions = instructions as CodeInstruction[] ?? instructions.ToArray();
            foreach (CodeInstruction instruction in codeInstructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand == waterBiome)
                {
                    yield return new CodeInstruction(opcode: OpCodes.Ldarg_1);
                    yield return new CodeInstruction(opcode: OpCodes.Call, operand: AccessTools.Method(type: typeof(Scatterer_WaterBiomeFix), name: nameof(SteamGeysers_WaterBiomeFix.AllowedInWaterBiome)));
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        static bool AllowedInWaterBiome(GenStep_ScatterThings step, Map map)
        {
            if(step.def.defName == "SteamGeysers")
            {
                if (map.Biome.HasModExtension<ZMDBiomeModExtension>())
                {
                    return true;
                }
            }
            return step.allowInWaterBiome;
        }

    }


    #endregion


    #region world map 

    [HarmonyPatch(typeof(RimWorld.Planet.WorldGenStep_Terrain))]
    [HarmonyPatch(nameof(RimWorld.Planet.WorldGenStep_Terrain.GenerateFresh))]
    static class WorldGen_PlaceBiomes
    {
        static void Postfix()
        {
            WorldGrid worldGrid = Find.WorldGrid;
            List<BiomeDef> biomesList = DefDatabase<BiomeDef>.AllDefsListForReading;
            WorldMapSettings settings = new WorldMapSettings();
            //World world = Find.World;

            // foreach biome that has world map settings
            foreach (BiomeDef b in biomesList)
            {
                if(b.HasModExtension<ZMDBiomeModExtension>())
                {
                    if (b.GetModExtension<ZMDBiomeModExtension>().worldMapSettings != null)
                    {
                        settings = b.GetModExtension<ZMDBiomeModExtension>().worldMapSettings;
                        GenWorldMapBiomes.PlaceBiome(b, settings);
                    }
                }
            }
        }
    }
    

    #endregion




    #region debug
    //[HarmonyPatch(typeof(RimWorld.Page_SelectStartingSite))]
    //[HarmonyPatch(nameof(RimWorld.Page_SelectStartingSite.PostOpen))]
    //static class PostOpen
    //{
    //    static bool Prefix()
    //    {
    //        List<Tile> tiles = Find.World.grid.tiles;
    //        List<BiomeDef> biomesList = DefDatabase<BiomeDef>.AllDefsListForReading;

    //    // counts each biome
    //        //foreach (var g in tiles.GroupBy(t => t.biome)
    //        //            .Select(group => new {
    //        //                Biome = group.Key,
    //        //                Count = group.Count()
    //        //            }).OrderBy(y => y.Biome.defName))
    //        //{
    //        //    Log.Message(String.Format("{0} - {1}", g.Biome.defName, g.Count));
    //        //}


    //    //float eleMax = tiles.Max(t => t.elevation);
    //    //float eleMin = tiles.Min(t => t.elevation);
    //    //Log.Message("Elevations: " + eleMin + ", " + eleMax);


    //        return true;
    //    }
    //}

    #endregion

}
