using UnityEngine;
using System;
using System.Runtime.InteropServices;

public class WindowSnapping : MonoBehaviour
{
    private Animator animator;
    private bool isDragging = false;
    private bool isSitting = false;

    public float snapDistance = 50f; // How close to a window before snapping
    public bool showDebug = true; // Debug visualization toggle

    private Vector3 offset;
    private IntPtr targetWindow = IntPtr.Zero;
    private Vector3 snapPosition;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    private struct RECT
    {
        public int left, top, right, bottom;
    }

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (isDragging)
        {
            FollowMouse();
        }
        else if (isSitting && targetWindow != IntPtr.Zero)
        {
            FollowWindow();
        }
    }

    void FollowMouse()
    {
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = Camera.main.WorldToScreenPoint(transform.position).z;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mousePos);
        transform.position = worldPos + offset;
    }

    void FollowWindow()
    {
        if (targetWindow != IntPtr.Zero && GetWindowRect(targetWindow, out RECT rect))
        {
            transform.position = new Vector3((rect.left + rect.right) / 2f, rect.top, transform.position.z);
        }
    }

    void OnMouseDown()
    {
        isDragging = true;
        isSitting = false;
        animator.SetBool("isDragging", true);
        animator.SetBool("isSitting", false);

        Vector3 mousePos = Input.mousePosition;
        mousePos.z = Camera.main.WorldToScreenPoint(transform.position).z;
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(mousePos);
        offset = transform.position - worldPos;
    }

    void OnMouseUp()
    {
        isDragging = false;
        animator.SetBool("isDragging", false);
        CheckForWindowSnap();
    }

    void CheckForWindowSnap()
    {
        IntPtr currentWindow = GetForegroundWindow();
        if (currentWindow != IntPtr.Zero && GetWindowRect(currentWindow, out RECT rect))
        {
            Vector3 windowTop = new Vector3((rect.left + rect.right) / 2f, rect.top, 0);
            float distance = Vector3.Distance(transform.position, windowTop);

            if (distance < snapDistance)
            {
                snapPosition = new Vector3(windowTop.x, windowTop.y, transform.position.z);
                transform.position = snapPosition;

                animator.SetBool("isSitting", true);
                isSitting = true;
                targetWindow = currentWindow;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebug) return;

        // Draw detection sphere for snap area
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, snapDistance);

        // Draw line to snap position
        if (isDragging)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, snapPosition);
        }
    }
}
