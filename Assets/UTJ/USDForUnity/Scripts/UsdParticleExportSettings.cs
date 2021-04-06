using System;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UTJ
{

    [AddComponentMenu("USD/Particle Export Config")]
    public class UsdParticleExportSettings : MonoBehaviour
    {
        #region fields
        public bool m_captureRotations = true;
        #endregion
    }

}
