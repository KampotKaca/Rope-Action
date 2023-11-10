using UnityEngine;

namespace RopeAction
{
    public class Tester : MonoBehaviour
    {
        [SerializeField] CapsuleRope[] ropes;
        [SerializeField] Vector3 force = Vector3.right * 3;
        
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                foreach (var rope in ropes) rope.Shake(force);
            }
        }
    }
}