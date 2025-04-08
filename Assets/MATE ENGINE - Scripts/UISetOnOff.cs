using UnityEngine;
using System.Collections.Generic;

public class UISetOnOff : MonoBehaviour
{
    public GameObject target;

    // This shows up as a no-arg function in the OnClick list
    public void ToggleTarget()
    {
        if (target != null)
            target.SetActive(!target.activeSelf);
    }

    // Optional: for OnClick(GameObject) style binding
    public void SetOnOff(GameObject obj)
    {
        if (obj != null)
            obj.SetActive(!obj.activeSelf);
    }

    // === NEW METHODS FOR ACCESSOIRES ===

    // Toggle accessory state on all active AccessoiresHandlers by rule name
    public void ToggleAccessoryByName(string ruleName)
    {
        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            foreach (var rule in handler.rules)
            {
                if (rule.ruleName == ruleName)
                {
                    rule.isEnabled = !rule.isEnabled;
                    break;
                }
            }
        }
    }

    // Set accessory state explicitly on all active AccessoiresHandlers by rule name
    public void SetAccessoryState(string ruleName, bool state)
    {
        foreach (var handler in AccessoiresHandler.ActiveHandlers)
        {
            foreach (var rule in handler.rules)
            {
                if (rule.ruleName == ruleName)
                {
                    rule.isEnabled = state;
                    break;
                }
            }
        }
    }
}
