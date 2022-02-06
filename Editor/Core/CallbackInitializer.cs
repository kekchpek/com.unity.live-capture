using UnityEngine.Playables;
using UnityEditor;
using UnityEditor.Timeline;

namespace Unity.LiveCapture.Editor
{
    [InitializeOnLoad]
    static class CallbackInitializer
    {
        static CallbackInitializer()
        {
            Callbacks.SeekOccurred += SeekOccurred;
        }

        static void SeekOccurred(ISlate slate, PlayableDirector director)
        {
            if (TimelineEditor.inspectedDirector == director)
            {
                TimelineEditor.Refresh(RefreshReason.WindowNeedsRedraw);
            }
        }
    }
}
