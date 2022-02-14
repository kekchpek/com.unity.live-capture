using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.LiveCapture.CompanionApp;
using UnityEditor;
using UnityEditorInternal;

namespace LiveCaptureEditor.CompanionApp
{
    [InitializeOnLoad]
    class ClientMappingDatabaseManager : Editor
    {
        const string AssetPath = "UserSettings/LiveCapture/ClientMappingDatabase.asset";

        static ClientMappingDatabaseManager()
        {
        }

        private static void OnBeforeCreateInstance()
        {
            InternalEditorUtility.LoadSerializedFileAndForget(AssetPath);
        }

        private static void OnTryToSave(ClientMappingDatabase instance)
        {
            var directoryName = Path.GetDirectoryName(AssetPath);

            if (!Directory.Exists(directoryName))
                Directory.CreateDirectory(directoryName);

            InternalEditorUtility.SaveToSerializedFileAndForget(new[] { instance }, AssetPath, true);
        }

        // A static destructor workaround
        private static readonly Destructor Finalise = new Destructor();
        private sealed class Destructor
        {
            ~Destructor()
            {
                ClientMappingDatabase.BeforeCreateInstance -= OnBeforeCreateInstance;
                ClientMappingDatabase.TryToSave -= OnTryToSave;
            }
        }
    }
}
