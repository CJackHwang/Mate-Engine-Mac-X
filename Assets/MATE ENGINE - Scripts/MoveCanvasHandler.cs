using UnityEngine;

public class MoveCanvasHandler : MonoBehaviour
{
    [Tooltip("GameObject that enables UniWindow drag movement")]
    public GameObject moveCanvas;

    private void Update()
    {
        if (!moveCanvas) return;

        bool shouldBeActive = !AvatarSettingsMenu.IsMenuOpen && !TutorialMenu.IsActive;

        if (moveCanvas.activeSelf != shouldBeActive)
        {
            moveCanvas.SetActive(shouldBeActive);
        }
    }
}
