﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using System.Reflection;
using static HarmonyLib.AccessTools;

namespace RimThreaded
{

    public class Reachability_Patch
    {
        [ThreadStatic]
        private static Queue<Region> openQueue;
        [ThreadStatic]
        private static List<Region> destRegions;
        [ThreadStatic]
        private static List<Region> startingRegions;
        [ThreadStatic]
        private static HashSet<Region> regionsReached;

        public static FieldRef<Reachability, Map> mapFieldRef = FieldRefAccess<Reachability, Map>("map");
        public static FieldRef<Reachability, ReachabilityCache> cacheFieldRef = FieldRefAccess<Reachability, ReachabilityCache>("cache");
        //public static uint offsetReachedIndex = 1;
        //private static readonly object reachedIndexLock = new object();

        private static readonly MethodInfo methodGetCachedResult =
            Method(typeof(Reachability), "GetCachedResult", new Type[] { typeof(TraverseParms) });
        private static readonly Func<Reachability, TraverseParms, BoolUnknown> funcGetCachedResult =
            (Func<Reachability, TraverseParms, BoolUnknown>)Delegate.CreateDelegate(typeof(Func<Reachability, TraverseParms, BoolUnknown>), methodGetCachedResult);

        private static readonly MethodInfo methodCheckCellBasedReachability =
            Method(typeof(Reachability), "CheckCellBasedReachability", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) });
        private static readonly Func<Reachability, IntVec3, LocalTargetInfo, PathEndMode, TraverseParms, bool> funcCheckCellBasedReachability =
            (Func<Reachability, IntVec3, LocalTargetInfo, PathEndMode, TraverseParms, bool>)Delegate.CreateDelegate(typeof(Func<Reachability, IntVec3, LocalTargetInfo, PathEndMode, TraverseParms, bool>), methodCheckCellBasedReachability);

        private static readonly MethodInfo methodCanUseCache =
            Method(typeof(Reachability), "CanUseCache", new Type[] { typeof(TraverseParms) });
        private static readonly Func<Reachability, TraverseParms, bool> funcCanUseCache =
            (Func<Reachability, TraverseParms, bool>)Delegate.CreateDelegate(typeof(Func<Reachability, TraverseParms, bool>), methodCanUseCache);

        private static void QueueNewOpenRegion(Region region, Queue<Region> openQueueParam, HashSet<Region> regionsReached)
        {
            if (region == null)
            {
                Log.ErrorOnce("Tried to queue null region.", 881121);
                return;
            }

            if (regionsReached.Contains(region))
            {
                Log.ErrorOnce("Region is already reached; you can't open it. Region: " + region.ToString(), 719991);
                return;
            }

            openQueueParam.Enqueue(region);
            regionsReached.Add(region);

        }

        public static bool CanReach(Reachability __instance, ref bool __result, IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams)
        {
            Map map = mapFieldRef(__instance);
            //if (working)
            //{
                //Log.ErrorOnce("Called CanReach() while working. This should never happen. Suppressing further errors.", 7312233);
                //return false;
            //}

            if (traverseParams.pawn != null)
            {
                if (!traverseParams.pawn.Spawned)
                {
                    __result = false;
                    return false;
                }

                if (traverseParams.pawn.Map != map)
                {
                    Log.Error(string.Concat("Called CanReach() with a pawn spawned not on this map. This means that we can't check his reachability here. Pawn's current map should have been used instead of this one. pawn=", traverseParams.pawn, " pawn.Map=", traverseParams.pawn.Map, " map=", map));
                    __result = false;
                    return false;
                }
            }

            if (ReachabilityImmediate.CanReachImmediate(start, dest, map, peMode, traverseParams.pawn))
            {
                __result = true;
                return false;
            }

            if (!dest.IsValid)
            {
                __result = false;
                return false;
            }

            if (dest.HasThing && dest.Thing.Map != map)
            {
                __result = false;
                return false;
            }

            if (!start.InBounds(map) || !dest.Cell.InBounds(map))
            {
                __result = false;
                return false;
            }

            if ((peMode == PathEndMode.OnCell || peMode == PathEndMode.Touch || peMode == PathEndMode.ClosestTouch) && traverseParams.mode != TraverseMode.NoPassClosedDoorsOrWater && traverseParams.mode != TraverseMode.PassAllDestroyableThingsNotWater)
            {
                Room room = RegionAndRoomQuery.RoomAtFast(start, map);
                if (room != null && room == RegionAndRoomQuery.RoomAtFast(dest.Cell, map))
                {
                    __result = true;
                    return false;
                }
            }

            if (traverseParams.mode == TraverseMode.PassAllDestroyableThings)
            {
                TraverseParms traverseParams2 = traverseParams;
                traverseParams2.mode = TraverseMode.PassDoors;
                bool canReachResult = false;
                CanReach(__instance, ref canReachResult, start, dest, peMode, traverseParams2);
                if (canReachResult)
                {
                    __result = true;
                    return false;
                }
            }

            dest = (LocalTargetInfo)GenPath.ResolvePathMode(traverseParams.pawn, dest.ToTargetInfo(map), ref peMode);
            //working = true;
            try
            {
                PathGrid pathGrid = map.pathGrid;
                RegionGrid regionGrid = map.regionGrid;

                if (destRegions == null)
                {
                    destRegions = new List<Region>();
                }
                else
                {
                    destRegions.Clear();
                }
                switch (peMode)
                {
                    case PathEndMode.OnCell:
                        {
                            Region region = dest.Cell.GetRegion(map);
                            if (region != null && region.Allows(traverseParams, isDestination: true))
                            {
                                destRegions.Add(region);
                            }

                            break;
                        }
                    case PathEndMode.Touch:
                        TouchPathEndModeUtility.AddAllowedAdjacentRegions(dest, traverseParams, map, destRegions);
                        break;
                }

                if (destRegions.Count == 0 && traverseParams.mode != TraverseMode.PassAllDestroyableThings && traverseParams.mode != TraverseMode.PassAllDestroyableThingsNotWater)
                {
                    //FinalizeCheck();
                    __result = false;
                    return false;
                }

                destRegions.RemoveDuplicates();

                if (regionsReached == null)
                {
                    regionsReached = new HashSet<Region>();
                }
                else
                {
                    regionsReached.Clear();
                }
                if (openQueue == null)
                {
                    openQueue = new Queue<Region>();
                }
                else
                {
                    openQueue.Clear();
                }
                if(startingRegions == null)
                {
                    startingRegions = new List<Region>();
                } else
                {
                    startingRegions.Clear();
                }
                DetermineStartRegions(map, start, startingRegions, pathGrid, regionGrid, openQueue, regionsReached);
                if (openQueue.Count == 0 && traverseParams.mode != TraverseMode.PassAllDestroyableThings && traverseParams.mode != TraverseMode.PassAllDestroyableThingsNotWater)
                {
                    //FinalizeCheck();
                    __result = false;
                    return false;
                }

                if (startingRegions.Any() && destRegions.Any() && funcCanUseCache(__instance, traverseParams.mode))
                {
                    switch (funcGetCachedResult(__instance, traverseParams))
                    {
                        case BoolUnknown.True:
                            __result = true;
                            return false;
                        case BoolUnknown.False:
                            __result = false;
                            return false;
                    }
                }
                if (traverseParams.mode == TraverseMode.PassAllDestroyableThings || traverseParams.mode == TraverseMode.PassAllDestroyableThingsNotWater || traverseParams.mode == TraverseMode.NoPassClosedDoorsOrWater)
                {
                    bool result = funcCheckCellBasedReachability(__instance, start, dest, peMode, traverseParams);
                    //FinalizeCheck();
                    __result = result;
                    return false;
                }

                bool result2 = CheckRegionBasedReachability(__instance, traverseParams, openQueue, regionsReached);
                //FinalizeCheck();
                __result = result2;
                return false;
            }
            finally
            {
                //working = false;
            }
        }
        private static bool CheckRegionBasedReachability(Reachability __instance, TraverseParms traverseParams, Queue<Region> openQueueParam, HashSet<Region> regionsReached)
        {
            ReachabilityCache cache = cacheFieldRef(__instance);
            while (openQueue.Count > 0)
            {
                Region region = openQueue.Dequeue();
                for (int i = 0; i < region.links.Count; i++)
                {
                    RegionLink regionLink = region.links[i];
                    for (int j = 0; j < 2; j++)
                    {
                        Region region2 = regionLink.regions[j];
                        if (region2 == null || regionsReached.Contains(region2) || !region2.type.Passable() || !region2.Allows(traverseParams, isDestination: false))
                        {
                            continue;
                        }

                        if (destRegions.Contains(region2))
                        {
                            for (int k = 0; k < startingRegions.Count; k++)
                            {
                                cache.AddCachedResult(startingRegions[k].Room, region2.Room, traverseParams, reachable: true);
                            }

                            return true;
                        }
                        QueueNewOpenRegion(region2, openQueueParam, regionsReached);
                    }
                }
            }

            for (int l = 0; l < startingRegions.Count; l++)
            {
                for (int m = 0; m < destRegions.Count; m++)
                {
                    cache.AddCachedResult(startingRegions[l].Room, destRegions[m].Room, traverseParams, reachable: false);
                }
            }

            return false;
        }

        private static void DetermineStartRegions(Map map, IntVec3 start, List<Region> startingRegionsParam, PathGrid pathGrid,
            RegionGrid regionGrid, Queue<Region> openQueueParam, HashSet<Region> regionsReached)
        {
            startingRegionsParam.Clear();
            if (pathGrid.WalkableFast(start))
            {
                Region validRegionAt = regionGrid.GetValidRegionAt(start);
                QueueNewOpenRegion(validRegionAt, openQueueParam, regionsReached);
                startingRegionsParam.Add(validRegionAt);
                return;
            }
            else
            {
                for (int index = 0; index < 8; ++index)
                {
                    IntVec3 intVec = start + GenAdj.AdjacentCells[index];
                    if (intVec.InBounds(map) && pathGrid.WalkableFast(intVec))
                    {
                        Region validRegionAt2 = regionGrid.GetValidRegionAt(intVec);
                        if (validRegionAt2 != null && !regionsReached.Contains(validRegionAt2))
                        {
                            QueueNewOpenRegion(validRegionAt2, openQueueParam, regionsReached);
                            startingRegionsParam.Add(validRegionAt2);
                        }
                    }
                }
            }
        }


    }
}