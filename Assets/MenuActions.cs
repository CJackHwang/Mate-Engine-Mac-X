using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class MenuEntry
{
    public GameObject menu;
    public bool blockMovement = true;
}

public class MenuActions : MonoBehaviour
{
    [Header("Menus")]
    public List<MenuEntry> menuEntries = new();

    [Header("Lock Canvas")]
    public GameObject moveCanvas;

    [Header("Radial Menu")]
    public GameObject radialMenuObject;
    public bool radialBlockMovement = true;
    public KeyCode radialMenuKey = KeyCode.F1;

    [Header("Bone Follow")]
    public bool followBone = true;
    public HumanBodyBones targetBone = HumanBodyBones.Head;
    [Range(0f, 1f)] public float followSmoothness = 0.15f;

    private static MenuActions Instance;
    private Xamin.CircleSelector radialMenu;
    private RectTransform radialRect;
    private Camera mainCam;

    private Transform modelRoot;
    private GameObject currentModel;
    private Animator currentAnimator;

    private Vector3 screenPosition;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (radialMenuObject != null)
        {
            radialMenu = radialMenuObject.GetComponent<Xamin.CircleSelector>();
            radialRect = radialMenuObject.GetComponent<RectTransform>();
        }

        var modelRootGO = GameObject.Find("Model");
        if (modelRootGO != null)
            modelRoot = modelRootGO.transform;

        mainCam = Camera.main;
    }

    void Update()
    {
        UpdateCurrentAvatar();

        if (moveCanvas != null)
            moveCanvas.SetActive(!IsMovementBlocked() && !TutorialMenu.IsActive);

        if (Input.GetKeyDown(radialMenuKey) && radialMenu != null)
        {
            if (IsAnyMenuOpen())
            {
                CloseAllMenus();
                PlayMenuCloseSound();
            }
            else
            {
                if (followBone && currentAnimator != null)
                {
                    Transform bone = currentAnimator.GetBoneTransform(targetBone);
                    if (bone != null)
                    {
                        screenPosition = mainCam.WorldToScreenPoint(bone.position);
                        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                            radialRect.parent as RectTransform,
                            screenPosition,
                            mainCam,
                            out Vector3 worldPos))
                        {
                            radialRect.position = worldPos;
                        }
                    }
                }

                radialMenu.Open();
                PlayMenuOpenSound();
            }
        }


        if (followBone && IsRadialOpen() && radialRect != null && currentAnimator != null)
        {
            Transform bone = currentAnimator.GetBoneTransform(targetBone);
            if (bone != null)
            {
                Vector3 targetScreenPos = mainCam.WorldToScreenPoint(bone.position);
                screenPosition = Vector3.Lerp(screenPosition, targetScreenPos, 1f - followSmoothness);

                if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    radialRect.parent as RectTransform,
                    screenPosition,
                    mainCam,
                    out Vector3 worldPos))
                {
                    radialRect.position = worldPos;
                }
            }
        }
    }

    private void UpdateCurrentAvatar()
    {
        if (modelRoot == null) return;

        GameObject activeModel = null;
        for (int i = 0; i < modelRoot.childCount; i++)
        {
            var child = modelRoot.GetChild(i);
            if (child.gameObject.activeInHierarchy)
            {
                activeModel = child.gameObject;
                break;
            }
        }

        if (activeModel != currentModel)
        {
            currentModel = activeModel;
            currentAnimator = currentModel != null ? currentModel.GetComponent<Animator>() : null;
        }
    }

    public static bool IsMovementBlocked()
    {
        if (Instance == null) return false;

        if (Instance.IsRadialOpen() && Instance.radialBlockMovement)
            return true;

        foreach (var entry in Instance.menuEntries)
            if (entry.menu != null && entry.menu.activeInHierarchy && entry.blockMovement)
                return true;

        return false;
    }

    public static bool IsAnyMenuOpen()
    {
        if (Instance == null) return false;
        if (Instance.IsRadialOpen()) return true;

        foreach (var entry in Instance.menuEntries)
            if (entry.menu != null && entry.menu.activeInHierarchy)
                return true;

        return false;
    }

    private bool IsRadialOpen()
    {
        return radialMenuObject != null && radialMenuObject.transform.localScale.x > 0.01f;
    }

    public void CloseAllMenus()
    {
        foreach (var entry in menuEntries)
            if (entry.menu != null && entry.menu.activeSelf)
                entry.menu.SetActive(false);

        if (IsRadialOpen())
            radialMenu?.Close();

        AvatarSettingsMenu.IsMenuOpen = false;
    }

    private void PlayMenuOpenSound()
    {
        var audio = FindFirstObjectByType<MenuAudioHandler>();
        if (audio != null)
            audio.PlayOpenSound();
    }

    private void PlayMenuCloseSound()
    {
        var audio = FindFirstObjectByType<MenuAudioHandler>();
        if (audio != null)
            audio.SendMessage("PlayCloseSound", SendMessageOptions.DontRequireReceiver);
    }
}