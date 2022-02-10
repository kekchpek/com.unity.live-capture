using System;
using UnityEngine;

namespace Unity.LiveCapture.ARKitFaceCapture.DefaultMapper
{
    /// <summary>
    /// An <see cref="IEvaluator"/> that uses an animation curve to define a custom function.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCurveEvaluator", menuName = "Live Capture/ARKit Face Capture/Evaluator/Curve", order = 5)]
    class CurveEvaluator : EvaluatorPreset
    {
        /// <inheritdoc cref="CurveEvaluator"/>
        [Serializable]
        public class Impl : IEvaluator
        {
            [SerializeField]
            internal AnimationCurve m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 100f));

            /// <inheritdoc />
            public float Evaluate(float value)
            {
                return m_Curve.Evaluate(value);
            }
        }

        [SerializeField]
        Impl m_Evaluator = new Impl();

        /// <inheritdoc />
        public override IEvaluator Evaluator => m_Evaluator;
    }
}
