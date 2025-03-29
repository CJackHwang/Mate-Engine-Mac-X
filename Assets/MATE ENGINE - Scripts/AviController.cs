using UnityEngine;
using System.Runtime.InteropServices;

public class AviController : MonoBehaviour
{
    public Animator animator;
    public AnimationClip idleAnimation;
    public AnimationClip sitAnimation;
    public AnimationClip dragAnimation;

    private bool isSnapped = false;
    private bool isDragging = false;
    private Vector2 screenSize;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(System.IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    void Start()
    {
        screenSize = new Vector2(Screen.width, Screen.height);

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        PlayIdle();
    }

    void Update()
    {
        if (isDragging)
        {
            PlayDrag();
        }
        else if (!isSnapped)
        {
            CheckForSnap();
        }
    }

    private void CheckForSnap()
    {
        Vector3 pos = transform.position;
        float snapThreshold = 50f;

        if (pos.y >= screenSize.y - snapThreshold)
        {
            SnapToTaskbar();
        }
        else
        {
            RECT windowRect;
            System.IntPtr hwnd = GetForegroundWindow();

            if (GetWindowRect(hwnd, out windowRect))
            {
                float windowTop = windowRect.top;
                if (Mathf.Abs(pos.y - windowTop) <= snapThreshold)
                {
                    SnapToWindow(windowTop);
                }
            }
        }
    }

    private void SnapToTaskbar()
    {
        transform.position = new Vector3(transform.position.x, screenSize.y - 25f, transform.position.z);
        isSnapped = true;
        PlaySit();
    }

    private void SnapToWindow(float windowTop)
    {
        transform.position = new Vector3(transform.position.x, windowTop + 10f, transform.position.z);
        isSnapped = true;
        PlaySit();
    }

    private void PlayIdle()
    {
        if (animator != null && idleAnimation != null)
        {
            animator.Play(idleAnimation.name);
        }
    }

    private void PlaySit()
    {
        if (animator != null && sitAnimation != null)
        {
            animator.Play(sitAnimation.name);
        }
    }

    private void PlayDrag()
    {
        if (animator != null && dragAnimation != null)
        {
            animator.Play(dragAnimation.name);
        }
    }

    public void StartDragging()
    {
        isDragging = true;
        PlayDrag();
    }

    public void StopDragging()
    {
        isDragging = false;
        PlayIdle();
    }

    public void ResetSnap()
    {
        isSnapped = false;
        PlayIdle();
    }
}
