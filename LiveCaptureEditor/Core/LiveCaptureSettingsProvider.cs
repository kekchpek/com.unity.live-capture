using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditorInternal;
using System.IO;

namespace Unity.LiveCapture.Editor
{
    class LiveCaptureSettingsProvider : SettingsProvider
    {
        const string k_SettingsMenuPath = "Project/Live Capture";

        static class Contents
        {
            public static readonly GUIContent SettingMenuIcon = EditorGUIUtility.IconContent("_Popup");
            public static readonly GUIContent TakeNameFormatLabel = EditorGUIUtility.TrTextContent("Take Name Format", "The format of the file name of the output take.");
            public static readonly GUIContent AssetNameFormatLabel = EditorGUIUtility.TrTextContent("Asset Name Format", "The format of the file name of the generated assets.");
            public static readonly GUIContent ResetLabel = EditorGUIUtility.TrTextContent("Reset", "Reset to default.");
        }

        SerializedObject m_SerializedObject;
        SerializedProperty m_TakeNameFormatProp;
        SerializedProperty m_AssetNameFormatProp;

        /// <summary>
        /// Open the settings in the Project Settings.
        /// </summary>
        public static void Open()
        {
            SettingsService.OpenProjectSettings(k_SettingsMenuPath);
        }

        public LiveCaptureSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords) {}

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            m_SerializedObject = new SerializedObject(LiveCaptureSettings.Instance);
            m_TakeNameFormatProp = m_SerializedObject.FindProperty("m_TakeNameFormat");
            m_AssetNameFormatProp = m_SerializedObject.FindProperty("m_AssetNameFormat");
        }

        public override void OnDeactivate()
        {
        }

        public override void OnTitleBarGUI()
        {
            if (EditorGUILayout.DropdownButton(Contents.SettingMenuIcon, FocusType.Passive, EditorStyles.label))
            {
                var menu = new GenericMenu();
                menu.AddItem(Contents.ResetLabel, false, reset =>
                {
                    LiveCaptureSettings.Instance.Reset();
                    Save(LiveCaptureSettings.Instance);
                }, null);
                menu.ShowAsContext();
            }
        }

        public override void OnGUI(string searchContext)
        {
            m_SerializedObject.Update();

            using (var change = new EditorGUI.ChangeCheckScope())
            using (new SettingsWindowGUIScope())
            {
                EditorGUILayout.PropertyField(m_TakeNameFormatProp, Contents.TakeNameFormatLabel);
                EditorGUILayout.PropertyField(m_AssetNameFormatProp, Contents.AssetNameFormatLabel);

                if (change.changed)
                {
                    m_SerializedObject.ApplyModifiedPropertiesWithoutUndo();
                    Save(LiveCaptureSettings.Instance);
                }
            }
        }

        public override void OnFooterBarGUI()
        {
        }

        public override void OnInspectorUpdate()
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new LiveCaptureSettingsProvider(
                k_SettingsMenuPath,
                SettingsScope.Project,
                GetSearchKeywordsFromSerializedObject(new SerializedObject(LiveCaptureSettings.Instance))
            );
        }

        /// <summary>
        /// Serializes the asset to disk.
        /// </summary>
        private static void Save<T>(SettingAsset<T> settingAsset) where T : ScriptableObject
        {
            if (settingAsset == null)
            {
                Debug.LogError($"Cannot save {nameof(SettingAsset<T>)}: no instance!");
                return;
            }

            var filePath = GetFilePath<T>();

            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            var folderPath = Path.GetDirectoryName(filePath);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            InternalEditorUtility.SaveToSerializedFileAndForget(new[] { settingAsset }, filePath, true);
        }

        /// <summary>
        /// Gets the file path of the asset relative to the project root folder.
        /// </summary>
        /// <returns>The file path of the asset.</returns>
        protected static string GetFilePath<T>()
        {
            foreach (var customAttribute in typeof(T).GetCustomAttributes(true))
            {
                if (customAttribute is SettingFilePathAttribute attribute)
                {
                    return attribute.FilePath;
                }
            }
            return string.Empty;
        }
    }
}
