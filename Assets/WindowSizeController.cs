using UnityEngine;
using UnityEngine.UI;
using Kirurobo;

public class WindowSizeController : MonoBehaviour
{
    public Button cycleButton;

    private UniWindowController windowController;

    private enum WindowSizeState { Normal, Big, Small }
    private WindowSizeState currentState = WindowSizeState.Normal;

    void Start()
    {
        windowController = UniWindowController.current;

        if (cycleButton != null)
        {
            cycleButton.onClick.AddListener(CycleSize);
        }
    }

    void CycleSize()
    {
        if (windowController == null) return;

        switch (currentState)
        {
            case WindowSizeState.Normal:
                windowController.windowSize = new Vector2(2048, 1536); // Big
                currentState = WindowSizeState.Big;
                break;
            case WindowSizeState.Big:
                windowController.windowSize = new Vector2(768, 512);   // Small
                currentState = WindowSizeState.Small;
                break;
            case WindowSizeState.Small:
                windowController.windowSize = new Vector2(1536, 1024); // Normal
                currentState = WindowSizeState.Normal;
                break;
        }
    }
}
