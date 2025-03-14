// DirectInputForceFeedback.cpp : Defines the exported functions for the DLL.
#include "pch.h"
#include <algorithm>
#include <limits>
#include <cstdlib>
#include "DirectInputForceFeedback.h"
#include <strsafe.h>

// Global variables
LPDIRECTINPUT8 _DirectInput = nullptr;
bool g_UnityInitialized = false;
std::string g_LastErrorMessage;

typedef std::string DeviceGUID;

std::vector<DeviceInfo> _DeviceInstances;
std::map<DeviceGUID, LPDIRECTINPUTDEVICE8> _ActiveDevices;
std::map<DeviceGUID, std::vector<DIEFFECTINFO>> _DeviceEnumeratedEffects;
std::map<DeviceGUID, std::vector<DIDEVICEOBJECTINSTANCE>> _DeviceFFBAxes;
std::map<DeviceGUID, std::map<Effects::Type, DIEFFECT>> _DeviceFFBEffectConfig;
std::map<DeviceGUID, std::map<Effects::Type, LPDIRECTINPUTEFFECT>> _DeviceFFBEffectControl;

DeviceChangeCallback g_deviceCallback = nullptr;
std::vector<std::wstring> DEBUGDATA;

// Unity plugin lifecycle functions
extern "C" DIRECTINPUTFORCEFEEDBACK_API void UNITY_INTERFACE_API UnityPluginLoad(void* unityInterfaces) {
	g_UnityInitialized = true;
	LogMessage("UnityPluginLoad called");
	StartDirectInput();
}

extern "C" DIRECTINPUTFORCEFEEDBACK_API void UNITY_INTERFACE_API UnityPluginUnload() {
	g_UnityInitialized = false;
	LogMessage("UnityPluginUnload called");
	StopDirectInput();
}

extern "C" DIRECTINPUTFORCEFEEDBACK_API int UNITY_INTERFACE_API GetPluginVersion() {
	return 1; // Increment this when making significant changes
}

extern "C" DIRECTINPUTFORCEFEEDBACK_API void InitializeForStandalone() {
	// Call UnityPluginLoad with a null pointer or dummy value
	UnityPluginLoad(nullptr);
}

// Logging function
void LogMessage(const char* format, ...) {
	char buffer[1024];
	va_list args;
	va_start(args, format);
	vsnprintf(buffer, sizeof(buffer), format, args);
	va_end(args);

	OutputDebugStringA(buffer);
	OutputDebugStringA("\n");
}

// Helper function to set last error message
void SetLastErrorMessage(const char* message) {
	g_LastErrorMessage = message;
}

// DLL Exported Functions
DIRECTINPUTFORCEFEEDBACK_API HRESULT StartDirectInput() {
	if (_DirectInput != nullptr) {
		LogMessage("DirectInput already initialized");
		return S_OK;
	}

	HRESULT hr = DirectInput8Create(
		GetModuleHandle(NULL),
		DIRECTINPUT_VERSION,
		IID_IDirectInput8,
		(void**)&_DirectInput,
		NULL
	);

	if (FAILED(hr)) {
		LogMessage("Failed to initialize DirectInput: 0x%08X", hr);
		SetLastErrorMessage("Failed to initialize DirectInput");
		return hr;
	}

	LogMessage("DirectInput initialized successfully");
	return S_OK;
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT StopDirectInput() {
	HRESULT hr = S_OK;

	if (_DirectInput == nullptr) {
		LogMessage("DirectInput already stopped");
		return S_OK;
	}

	for (const auto& [GUIDString, Device] : _ActiveDevices) {
		hr = StopAllFFBEffects(GUIDString.c_str());
		if (FAILED(hr)) {
			LogMessage("Failed to stop FFB effects for device %s: 0x%08X", GUIDString.c_str(), hr);
		}

		hr = Device->Unacquire();
		if (FAILED(hr)) {
			LogMessage("Failed to unacquire device %s: 0x%08X", GUIDString.c_str(), hr);
		}

		Device->Release();
	}

	_DeviceInstances.clear();
	_ActiveDevices.clear();
	_DeviceEnumeratedEffects.clear();
	_DeviceFFBAxes.clear();
	_DeviceFFBEffectConfig.clear();
	_DeviceFFBEffectControl.clear();

	if (_DirectInput) {
		_DirectInput->Release();
		_DirectInput = nullptr;
	}

	LogMessage("DirectInput stopped successfully");
	return S_OK;
}
DIRECTINPUTFORCEFEEDBACK_API DeviceInfo* EnumerateDevices(int& deviceCount) {
	try {
		HRESULT hr = E_FAIL;
		deviceCount = 0;

		if (_DirectInput == nullptr) {
			LogMessage("EnumerateDevices: DirectInput not initialized");
			SetLastErrorMessage("DirectInput not initialized");
			return nullptr;
		}

		_DeviceInstances.clear();

		// Enumerate all game controllers
		hr = _DirectInput->EnumDevices(
			DI8DEVCLASS_GAMECTRL,
			_EnumDevicesCallback,
			NULL,
			DIEDFL_ATTACHEDONLY
		);

		if (FAILED(hr)) {
			LogMessage("EnumerateDevices: Failed to enumerate devices: 0x%08X", hr);
			SetLastErrorMessage("Failed to enumerate devices");
			return nullptr;
		}

		// Enumerate FFB devices (update FFBCapable)
		hr = _DirectInput->EnumDevices(
			DI8DEVCLASS_GAMECTRL,
			_EnumDevicesCallbackFFB,
			NULL,
			DIEDFL_ATTACHEDONLY | DIEDFL_FORCEFEEDBACK
		);

		if (FAILED(hr)) {
			LogMessage("EnumerateDevices: Failed to enumerate FFB devices: 0x%08X", hr);
			SetLastErrorMessage("Failed to enumerate FFB devices");
			return nullptr;
		}

		if (!_DeviceInstances.empty()) {
			deviceCount = static_cast<int>(_DeviceInstances.size());
			LogMessage("EnumerateDevices: Found %d devices", deviceCount);
			return &_DeviceInstances[0];
		}
		else {
			LogMessage("EnumerateDevices: No devices found");
			return nullptr;
		}
	}
	catch (const std::exception& e) {
		LogMessage("EnumerateDevices: Exception: %s", e.what());
		SetLastErrorMessage(e.what());
		deviceCount = 0;
		return nullptr;
	}
	catch (...) {
		LogMessage("EnumerateDevices: Unknown exception");
		SetLastErrorMessage("Unknown exception in EnumerateDevices");
		deviceCount = 0;
		return nullptr;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT CreateDevice(LPCSTR guidInstance) {
	try {
		HRESULT hr = E_FAIL;

		// Check if Unity is initialized
		if (!g_UnityInitialized) {
			LogMessage("CreateDevice: Unity not initialized");
			SetLastErrorMessage("Unity not initialized");
			return DIERR_UNITYNOTINITIALIZED;
		}

		// Check if DirectInput is initialized
		if (_DirectInput == nullptr) {
			LogMessage("CreateDevice: DirectInput not initialized");
			SetLastErrorMessage("DirectInput not initialized");
			return DIERR_NOTINITIALIZED;
		}

		// Check if guidInstance is valid
		if (!guidInstance) {
			LogMessage("CreateDevice: Invalid GUID (null)");
			SetLastErrorMessage("Invalid GUID (null)");
			return E_INVALIDARG;
		}

		// Clear existing device if present
		DestroyDeviceIfExists(guidInstance);

		std::string GUIDString(guidInstance);
		LogMessage("CreateDevice: Creating device with GUID: %s", GUIDString.c_str());

		LPDIRECTINPUTDEVICE8 DIDevice = nullptr;
		hr = _DirectInput->CreateDevice(LPCSTRGUIDtoGUID(guidInstance), &DIDevice, NULL);
		if (FAILED(hr)) {
			LogMessage("CreateDevice: Failed to create device: 0x%08X", hr);
			SetLastErrorMessage("Failed to create device");
			return hr;
		}

		hr = DIDevice->SetDataFormat(&c_dfDIJoystick2);
		if (FAILED(hr)) {
			LogMessage("CreateDevice: Failed to set data format: 0x%08X", hr);
			DIDevice->Release();
			SetLastErrorMessage("Failed to set data format");
			return hr;
		}

		// Get a valid window handle with fallback mechanism
		HWND hWnd = GetForegroundWindow();
		if (!hWnd) {
			LogMessage("CreateDevice: No foreground window, using desktop window");
			hWnd = GetDesktopWindow(); // Fallback to desktop window

			if (!hWnd) {
				LogMessage("CreateDevice: Failed to get any window handle");
				DIDevice->Release();
				SetLastErrorMessage("Failed to get window handle");
				return DIERR_NOWINDOWHANDLE;
			}
		}

		hr = DIDevice->SetCooperativeLevel(hWnd, DISCL_EXCLUSIVE | DISCL_BACKGROUND);
		if (FAILED(hr)) {
			LogMessage("CreateDevice: Failed to set cooperative level: 0x%08X", hr);
			DIDevice->Release();
			SetLastErrorMessage("Failed to set cooperative level");
			return hr;
		}

		hr = DIDevice->Acquire();
		if (FAILED(hr)) {
			LogMessage("CreateDevice: Failed to acquire device: 0x%08X", hr);
			DIDevice->Release();
			SetLastErrorMessage("Failed to acquire device");
			return hr;
		}

		_ActiveDevices[GUIDString] = DIDevice;
		LogMessage("CreateDevice: Device created and acquired successfully");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("CreateDevice: Exception: %s", e.what());
		SetLastErrorMessage(e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("CreateDevice: Unknown exception");
		SetLastErrorMessage("Unknown exception in CreateDevice");
		return E_FAIL;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT DestroyDevice(LPCSTR guidInstance) {
	try {
		if (!guidInstance) {
			LogMessage("DestroyDevice: Invalid GUID (null)");
			SetLastErrorMessage("Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		LogMessage("DestroyDevice: Destroying device with GUID: %s", GUIDString.c_str());

		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("DestroyDevice: Device not found");
			SetLastErrorMessage("Device not found");
			return E_FAIL;
		}

		HRESULT hr = StopAllFFBEffects(guidInstance);
		if (FAILED(hr)) {
			LogMessage("DestroyDevice: Failed to stop FFB effects: 0x%08X", hr);
			// Continue anyway
		}

		LPDIRECTINPUTDEVICE8 device = _ActiveDevices[GUIDString];
		hr = device->Unacquire();
		if (FAILED(hr)) {
			LogMessage("DestroyDevice: Failed to unacquire device: 0x%08X", hr);
			// Continue anyway
		}

		device->Release();
		_ActiveDevices.erase(GUIDString);
		LogMessage("DestroyDevice: Device destroyed successfully");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("DestroyDevice: Exception: %s", e.what());
		SetLastErrorMessage(e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("DestroyDevice: Unknown exception");
		SetLastErrorMessage("Unknown exception in DestroyDevice");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT GetDeviceState(LPCSTR guidInstance, FlatJoyState2& deviceState) {
	try {
		if (!guidInstance) {
			LogMessage("GetDeviceState: Invalid GUID (null)");
			SetLastErrorMessage("Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("GetDeviceState: Device not found");
			SetLastErrorMessage("Device not found");
			return E_FAIL;
		}

		DIJOYSTATE2 DeviceStateRaw = {};
		HRESULT hr = _ActiveDevices[GUIDString]->GetDeviceState(sizeof(DIJOYSTATE2), &DeviceStateRaw);

		if (hr == DIERR_INPUTLOST || hr == DIERR_NOTACQUIRED) {
			LogMessage("GetDeviceState: Device input lost, reacquiring");
			hr = _ActiveDevices[GUIDString]->Acquire();
			if (FAILED(hr)) {
				LogMessage("GetDeviceState: Failed to reacquire device: 0x%08X", hr);
				SetLastErrorMessage("Failed to reacquire device");
				return hr;
			}
			hr = _ActiveDevices[GUIDString]->GetDeviceState(sizeof(DIJOYSTATE2), &DeviceStateRaw);
		}

		if (FAILED(hr)) {
			LogMessage("GetDeviceState: Failed to get device state: 0x%08X", hr);
			SetLastErrorMessage("Failed to get device state");
			return hr;
		}

		deviceState = FlattenDIJOYSTATE2(DeviceStateRaw);
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("GetDeviceState: Exception: %s", e.what());
		SetLastErrorMessage(e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("GetDeviceState: Unknown exception");
		SetLastErrorMessage("Unknown exception in GetDeviceState");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT GetDeviceStateRaw(LPCSTR guidInstance, DIJOYSTATE2& deviceState) {
	try {
		if (!guidInstance) {
			LogMessage("GetDeviceStateRaw: Invalid GUID (null)");
			SetLastErrorMessage("Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("GetDeviceStateRaw: Device not found");
			SetLastErrorMessage("Device not found");
			return E_FAIL;
		}

		HRESULT hr = _ActiveDevices[GUIDString]->GetDeviceState(sizeof(DIJOYSTATE2), &deviceState);

		if (hr == DIERR_INPUTLOST || hr == DIERR_NOTACQUIRED) {
			LogMessage("GetDeviceStateRaw: Device input lost, reacquiring");
			hr = _ActiveDevices[GUIDString]->Acquire();
			if (FAILED(hr)) {
				LogMessage("GetDeviceStateRaw: Failed to reacquire device: 0x%08X", hr);
				SetLastErrorMessage("Failed to reacquire device");
				return hr;
			}
			hr = _ActiveDevices[GUIDString]->GetDeviceState(sizeof(DIJOYSTATE2), &deviceState);
		}

		if (FAILED(hr)) {
			LogMessage("GetDeviceStateRaw: Failed to get device state: 0x%08X", hr);
			SetLastErrorMessage("Failed to get device state");
			return hr;
		}

		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("GetDeviceStateRaw: Exception: %s", e.what());
		SetLastErrorMessage(e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("GetDeviceStateRaw: Unknown exception");
		SetLastErrorMessage("Unknown exception in GetDeviceStateRaw");
		return E_FAIL;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT GetDeviceCapabilities(LPCSTR guidInstance, DIDEVCAPS& deviceCapabilitiesOut) {
	try {
		if (!guidInstance) {
			LogMessage("GetDeviceCapabilities: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("GetDeviceCapabilities: Device not found");
			return E_FAIL;
		}

		DIDEVCAPS DeviceCapabilities = {};
		DeviceCapabilities.dwSize = sizeof(DIDEVCAPS);
		HRESULT hr = _ActiveDevices[GUIDString]->GetCapabilities(&DeviceCapabilities);

		if (FAILED(hr)) {
			LogMessage("GetDeviceCapabilities: Failed to get capabilities: 0x%08X", hr);
			return hr;
		}

		deviceCapabilitiesOut = DeviceCapabilities;
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("GetDeviceCapabilities: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("GetDeviceCapabilities: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT SetAutocenter(LPCSTR guidInstance, bool AutocenterState) {
	try {
		if (!guidInstance) {
			LogMessage("SetAutocenter: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("SetAutocenter: Device not found");
			return E_FAIL;
		}

		DIPROPDWORD DIPropAutoCenter = {};
		DIPropAutoCenter.diph.dwSize = sizeof(DIPropAutoCenter);
		DIPropAutoCenter.diph.dwHeaderSize = sizeof(DIPROPHEADER);
		DIPropAutoCenter.diph.dwObj = 0;
		DIPropAutoCenter.diph.dwHow = DIPH_DEVICE;
		DIPropAutoCenter.dwData = AutocenterState ? DIPROPAUTOCENTER_ON : DIPROPAUTOCENTER_OFF;

		HRESULT hr = _ActiveDevices[GUIDString]->SetProperty(DIPROP_AUTOCENTER, &DIPropAutoCenter.diph);
		if (FAILED(hr)) {
			LogMessage("SetAutocenter: Failed to set autocenter: 0x%08X", hr);
			return hr;
		}

		LogMessage("SetAutocenter: Autocenter set to %s", AutocenterState ? "ON" : "OFF");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("SetAutocenter: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("SetAutocenter: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT GetActiveDevices(int* count, const char*** outStrings) {
	try {
		*count = 0;
		*outStrings = nullptr;

		if (_ActiveDevices.empty()) {
			LogMessage("GetActiveDevices: No active devices");
			return S_OK;
		}

		std::vector<std::wstring> deviceData;
		for (const auto& [GUIDString, Device] : _ActiveDevices) {
			deviceData.push_back(string_to_wstring(GUIDString));
		}

		*count = static_cast<int>(deviceData.size());
		const char** result = new const char* [*count];

		for (int i = 0; i < *count; ++i) {
			std::string utf8Str = wstring_to_string(deviceData[i]);
			result[i] = _strdup(utf8Str.c_str());
			if (!result[i]) {
				for (int j = 0; j < i; ++j)
					free((void*)result[j]);
				delete[] result;
				LogMessage("GetActiveDevices: Memory allocation failed");
				return E_OUTOFMEMORY;
			}
		}

		*outStrings = result;
		LogMessage("GetActiveDevices: Found %d active devices", *count);
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("GetActiveDevices: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("GetActiveDevices: Unknown exception");
		return E_FAIL;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT EnumerateFFBEffects(LPCSTR guidInstance, int* count, const char*** outStrings) {
	try {
		*count = 0;
		*outStrings = nullptr;

		if (!guidInstance) {
			LogMessage("EnumerateFFBEffects: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("EnumerateFFBEffects: Device not found");
			return E_FAIL;
		}

		_DeviceEnumeratedEffects[GUIDString].clear();
		HRESULT hr = _ActiveDevices[GUIDString]->EnumEffects(&_EnumFFBEffectsCallback, &GUIDString, DIEFT_ALL);
		if (FAILED(hr)) {
			LogMessage("EnumerateFFBEffects: Failed to enumerate effects: 0x%08X", hr);
			return hr;
		}

		std::vector<std::wstring> effectData;
		for (const auto& Effect : _DeviceEnumeratedEffects[GUIDString]) {
			effectData.push_back(Effect.tszName);
		}

		*count = static_cast<int>(effectData.size());
		if (*count == 0) {
			LogMessage("EnumerateFFBEffects: No effects found");
			return S_OK;
		}

		const char** result = new const char* [*count];
		for (int i = 0; i < *count; ++i) {
			std::string utf8Str = wstring_to_string(effectData[i]);
			result[i] = _strdup(utf8Str.c_str());
			if (!result[i]) {
				for (int j = 0; j < i; ++j)
					free((void*)result[j]);
				delete[] result;
				LogMessage("EnumerateFFBEffects: Memory allocation failed");
				return E_OUTOFMEMORY;
			}
		}

		*outStrings = result;
		LogMessage("EnumerateFFBEffects: Found %d effects", *count);
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("EnumerateFFBEffects: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("EnumerateFFBEffects: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT EnumerateFFBAxes(LPCSTR guidInstance, int* count, const char*** outStrings) {
	try {
		*count = 0;
		*outStrings = nullptr;

		if (!guidInstance) {
			LogMessage("EnumerateFFBAxes: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("EnumerateFFBAxes: Device not found");
			return E_FAIL;
		}

		_DeviceFFBAxes[GUIDString].clear();
		HRESULT hr = _ActiveDevices[GUIDString]->EnumObjects(&_EnumFFBAxisCallback, &GUIDString, DIEFT_ALL);
		if (FAILED(hr)) {
			LogMessage("EnumerateFFBAxes: Failed to enumerate axes: 0x%08X", hr);
			return hr;
		}

		std::vector<std::wstring> axesData;
		axesData.push_back(L"FFB Axes: " + std::to_wstring(_DeviceFFBAxes[GUIDString].size()));
		for (const auto& ObjectInst : _DeviceFFBAxes[GUIDString]) {
			wchar_t szGUID[64] = { 0 };
			(void)StringFromGUID2(ObjectInst.guidType, szGUID, 64);
			std::wstring guidType(szGUID);
			axesData.push_back(ObjectInst.tszName);
			axesData.push_back(L"dwSize: " + std::to_wstring(ObjectInst.dwSize));
			axesData.push_back(L"guidType: " + guidType);
		}

		*count = static_cast<int>(axesData.size());
		if (*count == 0) {
			LogMessage("EnumerateFFBAxes: No axes found");
			return S_OK;
		}

		const char** result = new const char* [*count];
		for (int i = 0; i < *count; ++i) {
			std::string utf8Str = wstring_to_string(axesData[i]);
			result[i] = _strdup(utf8Str.c_str());
			if (!result[i]) {
				for (int j = 0; j < i; ++j)
					free((void*)result[j]);
				delete[] result;
				LogMessage("EnumerateFFBAxes: Memory allocation failed");
				return E_OUTOFMEMORY;
			}
		}

		*outStrings = result;
		LogMessage("EnumerateFFBAxes: Found %d axes entries", *count);
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("EnumerateFFBAxes: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("EnumerateFFBAxes: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API void FreeStringArray(const char** strings, int count) {
	if (strings) {
		for (int i = 0; i < count; ++i) {
			if (strings[i]) {
				free((void*)strings[i]);
			}
		}
		delete[] strings;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT CreateFFBEffect(LPCSTR guidInstance, Effects::Type effectType) {
	try {
		if (!guidInstance) {
			LogMessage("CreateFFBEffect: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("CreateFFBEffect: Device not found");
			return E_FAIL;
		}

		if (_DeviceFFBEffectControl[GUIDString].contains(effectType)) {
			LogMessage("CreateFFBEffect: Effect already exists");
			return E_ABORT;
		}

		// Ensure FFBAxes are enumerated
		if (!_DeviceFFBAxes.contains(GUIDString)) {
			_DeviceFFBAxes[GUIDString].clear();
			HRESULT hr = _ActiveDevices[GUIDString]->EnumObjects(&_EnumFFBAxisCallback, &GUIDString, DIEFT_ALL);
			if (FAILED(hr)) {
				LogMessage("CreateFFBEffect: Failed to enumerate FFB axes: 0x%08X", hr);
				return hr;
			}
		}

		int FFBAxesCount = static_cast<int>(_DeviceFFBAxes[GUIDString].size());
		if (FFBAxesCount == 0) {
			LogMessage("CreateFFBEffect: No FFB axes found");
			return E_FAIL;
		}

		DWORD* FFBAxes = new DWORD[FFBAxesCount];
		LONG* FFBDirections = new LONG[FFBAxesCount];

		for (int idx = 0; idx < FFBAxesCount; idx++) {
			FFBAxes[idx] = AxisTypeToDIJOFS(_DeviceFFBAxes[GUIDString][idx].guidType);
			FFBDirections[idx] = 0;
		}

		// Prepare effect parameters based on type
		DICONSTANTFORCE* constantForce = nullptr;
		DICONDITION* conditions = nullptr;
		DIRAMPFORCE* rampForce = nullptr;
		DIPERIODIC* periodicForce = nullptr;
		DICUSTOMFORCE* customForce = nullptr;
		LPDIRECTINPUTEFFECT effectControl = nullptr;

		DIEFFECT effect = {};
		effect.dwSize = sizeof(DIEFFECT);
		effect.dwFlags = DIEFF_CARTESIAN | DIEFF_OBJECTOFFSETS;
		effect.dwDuration = INFINITE;
		effect.dwSamplePeriod = 0;
		effect.dwGain = DI_FFNOMINALMAX;
		effect.dwTriggerButton = DIEB_NOTRIGGER;
		effect.dwTriggerRepeatInterval = 0;
		effect.cAxes = FFBAxesCount;
		effect.rgdwAxes = FFBAxes;
		effect.rglDirection = FFBDirections;
		effect.lpEnvelope = nullptr;
		effect.dwStartDelay = 0;

		LONG* forceData = nullptr;
		HRESULT hr = E_FAIL;

		// Get device capabilities for sample period
		DIDEVCAPS deviceCaps = {};
		deviceCaps.dwSize = sizeof(DIDEVCAPS);

		switch (effectType) {
		case Effects::Type::ConstantForce:
			constantForce = new DICONSTANTFORCE();
			constantForce->lMagnitude = 0;
			effect.cbTypeSpecificParams = sizeof(DICONSTANTFORCE);
			effect.lpvTypeSpecificParams = constantForce;
			LogMessage("CreateFFBEffect: Creating Constant Force effect");
			break;

		case Effects::Type::Spring:
		case Effects::Type::Damper:
		case Effects::Type::Friction:
		case Effects::Type::Inertia:
			conditions = new DICONDITION[FFBAxesCount];
			ZeroMemory(conditions, sizeof(DICONDITION) * FFBAxesCount);
			effect.cbTypeSpecificParams = sizeof(DICONDITION) * FFBAxesCount;
			effect.lpvTypeSpecificParams = conditions;
			LogMessage("CreateFFBEffect: Creating Condition effect (type %d)", static_cast<int>(effectType));
			break;

		case Effects::Type::Sine:
		case Effects::Type::Square:
		case Effects::Type::Triangle:
		case Effects::Type::SawtoothUp:
		case Effects::Type::SawtoothDown:
			periodicForce = new DIPERIODIC();
			ZeroMemory(periodicForce, sizeof(DIPERIODIC));
			periodicForce->dwMagnitude = 0;
			periodicForce->lOffset = 0;
			periodicForce->dwPhase = 0;
			periodicForce->dwPeriod = 30000;
			effect.cbTypeSpecificParams = sizeof(DIPERIODIC);
			effect.lpvTypeSpecificParams = periodicForce;
			LogMessage("CreateFFBEffect: Creating Periodic effect (type %d)", static_cast<int>(effectType));
			break;

		case Effects::Type::RampForce:
			rampForce = new DIRAMPFORCE();
			ZeroMemory(rampForce, sizeof(DIRAMPFORCE));
			rampForce->lStart = 0;
			rampForce->lEnd = 0;
			effect.cbTypeSpecificParams = sizeof(DIRAMPFORCE);
			effect.lpvTypeSpecificParams = rampForce;
			LogMessage("CreateFFBEffect: Creating Ramp Force effect");
			break;

		case Effects::Type::CustomForce:
			customForce = new DICUSTOMFORCE();
			hr = _ActiveDevices[GUIDString]->GetCapabilities(&deviceCaps);
			if (FAILED(hr)) {
				LogMessage("CreateFFBEffect: Failed to get device capabilities: 0x%08X", hr);
				delete[] FFBAxes;
				delete[] FFBDirections;
				delete customForce;
				return hr;
			}

			ZeroMemory(customForce, sizeof(DICUSTOMFORCE));
			customForce->cChannels = FFBAxesCount;
			customForce->dwSamplePeriod = deviceCaps.dwFFSamplePeriod;
			customForce->cSamples = 2;

			forceData = new LONG[customForce->cSamples];
			forceData[0] = 0;
			forceData[1] = 5000;
			customForce->rglForceData = forceData;

			effect.cbTypeSpecificParams = sizeof(DICUSTOMFORCE);
			effect.lpvTypeSpecificParams = customForce;
			effect.dwSamplePeriod = customForce->dwSamplePeriod;
			LogMessage("CreateFFBEffect: Creating Custom Force effect");
			break;

		default:
			LogMessage("CreateFFBEffect: Unsupported effect type: %d", static_cast<int>(effectType));
			delete[] FFBAxes;
			delete[] FFBDirections;
			return E_INVALIDARG;
		}

		// Create the effect
		hr = _ActiveDevices[GUIDString]->CreateEffect(
			EffectTypeToGUID(effectType),
			&effect,
			&effectControl,
			nullptr
		);

		if (FAILED(hr)) {
			LogMessage("CreateFFBEffect: Failed to create effect: 0x%08X", hr);

			// Clean up resources
			delete[] FFBAxes;
			delete[] FFBDirections;

			if (constantForce) delete constantForce;
			if (conditions) delete[] conditions;
			if (rampForce) delete rampForce;
			if (periodicForce) delete periodicForce;
			if (customForce) {
				if (customForce->rglForceData) delete[] customForce->rglForceData;
				delete customForce;
			}

			return hr;
		}

		// Start the effect
		hr = effectControl->Start(1, 0);
		if (FAILED(hr)) {
			LogMessage("CreateFFBEffect: Failed to start effect: 0x%08X", hr);
			effectControl->Release();

			// Clean up resources
			delete[] FFBAxes;
			delete[] FFBDirections;

			if (constantForce) delete constantForce;
			if (conditions) delete[] conditions;
			if (rampForce) delete rampForce;
			if (periodicForce) delete periodicForce;
			if (customForce) {
				if (customForce->rglForceData) delete[] customForce->rglForceData;
				delete customForce;
			}

			return hr;
		}

		// Store the effect configuration and control
		_DeviceFFBEffectConfig[GUIDString][effectType] = effect;
		_DeviceFFBEffectControl[GUIDString][effectType] = effectControl;

		LogMessage("CreateFFBEffect: Effect created and started successfully");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("CreateFFBEffect: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("CreateFFBEffect: Unknown exception");
		return E_FAIL;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT DestroyFFBEffect(LPCSTR guidInstance, Effects::Type effectType) {
	try {
		if (!guidInstance) {
			LogMessage("DestroyFFBEffect: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("DestroyFFBEffect: Device not found");
			return E_HANDLE;
		}

		if (!_DeviceFFBEffectControl[GUIDString].contains(effectType)) {
			LogMessage("DestroyFFBEffect: Effect not found (already destroyed)");
			return S_OK;
		}

		LPDIRECTINPUTEFFECT diEffect = _DeviceFFBEffectControl[GUIDString][effectType];
		if (!diEffect) {
			LogMessage("DestroyFFBEffect: Effect control is null");
			_DeviceFFBEffectControl[GUIDString].erase(effectType);
			_DeviceFFBEffectConfig[GUIDString].erase(effectType);
			return E_POINTER;
		}

		// Stop the effect
		HRESULT hr = diEffect->Stop();
		if (FAILED(hr)) {
			LogMessage("DestroyFFBEffect: Failed to stop effect: 0x%08X", hr);
			// Continue anyway
		}

		// Unload the effect
		hr = diEffect->Unload();
		if (FAILED(hr)) {
			LogMessage("DestroyFFBEffect: Failed to unload effect: 0x%08X", hr);
			// Continue anyway
		}

		// Release the effect
		ULONG refCount = diEffect->Release();
		if (refCount > 0) {
			LogMessage("DestroyFFBEffect: Warning: Effect released but refCount = %lu", refCount);
		}

		// Clean up maps
		_DeviceFFBEffectControl[GUIDString].erase(effectType);
		_DeviceFFBEffectConfig[GUIDString].erase(effectType);

		LogMessage("DestroyFFBEffect: Effect destroyed successfully");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("DestroyFFBEffect: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("DestroyFFBEffect: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT UpdateFFBEffect(LPCSTR guidInstance, Effects::Type effectType, DICONDITION* conditions) {
	try {
		if (!guidInstance || !conditions) {
			LogMessage("UpdateFFBEffect: Invalid parameters");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("UpdateFFBEffect: Device not found");
			return E_FAIL;
		}

		if (!_DeviceFFBEffectControl[GUIDString].contains(effectType)) {
			LogMessage("UpdateFFBEffect: Effect not found");
			return E_ABORT;
		}

		auto& effectConfig = _DeviceFFBEffectConfig[GUIDString][effectType];

		for (DWORD idx = 0; idx < effectConfig.cAxes; idx++) {
			switch (effectType) {
			case Effects::Type::ConstantForce: {
				auto* cf = static_cast<DICONSTANTFORCE*>(effectConfig.lpvTypeSpecificParams);
				if (!cf) {
					LogMessage("UpdateFFBEffect: Invalid constant force parameters");
					return E_POINTER;
				}
				cf->lMagnitude = conditions[idx].lPositiveCoefficient;
				break;
			}
			case Effects::Type::Sine:
			case Effects::Type::Square:
			case Effects::Type::Triangle:
			case Effects::Type::SawtoothUp:
			case Effects::Type::SawtoothDown: {
				auto* pe = static_cast<DIPERIODIC*>(effectConfig.lpvTypeSpecificParams);
				if (!pe) {
					LogMessage("UpdateFFBEffect: Invalid periodic parameters");
					return E_POINTER;
				}
				pe->dwMagnitude = conditions[idx].lPositiveCoefficient;
				pe->lOffset = conditions[idx].lOffset;
				pe->dwPeriod = conditions[idx].dwPositiveSaturation;
				break;
			}
			case Effects::Type::RampForce: {
				auto* rf = static_cast<DIRAMPFORCE*>(effectConfig.lpvTypeSpecificParams);
				if (!rf) {
					LogMessage("UpdateFFBEffect: Invalid ramp force parameters");
					return E_POINTER;
				}
				rf->lStart = conditions[idx].lPositiveCoefficient;
				rf->lEnd = conditions[idx].lNegativeCoefficient;
				break;
			}
			default: {
				auto* cond = static_cast<DICONDITION*>(effectConfig.lpvTypeSpecificParams);
				if (!cond) {
					LogMessage("UpdateFFBEffect: Invalid condition parameters");
					return E_POINTER;
				}
				cond[idx].lOffset = conditions[idx].lOffset;
				cond[idx].lPositiveCoefficient = conditions[idx].lPositiveCoefficient;
				cond[idx].lNegativeCoefficient = conditions[idx].lNegativeCoefficient;
				cond[idx].dwPositiveSaturation = conditions[idx].dwPositiveSaturation;
				cond[idx].dwNegativeSaturation = conditions[idx].dwNegativeSaturation;
				cond[idx].lDeadBand = conditions[idx].lDeadBand;
				break;
			}
			}
		}

		// Update the effect parameters
		HRESULT hr = _DeviceFFBEffectControl[GUIDString][effectType]->SetParameters(
			&effectConfig, DIEP_TYPESPECIFICPARAMS
		);

		if (FAILED(hr)) {
			LogMessage("UpdateFFBEffect: Failed to update effect parameters: 0x%08X", hr);
			return hr;
		}

		LogMessage("UpdateFFBEffect: Effect updated successfully");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("UpdateFFBEffect: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("UpdateFFBEffect: Unknown exception");
		return E_FAIL;
	}
}
DIRECTINPUTFORCEFEEDBACK_API HRESULT StopAllFFBEffects(LPCSTR guidInstance) {
	try {
		if (!guidInstance) {
			LogMessage("StopAllFFBEffects: Invalid GUID (null)");
			return E_INVALIDARG;
		}

		std::string GUIDString(guidInstance);
		if (!_ActiveDevices.contains(GUIDString)) {
			LogMessage("StopAllFFBEffects: Device not found");
			return E_FAIL;
		}

		HRESULT hr = S_OK;

		// Explicitly destroy each supported effect type
		DestroyFFBEffect(guidInstance, Effects::Type::ConstantForce);
		DestroyFFBEffect(guidInstance, Effects::Type::RampForce);
		DestroyFFBEffect(guidInstance, Effects::Type::Square);
		DestroyFFBEffect(guidInstance, Effects::Type::Sine);
		DestroyFFBEffect(guidInstance, Effects::Type::Triangle);
		DestroyFFBEffect(guidInstance, Effects::Type::SawtoothUp);
		DestroyFFBEffect(guidInstance, Effects::Type::SawtoothDown);
		DestroyFFBEffect(guidInstance, Effects::Type::Spring);
		DestroyFFBEffect(guidInstance, Effects::Type::Damper);
		DestroyFFBEffect(guidInstance, Effects::Type::Inertia);
		DestroyFFBEffect(guidInstance, Effects::Type::Friction);
		DestroyFFBEffect(guidInstance, Effects::Type::CustomForce);

		LogMessage("StopAllFFBEffects: All effects stopped");
		return S_OK;
	}
	catch (const std::exception& e) {
		LogMessage("StopAllFFBEffects: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("StopAllFFBEffects: Unknown exception");
		return E_FAIL;
	}
}

DIRECTINPUTFORCEFEEDBACK_API void SetDeviceChangeCallback(DeviceChangeCallback CB) {
	g_deviceCallback = CB;
	LogMessage("SetDeviceChangeCallback: Callback set");
}

// State query functions
DIRECTINPUTFORCEFEEDBACK_API bool IsUnityInitialized() {
	return g_UnityInitialized;
}

DIRECTINPUTFORCEFEEDBACK_API bool IsDirectInputInitialized() {
	return _DirectInput != nullptr;
}

DIRECTINPUTFORCEFEEDBACK_API HRESULT GetDILastError(char* buffer, int bufferSize) {
	if (!buffer || bufferSize <= 0) {
		return E_INVALIDARG;
	}

	if (g_LastErrorMessage.empty()) {
		strncpy_s(buffer, bufferSize, "No error", _TRUNCATE);
	}
	else {
		strncpy_s(buffer, bufferSize, g_LastErrorMessage.c_str(), _TRUNCATE);
	}

	return S_OK;
}

// Generate SAFEARRAY of DEBUG data with comprehensive FFB information
DIRECTINPUTFORCEFEEDBACK_API HRESULT DEBUG1(LPCSTR guidInstance, /*[out]*/ SAFEARRAY** DebugData) {
	try {
		HRESULT hr = E_FAIL;
		std::vector<std::wstring> debugInfo;

		// Header and timestamp
		debugInfo.push_back(L"=== DirectInput Force Feedback Debug Information ===");
		SYSTEMTIME st;
		GetLocalTime(&st);
		wchar_t timeStr[100];
		swprintf_s(timeStr, L"Timestamp: %04d-%02d-%02d %02d:%02d:%02d.%03d",
			st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
		debugInfo.push_back(timeStr);

		// Unity and DirectInput status
		debugInfo.push_back(L"Unity Status: " + std::wstring(g_UnityInitialized ? L"Initialized" : L"Not Initialized"));
		debugInfo.push_back(L"DirectInput Status: " + std::wstring(_DirectInput ? L"Initialized" : L"Not Initialized"));

		if (!guidInstance) {
			debugInfo.push_back(L"ERROR: Invalid GUID (null)");
			hr = BuildSafeArray(debugInfo, DebugData);
			return hr;
		}

		std::string GUIDString(guidInstance);
		debugInfo.push_back(L"Device GUID: " + string_to_wstring(GUIDString));

		// Check if the device is active
		if (!_ActiveDevices.contains(GUIDString)) {
			debugInfo.push_back(L"ERROR: Device not attached or not active");
			hr = BuildSafeArray(debugInfo, DebugData);
			return hr;
		}

		// Get device capabilities
		DIDEVCAPS deviceCaps = {};
		deviceCaps.dwSize = sizeof(DIDEVCAPS);
		hr = _ActiveDevices[GUIDString]->GetCapabilities(&deviceCaps);
		if (SUCCEEDED(hr)) {
			debugInfo.push_back(L"Device Capabilities:");
			debugInfo.push_back(L"  - Type: 0x" + std::to_wstring(deviceCaps.dwDevType));
			debugInfo.push_back(L"  - Axes: " + std::to_wstring(deviceCaps.dwAxes));
			debugInfo.push_back(L"  - Buttons: " + std::to_wstring(deviceCaps.dwButtons));
			debugInfo.push_back(L"  - POVs: " + std::to_wstring(deviceCaps.dwPOVs));
			debugInfo.push_back(L"  - FFB Sample Period: " + std::to_wstring(deviceCaps.dwFFSamplePeriod) + L" microseconds");
			debugInfo.push_back(L"  - FFB Min Time Resolution: " + std::to_wstring(deviceCaps.dwFFMinTimeResolution) + L" microseconds");
		}
		else {
			debugInfo.push_back(L"ERROR: Failed to get device capabilities - 0x" + std::to_wstring(hr));
		}

		// Enumerate FFB axes
		debugInfo.push_back(L"Force Feedback Axes:");
		if (!_DeviceFFBAxes.contains(GUIDString)) {
			_DeviceFFBAxes[GUIDString].clear();
			hr = _ActiveDevices[GUIDString]->EnumObjects(&_EnumFFBAxisCallback, &GUIDString, DIEFT_ALL);
		}

		if (_DeviceFFBAxes[GUIDString].empty()) {
			debugInfo.push_back(L"  - No FFB axes found");
		}
		else {
			debugInfo.push_back(L"  - Found " + std::to_wstring(_DeviceFFBAxes[GUIDString].size()) + L" FFB axes");
			for (const auto& axis : _DeviceFFBAxes[GUIDString]) {
				wchar_t guidStr[64] = { 0 };
				StringFromGUID2(axis.guidType, guidStr, 64);
				debugInfo.push_back(L"  - Axis: " + std::wstring(axis.tszName) + L" (GUID: " + guidStr + L")");
			}
		}

		// Check for active FFB effects
		debugInfo.push_back(L"Active Force Feedback Effects:");
		if (!_DeviceFFBEffectControl[GUIDString].empty()) {
			debugInfo.push_back(L"  - Found " + std::to_wstring(_DeviceFFBEffectControl[GUIDString].size()) + L" active effects");

			// Focus on Constant Force effect
			if (_DeviceFFBEffectControl[GUIDString].contains(Effects::Type::ConstantForce)) {
				debugInfo.push_back(L"  - Constant Force effect is active");
				auto& effectConfig = _DeviceFFBEffectConfig[GUIDString][Effects::Type::ConstantForce];

				debugInfo.push_back(L"    Effect Configuration:");
				debugInfo.push_back(L"    - Duration: " + (effectConfig.dwDuration == INFINITE ?
					std::wstring(L"INFINITE") : std::to_wstring(effectConfig.dwDuration)));
				debugInfo.push_back(L"    - Gain: " + std::to_wstring(effectConfig.dwGain));
				debugInfo.push_back(L"    - Sample Period: " + std::to_wstring(effectConfig.dwSamplePeriod));
				debugInfo.push_back(L"    - Axes Count: " + std::to_wstring(effectConfig.cAxes));

				// Extract and display constant force parameters
				auto* cf = static_cast<DICONSTANTFORCE*>(effectConfig.lpvTypeSpecificParams);
				if (cf) {
					debugInfo.push_back(L"    Constant Force Parameters:");
					debugInfo.push_back(L"    - Magnitude: " + std::to_wstring(cf->lMagnitude) +
						L" (" + std::to_wstring((cf->lMagnitude * 100) / DI_FFNOMINALMAX) + L"%)");
				}
				else {
					debugInfo.push_back(L"    ERROR: Invalid constant force parameters");
				}

				// Test modifying the constant force
				debugInfo.push_back(L"Testing Constant Force Modification:");
				try {
					// Save original magnitude
					LONG originalMagnitude = 0;
					if (cf) originalMagnitude = cf->lMagnitude;

					// Set to 50% force
					LONG testMagnitude = DI_FFNOMINALMAX / 2;
					debugInfo.push_back(L"  - Setting magnitude to 50%: " + std::to_wstring(testMagnitude));

					if (cf) {
						cf->lMagnitude = testMagnitude;
						hr = _DeviceFFBEffectControl[GUIDString][Effects::Type::ConstantForce]->SetParameters(
							&effectConfig, DIEP_TYPESPECIFICPARAMS);

						if (SUCCEEDED(hr)) {
							debugInfo.push_back(L"  - Successfully applied 50% force");
						}
						else {
							debugInfo.push_back(L"  - Failed to apply 50% force - 0x" + std::to_wstring(hr));
						}

						// Restore original magnitude
						cf->lMagnitude = originalMagnitude;
						hr = _DeviceFFBEffectControl[GUIDString][Effects::Type::ConstantForce]->SetParameters(
							&effectConfig, DIEP_TYPESPECIFICPARAMS);

						if (SUCCEEDED(hr)) {
							debugInfo.push_back(L"  - Successfully restored original force");
						}
						else {
							debugInfo.push_back(L"  - Failed to restore original force - 0x" + std::to_wstring(hr));
						}
					}
				}
				catch (...) {
					debugInfo.push_back(L"  - Exception occurred during force modification test");
				}
			}
			else {
				debugInfo.push_back(L"  - Constant Force effect is not active");
			}
		}
		else {
			debugInfo.push_back(L"  - No active effects found");
		}

		// Get device path information
		debugInfo.push_back(L"Device Path Information:");
		LPDIRECTINPUTDEVICE8 DIDevice = nullptr;
		if (SUCCEEDED(hr = _DirectInput->CreateDevice(LPCSTRGUIDtoGUID(guidInstance), &DIDevice, NULL))) {
			DIPROPGUIDANDPATH GUIDPath = {};
			GUIDPath.diph.dwSize = sizeof(DIPROPGUIDANDPATH);
			GUIDPath.diph.dwHeaderSize = sizeof(DIPROPHEADER);
			GUIDPath.diph.dwObj = 0;
			GUIDPath.diph.dwHow = static_cast<DWORD>(DIPH_DEVICE);

			if (SUCCEEDED(hr = DIDevice->GetProperty(DIPROP_GUIDANDPATH, &GUIDPath.diph))) {
				debugInfo.push_back(L"  - Path: " + std::wstring(GUIDPath.wszPath));

				// Check for Fanatec duplicate markers
				if (wcsstr(GUIDPath.wszPath, L"&col01") != 0) {
					debugInfo.push_back(L"  - This is a primary Fanatec device (contains &col01)");
				}
				else if (wcsstr(GUIDPath.wszPath, L"&col02") != 0) {
					debugInfo.push_back(L"  - This is a duplicate Fanatec device (contains &col02)");
				}
			}
			else {
				debugInfo.push_back(L"  - Failed to get device path - 0x" + std::to_wstring(hr));
			}

			DIDevice->Release();
		}
		else {
			debugInfo.push_back(L"  - Failed to create temporary device - 0x" + std::to_wstring(hr));
		}

		// Store the debug data
		DEBUGDATA.clear();
		for (const auto& line : debugInfo) {
			DEBUGDATA.push_back(line);
		}

		// Build and return the SAFEARRAY
		hr = BuildSafeArray(DEBUGDATA, DebugData);
		return hr;
	}
	catch (const std::exception& e) {
		LogMessage("DEBUG1: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("DEBUG1: Unknown exception");
		return E_FAIL;
	}
}
//////////////////////////////////////////////////////////////
// Callback Functions
//////////////////////////////////////////////////////////////

BOOL CALLBACK _EnumDevicesCallback(const DIDEVICEINSTANCE* DIDI, void* pContext) {
	try {
		DeviceInfo di = { 0 };
		di.deviceType = DIDI->dwDevType;
		std::string GIStr = GUID_to_string(DIDI->guidInstance);
		std::string GPStr = GUID_to_string(DIDI->guidProduct);
		std::string INStr = wstring_to_string(DIDI->tszInstanceName);
		std::string PNStr = wstring_to_string(DIDI->tszProductName);

		di.guidInstance = new char[GIStr.length() + 1];
		di.guidProduct = new char[GPStr.length() + 1];
		di.instanceName = new char[INStr.length() + 1];
		di.productName = new char[PNStr.length() + 1];
		strcpy_s(di.guidInstance, GIStr.length() + 1, GIStr.c_str());
		strcpy_s(di.guidProduct, GPStr.length() + 1, GPStr.c_str());
		strcpy_s(di.instanceName, INStr.length() + 1, INStr.c_str());
		strcpy_s(di.productName, PNStr.length() + 1, PNStr.c_str());
		di.FFBCapable = false; // Default to false; will be updated later.

		// Fanatec devices fix: skip duplicate HID if applicable.
		if (LOWORD(DIDI->guidProduct.Data1) == 0x0EB7) {
			if (IsDuplicateHID(DIDI)) {
				LogMessage("_EnumDevicesCallback: Skipping duplicate Fanatec device: %s", INStr.c_str());
				return DIENUM_CONTINUE;
			}
		}

		_DeviceInstances.push_back(di);
		LogMessage("_EnumDevicesCallback: Found device: %s", INStr.c_str());
		return DIENUM_CONTINUE;
	}
	catch (const std::exception& e) {
		LogMessage("_EnumDevicesCallback: Exception: %s", e.what());
		return DIENUM_STOP;
	}
	catch (...) {
		LogMessage("_EnumDevicesCallback: Unknown exception");
		return DIENUM_STOP;
	}
}

BOOL CALLBACK _EnumDevicesCallbackFFB(const DIDEVICEINSTANCE* DIDI, void* pContext) {
	try {
		std::string GUIDStr = GUID_to_string(DIDI->guidInstance);
		for (auto& di : _DeviceInstances) {
			if (di.guidInstance == GUIDStr) {
				di.FFBCapable = true;
				LogMessage("_EnumDevicesCallbackFFB: Device %s is FFB capable", di.instanceName);
			}
		}
		return DIENUM_CONTINUE;
	}
	catch (const std::exception& e) {
		LogMessage("_EnumDevicesCallbackFFB: Exception: %s", e.what());
		return DIENUM_STOP;
	}
	catch (...) {
		LogMessage("_EnumDevicesCallbackFFB: Unknown exception");
		return DIENUM_STOP;
	}
}

BOOL CALLBACK _EnumFFBEffectsCallback(LPCDIEFFECTINFO EffectInfo, LPVOID pvRef) {
	try {
		std::string GUIDString = *reinterpret_cast<std::string*>(pvRef);
		_DeviceEnumeratedEffects[GUIDString].push_back(*EffectInfo);
		LogMessage("_EnumFFBEffectsCallback: Found effect: %S", EffectInfo->tszName);
		return DIENUM_CONTINUE;
	}
	catch (const std::exception& e) {
		LogMessage("_EnumFFBEffectsCallback: Exception: %s", e.what());
		return DIENUM_STOP;
	}
	catch (...) {
		LogMessage("_EnumFFBEffectsCallback: Unknown exception");
		return DIENUM_STOP;
	}
}

BOOL CALLBACK _EnumFFBAxisCallback(const DIDEVICEOBJECTINSTANCE* ObjectInst, LPVOID pvRef) {
	try {
		std::string GUIDString = *reinterpret_cast<std::string*>(pvRef);
		if ((ObjectInst->dwFlags & DIDOI_FFACTUATOR) != 0) {
			_DeviceFFBAxes[GUIDString].push_back(*ObjectInst);
			LogMessage("_EnumFFBAxisCallback: Found FFB axis: %S", ObjectInst->tszName);
		}
		return DIENUM_CONTINUE;
	}
	catch (const std::exception& e) {
		LogMessage("_EnumFFBAxisCallback: Exception: %s", e.what());
		return DIENUM_STOP;
	}
	catch (...) {
		LogMessage("_EnumFFBAxisCallback: Unknown exception");
		return DIENUM_STOP;
	}
}

LRESULT _WindowsHookCallback(int code, WPARAM wParam, LPARAM lParam) {
	if (code < 0)
		return CallNextHookEx(NULL, code, wParam, lParam);

	try {
		PCWPSTRUCT pMsg = (PCWPSTRUCT)lParam;
		if (pMsg->message == WM_DEVICECHANGE) {
			LogMessage("_WindowsHookCallback: Device change detected, wParam: 0x%08X", pMsg->wParam);
			if (g_deviceCallback) {
				g_deviceCallback(static_cast<DBTEvents>(pMsg->wParam));
			}
		}
		return CallNextHookEx(NULL, code, wParam, lParam);
	}
	catch (...) {
		LogMessage("_WindowsHookCallback: Exception in hook callback");
		return CallNextHookEx(NULL, code, wParam, lParam);
	}
}

//////////////////////////////////////////////////////////////
// Helper Functions
//////////////////////////////////////////////////////////////

HRESULT BuildSafeArray(std::vector<std::wstring> sourceData, SAFEARRAY** SafeArrayData) {
	HRESULT hr = E_FAIL;
	try {
		const LONG dataEntries = static_cast<LONG>(sourceData.size());
		CComSafeArray<BSTR> safeArray(dataEntries);
		for (LONG i = 0; i < dataEntries; i++) {
			CComBSTR bstr = ToBstr(sourceData[i]);
			if (FAILED(hr = safeArray.SetAt(i, bstr.Detach(), FALSE))) {
				LogMessage("BuildSafeArray: Failed to set array element: 0x%08X", hr);
				AtlThrow(hr);
			}
		}
		*SafeArrayData = safeArray.Detach();
		return S_OK;
	}
	catch (const CAtlException& e) {
		LogMessage("BuildSafeArray: CAtlException: 0x%08X", static_cast<HRESULT>(e));
		return e;
	}
	catch (const std::exception& e) {
		LogMessage("BuildSafeArray: Exception: %s", e.what());
		return E_FAIL;
	}
	catch (...) {
		LogMessage("BuildSafeArray: Unknown exception");
		return E_FAIL;
	}
}

std::wstring string_to_wstring(const std::string& str) {
	if (str.empty()) return std::wstring();
	int size_needed = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.size(), NULL, 0);
	std::wstring wstrTo(size_needed, 0);
	MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.size(), &wstrTo[0], size_needed);
	return wstrTo;
}

std::string wstring_to_string(const std::wstring& wstr) {
	if (wstr.empty()) return std::string();
	int size_needed = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.size(), NULL, 0, NULL, NULL);
	std::string strTo(size_needed, 0);
	WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.size(), &strTo[0], size_needed, NULL, NULL);
	return strTo;
}

std::string GUID_to_string(GUID guidInstance) {
	OLECHAR* guidSTR;
	HRESULT hr = StringFromCLSID(guidInstance, &guidSTR);
	if (FAILED(hr)) {
		LogMessage("GUID_to_string: StringFromCLSID failed: 0x%08X", hr);
		return "";
	}

	std::string s = wstring_to_string(guidSTR);
	CoTaskMemFree(guidSTR); // Free memory allocated by StringFromCLSID
	return s;
}

GUID LPCSTRGUIDtoGUID(LPCSTR guidInstance) {
	GUID deviceGuid = GUID_NULL;
	if (!guidInstance) {
		LogMessage("LPCSTRGUIDtoGUID: Invalid GUID string (null)");
		return deviceGuid;
	}

	int wcharCount = MultiByteToWideChar(CP_UTF8, 0, guidInstance, -1, NULL, 0);
	if (wcharCount <= 0) {
		LogMessage("LPCSTRGUIDtoGUID: MultiByteToWideChar failed to get size");
		return deviceGuid;
	}

	WCHAR* wstrGuidInstance = new WCHAR[wcharCount];
	if (!wstrGuidInstance) {
		LogMessage("LPCSTRGUIDtoGUID: Memory allocation failed");
		return deviceGuid;
	}

	MultiByteToWideChar(CP_UTF8, 0, guidInstance, -1, wstrGuidInstance, wcharCount);
	HRESULT hr = CLSIDFromString(wstrGuidInstance, &deviceGuid);
	delete[] wstrGuidInstance;

	if (FAILED(hr)) {
		LogMessage("LPCSTRGUIDtoGUID: CLSIDFromString failed: 0x%08X", hr);
	}

	return deviceGuid;
}
FlatJoyState2 FlattenDIJOYSTATE2(DIJOYSTATE2 DeviceState) {
	FlatJoyState2 state = {};

	// Buttons (banks A and B)
	for (int i = 0; i < 64; i++) {
		if (DeviceState.rgbButtons[i] == 128)
			state.buttonsA |= (unsigned long long)1 << i;
	}
	for (int i = 64; i < 128; i++) {
		if (DeviceState.rgbButtons[i] == 128)
			state.buttonsB |= (unsigned long long)1 << (i - 64);
	}

	auto ClampToUInt16 = [](LONG value) -> uint16_t {
		return static_cast<uint16_t>(std::clamp(value, 0L, static_cast<LONG>(UINT16_MAX)));
		};

	state.lX = ClampToUInt16(DeviceState.lX);
	state.lY = ClampToUInt16(DeviceState.lY);
	state.lZ = ClampToUInt16(DeviceState.lZ);
	state.lU = ClampToUInt16(DeviceState.rglSlider[0]);
	state.lV = ClampToUInt16(DeviceState.rglSlider[1]);
	state.lRx = ClampToUInt16(DeviceState.lRx);
	state.lRy = ClampToUInt16(DeviceState.lRy);
	state.lRz = ClampToUInt16(DeviceState.lRz);
	state.lVX = ClampToUInt16(DeviceState.lVX);
	state.lVY = ClampToUInt16(DeviceState.lVY);
	state.lVZ = ClampToUInt16(DeviceState.lVZ);
	state.lVU = ClampToUInt16(DeviceState.rglVSlider[0]);
	state.lVV = ClampToUInt16(DeviceState.rglVSlider[1]);
	state.lVRx = ClampToUInt16(DeviceState.lVRx);
	state.lVRy = ClampToUInt16(DeviceState.lVRy);
	state.lVRz = ClampToUInt16(DeviceState.lVRz);
	state.lAX = ClampToUInt16(DeviceState.lAX);
	state.lAY = ClampToUInt16(DeviceState.lAY);
	state.lAZ = ClampToUInt16(DeviceState.lAZ);
	state.lAU = ClampToUInt16(DeviceState.rglASlider[0]);
	state.lAV = ClampToUInt16(DeviceState.rglASlider[1]);
	state.lARx = ClampToUInt16(DeviceState.lARx);
	state.lARy = ClampToUInt16(DeviceState.lARy);
	state.lARz = ClampToUInt16(DeviceState.lARz);
	state.lFX = ClampToUInt16(DeviceState.lFX);
	state.lFY = ClampToUInt16(DeviceState.lFY);
	state.lFZ = ClampToUInt16(DeviceState.lFZ);
	state.lFU = ClampToUInt16(DeviceState.rglFSlider[0]);
	state.lFV = ClampToUInt16(DeviceState.rglFSlider[1]);
	state.lFRx = ClampToUInt16(DeviceState.lFRx);
	state.lFRy = ClampToUInt16(DeviceState.lFRy);
	state.lFRz = ClampToUInt16(DeviceState.lFRz);

	// DPAD: Use consistent bit shifting pattern for the four directions
	for (int i = 0; i < 4; i++) {
		switch (DeviceState.rgdwPOV[i]) {
		case 0:
			state.rgdwPOV |= (BYTE)(1 << (i * 4 + 0));
			break;
		case 18000:
			state.rgdwPOV |= (BYTE)(1 << (i * 4 + 1));
			break;
		case 27000:
			state.rgdwPOV |= (BYTE)(1 << (i * 4 + 2));
			break;
		case 9000:
			state.rgdwPOV |= (BYTE)(1 << (i * 4 + 3));
			break;
		}
	}
	return state;
}

bool GUIDMatch(LPCSTR guidInstance, LPDIRECTINPUTDEVICE8 Device) {
	if (!guidInstance || !Device) {
		LogMessage("GUIDMatch: Invalid parameters");
		return false;
	}

	DIDEVICEINSTANCE deviceInfo = { sizeof(DIDEVICEINSTANCE) };
	HRESULT hr = Device->GetDeviceInfo(&deviceInfo);
	if (FAILED(hr)) {
		LogMessage("GUIDMatch: Failed to get device info: 0x%08X", hr);
		return false;
	}

	GUID instanceGuid = LPCSTRGUIDtoGUID(guidInstance);
	return (deviceInfo.guidInstance == instanceGuid);
}

GUID Device2GUID(LPDIRECTINPUTDEVICE8 Device) {
	GUID result = GUID_NULL;
	if (!Device) {
		LogMessage("Device2GUID: Invalid device (null)");
		return result;
	}

	DIDEVICEINSTANCE deviceInfo = { sizeof(DIDEVICEINSTANCE) };
	HRESULT hr = Device->GetDeviceInfo(&deviceInfo);
	if (FAILED(hr)) {
		LogMessage("Device2GUID: Failed to get device info: 0x%08X", hr);
		return result;
	}

	return deviceInfo.guidInstance;
}

inline CComBSTR ToBstr(const std::wstring& s) {
	if (s.empty()) {
		return CComBSTR();
	}
	return CComBSTR(static_cast<int>(s.size()), s.data());
}

void DestroyDeviceIfExists(LPCSTR guidInstance) {
	if (!guidInstance) {
		LogMessage("DestroyDeviceIfExists: Invalid GUID (null)");
		return;
	}

	std::string GUIDString(guidInstance);
	if (!_ActiveDevices.contains(GUIDString)) {
		return; // Device not found, nothing to destroy
	}

	LogMessage("DestroyDeviceIfExists: Destroying existing device: %s", GUIDString.c_str());
	DestroyDevice(guidInstance);
}

DWORD AxisTypeToDIJOFS(GUID axisType) {
	if (axisType == GUID_XAxis) {
		return DIJOFS_X;
	}
	else if (axisType == GUID_YAxis) {
		return DIJOFS_Y;
	}
	else if (axisType == GUID_ZAxis) {
		return DIJOFS_Z;
	}
	else if (axisType == GUID_RxAxis) {
		return DIJOFS_RX;
	}
	else if (axisType == GUID_RyAxis) {
		return DIJOFS_RY;
	}
	else if (axisType == GUID_RzAxis) {
		return DIJOFS_RZ;
	}

	// GUID type not found (likely POV Hat, Slider or Button)
	return 0;
}

GUID EffectTypeToGUID(Effects::Type effectType) {
	switch (effectType) {
	case Effects::Type::ConstantForce:
		return GUID_ConstantForce;
	case Effects::Type::RampForce:
		return GUID_RampForce;
	case Effects::Type::Square:
		return GUID_Square;
	case Effects::Type::Sine:
		return GUID_Sine;
	case Effects::Type::Triangle:
		return GUID_Triangle;
	case Effects::Type::SawtoothUp:
		return GUID_SawtoothUp;
	case Effects::Type::SawtoothDown:
		return GUID_SawtoothDown;
	case Effects::Type::Spring:
		return GUID_Spring;
	case Effects::Type::Damper:
		return GUID_Damper;
	case Effects::Type::Inertia:
		return GUID_Inertia;
	case Effects::Type::Friction:
		return GUID_Friction;
	case Effects::Type::CustomForce:
		return GUID_CustomForce;
	default:
		LogMessage("EffectTypeToGUID: Unknown effect type: %d", static_cast<int>(effectType));
		return GUID_Unknown;
	}
}

bool IsDuplicateHID(const DIDEVICEINSTANCE* DIDI) {
	if (!DIDI) {
		LogMessage("IsDuplicateHID: Invalid device instance (null)");
		return true;
	}

	HRESULT hr;
	LPDIRECTINPUTDEVICE8 DIDevice = nullptr;
	if (FAILED(hr = _DirectInput->CreateDevice(DIDI->guidInstance, &DIDevice, NULL))) {
		LogMessage("IsDuplicateHID: Failed to create device: 0x%08X", hr);
		return true; // If we cannot create the device, assume it is duplicate
	}

	DIPROPGUIDANDPATH GUIDPath = {};
	GUIDPath.diph.dwSize = sizeof(DIPROPGUIDANDPATH);
	GUIDPath.diph.dwHeaderSize = sizeof(DIPROPHEADER);
	GUIDPath.diph.dwObj = 0;
	GUIDPath.diph.dwHow = DIPH_DEVICE;

	if (FAILED(hr = DIDevice->GetProperty(DIPROP_GUIDANDPATH, &GUIDPath.diph))) {
		LogMessage("IsDuplicateHID: Failed to get device path: 0x%08X", hr);
		DIDevice->Release();
		return true;
	}

	DIDevice->Release();

	// If the HID path contains "&col02", consider this a duplicate device
	if (wcsstr(GUIDPath.wszPath, L"&col02") != 0) {
		LogMessage("IsDuplicateHID: Duplicate device detected (contains &col02)");
		return true;
	}

	return false;
}
