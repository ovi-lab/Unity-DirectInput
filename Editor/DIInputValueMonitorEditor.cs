using System.Linq;
using UnityEditor;


namespace DirectInputManager.Editor
{

[CustomEditor(typeof(DIInputValueMonitor))]
public class DIInputValueMonitorEditor : UnityEditor.Editor
{
    private bool showCurrentValue = true;

    public override void OnInspectorGUI()
    {
        DIInputValueMonitor script = (DIInputValueMonitor)target;
        if (script == null)
        {
            script = FindFirstObjectByType<DIInputValueMonitor>();
            return;
        }
        script.Update();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("DI Input Monitor Settings", EditorStyles.boldLabel);
        //TODO: Fix How variables are accessed from
        if (DIInputManager.Instance != null && DIInputManager.Instance.inputMappings != null)
        {
            // Get all mapping names
            string[] mappingNames = DIInputManager.Instance.inputMappings
                .Where(m => !string.IsNullOrEmpty(m.mappingName))
                .Select(m => m.mappingName)
                .ToArray();

            // Create the popup
            if (mappingNames.Length > 0)
            {
                int currentIndex = System.Array.IndexOf(mappingNames, script.selectedMappingName);
                int newIndex = EditorGUILayout.Popup("Selected Mapping",
                    currentIndex, mappingNames);

                if (newIndex >= 0 && newIndex < mappingNames.Length)
                {
                    script.selectedMappingName = mappingNames[newIndex];
                    EditorUtility.SetDirty(script);
                }

                // Show current mapping details
                if (!string.IsNullOrEmpty(script.selectedMappingName))
                {
                    showCurrentValue = EditorGUILayout.Foldout(showCurrentValue, "Current Mapping Details");
                    if (showCurrentValue)
                    {
                        EditorGUI.indentLevel++;
                        var mapping = DIInputManager.Instance.inputMappings
                            .FirstOrDefault(m => m.mappingName == script.selectedMappingName);

                        if (mapping != null)
                        {
                            EditorGUILayout.LabelField("Device", mapping.deviceName);
                            EditorGUILayout.LabelField("Input", mapping.inputName);
                            EditorGUILayout.LabelField("Inverted", mapping.inverted.ToString());
                            EditorGUILayout.LabelField("Current Value", mapping.currentValue.ToString("F3"));
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No mapping names defined in DIInputManager",
                    MessageType.Warning
                );
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "DIInputManager not found or no mappings configured",
                MessageType.Error
            );
        }

        serializedObject.ApplyModifiedProperties();
    }
}
}
