using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using RocketMan;
using Soyuz.Profiling;
using UnityEngine;
using Verse;

namespace Soyuz
{
    [StaticConstructorOnStartup]
    public static class ContextualExtensions
    {
        private static Pawn _pawnTick;
        private static Pawn _pawnScreen; 

        private static bool offScreen;
        private static bool shouldTick;

        private static int curDelta;

        private const int TransformationCacheSize = 2500;

        private static readonly int[] _transformationCache = new int[TransformationCacheSize];
        private static readonly Dictionary<int, int> timers = new Dictionary<int, int>();
        
        private static readonly Dictionary<Pawn, PawnPerformanceModel> pawnPerformanceModels = new Dictionary<Pawn, PawnPerformanceModel>();
        private static readonly Dictionary<Pawn, Dictionary<Type,PawnNeedModel>> pawnNeedModels =
            new Dictionary<Pawn, Dictionary<Type,PawnNeedModel>>();
        private static readonly Dictionary<Pawn, Dictionary<Hediff,PawnHediffModel>> pawnHediffsModels =
            new Dictionary<Pawn, Dictionary<Hediff,PawnHediffModel>>();

        private static int DilationRate
        {
            get
            {
                switch (Context.zoomRange)
                {
                    default:
                        return 1;
                    case CameraZoomRange.Closest:
                        return 60;
                    case CameraZoomRange.Close:
                        return 20;
                    case CameraZoomRange.Middle:
                        return 15;
                    case CameraZoomRange.Far:
                        return 15;
                    case CameraZoomRange.Furthest:
                        return 7;
                }
            }
        }

        
        static ContextualExtensions()
        {
            for (int i = 0; i < _transformationCache.Length; i++)
                _transformationCache[i] = (int) Mathf.Max(Mathf.RoundToInt(i / 30) * 30, 30);
        }

        private static int RoundTransform(int interval)
        {
            if(interval >= TransformationCacheSize)
                return (int)Mathf.Max(Mathf.RoundToInt(interval / 30) * 30, 30);
            return _transformationCache[interval];
        }
        
        public static bool OffScreen(this Pawn pawn)
        {
            if (Finder.alwaysDilating) 
                return offScreen = true;
            if (_pawnScreen == pawn) 
                return offScreen;
            _pawnScreen = pawn;
            if (Context.curViewRect.Contains(pawn.positionInt))
                return offScreen = false;
            return offScreen = true;
        }

        private static bool _isSkippingPawn = false;
        private static Pawn _skippingPawn = null;
        public static bool IsSkippingTicks(this Pawn pawn)
        {
            if (!Finder.timeDilation) 
                return false;
            if (pawn == _skippingPawn) 
                return _isSkippingPawn;
            bool spawned = pawn.Spawned;
            _skippingPawn = pawn;
            _isSkippingPawn = _pawnTick == pawn && (
                        (spawned == false && WorldPawnsTicker.isActive && Finder.timeDilationWorldPawns) ||
                                         (spawned == true && (pawn.OffScreen() || 
                                                              Context.zoomRange == CameraZoomRange.Far ||
                                                              Context.zoomRange == CameraZoomRange.Furthest)));
            return _isSkippingPawn;
        }

        private static Stopwatch _stopwatch = new Stopwatch();

        public static void BeginTick(this Pawn pawn)
        {
            _pawnTick = pawn;
            if (!Finder.enabled || !Finder.timeDilation || !pawn.IsValidWildlifeOrWorldPawn())
            {
                _isValidPawn = false;
                _isSkippingPawn = false;
                _skippingPawn = pawn;
            }
            if (Finder.logData &&  Time.frameCount - Finder.lastFrame < 60)
            {
                _stopwatch.Reset();
                _stopwatch.Start();
            }
        }

        public static void EndTick(this Pawn pawn)
        {
            if (Finder.logData && Time.frameCount - Finder.lastFrame < 60)
            {
                _stopwatch.Stop();
                var performanceModel = pawn.GetPerformanceModel();
                performanceModel.AddResult(_stopwatch.ElapsedTicks);
                if (GenTicks.TicksGame % 150 == 0 )
                {
                    var needsModel = pawn.GetNeedModels();
                    if (pawn.needs?.needs != null)
                        foreach (var need in pawn.needs?.needs)
                        {
                            var type = need.GetType();
                            if (needsModel.TryGetValue(type, out var model)) model.AddResult(need.CurLevelPercentage);
                            else needsModel[type] = new PawnNeedModel();
                        }

                    var hediffModel = pawn.GetHediffModels();
                    foreach (var hediff  in pawn.health.hediffSet.hediffs)
                    {
                        if (hediffModel.TryGetValue(hediff, out var model)) model.AddResult(hediff.Severity);
                        else hediffModel[hediff] = new PawnHediffModel();
                    }
                }
            }
            _pawnScreen = null;
            _pawnTick = null;
            _skippingPawn = null;
            _validPawn = null;
        }

        public static PawnPerformanceModel GetPerformanceModel(this Pawn pawn)
        {
            if (pawnPerformanceModels.TryGetValue(pawn, out var model))
                return model;
            return pawnPerformanceModels[pawn] = new PawnPerformanceModel();
        }
        
        public static Dictionary<Type,PawnNeedModel> GetNeedModels(this Pawn pawn)
        {
            if (pawnNeedModels.TryGetValue(pawn, out var model))
                return model;
            return pawnNeedModels[pawn] = new Dictionary<Type, PawnNeedModel>();
        }
        
        public static Dictionary<Hediff,PawnHediffModel> GetHediffModels(this Pawn pawn)
        {
            if (pawnHediffsModels.TryGetValue(pawn, out var model))
                return model;
            return pawnHediffsModels[pawn] = new Dictionary<Hediff, PawnHediffModel>();
        }

        public static bool ShouldTick(this Pawn pawn)
        {
            var tick = GenTicks.TicksGame;
            shouldTick = ShouldTickInternal(pawn);
            if (timers.TryGetValue(pawn.thingIDNumber, out var val)) curDelta = tick - val;
            else curDelta = 1;
            if (shouldTick) timers[pawn.thingIDNumber] = tick;
            return shouldTick;
        }
        
        public static bool IsCustomTickInterval(this Thing thing, int interval)
        {
            if (_pawnTick == thing && Finder.timeDilation && Finder.enabled)
            {
                if (WorldPawnsTicker.isActive)
                {
                    return WorldPawnsTicker.curCycle % WorldPawnsTicker.Transform(interval) == 0;
                }
                else if (((Pawn) thing).IsSkippingTicks())
                {
                    return (thing.thingIDNumber + GenTicks.TicksGame) % RoundTransform(interval) == 0;
                }
            }
            return thing.IsHashIntervalTick(interval);
        }

        private static bool ShouldTickInternal(Pawn pawn)
        {
            if (!Finder.timeDilation || !Finder.enabled) 
                return true;
            if (WorldPawnsTicker.isActive && Finder.timeDilationWorldPawns)
                return true;
            var tick = GenTicks.TicksGame;
            if (false
                || (pawn.thingIDNumber + tick) % 30 == 0
                || (tick % 250 == 0)
                || pawn.jobs?.curJob != null && pawn.jobs?.curJob?.expiryInterval > 0 &&
                (tick - pawn.jobs.curJob.startTick) % (pawn.jobs.curJob.expiryInterval * 2) == 0)
                return true;
            if (pawn.OffScreen())
                return (pawn.thingIDNumber + tick) % DilationRate == 0;
            if (Context.zoomRange == CameraZoomRange.Far || Context.zoomRange == CameraZoomRange.Furthest)
                return (pawn.thingIDNumber + tick) % 3 == 0;
            return true;
        }
        
        public static int GetDeltaT(this Thing thing)
        {
            if (thing == _pawnTick) 
                return curDelta;
            if (timers.TryGetValue(thing.thingIDNumber, out var val))
                return GenTicks.TicksGame - val;
            throw new Exception();
        }

        private static bool _isValidPawn = false;
        private static Pawn _validPawn = null; 
        public static bool IsValidWildlifeOrWorldPawn(this Pawn pawn)
        {
            if (_validPawn == pawn) 
                return _isValidPawn;
            _validPawn = pawn;
            int pawnInt = pawn.AsInt();
            return _isValidPawn = (_pawnTick == pawn && !pawn.IsColonist && (
                (pawnInt == (pawnInt & Context.dilationInts[pawn.def.index])) || 
                (WorldPawnsTicker.isActive && Finder.timeDilationWorldPawns)));
        }

        public static int AsInt(this Pawn pawn)
        {
            int val = 1;
            if (pawn.factionInt != null)
            {
                if (pawn.factionInt != Faction.OfPlayerSilentFail) val = val | 2;
                else val = val | 4;
            }
            return val;
        }
    }
}