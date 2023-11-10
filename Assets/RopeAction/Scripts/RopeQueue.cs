using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace RopeAction
{
    public class RopeQueue : MonoBehaviour
    {
        public static RopeQueue m_Instance;
        
        [SerializeField, Range(1, 8)] int threadCount = 4;
        [SerializeField, Range(1, 256)] int maxInQueue = 216;

        public Queue<MeshData>[] Submissions;

        public static RopeQueue Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    m_Instance = FindObjectOfType<RopeQueue>();
                    if(m_Instance == null) Debug.LogError("Rope Queue Should be present in the scene");
                    else m_Instance.Init();
                }

                return m_Instance;
            }
        }

        void Init()
        {
            Submissions = new Queue<MeshData>[threadCount];
            for (int i = 0; i < threadCount; i++) Submissions[i] = new Queue<MeshData>();
        }
        
        void FixedUpdate()
        {
            foreach (var queue in Submissions)
            {
                if (queue.Count > 0)
                {
                    ThreadPool.QueueUserWorkItem((state) =>
                    {
                        var subQueue = (Queue<MeshData>)state;
                        while (subQueue.Count > 0)
                        {
                            MeshData submission;
                            lock (Submissions)
                            { submission = subQueue.Dequeue(); }
                            ProcessData(submission);
                        }
                    }, queue);
                }
            }
        }

        public void QueueUp(MeshData data)
        {
            if (Submissions.Length == 0) return;
            
            int lowest = 0;
            int lowestSize = Submissions[0].Count;
            for (int i = 1; i < Submissions.Length; i++)
            {
                int newSize = Submissions[i].Count;
                if (lowestSize > newSize)
                {
                    lowestSize = newSize;
                    lowest = i;
                }
            }

            lock (Submissions[lowest])
            {
                if (lowestSize >= maxInQueue) Submissions[lowest].Dequeue();
                Submissions[lowest].Enqueue(data);
            }
        }

        void ProcessData(MeshData data)
        {
            if (data.PhysicsData.ActivePhysics)
            {
                Simulate(ref data.PhysicsData, data.StartPoint, data.EndPoint, 0.02f);
                CalculatePhysicsOffsets(ref data.PhysicsData, data.StartPoint, data.EndPoint);
            }

            Vector3[] vertices = CalculateRopePoints(ref data, CalculateCapsulePoints(ref data));
            int[] indices = CalculateIndices(ref data);
            data.Callback.Invoke(vertices, indices);
        }
        
        static Vector3[] CalculateCapsulePoints(ref MeshData data)
        {
            int pointCount = (data.SphericalResolution + data.LinearResolution) * 2;
            float sphericalStep;

            if (data.LinearResolution != 0)
                sphericalStep = 360f / ((data.SphericalResolution - 1) * 2);
            else sphericalStep = 360f / (data.SphericalResolution * 2);

            Vector3[] points = new Vector3[pointCount];
            float angleOffset = 0f;
            if (data.LinearResolution == 0) angleOffset = sphericalStep / 2f;
            
            int indexOffset = 0;

            for (int i = 0; i < data.SphericalResolution; i++)
            {
                 Vector3 point = Quaternion.AngleAxis
                        (angleOffset + (i * sphericalStep), data.StartUp) * data.StartRight
                    * data.CapsuleRadius + data.StartPoint;
                 point.y = Mathf.Max(data.PhysicsData.MinY, point.y);
                 points[i + indexOffset] = point;
            }

            angleOffset += 180f;
            indexOffset += data.SphericalResolution + data.LinearResolution;
            for (int i = 0; i < data.SphericalResolution; i++)
            {
                Vector3 point = Quaternion.AngleAxis
                        ((angleOffset + (i * sphericalStep)) % 360f, data.EndUp) * data.EndRight
                    * data.CapsuleRadius + data.EndPoint;
                point.y = Mathf.Max(data.PhysicsData.MinY, point.y);
                points[i + indexOffset] = point;
            }

            indexOffset = data.SphericalResolution;
            CalculateLinear(indexOffset, data.LinearResolution, data.PhysicsData.MinY);

            indexOffset += data.SphericalResolution + data.LinearResolution;
            CalculateLinear(indexOffset, data.LinearResolution, data.PhysicsData.MinY);

            int halfSize = data.SphericalResolution + data.LinearResolution;
            int start = data.SphericalResolution / 2;
            float p = 1f / (halfSize);
            float step = 1f / (data.PhysicsData.Offsets.Length + 1);
            for (int i = 0; i < halfSize; i++)
                points[start + i] += GetOffset(data.PhysicsData.Offsets, 
                    Mathf.Clamp01(p * i), step);

            start += halfSize;
            for (int i = 0; i < halfSize; i++)
                points[(start + i) % pointCount] += GetOffset(data.PhysicsData.Offsets, 
                    Mathf.Clamp01(1 - p * i), step);
            
            return points;
            
            void CalculateLinear(int offset, int resolution, float minY)
            {
                Vector3 pStart = points[offset - 1];
                Vector3 pTarget = points[(offset + resolution) % pointCount];
                Vector3 pForward = pTarget - pStart;
                float h = pForward.magnitude;
                float linearStep = h / (1 + resolution);
                pForward /= h;

                for (int i = 0; i < resolution; i++)
                {
                    Vector3 point = pStart + (i + 1) * linearStep * pForward;
                    point.y = Mathf.Max(minY, point.y);
                    points[offset + i] = point;
                }
            }
        }

        static Vector3 GetOffset(Vector3[] offsets, float percent, float step)
        {
            int id = (int)(percent / step);
            if (id >= offsets.Length - 1) return offsets[^1];
            return offsets[id] + (offsets[id + 1] - offsets[id]) * ((percent % step) * (offsets.Length + 1));
        }

        static Vector3[] CalculateRopePoints(ref MeshData data, Vector3[] capsulePoints)
        {
            Vector3[] vertices = new Vector3[capsulePoints.Length * data.RopeResolution];
            int index = 0;
            float step = 360f / data.RopeResolution;
            for (int i = 0; i < capsulePoints.Length; i++)
            {
                Vector3 center = capsulePoints[i];
                Vector3 forward = (capsulePoints[(i + 1) % capsulePoints.Length] - center).normalized;
                Vector3 right = Vector3.Cross(forward, data.EndUp);
                float angle = 0f;
                while (angle < 360f)
                {
                    vertices[index] = Quaternion.AngleAxis(angle, forward) 
                        * right * data.RopeRadius + center;
                    angle += step;
                    index++;
                }
            }

            return vertices;
        }

        static int[] CalculateIndices(ref MeshData data)
        {
            int capsuleSize = (data.SphericalResolution + data.LinearResolution) * 2;
            int ropeRes = data.RopeResolution;
            int ropeSize = capsuleSize * ropeRes;
            int[] indices = new int[ropeSize * 6];

            int index = 0;
            for (int i = 0; i < capsuleSize; i++)
            {
                for (int j = 0; j < ropeRes; j++)
                {
                    int bIndex = i * ropeRes;
                    indices[index] = bIndex + j;
                    indices[index + 1] = (bIndex + (j + 1) % ropeRes + ropeRes) % ropeSize;
                    indices[index + 2] = (bIndex + j + ropeRes) % ropeSize;

                    indices[index + 3] = indices[index];
                    indices[index + 4] = bIndex + (j + 1) % ropeRes;
                    indices[index + 5] = indices[index + 1];

                    index += 6;
                }
            }

            return indices;
        }
        
        static void Simulate(ref PhysicsData data, Vector3 startPoint, Vector3 endPoint, float delta)
        {
            if (data.PhysicsSegments.Length < 2) return;

            float segLength = (startPoint - endPoint).magnitude / (data.PhysicsSegments.Length - 1);
            for (int i = 0; i < data.PhysicsSegments.Length; i++)
            {
                var firstSegment = data.PhysicsSegments[i];
                Vector3 velocity = firstSegment.PosNow - firstSegment.PosOld;

                firstSegment.PosOld = firstSegment.PosNow;
                Vector3 gravityApplied = firstSegment.PosNow + data.Gravity * delta;
                Vector3 newPos = gravityApplied + velocity;
                newPos.y = Mathf.Max(data.MinY, newPos.y);
                firstSegment.PosNow = newPos;
                data.PhysicsSegments[i] = firstSegment;
            }
            for (int i = 0; i < data.ConstApplyCount; i++) 
                Constraints(ref data, startPoint, endPoint, segLength);
        }

        static void Constraints(ref PhysicsData data, Vector3 startPoint, Vector3 endPoint, float segLength)
        {
            data.PhysicsSegments[0] = new PhysicsSegment(startPoint);
            
            for (int i = 0; i < data.PhysicsSegments.Length - 1; i++)
            {
                PhysicsSegment firstSeg = data.PhysicsSegments[i],
                               secondSeg = data.PhysicsSegments[i + 1];
                float dist = (firstSeg.PosNow - secondSeg.PosNow).magnitude;
                float error = Mathf.Abs(dist - segLength);
                Vector3 changeDir = Vector3.zero;

                if (dist > segLength)
                    changeDir = (firstSeg.PosNow - secondSeg.PosNow).normalized;
                else if (dist < segLength)
                    changeDir = (secondSeg.PosNow - firstSeg.PosNow).normalized;
                
                Vector3 changeAmount = changeDir * error;
                if (i != 0)
                {
                    firstSeg.PosNow -= changeAmount * 0.5f;
                    secondSeg.PosNow += changeAmount * 0.5f;
                }
                else secondSeg.PosNow += changeAmount;

                data.PhysicsSegments[i] = firstSeg;
                data.PhysicsSegments[i + 1] = secondSeg;
            }
            
            data.PhysicsSegments[^1] = new PhysicsSegment(endPoint);
        }

        static void CalculatePhysicsOffsets(ref PhysicsData data, Vector3 startPoint, Vector3 endPoint)
        {
            Vector3 direction = endPoint - startPoint;
            float fullLength = direction.magnitude;
            direction /= fullLength;
            float disBetweenPoints = fullLength / (data.PhysicsSegments.Length - 1);

            for (int i = 0; i < data.Offsets.Length; i++)
            {
                Vector3 point = startPoint + direction * (i * disBetweenPoints);
                point = data.PhysicsSegments[i].PosNow - point;
                data.Offsets[i] = data.Stiffness * point;
            }
        }
    }
}