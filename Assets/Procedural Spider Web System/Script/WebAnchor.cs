using UnityEngine;

namespace ProceduralSpiderWebSystem
{
    /// <summary>
    /// Marks a point that can be used by ProceduralSpiderWeb as a fixed connection point.
    /// Add this component to hooks, stones, beams, cave walls, or any other surface detail.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WebAnchor : MonoBehaviour
    {
        public enum AnchorRole
        {
            Structural,
            Decorative,
            BrokenEnd
        }

        [Tooltip("How this anchor should be treated by web generators.")]
        public AnchorRole role = AnchorRole.Structural;

        [Tooltip("Higher values make this anchor more likely to be kept when there are too many candidates.")]
        [Min(0.01f)]
        public float strength = 1f;

        [Tooltip("Small random offset radius used to avoid unnaturally perfect straight-line attachment.")]
        [Min(0f)]
        public float attachmentNoise = 0.035f;

        [Tooltip("Optional surface normal. If zero, the generator estimates it from nearby geometry.")]
        public Vector3 localSurfaceNormal = Vector3.forward;

        /// <summary>
        /// Returns the anchor normal in world space.
        /// </summary>
        public Vector3 WorldNormal
        {
            get
            {
                if (localSurfaceNormal.sqrMagnitude < 0.0001f)
                {
                    return transform.forward;
                }

                return transform.TransformDirection(localSurfaceNormal.normalized);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = role == AnchorRole.Structural
                ? new Color(0.75f, 0.95f, 1f, 0.95f)
                : new Color(1f, 0.8f, 0.45f, 0.9f);

            Gizmos.DrawSphere(transform.position, 0.045f);
            Gizmos.DrawLine(transform.position, transform.position + WorldNormal * 0.25f);
        }
    }
}
