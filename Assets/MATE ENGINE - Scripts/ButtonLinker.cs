using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonLinker : MonoBehaviour
{
    [Tooltip("The button whose OnClick event will be invoked when this button is clicked.")]
    public Button targetButton;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(InvokeTargetButton);
    }

    private void InvokeTargetButton()
    {
        if (targetButton != null)
            targetButton.onClick.Invoke();
        else
            Debug.LogWarning("ButtonLinker: No target button assigned!", this);
    }
}
