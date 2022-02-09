using System.IO;
using Unity.LiveCapture;
using UnityEditorInternal;
using UnityEngine;

namespace LiveCaptureEditor.VirtualCamera.Utilities
{
    static class SettingsProviderUtils
    {

        /// <summary>
        /// Serializes the asset to disk.
        /// </summary>
        public static void Save<T>(SettingAsset<T> settingAsset) where T : ScriptableObject
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
        private static string GetFilePath<T>()
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
