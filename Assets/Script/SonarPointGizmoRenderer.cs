using UnityEngine;

namespace BV5000.Sonar
{
    public class SonarPointGizmoRenderer : MonoBehaviour
    {
        public SonarScanner scanner;

        [Min(0.001f)]
        public float pointRadius = 0.04f;

        [Min(1)]
        public int stride = 5;

        public Color color = Color.green;

        private void OnDrawGizmos()
        {
            if (scanner == null || scanner.RecentPoints == null)
            {
                return;
            }

            Gizmos.color = color;
            int drawStride = Mathf.Max(1, stride);

            for (int i = 0; i < scanner.RecentPoints.Count; i += drawStride)
            {
                Gizmos.DrawSphere(scanner.RecentPoints[i].position, pointRadius);
            }
        }
    }
}
