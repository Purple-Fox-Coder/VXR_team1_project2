using UnityEngine;

namespace ProceduralSpiderWebSystem
{
    /// <summary>
    /// Adds subtle motion to the demo scene so generated silk catches light in play mode.
    /// </summary>
    public sealed class SpiderWebDemoAnimator : MonoBehaviour
    {
        [Tooltip("Target web generator that should be refreshed while the demo moves.")]
        public ProceduralSpiderWeb targetWeb;

        [Tooltip("Amplitude of the slow anchor motion.")]
        public float motionAmplitude = 0.045f;

        [Tooltip("Speed of the slow anchor motion.")]
        public float motionSpeed = 0.65f;

        private Vector3 startPosition;

        private void Awake()
        {
            startPosition = transform.localPosition;
        }

        private void Update()
        {
            float wave = Mathf.Sin(Time.time * motionSpeed + transform.GetSiblingIndex() * 0.7f);
            transform.localPosition = startPosition + Vector3.up * wave * motionAmplitude;
        }
    }
}
