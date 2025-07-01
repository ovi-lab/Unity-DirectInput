using UnityEngine;
using System.Linq;

public class DIInputValueMonitor : MonoBehaviour
{
    private DIInputManager inputManager;
    [SerializeField] public string selectedMappingName;

    private void OnEnable()
    {
        inputManager = DIInputManager.Instance;
        if (inputManager == null)
        {
            Debug.LogError("DIInputManager instance not found!");
            return;
        }
    }

    float previousValue = 0;
    public void Update()
    {
        if (inputManager == null)
        {
            inputManager = DIInputManager.Instance;
            return;
        }

        var selectedMapping = inputManager.inputMappings
            .FirstOrDefault(m => m.mappingName == selectedMappingName);

        if (selectedMapping != null)
        {
            // If the value has changed, log it
            if (!Mathf.Approximately(previousValue, selectedMapping.currentValue))
            {
                string message = $"Input '{selectedMapping.mappingName}' value: {selectedMapping.currentValue} " +
                               $"Device: {selectedMapping.deviceName} Input: {selectedMapping.inputName}";
                Debug.Log(message);
            }

            // Store the previous value to detect changes
            previousValue = selectedMapping.currentValue;
        }
    }
}
