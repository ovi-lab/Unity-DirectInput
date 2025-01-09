using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DirectInputManager;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using System;
using UnityEditor.Hardware;

public class DIInputManager : MonoBehaviour
{
    #region Singleton Pattern
    private static DIInputManager _instance;
    public static DIInputManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<DIInputManager>();

                if (_instance == null)
                {
                    GameObject go = new GameObject("DIInputManager");
                    _instance = go.AddComponent<DIInputManager>();
                }
            }
            return _instance;
        }
    }
    private void OnEnable()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        if (EnableFFB)
        {
            EnableFFB = false;
            if (ffbDevice != null)
            {
                DIManager.StopAllFFBEffects(ffbDevice.description.serial);
            }
        }
    }
    private void OnDestroy()
    {
        if (connectedDevices != null)
        {
            foreach (var device in connectedDevices)
            {
                if (device != null)
                {
                    if (device.description.capabilities.Contains("\"FFBCapable\":true"))
                        DIManager.StopAllFFBEffects(device.description.serial);
                    DIManager.Destroy(device.description.serial);
                }
            }
        }

        if (ffbDevice != null)
        {
            DIManager.Destroy(ffbDevice.description.serial);
        }
        isInitialized = false;
        connectedDevices = null;
    }
    public void CleanupDevices()
    {
        if (connectedDevices != null)
        {
            foreach (var device in connectedDevices)
            {
                if (device != null)
                {
                    if (device.description.capabilities.Contains("\"FFBCapable\":true"))
                        DIManager.StopAllFFBEffects(device.description.serial);
                }
            }
        }
    }
    #endregion

    #region Device Management
    public DirectInputDevice[] connectedDevices;
    private DirectInputDevice ffbDevice;
    private bool isInitialized = false;
    public string FFBDeviceName = "No FFB Device Connected";
    [HideInInspector]
    public string FFBDeviceSerial = "";
    #endregion

    #region Input Mapping Properties
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
    #endregion

    #region FFB Properties
    public bool EnableFFB = true;

    [Range(0, 126)] public int fFBDeviceNumber = 0;

    [Header("FFB Constant Force")]
    public bool ConstantForceEnabled = false;
    [Range(-10000f, 10000f)] public int ConstantForceMagnitude;

    [Header("FFB Damper")]
    public bool DamperForceEnabled = false;
    [Range(-10000f, 10000f)] public int DamperMagnitude;

    [Header("FFB Friction")]
    public bool FrictionForceEnabled = false;
    [Range(-10000f, 10000f)] public int FrictionMagnitude;

    [Header("FFB Inertia")]
    public bool InertiaForceEnabled = false;
    [Range(-10000f, 10000f)] public int InertiaMagnitude;

    [Header("FFB Spring")]
    public bool SpringForceEnabled = false;
    [Range(0, 10000f)] public uint SpringDeadband;
    [Range(-10000f, 10000f)] public int SpringOffset;
    [Range(0, 10000f)] public int SpringCoefficient;
    [Range(0, 10000f)] public uint SpringSaturation;

    [Header("Periodic & Custom Effects \nPlease note that negative values may result unexpected FFB! \n\nFFB Sine")]
    public bool SineForceEnabled = false;
    [Range(-10000f, 10000f)] public int SineMagnitude;
    [Range(0, 100000)] public uint SinePeriod = 30000;

    [Header("FFB Square")]
    public bool SquareForceEnabled = false;
    [Range(-10000f, 10000f)] public int SquareMagnitude;
    [Range(0, 100000)] public uint SquarePeriod = 30000;

    [Header("FFB Triangle")]
    public bool TriangleForceEnabled = false;
    [Range(-10000f, 10000f)] public int TriangleMagnitude;
    [Range(0, 100000)] public uint TrianglePeriod = 30000;

    [Header("FFB SawtoothUp")]
    public bool SawtoothUpForceEnabled = false;
    [Range(-10000f, 10000f)] public int SawtoothUpMagnitude;
    [Range(0, 100000)] public uint SawtoothUpPeriod = 30000;

    [Header("FFB SawtoothDown")]
    public bool SawtoothDownForceEnabled = false;
    [Range(-10000f, 10000f)] public int SawtoothDownMagnitude;
    [Range(0, 100000)] public uint SawtoothDownPeriod = 30000;

    [Header("FFB Ramp")]
    public bool RampForceEnabled = false;
    [Range(-10000f, 10000f)] public int RampStart;
    [Range(-10000f, 10000f)] public int RampEnd;

    [Header("Test Collision Effects")]
    [Range(0, 10000)] public int TestCollisionMagnitude = 5000;
    public bool TestFrontalCollision = false;
    public bool TestRearCollision = false;
    public bool TestLeftCollision = false;
    public bool TestRightCollision = false;

    // FFB Effect States
    private bool ConstantForceWasEnabled { get; set; }
    private bool DamperForceWasEnabled { get; set; }
    private bool FrictionForceWasEnabled { get; set; }
    private bool InertiaForceWasEnabled { get; set; }
    private bool SpringForceWasEnabled { get; set; }
    private bool SineForceWasEnabled { get; set; }
    private bool SquareForceWasEnabled { get; set; }
    private bool TriangleForceWasEnabled { get; set; }
    private bool SawtoothUpForceWasEnabled { get; set; }
    private bool SawtoothDownForceWasEnabled { get; set; }
    private bool RampForceWasEnabled { get; set; }
    #endregion
    #region Core Initialization and Update Methods

    public void InitializeDevices()
    {
        // Get all DirectInput devices and attempt to attach to them
        connectedDevices = InputSystem.devices
                .OfType<DirectInputDevice>()
                .Where(d => DIManager.Attach(d.description.serial))
                .ToArray();

        // Find the first FFB-capable device
        var allFFBDevices = connectedDevices
            .Where(d => d.description.capabilities.Contains("\"FFBCapable\":true"))?.ToList() ?? null;
        if (allFFBDevices != null)
        {
            ffbDevice = allFFBDevices[allFFBDevices.Count >= fFBDeviceNumber ? fFBDeviceNumber : 0];
        }

        if (ffbDevice != null)
        {
            FFBDeviceName = $"{ffbDevice.name} : {ffbDevice.description.serial}";
            FFBDeviceSerial = ffbDevice.description.serial;
            Debug.Log($"FFB Device initialized: {FFBDeviceName}");
        }

        isInitialized = true;
    }

    private DirectInputDevice GetDeviceByName(string deviceName)
    {
        return connectedDevices?.FirstOrDefault(d =>
            d.name.Replace(":/", "").Split('/').Last() == deviceName ||
            d.name == deviceName);
    }

    private float GetDeviceInputValue(DirectInputDevice device, string inputName)
    {
        try
        {
            var control = device?.allControls.FirstOrDefault(c => c.name == inputName);
            return control?.magnitude ?? 0f;
        }
        catch (InvalidOperationException)
        {
            return 0f;
        }
    }

    public void Update()
    {
        if (!isInitialized || connectedDevices == null || connectedDevices.Length == 0)
        {
            CleanupDevices();
            InitializeDevices();
            return;
        }

        UpdateInputMappings();
        UpdateFFBEffects();
    }

    private void UpdateInputMappings()
    {
        try
        {
            foreach (var mapping in inputMappings)
            {
                if (string.IsNullOrEmpty(mapping.inputName)) continue;

                var device = GetDeviceByName(mapping.deviceName);
                if (device == null || !device.added) continue;

                mapping.currentValue = GetDeviceInputValue(device, mapping.inputName);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating input mappings: {e.Message}");
        }
    }

    private void UpdateFFBEffects()
    {
        if (!EnableFFB || ffbDevice == null) return;

        // Update Constant Force
        if (ConstantForceEnabled)
        {
            if (ConstantForceWasEnabled)
                DIManager.UpdateConstantForceSimple(ffbDevice.description.serial, ConstantForceMagnitude);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce);

            ConstantForceWasEnabled = true;
        }
        else if (ConstantForceWasEnabled)
        {
            ConstantForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce);
        }

        // Update Damper Force
        if (DamperForceEnabled)
        {
            if (DamperForceWasEnabled)
                DIManager.UpdateDamperSimple(ffbDevice.description.serial, DamperMagnitude);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Damper);

            DamperForceWasEnabled = true;
        }
        else if (DamperForceWasEnabled)
        {
            DamperForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Damper);
        }

        // Update Friction Force
        if (FrictionForceEnabled)
        {
            if (FrictionForceWasEnabled)
                DIManager.UpdateFrictionSimple(ffbDevice.description.serial, FrictionMagnitude);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Friction);

            FrictionForceWasEnabled = true;
        }
        else if (FrictionForceWasEnabled)
        {
            FrictionForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Friction);
        }

        // Update Inertia Force
        if (InertiaForceEnabled)
        {
            if (InertiaForceWasEnabled)
                DIManager.UpdateInertiaSimple(ffbDevice.description.serial, InertiaMagnitude);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia);

            InertiaForceWasEnabled = true;
        }
        else if (InertiaForceWasEnabled)
        {
            InertiaForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia);
        }

        // Update Spring Force
        if (SpringForceEnabled)
        {
            if (SpringForceWasEnabled)
                DIManager.UpdateSpringSimple(ffbDevice.description.serial, SpringDeadband, SpringOffset,
                    SpringCoefficient, SpringCoefficient, SpringSaturation, SpringSaturation);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Spring);

            SpringForceWasEnabled = true;
        }
        else if (SpringForceWasEnabled)
        {
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Spring);
            SpringForceWasEnabled = false;
        }

        UpdatePeriodicForces();
        TestDirectionalCollision();
    }

    private void UpdatePeriodicForces()
    {
        if (ffbDevice == null) return;

        // Update Sine Force
        if (SineForceEnabled)
        {
            if (SineForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.Sine, SineMagnitude, SinePeriod);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Sine);

            SineForceWasEnabled = true;
        }
        else if (SineForceWasEnabled)
        {
            SineForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Sine);
        }

        // Update Square Force
        if (SquareForceEnabled)
        {
            if (SquareForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.Square, SquareMagnitude, SquarePeriod);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Square);

            SquareForceWasEnabled = true;
        }
        else if (SquareForceWasEnabled)
        {
            SquareForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Square);
        }

        // Update Triangle Force
        if (TriangleForceEnabled)
        {
            if (TriangleForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.Triangle, TriangleMagnitude, TrianglePeriod);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Triangle);

            TriangleForceWasEnabled = true;
        }
        else if (TriangleForceWasEnabled)
        {
            TriangleForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Triangle);
        }

        // Update SawtoothUp Force
        if (SawtoothUpForceEnabled)
        {
            if (SawtoothUpForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.SawtoothUp, SawtoothUpMagnitude, SawtoothUpPeriod);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.SawtoothUp);

            SawtoothUpForceWasEnabled = true;
        }
        else if (SawtoothUpForceWasEnabled)
        {
            SawtoothUpForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.SawtoothUp);
        }

        // Update SawtoothDown Force
        if (SawtoothDownForceEnabled)
        {
            if (SawtoothDownForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.SawtoothDown, SawtoothDownMagnitude, SawtoothDownPeriod);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.SawtoothDown);

            SawtoothDownForceWasEnabled = true;
        }
        else if (SawtoothDownForceWasEnabled)
        {
            SawtoothDownForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.SawtoothDown);
        }

        // Update Ramp Force
        if (RampForceEnabled)
        {
            if (RampForceWasEnabled)
                DIManager.UpdatePeriodicSimple(ffbDevice.description.serial, FFBEffects.RampForce, 0, 0, RampStart, RampEnd);
            else
                DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.RampForce);

            RampForceWasEnabled = true;
        }
        else if (RampForceWasEnabled)
        {
            RampForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.RampForce);
        }
    }
    #endregion
    #region Collision and FFB Effect Methods
    private void TestDirectionalCollision()
    {
        if (!EnableFFB || ffbDevice == null) return;

        // Test Frontal Collision (Negative force)
        if (TestFrontalCollision)
        {
            ConstantForceMagnitude = -TestCollisionMagnitude;
            DamperMagnitude = TestCollisionMagnitude / 2;
            SpringCoefficient = TestCollisionMagnitude / 4;

            ConstantForceEnabled = true;
            DamperForceEnabled = true;
            SpringForceEnabled = true;

            StartCoroutine(ResetTestCollision("front"));
            TestFrontalCollision = false;
        }

        // Test Rear Collision (Positive force)
        if (TestRearCollision)
        {
            ConstantForceMagnitude = TestCollisionMagnitude;
            DamperMagnitude = TestCollisionMagnitude / 2;
            SpringCoefficient = TestCollisionMagnitude / 4;

            ConstantForceEnabled = true;
            DamperForceEnabled = true;
            SpringForceEnabled = true;

            StartCoroutine(ResetTestCollision("rear"));
            TestRearCollision = false;
        }

        // Test Left Collision (Positive force)
        if (TestLeftCollision)
        {
            ConstantForceMagnitude = TestCollisionMagnitude;
            SpringOffset = TestCollisionMagnitude / 2;
            SpringCoefficient = TestCollisionMagnitude / 3;

            ConstantForceEnabled = true;
            SpringForceEnabled = true;

            StartCoroutine(ResetTestCollision("left"));
            TestLeftCollision = false;
        }

        // Test Right Collision (Negative force)
        if (TestRightCollision)
        {
            ConstantForceMagnitude = -TestCollisionMagnitude;
            SpringOffset = -TestCollisionMagnitude / 2;
            SpringCoefficient = TestCollisionMagnitude / 3;

            ConstantForceEnabled = true;
            SpringForceEnabled = true;

            StartCoroutine(ResetTestCollision("right"));
            TestRightCollision = false;
        }
    }

    private IEnumerator ResetTestCollision(string direction)
    {
        // Store initial values
        int initialConstant = ConstantForceMagnitude;
        int initialSpring = SpringCoefficient;
        int initialSpringOffset = SpringOffset;
        int initialDamper = DamperMagnitude;

        // Initial impact duration
        yield return new WaitForSeconds(0.15f);

        // Fade out duration
        float fadeTime = 0.2f;
        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            float t = elapsed / fadeTime;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            // Smoothly reduce all forces to zero
            ConstantForceMagnitude = Mathf.RoundToInt(Mathf.Lerp(initialConstant, 0, smoothT));
            SpringCoefficient = Mathf.RoundToInt(Mathf.Lerp(initialSpring, 0, smoothT));
            SpringOffset = Mathf.RoundToInt(Mathf.Lerp(initialSpringOffset, 0, smoothT));

            if (direction == "front" || direction == "rear")
            {
                DamperMagnitude = Mathf.RoundToInt(Mathf.Lerp(initialDamper, 0, smoothT));
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Disable all forces
        ConstantForceEnabled = false;
        SpringForceEnabled = false;
        DamperForceEnabled = false;
    }

    #endregion
} // End of DIInputManager class

#if UNITY_EDITOR
[CustomEditor(typeof(DIInputManager))]
public class DIInputManagerEditor : Editor
{
    private Dictionary<string, bool> isListeningForInput = new Dictionary<string, bool>();
    private Dictionary<string, float> previousValues = new Dictionary<string, float>();
    private double listenStartTime;
    private const double LISTEN_TIMEOUT = 5.0;
    private bool showInputSection = true;
    private bool showFFBSection = true;

    private void ListenForInput()
    {
        var script = (DIInputManager)target;
        if (script.connectedDevices == null) return;

        foreach (var mapping in script.inputMappings)
        {
            if (mapping == null || string.IsNullOrEmpty(mapping.deviceName)) continue;

            if (!isListeningForInput.TryGetValue(mapping.deviceName, out bool isListening) || !isListening)
                continue;

            if (!Application.isPlaying)
            {
                isListeningForInput[mapping.deviceName] = false;
                Debug.LogError($"Input listening failed for {mapping.deviceName} and cleaned, You must assign Inputs in play mode due to limitations of Unity's Input System. \n ATTENTION PLEASE: Do not forget to right-click and copy inspector properties to save in Edit mode later! Unity does not save anything when running in PlayMode!");
                continue;
            }

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

                        if (!previousValues.ContainsKey(controlKey))
                        {
                            previousValues[controlKey] = currentValue;
                            continue;
                        }

                        float previousValue = previousValues[controlKey];
                        float changeThreshold = 0.1f;

                        if (Mathf.Abs(currentValue - previousValue) > changeThreshold)
                        {
                            if (mapping.inverted && currentValue < previousValue ||
                                !mapping.inverted && currentValue > previousValue)
                            {
                                mapping.inputName = control.name;
                                isListeningForInput[mapping.deviceName] = false;
                                EditorUtility.SetDirty(target);
                                Debug.Log($"Input registered: {control.name} for {mapping.deviceName} with value change from {previousValue} to {currentValue}");
                                previousValues.Clear();
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
    DIInputManager script;
    string serialNumber;
    public override void OnInspectorGUI()
    {
        script = (DIInputManager)target;
        if (script.enabled)
            script.Update();
        else
            OnDisable();
        serialNumber = script.FFBDeviceSerial;
        ListenForInput();

        if (Application.isPlaying)
        {
            EditorUtility.SetDirty(target);
            Repaint();
        }

        // Input Section
        showInputSection = EditorGUILayout.Foldout(showInputSection, "Input Configuration");
        if (showInputSection)
        {
            EditorGUI.indentLevel++;

            //if (GUILayout.Button("Initialize Devices"))
            //{
            //    script.CleanupDevices();
            //    script.InitializeDevices();
            //}

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

                EditorGUILayout.PropertyField(mappingNameProperty);

                if (script.connectedDevices != null && script.connectedDevices.Length > 0)
                {
                    string[] deviceNames = script.connectedDevices.Select(d => d.name).ToArray();
                    int currentIndex = Array.IndexOf(deviceNames, deviceNameProperty.stringValue);
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
                    if (GUILayout.Button(invertedProperty.boolValue ? "Inverted" : "Not Inverted"))
                    {
                        invertedProperty.boolValue = !invertedProperty.boolValue;
                    }
                    if (GUILayout.Button(string.IsNullOrEmpty(inputNameProperty.stringValue) ?
                        "Click to assign input" : inputNameProperty.stringValue))
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

            EditorGUI.indentLevel--;
        }

        // FFB Section
        showFFBSection = EditorGUILayout.Foldout(showFFBSection, "Force Feedback Configuration");
        if (showFFBSection)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("FFB Device", script.FFBDeviceName);
            EditorGUILayout.Space();

            // Draw all FFB properties
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name != "m_Script" &&
                    !iterator.name.StartsWith("inputMappings") &&
                    !iterator.name.StartsWith("connectedDevices"))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }

            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void OnDisable()
    {
        script.ConstantForceEnabled = false;
        script.DamperForceEnabled = false;
        script.FrictionForceEnabled = false;
        script.InertiaForceEnabled = false;
        script.RampForceEnabled = false;
        script.SawtoothDownForceEnabled = false;
        script.SawtoothUpForceEnabled = false;
        script.SineForceEnabled = false;
        script.SquareForceEnabled = false;
        script.TriangleForceEnabled = false;
        script.SpringForceEnabled = false;
    }
}
#endif
