using System.Collections.Generic;
using UnityEngine;

namespace BV5000.Sonar
{
    public struct BeamHitResult
    {
        public bool hit;
        public Vector3 origin;
        public Vector3 point;
        public Vector3 normal;
        public Vector3 initialDirection;
        public Vector3 incomingDirection;
        public float range;
        public float geometricRange;
        public float acousticRange;
        public float oneWayTravelTimeSeconds;
        public float yawDeg;
        public float tiltDeg;
        public Collider collider;
        public int refractionSteps;
        public float refractionBendDeg;
        public float straightComparisonDistance;
    }

    public class SonarBeamTracer
    {
        private const float MinimumStepLength = 0.01f;
        private const float MinimumSoundSpeed = 1f;

        private readonly SonarConfig config;
        private readonly TemperatureProfile temperatureProfile;
        private readonly SoundSpeedModel soundSpeedModel;

        public SonarBeamTracer(SonarConfig config, TemperatureProfile temperatureProfile, SoundSpeedModel soundSpeedModel)
        {
            this.config = config;
            this.temperatureProfile = temperatureProfile;
            this.soundSpeedModel = soundSpeedModel;
        }

        public List<BeamHitResult> TraceFanBeam(Vector3 origin, Vector3 centerDirection, Vector3 verticalFanAxis, float yawDeg, float tiltDeg)
        {
            List<BeamHitResult> hits = new List<BeamHitResult>();

            if (config == null || centerDirection == Vector3.zero)
            {
                return hits;
            }

            float halfWidth = Mathf.Max(0f, config.beamWidthDeg) * 0.5f;
            float resolution = Mathf.Max(0.001f, config.beamResolutionDeg);
            int beamSteps = Mathf.Max(1, Mathf.CeilToInt(config.beamWidthDeg / resolution));

            if (config.beamWidthDeg <= 0f)
            {
                AddHitIfAny(hits, origin, centerDirection.normalized, yawDeg, tiltDeg);
                return hits;
            }

            for (int i = 0; i <= beamSteps; i++)
            {
                float t = beamSteps == 0 ? 0f : i / (float)beamSteps;
                float beamAngle = Mathf.Lerp(-halfWidth, halfWidth, t);
                Vector3 beamDirection = Quaternion.AngleAxis(beamAngle, verticalFanAxis.normalized) * centerDirection.normalized;
                AddHitIfAny(hits, origin, beamDirection, yawDeg, tiltDeg);
            }

            return hits;
        }

        private void AddHitIfAny(List<BeamHitResult> hits, Vector3 origin, Vector3 direction, float yawDeg, float tiltDeg)
        {
            BeamHitResult hit = IsRefractionEnabled()
                ? TraceStepwiseBeam(origin, direction, yawDeg, tiltDeg)
                : TraceStraightBeam(origin, direction, yawDeg, tiltDeg);

            float debrisMaxDistance = hit.hit ? hit.geometricRange : Mathf.Max(0.01f, config.maxRange);
            if (WorksiteDebrisField.TryGetNearestHitForAll(origin, direction, debrisMaxDistance, yawDeg, tiltDeg, out BeamHitResult debrisHit))
            {
                hits.Add(debrisHit);
                return;
            }

            if (hit.hit)
            {
                hits.Add(hit);
            }
        }

        private BeamHitResult TraceStraightBeam(Vector3 origin, Vector3 direction, float yawDeg, float tiltDeg)
        {
            float maxRange = Mathf.Max(0.01f, config.maxRange);
            Vector3 rayDirection = direction.normalized;

            if (Physics.Raycast(origin, rayDirection, out RaycastHit hit, maxRange, config.layerMask, QueryTriggerInteraction.Ignore))
            {
                float oneWayTravelTime = EstimateStraightPathTravelTime(origin, rayDirection, hit.distance);
                return CreateHitResult(origin, hit, rayDirection, rayDirection, hit.distance, oneWayTravelTime, yawDeg, tiltDeg);
            }

            return new BeamHitResult { hit = false, origin = origin, yawDeg = yawDeg, tiltDeg = tiltDeg };
        }

        private BeamHitResult TraceStepwiseBeam(Vector3 origin, Vector3 direction, float yawDeg, float tiltDeg)
        {
            float maxRange = Mathf.Max(0.01f, config.maxRange);
            float stepLength = Mathf.Max(MinimumStepLength, config.stepLength);
            int maxSteps = Mathf.CeilToInt(maxRange / stepLength);

            Vector3 currentOrigin = origin;
            Vector3 initialDirection = direction.normalized;
            Vector3 currentDirection = initialDirection;
            float traveledDistance = 0f;
            float oneWayTravelTime = 0f;
            int refractionSteps = 0;
            float totalBendDeg = 0f;

            for (int step = 0; step < maxSteps && traveledDistance < maxRange; step++)
            {
                float segmentDistance = Mathf.Min(stepLength, maxRange - traveledDistance);

                if (Physics.Raycast(currentOrigin, currentDirection, out RaycastHit hit, segmentDistance, config.layerMask, QueryTriggerInteraction.Ignore))
                {
                    oneWayTravelTime += GetSegmentTravelTime(currentOrigin, currentDirection, hit.distance);
                    BeamHitResult result = CreateHitResult(
                        origin,
                        hit,
                        initialDirection,
                        currentDirection,
                        traveledDistance + hit.distance,
                        oneWayTravelTime,
                        yawDeg,
                        tiltDeg);
                    result.refractionSteps = refractionSteps;
                    result.refractionBendDeg = totalBendDeg;
                    result.straightComparisonDistance = GetStraightComparisonDistance(origin, direction, result.point);
                    return result;
                }

                oneWayTravelTime += GetSegmentTravelTime(currentOrigin, currentDirection, segmentDistance);
                currentOrigin += currentDirection * segmentDistance;
                traveledDistance += segmentDistance;
                Vector3 previousDirection = currentDirection;
                currentDirection = ApplyRefraction(currentOrigin, currentDirection, segmentDistance);
                totalBendDeg += Vector3.Angle(previousDirection, currentDirection);
                refractionSteps++;
            }

            BeamHitResult fallbackHit = TraceStraightBeam(origin, direction, yawDeg, tiltDeg);
            if (fallbackHit.hit)
            {
                fallbackHit.refractionSteps = refractionSteps;
                fallbackHit.refractionBendDeg = totalBendDeg;
                fallbackHit.straightComparisonDistance = 0f;
                return fallbackHit;
            }

            return new BeamHitResult { hit = false, origin = origin, yawDeg = yawDeg, tiltDeg = tiltDeg };
        }

        private BeamHitResult CreateHitResult(
            Vector3 origin,
            RaycastHit hit,
            Vector3 initialDirection,
            Vector3 incomingDirection,
            float geometricRange,
            float oneWayTravelTimeSeconds,
            float yawDeg,
            float tiltDeg)
        {
            float acousticRange = GetAcousticRange(oneWayTravelTimeSeconds, geometricRange);
            float reportedRange = IsAcousticRangeEnabled() ? acousticRange : geometricRange;

            return new BeamHitResult
            {
                hit = true,
                origin = origin,
                point = hit.point,
                normal = hit.normal,
                initialDirection = initialDirection.normalized,
                incomingDirection = incomingDirection.normalized,
                range = reportedRange,
                geometricRange = geometricRange,
                acousticRange = acousticRange,
                oneWayTravelTimeSeconds = oneWayTravelTimeSeconds,
                yawDeg = yawDeg,
                tiltDeg = tiltDeg,
                collider = hit.collider
            };
        }

        private float EstimateStraightPathTravelTime(Vector3 origin, Vector3 direction, float distance)
        {
            if (!IsAcousticRangeEnabled() || distance <= 0f)
            {
                return GetFallbackTravelTime(distance);
            }

            float stepLength = Mathf.Max(MinimumStepLength, config.stepLength);
            float traveledDistance = 0f;
            float travelTime = 0f;
            Vector3 normalizedDirection = direction.normalized;

            while (traveledDistance < distance)
            {
                float segmentDistance = Mathf.Min(stepLength, distance - traveledDistance);
                Vector3 segmentOrigin = origin + normalizedDirection * traveledDistance;
                travelTime += GetSegmentTravelTime(segmentOrigin, normalizedDirection, segmentDistance);
                traveledDistance += segmentDistance;
            }

            return travelTime;
        }

        private float GetSegmentTravelTime(Vector3 segmentOrigin, Vector3 direction, float segmentDistance)
        {
            if (segmentDistance <= 0f)
            {
                return 0f;
            }

            Vector3 samplePosition = segmentOrigin + direction.normalized * (segmentDistance * 0.5f);
            float soundSpeed = GetSoundSpeedAt(samplePosition);
            return segmentDistance / soundSpeed;
        }

        private float GetAcousticRange(float oneWayTravelTimeSeconds, float fallbackGeometricRange)
        {
            if (!IsAcousticRangeEnabled() || oneWayTravelTimeSeconds <= 0f)
            {
                return fallbackGeometricRange;
            }

            return GetRangeReferenceSoundSpeed() * oneWayTravelTimeSeconds;
        }

        private float GetFallbackTravelTime(float distance)
        {
            return distance / GetRangeReferenceSoundSpeed();
        }

        private float GetRangeReferenceSoundSpeed()
        {
            float referenceSpeed = soundSpeedModel != null
                ? soundSpeedModel.GetReferenceSoundSpeed()
                : 1480f;
            return Mathf.Max(MinimumSoundSpeed, referenceSpeed);
        }

        private float GetSoundSpeedAt(Vector3 worldPosition)
        {
            if (!IsTemperatureEffectEnabled() || temperatureProfile == null || soundSpeedModel == null)
            {
                return GetRangeReferenceSoundSpeed();
            }

            return Mathf.Max(MinimumSoundSpeed, soundSpeedModel.GetSoundSpeedAtPosition(worldPosition, temperatureProfile));
        }

        private Vector3 ApplyRefraction(Vector3 worldPosition, Vector3 direction, float segmentDistance)
        {
            if (!IsTemperatureEffectEnabled() || temperatureProfile == null || soundSpeedModel == null)
            {
                return direction.normalized;
            }

            return ApplyLayeredSnellRefraction(worldPosition, direction, segmentDistance);
        }

        private Vector3 ApplyLayeredSnellRefraction(Vector3 worldPosition, Vector3 direction, float segmentDistance)
        {
            Vector3 normalizedDirection = direction.normalized;
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(normalizedDirection, Vector3.up);
            float horizontalMagnitude = horizontalDirection.magnitude;

            if (horizontalMagnitude <= 0.0001f)
            {
                return normalizedDirection;
            }

            Vector3 horizontalUnit = horizontalDirection / horizontalMagnitude;
            float verticalSign = Mathf.Sign(Vector3.Dot(normalizedDirection, Vector3.up));
            float currentSpeed = GetSoundSpeedAt(worldPosition);
            Vector3 previousPosition = worldPosition - normalizedDirection * Mathf.Max(MinimumStepLength, config.stepLength);
            float previousSpeed = GetSoundSpeedAt(previousPosition);

            if (Mathf.Approximately(verticalSign, 0f))
            {
                float sampleDistance = Mathf.Max(MinimumStepLength, segmentDistance);
                float speedUp = GetSoundSpeedAt(worldPosition + Vector3.up * sampleDistance);
                float speedDown = GetSoundSpeedAt(worldPosition - Vector3.up * sampleDistance);
                if (Mathf.Approximately(speedUp, speedDown))
                {
                    return normalizedDirection;
                }

                verticalSign = speedUp < speedDown ? 1f : -1f;
                previousSpeed = currentSpeed;
                currentSpeed = GetSoundSpeedAt(worldPosition + Vector3.up * verticalSign * Mathf.Max(MinimumStepLength, config.stepLength));
            }

            // In a horizontally layered medium c(z), Snell's invariant is cos(elevation) / c.
            float snellInvariant = horizontalMagnitude / Mathf.Max(MinimumSoundSpeed, previousSpeed);
            float newHorizontalMagnitude = snellInvariant * Mathf.Max(MinimumSoundSpeed, currentSpeed);

            if (newHorizontalMagnitude >= 1f)
            {
                // Turning point: the ray approaches horizontal and bends back toward the previous layer.
                newHorizontalMagnitude = 0.9999f;
                verticalSign = -verticalSign;
            }

            float newVerticalMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - newHorizontalMagnitude * newHorizontalMagnitude));
            return (horizontalUnit * newHorizontalMagnitude + Vector3.up * (verticalSign * newVerticalMagnitude)).normalized;
        }

        private float GetStraightComparisonDistance(Vector3 origin, Vector3 initialDirection, Vector3 refractedHitPoint)
        {
            if (!config.compareStraightAndRefractedHits)
            {
                return 0f;
            }

            if (Physics.Raycast(origin, initialDirection.normalized, out RaycastHit straightHit, Mathf.Max(0.01f, config.maxRange), config.layerMask, QueryTriggerInteraction.Ignore))
            {
                return Vector3.Distance(straightHit.point, refractedHitPoint);
            }

            return -1f;
        }

        private bool IsTemperatureEffectEnabled()
        {
            return config != null && config.temperatureInfluenceMode != TemperatureInfluenceMode.None && temperatureProfile != null && temperatureProfile.HasUsableProfile;
        }

        private bool IsRefractionEnabled()
        {
            return IsTemperatureEffectEnabled() && config.temperatureInfluenceMode == TemperatureInfluenceMode.RefractionAndRange;
        }

        private bool IsAcousticRangeEnabled()
        {
            return IsTemperatureEffectEnabled()
                && (config.temperatureInfluenceMode == TemperatureInfluenceMode.RangeOnly
                    || config.temperatureInfluenceMode == TemperatureInfluenceMode.RefractionAndRange);
        }

    }
}
