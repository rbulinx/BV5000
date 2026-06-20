using UnityEngine;

namespace BV5000.Sonar
{
    public class IntensityModel
    {
        private readonly SonarConfig config;

        public IntensityModel(SonarConfig config)
        {
            this.config = config;
        }

        public byte GetIntensity(Vector3 incomingDirection, Vector3 surfaceNormal, float rangeMeters)
        {
            if (config == null || incomingDirection == Vector3.zero || surfaceNormal == Vector3.zero)
            {
                return 0;
            }

            Vector3 d = incomingDirection.normalized;
            Vector3 n = surfaceNormal.normalized;
            float angleTerm = Mathf.Clamp01(Vector3.Dot(-d, n));
            float value = 255f * Mathf.Pow(angleTerm, Mathf.Max(0.01f, config.intensityGamma));

            if (config.useRangeAttenuation && rangeMeters > 0f)
            {
                value /= 1f + config.attenuationCoeff * rangeMeters;
            }

            return (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
        }
    }
}
