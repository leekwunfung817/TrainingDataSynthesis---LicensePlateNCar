using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif



namespace UTJ
{
    [ExecuteInEditMode]
    [AddComponentMenu("USD/Exporter")]
    public class UsdExporter : MonoBehaviour
    {
        #region impl

        static string CreateName(UnityEngine.Object target)
        {
            return target.name + "_" + target.GetInstanceID().ToString("X8");
        }

        public abstract class ComponentCapturer
        {
            protected UsdExporter m_exporter;
            protected ComponentCapturer m_parent;
            protected usdi.Schema m_usd;

            public UsdExporter exporter { get { return m_exporter; } }
            public usdi.Context ctx { get { return m_exporter.m_ctx; } }
            public ComponentCapturer parent { get { return m_parent; } }
            public usdi.Schema usd { get { return m_usd; } }
            public string primPath { get { return usdi.usdiPrimGetPathS(m_usd); } }
            public abstract void Capture(double t); // called from main thread
            public abstract void Flush(double t); // called from worker thread

            protected ComponentCapturer(UsdExporter exporter, ComponentCapturer parent)
            {
                m_exporter = exporter;
                m_parent = parent;
            }
        }

        public class RootCapturer : ComponentCapturer
        {
            public RootCapturer(UsdExporter exporter, usdi.Schema usd)
                : base(exporter, null)
            {
                m_usd = usd;
            }

            public override void Capture(double t) // called from main thread
            {
                // do nothing
            }
            public override void Flush(double t) // called from worker thread
            {
                // do nothing
            }
        }

        public class TransformCapturer : ComponentCapturer
        {
            Transform m_target;
            usdi.XformData m_data = usdi.XformData.default_value;
            bool m_inherits = true;
            bool m_scale = true;
            bool m_captureEveryFrame = true;
            int m_count = 0;

            public bool inherits
            {
                get { return m_inherits; }
                set { m_inherits = value; }
            }
            public bool scale
            {
                get { return m_scale; }
                set { m_scale = value; }
            }

            public TransformCapturer(UsdExporter exporter, ComponentCapturer parent, Transform target, bool create_usd_node = true)
                : base(exporter, parent)
            {
                m_target = target;
                if (create_usd_node)
                {
                    m_usd = usdi.usdiCreateXform(ctx, parent.usd, CreateName(target));
                }

                if (m_target.gameObject.isStatic)
                {
                    m_captureEveryFrame = false;
                }

                var config = target.GetComponent<UsdTransformExportSettings>();
                if (config)
                {
                    m_captureEveryFrame = config.m_captureEveryFrame;
                }
            }

            public override void Capture(double t) // called from main thread
            {
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    if (inherits)
                    {
                        m_data.position = m_target.localPosition;
                        m_data.rotation = m_target.localRotation;
                        m_data.scale = scale ? m_target.localScale : Vector3.one;
                    }
                    else
                    {
                        m_data.position = m_target.position;
                        m_data.rotation = m_target.rotation;
                        m_data.scale = scale ? m_target.lossyScale : Vector3.one;
                    }
                }
            }

            public override void Flush(double t) // called from worker thread
            {
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    t = m_count == 0 ? usdi.defaultTime : t;
                    usdi.usdiXformWriteSample(usdi.usdiAsXform(m_usd), ref m_data, t);
                    ++m_count;
                }
            }
        }

        public class CameraCapturer : TransformCapturer
        {
            Camera m_target;
            usdi.CameraData m_data = usdi.CameraData.default_value;
            int m_count = 0;

            public CameraCapturer(UsdExporter exporter, ComponentCapturer parent, Camera target)
                : base(exporter, parent, target.GetComponent<Transform>(), false)
            {
                m_usd = usdi.usdiCreateCamera(ctx, parent.usd, CreateName(target));
                m_target = target;
                //target.GetComponent<usdiCameraExportConfig>();
            }

            public override void Capture(double t) // called from main thread
            {
                base.Capture(t);
                if (m_target == null) { return; }

                m_data.near_clipping_plane = m_target.nearClipPlane;
                m_data.far_clipping_plane = m_target.farClipPlane;
                m_data.field_of_view = m_target.fieldOfView;
                //m_data.focal_length = cparams.m_focalLength;
                //m_data.focus_distance = cparams.m_focusDistance;
                //m_data.aperture = cparams.m_aperture;
                //m_data.aspect_ratio = cparams.GetAspectRatio();
            }

            public override void Flush(double t) // called from worker thread
            {
                base.Flush(t);
                if (m_target == null) { return; }

                t = m_count == 0 ? usdi.defaultTime : t;
                usdi.usdiCameraWriteSample(usdi.usdiAsCamera(m_usd), ref m_data, t);
                ++m_count;
            }
        }

        public class MeshBuffer
        {
            Mesh bakedMesh_;
            public Mesh bakedMesh
            {
                get
                {
                    if(bakedMesh_ == null) { bakedMesh_ = new Mesh(); }
                    return bakedMesh_;
                }
            }

            public int[] indices;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Vector2[] uvs;

            public BoneWeight[] weights;
            public Matrix4x4[] bindposes;
            public string rootBone;
            public string[] bones;
        }

        public class MeshCaptureFlags
        {
            public bool points;
            public bool indices;
            public bool normals;
            public bool tangents;
            public bool uvs;
        }


        public static void CaptureMesh(usdi.Mesh usd, ref usdi.MeshData data, MeshBuffer dst_buf)
        {
            data = usdi.MeshData.default_value;
            if (dst_buf.vertices != null)
            {
                data.points = usdi.GetArrayPtr(dst_buf.vertices);
                data.num_points = dst_buf.vertices.Length;
            }
            if (dst_buf.indices != null)
            {
                data.indices = usdi.GetArrayPtr(dst_buf.indices);
                data.num_indices = dst_buf.indices.Length;
            }
            if (dst_buf.normals != null)
            {
                data.normals = usdi.GetArrayPtr(dst_buf.normals);
            }
            if (dst_buf.tangents != null)
            {
                data.tangents = usdi.GetArrayPtr(dst_buf.tangents);
            }
            if (dst_buf.uvs != null)
            {
                data.uvs = usdi.GetArrayPtr(dst_buf.uvs);
            }
            if (dst_buf.weights != null && dst_buf.bones != null)
            {
                data.weights = usdi.GetArrayPtr(dst_buf.weights);
                data.bindposes = usdi.GetArrayPtr(dst_buf.bindposes);
                data.num_bones = dst_buf.bones.Length;
                data.max_bone_weights = 4;
                usdi.usdiMeshAssignBones(usd, ref data, dst_buf.bones, dst_buf.bones.Length);
                usdi.usdiMeshAssignRootBone(usd, ref data, dst_buf.rootBone);
            }
        }

        public static void CaptureMesh(
            usdi.Mesh usd, ref usdi.MeshData data, MeshBuffer buf,
            Mesh mesh, MeshCaptureFlags flags)
        {
            buf.vertices = flags.points ? mesh.vertices : null;
            buf.normals = flags.normals ? mesh.normals : null;
            buf.tangents = flags.tangents ? mesh.tangents : null;
            buf.indices = flags.indices ? mesh.triangles : null;
            buf.uvs = flags.uvs ? mesh.uv : null;
            CaptureMesh(usd, ref data, buf);
        }

        public static void CaptureMesh(
            usdi.Mesh usd, ref usdi.MeshData data, MeshBuffer buf,
            SkinnedMeshRenderer smr, MeshCaptureFlags flags, bool captureBones)
        {

            Cloth cloth = smr.GetComponent<Cloth>();

            if (cloth != null)
            {
                var mesh = buf.bakedMesh;
                smr.BakeMesh(mesh);

                buf.vertices = flags.points ? cloth.vertices : null;
                buf.normals  = flags.normals ? cloth.normals : null;
                buf.tangents = flags.tangents ? mesh.tangents : null;
                buf.indices  = flags.indices ? mesh.triangles : null;
                buf.uvs      = flags.uvs ? mesh.uv : null;
            }
            else if (captureBones && buf.bones != null)
            {
                var mesh = smr.sharedMesh;

                buf.vertices = flags.points ? mesh.vertices : null;
                buf.normals  = flags.normals ? mesh.normals : null;
                buf.tangents = flags.tangents ? mesh.tangents : null;
                buf.indices  = flags.indices ? mesh.triangles : null;
                buf.uvs      = flags.uvs ? mesh.uv : null;

                buf.weights   = mesh.boneWeights;
                buf.bindposes = mesh.bindposes;
            }
            else
            {
                var mesh = buf.bakedMesh;
                smr.BakeMesh(mesh);

                buf.vertices = flags.points ? mesh.vertices : null;
                buf.normals  = flags.normals ? mesh.normals : null;
                buf.tangents = flags.tangents ? mesh.tangents : null;
                buf.indices  = flags.indices ? mesh.triangles : null;
                buf.uvs      = flags.uvs ? mesh.uv : null;
            }

            CaptureMesh(usd, ref data, buf);
        }

        public class MeshCapturer : TransformCapturer
        {
            MeshRenderer m_target;
            MeshBuffer m_buffer;
            usdi.MeshData m_data = default(usdi.MeshData);
            bool m_captureNormals = true;
            bool m_captureTangents = true;
            bool m_captureUVs = true;
            bool m_captureEveryFrame = false;
            bool m_captureEveryFrameUV = false;
            bool m_captureEveryFrameIndices = false;
            int m_count = 0;

            public static bool canCapture(MeshRenderer target)
            {
                var mfilter = target.GetComponent<MeshFilter>();
                if(mfilter != null)
                {
                    var mesh = mfilter.sharedMesh;
                    return mesh != null && mesh.isReadable;
                }
                return false;
            }

            public MeshCapturer(UsdExporter exporter, ComponentCapturer parent, MeshRenderer target)
                : base(exporter, parent, target.GetComponent<Transform>(), false)
            {
                m_usd = usdi.usdiCreateMesh(ctx, parent.usd, CreateName(target));
                m_target = target;
                m_buffer = new MeshBuffer();

                m_captureNormals = exporter.m_meshNormals;
                m_captureTangents = exporter.m_meshTangents;
                m_captureUVs = exporter.m_meshUVs;

                var conf = target.GetComponent<UsdMeshExportSettings>();
                if (conf != null)
                {
                    m_captureNormals = conf.m_captureNormals;
                    m_captureTangents = conf.m_captureTangents;
                    m_captureUVs = conf.m_captureUVs;
                    m_captureEveryFrame = conf.m_captureEveryFrame;
                    m_captureEveryFrameUV = conf.m_captureEveryFrameUV;
                    m_captureEveryFrameIndices = conf.m_captureEveryFrameIndices;
                }
            }

            public override void Capture(double t) // called from main thread
            {
                base.Capture(t);
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    var mesh = m_target.GetComponent<MeshFilter>().sharedMesh;
                    var flags = new MeshCaptureFlags
                    {
                        points   = true,
                        normals  = m_captureNormals,
                        tangents = m_captureTangents,
                        uvs      = m_captureUVs && (m_count == 0 || m_captureEveryFrameUV),
                        indices  = m_count == 0 || m_captureEveryFrameIndices,
                    };
                    CaptureMesh(usdi.usdiAsMesh(m_usd), ref m_data, m_buffer, mesh, flags);
                }
            }

            public override void Flush(double t) // called from worker thread
            {
                base.Flush(t);
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    t = m_count == 0 ? usdi.defaultTime : t;
                    usdi.usdiMeshWriteSample(usdi.usdiAsMesh(m_usd), ref m_data, t);
                    ++m_count;
                }
            }
        }

        public class SkinnedMeshCapturer : TransformCapturer
        {
            SkinnedMeshRenderer m_target;
            MeshBuffer m_buffer;
            usdi.MeshData m_data = default(usdi.MeshData);
            bool m_captureNormals = true;
            bool m_captureTangents = true;
            bool m_captureUVs = true;
            bool m_captureBones = false;
            bool m_captureEveryFrame = true;
            bool m_captureEveryFrameUV = false;
            bool m_captureEveryFrameIndices = false;
            int m_count = 0;

            public static bool canCapture(SkinnedMeshRenderer target)
            {
                var mesh = target.sharedMesh;
                return mesh != null && mesh.isReadable;
            }

            public SkinnedMeshCapturer(UsdExporter exporter, ComponentCapturer parent, SkinnedMeshRenderer target)
                : base(exporter, parent, target.GetComponent<Transform>(), false)
            {
                m_usd = usdi.usdiCreateMesh(ctx, parent.usd, CreateName(target));
                m_target = target;
                m_buffer = new MeshBuffer();

                if (m_target.GetComponent<Cloth>() != null)
                {
                    base.scale = false;
                }

                m_captureNormals = exporter.m_meshNormals;
                m_captureTangents = exporter.m_meshTangents;
                m_captureUVs = exporter.m_meshUVs;
                m_captureBones = exporter.m_skinnedMeshFormat == SkinnedMeshFormat.BoneAndWeights;
                m_captureEveryFrame = !m_captureBones;

                var conf = target.GetComponent<UsdMeshExportSettings>();
                if (conf != null)
                {
                    m_captureNormals = conf.m_captureNormals;
                    m_captureTangents = conf.m_captureTangents;
                    m_captureUVs = conf.m_captureUVs;
                    m_captureEveryFrame = conf.m_captureEveryFrame;
                    m_captureEveryFrameUV = conf.m_captureEveryFrameUV;
                    m_captureEveryFrameIndices = conf.m_captureEveryFrameIndices;
                }
            }

            public override void Capture(double t) // called from main thread
            {
                base.Capture(t);
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    bool captureBones = m_captureBones && m_count == 0;

                    if (captureBones)
                    {
                        var root = m_exporter.FindNode(m_target.rootBone);
                        if (root != null)
                        {
                            m_buffer.rootBone = root.capturer.primPath;
                        }

                        var bones = m_target.bones;
                        if (bones != null && bones.Length > 0)
                        {
                            m_buffer.bones = new string[bones.Length];
                            for (int i = 0; i < bones.Length; ++i)
                            {
                                var bone = m_exporter.FindNode(bones[i]);
                                m_buffer.bones[i] = bone.capturer.primPath;
                            }

                            if (m_exporter.m_swapHandedness)
                            {
                                Debug.LogWarning("Swap Handedness export option is enabled. This may cause broken skinning animation.");
                            }
                        }
                    }

                    var flags = new MeshCaptureFlags
                    {
                        points   = true,
                        normals  = m_captureNormals,
                        tangents = m_captureTangents,
                        uvs      = m_captureUVs && (m_count == 0 || m_captureEveryFrameUV),
                        indices  = m_count == 0 || m_captureEveryFrameIndices,
                    };

                    CaptureMesh(usdi.usdiAsMesh(m_usd), ref m_data, m_buffer, m_target, flags, captureBones);
                }
            }

            public override void Flush(double t) // called from worker thread
            {
                base.Flush(t);
                if (m_target == null) { return; }

                if (m_captureEveryFrame || m_count == 0)
                {
                    t = m_count == 0 ? usdi.defaultTime : t;
                    usdi.usdiMeshWriteSample(usdi.usdiAsMesh(m_usd), ref m_data, t);
                    ++m_count;
                }
            }
        }

        public class ParticleCapturer : TransformCapturer
        {
            ParticleSystem m_target;
            usdi.Attribute m_attr_rotatrions;
            usdi.PointsData m_data = usdi.PointsData.default_value;
            usdi.AttributeData m_dataRot;

            ParticleSystem.Particle[] m_buf_particles;
            Vector3[] m_buf_positions;
            Vector4[] m_buf_rotations;

            bool m_captureRotations = true;


            public ParticleCapturer(UsdExporter exporter, ComponentCapturer parent, ParticleSystem target)
                : base(exporter, parent, target.GetComponent<Transform>(), false)
            {
                m_usd = usdi.usdiCreatePoints(ctx, parent.usd, CreateName(target));
                m_target = target;

                var config = target.GetComponent<UsdParticleExportSettings>();
                if (config != null)
                {
                    m_captureRotations = config.m_captureRotations;
                }
                if (m_captureRotations)
                {
                    m_attr_rotatrions = usdi.usdiPrimCreateAttribute(m_usd, "rotations", usdi.AttributeType.Float4Array);
                }
            }

            public override void Capture(double t) // called from main thread
            {
                base.Capture(t);
                if (m_target == null) { return; }

                // create buffer
                int count_max =
#if UNITY_5_5_OR_NEWER
                    m_target.main.maxParticles;
#else
                    m_target.maxParticles;
#endif
                bool allocated = false;
                if (m_buf_particles == null)
                {
                    m_buf_particles = new ParticleSystem.Particle[count_max];
                    m_buf_positions = new Vector3[count_max];
                    m_buf_rotations = new Vector4[count_max];
                    allocated = true;
                }
                else if (m_buf_particles.Length != count_max)
                {
                    Array.Resize(ref m_buf_particles, count_max);
                    Array.Resize(ref m_buf_positions, count_max);
                    Array.Resize(ref m_buf_rotations, count_max);
                    allocated = true;
                }

                if (allocated)
                {
                    m_data.points = usdi.GetArrayPtr(m_buf_positions);
                    m_dataRot.data = usdi.GetArrayPtr(m_buf_rotations);
                }

                // copy particle positions & rotations to buffer
                int count = m_target.GetParticles(m_buf_particles);
                for (int i = 0; i < count; ++i)
                {
                    m_buf_positions[i] = m_buf_particles[i].position;
                }
                if (m_captureRotations)
                {
                    for (int i = 0; i < count; ++i)
                    {
                        m_buf_rotations[i] = m_buf_particles[i].axisOfRotation;
                        m_buf_rotations[i].w = m_buf_particles[i].rotation;
                    }
                }

                m_data.num_points = count;
                m_dataRot.num_elements = count;
            }

            public override void Flush(double t) // called from worker thread
            {
                base.Flush(t);
                if (m_target == null) { return; }

                usdi.usdiPointsWriteSample(usdi.usdiAsPoints(m_usd), ref m_data, t);
                if (m_captureRotations)
                {
                    usdi.usdiAttrWriteSample(m_attr_rotatrions, ref m_dataRot, t);
                }
            }
        }

        public class CustomCapturerHandler : TransformCapturer
        {
            UsdCustomComponentCapturer m_target;

            public CustomCapturerHandler(UsdExporter exporter, ComponentCapturer parent, UsdCustomComponentCapturer target)
                : base(exporter, parent, target.GetComponent<Transform>(), false)
            {
                m_target = target;
            }

            public override void Capture(double t)
            {
                base.Capture(t);
                if (m_target == null) { return; }
                m_target.Capture(t);
            }

            public override void Flush(double t) // called from worker thread
            {
                base.Flush(t);
                if (m_target == null) { return; }
                m_target.Flush(t);
            }
        }

#if UNITY_EDITOR
        void ForceDisableBatching()
        {
            var method = typeof(UnityEditor.PlayerSettings).GetMethod("SetBatchingForPlatform", BindingFlags.NonPublic | BindingFlags.Static);
            method.Invoke(null, new object[] { BuildTarget.StandaloneWindows, 0, 0 });
            method.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, 0, 0 });
        }
#endif

        #endregion


        public enum Scope
        {
            EntireScene,
            CurrentBranch,
        }

        public enum SkinnedMeshFormat
        {
            VertexCache,
            BoneAndWeights,
        }

        #region fields
        [Header("USD")]

        [SerializeField] string m_outputPath;
        [SerializeField] TimeUnit m_timeUnit = new TimeUnit();
        [SerializeField] float m_scale = 1.0f;
        [SerializeField] bool m_swapHandedness = true;
        [SerializeField] bool m_swapFaces = true;

        [Header("Capture Components")]

        [SerializeField] Scope m_scope = Scope.EntireScene;
        [SerializeField] bool m_ignoreDisabled = true;
        [Space(8)]
        [SerializeField] bool m_captureMeshRenderer = true;
        [SerializeField] bool m_captureSkinnedMeshRenderer = true;
        [SerializeField] bool m_captureParticleSystem = true;
        [SerializeField] bool m_captureCamera = true;
        [SerializeField] bool m_customCapturer = true;
        [Space(8)]
        [SerializeField] SkinnedMeshFormat m_skinnedMeshFormat = SkinnedMeshFormat.VertexCache;
        [SerializeField] bool m_meshNormals = true;
        [SerializeField] bool m_meshTangents = true;
        [SerializeField] bool m_meshUVs = true;

        [Header("Capture Setting")]

        [Tooltip("Start capture on start.")]
        [SerializeField] bool m_captureOnStart = false;
        [Tooltip("Automatically end capture when reached Max Capture Frame. 0=Infinite")]
        [SerializeField] int m_maxCaptureFrame = 0;

        [Header("Debug")]
#if UNITY_EDITOR
        [SerializeField] bool m_forceSingleThread;
        [SerializeField] bool m_detailedLog;
#endif

        usdi.Context m_ctx;
        ComponentCapturer m_root;
        List<ComponentCapturer> m_capturers = new List<ComponentCapturer>();
        bool m_recording;
        float m_time;
        float m_elapsed;
        int m_frameCount;
        int m_prevFrame = -1;

        usdi.Task m_asyncFlush;
        float m_timeFlush;
        #endregion


        #region properties
        public bool isRecording { get { return m_recording; } }
        public float time { get { return m_time; } }
        public float elapsed { get { return m_elapsed; } }
        public float frameCount { get { return m_frameCount; } }
        #endregion


        #region impl
        void usdiLog(string message)
        {
#if UNITY_EDITOR
            if (m_detailedLog)
            {
                Debug.Log(message);
            }
#endif
        }

        T[] GetTargets<T>() where T : Component
        {
            if (m_scope == Scope.CurrentBranch)
            {
                return GetComponentsInChildren<T>();
            }
            else
            {
                return FindObjectsOfType<T>();
            }
        }



        bool ShouldBeIgnored(Behaviour target)
        {
            return m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled);
        }
        bool ShouldBeIgnored(ParticleSystem target)
        {
            return m_ignoreDisabled && (!target.gameObject.activeInHierarchy);
        }
        bool ShouldBeIgnored(MeshRenderer target)
        {
            if (m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled)) { return true; }
            var mesh = target.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) { return true; }
            return false;
        }
        bool ShouldBeIgnored(SkinnedMeshRenderer target)
        {
            if (m_ignoreDisabled && (!target.gameObject.activeInHierarchy || !target.enabled)) { return true; }
            var mesh = target.sharedMesh;
            if (mesh == null) { return true; }
            return false;
        }


        // capture node tree for "Preserve Tree Structure" option.
        public class CaptureNode
        {
            public CaptureNode parent;
            public List<CaptureNode> children = new List<CaptureNode>();
            public Type componentType;

            public Transform trans;
            public ComponentCapturer capturer;
        }

        Dictionary<Transform, CaptureNode> m_captureNodes;
        List<CaptureNode> m_rootNodes;

        CaptureNode FindNode(Transform t)
        {
            if (t == null) { return null; }
            CaptureNode ret;
            if (m_captureNodes.TryGetValue(t, out ret)) { return ret; }
            return null;
        }

        CaptureNode ConstructTree(Transform trans)
        {
            if (trans == null) { return null; }
            usdiLog("ConstructTree() : " + trans.name);

            // return existing one if found
            CaptureNode ret;
            if (m_captureNodes.TryGetValue(trans, out ret)) { return ret; }

            ret = new CaptureNode();
            ret.trans = trans;
            m_captureNodes.Add(trans, ret);

            var parent = ConstructTree(trans.parent);
            if (parent != null)
            {
                ret.parent = parent;
                parent.children.Add(ret);
            }
            else
            {
                m_rootNodes.Add(ret);
            }

            return ret;
        }


        void SetupComponentCapturer(CaptureNode parent, CaptureNode node)
        {
            usdiLog("SetupComponentCapturer() " + node.trans.name);

            node.parent = parent;
            var parent_capturer = parent == null ? m_root : parent.capturer;

            bool fallback = false;
            if (node.componentType == typeof(Camera))
            {
                node.capturer = new CameraCapturer(this, parent_capturer, node.trans.GetComponent<Camera>());
            }
            else if (node.componentType == typeof(MeshRenderer))
            {
                var renderer = node.trans.GetComponent<MeshRenderer>();
                if (MeshCapturer.canCapture(renderer))
                {
                    node.capturer = new MeshCapturer(this, parent_capturer, renderer);
                }
                else
                {
                    Debug.LogWarning("Mesh \"" + renderer.name + "\" is not readable and be skipped");
                    fallback = true;
                }
            }
            else if (node.componentType == typeof(SkinnedMeshRenderer))
            {
                var renderer = node.trans.GetComponent<SkinnedMeshRenderer>();
                if (SkinnedMeshCapturer.canCapture(renderer))
                {
                    node.capturer = new SkinnedMeshCapturer(this, parent_capturer, renderer);
                }
                else
                {
                    Debug.LogWarning("SkinnedMesh \"" + renderer.name + "\" is not readable and be skipped");
                    fallback = true;
                }
            }
            else if (node.componentType == typeof(ParticleSystem))
            {
                node.capturer = new ParticleCapturer(this, parent_capturer, node.trans.GetComponent<ParticleSystem>());
            }
            else if (node.componentType == typeof(UsdCustomComponentCapturer))
            {
                node.capturer = new CustomCapturerHandler(this, parent_capturer, node.trans.GetComponent<UsdCustomComponentCapturer>());
            }
            else
            {
                fallback = true;
            }

            if(fallback)
            {
                node.capturer = new TransformCapturer(this, parent_capturer, node.trans.GetComponent<Transform>());
            }

            if (node.capturer != null)
            {
                m_capturers.Add(node.capturer);
            }

            foreach (var c in node.children)
            {
                SetupComponentCapturer(node, c);
            }
        }

        void ConstructCaptureTree()
        {
            m_root = new RootCapturer(this, usdi.usdiGetRoot(m_ctx));
            m_captureNodes = new Dictionary<Transform, CaptureNode>();
            m_rootNodes = new List<CaptureNode>();

            var bones = new HashSet<Transform>();

            // construct tree
            // (bottom-up)
            if (m_captureCamera)
            {
                foreach (var t in GetTargets<Camera>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_captureMeshRenderer)
            {
                foreach (var t in GetTargets<MeshRenderer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_captureSkinnedMeshRenderer)
            {
                foreach (var t in GetTargets<SkinnedMeshRenderer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();

                    // capture bones as well
                    if (m_skinnedMeshFormat == SkinnedMeshFormat.BoneAndWeights)
                    {
                        if (t.rootBone != null)
                        {
                            bones.Add(t.rootBone);
                        }
                        if (t.bones != null)
                        {
                            foreach (var bone in t.bones)
                            {
                                bones.Add(bone);
                            }
                        }
                    }
                }
            }
            if (m_captureParticleSystem)
            {
                foreach (var t in GetTargets<ParticleSystem>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = t.GetType();
                }
            }
            if (m_customCapturer)
            {
                foreach (var t in GetTargets<UsdCustomComponentCapturer>())
                {
                    if (ShouldBeIgnored(t)) { continue; }
                    var node = ConstructTree(t.GetComponent<Transform>());
                    node.componentType = typeof(UsdCustomComponentCapturer);
                }
            }

            foreach (var t in bones)
            {
                var node = ConstructTree(t.GetComponent<Transform>());
                node.componentType = t.GetType();
            }

            // make component capturers (top-down)
            foreach (var c in m_rootNodes)
            {
                SetupComponentCapturer(null, c);
            }
        }

        void ApplyExportConfig()
        {
            usdi.ExportSettings conf = usdi.ExportSettings.default_value;
            conf.scale = m_scale;
            conf.swapHandedness = m_swapHandedness;
            conf.swapFaces = m_swapFaces;
            usdi.usdiSetExportSettings(m_ctx, ref conf);
        }


        public bool BeginCapture()
        {
            if (m_recording)
            {
                Debug.Log("usdiExporter: already started");
                return false;
            }

            // create context and open archive
            m_ctx = usdi.usdiCreateContext();
            if (!usdi.usdiCreateStage(m_ctx, m_outputPath))
            {
                Debug.LogError("usdiExporter: failed to create " + m_outputPath);
                usdi.usdiDestroyContext(m_ctx);
                m_ctx = default(usdi.Context);
                return false;
            }
            ApplyExportConfig();

            // create capturers
            ConstructCaptureTree();

            m_recording = true;
            //m_time = m_conf.startTime;
            m_frameCount = 0;

            Debug.Log("usdiExporter: start " + m_outputPath);
            return true;
        }

        public void EndCapture()
        {
            if (!m_recording) { return; }

            FlushUSD();
            m_capturers.Clear();
            usdi.usdiDestroyContext(m_ctx); // flush archive
            m_ctx = default(usdi.Context);
            m_recording = false;
            m_time = 0.0f;
            m_frameCount = 0;

            Debug.Log("usdiExporter: end: " + m_outputPath);
        }

        public void OneShot()
        {
            if (BeginCapture())
            {
                ProcessCapture();
                EndCapture();
            }
        }

        void WaitFlush()
        {
            if (m_asyncFlush != null)
            {
                m_asyncFlush.Wait();
            }
        }

        void FlushUSD()
        {
            WaitFlush();
            usdi.usdiSave(m_ctx);
        }


        void ProcessCapture()
        {
            if (!m_recording) { return; }

            // for some reason, come here twice on first frame. skip second run.
            int frame = Time.frameCount;
            if (frame == m_prevFrame) { return; }
            m_prevFrame = frame;

            // wait for complete previous flush
            WaitFlush();

            float begin_time = Time.realtimeSinceStartup;

            // capture components
            foreach (var c in m_capturers)
            {
                c.Capture(m_time);
            }

            // kick flush task
#if UNITY_EDITOR
            if (m_forceSingleThread)
            {
                foreach (var c in m_capturers) { c.Flush(time); }
            }
            else
#endif
            {
                if (m_asyncFlush == null)
                {
                    m_asyncFlush = new usdi.DelegateTask((var) =>
                    {
                        try
                        {
                            foreach (var c in m_capturers) { c.Flush(m_timeFlush); }
                        }
                        finally
                        {
                        }
                    }, "usdiExporter: " + gameObject.name);
                }
                m_timeFlush = m_time;
                m_asyncFlush.Run();
            }

            ++m_frameCount;
            switch(m_timeUnit.type)
            {
                case TimeUnit.Types.Frame_30FPS:
                case TimeUnit.Types.Frame_60FPS:
                    m_time = m_frameCount;
                    break;
                default:
                    m_time += Time.deltaTime * m_timeUnit.scale;
                    break;
            }

            m_elapsed = Time.realtimeSinceStartup - begin_time;
            usdiLog("usdiExporter.ProcessCapture(): " + (m_elapsed * 1000.0f) + "ms");

            if (m_maxCaptureFrame > 0 && m_frameCount >= m_maxCaptureFrame)
            {
                EndCapture();
            }
        }

        IEnumerator ProcessRecording()
        {
            yield return new WaitForEndOfFrame();
            if (!m_recording) { yield break; }

            ProcessCapture();
        }

        void UpdateOutputPath()
        {
            if (m_outputPath == null || m_outputPath == "")
            {
                m_outputPath = "Assets/StreamingAssets/" + gameObject.name + ".usdc";
            }
        }
        #endregion



        #region callbacks
#if UNITY_EDITOR
        void Reset()
        {
            ForceDisableBatching();
            UpdateOutputPath();
        }
#endif

        void Awake()
        {
            usdi.InitializePluginPass1();
            usdi.InitializePluginPass2();
        }

        void OnEnable()
        {
            UpdateOutputPath();

            switch (m_timeUnit.type)
            {
                case TimeUnit.Types.Frame_30FPS:
                    Time.captureFramerate = 30;
                    break;
                case TimeUnit.Types.Frame_60FPS:
                    Time.captureFramerate = 60;
                    break;
                default:
                    Time.captureFramerate = 0;
                    break;
            }
        }

        void OnDisable()
        {
            EndCapture();
        }

        void Start()
        {
            if (m_captureOnStart
#if UNITY_EDITOR
                 && EditorApplication.isPlaying
#endif
                )
            {
                BeginCapture();
            }
        }

        void Update()
        {
            if (m_recording)
            {
                StartCoroutine(ProcessRecording());
            }
        }
        #endregion
    }
}
