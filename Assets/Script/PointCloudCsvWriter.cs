using System;
using System.Globalization;
using System.IO;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace BV5000.Sonar
{
    public class PointCloudCsvWriter
    {
        private readonly SonarConfig config;

        public PointCloudCsvWriter(SonarConfig config)
        {
            this.config = config;
        }

        public string Save(PointCloudBuffer pointCloud, string fileName)
        {
            if (config == null)
            {
                throw new InvalidOperationException("SonarConfig is required for CSV output.");
            }

            if (pointCloud == null)
            {
                throw new ArgumentNullException(nameof(pointCloud));
            }

            string folderName = string.IsNullOrWhiteSpace(config.outputFolder)
                ? "SonarOutput"
                : config.outputFolder.Trim();
            string outputDirectory = Path.Combine(Application.dataPath, folderName);
            Directory.CreateDirectory(outputDirectory);

            string safeFileName = string.IsNullOrWhiteSpace(fileName) ? "sonar_scan.csv" : fileName;
            string path = Path.Combine(outputDirectory, safeFileName);

            File.WriteAllText(path, BuildCsv(pointCloud), Encoding.UTF8);
#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
            return path;
        }

        private string BuildCsv(PointCloudBuffer pointCloud)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(config.saveExtraColumns
                ? "x,y,z,i,yaw_deg,tilt_deg,range_m,geometric_range_m,acoustic_range_m,travel_time_s,sample_time_s"
                : "x,y,z,i");

            CultureInfo culture = CultureInfo.InvariantCulture;
            foreach (SonarPoint point in pointCloud.Points)
            {
                Vector3 outputPosition = ConvertOutputPosition(point.position);

                if (config.saveExtraColumns)
                {
                    builder.AppendFormat(
                        culture,
                        "{0:F6},{1:F6},{2:F6},{3},{4:F3},{5:F3},{6:F6},{7:F6},{8:F6},{9:F9},{10:F6}\n",
                        outputPosition.x,
                        outputPosition.y,
                        outputPosition.z,
                        point.intensity,
                        point.yawDeg,
                        point.tiltDeg,
                        point.rangeMeters,
                        point.geometricRangeMeters,
                        point.acousticRangeMeters,
                        point.travelTimeSeconds,
                        point.sampleTimeSeconds);
                }
                else
                {
                    builder.AppendFormat(
                        culture,
                        "{0:F6},{1:F6},{2:F6},{3}\n",
                        outputPosition.x,
                        outputPosition.y,
                        outputPosition.z,
                        point.intensity);
                }
            }

            return builder.ToString();
        }

        private Vector3 ConvertOutputPosition(Vector3 unityPosition)
        {
            if (config.exportCloudCompareZUp)
            {
                // Unity is Y-up. CloudCompare commonly uses Z-up point clouds.
                // Preserve Unity's horizontal X-Z plane as output X-Y, and move Unity Y to output Z.
                return new Vector3(unityPosition.x, unityPosition.z, unityPosition.y);
            }

            return unityPosition;
        }
    }
}
