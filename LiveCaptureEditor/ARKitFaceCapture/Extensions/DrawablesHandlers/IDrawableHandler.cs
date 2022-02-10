using Unity.LiveCapture.ARKitFaceCapture.DefaultMapper;
using UnityEngine;

namespace LiveCaptureEditor.ARKitFaceCapture.Extensions.DrawablesHandlers
{
    interface IDrawableHandler
    {
        /// <summary>
        /// Gets the vertical space needed to draw the GUI for this instance.
        /// </summary>
        float GetHeight(IDrawable drawable);

        /// <summary>
        /// Draws the inspector GUI for this instance.
        /// </summary>
        /// <param name="rect">The rect to draw the property in.</param>
        void OnGUI(IDrawable drawable, Rect rect);
    }
}
