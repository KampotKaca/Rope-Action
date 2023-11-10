using System;
using UnityEngine;

namespace RopeAction
{
    public struct MeshData
    {
        public Vector3 StartPoint;
        public Vector3 StartUp;
        public Vector3 StartRight;
        public Vector3 EndPoint;
        public Vector3 EndUp;
        public Vector3 EndRight;
        public float RopeRadius;
        public float CapsuleRadius;
        public int RopeResolution;
        public int SphericalResolution;
        public int LinearResolution;
        public Action<Vector3[], int[]> Callback;

        public PhysicsData PhysicsData;
    }

    public struct PhysicsData
    {
        public bool ActivePhysics;
        public PhysicsSegment[] PhysicsSegments;
        public Vector3[] Offsets;
        public Vector3 Gravity;
        public float MinY;
        public int ConstApplyCount;
        public float Stiffness;
    }
    
    [Serializable]
    public struct PhysicsSegment
    {
        public Vector3 PosNow;
        public Vector3 PosOld;
        public PhysicsSegment(Vector3 pos)
        {
            PosNow = pos;
            PosOld = pos;
        }
    }
    
    public class CapsuleRope : MonoBehaviour
    {
        public bool ActivePhysics = true;
        public bool StopRotation = false;
        public GameObject meshGameObject => m_MeshObject;
        
        [SerializeField] Transform p1, p2;
        [SerializeField, Range(.01f, 2f)] float ropeRadius = .2f;
        [SerializeField, Range(.01f, 20f)] float capsuleRadius = 2f;
        [SerializeField, Range(6, 24)] int ropeResolution = 8;
        [SerializeField, Range(4, 32)] int sphericalResolution = 16;
        [SerializeField, Range(.01f, 10f)] float linearStep = .5f;
        [SerializeField] Material material;
        [SerializeField, Range(1f, 15f)] float sleepTime = 3f;
        [SerializeField, Min(.001f)] float stretchiness = .1f;
        [SerializeField] float restingDistance = 5f;
        [SerializeField, Range(0, 1)] float minimumRadiusPercent = .3f;
        
        [Header("Physics")]
        [SerializeField] Vector3 gravity = new(0, -1, 0);
        [SerializeField] float minY;

        [SerializeField, Range(5, 30), Tooltip("Changing this has no effect in runtime")] 
        int physicsResolution = 20;
        [SerializeField, Range(0.001f, 1f)] float stiffness = .8f;
        [SerializeField, Range(1, 50)] int constApplyCount = 5;

        [SerializeField, Range(0f, 15f), Tooltip("offsets the time to update object on Awake")]
        float timeOffset = 3f;
        
        PhysicsSegment[] segments;
        Vector3[] offsets;
        
        GameObject m_MeshObject;
        MeshRenderer m_Renderer;
        Mesh m_Mesh;
        float m_LastMoveTime = -100f;
        Vector3 m_OldPos1, m_OldPos2;
        
        public Material Material
        {
            get => m_Renderer.sharedMaterial;
            set => m_Renderer.sharedMaterial = value;
        }
        
        void Awake()
        {
            m_Mesh = new Mesh
            {
                name = "CapsuleMesh"
            };
            m_Mesh.MarkDynamic();
            m_MeshObject = new GameObject("Capsule Object");
            m_MeshObject.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = m_MeshObject.AddComponent<MeshRenderer>();
            Material = material;

            segments = new PhysicsSegment[physicsResolution];
            offsets = new Vector3[physicsResolution];
            SetPhysicsPoints();
            
            m_LastMoveTime = -timeOffset;
            m_OldPos1 = p1.position;
            m_OldPos2 = p2.position;
            SendRequest();
        }
        
        public void InitManual()
        {
            m_Mesh = new Mesh
            {
                name = "CapsuleMesh"
            };
            m_Mesh.MarkDynamic();
            m_MeshObject = new GameObject("Capsule Object");
            m_MeshObject.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = m_MeshObject.AddComponent<MeshRenderer>();
            Material = material;
            segments = new PhysicsSegment[physicsResolution];
            SetPhysicsPoints();
            m_LastMoveTime = -timeOffset;
            m_OldPos1 = p1.position;
            m_OldPos2 = p2.position;
            m_MeshObject.transform.SetParent(transform, false);
        }

        void SetPhysicsPoints()
        {
            Vector3 startPos = p1.position;
            Vector3 direction = p2.position - startPos;
            float length = direction.magnitude;
            direction /= length;
            float lengthStep = length / (physicsResolution - 1);
            for (int i = 0; i < physicsResolution; i++)
                segments[i] = new PhysicsSegment(startPos + direction * (i * lengthStep));
        }
        
        void FixedUpdate()
        {
            if (m_Changed)
            {
                m_Mesh.Clear();
                m_Mesh.vertices = m_Vertices;
                m_Mesh.triangles = m_Indices;
                m_Mesh.RecalculateBounds();
                m_Mesh.RecalculateNormals();
                m_Changed = false;
            }

            Vector3 p1Pos = p1.position, 
                    p2Pos = p2.position;

            if (!Approx(m_OldPos1, p1Pos) || !Approx(m_OldPos2, p2Pos))
            {
                m_OldPos1 = p1Pos;
                m_OldPos2 = p2Pos;
                m_LastMoveTime = Time.time;
            }

            if (!StopRotation)
            {
                Vector3 direction = (p2Pos - p1Pos).normalized;
                var rot = Quaternion.LookRotation(direction, Vector3.up);
                p1.rotation = rot;
                p2.rotation = rot;
            }

            if (Time.time - m_LastMoveTime >= sleepTime) return;

            SendRequest();
        }

        void SendRequest()
        {
            float currentDistance = Vector3.Distance(m_OldPos1, m_OldPos2);
            float radius = ropeRadius * (1 - Mathf.Min(1 - minimumRadiusPercent,
                Mathf.Max(0, currentDistance - restingDistance) * stretchiness));
            
            RopeQueue.Instance.QueueUp(new MeshData
            {
                StartPoint = m_OldPos1,
                StartUp = p1.up,
                StartRight = p1.right,
                EndPoint = m_OldPos2,
                EndUp = p2.up,
                EndRight = p2.right,
                RopeRadius = radius,
                CapsuleRadius = Mathf.Max(0, capsuleRadius + radius),
                RopeResolution = ropeResolution,
                SphericalResolution = sphericalResolution,
                LinearResolution = (int)(currentDistance / linearStep),
                Callback = OnFinish,
                PhysicsData = new PhysicsData
                {
                    ActivePhysics = ActivePhysics,
                    Offsets = offsets,
                    PhysicsSegments = segments,
                    Gravity = gravity,
                    ConstApplyCount = constApplyCount,
                    Stiffness = stiffness,
                    MinY = minY + radius,
                }
            });
        }

        Vector3[] m_Vertices;
        int[] m_Indices;
        bool m_Changed;
        
        void OnFinish(Vector3[] vertices, int[] indices)
        {
            m_Vertices = vertices;
            m_Indices = indices;
            m_Changed = true;
        }

        public void Shake(Vector3 force, float percent = .5f)
        {
            m_LastMoveTime = Time.time;
            float step = 1f / ((segments.Length - 1) * .5f);
            int p = Mathf.Clamp((int)(percent * segments.Length), 1, segments.Length - 2);

            for (int i = 1; i < segments.Length - 1; i++)
            {
                float multiplier = 1 - Mathf.Clamp01(step * Mathf.Abs(i - p));
                segments[i].PosNow += force * multiplier;

            }
        }

        void OnDestroy()
        {
            if (m_MeshObject != null)
            {
                Destroy(m_MeshObject);
                Destroy(m_Mesh);
            }
        }

        static bool Approx(Vector3 v1, Vector3 v2)
        {
            return Mathf.Approximately(v1.x, v2.x) &
                   Mathf.Approximately(v1.y, v2.y) &
                   Mathf.Approximately(v1.z, v2.z);
        }
    }
}
