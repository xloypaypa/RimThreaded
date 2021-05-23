﻿using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace RimThreaded
{

    public class PathFinder_Patch
    {
        [ThreadStatic] public static List<int> disallowedCornerIndices;
        [ThreadStatic] public static PathFinderNodeFast2[] calcGrid;
        [ThreadStatic] public static FastPriorityQueue<CostNode2> openList;
        [ThreadStatic] public static ushort statusOpenValue;
        [ThreadStatic] public static ushort statusClosedValue;
        [ThreadStatic] public static Dictionary<PathFinder, RegionCostCalculatorWrapper> regionCostCalculatorDict;
        
        public static void InitializeThreadStatics()
        {
            openList = new FastPriorityQueue<CostNode2>(new CostNodeComparer2());
            statusOpenValue = 1;
            statusClosedValue = 2;
            disallowedCornerIndices = new List<int>(4);
            regionCostCalculatorDict = new Dictionary<PathFinder, RegionCostCalculatorWrapper>();
        }

        public class CostNodeComparer2 : IComparer<CostNode2>
        {
            public int Compare(CostNode2 a, CostNode2 b)
            {
                return a.cost.CompareTo(b.cost);
            }
        }
        public struct CostNode2
        {
            public int index;

            public int cost;

            public CostNode2(int index, int cost)
            {
                this.index = index;
                this.cost = cost;
            }
        }
        public struct PathFinderNodeFast2
        {
            public int knownCost;

            public int heuristicCost;

            public int parentIndex;

            public int costNodeCost;

            public ushort status;
        }

        public static void InitStatusesAndPushStartNode2(PathFinder __instance, ref int curIndex, IntVec3 start)
        {
            statusOpenValue += 2;
            statusClosedValue += 2;

            int size = __instance.mapSizeX * __instance.mapSizeZ;
            if (calcGrid == null || calcGrid.Length < size)
            {
                calcGrid = new PathFinderNodeFast2[size];
            }

            if (statusClosedValue >= 65435)
            {
                int num = calcGrid.Length;
                for (int i = 0; i < num; i++)
                {
                    calcGrid[i].status = 0;
                }

                statusOpenValue = 1;
                statusClosedValue = 2;
            }
            curIndex = __instance.cellIndices.CellToIndex(start);
            calcGrid[curIndex].knownCost = 0;
            calcGrid[curIndex].heuristicCost = 0;
            calcGrid[curIndex].costNodeCost = 0;
            calcGrid[curIndex].parentIndex = curIndex;
            calcGrid[curIndex].status = statusOpenValue;
            if(openList == null)
            {
                openList = new FastPriorityQueue<CostNode2>();
            }
            openList.Clear();
            openList.Push(new CostNode2(curIndex, 0));
        }

        public static PawnPath FinalizedPath2(PathFinder __instance, int finalIndex, bool usedRegionHeuristics)
        {
            //HACK - fix pool
            //PawnPath emptyPawnPath = map(__instance).pawnPathPool.GetEmptyPawnPath();
            PawnPath emptyPawnPath = new PawnPath();
            int num = finalIndex;
            while (true)
            {
                int parentIndex = calcGrid[num].parentIndex;
                emptyPawnPath.AddNode(__instance.cellIndices.IndexToCell(num));
                if (num == parentIndex)
                {
                    break;
                }

                num = parentIndex;
            }
            emptyPawnPath.SetupFound(calcGrid[finalIndex].knownCost, usedRegionHeuristics);
            return emptyPawnPath;
        }

        public static void CalculateAndAddDisallowedCorners2(PathFinder __instance, TraverseParms traverseParms, PathEndMode peMode, CellRect destinationRect)
        {
            if (disallowedCornerIndices == null)
            {
                disallowedCornerIndices = new List<int>();
            }
            else
            {
                disallowedCornerIndices.Clear();
            }
            if (peMode == PathEndMode.Touch)
            {
                int minX = destinationRect.minX;
                int minZ = destinationRect.minZ;
                int maxX = destinationRect.maxX;
                int maxZ = destinationRect.maxZ;
                Map map = __instance.map;
                if (!__instance.IsCornerTouchAllowed(minX + 1, minZ + 1, minX + 1, minZ, minX, minZ + 1))
                {
                    disallowedCornerIndices.Add(map.cellIndices.CellToIndex(minX, minZ));
                }

                if (!__instance.IsCornerTouchAllowed(minX + 1, maxZ - 1, minX + 1, maxZ, minX, maxZ - 1))
                {
                    disallowedCornerIndices.Add(map.cellIndices.CellToIndex(minX, maxZ));
                }

                if (!__instance.IsCornerTouchAllowed(maxX - 1, maxZ - 1, maxX - 1, maxZ, maxX, maxZ - 1))
                {
                    disallowedCornerIndices.Add(map.cellIndices.CellToIndex(maxX, maxZ));
                }

                if (!__instance.IsCornerTouchAllowed(maxX - 1, minZ + 1, maxX - 1, minZ, maxX, minZ + 1))
                {
                    disallowedCornerIndices.Add(map.cellIndices.CellToIndex(maxX, minZ));
                }
            }
        }

        public static bool FindPath(PathFinder __instance, ref PawnPath __result, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode = PathEndMode.OnCell)
        {
            if (DebugSettings.pathThroughWalls)
            {
                traverseParms.mode = TraverseMode.PassAllDestroyableThings;
            }

            Pawn pawn = traverseParms.pawn;
            if (pawn != null && pawn.Map != __instance.map)
            {
                Log.Error(string.Concat("Tried to FindPath for pawn which is spawned in another map. His map PathFinder should have been used, not this one. pawn=", pawn, " pawn.Map=", pawn.Map, " map=", __instance.map));
                __result = PawnPath.NotFound;
                return false;
            }

            if (!start.IsValid)
            {
                Log.Error(string.Concat("Tried to FindPath with invalid start ", start, ", pawn= ", pawn));
                __result = PawnPath.NotFound;
                return false;
            }

            if (!dest.IsValid)
            {
                Log.Error(string.Concat("Tried to FindPath with invalid dest ", dest, ", pawn= ", pawn));
                __result = PawnPath.NotFound;
                return false;
            }

            if (traverseParms.mode == TraverseMode.ByPawn)
            {
                if (!pawn.CanReach(dest, peMode, Danger.Deadly, traverseParms.canBash, traverseParms.mode))
                {
                    __result = PawnPath.NotFound;
                    return false;
                }
            }
            else if (!__instance.map.reachability.CanReach(start, dest, peMode, traverseParms))
            {
                __result = PawnPath.NotFound;
                return false;
            }

            __instance.PfProfilerBeginSample(string.Concat("FindPath for ", pawn, " from ", start, " to ", dest, dest.HasThing ? (" at " + dest.Cell) : ""));
            __instance.cellIndices = __instance.map.cellIndices;
            __instance.pathGrid = __instance.map.pathGrid;
            __instance.edificeGrid = __instance.map.edificeGrid.InnerArray;
            __instance.blueprintGrid = __instance.map.blueprintGrid.InnerArray;
            int x = dest.Cell.x;
            int z = dest.Cell.z;
            int curIndex = __instance.cellIndices.CellToIndex(start);
            int num = __instance.cellIndices.CellToIndex(dest.Cell);
            ByteGrid byteGrid = pawn?.GetAvoidGrid();
            bool flag = traverseParms.mode == TraverseMode.PassAllDestroyableThings || traverseParms.mode == TraverseMode.PassAllDestroyableThingsNotWater;
            bool flag2 = traverseParms.mode != TraverseMode.NoPassClosedDoorsOrWater && traverseParms.mode != TraverseMode.PassAllDestroyableThingsNotWater;
            bool flag3 = !flag;
            CellRect destinationRect = __instance.CalculateDestinationRect(dest, peMode);
            bool flag4 = destinationRect.Width == 1 && destinationRect.Height == 1;
            int[] array = __instance.map.pathGrid.pathGrid;
            TerrainDef[] topGrid = __instance.map.terrainGrid.topGrid;
            EdificeGrid edificeGrid = __instance.map.edificeGrid;
            int num2 = 0;
            int num3 = 0;
            Area allowedArea = __instance.GetAllowedArea(pawn);
            bool flag5 = pawn != null && PawnUtility.ShouldCollideWithPawns(pawn);
            bool flag6 = !flag && start.GetRegion(__instance.map) != null && flag2;
            bool flag7 = !flag || !flag3;
            bool flag8 = false;
            bool flag9 = pawn?.Drafted ?? false;
            int num4 = (pawn?.IsColonist ?? false) ? 100000 : 2000;
            int num5 = 0;
            int num6 = 0;
            float num7 = __instance.DetermineHeuristicStrength(pawn, start, dest);
            int num8;
            int num9;
            if (pawn != null)
            {
                num8 = pawn.TicksPerMoveCardinal;
                num9 = pawn.TicksPerMoveDiagonal;
            }
            else
            {
                num8 = 13;
                num9 = 18;
            }
            CalculateAndAddDisallowedCorners2(__instance, traverseParms, peMode, destinationRect);
            InitStatusesAndPushStartNode2(__instance, ref curIndex, start);
            while (true)
            {
                __instance.PfProfilerBeginSample("Open cell");
                if (openList.Count <= 0)
                {
                    string text = (pawn != null && pawn.CurJob != null) ? pawn.CurJob.ToString() : "null";
                    string text2 = (pawn != null && pawn.Faction != null) ? pawn.Faction.ToString() : "null";
                    Log.Warning(string.Concat(pawn, " pathing from ", start, " to ", dest, " ran out of cells to process.\nJob:", text, "\nFaction: ", text2));
                    __instance.DebugDrawRichData();
                    __instance.PfProfilerEndSample();
                    __instance.PfProfilerEndSample();
                    __result = PawnPath.NotFound;
                    return false;
                }

                num5 += openList.Count;
                num6++;
                CostNode2 costNode = openList.Pop();
                curIndex = costNode.index;
                if (costNode.cost != calcGrid[curIndex].costNodeCost)
                {
                    __instance.PfProfilerEndSample();
                    continue;
                }

                if (calcGrid[curIndex].status == statusClosedValue)
                {
                    __instance.PfProfilerEndSample();
                    continue;
                }

                IntVec3 c = __instance.cellIndices.IndexToCell(curIndex);
                int x2 = c.x;
                int z2 = c.z;
                if (flag4)
                {
                    if (curIndex == num)
                    {
                        __instance.PfProfilerEndSample();
                        PawnPath result = FinalizedPath2(__instance, curIndex, flag8);
                        __instance.PfProfilerEndSample();
                        __result = result;
                        return false;
                    }
                }
                else if (destinationRect.Contains(c) && !disallowedCornerIndices.Contains(curIndex))
                {
                    __instance.PfProfilerEndSample();
                    PawnPath result2 = FinalizedPath2(__instance, curIndex, flag8);
                    __instance.PfProfilerEndSample();
                    __result = result2;
                    return false;
                }

                if (num2 > 160000)
                {
                    break;
                }

                __instance.PfProfilerEndSample();
                __instance.PfProfilerBeginSample("Neighbor consideration");
                for (int i = 0; i < 8; i++)
                {
                    uint num10 = (uint)(x2 + PathFinder.Directions[i]);
                    uint num11 = (uint)(z2 + PathFinder.Directions[i + 8]);
                    if (num10 >= __instance.mapSizeX || num11 >= __instance.mapSizeZ)
                    {
                        continue;
                    }

                    int num12 = (int)num10;
                    int num13 = (int)num11;
                    int num14 = __instance.cellIndices.CellToIndex(num12, num13);
                    if (calcGrid[num14].status == statusClosedValue && !flag8)
                    {
                        continue;
                    }

                    int num15 = 0;
                    bool flag10 = false;
                    if (!flag2 && new IntVec3(num12, 0, num13).GetTerrain(__instance.map).HasTag("Water"))
                    {
                        continue;
                    }

                    if (!__instance.pathGrid.WalkableFast(num14))
                    {
                        if (!flag)
                        {
                            continue;
                        }

                        flag10 = true;
                        num15 += 70;
                        Building building = edificeGrid[num14];
                        if (building == null || !PathFinder.IsDestroyable(building))
                        {
                            continue;
                        }

                        num15 += (int)(building.HitPoints * 0.2f);
                    }

                    switch (i)
                    {
                        case 4:
                            if (PathFinder.BlocksDiagonalMovement(curIndex - __instance.mapSizeX, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            if (PathFinder.BlocksDiagonalMovement(curIndex + 1, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            break;
                        case 5:
                            if (PathFinder.BlocksDiagonalMovement(curIndex + __instance.mapSizeX, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            if (PathFinder.BlocksDiagonalMovement(curIndex + 1, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            break;
                        case 6:
                            if (PathFinder.BlocksDiagonalMovement(curIndex + __instance.mapSizeX, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            if (PathFinder.BlocksDiagonalMovement(curIndex - 1, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            break;
                        case 7:
                            if (PathFinder.BlocksDiagonalMovement(curIndex - __instance.mapSizeX, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            if (PathFinder.BlocksDiagonalMovement(curIndex - 1, __instance.map))
                            {
                                if (flag7)
                                {
                                    continue;
                                }

                                num15 += 70;
                            }

                            break;
                    }

                    int num16 = (i > 3) ? num9 : num8;
                    num16 += num15;
                    if (!flag10)
                    {
                        num16 += array[num14];
                        num16 = ((!flag9) ? (num16 + topGrid[num14].extraNonDraftedPerceivedPathCost) : (num16 + topGrid[num14].extraDraftedPerceivedPathCost));
                    }

                    if (byteGrid != null)
                    {
                        num16 += byteGrid[num14] * 8;
                    }

                    if (allowedArea != null && !allowedArea[num14])
                    {
                        num16 += 600;
                    }

                    if (flag5 && PawnUtility.AnyPawnBlockingPathAt(new IntVec3(num12, 0, num13), pawn, actAsIfHadCollideWithPawnsJob: false, collideOnlyWithStandingPawns: false, forPathFinder: true))
                    {
                        num16 += 175;
                    }

                    Building building2 = __instance.edificeGrid[num14];
                    if (building2 != null)
                    {
                        __instance.PfProfilerBeginSample("Edifices");
                        int buildingCost = PathFinder.GetBuildingCost(building2, traverseParms, pawn);
                        if (buildingCost == int.MaxValue)
                        {
                            __instance.PfProfilerEndSample();
                            continue;
                        }

                        num16 += buildingCost;
                        __instance.PfProfilerEndSample();
                    }

                    List<Blueprint> list = __instance.blueprintGrid[num14];
                    if (list != null)
                    {
                        __instance.PfProfilerBeginSample("Blueprints");
                        int num17 = 0;
                        for (int j = 0; j < list.Count; j++)
                        {
                            num17 = Mathf.Max(num17, PathFinder.GetBlueprintCost(list[j], pawn));
                        }

                        if (num17 == int.MaxValue)
                        {
                            __instance.PfProfilerEndSample();
                            continue;
                        }

                        num16 += num17;
                        __instance.PfProfilerEndSample();
                    }

                    int num18 = num16 + calcGrid[curIndex].knownCost;
                    ushort status = calcGrid[num14].status;
                    if (status == statusClosedValue || status == statusOpenValue)
                    {
                        int num19 = 0;
                        if (status == statusClosedValue)
                        {
                            num19 = num8;
                        }

                        if (calcGrid[num14].knownCost <= num18 + num19)
                        {
                            continue;
                        }
                    }

                    if (flag8)
                    {
                        calcGrid[num14].heuristicCost = Mathf.RoundToInt(get_regionCostCalculator(__instance).GetPathCostFromDestToRegion(num14) * PathFinder.RegionHeuristicWeightByNodesOpened.Evaluate(num3));
                        if (calcGrid[num14].heuristicCost < 0)
                        {
                            Log.ErrorOnce(string.Concat("Heuristic cost overflow for ", pawn.ToStringSafe(), " pathing from ", start, " to ", dest, "."), pawn.GetHashCode() ^ 0xB8DC389);
                            calcGrid[num14].heuristicCost = 0;
                        }
                    }
                    else if (status != statusClosedValue && status != statusOpenValue)
                    {
                        int dx = Math.Abs(num12 - x);
                        int dz = Math.Abs(num13 - z);
                        int num20 = GenMath.OctileDistance(dx, dz, num8, num9);
                        calcGrid[num14].heuristicCost = Mathf.RoundToInt(num20 * num7);
                    }

                    int num21 = num18 + calcGrid[num14].heuristicCost;
                    if (num21 < 0)
                    {
                        Log.ErrorOnce(string.Concat("Node cost overflow for ", pawn.ToStringSafe(), " pathing from ", start, " to ", dest, "."), pawn.GetHashCode() ^ 0x53CB9DE);
                        num21 = 0;
                    }

                    calcGrid[num14].parentIndex = curIndex;
                    calcGrid[num14].knownCost = num18;
                    calcGrid[num14].status = statusOpenValue;
                    calcGrid[num14].costNodeCost = num21;
                    num3++;
                    openList.Push(new CostNode2(num14, num21));
                }

                __instance.PfProfilerEndSample();
                num2++;
                calcGrid[curIndex].status = statusClosedValue;
                if (num3 >= num4 && flag6 && !flag8)
                {
                    flag8 = true;
                    get_regionCostCalculator(__instance).Init(destinationRect, traverseParms, num8, num9, byteGrid, allowedArea, flag9, disallowedCornerIndices);
                    InitStatusesAndPushStartNode2(__instance, ref curIndex, start);
                    openList.Clear();
                    openList.Push(new CostNode2(curIndex, 0));
                    num3 = 0;
                    num2 = 0;
                }
            }

            Log.Warning(string.Concat(pawn, " pathing from ", start, " to ", dest, " hit search limit of ", 160000, " cells."));
            __instance.DebugDrawRichData();
            __instance.PfProfilerEndSample();
            __instance.PfProfilerEndSample();
            __result = PawnPath.NotFound;
            return false;
        }

        public static RegionCostCalculatorWrapper get_regionCostCalculator(PathFinder __instance)
        {
            if (!regionCostCalculatorDict.TryGetValue(__instance, out RegionCostCalculatorWrapper regionCostCalculatorWrapper)) {
                regionCostCalculatorWrapper = new RegionCostCalculatorWrapper(__instance.map);
                regionCostCalculatorDict[__instance] = regionCostCalculatorWrapper;
            }
            return regionCostCalculatorWrapper;
        }
    }
}
