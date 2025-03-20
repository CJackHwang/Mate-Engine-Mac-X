using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;

public class AvatarSitController : MonoBehaviour
{
    [Header("Sitting Settings")]
    public Animator animator;
    public float snapDistance = 10f; // Distance threshold for snapping to a window
    public float checkInterval = 0.2f; // How often to check for nearby windows

    private bool isSitting = false;
    private bool isDragging = false;
    private IntPtr attachedWindow = IntPtr.Zero;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        StartCoroutine(CheckForWindowContinuously());
    }

    private IEnumerator CheckForWindowContinuously()
    {
        while (true)
        {
            if (!isDragging && !isSitting)
                TryAttachToWindow();

            yield return new WaitForSeconds(checkInterval);
        }
    }

    void TryAttachToWindow()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero) return;

        Rect windowRect;
        if (GetWindowRect(foregroundWindow, out windowRect))
        {
            Vector2 petPosition = transform.position;
            float windowTopY = Screen.height - windowRect.top;

            if (Mathf.Abs(petPosition.y - windowTopY) < snapDistance)
            {
                isSitting = true;
                attachedWindow = foregroundWindow;
                animator.SetBool("isSitting", true);
            }
        }
    }

    void Update()
    {
        if (isDragging)
        {
            isSitting = false;
            animator.SetBool("isSitting", false);
            attachedWindow = IntPtr.Zero;
        }

        if (isSitting && attachedWindow != IntPtr.Zero)
            FollowWindow();
    }

    void FollowWindow()
    {
        Rect windowRect;
        if (GetWindowRect(attachedWindow, out windowRect))
        {
            transform.position = new Vector2(windowRect.left + (windowRect.width / 2), Screen.height - windowRect.top);
        }
        else
        {
            isSitting = false;
            animator.SetBool("isSitting", false);
            attachedWindow = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left, top, right, bottom;
        public int width { get { return right - left; } }
        public int height { get { return bottom - top; } }
    }
}
