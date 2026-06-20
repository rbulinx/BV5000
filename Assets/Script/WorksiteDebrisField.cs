using System.Collections.Generic;
using UnityEngine;

namespace BV5000.Sonar
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WorksiteDebrisField : MonoBehaviour
    {
        public enum FieldShape
        {
            Ellipsoid,
            Box
        }

        [Header("Field")]
        public bool debrisEnabled = true;
        public FieldShape fieldShape = FieldShape.Ellipsoid;
        [Range(0, 2000)] public int debrisCount = 120;
        public Vector3 fieldSize = new Vector3(8f, 3f, 8f);
        public int seed = 20260604;

        [Header("Sonar Return")]
        public bool visibleToSonar = true;
        [Range(0.02f, 1f)] public float hitRadiusMeters = 0.18f;
        [Range(0f, 1f)] public float reflectivity = 0.75f;
        [Range(0f, 1f)] public float speckle = 0.35f;
        [Range(0f, 0.2f)] public float absorption = 0.025f;
        [Range(0f, 1f)] public float threshold = 0.02f;
        [Range(1, 256)] public int maxSamplesCheckedPerRay = 32;

        [Header("Visual Debris")]
        public bool showVisualDebris = true;
        [Range(0, 1000)] public int maxVisualDebrisCount = 300;
        public Vector2 visualSizeRange = new Vector2(0.03f, 0.18f);
        [Range(0f, 1f)] public float visualAlpha = 0.55f;
        public bool drawDebrisGizmos = true;
        public Color darkColor = new Color(0.32f, 0.28f, 0.20f, 1f);
        public Color lightColor = new Color(0.78f, 0.72f, 0.55f, 1f);
        [Range(0f, 1f)] public float plateRatio = 0.7f;
        [Range(0.1f, 1f)] public float visualSizeMultiplier = 0.55f;

        [Header("Water Surface")]
        public bool hideAboveWaterSurface = true;
        public float waterSurfaceY = 0f;
        public float waterSurfaceMargin = 0.05f;

        [Header("Motion")]
        public bool animateInPlayMode = true;
        public Vector3 currentVelocity = new Vector3(0.004f, 0.001f, -0.006f);
        public Vector2 randomDriftSpeed = new Vector2(0.001f, 0.01f);
        public float bobAmplitude = 0.025f;
        public float bobFrequency = 0.12f;

        private static readonly List<WorksiteDebrisField> ActiveFields = new List<WorksiteDebrisField>();

        private readonly List<DebrisSample> samples = new List<DebrisSample>();
        private readonly List<Material> materials = new List<Material>();
        private Mesh roundMesh;
        private Mesh shardMesh;
        private int builtSeed;
        private int builtCount;
        private bool builtVisuals;
        private int builtVisualLimit;
        private FieldShape builtFieldShape;
        private Vector3 builtFieldSize;
        private Vector2 builtVisualSizeRange;
        private float builtVisualSizeMultiplier;
        private float builtHitRadiusMeters;
        private float builtVisualAlpha;
        private Color builtDarkColor;
        private Color builtLightColor;
        private bool builtHideAboveWaterSurface;
        private float builtWaterSurfaceY;
        private float builtWaterSurfaceMargin;

        private class DebrisSample
        {
            public Transform visualTransform;
            public Vector3 localPosition;
            public Vector3 baseLocalPosition;
            public Vector3 worldPosition;
            public Vector3 drift;
            public Vector3 spin;
            public float radius;
            public float strength;
            public float bobPhase;
        }

        private void OnEnable()
        {
            if (!ActiveFields.Contains(this))
            {
                ActiveFields.Add(this);
            }

            RebuildIfNeeded();
        }

        private void Update()
        {
            if (!debrisEnabled || debrisCount <= 0)
            {
                ClearSamples();
                return;
            }

            RebuildIfNeeded();
            RefreshSampleWorldPositions();

            if (Application.isPlaying && animateInPlayMode)
            {
                AnimateSamples();
            }
        }

        private void OnValidate()
        {
            fieldSize = new Vector3(
                Mathf.Max(0.1f, fieldSize.x),
                Mathf.Max(0.1f, fieldSize.y),
                Mathf.Max(0.1f, fieldSize.z));
            randomDriftSpeed.x = Mathf.Max(0f, randomDriftSpeed.x);
            randomDriftSpeed.y = Mathf.Max(randomDriftSpeed.x, randomDriftSpeed.y);
            hitRadiusMeters = Mathf.Max(0.02f, hitRadiusMeters);
            visualSizeRange.x = Mathf.Max(0.001f, visualSizeRange.x);
            visualSizeRange.y = Mathf.Max(visualSizeRange.x, visualSizeRange.y);
        }

        private void OnDisable()
        {
            ActiveFields.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveFields.Remove(this);
            ClearSamples();
            DestroyRuntimeAssets();
        }

        public static bool TryGetNearestHitForAll(
            Vector3 origin,
            Vector3 direction,
            float maxDistanceMeters,
            float yawDeg,
            float tiltDeg,
            out BeamHitResult nearestHit)
        {
            nearestHit = new BeamHitResult { hit = false, origin = origin, yawDeg = yawDeg, tiltDeg = tiltDeg };

            if (direction == Vector3.zero || maxDistanceMeters <= 0f)
            {
                return false;
            }

            bool found = false;
            float nearestDistance = maxDistanceMeters;
            Vector3 normalizedDirection = direction.normalized;

            for (int i = 0; i < ActiveFields.Count; i++)
            {
                WorksiteDebrisField field = ActiveFields[i];
                if (field == null || !field.debrisEnabled || !field.visibleToSonar)
                {
                    continue;
                }

                if (field.TryGetNearestHit(origin, normalizedDirection, nearestDistance, yawDeg, tiltDeg, out BeamHitResult hit))
                {
                    found = true;
                    nearestDistance = hit.geometricRange;
                    nearestHit = hit;
                }
            }

            return found;
        }

        private bool TryGetNearestHit(
            Vector3 origin,
            Vector3 direction,
            float maxDistanceMeters,
            float yawDeg,
            float tiltDeg,
            out BeamHitResult nearestHit)
        {
            nearestHit = new BeamHitResult { hit = false, origin = origin, yawDeg = yawDeg, tiltDeg = tiltDeg };
            RebuildIfNeeded();
            RefreshSampleWorldPositions();

            if (!RayMayTouchField(origin, direction, maxDistanceMeters))
            {
                return false;
            }

            bool found = false;
            float nearestDistance = maxDistanceMeters;
            int sampleCount = samples.Count;
            if (sampleCount <= 0)
            {
                return false;
            }

            int sampleLimit = Mathf.Min(sampleCount, Mathf.Max(1, maxSamplesCheckedPerRay));
            int stride = Mathf.Max(1, sampleCount / sampleLimit);
            int start = sampleCount == 0
                ? 0
                : Mathf.Abs(Mathf.RoundToInt(yawDeg * 17f + tiltDeg * 31f + seed)) % sampleCount;

            for (int checkedCount = 0; checkedCount < sampleLimit; checkedCount++)
            {
                int i = (start + checkedCount * stride) % sampleCount;
                DebrisSample sample = samples[i];
                Vector3 world = sample.worldPosition;

                if (hideAboveWaterSurface && world.y > waterSurfaceY - waterSurfaceMargin)
                {
                    continue;
                }

                float distance = Vector3.Dot(world - origin, direction);
                if (distance <= 0f || distance >= nearestDistance)
                {
                    continue;
                }

                Vector3 closestPointOnRay = origin + direction * distance;
                float lateralDistance = Vector3.Distance(world, closestPointOnRay);
                float hitRadius = Mathf.Max(hitRadiusMeters, sample.radius);
                if (lateralDistance > hitRadius)
                {
                    continue;
                }

                uint state = Hash((uint)(seed + i * 1009 + Mathf.RoundToInt(yawDeg * 100f) * 31 + Mathf.RoundToInt(tiltDeg * 100f) * 131));
                float intensity01 = ComputeIntensity(distance, lateralDistance, hitRadius, sample.strength, ref state);
                if (intensity01 < threshold)
                {
                    continue;
                }

                found = true;
                nearestDistance = distance;
                nearestHit = CreateHit(origin, direction, closestPointOnRay, distance, intensity01, yawDeg, tiltDeg);
            }

            return found;
        }

        private BeamHitResult CreateHit(Vector3 origin, Vector3 direction, Vector3 point, float distance, float intensity01, float yawDeg, float tiltDeg)
        {
            return new BeamHitResult
            {
                hit = true,
                origin = origin,
                point = point,
                normal = GetNormalForIntensity(direction, intensity01),
                initialDirection = direction,
                incomingDirection = direction,
                range = distance,
                geometricRange = distance,
                acousticRange = distance,
                oneWayTravelTimeSeconds = distance / 1480f,
                yawDeg = yawDeg,
                tiltDeg = tiltDeg,
                straightComparisonDistance = 0f
            };
        }

        private Vector3 GetNormalForIntensity(Vector3 direction, float intensity01)
        {
            Vector3 incoming = direction.normalized;
            Vector3 perpendicular = Vector3.Cross(incoming, Vector3.up);
            if (perpendicular.sqrMagnitude < 0.0001f)
            {
                perpendicular = Vector3.Cross(incoming, Vector3.right);
            }

            float dot = Mathf.Clamp01(intensity01);
            return (perpendicular.normalized * Mathf.Sqrt(Mathf.Max(0f, 1f - dot * dot)) - incoming * dot).normalized;
        }

        private float ComputeIntensity(float distance, float lateralDistance, float hitRadius, float sampleStrength, ref uint state)
        {
            float lateralWeight = Mathf.Clamp01(1f - lateralDistance / Mathf.Max(0.001f, hitRadius));
            lateralWeight *= lateralWeight;

            float attenuation = Mathf.Exp(-absorption * Mathf.Max(0f, distance));
            float randomSpeckle = Mathf.Lerp(1f - speckle, 1f + speckle, Next01(ref state));
            return Mathf.Clamp01(reflectivity * sampleStrength * lateralWeight * attenuation * randomSpeckle);
        }

        [ContextMenu("Rebuild Worksite Debris Samples")]
        public void ForceRebuild()
        {
            ClearSamples();
            RebuildIfNeeded(true);
        }

        [ContextMenu("Clear Visual Debris")]
        public void ClearVisualDebris()
        {
            showVisualDebris = false;
            ClearSamples();
            RebuildIfNeeded(true);
        }

        private void RebuildIfNeeded(bool force = false)
        {
            if (!debrisEnabled || debrisCount <= 0)
            {
                return;
            }

            if (!force &&
                samples.Count == debrisCount &&
                builtSeed == seed &&
                builtCount == debrisCount &&
                builtVisuals == showVisualDebris &&
                builtVisualLimit == maxVisualDebrisCount &&
                builtFieldShape == fieldShape &&
                builtFieldSize == fieldSize &&
                builtVisualSizeRange == visualSizeRange &&
                Mathf.Approximately(builtVisualSizeMultiplier, visualSizeMultiplier) &&
                Mathf.Approximately(builtHitRadiusMeters, hitRadiusMeters) &&
                Mathf.Approximately(builtVisualAlpha, visualAlpha) &&
                builtDarkColor == darkColor &&
                builtLightColor == lightColor &&
                builtHideAboveWaterSurface == hideAboveWaterSurface &&
                Mathf.Approximately(builtWaterSurfaceY, waterSurfaceY) &&
                Mathf.Approximately(builtWaterSurfaceMargin, waterSurfaceMargin))
            {
                return;
            }

            ClearSamples();
            ClearExistingVisualChildren();
            builtSeed = seed;
            builtCount = debrisCount;
            builtVisuals = showVisualDebris;
            builtVisualLimit = maxVisualDebrisCount;
            builtFieldShape = fieldShape;
            builtFieldSize = fieldSize;
            builtVisualSizeRange = visualSizeRange;
            builtVisualSizeMultiplier = visualSizeMultiplier;
            builtHitRadiusMeters = hitRadiusMeters;
            builtVisualAlpha = visualAlpha;
            builtDarkColor = darkColor;
            builtLightColor = lightColor;
            builtHideAboveWaterSurface = hideAboveWaterSurface;
            builtWaterSurfaceY = waterSurfaceY;
            builtWaterSurfaceMargin = waterSurfaceMargin;

            for (int i = 0; i < debrisCount; i++)
            {
                samples.Add(CreateSample(i));
            }
        }

        private DebrisSample CreateSample(int index)
        {
            uint state = Hash((uint)(seed + index * 1009));
            Vector3 position = RandomPointInField(ref state);
            if (hideAboveWaterSurface)
            {
                position = RandomUnderwaterPointInField(position, ref state);
            }

            DebrisSample sample = new DebrisSample
            {
                localPosition = position,
                baseLocalPosition = position,
                drift = currentVelocity + RandomUnitVector(ref state) * Mathf.Lerp(randomDriftSpeed.x, randomDriftSpeed.y, Next01(ref state)),
                spin = RandomUnitVector(ref state) * Mathf.Lerp(0.2f, 3f, Next01(ref state)),
                radius = hitRadiusMeters * Mathf.Lerp(0.6f, 1.8f, Next01(ref state)),
                strength = Mathf.Lerp(0.45f, 1.15f, Next01(ref state)),
                bobPhase = Next01(ref state) * Mathf.PI * 2f
            };

            if (showVisualDebris && index < maxVisualDebrisCount)
            {
                CreateVisualDebris(sample, ref state);
            }
            else
            {
                UpdateCachedWorldPosition(sample);
            }

            return sample;
        }

        private Vector3 RandomPointInField(ref uint state)
        {
            return fieldShape == FieldShape.Box
                ? RandomPointInBox(ref state)
                : RandomPointInEllipsoid(ref state);
        }

        private Vector3 RandomUnderwaterPointInField(Vector3 fallback, ref uint state)
        {
            if (IsUnderWaterSurface(fallback))
            {
                return fallback;
            }

            for (int attempt = 0; attempt < 64; attempt++)
            {
                Vector3 candidate = RandomPointInField(ref state);
                if (IsUnderWaterSurface(candidate))
                {
                    return candidate;
                }
            }

            return ProjectLocalPointBelowWater(fallback);
        }

        private bool IsUnderWaterSurface(Vector3 local)
        {
            return !hideAboveWaterSurface || transform.TransformPoint(local).y <= waterSurfaceY - waterSurfaceMargin;
        }

        private Vector3 ProjectLocalPointBelowWater(Vector3 local)
        {
            float worldY = transform.TransformPoint(local).y;
            float maxWorldY = waterSurfaceY - waterSurfaceMargin;
            if (worldY <= maxWorldY)
            {
                return local;
            }

            float deltaWorld = worldY - maxWorldY;
            local.y -= deltaWorld / Mathf.Max(0.0001f, transform.lossyScale.y);
            Vector3 half = HalfSize();
            local.y = Mathf.Clamp(local.y, -half.y, half.y);
            return local;
        }

        private void AnimateSamples()
        {
            Vector3 half = HalfSize();
            float time = Time.time;

            for (int i = 0; i < samples.Count; i++)
            {
                DebrisSample sample = samples[i];
                sample.localPosition += sample.drift * Time.deltaTime;
                sample.localPosition = WrapBox(sample.localPosition, half);
                sample.localPosition.y = sample.baseLocalPosition.y + Mathf.Sin(time * bobFrequency * Mathf.PI * 2f + sample.bobPhase) * bobAmplitude;
                sample.localPosition = GetWaterClampedLocalPosition(sample.localPosition);
                ApplyVisualTransform(sample);
                UpdateCachedWorldPosition(sample);

                if (sample.visualTransform != null)
                {
                    sample.visualTransform.Rotate(sample.spin * Time.deltaTime, Space.Self);
                }
            }
        }

        private bool RayMayTouchField(Vector3 origin, Vector3 direction, float maxDistanceMeters)
        {
            if (TryGetSampleBounds(out Bounds sampleBounds))
            {
                Ray ray = new Ray(origin, direction);
                if (sampleBounds.IntersectRay(ray, out float hitDistance))
                {
                    return hitDistance <= maxDistanceMeters;
                }

                Vector3 center = sampleBounds.center;
                float alongRay = Vector3.Dot(center - origin, direction);
                float closestDistance = Mathf.Clamp(alongRay, 0f, Mathf.Max(0f, maxDistanceMeters));
                Vector3 closestPoint = origin + direction * closestDistance;
                return sampleBounds.SqrDistance(closestPoint) <= 0.0001f;
            }

            Vector3 centerFallback = GetWaterClampedFieldCenter();
            Vector3 toCenter = centerFallback - origin;
            float alongRayFallback = Vector3.Dot(toCenter, direction);
            float closestDistanceFallback = Mathf.Clamp(alongRayFallback, 0f, Mathf.Max(0f, maxDistanceMeters));
            Vector3 closestPointFallback = origin + direction * closestDistanceFallback;

            Vector3 scale = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float radius = fieldSize.magnitude * 0.5f * Mathf.Max(0.0001f, maxScale) + hitRadiusMeters;
            return Vector3.SqrMagnitude(centerFallback - closestPointFallback) <= radius * radius;
        }

        private bool TryGetSampleBounds(out Bounds bounds)
        {
            RebuildIfNeeded();
            bounds = new Bounds();

            if (samples.Count <= 0)
            {
                return false;
            }

            bounds = new Bounds(samples[0].worldPosition, Vector3.zero);
            float maxRadius = hitRadiusMeters;
            for (int i = 0; i < samples.Count; i++)
            {
                DebrisSample sample = samples[i];
                UpdateCachedWorldPosition(sample);
                bounds.Encapsulate(sample.worldPosition);
                maxRadius = Mathf.Max(maxRadius, sample.radius);
            }

            bounds.Expand(maxRadius * 2f);
            return true;
        }

        private Vector3 GetWaterClampedFieldCenter()
        {
            return transform.TransformPoint(GetWaterClampedLocalPosition(Vector3.zero));
        }

        private void UpdateCachedWorldPosition(DebrisSample sample)
        {
            if (sample.visualTransform != null)
            {
                sample.worldPosition = sample.visualTransform.position;
                return;
            }

            sample.worldPosition = transform.TransformPoint(GetWaterClampedLocalPosition(sample.localPosition));
        }

        private void RefreshSampleWorldPositions()
        {
            for (int i = 0; i < samples.Count; i++)
            {
                UpdateCachedWorldPosition(samples[i]);
            }

            transform.hasChanged = false;
        }

        private void CreateVisualDebris(DebrisSample sample, ref uint state)
        {
            GameObject go = new GameObject("WorksiteDebris");
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            go.transform.SetParent(transform, false);

            bool plate = Next01(ref state) < plateRatio;
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            filter.sharedMesh = plate ? GetRoundMesh() : GetShardMesh();

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            renderer.sharedMaterial = CreateMaterial(ref state);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            float size = Mathf.Lerp(visualSizeRange.x, visualSizeRange.y, Next01(ref state));
            float aspect = plate ? Mathf.Lerp(0.25f, 1.6f, Next01(ref state)) : Mathf.Lerp(0.45f, 1.2f, Next01(ref state));
            float visualSize = size * Mathf.Clamp(visualSizeMultiplier, 0.1f, 1f);
            go.transform.localScale = new Vector3(visualSize * aspect, visualSize, visualSize * 0.08f);
            go.transform.localRotation = RandomRotation(ref state);

            sample.visualTransform = go.transform;
            ApplyVisualTransform(sample);
            UpdateCachedWorldPosition(sample);
        }

        private void ApplyVisualTransform(DebrisSample sample)
        {
            if (sample.visualTransform == null)
            {
                return;
            }

            sample.visualTransform.localPosition = GetWaterClampedLocalPosition(sample.localPosition);
        }

        private Material CreateMaterial(ref uint state)
        {
            Shader shader = Shader.Find("HDRP/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material material = new Material(shader)
            {
                name = "Runtime Worksite Debris Material",
                hideFlags = HideFlags.DontSave
            };

            Color color = Color.Lerp(darkColor, lightColor, Next01(ref state));
            color.a = visualAlpha * Mathf.Lerp(0.55f, 1f, Next01(ref state));

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_SurfaceType")) material.SetFloat("_SurfaceType", 1f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_AlphaCutoffEnable")) material.SetFloat("_AlphaCutoffEnable", 0f);

            materials.Add(material);
            return material;
        }

        private Mesh GetRoundMesh()
        {
            if (roundMesh != null)
            {
                return roundMesh;
            }

            const int segments = 18;
            Vector3[] vertices = new Vector3[segments + 1];
            Vector2[] uvs = new Vector2[segments + 1];
            int[] triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / segments;
                float radius = 0.5f * (0.86f + 0.10f * Mathf.Sin(i * 1.7f));
                float x = Mathf.Cos(angle) * radius;
                float y = Mathf.Sin(angle) * radius;
                vertices[i + 1] = new Vector3(x, y, 0f);
                uvs[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
            }

            for (int i = 0; i < segments; i++)
            {
                int tri = i * 3;
                triangles[tri] = 0;
                triangles[tri + 1] = i + 1;
                triangles[tri + 2] = i == segments - 1 ? 1 : i + 2;
            }

            roundMesh = new Mesh { name = "Runtime Worksite Debris Round", hideFlags = HideFlags.DontSave };
            roundMesh.vertices = vertices;
            roundMesh.uv = uvs;
            roundMesh.triangles = triangles;
            roundMesh.RecalculateNormals();
            roundMesh.RecalculateBounds();
            return roundMesh;
        }

        private Mesh GetShardMesh()
        {
            if (shardMesh != null)
            {
                return shardMesh;
            }

            shardMesh = new Mesh { name = "Runtime Worksite Debris Shard", hideFlags = HideFlags.DontSave };
            shardMesh.vertices = new[]
            {
                new Vector3(-0.46f, -0.22f, 0f),
                new Vector3(-0.18f, -0.42f, 0f),
                new Vector3(0.30f, -0.34f, 0f),
                new Vector3(0.50f, -0.04f, 0f),
                new Vector3(0.28f, 0.34f, 0f),
                new Vector3(-0.20f, 0.42f, 0f),
                new Vector3(-0.44f, 0.16f, 0f),
            };
            shardMesh.triangles = new[] { 0, 1, 2, 0, 2, 6, 6, 2, 5, 5, 2, 4, 4, 2, 3 };
            shardMesh.RecalculateNormals();
            shardMesh.RecalculateBounds();
            return shardMesh;
        }

        private void ClearSamples()
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].visualTransform == null)
                {
                    continue;
                }

                GameObject go = samples[i].visualTransform.gameObject;
                if (Application.isPlaying)
                {
                    Destroy(go);
                }
                else
                {
                    DestroyImmediate(go);
                }
            }

            samples.Clear();
        }

        private void ClearExistingVisualChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child == null || child.name != "WorksiteDebris")
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void DestroyRuntimeAssets()
        {
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(materials[i]);
                }
                else
                {
                    DestroyImmediate(materials[i]);
                }
            }

            materials.Clear();

            if (roundMesh != null)
            {
                Mesh mesh = roundMesh;
                roundMesh = null;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }

            if (shardMesh != null)
            {
                Mesh mesh = shardMesh;
                shardMesh = null;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }

        private Vector3 GetWaterClampedLocalPosition(Vector3 local)
        {
            if (!hideAboveWaterSurface)
            {
                return local;
            }

            float worldY = transform.TransformPoint(local).y;
            if (worldY <= waterSurfaceY - waterSurfaceMargin)
            {
                return local;
            }

            float deltaWorld = worldY - (waterSurfaceY - waterSurfaceMargin);
            local.y -= deltaWorld / Mathf.Max(0.0001f, transform.lossyScale.y);
            Vector3 half = HalfSize();
            local.y = Mathf.Clamp(local.y, -half.y, half.y);
            return local;
        }

        private Vector3 RandomPointInBox(ref uint state)
        {
            Vector3 half = HalfSize();
            return new Vector3(
                Mathf.Lerp(-half.x, half.x, Next01(ref state)),
                Mathf.Lerp(-half.y, half.y, Next01(ref state)),
                Mathf.Lerp(-half.z, half.z, Next01(ref state)));
        }

        private Vector3 RandomPointInEllipsoid(ref uint state)
        {
            Vector3 half = HalfSize();
            Vector3 p;
            int guard = 0;

            do
            {
                p = RandomPointInBox(ref state);
                guard++;
            }
            while (guard < 16 &&
                   (p.x * p.x) / (half.x * half.x) +
                   (p.y * p.y) / (half.y * half.y) +
                   (p.z * p.z) / (half.z * half.z) > 1f);

            return p;
        }

        private Vector3 HalfSize()
        {
            return new Vector3(
                Mathf.Max(0.1f, fieldSize.x) * 0.5f,
                Mathf.Max(0.1f, fieldSize.y) * 0.5f,
                Mathf.Max(0.1f, fieldSize.z) * 0.5f);
        }

        private static Vector3 WrapBox(Vector3 p, Vector3 half)
        {
            if (p.x < -half.x) p.x = half.x;
            if (p.x > half.x) p.x = -half.x;
            if (p.y < -half.y) p.y = half.y;
            if (p.y > half.y) p.y = -half.y;
            if (p.z < -half.z) p.z = half.z;
            if (p.z > half.z) p.z = -half.z;
            return p;
        }

        private static Vector3 RandomUnitVector(ref uint state)
        {
            Vector3 v = new Vector3(
                Next01(ref state) * 2f - 1f,
                Next01(ref state) * 2f - 1f,
                Next01(ref state) * 2f - 1f);
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }

        private static Quaternion RandomRotation(ref uint state)
        {
            return Quaternion.Euler(Next01(ref state) * 360f, Next01(ref state) * 360f, Next01(ref state) * 360f);
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.4f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (fieldShape == FieldShape.Box)
            {
                Gizmos.DrawWireCube(Vector3.zero, fieldSize);
            }
            else
            {
                Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.Scale(fieldSize);
                Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
            }

            Gizmos.matrix = previous;

            DrawSampleGizmos();
        }

        private void OnDrawGizmos()
        {
            DrawSampleGizmos();
        }

        private void DrawSampleGizmos()
        {
            if (!drawDebrisGizmos || !debrisEnabled)
            {
                return;
            }

            RebuildIfNeeded();
            RefreshSampleWorldPositions();
            Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, Mathf.Clamp01(visualAlpha));
            int count = Mathf.Min(samples.Count, maxVisualDebrisCount);
            for (int i = 0; i < count; i++)
            {
                float radius = Mathf.Max(0.01f, samples[i].radius * 0.25f);
                Gizmos.DrawSphere(samples[i].worldPosition, radius);
            }
        }
    }
}
