using UnityEngine;

namespace BV5000.Sonar
{
    public class SoundSpeedModel
    {
        private const float FallbackSoundSpeed = 1480f;
        private const float StandardSalinityPsu = 35f;

        private readonly SonarConfig config;

        public SoundSpeedModel(SonarConfig config)
        {
            this.config = config;
        }

        public float GetSoundSpeed(float temperatureCelsius)
        {
            return GetSoundSpeed(temperatureCelsius, 0f);
        }

        public float GetSoundSpeed(float temperatureCelsius, float depthMeters)
        {
            if (config == null)
            {
                return FallbackSoundSpeed;
            }

            float positiveDepth = Mathf.Max(0f, depthMeters);

            if (config.soundSpeedFormula == SoundSpeedFormula.Mackenzie1981)
            {
                return GetMackenzie1981SoundSpeed(temperatureCelsius, config.salinityPsu, positiveDepth);
            }

            return GetFreshWaterSoundSpeed(temperatureCelsius, positiveDepth);
        }

        public float GetSoundSpeedAtPosition(Vector3 worldPosition, TemperatureProfile temperatureProfile)
        {
            if (temperatureProfile == null || !temperatureProfile.HasUsableProfile)
            {
                return GetReferenceSoundSpeed();
            }

            float temperature = temperatureProfile.GetTemperatureAtWorldPosition(worldPosition);
            float depth = Mathf.Max(0f, temperatureProfile.GetDepthMeters(worldPosition));
            return GetSoundSpeed(temperature, depth);
        }

        public float GetReferenceSoundSpeed()
        {
            if (config == null)
            {
                return FallbackSoundSpeed;
            }

            if (config.soundSpeedFormula == SoundSpeedFormula.Mackenzie1981)
            {
                return GetMackenzie1981SoundSpeed(
                    config.referenceWaterTemperatureC,
                    config.salinityPsu,
                    0f);
            }

            return GetFreshWaterSoundSpeed(config.referenceWaterTemperatureC, 0f);
        }

        private static float GetFreshWaterSoundSpeed(float temperatureCelsius, float depthMeters)
        {
            // Pure/fresh water polynomial near atmospheric pressure, plus a small depth-pressure term.
            // T: degrees Celsius, D: depth in meters. Best used for tanks, reservoirs, lakes, and trend studies.
            float t = temperatureCelsius;
            float t2 = t * t;
            float t3 = t2 * t;
            float t4 = t3 * t;
            float t5 = t4 * t;
            float surfaceSpeed = 1402.388f
                + 5.03830f * t
                - 5.81090e-2f * t2
                + 3.3432e-4f * t3
                - 1.47797e-6f * t4
                + 3.1419e-9f * t5;

            return surfaceSpeed + 1.63e-2f * Mathf.Max(0f, depthMeters);
        }

        private static float GetMackenzie1981SoundSpeed(float temperatureCelsius, float salinityPsu, float depthMeters)
        {
            // Mackenzie-style practical approximation for seawater sound speed.
            // T: degrees Celsius, S: PSU/ppt, D: depth in meters.
            // This is appropriate for an emulator/trend study, not a metrology-grade ocean model.
            float t = temperatureCelsius;
            float s = salinityPsu;
            float d = depthMeters;
            float salinityOffset = s - StandardSalinityPsu;

            return 1448.96f
                + 4.591f * t
                - 5.304e-2f * t * t
                + 2.374e-4f * t * t * t
                + 1.340f * salinityOffset
                + 1.630e-2f * d
                + 1.675e-7f * d * d
                - 1.025e-2f * t * salinityOffset
                - 7.139e-13f * t * d * d * d;
        }
    }
}
