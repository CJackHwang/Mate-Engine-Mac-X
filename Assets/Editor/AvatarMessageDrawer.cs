using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AvatarMessage))]
public class AvatarMessageDrawer : PropertyDrawer
{
    const float Pad = 4f;
    const float Gutter = 8f;
    const float LabelW = 70f;
    const float StateLabelW = 60f;
    const float OnBtnW = 72f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var keyProp = property.FindPropertyRelative("locKey");
        var stateProp = property.FindPropertyRelative("state");
        var activeProp = property.FindPropertyRelative("onActive");

        float h = EditorGUIUtility.singleLineHeight;
        bool narrow = position.width < 480f;

        if (!narrow)
        {
            float rightStart = position.x + position.width - OnBtnW;
            Rect onBtn = new Rect(rightStart, position.y, OnBtnW, h);

            float leftWidth = (onBtn.x - Gutter) - position.x;
            float col1W = Mathf.Floor(leftWidth * 0.55f);
            float col2W = leftWidth - col1W - Gutter;

            Rect kLabel = new Rect(position.x, position.y, LabelW, h);
            Rect kField = new Rect(kLabel.xMax + 4f, position.y, col1W - LabelW - 4f, h);

            Rect sLabel = new Rect(kField.xMax + Gutter, position.y, StateLabelW, h);
            Rect sField = new Rect(sLabel.xMax + 4f, position.y, col2W - StateLabelW - 4f, h);

            EditorGUI.LabelField(kLabel, "Loc Key");
            keyProp.stringValue = EditorGUI.TextField(kField, GUIContent.none, keyProp.stringValue);

            EditorGUI.LabelField(sLabel, "State");
            stateProp.stringValue = EditorGUI.TextField(sField, GUIContent.none, stateProp.stringValue);

            bool newOn = GUI.Toggle(onBtn, activeProp.boolValue, activeProp.boolValue ? "On" : "Off", EditorStyles.miniButton);
            if (newOn != activeProp.boolValue) activeProp.boolValue = newOn;
        }
        else
        {
            float y2 = position.y;
            Rect kLabel = new Rect(position.x, y2, LabelW, h);
            Rect kField = new Rect(kLabel.xMax + 4f, y2, position.width - kLabel.width - 4f, h);
            EditorGUI.LabelField(kLabel, "Loc Key");
            keyProp.stringValue = EditorGUI.TextField(kField, GUIContent.none, keyProp.stringValue);

            float y3 = y2 + h + Pad;
            Rect onBtn = new Rect(position.x + position.width - OnBtnW, y3, OnBtnW, h);

            float leftWidth = (onBtn.x - Gutter) - position.x;
            Rect sLabel = new Rect(position.x, y3, StateLabelW, h);
            Rect sField = new Rect(sLabel.xMax + 4f, y3, leftWidth - StateLabelW - 4f, h);

            EditorGUI.LabelField(sLabel, "State");
            stateProp.stringValue = EditorGUI.TextField(sField, GUIContent.none, stateProp.stringValue);

            bool newOn = GUI.Toggle(onBtn, activeProp.boolValue, activeProp.boolValue ? "On" : "Off", EditorStyles.miniButton);
            if (newOn != activeProp.boolValue) activeProp.boolValue = newOn;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float h = EditorGUIUtility.singleLineHeight;
        bool narrow = EditorGUIUtility.currentViewWidth < 520f;
        return narrow ? (h * 2f + Pad) : h + Pad;
    }
}
