using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProceduralSpiderWebSystem
{
    /// <summary>
    /// Builds a spider web from explicit WebAnchor objects or from raycast hits around the object.
    /// The generator creates line renderers at edit time and play time, so the web stays lightweight.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ProceduralSpiderWeb : MonoBehaviour
    {
        [Serializable]
        public sealed class GeneratedSegment
        {
            public Vector3 a;
            public Vector3 b;
            public float widthMultiplier = 1f;
            public bool dropletCandidate;
        }

        [Header("Anchors")]
        [Tooltip("Explicit anchors. If empty, the generator raycasts outward and uses hit points.")]
        public List<WebAnchor> explicitAnchors = new List<WebAnchor>();

        [Tooltip("Surfaces that can receive generated web anchors.")]
        public LayerMask surfaceMask = ~0;

        [Tooltip("Maximum distance used when searching for surrounding surfaces.")]
        [Min(0.25f)]
        public float maxAnchorDistance = 6f;

        [Tooltip("Maximum amount of structural anchors used by the web.")]
        [Range(5, 32)]
        public int maxAnchors = 14;

        [Tooltip("Number of raycast directions used when explicit anchors are not assigned.")]
        [Range(12, 96)]
        public int raycastSamples = 48;

        [Header("Shape")]
        [Tooltip("Number of circular rings between the center and the outer anchors.")]
        [Range(2, 16)]
        public int ringCount = 7;

        [Tooltip("Sag applied to silk lines. Higher values create an older, heavier web.")]
        [Range(0f, 0.8f)]
        public float sag = 0.18f;

        [Tooltip("Irregularity applied to each node. Keep this subtle for realistic webs.")]
        [Range(0f, 0.5f)]
        public float irregularity = 0.12f;

        [Tooltip("Random seed used for deterministic generation.")]
        public int seed = 1337;

        [Header("Rendering")]
        [Tooltip("Material assigned to generated silk line renderers.")]
        public Material silkMaterial;

        [Tooltip("Base line width in world units.")]
        [Min(0.001f)]
        public float lineWidth = 0.012f;

        [Tooltip("Prefab spawned on selected silk segments as dew or dust detail.")]
        public GameObject dropletPrefab;

        [Tooltip("How many decorative droplets may be created.")]
        [Range(0, 64)]
        public int dropletCount = 18;

        [Header("Generation")]
        [Tooltip("Regenerate automatically when the scene starts.")]
        public bool generateOnStart = true;

        [Tooltip("Regenerate automatically when values are edited in the Inspector.")]
        public bool livePreviewInEditor = true;

        [SerializeField, HideInInspector]
        private List<GameObject> generatedObjects = new List<GameObject>();

        private readonly List<Vector3> workingAnchors = new List<Vector3>();
        private readonly List<GeneratedSegment> workingSegments = new List<GeneratedSegment>();

#if UNITY_EDITOR
        private bool editorPreviewQueued;
#endif

        private void Start()
        {
            if (generateOnStart)
            {
                Generate();
            }
        }

        private void OnValidate()
        {
            maxAnchorDistance = Mathf.Max(0.25f, maxAnchorDistance);
            lineWidth = Mathf.Max(0.001f, lineWidth);

            if (!Application.isPlaying && livePreviewInEditor && isActiveAndEnabled)
            {
                QueueEditorPreviewGeneration();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Schedules preview generation after OnValidate has finished.
        /// Unity does not allow DestroyImmediate during validation callbacks.
        /// </summary>
        private void QueueEditorPreviewGeneration()
        {
            if (editorPreviewQueued)
            {
                return;
            }

            editorPreviewQueued = true;
            EditorApplication.delayCall += GenerateEditorPreview;
        }

        private void GenerateEditorPreview()
        {
            editorPreviewQueued = false;

            if (this == null || Application.isPlaying || !livePreviewInEditor || !isActiveAndEnabled)
            {
                return;
            }

            Generate();
        }
#else
        private void QueueEditorPreviewGeneration()
        {
        }
#endif

        /// <summary>
        /// Removes all generated child renderers and decorative objects.
        /// </summary>
        public void Clear()
        {
            for (int i = generatedObjects.Count - 1; i >= 0; i--)
            {
                GameObject obj = generatedObjects[i];
                if (obj == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(obj);
                }
                else
                {
                    DestroyImmediate(obj);
                }
            }

            generatedObjects.Clear();
        }

        /// <summary>
        /// Rebuilds the whole web from the current settings.
        /// </summary>
        [ContextMenu("Generate Spider Web")]
        public void Generate()
        {
            Clear();
            workingAnchors.Clear();
            workingSegments.Clear();

            CollectAnchors(workingAnchors);
            if (workingAnchors.Count < 3)
            {
                return;
            }

            SortAnchorsAroundCenter(workingAnchors, out Vector3 center, out Vector3 axisX, out Vector3 axisY);
            BuildSegments(workingAnchors, center, axisX, axisY, workingSegments);
            RenderSegments(workingSegments);
            PlaceDroplets(workingSegments);
        }

        private void CollectAnchors(List<Vector3> anchors)
        {
            System.Random random = new System.Random(seed);

            for (int i = 0; i < explicitAnchors.Count; i++)
            {
                WebAnchor anchor = explicitAnchors[i];
                if (anchor == null || anchor.role == WebAnchor.AnchorRole.Decorative)
                {
                    continue;
                }

                Vector3 noise = UnityRandomInsideSphere(random) * anchor.attachmentNoise;
                anchors.Add(anchor.transform.position + noise);
            }

            if (anchors.Count == 0)
            {
                RaycastForAnchors(anchors);
            }

            if (anchors.Count > maxAnchors)
            {
                anchors.Sort((a, b) =>
                    Vector3.Distance(transform.position, a).CompareTo(Vector3.Distance(transform.position, b)));
                anchors.RemoveRange(maxAnchors, anchors.Count - maxAnchors);
            }
        }

        private void RaycastForAnchors(List<Vector3> anchors)
        {
            Vector3 origin = transform.position;
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

            for (int i = 0; i < raycastSamples; i++)
            {
                float t = (i + 0.5f) / raycastSamples;
                float y = 1f - 2f * t;
                float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float angle = goldenAngle * i;
                Vector3 localDirection = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                Vector3 direction = transform.TransformDirection(localDirection.normalized);

                if (Physics.Raycast(origin, direction, out RaycastHit hit, maxAnchorDistance, surfaceMask, QueryTriggerInteraction.Ignore))
                {
                    anchors.Add(hit.point + hit.normal * 0.01f);
                }
            }
        }

        private void SortAnchorsAroundCenter(List<Vector3> anchors, out Vector3 center, out Vector3 axisX, out Vector3 axisY)
        {
            center = Vector3.zero;
            for (int i = 0; i < anchors.Count; i++)
            {
                center += anchors[i];
            }

            center /= anchors.Count;
            Vector3 normal = Vector3.zero;

            for (int i = 0; i < anchors.Count; i++)
            {
                Vector3 a = anchors[i] - center;
                Vector3 b = anchors[(i + 1) % anchors.Count] - center;
                normal += Vector3.Cross(a, b);
            }

            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = transform.forward;
            }

            normal.Normalize();
            axisX = Vector3.ProjectOnPlane(anchors[0] - center, normal).normalized;
            if (axisX.sqrMagnitude < 0.0001f)
            {
                axisX = Vector3.right;
            }

            axisY = Vector3.Cross(normal, axisX).normalized;

            Vector3 sortCenter = center;
            Vector3 sortAxisX = axisX;
            Vector3 sortAxisY = axisY;

            anchors.Sort((a, b) =>
            {
                Vector3 da = a - sortCenter;
                Vector3 db = b - sortCenter;
                float angleA = Mathf.Atan2(Vector3.Dot(da, sortAxisY), Vector3.Dot(da, sortAxisX));
                float angleB = Mathf.Atan2(Vector3.Dot(db, sortAxisY), Vector3.Dot(db, sortAxisX));
                return angleA.CompareTo(angleB);
            });
        }

        private void BuildSegments(
            IReadOnlyList<Vector3> anchors,
            Vector3 center,
            Vector3 axisX,
            Vector3 axisY,
            List<GeneratedSegment> segments)
        {
            System.Random random = new System.Random(seed);
            int radialCount = anchors.Count;
            Vector3[,] nodes = new Vector3[ringCount + 1, radialCount];

            for (int radial = 0; radial < radialCount; radial++)
            {
                Vector3 anchor = anchors[radial];

                for (int ring = 0; ring <= ringCount; ring++)
                {
                    float t = ring / (float)ringCount;
                    Vector3 point = Vector3.Lerp(center, anchor, t);
                    float falloff = Mathf.Sin(t * Mathf.PI);
                    Vector3 planarNoise = (axisX * RandomRange(random, -1f, 1f) + axisY * RandomRange(random, -1f, 1f))
                        * irregularity * falloff;
                    Vector3 sagOffset = Vector3.down * sag * falloff * Vector3.Distance(center, anchor) * 0.15f;

                    nodes[ring, radial] = point + planarNoise + sagOffset;
                }
            }

            for (int radial = 0; radial < radialCount; radial++)
            {
                AddSegment(segments, center, nodes[ringCount, radial], 1.25f, false);
            }

            for (int ring = 1; ring <= ringCount; ring++)
            {
                for (int radial = 0; radial < radialCount; radial++)
                {
                    Vector3 a = nodes[ring, radial];
                    Vector3 b = nodes[ring, (radial + 1) % radialCount];
                    float width = Mathf.Lerp(1.2f, 0.6f, ring / (float)ringCount);
                    AddSegment(segments, a, b, width, ring > 1);
                }
            }

            for (int radial = 0; radial < radialCount; radial += 2)
            {
                int next = (radial + 1) % radialCount;
                int ring = Mathf.Max(1, ringCount - 2);
                AddSegment(segments, nodes[ring, radial], nodes[ringCount, next], 0.5f, true);
            }
        }

        private static void AddSegment(List<GeneratedSegment> segments, Vector3 a, Vector3 b, float width, bool dropletCandidate)
        {
            segments.Add(new GeneratedSegment
            {
                a = a,
                b = b,
                widthMultiplier = width,
                dropletCandidate = dropletCandidate
            });
        }

        private void RenderSegments(IReadOnlyList<GeneratedSegment> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                GeneratedSegment segment = segments[i];
                GameObject obj = new GameObject($"Silk Segment {i:000}");
                obj.transform.SetParent(transform, true);

                LineRenderer line = obj.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPosition(0, segment.a);
                line.SetPosition(1, segment.b);
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.useWorldSpace = true;
                line.widthMultiplier = lineWidth * segment.widthMultiplier;
                line.numCapVertices = 4;
                line.numCornerVertices = 2;

                if (silkMaterial != null)
                {
                    line.sharedMaterial = silkMaterial;
                }

                generatedObjects.Add(obj);
            }
        }

        private void PlaceDroplets(IReadOnlyList<GeneratedSegment> segments)
        {
            if (dropletPrefab == null || dropletCount <= 0)
            {
                return;
            }

            System.Random random = new System.Random(seed * 17 + 11);
            int placed = 0;

            for (int i = 0; i < segments.Count && placed < dropletCount; i++)
            {
                GeneratedSegment segment = segments[i];
                if (!segment.dropletCandidate || random.NextDouble() > 0.38)
                {
                    continue;
                }

                float t = RandomRange(random, 0.2f, 0.8f);
                Vector3 position = Vector3.Lerp(segment.a, segment.b, t);
                GameObject droplet = Instantiate(dropletPrefab, position, Quaternion.identity, transform);
                droplet.name = $"Silk Droplet {placed:00}";
                droplet.transform.localScale *= RandomRange(random, 0.65f, 1.25f);
                generatedObjects.Add(droplet);
                placed++;
            }
        }

        private static Vector3 UnityRandomInsideSphere(System.Random random)
        {
            Vector3 value;
            do
            {
                value = new Vector3(
                    RandomRange(random, -1f, 1f),
                    RandomRange(random, -1f, 1f),
                    RandomRange(random, -1f, 1f));
            }
            while (value.sqrMagnitude > 1f);

            return value;
        }

        private static float RandomRange(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
