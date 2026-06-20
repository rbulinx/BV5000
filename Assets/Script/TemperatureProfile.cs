using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BV5000.Sonar
{
    public class TemperatureProfile
    {
        private struct TemperatureSample
        {
            public float depthMeters;
            public float temperatureCelsius;
        }

        private readonly SonarConfig config;
        private readonly List<TemperatureSample> samples = new List<TemperatureSample>();

        public bool HasUsableProfile => config != null && config.temperatureInfluenceMode != TemperatureInfluenceMode.None && samples.Count > 0;
        public int SampleCount => samples.Count;

        public TemperatureProfile(SonarConfig config)
        {
            this.config = config;
            RefreshTable();
        }

        public void RefreshTable()
        {
            samples.Clear();

            if (config == null || config.temperatureInfluenceMode == TemperatureInfluenceMode.None)
            {
                return;
            }

            LoadCsvSamples();
            samples.Sort((a, b) => a.depthMeters.CompareTo(b.depthMeters));
        }

        public float GetDepthMeters(Vector3 worldPosition)
        {
            // Unity world Y axis is upward. Water depth is positive downward.
            return -worldPosition.y;
        }

        public float GetTemperatureAtWorldPosition(Vector3 worldPosition)
        {
            return GetTemperatureAtDepth(GetDepthMeters(worldPosition));
        }

        public float GetTemperatureAtDepth(float depthMeters)
        {
            if (!HasUsableProfile)
            {
                return config != null ? config.referenceWaterTemperatureC : 20f;
            }

            if (samples.Count == 1)
            {
                return samples[0].temperatureCelsius;
            }

            if (depthMeters <= samples[0].depthMeters)
            {
                return samples[0].temperatureCelsius;
            }

            int lastIndex = samples.Count - 1;
            if (depthMeters >= samples[lastIndex].depthMeters)
            {
                return samples[lastIndex].temperatureCelsius;
            }

            for (int i = 0; i < lastIndex; i++)
            {
                TemperatureSample lower = samples[i];
                TemperatureSample upper = samples[i + 1];

                if (depthMeters >= lower.depthMeters && depthMeters <= upper.depthMeters)
                {
                    float t = Mathf.InverseLerp(lower.depthMeters, upper.depthMeters, depthMeters);
                    return Mathf.Lerp(lower.temperatureCelsius, upper.temperatureCelsius, t);
                }
            }

            return samples[lastIndex].temperatureCelsius;
        }

        private void LoadCsvSamples()
        {
            string path = GetCsvPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"Temperature profile CSV was not found: {path}. Temperature influence will be disabled for this scan.");
                return;
            }

            CultureInfo culture = CultureInfo.InvariantCulture;
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                {
                    continue;
                }

                string[] columns = line.Split(',');
                if (columns.Length < 2)
                {
                    continue;
                }

                if (!float.TryParse(columns[0].Trim(), NumberStyles.Float, culture, out float depthMeters) ||
                    !float.TryParse(columns[1].Trim(), NumberStyles.Float, culture, out float temperatureCelsius))
                {
                    continue;
                }

                samples.Add(new TemperatureSample
                {
                    depthMeters = depthMeters,
                    temperatureCelsius = temperatureCelsius
                });
            }

            if (samples.Count == 0)
            {
                Debug.LogWarning($"Temperature profile CSV has no usable depth,temperature rows: {path}. Temperature influence will be disabled for this scan.");
            }
        }

        private string GetCsvPath()
        {
            if (config == null || string.IsNullOrWhiteSpace(config.temperatureProfileCsvPath))
            {
                return string.Empty;
            }

            string configuredPath = config.temperatureProfileCsvPath.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            if (configuredPath.StartsWith("Assets/"))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                return Path.Combine(projectRoot, configuredPath);
            }

            return Path.Combine(Application.dataPath, configuredPath);
        }
    }
}
