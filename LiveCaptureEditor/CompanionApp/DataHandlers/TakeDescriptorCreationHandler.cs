using Unity.LiveCapture;
using Unity.LiveCapture.CompanionApp;
using UnityEditor;

namespace LiveCaptureEditor.CompanionApp.DataHandlers
{
    [InitializeOnLoad]
    class TakeDescriptorCreationHandler
    {
        static TakeDescriptorCreationHandler()
        {
            TakeDescriptor.Created += OnCreated;
        }

        static void OnCreated(TakeDescriptor descriptor, Take take)
        {
            descriptor.Guid = SerializableGuid.FromString(AssetDatabaseUtility.GetAssetGUID(take));
            if (take.Screenshot != null)
            {
                descriptor.Screenshot = SerializableGuid.FromString(AssetDatabaseUtility.GetAssetGUID(take.Screenshot));
            }
        }
    }
}
