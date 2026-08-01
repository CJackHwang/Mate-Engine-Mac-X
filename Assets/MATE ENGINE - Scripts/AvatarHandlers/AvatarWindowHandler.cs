using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class AvatarWindowHandler : MonoBehaviour
{
    [Header("Snap Safety")]
    public float minDragHoldSecondsToSit = 1f;
    public float unsnapCooldownSeconds = 0.3f;
    float _dragStartTime = -1f;
    bool _canSitHold;
    float _unsnapCooldownUntil = -1f;

    [Header("Snap Probe Offset")]
    public float probeZoneYOffsetLocal = 0f;
    Vector3 GetProbeWorld() => GetHipWorld() + transform.up * (probeZoneYOffsetLocal * transform.lossyScale.y);
    [Header("Snap Probe")]
    public float probeRadiusPx = 24f;
    public bool showProbeGizmo = true;
    public Color probeGizmoColor = Color.magenta;
    bool _guardZoneActive;
    Vector2 _guardCenterDesktop;
    [Header("Snap Guard Zone")]
    public bool useGuardZone = true;
    public float probeGuardPx = 240f;
    public Color probeGuardGizmoColor = Color.cyan;
    [Header("Sit Blockers")]
    public List<string> blockSitIfBoolTrue = new List<string>();
    readonly List<string> _blockSitValidNames = new List<string>();
    [Header("Window Sit BlendTree")]
    public int totalWindowSitAnimations = 4;
    static readonly int windowSitIndexParam = Animator.StringToHash("WindowSitIndex");
    bool wasSitting;
    [Header("Seat Alignment")]
    [Range(-256f, 256f)] public float seatOffsetPx = 0f;
    [Range(-1.0f, 1.0f)] public float windowSitYOffset = -0.0286f;
    // "auto" = both edges, "up" = top edge only, "down" = bottom edge only
    public string windowSitEdge = "auto";
    [Header("Occluder")]
    public Material occluderMaterial;
    public Camera targetCamera;
    public float targetQuadZOffset = 0.001f;
    public float othersQuadZOffset = 0.002f;
    public int maxOtherQuads = 12;
    [Header("Occluder Pool")]
    public bool precreateQuadsOnStart = true;
    public int prewarmOtherQuads = 6;
    [Header("Target Quad Z Auto-Scale")]
    public bool autoScaleTargetZ = true;
    public float targetZBase = 3.2f;
    public float targetZRefScale = 1.0f;
    public float targetZSensitivity = 3.0f;
    public float targetZMin = 0.05f;
    public float targetZMax = 10f;
    [Tooltip("Shifts the cliff occluder plane forward/back from the character's seat depth. Positive moves it deeper (character shows more), negative closer (more of the character's back / hair is occluded below the seat line).")]
    [Range(-1f, 1f)] public float windowSitCliffOffset = -0.18f;
    [Header("Snap Smoothing")]
    public bool enableSnapSmoothing = true;
    [Range(0.01f, 0.5f)] public float snapSmoothingTime = 0.12f;
    public float snapSmoothingMaxSpeed = 6000f;
    bool _snapSmoothingActive;
    float _snapVelX, _snapVelY;
    bool _havePrevSnapRect;
    RECT _prevSnapRect;
    Vector3 _prevLossyScale;
    [Header("Snap Trigger")]
    public int minDragPixelsToSnap = 4;
    int _dragStartCursorX, _dragStartCursorY;
    bool _postSettleRecalib;
    int _postSettleFrames;
    [Header("Snap Guard")]
    public int snapGuardFrames = 8;
    public int snapLatchFrames = 18;
    public int unsnapVerticalBand = 16;
    [Header("Transparent-Window-Filter")]
    [Range(0, 255)] public int layeredAlphaIgnoreBelow = 230;
    public bool ignoreLayeredClickThrough = true;
    public bool ignoreLayeredToolOrNoActivate = true;
    public bool ignoreLayeredWithColorKey = true;
    [Header("Performance")]
    public float windowEnumFPS = 15f;
    public float windowEnumIdleFPS = 8f;

    float snapFraction;
    int _snapCursorY;
    bool wasDragging;
    IntPtr snappedHWND = IntPtr.Zero, unityHWND = IntPtr.Zero;
    Vector2 lastDesktopPosition;
    readonly List<WindowEntry> cachedWindows = new List<WindowEntry>(128);
    readonly List<WindowEntry> activeOccluders = new List<WindowEntry>(16);
    Animator animator;
    AvatarAnimatorController controller;
    readonly System.Text.StringBuilder classNameBuffer = new System.Text.StringBuilder(256);
    Transform occluderRoot;
    GameObject targetQuadGO;
    Mesh targetMesh;
    readonly List<GameObject> otherQuadGOs = new List<GameObject>(16);
    readonly List<Mesh> otherMeshes = new List<Mesh>(16);
    Material _occluderSharedMat;
    int _guard;
    int _latch;
    float _nextEnumTime;
    float _nextMacWindowGuardTime;
    bool _macHideForFullscreen;
    // While a macOS Space transition is animating, the desktop (and every normal
    // window) slides while this floating pet window stays put. Snap-following a
    // stale cached rect during that animation is what causes the avatar to drift,
    // so we pause follow/unsnap until the transition settles.
    float _macSpaceTransitionUntil;
    float macSpaceTransitionSeconds = 0.6f;
    RECT _lastUnityCli;
    bool _haveUnityCli;
    static readonly int[] TRI = { 0, 1, 2, 0, 2, 3 };
    readonly Vector3[] verts4 = new Vector3[4];
    readonly Vector3[] verts4Other = new Vector3[4];
    Transform boneHips, boneLUL, boneRUL, boneLFoot, boneRFoot, boneHead;
    SkinnedMeshRenderer[] skinned;
    bool _skinnedCached;
    bool seatCalibrated;
    Vector3 seatLocalAtSnap;
    Vector3 boundsMinSnapLocal;
    Vector3 boundsSizeSnapLocal;
    float seatNormY;
    bool _recentUnsnap;
    int _lastSnapTopY;
    int _snappedEdgeY;  // the actual edge Y we snapped to (top or bottom)
    uint _currentPid;
    float _guardRadiusSq;
    void Start()
    {
#if UNITY_STANDALONE_WIN
        unityHWND = Process.GetCurrentProcess().MainWindowHandle;
        _currentPid = GetCurrentProcessId();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        _currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
        animator = GetComponent<Animator>();
        controller = GetComponent<AvatarAnimatorController>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (SaveLoadHandler.Instance != null && SaveLoadHandler.Instance.data.windowSitCliffOffsetSet)
            windowSitCliffOffset = SaveLoadHandler.Instance.data.windowSitCliffOffset;
        CacheRigRefs(); BuildBlockSitCache(); EnsureOccluderRoot();
        if (occluderMaterial != null) _occluderSharedMat = new Material(occluderMaterial);
        if (precreateQuadsOnStart)
        {
            EnsureTargetQuad();
            int pre = Mathf.Clamp(prewarmOtherQuads, 0, Mathf.Max(maxOtherQuads, 0));
            for (int i = 0; i < pre; i++) EnsureOtherQuad(i);
            SetTargetQuadActive(false); SetOtherQuadsActive(0);
        }
        SetTopMost(SaveLoadHandler.Instance != null ? SaveLoadHandler.Instance.data.isTopmost : true);
        _nextEnumTime = 0f;
        _prevLossyScale = transform.lossyScale;
        _lastSnapTopY = int.MinValue;
        cachedWindows.Capacity = Mathf.Max(cachedWindows.Capacity, 128);
        activeOccluders.Capacity = Mathf.Max(activeOccluders.Capacity, maxOtherQuads);
    }
    // Built-in runtime fine-tuning for the cliff occluder plane. Holds Command and
    // presses [ or ] (also -/= for coarse steps) to move the plane forward/back in
    // real time; the value persists across restarts via SaveLoadHandler.
    void HandleCliffTuningHotkey()
    {
        bool cmd = Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!cmd) return;
        float delta = 0f;
        if (Input.GetKeyDown(KeyCode.LeftBracket)) delta = -0.02f;
        else if (Input.GetKeyDown(KeyCode.RightBracket)) delta = 0.02f;
        else if (Input.GetKeyDown(KeyCode.Minus)) delta = -0.1f;
        else if (Input.GetKeyDown(KeyCode.Equals)) delta = 0.1f;
        if (delta == 0f) return;
        windowSitCliffOffset = Mathf.Clamp(windowSitCliffOffset + delta, -1f, 1f);
        UnityEngine.Debug.Log($"[WindowSit] Cliff offset = {windowSitCliffOffset:0.00}  (⌘+[ / ⌘+] to tune)");
        if (SaveLoadHandler.Instance != null)
        {
            SaveLoadHandler.Instance.data.windowSitCliffOffset = windowSitCliffOffset;
            SaveLoadHandler.Instance.data.windowSitCliffOffsetSet = true;
            SaveLoadHandler.Instance.SaveToDisk();
        }
    }
    // Built-in runtime fine-tuning for the character's overall seat height.
    // Adjusts windowSitYOffset (the seat point's position on the character body),
    // so the character moves up/down while the occluder's horizontal line stays
    // pinned to the window edge. Holds Command and presses ↑/↓ (fine) or
    // Shift+↑/↓ (coarse); persists via settings (same value the settings-menu
    // slider drives).
    void HandleSeatHeightHotkey()
    {
        bool cmd = Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        if (!cmd) return;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float delta = 0f;
        // Increasing windowSitYOffset raises the seat point on the body, which
        // makes the whole character sit lower; decreasing raises the character.
        if (Input.GetKeyDown(KeyCode.UpArrow)) delta = shift ? -0.1f : -0.02f;
        else if (Input.GetKeyDown(KeyCode.DownArrow)) delta = shift ? 0.1f : 0.02f;
        if (delta == 0f) return;
        windowSitYOffset = Mathf.Clamp(windowSitYOffset + delta, -1f, 1f);
        UnityEngine.Debug.Log($"[WindowSit] Seat height = {windowSitYOffset:0.00}  (⌘+↑ / ⌘+↓ to tune)");
        if (SaveLoadHandler.Instance != null)
        {
            SaveLoadHandler.Instance.data.windowSitYOffset = windowSitYOffset;
            SaveLoadHandler.Instance.SaveToDisk();
        }
    }
    void OnDisable()
    {
        ClearSnapAndHide();
    }

    void OnDestroy()
    {
        CleanupOccluderArtifacts();
    }

    void BuildBlockSitCache()
    {
        _blockSitValidNames.Clear();
        if (animator == null || blockSitIfBoolTrue == null || blockSitIfBoolTrue.Count == 0) return;
        var wanted = new HashSet<string>(blockSitIfBoolTrue);
        var ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].type == AnimatorControllerParameterType.Bool && wanted.Contains(ps[i].name))
                _blockSitValidNames.Add(ps[i].name);
    }
    bool IsSitBlocked()
    {
        if (animator == null || _blockSitValidNames.Count == 0) return false;
        for (int i = 0; i < _blockSitValidNames.Count; i++)
            if (animator.GetBool(_blockSitValidNames[i])) return true;
        return false;
    }
    void Update()
    {
#if !UNITY_STANDALONE_WIN && !(UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX)
        return;
#endif
        if (snappedHWND != IntPtr.Zero)
        {
            if ((transform.lossyScale - _prevLossyScale).sqrMagnitude > 1e-8f) { _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
            _prevLossyScale = transform.lossyScale;
        }

#if UNITY_STANDALONE_WIN
        if (unityHWND == IntPtr.Zero || animator == null || controller == null) return;
#else
        if (animator == null || controller == null) return;
#endif
        if (!SaveLoadHandler.Instance.data.enableWindowSitting) { ClearSnapAndHide(); return; }
        if (IsSitBlocked()) { if (snappedHWND != IntPtr.Zero) ClearSnapAndHide(); return; }

        bool isWindowSitNow = animator.GetBool("isWindowSit");
        if (isWindowSitNow && !wasSitting) animator.SetFloat(windowSitIndexParam, UnityEngine.Random.Range(0, totalWindowSitAnimations));
        wasSitting = isWindowSitNow;

        HandleCliffTuningHotkey();
        HandleSeatHeightHotkey();

        float enumHz = (controller.isDragging || snappedHWND != IntPtr.Zero) ? Mathf.Max(1f, windowEnumFPS) : Mathf.Max(1f, windowEnumIdleFPS);
        if (Time.unscaledTime >= _nextEnumTime)
        {
            UpdateCachedWindows();
            if (snappedHWND != IntPtr.Zero) RebuildActiveOccluders();
            _nextEnumTime = Time.unscaledTime + 1f / enumHz;
        }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (Time.unscaledTime >= _nextMacWindowGuardTime)
        {
            _nextMacWindowGuardTime = Time.unscaledTime + 1f;
            bool wantTopmost = SaveLoadHandler.Instance != null &&
                               (SaveLoadHandler.Instance.data.isTopmost || snappedHWND != IntPtr.Zero);
            bool fullscreenBlock = !MacWindowHelper.IsAppFocused() && MacWindowHelper.IsFrontWindowFullscreen();

            if (fullscreenBlock)
            {
                if (!_macHideForFullscreen)
                {
                    _macHideForFullscreen = true;
                    MacWindowHelper.SetTopMost(false);
                }
            }
            else
            {
                if (_macHideForFullscreen)
                {
                    _macHideForFullscreen = false;
                    SetTopMost(SaveLoadHandler.Instance != null ? SaveLoadHandler.Instance.data.isTopmost : true);
                }
                if (wantTopmost) MacWindowHelper.BringSelfToFront();
            }

            if (snappedHWND == IntPtr.Zero && !controller.isDragging)
                MacWindowHelper.ConstrainWindowToScreens();
        }
#endif

        if (controller.isDragging && !wasDragging)
        {
#if UNITY_STANDALONE_WIN
            Kirurobo.WinApi.POINT cp;
            if (Kirurobo.WinApi.GetCursorPos(out cp))
            {
                _dragStartCursorX = cp.x; _dragStartCursorY = cp.y;
                if (snappedHWND != IntPtr.Zero && isWindowSitNow) _snapCursorY = cp.y;
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (MacWindowHelper.TryGetCursorPosition(out Vector2Int macCp))
            {
                _dragStartCursorX = macCp.x; _dragStartCursorY = macCp.y;
                if (snappedHWND != IntPtr.Zero && isWindowSitNow) _snapCursorY = macCp.y;
            }
#endif
            _dragStartTime = Time.unscaledTime;
            _canSitHold = false;
        }
        if (controller.isDragging)
        {
            if (!_canSitHold && _dragStartTime >= 0f && Time.unscaledTime - _dragStartTime >= minDragHoldSecondsToSit) _canSitHold = true;
        }
        else
        {
            _canSitHold = false;
            _dragStartTime = -1f;
        }

        if (_recentUnsnap)
        {
            if (!controller.isDragging) _recentUnsnap = false;
            else if (ComputeZoneDesktop(out _, out float py))
            {
                int vBand = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
                if (Mathf.Abs(py - _lastSnapTopY) >= vBand) _recentUnsnap = false;
            }
        }

        if (snappedHWND != IntPtr.Zero)
        {
            bool handled = false;
            for (int i = 0; i < cachedWindows.Count; i++)
            {
                var win = cachedWindows[i];
                if (win.hwnd != snappedHWND) continue;
                if (IsWindowMaximized(win.hwnd) || IsWindowFullscreen(win)) { ClearSnapAndHide(); handled = true; break; }
            }
            if (!handled && (IsIconic(snappedHWND) || IsCloaked(snappedHWND))) { ClearSnapAndHide(); }
        }
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (MacSystemBridge.ConsumeSpaceChange())
        {
            _macSpaceTransitionUntil = Time.unscaledTime + macSpaceTransitionSeconds;
            // Forget the pre-transition target rect so the first follow after the
            // slide re-anchors smoothly instead of being treated as a "jump".
            _havePrevSnapRect = false;
        }
        bool macSpaceTransition = Time.unscaledTime < _macSpaceTransitionUntil;
#else
        bool macSpaceTransition = false;
#endif
        if (controller.isDragging)
        {
            if (snappedHWND == IntPtr.Zero) { if (_canSitHold && DraggedPastSnapThreshold()) TrySnap(); }
            else if (macSpaceTransition) { _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
            else if (!IsStillNearSnappedWindow()) { SetGuardZoneFromCurrent(); ClearSnapAndHide(true); }
            else FollowSnapped(true);
        }
        else if (!controller.isDragging && snappedHWND != IntPtr.Zero && !macSpaceTransition) FollowSnapped(false);
        if (animator.GetBool("isBigScreenAlarm"))
        {
            if (isWindowSitNow) animator.SetBool("isWindowSit", false);
            ClearSnapAndHide();
        }

        if (snappedHWND != IntPtr.Zero && _postSettleRecalib)
        {
            if (_postSettleFrames > 0) _postSettleFrames--;
            else
            {
#if UNITY_STANDALONE_WIN
                bool gotRect = GetWindowRect(snappedHWND, out RECT tr);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                RECT tr = new RECT();
                bool gotRect = false;
                for (int i = 0; i < cachedWindows.Count; i++)
                    if (cachedWindows[i].hwnd == snappedHWND) { tr = cachedWindows[i].rect; gotRect = true; break; }
#else
                RECT tr = new RECT(); bool gotRect = false;
#endif
                if (gotRect)
                {
                    int trTop    = tr.Top;
                    int trBottom = tr.Bottom;
                    int snapEdgeY = (windowSitEdge == "down") ? trBottom : trTop;
                    CalibrateSeatAnchorToDesktopY(snapEdgeY + seatOffsetPx);
                    if (ComputeSeatDesktop(out float px2, out _))
                    {
                        float w = Mathf.Max(1, tr.Right - tr.Left);
                        snapFraction = Mathf.Clamp01((px2 - tr.Left) / w);
                    }
                    _snapSmoothingActive = enableSnapSmoothing;
                    _snapVelX = _snapVelY = 0f;
                    _havePrevSnapRect = false;
                    PinToTarget(tr);
                }
                _postSettleRecalib = false;
            }
        }
        wasDragging = controller.isDragging;
    }
    void LateUpdate() { UpdateOccluderQuadsFrameSync(); }
    bool DraggedPastSnapThreshold()
    {
#if UNITY_STANDALONE_WIN
        Kirurobo.WinApi.POINT cp;
        if (!Kirurobo.WinApi.GetCursorPos(out cp)) return true;
        return Mathf.Abs(cp.x - _dragStartCursorX) >= minDragPixelsToSnap || Mathf.Abs(cp.y - _dragStartCursorY) >= minDragPixelsToSnap;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (!MacWindowHelper.TryGetCursorPosition(out Vector2Int macCp))
            return true;
        return Mathf.Abs(macCp.x - _dragStartCursorX) >= minDragPixelsToSnap || Mathf.Abs(macCp.y - _dragStartCursorY) >= minDragPixelsToSnap;
#else
        return true;
#endif
    }
    void SetGuardZoneFromCurrent()
    {
        if (!useGuardZone) return;
        if (ComputeZoneDesktop(out float gx, out float gy))
        {
            _guardCenterDesktop = new Vector2(gx, gy);
            _guardZoneActive = true;
            float r = ScaledGuardRadiusF();
            _guardRadiusSq = r * r;
        }
    }
    float ScaleFactor() => boneHips != null ? boneHips.lossyScale.magnitude : Mathf.Max(0.0001f, transform.lossyScale.magnitude);
    int ScaledProbeRadiusI() => Mathf.Max(1, Mathf.RoundToInt(probeRadiusPx * ScaleFactor()));
    int ScaledGuardRadiusI() => Mathf.Max(1, Mathf.RoundToInt(probeGuardPx * ScaleFactor()));
    float ScaledProbeRadiusF() => probeRadiusPx * ScaleFactor();
    float ScaledGuardRadiusF() => probeGuardPx * ScaleFactor();
    Vector3 GetHipWorld() => boneHips != null ? boneHips.position : transform.position;
    bool ComputeZoneDesktop(out float px, out float py) => ComputeDesktopFromWorld(GetProbeWorld(), out px, out py);
    bool ComputeSeatDesktop(out float px, out float py) => ComputeDesktopFromWorld(GetSeatWorldCurrent(), out px, out py);
    bool ComputeDesktopFromWorld(Vector3 wp, out float px, out float py)
    {
        px = py = 0f;
        if (targetCamera == null) return false;
        if (!GetUnityClientRect(out RECT uCli)) return false;
        _haveUnityCli = true; _lastUnityCli = uCli;
        Vector3 sp = targetCamera.WorldToScreenPoint(wp);
        if (sp.z < 0.01f) return false;
        float clientW = Mathf.Max(1f, uCli.Right - uCli.Left);
        float clientH = Mathf.Max(1f, uCli.Bottom - uCli.Top);
        px = uCli.Left + Mathf.Clamp(sp.x, 0, targetCamera.pixelWidth) * (clientW / Mathf.Max(1, targetCamera.pixelWidth));
        py = uCli.Top + (targetCamera.pixelHeight - Mathf.Clamp(sp.y, 0, targetCamera.pixelHeight)) * (clientH / Mathf.Max(1, targetCamera.pixelHeight));
        return true;
    }
    void CacheRigRefs()
    {
        if (animator != null && animator.isHuman)
        {
            boneHips = animator.GetBoneTransform(HumanBodyBones.Hips);
            boneLUL = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            boneRUL = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            boneLFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            boneRFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            boneHead = animator.GetBoneTransform(HumanBodyBones.Head);
        }
        if (!_skinnedCached)
        {
            skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _skinnedCached = true;
        }
    }
    bool IsEffectivelyTransparentWindow(IntPtr hWnd, System.Text.StringBuilder cls)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_LAYERED) == 0) return false;
        if (ignoreLayeredClickThrough && (ex & WS_EX_TRANSPARENT) != 0) return true;
        if (ignoreLayeredToolOrNoActivate && ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0)) return true;
        if (GetLayeredWindowAttributes(hWnd, out _, out byte alpha, out uint flags))
        {
            if (ignoreLayeredWithColorKey && (flags & LWA_COLORKEY) != 0) return true;
            if ((flags & LWA_ALPHA) != 0 && alpha <= layeredAlphaIgnoreBelow) return true;
        }
        long st = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
        int titleLen = GetWindowTextLength(hWnd);
        if ((st & WS_CAPTION) == 0 && titleLen <= 1) return true;
        if ((st & WS_CAPTION) == 0 && (SBEq(cls, "UnityWndClass") || SBEq(cls, "UnityGUIView"))) return true;
        return false;
    }
    bool IsSameProcessWindow(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid == _currentPid;
    }
    void ClearSnapAndHide(bool fromUnsnap = false)
    {
        _havePrevSnapRect = false;
        _snapSmoothingActive = false;
        _snapVelX = _snapVelY = 0f;
        if (controller != null && controller.isDragging) _recentUnsnap = true;
        if (fromUnsnap) _unsnapCooldownUntil = Time.unscaledTime + Mathf.Max(0f, unsnapCooldownSeconds);
        snappedHWND = IntPtr.Zero;
        seatCalibrated = false;
        _snappedEdgeY = 0;
        if (animator != null) { animator.SetBool("isWindowSit", false); animator.SetBool("isTaskbarSit", false); }
        SetTopMost(SaveLoadHandler.Instance != null ? SaveLoadHandler.Instance.data.isTopmost : true);
        SetTargetQuadActive(false); SetOtherQuadsActive(0);
        _guard = _latch = 0;
        activeOccluders.Clear();
    }

    void UpdateCachedWindows()
    {
#if UNITY_STANDALONE_WIN
        cachedWindows.Clear();
        EnumWindows((hWnd, lParam) =>
        {
            if (hWnd == unityHWND || !IsWindowVisible(hWnd) || !GetWindowRect(hWnd, out RECT r)) return true;
            classNameBuffer.Clear(); GetClassName(hWnd, classNameBuffer, classNameBuffer.Capacity);
            if (IsSameProcessWindow(hWnd) || IsEffectivelyTransparentWindow(hWnd, classNameBuffer)) return true;
            bool isTaskbar = SBEq(classNameBuffer, "Shell_TrayWnd") || SBEq(classNameBuffer, "Shell_SecondaryTrayWnd");
            if (isTaskbar) { cachedWindows.Add(new WindowEntry { hwnd = hWnd, rect = r, isTaskbar = true }); return true; }
            if (IsLikelyUniWindowMascot(hWnd, classNameBuffer) || !IsSitEligibleWindow(hWnd, r, classNameBuffer)) return true;
            cachedWindows.Add(new WindowEntry { hwnd = hWnd, rect = r, isTaskbar = false });
            return true;
        }, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        cachedWindows.Clear();
        MacWindowListBinding.MacWin_Refresh((int)_currentPid);
        int count = MacWindowListBinding.MacWin_GetCount();
        for (int i = 0; i < count; i++)
        {
            if (MacWindowListBinding.MacWin_GetWindow(i,
                out int wx, out int wy, out int ww, out int wh,
                out int pid, out int layer, out int isOnscreen, out int windowNumber) == 0) continue;
            if (isOnscreen == 0 || layer < 0) continue;
            if (ww < 200 || wh < 60) continue;
            // wx,wy is the top-left corner; wy+wh is the bottom edge (Y-down).
            // CGWindowList is ordered front-to-back: index 0 is the frontmost window.
            var r = new RECT { Left = wx, Top = wy, Right = wx + ww, Bottom = wy + wh };
            cachedWindows.Add(new WindowEntry { hwnd = new IntPtr(windowNumber), rect = r, isTaskbar = false, layer = layer });
        }
#endif
    }
    void RebuildActiveOccluders()
    {
        activeOccluders.Clear();
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        int snappedIndex = FindCachedIndex(snappedHWND);
        if (snappedIndex < 0) return;
        RECT snappedRect = cachedWindows[snappedIndex].rect;
        for (int i = 0; i < snappedIndex && activeOccluders.Count < maxOtherQuads; i++)
        {
            var w = cachedWindows[i];
            if (w.layer != 0) continue;                      // only normal windows sit above a normal target
            if (!RectsOverlap(w.rect, snappedRect)) continue; // only ones actually covering the target matter
            activeOccluders.Add(w);
        }
#else
        for (int i = 0; i < cachedWindows.Count && activeOccluders.Count < maxOtherQuads; i++)
        {
            var w = cachedWindows[i];
            if (w.hwnd == unityHWND || w.hwnd == snappedHWND || IsSameProcessWindow(w.hwnd)) continue;
            classNameBuffer.Clear(); GetClassName(w.hwnd, classNameBuffer, classNameBuffer.Capacity);
            if (IsEffectivelyTransparentWindow(w.hwnd, classNameBuffer) || IsLikelyUniWindowMascot(w.hwnd, classNameBuffer)) continue;
            if (!(w.isTaskbar || IsAboveInZOrder(w.hwnd, snappedHWND))) continue;
            activeOccluders.Add(w);
        }
#endif
    }
    bool IsSitEligibleWindow(IntPtr hWnd, RECT r, System.Text.StringBuilder cls)
    {
        if (GetParent(hWnd) != IntPtr.Zero || GetAncestor(hWnd, GA_ROOT) != hWnd || IsIconic(hWnd) || GetWindowTextLength(hWnd) == 0 || IsCloaked(hWnd)) return false;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w < 200 || h < 60) return false;
        if (SBEq(cls, "Progman") || SBEq(cls, "WorkerW") || SBEq(cls, "DV2ControlHost") || SBEq(cls, "MsgrIMEWindowClass")) return false;
        if (SBStartsWith(cls, "#") || SBContains(cls, "Desktop")) return false;
        return true;
    }
    bool IsCloaked(IntPtr hWnd)
    {
#if UNITY_STANDALONE_WIN
        int cloaked = 0; DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)); return cloaked != 0;
#else
        return false;
#endif
    }
    void TrySnap()
    {
        if (Time.unscaledTime < _unsnapCooldownUntil) return;
        if (IsSitBlocked()) return;
        if (useGuardZone && _guardZoneActive && ComputeZoneDesktop(out float gx, out float gy))
        {
            float dx = gx - _guardCenterDesktop.x;
            float dy = gy - _guardCenterDesktop.y;
            if (dx * dx + dy * dy < _guardRadiusSq) return;
            _guardZoneActive = false;
        }
        if (!ComputeZoneDesktop(out float px, out float py)) return;
        if (_recentUnsnap)
        {
            int vBlock = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
            if (Mathf.Abs(py - _lastSnapTopY) < vBlock) return;
        }

        int spr = ScaledProbeRadiusI();
        float sprF = spr;

        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd == unityHWND) continue;
            int left = win.rect.Left, right = win.rect.Right;
            int top    = win.rect.Top;
            int bottom = win.rect.Bottom;
            if (!(px >= left && px <= right)) continue;
            bool checkTop    = windowSitEdge != "down";
            bool checkBottom = windowSitEdge != "up";
            bool nearTop    = checkTop    && Mathf.Abs(py - top)    <= sprF;
            bool nearBottom = checkBottom && Mathf.Abs(py - bottom) <= sprF;
            if (!nearTop && !nearBottom) continue;
            int snapEdge = (nearTop && nearBottom)
                ? (Mathf.Abs(py - top) <= Mathf.Abs(py - bottom) ? top : bottom)
                : (nearTop ? top : bottom);
            if (IsSameProcessWindow(win.hwnd)) continue;
            if (IsOccludedByHigherWindowsAtPoint(win.hwnd, Mathf.RoundToInt(px), Mathf.RoundToInt(py))) continue;
            classNameBuffer.Clear(); GetClassName(win.hwnd, classNameBuffer, classNameBuffer.Capacity);
            if (IsEffectivelyTransparentWindow(win.hwnd, classNameBuffer)) continue;

            lastDesktopPosition = GetUnityWindowPosition();
            snappedHWND = win.hwnd;
            _guardZoneActive = false;

            animator.SetBool("isWindowSit", true);
            animator.SetBool("isTaskbarSit", win.isTaskbar);
            animator.Update(0f);
            CalibrateSeatAnchorToDesktopY(snapEdge + seatOffsetPx);

            _postSettleFrames = 1; _postSettleRecalib = true;

            if (ComputeSeatDesktop(out float px2, out _))
            {
                float w = Mathf.Max(1, right - left);
                snapFraction = Mathf.Clamp01((px2 - left) / w);
            }

            _lastSnapTopY = snapEdge;
            _snappedEdgeY = snapEdge;
            _recentUnsnap = false;
            SetTopMost(true);

#if UNITY_STANDALONE_WIN
            Kirurobo.WinApi.POINT cp;
            if (Kirurobo.WinApi.GetCursorPos(out cp)) _snapCursorY = cp.y;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (MacWindowHelper.TryGetCursorPosition(out Vector2Int snapCp))
                _snapCursorY = snapCp.y;
#endif
            _guard = Mathf.Max(1, snapGuardFrames);
            _latch = Mathf.Max(1, snapLatchFrames);

            _snapSmoothingActive = enableSnapSmoothing;
            _snapVelX = _snapVelY = 0f;
            _havePrevSnapRect = false;

            RebuildActiveOccluders(); UpdateOccluderQuadsFrameSync();
#if UNITY_STANDALONE_WIN
            if (GetWindowRect(win.hwnd, out RECT tr)) PinToTarget(tr); else PinToTarget(win.rect);
#else
            PinToTarget(win.rect);
#endif
            return;
        }
    }
    void CancelSnapSmoothingIfTargetMoved(RECT tr)
    {
        if (!_havePrevSnapRect) { _prevSnapRect = tr; _havePrevSnapRect = true; return; }
        if (tr.Left != _prevSnapRect.Left || tr.Top != _prevSnapRect.Top || tr.Right != _prevSnapRect.Right || tr.Bottom != _prevSnapRect.Bottom)
        {
            _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f;
        }
        _prevSnapRect = tr;
    }
    bool CalibrateSeatAnchorToDesktopY(float targetDesktopY)
    {
        if (targetCamera == null || !GetUnityClientRect(out RECT uCli)) return false;
        Matrix4x4 inv = transform.worldToLocalMatrix;
        float yMinW = float.PositiveInfinity, yMaxW = float.NegativeInfinity;

        if (animator != null && animator.isHuman)
        {
            if (boneHead != null) { var p = boneHead.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneHips != null) { var p = boneHips.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneLUL != null) { var p = boneLUL.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneRUL != null) { var p = boneRUL.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneLFoot != null) { var p = boneLFoot.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
            if (boneRFoot != null) { var p = boneRFoot.position.y; if (p < yMinW) yMinW = p; if (p > yMaxW) yMaxW = p; }
        }
        float low, high;
        if (float.IsInfinity(yMinW) || float.IsInfinity(yMaxW))
        {
            Bounds lb = WorldBoundsToRootLocal(GetCombinedWorldBounds());
            float h = Mathf.Max(0.0001f, lb.size.y);
            low = lb.min.y - 0.5f * h - 0.25f;
            high = lb.max.y + 0.5f * h + 0.25f;
            boundsMinSnapLocal = lb.min;
            boundsSizeSnapLocal = lb.size;
        }
        else
        {
            Vector3 lmin = inv.MultiplyPoint3x4(new Vector3(transform.position.x, yMinW, transform.position.z));
            Vector3 lmax = inv.MultiplyPoint3x4(new Vector3(transform.position.x, yMaxW, transform.position.z));
            float ymin = Mathf.Min(lmin.y, lmax.y), ymax = Mathf.Max(lmin.y, lmax.y);
            float pad = Mathf.Max(0.05f, (ymax - ymin) * 0.2f);
            low = ymin - pad; high = ymax + pad;
            Bounds worldB = GetCombinedWorldBounds();
            Bounds localB = WorldBoundsToRootLocal(worldB);
            boundsMinSnapLocal = localB.min;
            boundsSizeSnapLocal = localB.size;
        }
        Vector3 guessL = transform.worldToLocalMatrix.MultiplyPoint3x4(SeatWorldGuess());
        float bestY = guessL.y, bestErr = float.MaxValue;

        for (int i = 0; i < 20; i++)
        {
            float mid = 0.5f * (low + high);
            Vector3 lp = new Vector3(guessL.x, mid, guessL.z);
            Vector3 sp = targetCamera.WorldToScreenPoint(transform.localToWorldMatrix.MultiplyPoint3x4(lp));
            if (sp.z < 0.01f) break;
            float clientH = Mathf.Max(1f, uCli.Bottom - uCli.Top);
            float py = uCli.Top + (targetCamera.pixelHeight - Mathf.Clamp(sp.y, 0, targetCamera.pixelHeight)) * (clientH / Mathf.Max(1, targetCamera.pixelHeight));
            float err = py - targetDesktopY;
            if (Mathf.Abs(err) < Mathf.Abs(bestErr)) { bestErr = err; bestY = mid; }
            if (err > 0f) high = mid; else low = mid;
        }
        seatLocalAtSnap = new Vector3(guessL.x, bestY, guessL.z);
        float denom = Mathf.Max(0.0001f, boundsSizeSnapLocal.y);
        seatNormY = Mathf.Clamp01((bestY - boundsMinSnapLocal.y) / denom);
        seatCalibrated = true;
        return true;
    }
    void FollowSnapped(bool dragging)
    {
#if UNITY_STANDALONE_WIN
        if (snappedHWND == IntPtr.Zero || !GetWindowRect(snappedHWND, out RECT tr)) { ClearSnapAndHide(); return; }
        CancelSnapSmoothingIfTargetMoved(tr);
        if (dragging && ComputeSeatDesktop(out float px, out _))
        {
            float ww = Mathf.Max(1, tr.Right - tr.Left);
            snapFraction = Mathf.Clamp01((px - tr.Left) / ww);
        }
        RecalibrateIfScaleChanged(tr);
        PinToTarget(tr); SetTopMost(true);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            if (cachedWindows[i].hwnd != snappedHWND) continue;
            RECT tr = cachedWindows[i].rect;
            CancelSnapSmoothingIfTargetMoved(tr);
            if (dragging && ComputeSeatDesktop(out float px, out _))
            {
                float ww = Mathf.Max(1, tr.Right - tr.Left);
                snapFraction = Mathf.Clamp01((px - tr.Left) / ww);
            }
            RecalibrateIfScaleChanged(tr);
            PinToTarget(tr);
            return;
        }
        ClearSnapAndHide();
#endif
    }

    void RecalibrateIfScaleChanged(RECT tr)
    {
        if ((transform.lossyScale - _prevLossyScale).sqrMagnitude < 1e-6f) return;
        _prevLossyScale = transform.lossyScale;
        int trTop    = tr.Top;
        int trBottom = tr.Bottom;
        int snapEdgeY = (windowSitEdge == "down") ? trBottom : trTop;
        CalibrateSeatAnchorToDesktopY(snapEdgeY + seatOffsetPx);
        _snapSmoothingActive = false;
        _snapVelX = _snapVelY = 0f;
    }
    void PinToTarget(RECT r)
    {
        if (!ComputeSeatDesktop(out float px, out float py)) return;
        int left = r.Left, right = r.Right;
        int rTop    = r.Top;
        int rBottom = r.Bottom;
        // Determine which edge to follow: windowSitEdge="down" always uses bottom,
        // "up" always uses top, "auto" uses whichever was snapped to
        bool snappedToBottom;
        if (windowSitEdge == "down")
            snappedToBottom = true;
        else if (windowSitEdge == "up")
            snappedToBottom = false;
        else
            snappedToBottom = Mathf.Abs(_snappedEdgeY - rBottom) < Mathf.Abs(_snappedEdgeY - rTop);
        int top = snappedToBottom ? rBottom : rTop;
        float desiredPX = left + snapFraction * Mathf.Max(1, right - left);
        float desiredPY = top + seatOffsetPx;
        int dx = Mathf.RoundToInt(desiredPX - px);
        int dy = Mathf.RoundToInt(desiredPY - py);

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        var uwc = Kirurobo.UniWindowController.current;
        if (uwc == null) return;
        var upos = uwc.windowPosition;  // AppKit bottom-left origin
        var usize = uwc.windowSize;
        float mainH = MacWindowHelper.GetGlobalScreenHeight();
        int urLeft = (int)upos.x;
        int urTop = Mathf.RoundToInt(mainH - (upos.y + usize.y));  // top edge, Y-down
        int w = (int)usize.x, h = (int)usize.y;
        int targetX = urLeft + dx, targetTop = urTop + dy;
        if (!_snapSmoothingActive || !enableSnapSmoothing)
        {
            if (dx != 0 || dy != 0) uwc.windowPosition = new Vector2(targetX, mainH - targetTop - h);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nextX = Mathf.SmoothDamp(urLeft, targetX, ref _snapVelX, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        float nextTop = Mathf.SmoothDamp(urTop, targetTop, ref _snapVelY, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        if (controller != null && controller.isDragging)
        {
            float predictedSeatY = py + (nextTop - urTop);
            float afterError = predictedSeatY - desiredPY;
            if (afterError > 0f)
            {
                float maxStep = snapSmoothingMaxSpeed * dt;
                float need = Mathf.Max(0f, afterError - 1f);
                nextTop -= Mathf.Min(maxStep, need);
            }
        }
        int nx = Mathf.RoundToInt(nextX), nyTop = Mathf.RoundToInt(nextTop);
        if (Mathf.Abs(targetX - nx) <= 1 && Mathf.Abs(targetTop - nyTop) <= 1) { nx = targetX; nyTop = targetTop; _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
        if (nx != urLeft || nyTop != urTop) uwc.windowPosition = new Vector2(nx, mainH - nyTop - h);
#else
        GetWindowRect(unityHWND, out RECT ur);
        int w = ur.Right - ur.Left, h = ur.Bottom - ur.Top;
        int targetX = ur.Left + dx, targetY = ur.Top + dy;

        if (!_snapSmoothingActive || !enableSnapSmoothing)
        {
            if (dx != 0 || dy != 0) MoveWindow(unityHWND, targetX, targetY, w, h, true);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float nextX = Mathf.SmoothDamp(ur.Left, targetX, ref _snapVelX, snapSmoothingTime, snapSmoothingMaxSpeed, dt);
        float nextY = Mathf.SmoothDamp(ur.Top, targetY, ref _snapVelY, snapSmoothingTime, snapSmoothingMaxSpeed, dt);

        if (controller != null && controller.isDragging)
        {
            float predictedSeatY = py + (nextY - ur.Top);
            float afterError = predictedSeatY - desiredPY;
            if (afterError > 0f)
            {
                float maxStep = snapSmoothingMaxSpeed * dt;
                float need = Mathf.Max(0f, afterError - 1f);
                nextY -= Mathf.Min(maxStep, need);
            }
        }

        int nx = Mathf.RoundToInt(nextX), ny = Mathf.RoundToInt(nextY);
        if (Mathf.Abs(targetX - nx) <= 1 && Mathf.Abs(targetY - ny) <= 1) { nx = targetX; ny = targetY; _snapSmoothingActive = false; _snapVelX = _snapVelY = 0f; }
        if (nx != ur.Left || ny != ur.Top) MoveWindow(unityHWND, nx, ny, w, h, true);
#endif
    }
    bool IsStillNearSnappedWindow()
    {
        if (_latch > 0) { _latch--; return true; }
        if (_guard > 0) { _guard--; return true; }

        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var win = cachedWindows[i];
            if (win.hwnd != snappedHWND) continue;
            if (!ComputeZoneDesktop(out float px, out float py)) return true;
            int left = win.rect.Left, right = win.rect.Right;
            int top    = win.rect.Top;
            int bottom = win.rect.Bottom;
            bool hitHoriz = px >= left && px <= right;
            int vBandCheck = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
            bool hitTop    = windowSitEdge != "down" && Mathf.Abs(py - top)    <= vBandCheck;
            bool hitBottom = windowSitEdge != "up"   && Mathf.Abs(py - bottom) <= vBandCheck;
            bool hitVert = hitTop || hitBottom;
            if (!hitHoriz || !hitVert) return false;

            if (controller.isDragging && animator.GetBool("isWindowSit"))
            {
#if UNITY_STANDALONE_WIN
                Kirurobo.WinApi.POINT cp;
                if (!Kirurobo.WinApi.GetCursorPos(out cp)) return true;
                int vBand = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
                if (Mathf.Abs(cp.y - _snapCursorY) > vBand) return false;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                if (!MacWindowHelper.TryGetCursorPosition(out Vector2Int macCp2))
                    return true;
                int vBand = Mathf.Max(unsnapVerticalBand, ScaledProbeRadiusI());
                if (Mathf.Abs(macCp2.y - _snapCursorY) > vBand) return false;
#endif
            }
            return true;
        }
        return false;
    }
    bool IsOccludedByHigherWindowsAtPoint(IntPtr hwnd, int x, int y)
    {
#if UNITY_STANDALONE_WIN
        IntPtr h = GetWindow(hwnd, GW_HWNDPREV);
        while (h != IntPtr.Zero)
        {
            if (h == unityHWND || IsSameProcessWindow(h)) { h = GetWindow(h, GW_HWNDPREV); continue; }
            if (!IsWindowVisible(h) || IsCloaked(h) || !GetWindowRect(h, out RECT r)) { h = GetWindow(h, GW_HWNDPREV); continue; }
            bool hit = x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
            if (!hit) { h = GetWindow(h, GW_HWNDPREV); continue; }
            classNameBuffer.Clear(); GetClassName(h, classNameBuffer, classNameBuffer.Capacity);
            if (IsEffectivelyTransparentWindow(h, classNameBuffer) || IsLikelyUniWindowMascot(h, classNameBuffer)) { h = GetWindow(h, GW_HWNDPREV); continue; }

            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TRANSPARENT) != 0) { h = GetWindow(h, GW_HWNDPREV); continue; }
            if ((ex & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags))
            {
                if ((flags & LWA_ALPHA) != 0 && alpha <= 8) { h = GetWindow(h, GW_HWNDPREV); continue; }
            }
            return true;
        }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // cachedWindows is front-to-back; any normal window in front of the
        // candidate that covers the point occludes it. Skip menu-bar / status
        // layers (>= 25) and window levels that float above everything.
        for (int i = 0; i < cachedWindows.Count; i++)
        {
            var w = cachedWindows[i];
            if (w.hwnd == hwnd) break;
            if (w.layer != 0) continue;
            var r = w.rect;
            if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom) return true;
        }
#endif
        return false;
    }
    Vector3 GetSeatWorldCurrent()
    {
        if (!seatCalibrated) return GetHipWorld();
        float yFrac = Mathf.Clamp(seatNormY + windowSitYOffset, -0.5f, 1.5f);
        float yLocal = boundsMinSnapLocal.y + yFrac * boundsSizeSnapLocal.y;
        Vector3 localSeat = new Vector3(seatLocalAtSnap.x, yLocal, seatLocalAtSnap.z);
        return transform.localToWorldMatrix.MultiplyPoint3x4(localSeat);
    }
    Vector3 SeatWorldGuess()
    {
        if (animator != null && animator.isHuman)
        {
            Vector3 pelvis = boneHips != null ? boneHips.position : transform.position;
            Vector3 thighAvg = (boneLUL != null && boneRUL != null) ? (boneLUL.position + boneRUL.position) * 0.5f : pelvis;
            float headY = boneHead != null ? boneHead.position.y : pelvis.y + 0.5f;
            float footY = pelvis.y;
            if (boneLFoot != null) footY = boneLFoot.position.y;
            if (boneRFoot != null) footY = Mathf.Min(footY, boneRFoot.position.y);
            float h = Mathf.Max(0.1f, headY - footY);
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            float down = Mathf.Clamp(h * 0.15f, 0.01f, h * 0.5f);
#else
            float down = Mathf.Clamp(h * 0.12f, 0.01f, h * 0.5f);
#endif
            return thighAvg + Vector3.down * down;
        }
        Bounds b = GetCombinedWorldBounds();
        return new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.center.y, 0.2f), b.center.z);
    }
    Bounds GetCombinedWorldBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool has = false;
        if (!_skinnedCached || skinned == null || skinned.Length == 0) { skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true); _skinnedCached = true; }
        if (skinned != null)
        {
            for (int i = 0; i < skinned.Length; i++)
            {
                var s = skinned[i];
                if (s == null || !s.enabled) continue;
                if (!has) { b = s.bounds; has = true; } else b.Encapsulate(s.bounds);
            }
        }
        if (!has)
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
        }
        if (!has) b = new Bounds(transform.position, Vector3.one * 0.5f);
        return b;
    }
    Bounds WorldBoundsToRootLocal(Bounds wb)
    {
        Matrix4x4 inv = transform.worldToLocalMatrix;
        Vector3 min = wb.min, max = wb.max;
        Vector3[] c = new Vector3[8];
        c[0] = inv.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z));
        c[1] = inv.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z));
        c[2] = inv.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z));
        c[3] = inv.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z));
        c[4] = inv.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z));
        c[5] = inv.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z));
        c[6] = inv.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z));
        c[7] = inv.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z));
        Vector3 lmin = c[0], lmax = c[0];
        for (int i = 1; i < 8; i++) { lmin = Vector3.Min(lmin, c[i]); lmax = Vector3.Max(lmax, c[i]); }
        return new Bounds((lmin + lmax) * 0.5f, lmax - lmin);
    }
    void UpdateOccluderQuadsFrameSync()
    {
#if UNITY_STANDALONE_WIN
        if (_occluderSharedMat == null || targetCamera == null || snappedHWND == IntPtr.Zero) { SetTargetQuadActive(false); SetOtherQuadsActive(0); return; }
        if (!_haveUnityCli && !GetUnityClientRect(out _lastUnityCli)) { SetTargetQuadActive(false); SetOtherQuadsActive(0); return; }
        RECT uCli = _lastUnityCli;
        Rect unityClient = new Rect(uCli.Left, uCli.Top, uCli.Right - uCli.Left, uCli.Bottom - uCli.Top);

        if (snappedHWND != unityHWND && GetWindowRect(snappedHWND, out RECT tr))
        {
            // Absolute barrier BELOW the seat line. On the TOP edge it spans only
            // the snapped window's horizontal extent (so it doesn't occlude empty
            // wallpaper past the window's sides); on the BOTTOM edge the character
            // dangles below the window, so the barrier keeps spanning the whole
            // screen width to avoid a broken cliff at the window's side edges.
            int seatLineY = GetSeatLineDesktopY(tr);
            Rect tInter;
            if (IsSnappedToBottom(tr))
                tInter = Intersect(new Rect(unityClient.xMin, seatLineY, unityClient.width, unityClient.yMax - seatLineY), unityClient);
            else
                tInter = Intersect(new Rect(tr.Left, seatLineY, tr.Right - tr.Left, unityClient.yMax - seatLineY), unityClient);
            if (tInter.width > 0 && tInter.height > 0)
            {
                EnsureTargetQuad();
                float z = autoScaleTargetZ ? GetVerticalPlaneDepth() : targetQuadZOffset;
                UpdateQuadLocalFast(tInter, unityClient, z, targetMesh, targetQuadGO, verts4);
                SetTargetQuadActive(true);
            }
            else SetTargetQuadActive(false);
        }
        else SetTargetQuadActive(false);

        int outCount = 0;
        for (int i = 0; i < activeOccluders.Count && outCount < maxOtherQuads; i++)
        {
            var w = activeOccluders[i];
            if (!GetWindowRect(w.hwnd, out RECT wrct)) continue;
            Rect inter = Intersect(new Rect(wrct.Left, wrct.Top, wrct.Right - wrct.Left, wrct.Bottom - wrct.Top), unityClient);
            if (inter.width <= 0 || inter.height <= 0) continue;
            EnsureOtherQuad(outCount);
            UpdateQuadLocalFast(inter, unityClient, othersQuadZOffset, otherMeshes[outCount], otherQuadGOs[outCount], verts4Other);
            outCount++;
        }
        SetOtherQuadsActive(outCount);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // Same window-occlusion quads as Windows, fed from the cached CGWindowList
        // rects (the Win32 GetWindowRect calls above have no macOS equivalent).
        if (_occluderSharedMat == null || targetCamera == null || snappedHWND == IntPtr.Zero) { SetTargetQuadActive(false); SetOtherQuadsActive(0); return; }
        if (!_haveUnityCli && !GetUnityClientRect(out _lastUnityCli)) { SetTargetQuadActive(false); SetOtherQuadsActive(0); return; }
        RECT uCli2 = _lastUnityCli;
        Rect unityClient2 = new Rect(uCli2.Left, uCli2.Top, uCli2.Right - uCli2.Left, uCli2.Bottom - uCli2.Top);

        if (TryGetCachedRect(snappedHWND, out RECT tr2))
        {
            // Same edge-dependent barrier as Windows: top edge spans the window's
            // width, bottom edge spans the whole screen width.
            int seatLineY2 = GetSeatLineDesktopY(tr2);
            Rect tInter2;
            if (IsSnappedToBottom(tr2))
                tInter2 = Intersect(new Rect(unityClient2.xMin, seatLineY2, unityClient2.width, unityClient2.yMax - seatLineY2), unityClient2);
            else
                tInter2 = Intersect(new Rect(tr2.Left, seatLineY2, tr2.Right - tr2.Left, unityClient2.yMax - seatLineY2), unityClient2);
            if (tInter2.width > 0 && tInter2.height > 0)
            {
                EnsureTargetQuad();
                float z2 = autoScaleTargetZ ? GetVerticalPlaneDepth() : targetQuadZOffset;
                UpdateQuadLocalFast(tInter2, unityClient2, z2, targetMesh, targetQuadGO, verts4);
                SetTargetQuadActive(true);
            }
            else SetTargetQuadActive(false);
        }
        else SetTargetQuadActive(false);

        int outCount2 = 0;
        for (int i = 0; i < activeOccluders.Count && outCount2 < maxOtherQuads; i++)
        {
            var w = activeOccluders[i];
            Rect inter2 = Intersect(new Rect(w.rect.Left, w.rect.Top, w.rect.Right - w.rect.Left, w.rect.Bottom - w.rect.Top), unityClient2);
            if (inter2.width <= 0 || inter2.height <= 0) continue;
            EnsureOtherQuad(outCount2);
            UpdateQuadLocalFast(inter2, unityClient2, othersQuadZOffset, otherMeshes[outCount2], otherQuadGOs[outCount2], verts4Other);
            outCount2++;
        }
        SetOtherQuadsActive(outCount2);
#endif
    }
    float GetAutoTargetZ()
    {
        float s = Mathf.Max(0.0001f, transform.lossyScale.y);
        float z = targetZBase + (s - targetZRefScale) * targetZSensitivity;
        return Mathf.Clamp(z, targetZMin, targetZMax);
    }
    // Camera-space depth of the "cliff" occluder plane. The plane sits at the
    // character's seat depth and extends down from the seat line, so the parts of
    // the character below the seat line that are deeper than it (the back of the
    // body / long hair) get occluded while the parts in front (dangling legs,
    // torso) stay visible - a 3D ledge look instead of a full silhouette cutout.
    float GetVerticalPlaneDepth()
    {
        if (targetCamera == null) return GetAutoTargetZ();
        Vector3 seat = GetSeatWorldCurrent();
        Vector3 sp = targetCamera.WorldToScreenPoint(seat);
        if (sp.z < 0.01f) return GetAutoTargetZ();
        return sp.z + windowSitCliffOffset;
    }
    // Whether the character is sitting on the window's bottom edge (true) or top
    // edge (false), mirroring the edge choice in PinToTarget.
    bool IsSnappedToBottom(RECT r)
    {
        if (windowSitEdge == "down") return true;
        if (windowSitEdge == "up") return false;
        return Mathf.Abs(_snappedEdgeY - r.Bottom) < Mathf.Abs(_snappedEdgeY - r.Top);
    }
    // Desktop Y of the horizontal line the character sits on (the snapped edge
    // plus seat offset). The absolute barrier applies only BELOW this line, so
    // the upper body stays fully visible and the cliff occlusion exists below it.
    int GetSeatLineDesktopY(RECT r)
    {
        return (IsSnappedToBottom(r) ? r.Bottom : r.Top) + Mathf.RoundToInt(seatOffsetPx);
    }
    void EnsureOccluderRoot()
    {
        if (occluderRoot != null) return;
        var root = new GameObject("OccluderRoot");
        root.layer = targetCamera != null ? targetCamera.gameObject.layer : 0;
        root.transform.SetParent(targetCamera != null ? targetCamera.transform : null, false);
        occluderRoot = root.transform;
    }
    void EnsureTargetQuad()
    {
        if (targetQuadGO != null) return;
        targetQuadGO = new GameObject("TargetWindowQuad");
        targetQuadGO.layer = targetCamera.gameObject.layer;
        targetQuadGO.transform.SetParent(occluderRoot, false);
        var mf = targetQuadGO.AddComponent<MeshFilter>();
        var mr = targetQuadGO.AddComponent<MeshRenderer>();
        targetMesh = new Mesh(); targetMesh.MarkDynamic();
        mf.sharedMesh = targetMesh;
        mr.sharedMaterial = _occluderSharedMat;
        targetMesh.vertices = verts4;
        targetMesh.triangles = TRI;
        targetMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        targetQuadGO.SetActive(false);
    }
    void EnsureOtherQuad(int index)
    {
        while (otherQuadGOs.Count <= index)
        {
            var go = new GameObject("OtherWindowQuad_" + otherQuadGOs.Count);
            go.layer = targetCamera.gameObject.layer;
            go.transform.SetParent(occluderRoot, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh(); mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = _occluderSharedMat;
            mesh.vertices = verts4Other;
            mesh.triangles = TRI;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
            otherQuadGOs.Add(go);
            otherMeshes.Add(mesh);
            go.SetActive(false);
        }
    }
    void SetTargetQuadActive(bool on) { if (targetQuadGO != null && targetQuadGO.activeSelf != on) targetQuadGO.SetActive(on); }
    void SetOtherQuadsActive(int activeCount)
    {
        for (int i = 0; i < otherQuadGOs.Count; i++)
        {
            bool on = i < activeCount;
            if (otherQuadGOs[i].activeSelf != on) otherQuadGOs[i].SetActive(on);
        }
    }
    void CleanupOccluderArtifacts()
    {
        if (targetQuadGO) { Destroy(targetQuadGO); targetQuadGO = null; targetMesh = null; }
        for (int i = 0; i < otherQuadGOs.Count; i++) if (otherQuadGOs[i]) Destroy(otherQuadGOs[i]);
        otherQuadGOs.Clear(); otherMeshes.Clear(); activeOccluders.Clear(); _haveUnityCli = false;
        if (_occluderSharedMat) { Destroy(_occluderSharedMat); _occluderSharedMat = null; }
    }
    bool IsLikelyUniWindowMascot(IntPtr hWnd, System.Text.StringBuilder cls)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        long st = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
        bool layered = (ex & WS_EX_LAYERED) != 0;
        bool toolOrNoAct = ((ex & WS_EX_TOOLWINDOW) != 0) || ((ex & WS_EX_NOACTIVATE) != 0);
        bool clickThrough = (ex & WS_EX_TRANSPARENT) != 0;
        bool translucent = false;
        if (layered && GetLayeredWindowAttributes(hWnd, out _, out byte alpha, out uint flags)) translucent = ((flags & LWA_ALPHA) != 0 && alpha < 255) || ((flags & LWA_COLORKEY) != 0);
        int titleLen = GetWindowTextLength(hWnd);
        if (layered && (toolOrNoAct || clickThrough || translucent) && (st & WS_CAPTION) == 0 && titleLen <= 1) return true;
        if (layered && (toolOrNoAct || clickThrough || translucent) && SBEq(cls, "UnityWndClass")) return true;
        return false;
    }
    bool IsAboveInZOrder(IntPtr a, IntPtr b)
    {
        if (a == b || a == IntPtr.Zero || b == IntPtr.Zero) return false;
        IntPtr h = b;
        for (int i = 0; i < 2048 && h != IntPtr.Zero; i++)
        {
            h = GetWindow(h, GW_HWNDPREV);
            if (h == a) return true;
        }
        return false;
    }
    int FindCachedIndex(IntPtr hwnd)
    {
        for (int i = 0; i < cachedWindows.Count; i++)
            if (cachedWindows[i].hwnd == hwnd) return i;
        return -1;
    }
    bool TryGetCachedRect(IntPtr hwnd, out RECT r)
    {
        int i = FindCachedIndex(hwnd);
        if (i < 0) { r = new RECT(); return false; }
        r = cachedWindows[i].rect;
        return true;
    }
    static bool RectsOverlap(RECT a, RECT b)
    {
        return a.Left < b.Right && b.Left < a.Right &&
               a.Top < b.Bottom && b.Top < a.Bottom;
    }
    void UpdateQuadLocalFast(Rect desktopRect, Rect unityDesktopRect, float zOffset, Mesh mesh, GameObject go, Vector3[] buffer)
    {
        float clientW = Mathf.Max(1f, unityDesktopRect.width);
        float clientH = Mathf.Max(1f, unityDesktopRect.height);
        float pxW = Mathf.Max(1, targetCamera.pixelWidth);
        float pxH = Mathf.Max(1, targetCamera.pixelHeight);
        float sx0 = (desktopRect.xMin - unityDesktopRect.xMin) * (pxW / clientW);
        float sx1 = (desktopRect.xMax - unityDesktopRect.xMin) * (pxW / clientW);
        float sy0 = pxH - (desktopRect.yMax - unityDesktopRect.yMin) * (pxH / clientH);
        float sy1 = pxH - (desktopRect.yMin - unityDesktopRect.yMin) * (pxH / clientH);
        float z = targetCamera.nearClipPlane + zOffset;

        Vector3 blW = targetCamera.ScreenToWorldPoint(new Vector3(sx0, sy0, z));
        Vector3 tlW = targetCamera.ScreenToWorldPoint(new Vector3(sx0, sy1, z));
        Vector3 trW = targetCamera.ScreenToWorldPoint(new Vector3(sx1, sy1, z));
        Vector3 brW = targetCamera.ScreenToWorldPoint(new Vector3(sx1, sy0, z));
        buffer[0] = targetCamera.transform.InverseTransformPoint(blW);
        buffer[1] = targetCamera.transform.InverseTransformPoint(tlW);
        buffer[2] = targetCamera.transform.InverseTransformPoint(trW);
        buffer[3] = targetCamera.transform.InverseTransformPoint(brW);

        mesh.vertices = buffer;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
    }
    static Rect Intersect(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Max(Mathf.Min(a.yMax, b.yMax), yMin);
        if (xMax <= xMin || yMax <= yMin) return new Rect(0, 0, 0, 0);
        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }
    Vector2 GetUnityWindowPosition()
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        var uwc = Kirurobo.UniWindowController.current;
        if (uwc == null) return Vector2.zero;
        var pos = uwc.windowPosition;
        var size = uwc.windowSize;
        return new Vector2(pos.x, MacWindowHelper.GetGlobalScreenHeight() - (pos.y + size.y));  // top edge, Y-down
#else
        GetWindowRect(unityHWND, out RECT r); return new Vector2(r.Left, r.Top);
#endif
    }
    bool GetUnityClientRect(out RECT r)
    {
        r = new RECT();
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        if (!MacWindowHelper.TryGetClientRect(out RectInt client))
            return false;
        r.Left = client.x; r.Top = client.y;
        r.Right = client.x + client.width; r.Bottom = client.y + client.height;
        return true;
#else
        if (!GetClientRect(unityHWND, out RECT client)) return false;
        POINT p = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(unityHWND, ref p)) return false;
        r.Left = p.X; r.Top = p.Y; r.Right = p.X + client.Right; r.Bottom = p.Y + client.Bottom;
        return true;
#endif
    }
    void SetTopMost(bool en)
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        var uwc = Kirurobo.UniWindowController.current;
        if (uwc != null) uwc.isTopmost = en;
        if (en) MacWindowHelper.BringSelfToFront();
#else
        SetWindowPos(unityHWND, en ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#endif
    }

    bool IsWindowMaximized(IntPtr hwnd)
    {
        WINDOWPLACEMENT placement = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
        if (GetWindowPlacement(hwnd, ref placement)) return placement.showCmd == SW_MAXIMIZE;
        return false;
    }
    bool IsWindowFullscreen(WindowEntry win)
    {
        int width = win.rect.Right - win.rect.Left;
        int height = win.rect.Bottom - win.rect.Top;
        int screenWidth = Display.main.systemWidth;
        int screenHeight = Display.main.systemHeight;
        int tolerance = 2;
        return Mathf.Abs(width - screenWidth) <= tolerance && Mathf.Abs(height - screenHeight) <= tolerance;
    }
    public void ForceExitWindowSitting() { ClearSnapAndHide(); }

    void OnDrawGizmos()
    {
        if (!showProbeGizmo || targetCamera == null) return;
        Vector3 hip = GetProbeWorld();
        Vector3 sp = targetCamera.WorldToScreenPoint(hip);
        if (sp.z <= 0f) return;
        Vector3 sp2 = sp + new Vector3(ScaledProbeRadiusF(), 0f, 0f);
        Vector3 w1 = targetCamera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, sp.z));
        Vector3 w2 = targetCamera.ScreenToWorldPoint(new Vector3(sp2.x, sp2.y, sp2.z));
        float worldR = Vector3.Distance(w1, w2);
        Gizmos.color = probeGizmoColor; Gizmos.DrawWireSphere(hip, worldR);
        Vector3 spg2 = sp + new Vector3(ScaledGuardRadiusF(), 0f, 0f);
        Vector3 wg2a = targetCamera.ScreenToWorldPoint(new Vector3(spg2.x, spg2.y, spg2.z));
        float worldRGuard = Vector3.Distance(w1, wg2a);
        Gizmos.color = probeGuardGizmoColor; Gizmos.DrawWireSphere(hip, worldRGuard);
    }
    public void SetBaseOffset(float v) { }
    public void SetBaseScale(float v) { }
    public float GetBaseOffset() => 0f; public float GetBaseScale() => 1f; public float GetScaleCompPx() => 0f;
    static bool SBEq(System.Text.StringBuilder sb, string s)
    {
        if (sb.Length != s.Length) return false;
        for (int i = 0; i < s.Length; i++) if (sb[i] != s[i]) return false;
        return true;
    }
    static bool SBStartsWith(System.Text.StringBuilder sb, string s)
    {
        if (sb.Length < s.Length) return false;
        for (int i = 0; i < s.Length; i++) if (sb[i] != s[i]) return false;
        return true;
    }
    static bool SBContains(System.Text.StringBuilder sb, string s)
    {
        int n = sb.Length, m = s.Length;
        if (m == 0) return true;
        for (int i = 0; i <= n - m; i++)
        {
            int j = 0;
            while (j < m && sb[i + j] == s[j]) j++;
            if (j == m) return true;
        }
        return false;
    }

#if UNITY_STANDALONE_WIN
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int length; public int flags; public int showCmd; public POINT ptMinPosition; public POINT ptMaxPosition; public RECT rcNormalPosition; }
    const int SW_MAXIMIZE = 3;
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    const int DWMWA_CLOAKED = 14;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    const uint GW_HWNDPREV = 3;
    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const int WS_CAPTION = 0x00C00000;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint LWA_COLORKEY = 0x00000001;
    const uint LWA_ALPHA = 0x00000002;
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const uint GA_ROOT = 2;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
#else
    static uint GetCurrentProcessId() => 0;
    static bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT p) => false;
    struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }
    const int SW_MAXIMIZE = 3;
    static bool IsIconic(IntPtr hWnd) => false;
    static int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size) { val = 0; return 0; }
    const int DWMWA_CLOAKED = 14;
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int n) => IntPtr.Zero;
    static bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags) { key = 0; alpha = 255; flags = 0; return false; }
    static IntPtr GetWindow(IntPtr hWnd, uint cmd) => IntPtr.Zero;
    const uint GW_HWNDPREV = 3;
    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const int WS_CAPTION = 0x00C00000;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint LWA_COLORKEY = 0x00000001;
    const uint LWA_ALPHA = 0x00000002;
    static int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max) => 0;
    static IntPtr GetAncestor(IntPtr hwnd, uint flags) => IntPtr.Zero;
    static bool GetWindowRect(IntPtr hWnd, out RECT r) { r = new RECT(); return false; }
    static bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint) => false;
    static bool IsWindowVisible(IntPtr hWnd) => false;
    static uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid) { pid = 0; return 0; }
    static bool EnumWindows(EnumWindowsProc fn, IntPtr lp) => false;
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    static bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags) => false;
    static IntPtr GetParent(IntPtr hWnd) => IntPtr.Zero;
    static int GetWindowTextLength(IntPtr hWnd) => 0;
    static bool GetClientRect(IntPtr hWnd, out RECT r) { r = new RECT(); return false; }
    static bool ClientToScreen(IntPtr hWnd, ref POINT p) => false;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const uint GA_ROOT = 2;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
#endif
    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }
    struct WindowEntry { public IntPtr hwnd; public RECT rect; public bool isTaskbar; public int layer; }
}
