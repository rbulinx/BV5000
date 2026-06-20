using System.Collections.Generic;
using UnityEngine;

namespace BV5000.Sonar
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SonarDensityRegionVolume : MonoBehaviour
    {
        private static readonly List<SonarDensityRegionVolume> ActiveVolumes = new List<SonarDensityRegionVolume>();

        public bool regionEnabled = true;
        public bool terrainHitsOnly = true;
        public SonarDensityRegionShape shape = SonarDensityRegionShape.Box;
        public Vector3 localCenter = Vector3.zero;
        public Vector3 localEulerAngles = Vector3.zero;
        public Vector3 localSize = new Vector3(5f, 2f, 5f);

        [Range(0f, 1f)]
        public float keepProbability = 1f;

        [Range(0, 24)]
        public int extraPointsPerHit = 3;

        [Min(0f)]
        public float blendDistanceMeters = 1f;

        [Min(0f)]
        public float lateralJitterMeters = 0.04f;

        [Min(0f)]
        public float normalThicknessMeters = 0.12f;

        [Range(0f, 1f)]
        public float thicknessVariation = 0.65f;

        [Min(0.01f)]
        public float thicknessPatchScaleMeters = 4f;

        [Range(0f, 1f)]
        public float thicknessPatchVariation = 0.6f;

        public bool drawRegionGizmo = true;
        public Color gizmoColor = new Color(1f, 0.8f, 0.15f, 0.65f);

        public static bool TryGetRegion(Vector3 worldPosition, out SonarDensityRegionVolume region, out float blendWeight)
        {
            region = null;
            blendWeight = 0f;

            for (int i = 0; i < ActiveVolumes.Count; i++)
            {
                SonarDensityRegionVolume candidate = ActiveVolumes[i];
                if (candidate == null || !candidate.regionEnabled)
                {
                    continue;
                }

                float candidateWeight = candidate.GetBlendWeight(worldPosition);
                if (candidateWeight > blendWeight)
                {
                    region = candidate;
                    blendWeight = candidateWeight;
                }
            }

            return region != null;
        }

        private void Reset()
        {
            Terrain terrain = GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                return;
            }

            Vector3 terrainSize = terrain.terrainData.size;
            localCenter = new Vector3(terrainSize.x * 0.5f, terrainSize.y * 0.5f, terrainSize.z * 0.5f);
            localSize = new Vector3(
                Mathf.Max(1f, terrainSize.x * 0.25f),
                Mathf.Max(1f, terrainSize.y),
                Mathf.Max(1f, terrainSize.z * 0.25f));
        }

        private void OnEnable()
        {
            if (!ActiveVolumes.Contains(this))
            {
                ActiveVolumes.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveVolumes.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveVolumes.Remove(this);
        }

        private void OnValidate()
        {
            localSize = new Vector3(
                Mathf.Max(0.001f, localSize.x),
                Mathf.Max(0.001f, localSize.y),
                Mathf.Max(0.001f, localSize.z));
            blendDistanceMeters = Mathf.Max(0f, blendDistanceMeters);
            thicknessPatchScaleMeters = Mathf.Max(0.01f, thicknessPatchScaleMeters);
        }

        public float GetBlendWeight(Vector3 worldPosition)
        {
            Vector3 local = Quaternion.Inverse(GetLocalRotation()) * (transform.InverseTransformPoint(worldPosition) - localCenter);
            Vector3 halfSize = localSize * 0.5f;
            float signedDistance = shape == SonarDensityRegionShape.Ellipsoid
                ? GetSphereSignedDistance(local, halfSize)
                : GetBoxSignedDistance(local, halfSize);

            if (blendDistanceMeters <= 0f)
            {
                return signedDistance >= 0f ? 1f : 0f;
            }

            float t = Mathf.InverseLerp(-blendDistanceMeters, blendDistanceMeters, signedDistance);
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

        private void OnDrawGizmos()
        {
            DrawGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmo(true);
        }

        private void DrawGizmo(bool selected)
        {
            if (!drawRegionGizmo || !regionEnabled)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color color = gizmoColor;
            color.a = selected ? gizmoColor.a : gizmoColor.a * 0.35f;
            Gizmos.color = color;
            Quaternion localRotation = GetLocalRotation();
            Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(localCenter, localRotation, localSize);
            DrawShapeGizmo();

            if (blendDistanceMeters > 0f)
            {
                Vector3 blendSize = localSize + Vector3.one * (blendDistanceMeters * 2f);
                color.a *= 0.45f;
                Gizmos.color = color;
                Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(localCenter, localRotation, blendSize);
                DrawShapeGizmo();
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private void DrawShapeGizmo()
        {
            if (shape == SonarDensityRegionShape.Ellipsoid)
            {
                Gizmos.DrawWireSphere(Vector3.zero, 0.5f);
            }
            else
            {
                Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            }
        }

        private Quaternion GetLocalRotation()
        {
            return Quaternion.Euler(localEulerAngles);
        }
    }
}
