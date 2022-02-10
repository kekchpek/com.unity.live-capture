using Unity.LiveCapture.ARKitFaceCapture.DefaultMapper;
using UnityEditor;
using UnityEngine;
using static Unity.LiveCapture.ARKitFaceCapture.DefaultMapper.BindingConfig;

namespace LiveCaptureEditor.ARKitFaceCapture.Extensions.DrawablesHandlers
{
    class BindingConfigHandler : IDrawableHandler
    {

        static class Contents
        {
            public static readonly GUIContent OverrideSmoothing = new GUIContent("Override Smoothing", "Whether the smoothing value set on this binding overrides the default value for the mapper.");
            public static readonly GUIContent Smoothing = new GUIContent("Smoothing", "The amount of smoothing to apply to the blend shape value. " +
                "It can help reduce jitter in the face capture, but it will also smooth out fast motions.");
            public static readonly GUIContent EvaluatorPreset = new GUIContent("Evaluator Preset", "A preset evaluation function to use. " +
                "If none is assigned, a new function must be configured for this blend shape.");
            public static readonly GUIContent Type = new GUIContent("Type", "The type of evaluation function to use when a preset is not assigned.");
        }

        /// <inheritdoc/>
        public float GetHeight(IDrawable drawable)
        {
            var bindingConfig = (BindingConfig)drawable;
            const int lines = 3;
            var height = (lines * EditorGUIUtility.singleLineHeight) + ((lines - 1) * EditorGUIUtility.standardVerticalSpacing);

            if (bindingConfig.m_EvaluatorPreset == null)
            {
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
                height += EditorGUIUtility.standardVerticalSpacing + bindingConfig.GetEvaluator().GetHeight();
            }

            return height;
        }

        /// <inheritdoc/>
        public void OnGUI(IDrawable drawable, Rect rect)
        {
            var bindingConfig = (BindingConfig)drawable;
            var line = rect;
            line.height = EditorGUIUtility.singleLineHeight;

            bindingConfig.m_OverrideSmoothing = 
                EditorGUI.Toggle(line, Contents.OverrideSmoothing, bindingConfig.m_OverrideSmoothing);
            GUIUtils.NextLine(ref line);

            using (new EditorGUI.DisabledScope(!bindingConfig.m_OverrideSmoothing))
            {
                EditorGUI.indentLevel++;
                bindingConfig.m_Smoothing = EditorGUI.Slider(line, Contents.Smoothing, bindingConfig.m_Smoothing, 0f, 1f);
                EditorGUI.indentLevel--;
            }

            GUIUtils.NextLine(ref line);
            bindingConfig.m_EvaluatorPreset = 
                EditorGUI.ObjectField(line, Contents.EvaluatorPreset, bindingConfig.m_EvaluatorPreset, typeof(EvaluatorPreset), false) as EvaluatorPreset;

            if (bindingConfig.m_EvaluatorPreset == null)
            {
                GUIUtils.NextLine(ref line);
                bindingConfig.m_Type = (Type)EditorGUI.EnumPopup(line, Contents.Type, bindingConfig.m_Type);

                var evaluatorRect = rect;
                evaluatorRect.yMin = line.yMax;
                bindingConfig.GetEvaluator().OnGUI(evaluatorRect);
            }
        }
    }
}
