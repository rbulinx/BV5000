using System.Collections.Generic;
using UnityEngine;

namespace BV5000.Sonar
{
    public readonly struct SonarPoint
    {
        public readonly Vector3 position;
        public readonly byte intensity;
        public readonly float yawDeg;
        public readonly float tiltDeg;
        public readonly float rangeMeters;
        public readonly float geometricRangeMeters;
        public readonly float acousticRangeMeters;
        public readonly float travelTimeSeconds;
        public readonly float sampleTimeSeconds;

        public SonarPoint(
            Vector3 position,
            byte intensity,
            float yawDeg,
            float tiltDeg,
            float rangeMeters,
            float geometricRangeMeters,
            float acousticRangeMeters,
            float travelTimeSeconds,
            float sampleTimeSeconds)
        {
            this.position = position;
            this.intensity = intensity;
            this.yawDeg = yawDeg;
            this.tiltDeg = tiltDeg;
            this.rangeMeters = rangeMeters;
            this.geometricRangeMeters = geometricRangeMeters;
            this.acousticRangeMeters = acousticRangeMeters;
            this.travelTimeSeconds = travelTimeSeconds;
            this.sampleTimeSeconds = sampleTimeSeconds;
        }
    }

    public class PointCloudBuffer
    {
        private readonly List<SonarPoint> points = new List<SonarPoint>();

        public int Count => points.Count;
        public IReadOnlyList<SonarPoint> Points => points;

        public void AddPoint(SonarPoint point)
        {
            points.Add(point);
        }

        public void Clear()
        {
            points.Clear();
        }
    }
}
