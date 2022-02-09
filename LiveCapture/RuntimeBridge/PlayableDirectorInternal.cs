using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;

namespace Unity.LiveCapture.Internal
{
    static class PlayableDirectorInternal
    {
        public static void ResetFrameTiming()
        {
            // awersome unity devs thing that it is a great idea to let access for internal methods to their modules
            // so when I migrate it to separate nuget dll i have to use a reflection to call this.
            var playableDirectorType = typeof(PlayableDirector);
            MethodInfo staticMethod = playableDirectorType.GetMethod("ResetFrameTiming", BindingFlags.Static | BindingFlags.Public);
            staticMethod.Invoke(null, null);
        }
    }
}
