using Unity.LiveCapture.ARKitFaceCapture.DefaultMapper;
using UnityEditor;
using UnityEngine;
using static Unity.LiveCapture.ARKitFaceCapture.DefaultMapper.SimpleEvaluator.Impl;

namespace LiveCaptureEditor.ARKitFaceCapture.Extensions.DrawablesHandlers
{
    class SimpleEvaluatorHandler : IDrawableHandler
    {
        static class Contents
        {
            public static readonly GUIContent Multiplier = new GUIContent("Multiplier", "The scaling coefficient applied to the blend shape value. " +
                "Larger values make the character more expressive.");
            public static readonly GUIContent Offset = new GUIContent("Offset", "Offsets the zero value of the blend shape. " +
                "Non-zero values will change the face's resting pose.");
            public static readonly GUIContent Max = new GUIContent("Max", "The maximum value the blend shape can reach. " +
                "Values larger than 100 allow the blend shape to go past its default extremes, while smaller values constrain them.");
            public static readonly GUIContent Clamping = new GUIContent("Clamping", "Controls how the evaluated blend shape value should behave as it reaches the maximum value. " +
                "Soft clamping will ease near the max value, while hard clamping will not.");
        }

        /// <inheritdoc/>
        public float GetHeight(IDrawable drawable)
        {
            return (4 * EditorGUIUtility.singleLineHeight) + (3 * EditorGUIUtility.standardVerticalSpacing);
        }

        /// <inheritdoc/>
        public void OnGUI(IDrawable drawable, Rect rect)
        {
            var simpleEvaluator = (SimpleEvaluator.Impl)drawable;
            rect.height = EditorGUIUtility.singleLineHeight;
            simpleEvaluator.m_Multiplier = EditorGUI.Slider(rect, Contents.Multiplier, simpleEvaluator.m_Multiplier, 0f, 200f);

            GUIUtils.NextLine(ref rect);
            simpleEvaluator.m_Offset = EditorGUI.Slider(rect, Contents.Offset, simpleEvaluator.m_Offset, -200f, 200f);

            GUIUtils.NextLine(ref rect);
            simpleEvaluator.m_Max = EditorGUI.Slider(rect, Contents.Max, simpleEvaluator.m_Max, 0f, 200f);

            GUIUtils.NextLine(ref rect);
            simpleEvaluator.m_Clamping = (Clamping)EditorGUI.EnumPopup(rect, Contents.Clamping, simpleEvaluator.m_Clamping);
        }
    }
}
