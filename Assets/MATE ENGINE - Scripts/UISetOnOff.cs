using UnityEngine;

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
}
