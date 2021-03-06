﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.BaseGen;
using Verse;


namespace ZMapDesigner.GenSteps_ZMD
{
    public class CenterShapeIrregular : DefModExtension
    {
        public bool enabled = false;
        public int irregularity = 6;
        public bool isCrescent = false;
    }


    /// <summary>
    /// based on FertilityElevation and probably needs to be linked there via patch
    /// </summary>
    public class GenStep_CenterShapeIrregular : GenStep
    {
        public override int SeedPart
        {
            get
            {
                return 2115796768;
            }
        }

        // allowed sizes for the little circles that get piled to make the landmass
        public IntRange SizeRange = new IntRange(3, 16);

        // used for the total size of the main ellipse. These numbers work well for the default map size, but are adjusted to actual map size before use
        private IntRange totalRange = new IntRange(100, 150);

        public int totalDist;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!map.Biome.HasModExtension<CenterShapeIrregular>())
            {
                return;
            }
            if (!map.Biome.GetModExtension<CenterShapeIrregular>().enabled)
            {
                return;
            }

            MapGenFloatGrid fertility = MapGenerator.Fertility;

            // make ellipse centered on the map's center. Smaller maps get smaller ellipses
            IntRange NoiseRange = new IntRange(-50, 50);
            NoiseRange.min = (int)(0 - 0.15 * map.Size.x);
            NoiseRange.max = (int)(0.15 * map.Size.x);

            IntVec3 mapCenter = map.Center;
            int x = NoiseRange.RandomInRange;
            int z = NoiseRange.RandomInRange;
            IntVec3 focus1 = mapCenter;
            focus1.x += x;
            focus1.z += z;
            IntVec3 focus2 = mapCenter;
            focus2.x -= x;
            focus2.z -= z;

            SizeRange.max = 5 + map.Size.x / 20;
            totalRange.max = (int)(Math.Min(0.6 * map.Size.x, map.Size.x - (3 * SizeRange.max)));
            totalRange.min = Math.Max((int)(0.45 * map.Size.x), SizeRange.min + (int)GeometryUtility.DistanceBetweenPoints(focus1, focus2));

            totalDist = totalRange.RandomInRange;

            List<IntVec3> shape = GeometryUtility.MakeEllipse(focus1, focus2, totalDist, map);
            // generate random circles and cut them out of the ellipse, so that the final shape is squigglier
            // some circles won't overlap with the main shape, so maps can end up either really squiggly or almost perfectly elliptical. Variety!
            List<IntVec3> originalShape = shape;
            IntRange cutoutSizes = new IntRange(Math.Min(totalRange.min / 8, 56), Math.Min(totalRange.max / 3, 56));

            if(map.Biome.GetModExtension<CenterShapeIrregular>().isCrescent)
            {
                float ellipseRadius = 0.5f * (float)Math.Sqrt(Math.Pow(totalDist, 2) - Math.Pow(GeometryUtility.DistanceBetweenPoints(focus1, focus2), 2));
                Log.Message("ellipseRadius = " + ellipseRadius);
                IEnumerable<IntVec3> donut = GeometryUtility.MakeCircleAround(mapCenter, ellipseRadius, map).Except(GeometryUtility.MakeCircleAround(mapCenter, 0.5f * ellipseRadius, map));
                IntVec3 lagoonCenter = donut.RandomElement();
                float lagoonDist = GeometryUtility.DistanceBetweenPoints(lagoonCenter, mapCenter);
                FloatRange lagoonSizeRange = new FloatRange(0.75f * lagoonDist, lagoonDist + 0.2f * ellipseRadius);
                shape = shape.Except(GeometryUtility.MakeCircleAround(lagoonCenter, lagoonSizeRange.RandomInRange, map)).ToList();
            }
            else
            {
                Log.Message("Squiggly version");
                int numCutouts = map.Biome.GetModExtension<CenterShapeIrregular>().irregularity;
                for (int i = 0; i < numCutouts; i++)
                {
                    IntVec3 tempCenter = map.AllCells.RandomElement();

                    if (!originalShape.Contains(tempCenter) && shape.Count >= 0.75 * originalShape.Count)
                    {
                        shape = shape.Except(GenRadial.RadialCellsAround(tempCenter, cutoutSizes.RandomInRange, true)).ToList();
                    }
                }
            }

            Makelandmass(shape, ref fertility, map);
            SetNewTerrains(map);

            //GenStep_OceanRockChunks genStep = new GenStep_OceanRockChunks();
            //genStep.Generate(map, parms);
            //GenStep_OceanDeepResources deepLumps = new GenStep_OceanDeepResources();
            //deepLumps.Generate(map, parms);
        }

        /// <summary>
        /// Assigns terrains to the finished fertility bump map, using the terrainsByFertility list from BiomeDef in xml
        /// </summary>
        private void SetNewTerrains(Map map)
        {
            TerrainGrid terrainGrid = map.terrainGrid;
            MapGenFloatGrid elevation = MapGenerator.Elevation;
            MapGenFloatGrid fertility = MapGenerator.Fertility;

            foreach (IntVec3 current in map.AllCells)
            {

                TerrainDef terrainDef;

                terrainDef = this.TerrainFrom(current, map, elevation[current], fertility[current], false);


                terrainGrid.SetTerrain(current, terrainDef);
            }

        }

        /// <summary>
        /// Makes the landmass shape on the map's fertility grid. 
        /// Generates randomly-sized circles on each tile in the main shape and increments fertility in each circle.
        /// The circles "pile" up, resulting in a bumpy, irregular landmass with the same overall shape as the initial landCells list
        /// </summary>
        private void Makelandmass(List<IntVec3> landCells, ref MapGenFloatGrid fertility, Map map)
        {
            // smaller shapes get higher increments. For atolls, this lets smaller "hills" get tall enough to actually spawn water at the peak
            float fertIncrement = 0.06f;

            if (totalDist < 90)
            {
                fertIncrement = 0.09f;
            }
            if (totalDist < 70)
            {
                fertIncrement = 0.13f;
            }
            if (totalDist < 55)
            {
                fertIncrement = 0.2f;
            }
            if (totalDist < 30)
            {
                fertIncrement = 0.3f;
            }

            foreach (IntVec3 a in landCells)
            {
                if (Rand.Bool)
                {
                    IntVec3 ne = a;

                    List<IntVec3> island = new List<IntVec3>();

                    // Small chance of bigger circles. Helps the overall shape to look more natural.
                    // For atolls, this helps to flatten/ spread out the sand and shallow water around the island. 
                    if (Rand.Chance(0.3f))
                    {
                        if (Rand.Chance(0.2f))
                        {
                            island = GenRadial.RadialCellsAround(ne, Math.Min(56, 3.5f * SizeRange.RandomInRange), true).ToList();

                        }
                        else
                        {
                            island = GenRadial.RadialCellsAround(ne, Math.Min(56, 2.0f * SizeRange.RandomInRange), true).ToList();
                        }
                    }
                    else
                    {
                        island = GenRadial.RadialCellsAround(ne, Math.Min(56, SizeRange.RandomInRange), true).ToList();
                    }

                    foreach (IntVec3 groundTile in island)
                    {
                        if (groundTile.InBounds(map))
                        {
                            fertility[groundTile] += fertIncrement;
                        }
                    }
                }

            }
        }

        
        /// <summary>
        /// Finds the actual terrain types. 
        /// Copied from Vanilla, probably needs heavy cleanup
        /// </summary>
        private TerrainDef TerrainFrom(IntVec3 c, Map map, float elevation, float fertility, bool preferSolid)
        {
            TerrainDef terrainDef = null;

            if (terrainDef == null && preferSolid)
            {
                return GenStep_RocksFromGrid.RockDefAt(c).building.naturalTerrain;
            }
            TerrainDef terrainDef2 = BeachMaker.BeachTerrainAt(c, map.Biome);
            if (terrainDef2 == TerrainDefOf.WaterOceanDeep)
            {
                return terrainDef2;
            }
            if (terrainDef != null && terrainDef.IsRiver)
            {
                return terrainDef;
            }
            if (terrainDef2 != null)
            {
                return terrainDef2;
            }
            if (terrainDef != null)
            {
                return terrainDef;
            }
            for (int i = 0; i < map.Biome.terrainPatchMakers.Count; i++)
            {
                terrainDef2 = map.Biome.terrainPatchMakers[i].TerrainAt(c, map, fertility);
                if (terrainDef2 != null)
                {
                    return terrainDef2;
                }
            }
            if (elevation > 0.55f && elevation < 0.61f)
            {
                return TerrainDefOf.Gravel;
            }
            if (elevation >= 0.61f)
            {
                return GenStep_RocksFromGrid.RockDefAt(c).building.naturalTerrain;
            }
            terrainDef2 = TerrainThreshold.TerrainAtValue(map.Biome.terrainsByFertility, fertility);
            if (terrainDef2 != null)
            {
                return terrainDef2;
            }

            return TerrainDefOf.Sand;
        }

    }
}

