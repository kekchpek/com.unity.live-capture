using System;
using UnityEngine;

namespace Unity.LiveCapture.ARKitFaceCapture.DefaultMapper
{
    /// <summary>
    /// An <see cref="IEvaluator"/> that uses a mostly linear evaluation function.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSimpleEvaluator", menuName = "Live Capture/ARKit Face Capture/Evaluator/Simple", order = 0)]
    class SimpleEvaluator : EvaluatorPreset
    {
        /// <inheritdoc cref="SimpleEvaluator"/>
        [Serializable]
        public class Impl : IEvaluator
        {
            /// <summary>
            /// How the evaluated blend shape value should behave as it reaches the maximum value.
            /// </summary>
            internal enum Clamping
            {
                /// <summary>
                /// Clamp using a max function.
                /// </summary>
                Hard,
                /// <summary>
                /// Clamp using a softmax function.
                /// </summary>
                Soft,
            }

            [SerializeField]
            internal float m_Multiplier = 100f;
            [SerializeField]
            internal float m_Offset = 0f;
            [SerializeField]
            internal float m_Max = 100f;
            [SerializeField]
            internal Clamping m_Clamping = Clamping.Hard;

            /// <inheritdoc />
            public float Evaluate(float value)
            {
                switch (m_Clamping)
                {
                    case Clamping.Hard:
                        return Mathf.Min((m_Multiplier * value) + m_Offset, m_Max);
                    case Clamping.Soft:
                        return SmoothClamp(m_Offset, m_Max, m_Multiplier * value);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            float SmoothClamp(float a, float b, float t)
            {
                if (Mathf.Approximately(a, b))
                    return a;

                // This doesn't use Mathf smoothstep since that uses much higher
                // precision than needed. We remap to only use the last half of the
                // smoothstep curve, as we don't want smoothing near t=0.
                t = Mathf.Clamp01((t - a) / (b - a));
                t = (0.5f * t) + 0.5f;
                return ((t * t * (6f - 4f * t)) - 1f) * (b - a) + a;
            }
        }

        [SerializeField]
        Impl m_Evaluator = new Impl();

        /// <inheritdoc />
        public override IEvaluator Evaluator => m_Evaluator;
    }
}
