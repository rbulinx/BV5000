using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BV5000.Sonar
{
    [RequireComponent(typeof(SonarConfig))]
    public class SonarScanner : MonoBehaviour
    {
        [Tooltip("Run a scan automatically when Play mode starts.")]
        public bool runOnStart = false;

        public SonarConfig config;

        [Header("Rig")]
        [Tooltip("Transform that rotates mechanically around local Y during the 360 degree scan.")]
        public Transform yawRig;

        [Tooltip("Transform that applies the tilt angle. Usually a child of yawRig.")]
        public Transform tiltRig;

        [Tooltip("Actual sonar head transform used as the ray origin and beam orientation.")]
        public Transform sonarHead;

        [Tooltip("Restore yaw/tilt rig rotations after each scan.")]
        public bool restoreRigAfterScan = true;

        private TemperatureProfile temperatureProfile;
        private SoundSpeedModel soundSpeedModel;
        private IntensityModel intensityModel;
        private PointCloudBuffer pointCloud;
        private PointCloudCsvWriter csvWriter;
        private SonarBeamTracer beamTracer;

        private readonly List<SonarPoint> recentPoints = new List<SonarPoint>();
        private float totalRefractionBendDeg;
        private float maxRefractionBendDeg;
        private int refractionHitCount;
        private float totalStraightComparisonDistance;
        private float maxStraightComparisonDistance;
        private int straightComparisonCount;
        private float totalAcousticRangeDelta;
        private float maxAcousticRangeDelta;
        private int acousticRangeHitCount;

        private Coroutine activeScanCoroutine;
        private bool isScanning;
        private bool hasCurrentScanRay;
        private Vector3 currentScanRayOrigin;
        private Vector3 currentScanRayEnd;
        private readonly Vector3[] currentFanRayEnds = new Vector3[3];
        private int currentFanRayCount;
        private Quaternion initialYawRigLocalRotation;
        private Quaternion initialTiltRigLocalRotation;
        private bool rigSetupLogged;

        public IReadOnlyList<SonarPoint> RecentPoints => recentPoints;

        private void Awake()
        {
            if (config == null)
            {
                config = GetComponent<SonarConfig>();
            }

            if (config == null)
            {
                Debug.LogError("SonarScanner requires a SonarConfig component.");
                enabled = false;
                return;
            }

            InitializeRigReferences();

            InitializeModels();
        }

        private void InitializeRigReferences()
        {
            if (sonarHead == null)
            {
                sonarHead = transform;
            }

            if (tiltRig == null)
            {
                tiltRig = sonarHead;
            }

            if (yawRig == null)
            {
                yawRig = tiltRig;
            }
        }

        [ContextMenu("Log Rig Setup")]
        public void LogRigSetup()
        {
            EnsureConfigReference();
            if (config == null)
            {
                Debug.LogError("SonarScanner requires a SonarConfig component on the same GameObject.");
                return;
            }

            InitializeRigReferences();
            Debug.Log(
                $"Sonar rig setup: scanner={name}, yawRig={yawRig.name}, tiltRig={tiltRig.name}, sonarHead={sonarHead.name}, " +
                $"forwardAxis={config.forwardAxis}, headPosition={sonarHead.position}, headForward={sonarHead.forward}, headRight={sonarHead.right}");
        }

        [ContextMenu("Test Current Fan Beam Once")]
        public void TestCurrentFanBeamOnce()
        {
            if (!ValidateReady())
            {
                return;
            }

            temperatureProfile.RefreshTable();
            pointCloud.Clear();
            recentPoints.Clear();
            ResetRefractionStats();

            Vector3 origin = sonarHead.position;
            GetBeamFrameFromRig(out Vector3 centerDirection, out Vector3 verticalFanAxis);
            DrawActiveScanFan(origin, centerDirection, verticalFanAxis);

            List<BeamHitResult> hits = beamTracer.TraceFanBeam(origin, centerDirection, verticalFanAxis, 0f, 0f);
            foreach (BeamHitResult hit in hits)
            {
                AddHit(hit, 0f);
            }

            Debug.Log($"Test fan beam once: hits={hits.Count}, points={recentPoints.Count}, origin={origin}, direction={centerDirection}, fanAxis={verticalFanAxis}");
        }

        private void Start()
        {
            if (runOnStart)
            {
                if (config != null && config.animateScan)
                {
                    RunScanAnimated();
                }
                else
                {
                    RunScan();
                }
            }
        }

        private void InitializeModels()
        {
            temperatureProfile = new TemperatureProfile(config);
            soundSpeedModel = new SoundSpeedModel(config);
            intensityModel = new IntensityModel(config);
            pointCloud = new PointCloudBuffer();
            csvWriter = new PointCloudCsvWriter(config);
            beamTracer = new SonarBeamTracer(config, temperatureProfile, soundSpeedModel);
        }

        [ContextMenu("Run Sonar Scan")]
        public void RunScan()
        {
            StopActiveScanCoroutine();

            if (!ValidateReady())
            {
                return;
            }

            CaptureRigStartRotations();
            isScanning = true;
            temperatureProfile.RefreshTable();
            pointCloud.Clear();
            recentPoints.Clear();
            ResetRefractionStats();
            hasCurrentScanRay = false;
            currentFanRayCount = 0;

            float[] tiltAngles = GetValidTiltAngles();
            LogScanPlan("Sonar scan", tiltAngles);
            ScanAllTilts(tiltAngles);

            if (!config.savePerTilt)
            {
                SavePointCloud(GetCombinedOutputFileName());
            }

            LogScanFinished("Sonar scan");
            isScanning = false;
            RestoreRigRotationsIfNeeded();
        }

        [ContextMenu("Run Sonar Scan Animated")]
        public void RunScanAnimated()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Animated sonar scan requires Play mode. Running an immediate scan instead.");
                RunScan();
                return;
            }

            StopActiveScanCoroutine();
            activeScanCoroutine = StartCoroutine(RunScanAnimatedRoutine());
        }

        private IEnumerator RunScanAnimatedRoutine()
        {
            if (!ValidateReady())
            {
                yield break;
            }

            CaptureRigStartRotations();
            isScanning = true;
            temperatureProfile.RefreshTable();
            pointCloud.Clear();
            recentPoints.Clear();
            ResetRefractionStats();
            hasCurrentScanRay = false;
            currentFanRayCount = 0;

            float[] tiltAngles = GetValidTiltAngles();
            LogScanPlan("Animated sonar scan", tiltAngles);
            yield return ScanAllTiltsAnimated(tiltAngles);

            if (!config.savePerTilt)
            {
                SavePointCloud(GetCombinedOutputFileName());
            }

            LogScanFinished("Animated sonar scan");
            isScanning = false;
            activeScanCoroutine = null;
            RestoreRigRotationsIfNeeded();
        }

        private void StopActiveScanCoroutine()
        {
            if (activeScanCoroutine != null)
            {
                StopCoroutine(activeScanCoroutine);
                activeScanCoroutine = null;
                isScanning = false;
            }
        }

        private bool ValidateReady()
        {
            EnsureConfigReference();
            if (config == null)
            {
                Debug.LogError("SonarConfig is missing. Add SonarConfig to the same GameObject as SonarScanner, or assign the Config field.");
                return false;
            }

            InitializeRigReferences();
            ApplyBV5000SamplingPresetIfNeeded();
            ValidateRigSetup();

            if (temperatureProfile == null || soundSpeedModel == null || intensityModel == null || pointCloud == null || csvWriter == null || beamTracer == null)
            {
                InitializeModels();
            }

            return true;
        }

        private void ApplyBV5000SamplingPresetIfNeeded()
        {
            if (config == null || !config.useBV5000SamplingPreset)
            {
                return;
            }

            config.yawResolutionDeg = SonarConfig.BV5000AngularResolutionDeg;
            config.beamWidthDeg = SonarConfig.BV5000SectorDeg;
            config.beamResolutionDeg = SonarConfig.BV5000AngularResolutionDeg;
            config.saveExtraColumns = true;
            config.debugPointStride = 1;
        }

        private void EnsureConfigReference()
        {
            if (config == null)
            {
                config = GetComponent<SonarConfig>();
            }
        }

        private void ValidateRigSetup()
        {
            if (!rigSetupLogged)
            {
                LogRigSetup();
                rigSetupLogged = true;
            }

            if (yawRig == transform && tiltRig == transform && sonarHead == transform)
            {
                Debug.LogWarning(
                    "YawRig, TiltRig, and SonarHead are all using the SonarScanner transform. " +
                    "This can work for a simple single-object test, but if you created a rig hierarchy, assign those Transform fields explicitly.");
            }
        }

        private float[] GetValidTiltAngles()
        {
            if (config.scanAllTiltAngles)
            {
                if (config.tiltAnglesDeg == null || config.tiltAnglesDeg.Length == 0)
                {
                    return new float[] { 0f };
                }

                return config.tiltAnglesDeg;
            }

            return new float[] { config.tiltAngleDeg };
        }

        private void ScanAllTilts(float[] tiltAngles)
        {
            for (int tiltIndex = 0; tiltIndex < tiltAngles.Length; tiltIndex++)
            {
                if (config.savePerTilt)
                {
                    pointCloud.Clear();
                }

                ScanSingleTilt(tiltIndex, tiltAngles[tiltIndex]);

                if (config.savePerTilt)
                {
                    SavePointCloud(GetTiltOutputFileName(tiltIndex, tiltAngles[tiltIndex]));
                }
            }
        }

        private void ScanSingleTilt(int tiltIndex, float tiltDeg)
        {
            float sweepAngle = GetSweepAngle();
            int yawSamples = Mathf.Max(1, Mathf.CeilToInt(sweepAngle / Mathf.Max(0.001f, config.yawResolutionDeg)));
            float yawStep = sweepAngle / yawSamples;
            int startPointCount = recentPoints.Count;

            for (int yawIndex = 0; yawIndex < yawSamples; yawIndex++)
            {
                float sampleTime = GetSampleTime(tiltIndex, yawIndex, yawSamples, yawStep);
                float yawDeg = config.yawStartDeg + yawIndex * yawStep;
                ApplyRigAngles(yawDeg, tiltDeg);
                Vector3 origin = sonarHead.position;
                GetBeamFrameFromRig(out Vector3 centerDirection, out Vector3 verticalFanAxis);

                DrawActiveScanFan(origin, centerDirection, verticalFanAxis);
                List<BeamHitResult> hits = beamTracer.TraceFanBeam(origin, centerDirection, verticalFanAxis, yawDeg, tiltDeg);
                foreach (BeamHitResult hit in hits)
                {
                    AddHit(hit, sampleTime);
                }

                LogProgressIfNeeded(tiltIndex, tiltDeg, yawIndex, yawSamples, startPointCount);
            }
        }

        private IEnumerator ScanAllTiltsAnimated(float[] tiltAngles)
        {
            for (int tiltIndex = 0; tiltIndex < tiltAngles.Length; tiltIndex++)
            {
                if (config.savePerTilt)
                {
                    pointCloud.Clear();
                }

                yield return ScanSingleTiltAnimated(tiltIndex, tiltAngles[tiltIndex]);

                if (config.savePerTilt)
                {
                    SavePointCloud(GetTiltOutputFileName(tiltIndex, tiltAngles[tiltIndex]));
                }
            }
        }

        private IEnumerator ScanSingleTiltAnimated(int tiltIndex, float tiltDeg)
        {
            float sweepAngle = GetSweepAngle();
            int yawSamples = Mathf.Max(1, Mathf.CeilToInt(sweepAngle / Mathf.Max(0.001f, config.yawResolutionDeg)));
            float yawStep = sweepAngle / yawSamples;
            float animationDelaySeconds = config.animateUsingScanSpeedTiming
                ? GetSampleIntervalSeconds(yawStep)
                : config.scanStepDelaySeconds;
            WaitForSeconds scanDelay = animationDelaySeconds > 0f
                ? new WaitForSeconds(animationDelaySeconds)
                : null;
            int startPointCount = recentPoints.Count;

            for (int yawIndex = 0; yawIndex < yawSamples; yawIndex++)
            {
                float sampleTime = GetSampleTime(tiltIndex, yawIndex, yawSamples, yawStep);
                float yawDeg = config.yawStartDeg + yawIndex * yawStep;
                ApplyRigAngles(yawDeg, tiltDeg);
                Vector3 origin = sonarHead.position;
                GetBeamFrameFromRig(out Vector3 centerDirection, out Vector3 verticalFanAxis);

                DrawActiveScanFan(origin, centerDirection, verticalFanAxis);
                List<BeamHitResult> hits = beamTracer.TraceFanBeam(origin, centerDirection, verticalFanAxis, yawDeg, tiltDeg);
                foreach (BeamHitResult hit in hits)
                {
                    AddHit(hit, sampleTime);
                }

                LogProgressIfNeeded(tiltIndex, tiltDeg, yawIndex, yawSamples, startPointCount);

                if (scanDelay != null)
                {
                    yield return scanDelay;
                }
                else
                {
                    yield return null;
                }
            }
        }

        private float GetSweepAngle()
        {
            float sweepAngle = config.yawEndDeg - config.yawStartDeg;
            while (sweepAngle <= 0f)
            {
                sweepAngle += 360f;
            }

            return Mathf.Min(sweepAngle, 360f);
        }

        private void ApplyRigAngles(float yawDeg, float tiltDeg)
        {
            Quaternion yawRotation = Quaternion.AngleAxis(yawDeg, Vector3.up);
            Quaternion tiltRotation = GetLocalTiltRotation(tiltDeg);

            if (yawRig == tiltRig)
            {
                yawRig.localRotation = initialYawRigLocalRotation * yawRotation * tiltRotation;
                return;
            }

            yawRig.localRotation = initialYawRigLocalRotation * yawRotation;
            tiltRig.localRotation = initialTiltRigLocalRotation * tiltRotation;
        }

        private Quaternion GetLocalTiltRotation(float tiltDeg)
        {
            if (config.forwardAxis == SonarForwardAxis.LocalZ)
            {
                return Quaternion.AngleAxis(tiltDeg, Vector3.right);
            }

            return Quaternion.AngleAxis(tiltDeg, Vector3.forward);
        }

        private void GetBeamFrameFromRig(out Vector3 centerDirectionWorld, out Vector3 verticalFanAxisWorld)
        {
            if (config.forwardAxis == SonarForwardAxis.LocalZ)
            {
                centerDirectionWorld = sonarHead.forward.normalized;
                verticalFanAxisWorld = sonarHead.right.normalized;
                return;
            }

            centerDirectionWorld = sonarHead.right.normalized;
            verticalFanAxisWorld = sonarHead.forward.normalized;
        }

        private void CaptureRigStartRotations()
        {
            InitializeRigReferences();
            initialYawRigLocalRotation = yawRig.localRotation;
            initialTiltRigLocalRotation = tiltRig.localRotation;
        }

        private void RestoreRigRotationsIfNeeded()
        {
            if (!restoreRigAfterScan || yawRig == null || tiltRig == null)
            {
                return;
            }

            yawRig.localRotation = initialYawRigLocalRotation;
            if (tiltRig != yawRig)
            {
                tiltRig.localRotation = initialTiltRigLocalRotation;
            }
        }

        private float GetSampleTime(int tiltIndex, int yawIndex, int yawSamples, float yawStepDeg)
        {
            int sampleIndex = Mathf.Max(0, tiltIndex) * Mathf.Max(1, yawSamples) + Mathf.Max(0, yawIndex);
            return sampleIndex * GetSampleIntervalSeconds(yawStepDeg);
        }

        private float GetSampleIntervalSeconds(float yawStepDeg)
        {
            if (config.useScanSpeedForTiming)
            {
                return Mathf.Abs(yawStepDeg) / Mathf.Max(0.1f, config.scanSpeedDegPerSecond);
            }

            return Mathf.Max(0.0001f, config.sampleIntervalSeconds);
        }

        private void AddHit(BeamHitResult hit, float sampleTimeSeconds)
        {
            byte intensity = intensityModel.GetIntensity(hit.incomingDirection, hit.normal, hit.range);
            SonarPoint point = new SonarPoint(
                GetMeasuredOutputPoint(hit),
                intensity,
                hit.yawDeg,
                hit.tiltDeg,
                hit.range,
                hit.geometricRange,
                hit.acousticRange,
                hit.oneWayTravelTimeSeconds,
                sampleTimeSeconds);

            if (TryAddDensityShapedPoint(point, hit))
            {
                AccumulateRefractionStats(hit);
            }

            if (config.showDebugRays)
            {
                Debug.DrawLine(hit.origin, hit.point, config.hitRayColor, Mathf.Max(0f, config.scanRayDuration));
            }
        }

        private bool TryAddDensityShapedPoint(SonarPoint point, BeamHitResult hit)
        {
            if (config == null)
            {
                AddPointToCloud(point);
                return true;
            }

            SonarDensityRegionVolume volumeRegion = GetDensityVolumeRegion(point.position, out float regionWeight);
            if (volumeRegion != null && volumeRegion.terrainHitsOnly && !IsTerrainHit(hit))
            {
                volumeRegion = null;
                regionWeight = 0f;
            }

            if (!config.useDensityRegions && volumeRegion == null)
            {
                AddPointToCloud(point);
                return true;
            }

            SonarDensityRegion configRegion = volumeRegion == null && config.useDensityRegions ? GetDensityRegion(point.position, out regionWeight) : null;
            float outsideKeepProbability = config.useDensityRegions ? Mathf.Clamp01(config.outsideRegionKeepProbability) : 1f;
            float targetKeepProbability = volumeRegion != null
                ? Mathf.Clamp01(volumeRegion.keepProbability)
                : configRegion != null
                    ? Mathf.Clamp01(configRegion.keepProbability)
                    : outsideKeepProbability;
            float keepProbability = Mathf.Lerp(outsideKeepProbability, targetKeepProbability, regionWeight);
            uint state = GetDensityRandomState(point, hit);

            if (Next01(ref state) > keepProbability)
            {
                return false;
            }

            AddPointToCloud(point);

            int extraCount = 0;
            float lateralJitterMeters = 0f;
            float normalThicknessMeters = 0f;
            float thicknessVariation = 0f;
            float thicknessPatchScaleMeters = 1f;
            float thicknessPatchVariation = 0f;
            if (volumeRegion != null)
            {
                extraCount = volumeRegion.extraPointsPerHit;
                lateralJitterMeters = volumeRegion.lateralJitterMeters;
                normalThicknessMeters = volumeRegion.normalThicknessMeters;
                thicknessVariation = volumeRegion.thicknessVariation;
                thicknessPatchScaleMeters = volumeRegion.thicknessPatchScaleMeters;
                thicknessPatchVariation = volumeRegion.thicknessPatchVariation;
            }
            else if (configRegion != null)
            {
                extraCount = configRegion.extraPointsPerHit;
                lateralJitterMeters = configRegion.lateralJitterMeters;
                normalThicknessMeters = configRegion.normalThicknessMeters;
                thicknessVariation = configRegion.thicknessVariation;
                thicknessPatchScaleMeters = configRegion.thicknessPatchScaleMeters;
                thicknessPatchVariation = configRegion.thicknessPatchVariation;
            }
            else
            {
                return true;
            }

            float blendedExtraCount = Mathf.Clamp(extraCount, 0, 24) * regionWeight;
            extraCount = Mathf.FloorToInt(blendedExtraCount);
            if (Next01(ref state) < blendedExtraCount - extraCount)
            {
                extraCount++;
            }

            float blendedLateralJitter = lateralJitterMeters * regionWeight;
            float patchThicknessMultiplier = GetThicknessPatchMultiplier(point.position, thicknessPatchScaleMeters, thicknessPatchVariation, config.densityRandomSeed);
            float blendedNormalThickness = normalThicknessMeters * regionWeight * patchThicknessMultiplier;
            for (int i = 0; i < extraCount; i++)
            {
                Vector3 offset = GetDensityJitterOffset(hit, blendedLateralJitter, blendedNormalThickness, thicknessVariation, ref state);
                AddPointToCloud(ClonePointWithPosition(point, point.position + offset));
            }

            return true;
        }

        private void AddPointToCloud(SonarPoint point)
        {
            pointCloud.AddPoint(point);
            recentPoints.Add(point);
        }

        private static bool IsTerrainHit(BeamHitResult hit)
        {
            return hit.collider != null && hit.collider.GetComponent<Terrain>() != null;
        }

        private static SonarDensityRegionVolume GetDensityVolumeRegion(Vector3 worldPosition, out float regionWeight)
        {
            return SonarDensityRegionVolume.TryGetRegion(worldPosition, out SonarDensityRegionVolume region, out regionWeight)
                ? region
                : null;
        }

        private SonarDensityRegion GetDensityRegion(Vector3 worldPosition, out float regionWeight)
        {
            regionWeight = 0f;
            if (config.densityRegions == null)
            {
                return null;
            }

            SonarDensityRegion bestRegion = null;
            for (int i = 0; i < config.densityRegions.Length; i++)
            {
                SonarDensityRegion region = config.densityRegions[i];
                if (region == null || !region.enabled)
                {
                    continue;
                }

                float weight = GetDensityRegionBlendWeight(worldPosition, region);
                if (weight > regionWeight)
                {
                    bestRegion = region;
                    regionWeight = weight;
                }
            }

            return bestRegion;
        }

        private static float GetDensityRegionBlendWeight(Vector3 worldPosition, SonarDensityRegion region)
        {
            Vector3 halfSize = new Vector3(
                Mathf.Max(0.001f, region.size.x) * 0.5f,
                Mathf.Max(0.001f, region.size.y) * 0.5f,
                Mathf.Max(0.001f, region.size.z) * 0.5f);
            Vector3 local = worldPosition - region.center;
            float signedDistance = region.shape == SonarDensityRegionShape.Ellipsoid
                ? GetSphereSignedDistance(local, halfSize)
                : GetBoxSignedDistance(local, halfSize);

            if (region.blendDistanceMeters <= 0f)
            {
                return signedDistance >= 0f ? 1f : 0f;
            }

            float t = Mathf.InverseLerp(-region.blendDistanceMeters, region.blendDistanceMeters, signedDistance);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        private static float GetBoxSignedDistance(Vector3 local, Vector3 halfSize)
        {
            Vector3 q = new Vector3(
                Mathf.Abs(local.x) - Mathf.Max(0.001f, halfSize.x),
                Mathf.Abs(local.y) - Mathf.Max(0.001f, halfSize.y),
                Mathf.Abs(local.z) - Mathf.Max(0.001f, halfSize.z));
            Vector3 outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f));
            float outsideDistance = outside.magnitude;
            float insideDistance = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return -(outsideDistance + insideDistance);
        }

        private static float GetSphereSignedDistance(Vector3 local, Vector3 halfSize)
        {
            float radius = Mathf.Max(0.001f, Mathf.Min(halfSize.x, Mathf.Min(halfSize.y, halfSize.z)));
            Vector3 normalized = new Vector3(
                local.x / Mathf.Max(0.001f, halfSize.x),
                local.y / Mathf.Max(0.001f, halfSize.y),
                local.z / Mathf.Max(0.001f, halfSize.z));
            return (1f - normalized.magnitude) * radius;
        }

        private static float GetThicknessPatchMultiplier(Vector3 worldPosition, float patchScaleMeters, float patchVariation, int seed)
        {
            float variation = Mathf.Clamp01(patchVariation);
            if (variation <= 0f)
            {
                return 1f;
            }

            float scale = Mathf.Max(0.01f, patchScaleMeters);
            Vector3 p = worldPosition / scale;
            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);
            float tx = Smooth01(p.x - x0);
            float ty = Smooth01(p.y - y0);
            float tz = Smooth01(p.z - z0);

            float c000 = Hash01(x0, y0, z0, seed);
            float c100 = Hash01(x0 + 1, y0, z0, seed);
            float c010 = Hash01(x0, y0 + 1, z0, seed);
            float c110 = Hash01(x0 + 1, y0 + 1, z0, seed);
            float c001 = Hash01(x0, y0, z0 + 1, seed);
            float c101 = Hash01(x0 + 1, y0, z0 + 1, seed);
            float c011 = Hash01(x0, y0 + 1, z0 + 1, seed);
            float c111 = Hash01(x0 + 1, y0 + 1, z0 + 1, seed);

            float x00 = Mathf.Lerp(c000, c100, tx);
            float x10 = Mathf.Lerp(c010, c110, tx);
            float x01 = Mathf.Lerp(c001, c101, tx);
            float x11 = Mathf.Lerp(c011, c111, tx);
            float y0v = Mathf.Lerp(x00, x10, ty);
            float y1v = Mathf.Lerp(x01, x11, ty);
            float noise = Mathf.Lerp(y0v, y1v, tz);
            return Mathf.Lerp(1f, noise * 2f, variation);
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= (uint)x * 73856093u;
                h ^= (uint)y * 19349663u;
                h ^= (uint)z * 83492791u;
                return (Hash(h) & 0x00ffffff) / 16777216f;
            }
        }

        private static Vector3 GetDensityJitterOffset(BeamHitResult hit, float lateralJitterMeters, float normalThicknessMeters, float thicknessVariation, ref uint state)
        {
            Vector3 normal = hit.normal == Vector3.zero ? Vector3.up : hit.normal.normalized;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            float lateralRadius = Mathf.Max(0f, lateralJitterMeters);
            float variation = Mathf.Clamp01(thicknessVariation);
            float normalRadius = Mathf.Max(0f, normalThicknessMeters) * Mathf.Lerp(1f - variation, 1f, Next01(ref state));
            float angle = Next01(ref state) * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(Next01(ref state)) * lateralRadius;
            float normalOffset = (Next01(ref state) * 2f - 1f) * normalRadius;

            return tangent * (Mathf.Cos(angle) * radius)
                + bitangent * (Mathf.Sin(angle) * radius)
                + normal * normalOffset;
        }

        private static SonarPoint ClonePointWithPosition(SonarPoint point, Vector3 position)
        {
            return new SonarPoint(
                position,
                point.intensity,
                point.yawDeg,
                point.tiltDeg,
                point.rangeMeters,
                point.geometricRangeMeters,
                point.acousticRangeMeters,
                point.travelTimeSeconds,
                point.sampleTimeSeconds);
        }

        private uint GetDensityRandomState(SonarPoint point, BeamHitResult hit)
        {
            unchecked
            {
                uint hash = (uint)config.densityRandomSeed;
                hash ^= (uint)Mathf.RoundToInt(point.position.x * 1000f) * 73856093u;
                hash ^= (uint)Mathf.RoundToInt(point.position.y * 1000f) * 19349663u;
                hash ^= (uint)Mathf.RoundToInt(point.position.z * 1000f) * 83492791u;
                hash ^= (uint)Mathf.RoundToInt(hit.yawDeg * 1000f) * 2654435761u;
                hash ^= (uint)Mathf.RoundToInt(hit.tiltDeg * 1000f) * 1597334677u;
                return Hash(hash);
            }
        }

        private static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x == 0 ? 1u : x;
        }

        private static float Next01(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00ffffff) / 16777216f;
        }

        private Vector3 GetMeasuredOutputPoint(BeamHitResult hit)
        {
            if (!IsAcousticRangeActive() || !config.useAcousticRangeForPointPosition || hit.range <= 0f)
            {
                return hit.point;
            }

            Vector3 bearing = hit.initialDirection != Vector3.zero
                ? hit.initialDirection.normalized
                : (hit.point - hit.origin).normalized;

            if (bearing == Vector3.zero)
            {
                return hit.point;
            }

            return hit.origin + bearing * hit.range;
        }

        private void DrawActiveScanFan(Vector3 origin, Vector3 centerDirection, Vector3 verticalFanAxis)
        {
            if (config == null || !config.showDebugRays || !config.showFullRangeScanRay || centerDirection == Vector3.zero)
            {
                return;
            }

            Vector3 rayOrigin = origin;
            float range = Mathf.Max(0.01f, config.maxRange);
            float halfWidth = Mathf.Max(0f, config.beamWidthDeg) * 0.5f;
            Vector3 axis = verticalFanAxis == Vector3.zero ? Vector3.forward : verticalFanAxis.normalized;
            Vector3 endPoint = rayOrigin + centerDirection.normalized * range;

            currentScanRayOrigin = rayOrigin;
            currentScanRayEnd = endPoint;
            hasCurrentScanRay = true;
            currentFanRayCount = 0;

            if (config.showCurrentFanRays && halfWidth > 0f)
            {
                Vector3 lowerDirection = Quaternion.AngleAxis(-halfWidth, axis) * centerDirection.normalized;
                Vector3 upperDirection = Quaternion.AngleAxis(halfWidth, axis) * centerDirection.normalized;
                currentFanRayEnds[currentFanRayCount++] = rayOrigin + lowerDirection * range;
                currentFanRayEnds[currentFanRayCount++] = endPoint;
                currentFanRayEnds[currentFanRayCount++] = rayOrigin + upperDirection * range;
            }
            else
            {
                currentFanRayEnds[currentFanRayCount++] = endPoint;
            }

            for (int i = 0; i < currentFanRayCount; i++)
            {
                Debug.DrawLine(rayOrigin, currentFanRayEnds[i], config.scanRayColor, GetRayDrawDuration());
            }
        }

        private void SavePointCloud(string fileName)
        {
            string path = csvWriter.Save(pointCloud, fileName);
            string coordinateSystem = config.exportCloudCompareZUp ? "CloudCompare Z-up (x=Unity x, y=Unity z, z=Unity y)" : "Unity Y-up";
            Debug.Log($"Sonar CSV saved: {path} ({pointCloud.Count} points, coordinates={coordinateSystem})");
        }

        private void LogScanPlan(string label, float[] tiltAngles)
        {
            float sweepAngle = GetSweepAngle();
            int yawSamples = Mathf.Max(1, Mathf.CeilToInt(sweepAngle / Mathf.Max(0.001f, config.yawResolutionDeg)));
            float yawStep = sweepAngle / yawSamples;
            int beamsPerYaw = GetBeamRayCount();
            int tiltSweepCount = tiltAngles == null ? 0 : tiltAngles.Length;
            int totalRaycasts = tiltSweepCount * yawSamples * beamsPerYaw;
            float scanDurationSeconds = yawSamples * GetSampleIntervalSeconds(yawStep);

            Debug.Log(
                $"{label} started. tiltSweeps={tiltSweepCount}, yawSamplesPerSweep={yawSamples}, yawStep={yawStep:F3} deg, " +
                $"beamsPerYaw={beamsPerYaw}, estimatedRaycasts={totalRaycasts}, scanSpeed={config.scanSpeedDegPerSecond:F3} deg/s, " +
                $"sampleInterval={GetSampleIntervalSeconds(yawStep):F3} s, scanDurationPerSweep={scanDurationSeconds:F1} s, " +
                $"rawTiltAngleDeg={config.tiltAngleDeg:F2}, scanAllTiltAngles={config.scanAllTiltAngles}, " +
                $"selectedTilt={GetTiltSummary(tiltAngles)}, verticalFan={config.beamWidthDeg:F1} deg, maxRange={config.maxRange:F2} m, " +
                $"temperatureInfluenceMode={config.temperatureInfluenceMode}, temperatureProfile={IsTemperatureProfileActive()}.");
        }

        private int GetBeamRayCount()
        {
            if (config.beamWidthDeg <= 0f)
            {
                return 1;
            }

            return Mathf.Max(1, Mathf.CeilToInt(config.beamWidthDeg / Mathf.Max(0.001f, config.beamResolutionDeg))) + 1;
        }

        private string GetTiltSummary(float[] tiltAngles)
        {
            if (tiltAngles == null || tiltAngles.Length == 0)
            {
                return "none";
            }

            if (tiltAngles.Length == 1)
            {
                return $"{tiltAngles[0]:F2} deg";
            }

            return $"{tiltAngles.Length} angles";
        }

        private float GetRayDrawDuration()
        {
            float delayBasedDuration = config.animateScan ? config.scanStepDelaySeconds * 1.25f : 0f;
            return Mathf.Max(0.02f, config.scanRayDuration, delayBasedDuration);
        }

        private void LogProgressIfNeeded(int tiltIndex, float tiltDeg, int yawIndex, int yawSamples, int startPointCount)
        {
            if (!config.logScanProgress)
            {
                return;
            }

            int interval = Mathf.Max(1, config.progressLogIntervalSamples);
            bool isLastSample = yawIndex == yawSamples - 1;
            if (!isLastSample && yawIndex % interval != 0)
            {
                return;
            }

            float progress = (yawIndex + 1) / (float)yawSamples * 100f;
            int addedPoints = recentPoints.Count - startPointCount;
            Debug.Log($"Scanning tilt[{tiltIndex}]={tiltDeg:F1} deg: yaw {yawIndex + 1}/{yawSamples} ({progress:F1}%), points in this sweep={addedPoints}");
        }

        private void LogScanFinished(string label)
        {
            if (recentPoints.Count == 0)
            {
                Debug.LogWarning($"{label} finished with 0 points. CSV is still saved with a header row. Check target Colliders, Layer Mask, Max Range, and the selected sonar forward axis.");
                LogZeroHitDiagnostics();
                return;
            }

            string refractionStats = GetRefractionStatsSummary();
            Debug.Log($"{label} finished. Points={recentPoints.Count}, TemperatureInfluenceMode={config.temperatureInfluenceMode}, TemperatureProfile={IsTemperatureProfileActive()}.{refractionStats}");
        }

        private void ResetRefractionStats()
        {
            totalRefractionBendDeg = 0f;
            maxRefractionBendDeg = 0f;
            refractionHitCount = 0;
            totalStraightComparisonDistance = 0f;
            maxStraightComparisonDistance = 0f;
            straightComparisonCount = 0;
            totalAcousticRangeDelta = 0f;
            maxAcousticRangeDelta = 0f;
            acousticRangeHitCount = 0;
        }

        private void AccumulateRefractionStats(BeamHitResult hit)
        {
            if (IsAcousticRangeActive() && hit.geometricRange > 0f)
            {
                float delta = hit.acousticRange - hit.geometricRange;
                totalAcousticRangeDelta += delta;
                maxAcousticRangeDelta = Mathf.Max(maxAcousticRangeDelta, Mathf.Abs(delta));
                acousticRangeHitCount++;
            }

            if (!IsRefractionActive())
            {
                return;
            }

            refractionHitCount++;
            totalRefractionBendDeg += hit.refractionBendDeg;
            maxRefractionBendDeg = Mathf.Max(maxRefractionBendDeg, hit.refractionBendDeg);

            if (hit.straightComparisonDistance >= 0f)
            {
                straightComparisonCount++;
                totalStraightComparisonDistance += hit.straightComparisonDistance;
                maxStraightComparisonDistance = Mathf.Max(maxStraightComparisonDistance, hit.straightComparisonDistance);
            }
        }

        private string GetRefractionStatsSummary()
        {
            string summary = string.Empty;

            if (acousticRangeHitCount > 0)
            {
                float averageRangeDelta = totalAcousticRangeDelta / acousticRangeHitCount;
                summary += $" AcousticRangeStats: hits={acousticRangeHitCount}, avgDelta={averageRangeDelta:F6} m, maxAbsDelta={maxAcousticRangeDelta:F6} m";
            }

            if (!IsRefractionActive() || refractionHitCount == 0)
            {
                return summary;
            }

            float averageBend = totalRefractionBendDeg / refractionHitCount;
            summary += $" RefractionStats: hits={refractionHitCount}, avgBend={averageBend:F4} deg, maxBend={maxRefractionBendDeg:F4} deg";

            if (straightComparisonCount > 0)
            {
                float averageDistance = totalStraightComparisonDistance / straightComparisonCount;
                summary += $", avgStraightDelta={averageDistance:F6} m, maxStraightDelta={maxStraightComparisonDistance:F6} m";
            }

            return summary;
        }

        private bool IsTemperatureProfileActive()
        {
            return config != null && config.temperatureInfluenceMode != TemperatureInfluenceMode.None && temperatureProfile != null && temperatureProfile.HasUsableProfile;
        }

        private bool IsAcousticRangeActive()
        {
            return IsTemperatureProfileActive()
                && (config.temperatureInfluenceMode == TemperatureInfluenceMode.RangeOnly
                    || config.temperatureInfluenceMode == TemperatureInfluenceMode.RefractionAndRange);
        }

        private bool IsRefractionActive()
        {
            return IsTemperatureProfileActive() && config.temperatureInfluenceMode == TemperatureInfluenceMode.RefractionAndRange;
        }

        private void LogZeroHitDiagnostics()
        {
            if (config == null || !config.logZeroHitDiagnostics)
            {
                return;
            }

            Quaternion yawBeforeDiagnostics = yawRig.localRotation;
            Quaternion tiltBeforeDiagnostics = tiltRig.localRotation;
            float tiltDeg = config.scanAllTiltAngles && config.tiltAnglesDeg != null && config.tiltAnglesDeg.Length > 0
                ? config.tiltAnglesDeg[0]
                : config.tiltAngleDeg;
            float maxRange = Mathf.Max(0.01f, config.maxRange);
            int configuredMaskHits = 0;
            int anyLayerHits = 0;
            int configuredFanHits = 0;
            int anyLayerFanHits = 0;
            int actualTracerHits = 0;
            string firstAnyLayerHit = "none";
            float[] testYawAngles = new float[] { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };
            float halfFan = Mathf.Max(0f, config.beamWidthDeg) * 0.5f;
            float[] testFanAngles = new float[] { -halfFan, 0f, halfFan };

            foreach (float yawDeg in testYawAngles)
            {
                ApplyRigAngles(yawDeg, tiltDeg);
                GetBeamFrameFromRig(out Vector3 direction, out Vector3 fanAxis);

                if (Physics.Raycast(sonarHead.position, direction, out RaycastHit configuredHit, maxRange, config.layerMask, QueryTriggerInteraction.Ignore))
                {
                    configuredMaskHits++;
                }

                if (Physics.Raycast(sonarHead.position, direction, out RaycastHit anyHit, maxRange, ~0, QueryTriggerInteraction.Ignore))
                {
                    anyLayerHits++;
                    if (firstAnyLayerHit == "none")
                    {
                        firstAnyLayerHit = $"{anyHit.collider.name}, layer={LayerMask.LayerToName(anyHit.collider.gameObject.layer)}, distance={anyHit.distance:F3} m";
                    }
                }

                foreach (float fanAngle in testFanAngles)
                {
                    Vector3 fanDirection = Quaternion.AngleAxis(fanAngle, fanAxis) * direction;

                    if (Physics.Raycast(sonarHead.position, fanDirection, out _, maxRange, config.layerMask, QueryTriggerInteraction.Ignore))
                    {
                        configuredFanHits++;
                    }

                    if (Physics.Raycast(sonarHead.position, fanDirection, out RaycastHit anyFanHit, maxRange, ~0, QueryTriggerInteraction.Ignore))
                    {
                        anyLayerFanHits++;
                        if (firstAnyLayerHit == "none")
                        {
                            firstAnyLayerHit = $"{anyFanHit.collider.name}, layer={LayerMask.LayerToName(anyFanHit.collider.gameObject.layer)}, distance={anyFanHit.distance:F3} m";
                        }
                    }
                }

                List<BeamHitResult> actualHits = beamTracer.TraceFanBeam(sonarHead.position, direction, fanAxis, yawDeg, tiltDeg);
                actualTracerHits += actualHits.Count;
            }

            int nearbyColliderCount = Physics.OverlapSphere(sonarHead.position, maxRange, ~0, QueryTriggerInteraction.Ignore).Length;
            yawRig.localRotation = yawBeforeDiagnostics;
            tiltRig.localRotation = tiltBeforeDiagnostics;

            Debug.LogWarning(
                $"Zero-hit diagnostics: tested {testYawAngles.Length} center rays and {testYawAngles.Length * testFanAngles.Length} fan-edge/center rays at tilt={tiltDeg:F2} deg, maxRange={maxRange:F2} m. " +
                $"configuredCenterHits={configuredMaskHits}, allLayerCenterHits={anyLayerHits}, configuredFanHits={configuredFanHits}, allLayerFanHits={anyLayerFanHits}, " +
                $"actualTracerHits={actualTracerHits}, temperatureInfluenceMode={config.temperatureInfluenceMode}, temperatureProfile={IsTemperatureProfileActive()}, nearbyCollidersWithinRange={nearbyColliderCount}, firstAllLayerHit={firstAnyLayerHit}. " +
                $"Sonar position={sonarHead.position}, zero-yaw direction={GetZeroYawWorldDirection()}. " +
                "If allLayerFanHits > 0 but actualTracerHits = 0 while Use Refraction is true, refraction is bending the ray away; reduce refraction settings or disable Use Refraction. " +
                "If allLayerFanHits > 0 but configuredFanHits = 0, fix Layer Mask. If all hits are 0 and nearbyCollidersWithinRange is 0, increase Max Range or move the sonar/target.");
        }

        private Vector3 GetZeroYawWorldDirection()
        {
            return config.forwardAxis == SonarForwardAxis.LocalZ ? sonarHead.forward : sonarHead.right;
        }

        private string GetCombinedOutputFileName()
        {
            string prefix = GetSafePrefix();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{prefix}_{timestamp}.csv";
        }

        private string GetTiltOutputFileName(int tiltIndex, float tiltDeg)
        {
            string prefix = GetSafePrefix();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{prefix}_{timestamp}_tilt_{tiltIndex:00}_{tiltDeg:+0.###;-0.###;0}deg.csv";
        }

        private string GetSafePrefix()
        {
            return string.IsNullOrWhiteSpace(config.filePrefix) ? "sonar_scan" : config.filePrefix.Trim();
        }

        private void OnDrawGizmosSelected()
        {
            if (config == null)
            {
                return;
            }

            if (config.showDebugRays && config.showOnlyCurrentScanRay && hasCurrentScanRay)
            {
                Gizmos.color = config.scanRayColor;
                if (config.showCurrentFanRays && currentFanRayCount > 0)
                {
                    for (int i = 0; i < currentFanRayCount; i++)
                    {
                        Gizmos.DrawLine(currentScanRayOrigin, currentFanRayEnds[i]);
                    }
                }
                else
                {
                    Gizmos.DrawLine(currentScanRayOrigin, currentScanRayEnd);
                }
            }

            if (config.showPoints && recentPoints.Count > 0)
            {
                Gizmos.color = isScanning ? Color.green : Color.yellow;
                int stride = Mathf.Max(1, config.debugPointStride);

                for (int i = 0; i < recentPoints.Count; i += stride)
                {
                    Gizmos.DrawSphere(recentPoints[i].position, 0.05f);
                }
            }

            DrawDensityRegionGizmos();
        }

        private void DrawDensityRegionGizmos()
        {
            if (config == null || !config.useDensityRegions || config.densityRegions == null)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Gizmos.color = new Color(1f, 0.8f, 0.15f, 0.65f);

            for (int i = 0; i < config.densityRegions.Length; i++)
            {
                SonarDensityRegion region = config.densityRegions[i];
                if (region == null || !region.enabled)
                {
                    continue;
                }

                if (region.shape == SonarDensityRegionShape.Ellipsoid)
                {
                    Matrix4x4 previousMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(region.center, Quaternion.identity, region.size);
                    Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
                    Gizmos.matrix = previousMatrix;
                }
                else
                {
                    Gizmos.DrawWireCube(region.center, region.size);
                }
            }

            Gizmos.color = previousColor;
        }
    }
}
