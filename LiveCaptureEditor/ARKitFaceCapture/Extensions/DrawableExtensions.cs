using LiveCaptureEditor.ARKitFaceCapture.Extensions.DrawablesHandlers;
using System;
using System.Collections.Generic;
using Unity.LiveCapture.ARKitFaceCapture.DefaultMapper;
using UnityEngine;

namespace LiveCaptureEditor.ARKitFaceCapture.Extensions
{
    static class DrawableExtensions
    {

        private static IDictionary<Type, IDrawableHandler> _drawablesHandlers = new Dictionary<Type, IDrawableHandler>
        {
            { typeof(BindingConfig), new BindingConfigHandler() },
            { typeof(CurveEvaluator.Impl), new CurveEvaluatorHandler() }
        };

        /// <summary>
        /// Gets the vertical space needed to draw the GUI for this instance.
        /// </summary>
        public static float GetHeight(this IDrawable drawable)
        {
            var drawableType = drawable.GetType();
            if (_drawablesHandlers.TryGetValue(drawableType, out var handler))
            {
                return handler.GetHeight(drawable);
            }
            else
            {
                Debug.LogError($"Unexpected type of drawable. Drawable type: {drawableType.FullName}");
                return default;
            }
        }

        /// <summary>
        /// Draws the inspector GUI for this instance.
        /// </summary>
        /// <param name="rect">The rect to draw the property in.</param>
        public static void OnGUI(this IDrawable drawable, Rect rect)
        {
            var drawableType = drawable.GetType();
            if (_drawablesHandlers.TryGetValue(drawableType, out var handler))
            {
                handler.OnGUI(drawable, rect);
            }
            else
            {
                Debug.LogError($"Unexpected type of drawable. Drawable type: {drawableType.FullName}");
            }
        }

    }
}
