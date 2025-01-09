using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
using System.Collections.Generic;
using DirectInputManager;
using UnityEditor;
using System;
using UnityEngine.PlayerLoop;

public class InputSelector : MonoBehaviour
{
    [System.Serializable]
    public class DeviceInputMapping
    {
        public string mappingName;
        public string deviceName;
        [SerializeField] private string _inputName;
        public float currentValue;
        public bool inverted = false;
        public string inputName
        {
            get => _inputName;
            set => _inputName = value;
        }
    }

    public DeviceInputMapping[] inputMappings = new DeviceInputMapping[1];
    private DirectInputDevice[] connectedDevices;
    private bool isInitialized = false;

    private void OnDisable()
    {
        foreach (var device in connectedDevices ?? new DirectInputDevice[0])
        {
            if (device != null)
            {
                DIManager.StopAllFFBEffects(device.description.serial);
                DIManager.Destroy(device.description.serial);
            }
        }
    }

    public void InitializeDevices()
    {
        connectedDevices = InputSystem.devices
            .OfType<DirectInputDevice>()
            .Where(d => DIManager.Attach(d.description.serial))
            .ToArray();

        isInitialized = true;
    }

    public void Update()
    {
        if (!isInitialized)
        {
            InitializeDevices();
            return;
        }
        try
        {
            foreach (var mapping in inputMappings)
            {
                if (string.IsNullOrEmpty(mapping.inputName)) continue;

                if (connectedDevices == null)
                {
                    InitializeDevices();
                }

                var device = connectedDevices.FirstOrDefault(d =>
                    d.name.Replace(":/", "").Split('/').Last() == mapping.deviceName ||
                    d.name == mapping.deviceName);
                if (device == null || !device.added) continue; // Check if device is properly added

                var control = device.allControls.FirstOrDefault(c => c.name == mapping.inputName);
                if (control != null)
                {
                    try
                    {
                        mapping.currentValue = control.magnitude;
#if UNITY_EDITOR
                        if (mapping.currentValue != 0)
                        {
                            EditorUtility.SetDirty(this);
                        }
#endif
                    }
                    catch (InvalidOperationException)
                    {
                        // Skip controls that aren't ready
                        continue;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(InputSelector))]
    public class InputSelectorEditor : Editor
    {
        private Dictionary<string, bool> isListeningForInput = new Dictionary<string, bool>();
        private double listenStartTime;
        private const double LISTEN_TIMEOUT = 5.0;

        private void OnEnable()
        {
            EditorApplication.update += ListenForInput;
        }

        private void OnDisable()
        {
            EditorApplication.update -= ListenForInput;
        }

        private Dictionary<string, float> previousValues = new Dictionary<string, float>();

        private void ListenForInput()
        {
            var script = (InputSelector)target;
            if (script.connectedDevices == null) return;

            foreach (var mapping in script.inputMappings)
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.deviceName)) continue;

                if (!isListeningForInput.TryGetValue(mapping.deviceName, out bool isListening) || !isListening)
                    continue;

                var device = script.connectedDevices.FirstOrDefault(d =>
                    d.name.Replace(":/", "").Split('/').Last() == mapping.deviceName ||
                    d.name == mapping.deviceName);
                if (device == null || !device.added) continue;

                if (EditorApplication.timeSinceStartup - listenStartTime > LISTEN_TIMEOUT)
                {
                    isListeningForInput[mapping.deviceName] = false;
                    Debug.Log($"Input listening timed out for {mapping.deviceName}");
                    continue;
                }

                foreach (var control in device.allControls)
                {
                    try
                    {
                        if (control is InputControl<float> floatControl)
                        {
                            float currentValue = floatControl.ReadValue();
                            string controlKey = $"{mapping.deviceName}_{control.name}";

                            // Get previous value, default to current value if not exists
                            if (!previousValues.ContainsKey(controlKey))
                            {
                                previousValues[controlKey] = currentValue;
                                continue;
                            }

                            float previousValue = previousValues[controlKey];
                            float changeThreshold = 0.1f; // Adjust this value as needed

                            // Detect significant value change
                            if (Mathf.Abs(currentValue - previousValue) > changeThreshold)
                            {
                                // For inverted inputs, check for negative change
                                if (mapping.inverted && currentValue < previousValue ||
                                    !mapping.inverted && currentValue > previousValue)
                                {
                                    mapping.inputName = control.name;
                                    isListeningForInput[mapping.deviceName] = false;
                                    script.InitializeDevices();
                                    EditorUtility.SetDirty(target);
                                    Debug.Log($"Input registered: {control.name} for {mapping.deviceName} with value change from {previousValue} to {currentValue}");
                                    previousValues.Clear(); // Clear the dictionary after successful detection
                                    return;
                                }
                            }

                            previousValues[controlKey] = currentValue;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                }
            }
        }


        public override void OnInspectorGUI()
        {


            var script = (InputSelector)target;

            script.Update();

            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
                Repaint();
            }

            if (GUILayout.Button("Initialize Devices"))
            {
                script.InitializeDevices();
            }

            EditorGUILayout.Space();

            SerializedProperty mappingsProperty = serializedObject.FindProperty("inputMappings");
            EditorGUILayout.PropertyField(mappingsProperty);
            EditorGUILayout.PropertyField(mappingsProperty.FindPropertyRelative("Array.size"));

            for (int i = 0; i < mappingsProperty.arraySize; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                SerializedProperty mappingProperty = mappingsProperty.GetArrayElementAtIndex(i);
                SerializedProperty deviceNameProperty = mappingProperty.FindPropertyRelative("deviceName");
                SerializedProperty inputNameProperty = mappingProperty.FindPropertyRelative("_inputName");
                SerializedProperty mappingNameProperty = mappingProperty.FindPropertyRelative("mappingName");
                SerializedProperty invertedProperty = mappingProperty.FindPropertyRelative("inverted");

                if (script.connectedDevices != null && script.connectedDevices.Length > 0)
                {
                    string[] deviceNames = script.connectedDevices.Select(d => d.name).ToArray();
                    int currentIndex = System.Array.IndexOf(deviceNames, deviceNameProperty.stringValue);
                    int newIndex = EditorGUILayout.Popup("Device", currentIndex, deviceNames);

                    if (newIndex >= 0 && newIndex < deviceNames.Length)
                    {
                        deviceNameProperty.stringValue = deviceNames[newIndex];
                    }
                }

                string deviceName = deviceNameProperty.stringValue;
                bool isListening = !string.IsNullOrEmpty(deviceName) &&
                                 isListeningForInput.ContainsKey(deviceName) &&
                                 isListeningForInput[deviceName];

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Input");

                if (isListening)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    GUILayout.Button("Listening...");
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    if (GUILayout.Button(invertedProperty.boolValue ? "Don't Invert" : "Invert"))
                    {
                        invertedProperty.boolValue = !invertedProperty.boolValue;
                    }
                    if (GUILayout.Button(string.IsNullOrEmpty(inputNameProperty.stringValue) ? "Click to assign pressed input" : inputNameProperty.stringValue))
                    {
                        isListeningForInput[deviceName] = true;
                        listenStartTime = EditorApplication.timeSinceStartup;
                        inputNameProperty.stringValue = "";
                    }
                }
                EditorGUILayout.EndHorizontal();

                SerializedProperty currentValueProperty = mappingProperty.FindPropertyRelative("currentValue");
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(currentValueProperty);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
