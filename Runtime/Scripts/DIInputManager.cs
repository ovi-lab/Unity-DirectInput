// This script is by ImDanOush (for his hobby project ATG-Simulator.com) and is shared for everyone.
// This uses the enhanced Direct Input Plugin (from the ATG-Simulator github) based on Mr.Tim Cakes' repo.
// This requires Unity's new Input System, IT DOES NOT REPLACE IT as this script designed to manage DInputs & FFB!

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DirectInputManager;
using System;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
[DefaultExecutionOrder(-745)]
public class DIInputManager : Singleton<DIInputManager>
{

    [SerializeField] private string ffbDeviceSearchTerm = string.Empty;
    private bool hasSearchedDevice = false;

    #region Static Configuration
    private static readonly float COLLISION_FADE_TIME = 0.2f;
    private static readonly float INITIAL_IMPACT_DURATION = 0.15f;
    private static readonly Dictionary<string, DirectInputDevice> DeviceCache = new();
    #endregion

    #region Device Management
    public bool realTimeDirectInputManagerLogs;
    public DirectInputDevice[] connectedDevices;
    public DirectInputDevice ffbDevice;
    private bool isInitialized = false;
    public string FFBDeviceName = "No FFB Device Connected";
    [HideInInspector]
    public string FFBDeviceSerial = "";
    #endregion

    #region Input Mapping Properties

    public DeviceInputMapping[] inputMappings = new DeviceInputMapping[1];
    #endregion

    #region FFB Properties
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
    [Tooltip("You can use Unity's Input system for input management (recommended) unless you will be dealing with DirectInput devices for vehicle simulations then this manager is recommended.")]
    public bool useDInputManager = true;
    public Dictionary<string, int> _inputMappingCache = new();

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

    private bool forgetIt = false;
    #endregion

    private Dictionary<string, InputControl> controlLookup = new();


    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        ResetAllFFBEffects();
        if (ffbDevice != null)
        {
            DIManager.StopAllFFBEffects(ffbDevice.description.serial);
        }

        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        ResetAllFFBEffects();
        if (ffbDevice != null)
        {
            DIManager.StopAllFFBEffects(ffbDevice.description.serial);
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
        ResetAllFFBEffects();
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

    private void ResetAllFFBEffects()
    {
        ConstantForceEnabled = false;
        DamperForceEnabled = false;
        FrictionForceEnabled = false;
        InertiaForceEnabled = false;
        RampForceEnabled = false;
        SawtoothDownForceEnabled = false;
        SawtoothUpForceEnabled = false;
        SineForceEnabled = false;
        SquareForceEnabled = false;
        TriangleForceEnabled = false;
        SpringForceEnabled = false;
    }

    public void InitializeDevices(string searchTerm = "")
    {
        if (!Application.isPlaying || forgetIt) return;

        if (searchTerm != "" && connectedDevices is { Length: > 0 })
        {
            Debug.Log("trying to load FFB");

            if (ffbDevice == null)
                hasSearchedDevice = false;
            else
                return;

            List<DirectInputDevice> allFFBDevices = connectedDevices
                                .Where(d => d.description.capabilities.Contains("\"FFBCapable\":true"))?.ToList() ?? null;

            if (allFFBDevices != null)
            {
                if (!hasSearchedDevice)
                {
                    int deviceIndex = allFFBDevices.FindIndex(d =>
                        d.name.ToLower().Contains(searchTerm.ToLower()));
                    if (deviceIndex >= 0)
                    {
                        fFBDeviceNumber = deviceIndex;
                        hasSearchedDevice = true;
                    }
                }

                ffbDevice = allFFBDevices[allFFBDevices.Count > fFBDeviceNumber ? fFBDeviceNumber : 0];
            }

            if (ffbDevice != null)
            {
                FFBDeviceName = $"{ffbDevice.name} : {ffbDevice.description.serial}";
                FFBDeviceSerial = ffbDevice.description.serial;
                Debug.Log($"FFB Device initialized: {FFBDeviceName}");
            }
            else
            {
                FFBDeviceSerial = "";
                Debug.Log("No FFB Device initialized");
            }

            if (ffbDevice.name.ToLower().Contains("vjoy"))
            {
                ffbDevice = null;
                forgetIt = true;
            }
        }
        else
        {
            Debug.Log("trying to load DIMap");

            if ((connectedDevices?.Length ?? 0) == 0)
                connectedDevices = InputSystem.devices
                        .OfType<DirectInputDevice>()
                        .Where(d => DIManager.Attach(d.description.serial))
                        .ToArray();

            if (inputMappings != null)
            {
                _inputMappingCache = new();

                for (int i = 0; i < inputMappings.Length; i++)
                {
                    var mapping = inputMappings[i];
                    if (!string.IsNullOrEmpty(mapping.mappingName))
                    {
                        CacheInputMapping(mapping.mappingName, i);
                    }
                }
            }

            isInitialized = true;
        }
    }
    private DirectInputDevice GetDeviceByName(string deviceName)
    {
        if (!Application.isPlaying) return null;

        if (DeviceCache.ContainsKey(deviceName))
            return DeviceCache[deviceName];

        var device = connectedDevices?.FirstOrDefault(d =>
            d.name.Replace(":/", "").Split('/').Last() == deviceName ||
            d.name == deviceName);

        if (device != null)
            DeviceCache[deviceName] = device;

        return device;
    }

    private float GetDeviceInputValue(DirectInputDevice device, string inputName)
    {
        if (!Application.isPlaying) return 0f;

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
        if (ffbDevice == null || connectedDevices == null || connectedDevices.Length == 0)
        {
            if (isInitialized)
            {
                if (ffbDeviceSearchTerm != string.Empty)
                    StartService(ffbDeviceSearchTerm);
            }
            else InitializeDevices();
            return;
        }

        if (useDInputManager)
            UpdateInputMappings();

        UpdateFFBEffects();
    }

    public void StartService(string searchTerm)
    {
        CleanupDevices();
        InitializeDevices(searchTerm);
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
        if (ffbDevice == null) return;

        DIManager.showLogsRuntime = realTimeDirectInputManagerLogs;

        if (ConstantForceEnabled)
        {
            if (ConstantForceWasEnabled)
            {
                if (!DIManager.UpdateConstantForceSimple(ffbDevice.description.serial, ConstantForceMagnitude))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce);
                    ConstantForceWasEnabled = false;
                }
                else
                {
                    ConstantForceWasEnabled = true;
                }
            }
            else
            {
                if (!DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce);
                    ConstantForceWasEnabled = false;
                }
                else
                {
                    ConstantForceWasEnabled = true;
                }
            }
        }
        else if (ConstantForceWasEnabled)
        {
            ConstantForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.ConstantForce);
        }

        if (DamperForceEnabled)
        {
            if (DamperForceWasEnabled)
            {
                if (!DIManager.UpdateDamperSimple(ffbDevice.description.serial, DamperMagnitude))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Damper);
                    DamperForceWasEnabled = false;
                }
                else
                {
                    DamperForceWasEnabled = true;
                }
            }
            else
            {
                if (!DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Damper))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Damper);
                    DamperForceWasEnabled = false;
                }
                else
                {
                    DamperForceWasEnabled = true;
                }
            }
        }
        else if (DamperForceWasEnabled)
        {
            DamperForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Damper);
        }

        if (FrictionForceEnabled)
        {
            if (FrictionForceWasEnabled)
            {
                if (!DIManager.UpdateFrictionSimple(ffbDevice.description.serial, FrictionMagnitude))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Friction);
                    FrictionForceWasEnabled = false;
                }
                else
                {
                    FrictionForceWasEnabled = true;
                }
            }
            else
            {
                if (!DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Friction))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Friction);
                    FrictionForceWasEnabled = false;
                }
                else
                {
                    FrictionForceWasEnabled = true;
                }
            }
        }
        else if (FrictionForceWasEnabled)
        {
            FrictionForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Friction);
        }

        if (InertiaForceEnabled)
        {
            if (InertiaForceWasEnabled)
            {
                if (!DIManager.UpdateInertiaSimple(ffbDevice.description.serial, InertiaMagnitude))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia);
                    InertiaForceWasEnabled = false;
                }
                else
                {
                    InertiaForceWasEnabled = true;
                }
            }
            else
            {
                if (!DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia);
                    InertiaForceWasEnabled = false;
                }
                else
                {
                    InertiaForceWasEnabled = true;
                }
            }
        }
        else if (InertiaForceWasEnabled)
        {
            InertiaForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Inertia);
        }

        if (SpringForceEnabled)
        {
            if (SpringForceWasEnabled)
            {
                if (!DIManager.UpdateSpringSimple(ffbDevice.description.serial, SpringDeadband, SpringOffset,
                        SpringCoefficient, SpringCoefficient, SpringSaturation, SpringSaturation))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Spring);
                    SpringForceWasEnabled = false;
                }
                else
                {
                    SpringForceWasEnabled = true;
                }
            }
            else
            {
                if (!DIManager.EnableFFBEffect(ffbDevice.description.serial, FFBEffects.Spring))
                {
                    DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Spring);
                    SpringForceWasEnabled = false;
                }
                else
                {
                    SpringForceWasEnabled = true;
                }
            }
        }
        else if (SpringForceWasEnabled)
        {
            SpringForceWasEnabled = false;
            DIManager.DestroyFFBEffect(ffbDevice.description.serial, FFBEffects.Spring);
        }
        UpdatePeriodicForces();
        TestDirectionalCollision();
    }

    private void UpdatePeriodicForces()
    {
        if (ffbDevice == null) return;

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

    private void TestDirectionalCollision()
    {
        if (ffbDevice == null) return;

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
        if (!Application.isPlaying) yield break;

        int initialConstant = ConstantForceMagnitude;
        int initialSpring = SpringCoefficient;
        int initialSpringOffset = SpringOffset;
        int initialDamper = DamperMagnitude;

        yield return new WaitForSeconds(INITIAL_IMPACT_DURATION);

        float elapsed = 0f;

        while (elapsed < COLLISION_FADE_TIME)
        {
            float t = elapsed / COLLISION_FADE_TIME;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

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

        ConstantForceEnabled = false;
        SpringForceEnabled = false;
        DamperForceEnabled = false;
    }

    public float GetFFBDeviceAxisValue(string axisName)
    {
        if (ffbDevice == null || !ffbDevice.added) return 0f;
        if (controlLookup.Count == 0 || controlLookup == null)
        {
            // controlLookup = ffbDevice.allControls.ToDictionary(c => c.name);
            Dictionary<string, InputControl> dictionary = new Dictionary<string, InputControl>();
            foreach (InputControl control in ffbDevice.allControls)
            {
                if(!dictionary.TryAdd(control.name, control))
                {
                    Debug.LogWarning($"===={control.name} : {control}");
                }
            }
            controlLookup = dictionary;
        }
        try
        {
            var control = controlLookup[axisName];
            // var control = ffbDevice.allControls.FirstOrDefault(c => c.name == axisName);
            if (control != null)
            {
                if (control is AxisControl axisControl)
                {
                    return axisControl.ReadValue();
                }
                else if (control is InputControl<float> floatControl)
                {
                    return floatControl.ReadValue();
                }
            }
        }
        catch (InvalidOperationException)
        {
            Debug.LogWarning($"Failed to read axis {axisName} from FFB device");
        }

        return 0f;
    }


    public void CacheInputMapping(string entryName, int index)
    {
        if (_inputMappingCache.ContainsKey(entryName))
        {
            Debug.LogError($"Duplicate entry name found: {entryName}. Application will quit.");
            Application.Quit();
            return;
        }
        _inputMappingCache[entryName] = index;
    }

    public int? GetMappingByName(string entryName)
    {
        return _inputMappingCache.TryGetValue(entryName, out int index) ? index : null;
    }

    public void UpdateAxisMapping(string axisName, int newIndex)
    {
        if (_inputMappingCache.ContainsKey(axisName))
        {
            int oldIndex = _inputMappingCache[axisName];
            Debug.Log($"Updating '{axisName}' from index {oldIndex} to {newIndex}");
            _inputMappingCache[axisName] = newIndex;
        }
        else
        {
            Debug.Log($"'{axisName}' not found in cache. Adding it now.");
            CacheInputMapping(axisName, newIndex);
        }
    }


    [Serializable]
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

}

