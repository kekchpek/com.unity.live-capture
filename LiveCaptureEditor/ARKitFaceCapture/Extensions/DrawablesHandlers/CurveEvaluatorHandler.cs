using System;
using System.Collections.Generic;
using System.Text;
using Unity.LiveCapture.ARKitFaceCapture.DefaultMapper;
using UnityEditor;
using UnityEngine;

namespace LiveCaptureEditor.ARKitFaceCapture.Extensions.DrawablesHandlers
{
    class CurveEvaluatorHandler : IDrawableHandler
    {
        static class Contents
        {
            public static readonly GUIContent Curve = new GUIContent("Curve", "The curve defining a custom evaluation function. It is expected to map values in the domain [0, 1].");
        }

        /// <inheritdoc/>
        public float GetHeight(IDrawable drawable)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        /// <inheritdoc/>
        public void OnGUI(IDrawable drawable, Rect rect)
        {
            var curveEvaluator = (CurveEvaluator.Impl)drawable;
            curveEvaluator.m_Curve = EditorGUI.CurveField(rect, Contents.Curve, curveEvaluator.m_Curve);
        }
    }
}
