using UnityEngine;

public class MoveCanvasHandler : MonoBehaviour
{
    [Tooltip("GameObject that enables UniWindow drag movement")]
    public GameObject moveCanvas;

    void Update()
    {
        if (!moveCanvas) return;

        bool shouldBeActive = !AvatarSettingsMenu.IsMenuOpen;

        if (moveCanvas.activeSelf != shouldBeActive)
        {
            moveCanvas.SetActive(shouldBeActive);
        }
    }
}
