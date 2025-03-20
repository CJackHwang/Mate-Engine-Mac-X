using CitrioN.Common;
using UnityEngine;
using UnityEngine.UI;

namespace CitrioN.UI
{
  /// <summary>
  /// Base class for <see cref="Canvas"/> based UI panels.
  /// Enables or disables the <see cref="Canvas"/> component
  /// when the panel is opened or closed.
  /// Having panels in separate canvases improves performance
  /// as they are set dirty less often.
  /// </summary>
  //[RequireComponent(typeof(Canvas))]
  [HeaderInfo("\n\nCanvas Panel:\nPanel that is opened and closed by controlling a 'Canvas' component.")]
  public class CanvasUIPanel : AbstractUIPanel
  {
    [Header("Canvas UI Panel")]
    /// <summary>
    /// The reference to the <see cref="Canvas"/> component 
    /// attached to this GameObject. Used to show/hide this panel.
    /// </summary>
    [SerializeField/*, ReadOnly*/]
    [Tooltip("The reference to the Canvas component. " +
             "If left blank the script will try to find it on this GameObject.")]
    private Canvas canvas;

    [SerializeField]
    [Tooltip("An optional button that will close this panel when clicked.")]
    protected Button closePanelButton;

    public Canvas Canvas
    {
      get
      {
        try
        {
          if (canvas == null && ApplicationQuitListener.isQuitting == false/* && gameObject != null*/)
          {
            // Check if the GameObject is still valid. 
            // For some reason in some cases it may not be.
            canvas = gameObject?.GetComponent<Canvas>();
          }
        }
        catch (System.Exception e)
        {
          Debug.LogError($"{e}");
          //throw;
        }
        return canvas;
      }
    }

    /// <summary>
    /// Determines if this panel is currently open by checking
    /// if the <see cref="Canvas"/> component is currently enabled.
    /// </summary>
    public override bool IsOpen
    {
      get
      {
        if (Canvas != null) { return Canvas.enabled; }
        else
        {
          if (ApplicationQuitListener.isQuitting == false)
          {
            Debug.LogWarning($"Unable to determine the open state for " +
                             $"{name} as no Canvas reference is cached!");
          }
          return false;
        }
      }
    }

    /// <summary>
    /// Determines if this panel can be opened by checking
    /// if the <see cref="Canvas"/> component is currently disabled.
    /// </summary>
    public override bool CanOpen
    {
      get
      {
        if (Canvas == null) { return false; }
        return base.CanOpen && !Canvas.enabled;
      }
    }

    protected virtual void Reset()
    {
      canvas = GetComponent<Canvas>();
    }

    /// <summary>
    /// Shows or hides this panel by enabling or disabling the <see cref="Canvas"/>.
    /// </summary>
    /// <param name="show">Whether this panel should be shown or hidden</param>
    protected override void Show(bool show)
    {
      if (Canvas != null) { Canvas.enabled = show; }
    }

    /// <summary>
    /// Determines the position of the mouse based on the canvas settings.
    /// </summary>
    /// <returns>The position of the mouse inside the <see cref="Canvas"/></returns>
    protected virtual Vector2 GetMousePositionOnCanvas()
    {
      if (Canvas != null)
      {
        // Determine the point by using the relative sizes of the canvas rect 
        // to the actual screen size and multiplying this with the mouse position.
        return new Vector2((Canvas.GetComponent<RectTransform>().sizeDelta.x / Screen.width) *
                            UnityEngine.Input.mousePosition.x,
                           (Canvas.GetComponent<RectTransform>().sizeDelta.y / Screen.height) *
                            UnityEngine.Input.mousePosition.y);
      }
      return default;
    }

    protected override void OnPanelOpened()
    {
      base.OnPanelOpened();

      if (Canvas != null)
      {
        if (Canvas.TryGetComponent<RectTransform>(out var t))
        {
          t.RebuildHierarchyLayoutImmediate();
        }
      }

      //var selectables = Selectable.allSelectablesArray;

      //foreach (var selectable in selectables)
      //{
      //  ConsoleLogger.Log(selectable.name);
      //}

      //EventSystem.current.SetSelectedGameObject(selectables[0].gameObject);

      if (closePanelButton != null)
      {
        closePanelButton.onClick.RemoveListener(CloseNoParams);
        closePanelButton.onClick.AddListener(CloseNoParams);
      }
    }
  }
}