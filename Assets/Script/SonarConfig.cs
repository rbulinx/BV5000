using System;
using UnityEngine;

namespace BV5000.Sonar
{
    public enum SonarForwardAxis
    {
        LocalX,
        LocalZ
    }

    public enum TemperatureInfluenceMode
    {
        None,
        RangeOnly,
        RefractionAndRange
    }

    public enum SoundSpeedFormula
    {
        FreshWater,
        Mackenzie1981
    }

    public enum SonarDensityRegionShape
    {
        Box,
        Ellipsoid
    }

    [Serializable]
    public class SonarDensityRegion
    {
        public bool enabled = true;
        public SonarDensityRegionShape shape = SonarDensityRegionShape.Box;
        public Vector3 center = Vector3.zero;
        public Vector3 size = new Vector3(5f, 2f, 5f);

        [Range(0f, 1f)]
        [Tooltip("Probability that the original hit point is kept inside this region.")]
        public float keepProbability = 1f;

        [Range(0, 24)]
        [Tooltip("Additional points created around each kept hit inside this region.")]
        public int extraPointsPerHit = 3;

        [Min(0f)]
        [Tooltip("Distance in meters used to fade density around this region boundary.")]
        public float blendDistanceMeters = 1f;

        [Min(0f)]
        [Tooltip("Random spread along the hit surface, in meters.")]
        public float lateralJitterMeters = 0.04f;

        [Min(0f)]
        [Tooltip("Random spread along the hit normal, in meters. This gives the point cloud visible thickness.")]
        public float normalThicknessMeters = 0.12f;

        [Range(0f, 1f)]
        [Tooltip("Randomizes the effective thickness per added point. 0 keeps a fixed thickness, 1 allows very thin to full thickness.")]
        public float thicknessVariation = 0.65f;

        [Min(0.01f)]
        [Tooltip("Size of broad thick/thin patches in meters. Larger values create wider areas with similar thickness.")]
        public float thicknessPatchScaleMeters = 4f;

        [Range(0f, 1f)]
        [Tooltip("Varies the maximum thickness by world position. 0 keeps a uniform envelope, 1 allows very thin to about double thickness patches.")]
        public float thicknessPatchVariation = 0.6f;
    }

    public class SonarConfig : MonoBehaviour
    {
        public const float BV5000SectorDeg = 45f;
        public const int BV5000BeamCount = 256;
        public const float BV5000AngularResolutionDeg = BV5000SectorDeg / (BV5000BeamCount - 1);

        [Header("BV5000 Sampling Preset")]
        [Tooltip("Force BV5000-style sampling density at scan time, even if an open scene still has older serialized values.")]
        public bool useBV5000SamplingPreset = true;

        [Header("Scan Geometry")]
        [Min(0.01f)]
        [Tooltip("Maximum sensing range in meters.")]
        public float maxRange = 30f;

        [Min(0.001f)]
        [Tooltip("Yaw angular resolution in degrees. Use about 0.176 degrees for BV5000-style dense 360 degree point clouds.")]
        public float yawResolutionDeg = BV5000AngularResolutionDeg;

        [Min(0.1f)]
        [Tooltip("Mechanical scan speed in degrees per second. BV5000-style scan setup is commonly expressed as deg/sec.")]
        public float scanSpeedDegPerSecond = 1f;

        [Tooltip("When enabled, sample time is computed from yaw step / scan speed. When disabled, sampleIntervalSeconds is used directly.")]
        public bool useScanSpeedForTiming = true;

        [Tooltip("When enabled, animated scans wait for the real sample interval from scan speed. At 1 deg/sec and 360 degrees this takes about 6 minutes.")]
        public bool animateUsingScanSpeedTiming = false;

        [Tooltip("Starting yaw angle in degrees relative to this GameObject's local forward direction.")]
        public float yawStartDeg = 0f;

        [Tooltip("Ending yaw angle in degrees. A full sweep is usually 0..360.")]
        public float yawEndDeg = 360f;

        [Tooltip("Local axis used as the sonar zero-yaw range direction. Use LocalZ if your sonar GameObject was aimed with Unity's blue forward axis.")]
        public SonarForwardAxis forwardAxis = SonarForwardAxis.LocalZ;

        [Tooltip("Tilt angles in degrees. Each tilt performs a yaw sweep.")]
        public float[] tiltAnglesDeg = new float[] { -20f, -10f, 0f, 10f, 20f };

        [Tooltip("Tilt angle in degrees for a single 360 degree scan. Negative values are allowed.")]
        public float tiltAngleDeg = 0f;

        [Tooltip("When false, one scan uses tiltAngleDeg and stops after one 360 degree yaw sweep. Enable this only when you intentionally want multiple full 360 sweeps from tiltAnglesDeg.")]
        public bool scanAllTiltAngles = false;

        [Min(0.0001f)]
        [Tooltip("Fallback acquisition time in seconds per yaw sample when useScanSpeedForTiming is disabled.")]
        public float sampleIntervalSeconds = 0.01f;

        [Header("Beam Geometry")]
        [Min(0f)]
        [Tooltip("Fan beam width in degrees. Use 0 for a single ray per yaw sample.")]
        public float beamWidthDeg = BV5000SectorDeg;

        [Min(0.001f)]
        [Tooltip("Angular spacing inside the fan beam in degrees. BV5000-style density is 45 degrees / 255 intervals = 256 beams.")]
        public float beamResolutionDeg = BV5000AngularResolutionDeg;

        [Header("Logging")]
        [Tooltip("Print scan progress to the Console while scanning.")]
        public bool logScanProgress = true;

        [Min(1)]
        [Tooltip("Print one progress log every N yaw samples.")]
        public int progressLogIntervalSamples = 30;

        [Tooltip("When refraction is enabled, also trace the straight ray for diagnostics and log average hit-point difference.")]
        public bool compareStraightAndRefractedHits = true;

        [Tooltip("When a scan records zero points, cast a few diagnostic rays with both the configured layer mask and all layers.")]
        public bool logZeroHitDiagnostics = true;

        [Header("Ray Tracing")]
        [Tooltip("None: no temperature effect. RangeOnly: sound speed changes range only. RefractionAndRange: sound speed changes both ray bending and range.")]
        public TemperatureInfluenceMode temperatureInfluenceMode = TemperatureInfluenceMode.None;

        [Min(0.01f)]
        [Tooltip("Segment length in meters for acoustic range integration and Layered Snell ray tracing.")]
        public float stepLength = 0.25f;

        [Header("Acoustic Range")]
        [Tooltip("Move exported points along the emitted beam by the acoustic range. Disable this to export the physical collider hit point while still logging acoustic_range_m.")]
        public bool useAcousticRangeForPointPosition = true;

        [Header("Temperature Profile")]
        [Tooltip("CSV file path for the temperature profile. Relative paths are resolved from the Assets folder. Format: depth_m,temperature_c")]
        public string temperatureProfileCsvPath = "TemperatureProfiles/temperature_profile.csv";

        [Header("Sound Speed Model")]
        [Tooltip("FreshWater is suitable for reservoirs/tanks. Mackenzie1981 is for seawater and uses salinity.")]
        public SoundSpeedFormula soundSpeedFormula = SoundSpeedFormula.FreshWater;

        [Tooltip("Reference water temperature used when the temperature profile is disabled, and for range reference sound speed.")]
        public float referenceWaterTemperatureC = 20f;

        [Min(0f)]
        [Tooltip("Salinity in PSU/ppt used by Mackenzie1981. Fresh water is 0, typical seawater is about 35.")]
        public float salinityPsu = 0f;

        [Header("Intensity Model")]
        [Min(0.01f)]
        [Tooltip("Gamma exponent for angle-based intensity.")]
        public float intensityGamma = 1f;

        [Tooltip("Apply simple distance attenuation after angle intensity.")]
        public bool useRangeAttenuation = false;

        [Min(0f)]
        [Tooltip("Distance attenuation coefficient. Larger values make far hits darker.")]
        public float attenuationCoeff = 0.01f;

        [Header("Output")]
        [Tooltip("Output folder relative to the Unity project's Assets folder.")]
        public string outputFolder = "SonarOutput";

        [Tooltip("CSV filename prefix.")]
        public string filePrefix = "sonar_scan";

        [Tooltip("Save one CSV file per tilt angle instead of one combined file.")]
        public bool savePerTilt = false;

        [Tooltip("When enabled, CSV contains yaw_deg, tilt_deg, and range_m after x,y,z,i.")]
        public bool saveExtraColumns = true;

        [Tooltip("Export CSV coordinates as CloudCompare-style Z-up: output x=Unity x, output y=Unity z, output z=Unity y.")]
        public bool exportCloudCompareZUp = true;

        [Header("Physics")]
        [Tooltip("Only colliders on these layers are visible to the virtual sonar.")]
        public LayerMask layerMask = ~0;

        [Header("Point Cloud Density")]
        [Tooltip("Enable post-hit density shaping. Use this to make sparse terrain with locally thicker point clouds.")]
        public bool useDensityRegions = false;

        [Range(0f, 1f)]
        [Tooltip("Probability that a hit outside all density regions is kept. Lower values make the general terrain look sparse.")]
        public float outsideRegionKeepProbability = 1f;

        [Tooltip("World-space regions where hits become denser and thicker.")]
        public SonarDensityRegion[] densityRegions = new SonarDensityRegion[]
        {
            new SonarDensityRegion()
        };

        [Tooltip("Seed used for deterministic thinning and jitter.")]
        public int densityRandomSeed = 5000;

        [Header("Debug Display")]
        public bool showDebugRays = false;
        public bool showPoints = false;

        [Tooltip("When enabled, Run On Start uses an animated scan so the current ray can be seen in the Scene view.")]
        public bool animateScan = true;

        [Min(0f)]
        [Tooltip("Delay in seconds after each yaw sample during animated scans.")]
        public float scanStepDelaySeconds = 0.01f;

        [Min(0.01f)]
        [Tooltip("How long Debug.DrawLine scan rays remain visible.")]
        public float scanRayDuration = 0.1f;

        [Tooltip("Draw a full-range ray for the active yaw sample, even when there is no hit.")]
        public bool showFullRangeScanRay = true;

        [Tooltip("Draw only the current active scan ray as a Gizmo. This avoids old yaw samples looking like a horizontal fan in top view.")]
        public bool showOnlyCurrentScanRay = true;

        [Tooltip("Draw the current vertical fan as lower/center/upper rays. From top view these should almost overlap; from side view the 42 degree vertical spread is visible.")]
        public bool showCurrentFanRays = true;

        public Color scanRayColor = Color.green;
        public Color hitRayColor = Color.cyan;

        [Min(1)]
        [Tooltip("Draw every Nth point in Gizmos.")]
        public int debugPointStride = 1;
    }
}
