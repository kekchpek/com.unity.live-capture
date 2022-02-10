using System;
using UnityEngine;

namespace Unity.LiveCapture.ARKitFaceCapture.DefaultMapper
{
    /// <summary>
    /// Defines how a <see cref="FaceBlendShape"/> affects a blend shape on a skinned mesh.
    /// </summary>
    [Serializable]
    class BindingConfig : IDrawable
    {
        internal enum Type
        {
            Simple,
            Curve,
        }

        [SerializeField, Tooltip("Whether the smoothing value set on this binding overrides the default value for the mapper.")]
        internal bool m_OverrideSmoothing;
        [SerializeField]
        internal float m_Smoothing = 0.1f;
        [SerializeField]
        internal EvaluatorPreset m_EvaluatorPreset = null;
        [SerializeField]
        internal Type m_Type = Type.Simple;
        [SerializeField]
        SimpleEvaluator.Impl m_SimpleEvaluator = new SimpleEvaluator.Impl();
        [SerializeField]
        CurveEvaluator.Impl m_CurveEvaluator = new CurveEvaluator.Impl();

        /// <summary>
        /// The amount of smoothing to apply to the blend shape value, with a value in the range [0, 1].
        /// </summary>
        public float Smoothing => m_Smoothing;

        /// <summary>
        /// Whether the smoothing set on this binding overrides the global value.
        /// </summary>
        public bool OverrideSmoothing => m_OverrideSmoothing;

        /// <summary>
        /// Creates a new <see cref="BindingConfig"/> instance.
        /// </summary>
        /// <param name="preset">The preset evaluation function to use, or null to use a custom function.</param>
        public BindingConfig(EvaluatorPreset preset)
        {
            m_EvaluatorPreset = preset;
        }

        /// <summary>
        /// Gets the evaluation function defined by this configuration.
        /// </summary>
        public IEvaluator GetEvaluator()
        {
            if (m_EvaluatorPreset != null)
                return m_EvaluatorPreset.Evaluator;

            switch (m_Type)
            {
                case Type.Simple:
                    return m_SimpleEvaluator;
                case Type.Curve:
                    return m_CurveEvaluator;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
