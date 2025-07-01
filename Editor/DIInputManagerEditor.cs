using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DirectInputManager.Editor
{
    [DefaultExecutionOrder(-741)]
    [CustomEditor(typeof(DIInputManager))]
    public class DIInputManagerEditor : UnityEditor.Editor
    {

        #region Editor Messages and UI
        public static class EditorMessages
        {
            public const string PLAY_MODE_REQUIRED =
                @"
+--------------------------------------------------+
         DIRECT INPUT & FFB MANAGER v1.0f
         By ImDanOush (ATG-Simulator.com)
+--------------------------------------------------+
 
   (>) Please enter Play Mode when the script is
       enabled to access the DirectInput and
       Force Feedback features.
 
   (!) IMPORTANT: After making changes in PlayMode,
       right-click this component and select
       'Copy Component Values' to preserve your
       settings for Edit Mode.
 
   [?] FFB Device Selection:
       Enter part of your device name below to
       auto-select it when entering Play Mode.
       Example: For 'Fanatec CSL DD',
       you can enter 'fanatec'

+--------------------------------------------------+
";
            public const string DEVICE_SEARCH_HELP =
                @"Enter partial device name to auto-select FFB device.
You can leave this empty and use the UI example script.
Example: 'fanatec' for Fanatec devices";
        }

        #endregion
        private int listeningIndex = -1;
        private Dictionary<string, float> previousValues = new();
        private double listenStartTime;
        private const double LISTEN_TIMEOUT = 5.0;
        private bool showInputSection = true;
        private bool showFFBSection = true;
        private bool showLiveValues = false;
        private bool showAxisHelper = false;

        private void ListenForInput()
        {
            var script = (DIInputManager)target;
            if (script.connectedDevices == null || listeningIndex < 0 || listeningIndex >= script.inputMappings.Length) return;

            var mapping = script.inputMappings[listeningIndex];
            if (mapping == null || string.IsNullOrEmpty(mapping.deviceName)) return;

            if (!Application.isPlaying)
            {
                listeningIndex = -1;
                Debug.LogWarning("Input listening requires Play Mode. Please enter Play Mode first.");
                return;
            }

            var device = script.connectedDevices.FirstOrDefault(d =>
                d.name.Replace(":/", "").Split('/').Last() == mapping.deviceName ||
                d.name == mapping.deviceName);

            if (device == null || !device.added) return;

            if (EditorApplication.timeSinceStartup - listenStartTime > LISTEN_TIMEOUT)
            {
                listeningIndex = -1;
                Debug.Log($"Input listening timed out for {mapping.deviceName}");
                return;
            }

            foreach (var control in device.allControls)
            {
                try
                {
                    float currentValue = 0f;
                    bool validControl = false;

                    if (control is InputControl<float> floatControl)
                    {
                        currentValue = floatControl.ReadValue();
                        validControl = true;
                    }
                    else if (control is InputControl<double> doubleControl)
                    {
                        currentValue = (float)doubleControl.ReadValue();
                        validControl = true;
                    }
                    else if (control is InputControl<Vector2> vector2Control)
                    {
                        var vec2 = vector2Control.ReadValue();
                        currentValue = Mathf.Max(Mathf.Abs(vec2.x), Mathf.Abs(vec2.y));
                        validControl = true;
                    }
                    else if (control is InputControl<bool> boolControl)
                    {
                        currentValue = boolControl.ReadValue() ? 1f : 0f;
                        validControl = true;
                    }
                    else if (control is AxisControl axisControl)
                    {
                        currentValue = axisControl.ReadValue();
                        validControl = true;
                    }

                    if (!validControl) continue;

                    string controlKey = $"{mapping.deviceName}_{control.name}";

                    if (!previousValues.ContainsKey(controlKey))
                    {
                        previousValues[controlKey] = currentValue;
                        continue;
                    }

                    float previousValue = previousValues[controlKey];
                    float changeThreshold = control is InputControl<bool> ? 0.5f : 0.1f;

                    if (Mathf.Abs(currentValue - previousValue) > changeThreshold)
                    {
                        if (mapping.inverted && currentValue < previousValue ||
                            !mapping.inverted && currentValue > previousValue)
                        {
                            mapping.inputName = control.name;

                            if (script._inputMappingCache.ContainsKey(mapping.mappingName))
                            {
                                script._inputMappingCache[mapping.mappingName] = listeningIndex;
                            }
                            else
                            {
                                script.CacheInputMapping(mapping.mappingName, listeningIndex);
                            }

                            listeningIndex = -1;
                            EditorUtility.SetDirty(target);
                            Debug.Log($"Input registered: {control.name} for {mapping.deviceName} with value change from {previousValue} to {currentValue}");
                            previousValues.Clear();
                            return;
                        }
                    }

                    previousValues[controlKey] = currentValue;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            var script = (DIInputManager)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(EditorMessages.PLAY_MODE_REQUIRED, MessageType.Info);

                EditorGUILayout.Space(10);
                SerializedProperty mappingsProperty = serializedObject.FindProperty("inputMappings");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Input Mappings", EditorStyles.boldLabel);
                if (GUILayout.Button("Add Mapping", GUILayout.Width(100)))
                {
                    mappingsProperty.arraySize++;
                    serializedObject.ApplyModifiedProperties();
                }
                EditorGUILayout.EndHorizontal();

                HashSet<string> mappingNames = new();
                bool hasDuplicates = false;

                for (int i = 0; i < mappingsProperty.arraySize; i++)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    SerializedProperty mappingProperty = mappingsProperty.GetArrayElementAtIndex(i);
                    SerializedProperty mappingNameProperty = mappingProperty.FindPropertyRelative("mappingName");
                    SerializedProperty deviceNameProperty = mappingProperty.FindPropertyRelative("deviceName");
                    SerializedProperty inputNameProperty = mappingProperty.FindPropertyRelative("_inputName");

                    string mappingName = mappingNameProperty.stringValue;
                    if (!string.IsNullOrEmpty(mappingName))
                    {
                        if (mappingNames.Contains(mappingName))
                        {
                            hasDuplicates = true;
                            EditorGUILayout.HelpBox($"Duplicate mapping name: {mappingName}", MessageType.Error);
                        }
                        else
                        {
                            mappingNames.Add(mappingName);
                        }
                    }

                    EditorGUILayout.PropertyField(mappingNameProperty);
                    EditorGUILayout.PropertyField(deviceNameProperty, new GUIContent("Device Name"));
                    EditorGUILayout.PropertyField(inputNameProperty, new GUIContent("Input Name"));

                    if (GUILayout.Button("Remove Mapping", GUILayout.Width(100)))
                    {
                        mappingsProperty.DeleteArrayElementAtIndex(i);
                        break;
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(5);
                }

                if (hasDuplicates)
                {
                    EditorGUILayout.HelpBox("Duplicate mapping names detected! Each mapping must have a unique name.", MessageType.Error);
                }

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("FFB Device Search", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(EditorMessages.DEVICE_SEARCH_HELP, MessageType.Warning);

                SerializedProperty searchTermProperty = serializedObject.FindProperty("ffbDeviceSearchTerm");
                EditorGUILayout.PropertyField(searchTermProperty, new GUIContent("Search Term"));

                SerializedProperty logsShowHide = serializedObject.FindProperty("realTimeDirectInputManagerLogs");
                EditorGUILayout.PropertyField(logsShowHide, new GUIContent("Should view FFB critical logs in runtime?"));

                serializedObject.ApplyModifiedProperties();
                return;
            }

            serializedObject.Update();
            ListenForInput();

            if (script.ffbDevice == null)
            {
                EditorGUILayout.LabelField("FFB Device Search", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(EditorMessages.DEVICE_SEARCH_HELP, MessageType.Info);
                SerializedProperty searchTermProperty = serializedObject.FindProperty("ffbDeviceSearchTerm");
                EditorGUILayout.PropertyField(searchTermProperty, new GUIContent("Search Term"));

                SerializedProperty logsShowHide = serializedObject.FindProperty("realTimeDirectInputManagerLogs");
                EditorGUILayout.PropertyField(logsShowHide, new GUIContent("Should view FFB critical logs in runtime?"));

                if (GUILayout.Button("Start Service"))
                {
                    script.StartService(searchTermProperty.stringValue);
                }
                return;
            }

            EditorUtility.SetDirty(target);
            Repaint();

            showInputSection = EditorGUILayout.Foldout(showInputSection, "Input Configuration");
            if (showInputSection)
            {
                EditorGUI.indentLevel++;
                SerializedProperty mappingsProperty = serializedObject.FindProperty("inputMappings");
                EditorGUILayout.PropertyField(mappingsProperty.FindPropertyRelative("Array.size"));

                for (int i = 0; i < mappingsProperty.arraySize; i++)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    SerializedProperty mappingProperty = mappingsProperty.GetArrayElementAtIndex(i);
                    SerializedProperty deviceNameProperty = mappingProperty.FindPropertyRelative("deviceName");
                    SerializedProperty inputNameProperty = mappingProperty.FindPropertyRelative("_inputName");
                    SerializedProperty mappingNameProperty = mappingProperty.FindPropertyRelative("mappingName");
                    SerializedProperty invertedProperty = mappingProperty.FindPropertyRelative("inverted");
                    SerializedProperty currentValueProperty = mappingProperty.FindPropertyRelative("currentValue");

                    EditorGUILayout.PropertyField(mappingNameProperty);

                    if (script.connectedDevices != null && script.connectedDevices.Length > 0)
                    {
                        string[] deviceNames = script.connectedDevices
                            .Select(d => d.name.Replace(":/", "").Split('/').Last())
                            .ToArray();
                        int currentIndex = Array.IndexOf(deviceNames,
                            deviceNameProperty.stringValue.Replace(":/", "").Split('/').Last());
                        int newIndex = EditorGUILayout.Popup("Device", currentIndex, deviceNames);

                        if (newIndex >= 0 && newIndex < deviceNames.Length)
                        {
                            deviceNameProperty.stringValue = script.connectedDevices[newIndex].name;
                        }
                    }

                    string deviceName = deviceNameProperty.stringValue;
                    bool isListening = (i == listeningIndex);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Input");

                    if (isListening)
                    {
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.LabelField($"Listening... ({Math.Round(LISTEN_TIMEOUT - (EditorApplication.timeSinceStartup - listenStartTime))}s)");
                        EditorGUI.EndDisabledGroup();
                    }
                    else
                    {
                        if (GUILayout.Button(invertedProperty.boolValue ? "Inverted" : "Not Inverted"))
                        {
                            invertedProperty.boolValue = !invertedProperty.boolValue;
                        }

                        GUI.enabled = !string.IsNullOrEmpty(deviceName);
                        if (GUILayout.Button(string.IsNullOrEmpty(inputNameProperty.stringValue) ?
                                "Click to assign input" : inputNameProperty.stringValue))
                        {
                            listeningIndex = i;
                            listenStartTime = EditorApplication.timeSinceStartup;
                            inputNameProperty.stringValue = "";
                            previousValues.Clear();
                        }
                        GUI.enabled = true;
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(currentValueProperty);
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.EndVertical();
                }
                EditorGUI.indentLevel--;

                if (script.ffbDevice != null)
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(showLiveValues ? "Hide Live FFB Device Values" : "Show Live FFB Device Values"))
                    {
                        showLiveValues = !showLiveValues;
                    }
                    if (GUILayout.Button(showAxisHelper ? "Hide Axis Helper" : "Show Axis Helper"))
                    {
                        showAxisHelper = !showAxisHelper;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (showAxisHelper)
                    {
                        EditorGUILayout.HelpBox(
                            "To identify specific controls:\n" +
                            "1. Look at the live values below\n" +
                            "2. Move your wheel/pedals/buttons\n" +
                            "3. Watch which values change\n" +
                            "Common mappings:\n" +
                            "- Steering: Usually 'stick/x' or 'x'\n" +
                            "- Throttle: Often 'z' or 'rz'\n" +
                            "- Brake: Usually 'y' or 'ry'",
                            MessageType.Info);
                    }

                    if (showLiveValues)
                    {
                        EditorGUILayout.LabelField("FFB Device Live Values", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;

                        foreach (var control in script.ffbDevice.allControls)
                        {
                            if (control is AxisControl || control is InputControl<float>)
                            {
                                float value = script.GetFFBDeviceAxisValue(control.name);
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField($"{control.name}:", GUILayout.Width(150));
                                EditorGUILayout.LabelField($"{value:F3}", EditorStyles.boldLabel);
                                EditorGUILayout.EndHorizontal();
                            }
                        }

                        EditorGUI.indentLevel--;
                    }
                    EditorGUILayout.EndVertical();
                }

                showFFBSection = EditorGUILayout.Foldout(showFFBSection, "Force Feedback Configuration");
                if (showFFBSection)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("FFB Device", script.FFBDeviceName);
                    EditorGUILayout.Space();

                    SerializedProperty iterator = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (iterator.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (iterator.name != "m_Script" &&
                            !iterator.name.StartsWith("inputMappings") &&
                            !iterator.name.StartsWith("connectedDevices") &&
                            !iterator.name.StartsWith("ffbDeviceSearchTerm"))
                        {
                            EditorGUILayout.PropertyField(iterator, true);
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }

        private void OnDisable()
        {
            if (target != null)
            {
                var script = (DIInputManager)target;
                script.CleanupDevices();
            }
        }
    }
}
