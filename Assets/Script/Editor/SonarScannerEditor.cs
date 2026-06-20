using UnityEditor;
using UnityEngine;

namespace BV5000.Sonar.EditorTools
{
    [CustomEditor(typeof(SonarScanner))]
    public class SonarScannerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Sonar Controls", EditorStyles.boldLabel);

            SonarScanner scanner = (SonarScanner)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run Scan"))
                {
                    scanner.RunScan();
                }

                if (GUILayout.Button("Run Animated Scan"))
                {
                    scanner.RunScanAnimated();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Test Fan Once"))
                {
                    scanner.TestCurrentFanBeamOnce();
                }

                if (GUILayout.Button("Log Rig Setup"))
                {
                    scanner.LogRigSetup();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Animated scan needs Play mode. Run Scan can be used outside Play mode, but movement will not animate.", MessageType.Info);
            }
        }
    }
}
