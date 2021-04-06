using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UTJ
{

    [Serializable]
    public class UsdMesh : UsdXform
    {
        #region fields
        [SerializeField] List<UsdSubmesh> m_submeshes = new List<UsdSubmesh>();

        usdi.Mesh m_mesh;
        usdi.MeshData m_meshData;
        usdi.SubmeshData[] m_submeshData;
        usdi.MeshSummary m_meshSummary;

        bool m_allocateMeshDataRequired;
        bool m_updateIndicesRequired;
        bool m_updateVerticesRequired;
        bool m_updateSkinningRequired;
        bool m_kickVBUpdate; // for Unity 5.5 or later
        double m_timeRead; // accessed from worker thread
        usdi.Task m_asyncRead;
        #endregion


        #region properties
        public usdi.Mesh nativeMeshPtr
        {
            get { return m_mesh; }
        }
        public usdi.MeshSummary meshSummary
        {
            get { return m_meshSummary; }
        }
        public usdi.MeshData meshData
        {
            get { return m_meshData; }
            set { m_meshData = value; }
        }
        public usdi.SubmeshData[] submeshData
        {
            get { return m_submeshData; }
        }
        public List<UsdSubmesh> submeshes
        {
            get { return m_submeshes; }
        }
        #endregion


        #region impl
        UsdSubmesh usdiAddSubmesh()
        {
            var sm = new UsdSubmesh();
            sm.usdiOnLoad(this, m_submeshes.Count);
            m_submeshes.Add(sm);
            return sm;
        }

        protected override UsdIComponent usdiSetupSchemaComponent()
        {
            return GetOrAddComponent<UsdMeshComponent>();
        }

        public override void usdiOnLoad()
        {
            base.usdiOnLoad();

            m_mesh = usdi.usdiAsMesh(m_schema);
            usdi.usdiMeshGetSummary(m_mesh, ref m_meshSummary);
            if(m_submeshes.Count == 0)
            {
                usdiAddSubmesh();
            }

            if (isInstance)
            {
                // 
            }
            else
            {
                m_allocateMeshDataRequired = true;
                if (m_meshSummary.num_bones > 0 && m_stream.importSettings.swapHandedness)
                {
                    Debug.LogWarning("Swap Handedness import option is enabled. This may cause broken skinning animation.");
                }
            }
        }

        public override void usdiOnUnload()
        {
            base.usdiOnUnload();

            int c = m_submeshes.Count;
            for (int i = 0; i < c; ++i) { m_submeshes[i].usdiOnUnload(); }

            m_meshData = usdi.MeshData.default_value;
            m_submeshData = null;
            m_meshSummary = usdi.MeshSummary.default_value;

            m_allocateMeshDataRequired = false;
            m_updateIndicesRequired = false;
            m_updateVerticesRequired = false;
            m_updateSkinningRequired = false;
            m_timeRead = 0.0;

            m_asyncRead = null;
        }


        // async
        void usdiAllocateMeshData(double t)
        {
            usdi.MeshData md = usdi.MeshData.default_value;
            usdi.usdiMeshReadSample(m_mesh, ref md, t, true);

            m_meshData = md;
            if (m_meshData.num_submeshes == 0)
            {
                m_submeshes[0].usdiAllocateMeshData(ref m_meshSummary, ref m_meshData);
            }
            else
            {
                m_submeshData = new usdi.SubmeshData[m_meshData.num_submeshes];
                m_meshData.submeshes = usdi.GetArrayPtr(m_submeshData);
                usdi.usdiMeshReadSample(m_mesh, ref m_meshData, t, true);

                while (m_submeshes.Count < m_meshData.num_submeshes)
                {
                    usdiAddSubmesh();
                }
                for (int i = 0; i < m_meshData.num_submeshes; ++i)
                {
                    m_submeshes[i].usdiAllocateMeshData(ref m_meshSummary, ref m_submeshData);
                }
            }
        }

        // async
        public override void usdiAsyncUpdate(double time)
        {
            base.usdiAsyncUpdate(time);
            if (m_updateFlags.bits == 0 && !m_allocateMeshDataRequired && !m_updateIndicesRequired && !m_updateVerticesRequired) {
                return;
            }
            if(m_updateFlags.importConfigChanged || m_updateFlags.variantSetChanged)
            {
                usdi.usdiMeshGetSummary(m_mesh, ref m_meshSummary);
            }

            m_timeRead = time;
            if(isInstance)
            {
                m_allocateMeshDataRequired = false;
                m_updateIndicesRequired = false;
                m_updateVerticesRequired = false;
                m_updateSkinningRequired = false;

                usdi.usdiMeshReadSample(m_mesh, ref m_meshData, m_timeRead, true);
                while (m_submeshes.Count < m_meshData.num_submeshes)
                {
                    usdiAddSubmesh();
                }
            }
            else
            {
                if (!m_allocateMeshDataRequired)
                {
                    m_allocateMeshDataRequired =
                        m_meshSummary.topology_variance == usdi.TopologyVariance.Heterogenous ||
                        m_updateFlags.variantSetChanged;
                }
                if(!m_updateIndicesRequired)
                {
                    m_updateIndicesRequired =
                        m_allocateMeshDataRequired ||
                        m_meshSummary.topology_variance == usdi.TopologyVariance.Heterogenous ||
                        m_updateFlags.importConfigChanged;

                }
                if (!m_updateVerticesRequired)
                {
                    m_updateVerticesRequired =
                        m_updateIndicesRequired ||
                        m_meshSummary.topology_variance != usdi.TopologyVariance.Constant;
                }
                if(!m_updateSkinningRequired)
                {
                    m_updateSkinningRequired = m_allocateMeshDataRequired || m_updateFlags.importConfigChanged;
                }
            }

            // todo: update heterogenous mesh when possible
            m_kickVBUpdate =
#if UNITY_5_5_OR_NEWER
                m_stream.directVBUpdate && !m_allocateMeshDataRequired &&
                m_meshSummary.topology_variance == usdi.TopologyVariance.Homogenous;
#else
                false;
#endif

            if (m_allocateMeshDataRequired)
            {
                usdiAllocateMeshData(m_timeRead);
            }

            if (m_updateVerticesRequired)
            {
                if (m_kickVBUpdate)
                {
                    usdi.usdiMeshReadSample(m_mesh, ref m_meshData, m_timeRead, false);
                    // kick VB update task in usdiUpdate()
                }
                else
                {
#if UNITY_EDITOR
                    if (m_stream.forceSingleThread)
                    {
                        usdi.usdiMeshReadSample(m_mesh, ref m_meshData, m_timeRead, true);
                    }
                    else
#endif
                    {
                        if (m_asyncRead == null)
                        {
                            m_asyncRead = new usdi.Task(usdi.usdiTaskCreateMeshReadSample(m_mesh, ref m_meshData, ref m_timeRead));
                        }
                        m_asyncRead.Run();
                    }
                }
            }
        }


        // sync
        void usdiUploadMeshData(double t, bool topology, bool skinning)
        {
            int num_submeshes = m_meshData.num_submeshes == 0 ? 1 : m_meshData.num_submeshes;
            for (int i = 0; i < num_submeshes; ++i)
            {
                m_submeshes[i].usdiUploadMeshData(m_kickVBUpdate, topology, skinning, m_stream.directVBUpdate);
            }
        }

        // sync
        public override void usdiUpdate(double time)
        {
            if (m_updateFlags.bits == 0 && !m_allocateMeshDataRequired && !m_updateVerticesRequired)
            {
                return;
            }
            base.usdiUpdate(time);

            usdiSync();

            int num_submeshes = m_meshData.num_submeshes == 0 ? 1 : m_meshData.num_submeshes;

            if(m_goAssigned)
            {
                for (int i = 0; i < num_submeshes; ++i)
                {
                    m_submeshes[i].usdiSetupComponents(this);
                }
            }
            else
            {
                for (int i = 0; i < num_submeshes; ++i)
                {
                    m_submeshes[i].usdiSetupMesh(this);
                }
            }

            if (m_meshSummary.topology_variance == usdi.TopologyVariance.Heterogenous)
            {
                // number of active submeshes may change over time if topology is dynamic.
                int i = 1;
                for (; i < num_submeshes; ++i)
                {
                    m_submeshes[i].usdiSetActive(true);
                }
                for (; i < m_submeshes.Count; ++i)
                {
                    m_submeshes[i].usdiSetActive(false);
                }
            }

            if (m_updateVerticesRequired)
            {
                if(m_meshData.num_submeshes == 0)
                {
                    m_submeshes[0].usdiUpdateBounds(ref m_meshData);
                }
                else
                {
                    for (int i = 0; i < num_submeshes; ++i)
                    {
                        m_submeshes[i].usdiUpdateBounds(ref m_submeshData[i]);
                    }
                }
            }

            if (m_allocateMeshDataRequired)
            {
                //bool close = m_meshSummary.topology_variance == usdi.TopologyVariance.Constant;
                usdiUploadMeshData(time, true, true);
            }
            else if(m_updateVerticesRequired)
            {
                if (m_kickVBUpdate)
                {
                    if (m_meshData.num_submeshes == 0)
                    {
                        m_submeshes[0].usdiKickVBUpdateTask(ref m_meshData, m_updateIndicesRequired);
                    }
                    else
                    {
                        for (int i = 0; i < num_submeshes; ++i)
                        {
                            m_submeshes[i].usdiKickVBUpdateTask(ref m_submeshData[i], m_updateIndicesRequired);
                        }
                    }
                }
                else
                {
                    usdiUploadMeshData(m_timeRead, m_updateIndicesRequired, m_updateSkinningRequired);
                }
            }

            m_allocateMeshDataRequired = false;
            m_updateIndicesRequired = false;
            m_updateVerticesRequired = false;
            m_updateSkinningRequired = false;
        }

        public override void usdiSync()
        {
            if (m_asyncRead != null)
            {
                m_asyncRead.Wait();
            }
        }

        #endregion


        #region callbacks
        //// bounds debug
        //void OnDrawGizmos()
        //{
        //    if (!enabled || m_umesh == null) return;
        //    var t = GetComponent<Transform>();
        //    var b = m_umesh.bounds;
        //    Gizmos.color = Color.cyan;
        //    Gizmos.matrix = t.localToWorldMatrix;
        //    Gizmos.DrawWireCube(b.center, b.extents * 2.0f);
        //    Gizmos.matrix = Matrix4x4.identity;
        //}
        #endregion
    }

}
