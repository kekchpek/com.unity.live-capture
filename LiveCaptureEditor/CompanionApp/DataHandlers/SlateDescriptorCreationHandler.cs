using Unity.LiveCapture;
using Unity.LiveCapture.CompanionApp;
using UnityEditor;

namespace LiveCaptureEditor.CompanionApp.DataHandlers
{
    [InitializeOnLoad]
    class SlateDescriptorCreationHandler
    {
        static SlateDescriptorCreationHandler()
        {
            SlateDescriptor.Created += OnCreated;
        }

        static void OnCreated(SlateDescriptor descriptor, ISlate slate)
        {
            var takes = AssetDatabaseUtility.GetAssetsAtPath<Take>(slate.Directory);
            descriptor.SelectedTake = takes.IndexOf(slate.Take);
            descriptor.IterationBase = takes.IndexOf(slate.IterationBase);
            descriptor.Takes = takes.Select(take => TakeDescriptor.Create(take)).ToArray();
        }
    }
}
