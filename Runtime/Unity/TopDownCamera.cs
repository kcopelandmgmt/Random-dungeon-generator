using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The locked camera: 3D scene, top-down view, fixed yaw so corridors stay axis-aligned to the
    /// grid, pitch between 55 and 60 degrees. Follows a target with a little lead toward the aim
    /// point so a Marksman can see down a corridor. Rotation is deliberately not exposed to the
    /// player; readability of an eight-team fight depends on everyone sharing the same frame.
    /// </summary>
    public sealed class TopDownCamera : MonoBehaviour
    {
        public Transform Target;
        [Range(45f, 70f)] public float PitchDegrees = 57f;
        [Tooltip("Fixed world yaw. 0 keeps grid X to the right and grid Y up the screen.")]
        public float YawDegrees = 0f;
        [Tooltip("Distance from the target along the view direction. ~22 m shows a 13-cell boss arena with margin at 2 m cells.")]
        public float Distance = 22f;
        [Tooltip("How far the camera leads toward the mouse aim point, as a fraction of the aim offset.")]
        [Range(0f, 0.6f)] public float AimLead = 0.25f;
        public float FollowSharpness = 12f;
        public Camera Camera;

        private Vector3 _focus;

        private void Awake()
        {
            if (Camera == null) Camera = GetComponentInChildren<Camera>();
            if (Target != null) _focus = Target.position;
        }

        private void LateUpdate()
        {
            if (Target == null || Camera == null) return;
            Vector3 desired = Target.position + AimOffset() * AimLead;
            _focus = Vector3.Lerp(_focus, desired, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            var rot = Quaternion.Euler(PitchDegrees, YawDegrees, 0f);
            Camera.transform.SetPositionAndRotation(_focus - rot * Vector3.forward * Distance, rot);
        }

        /// <summary>Where the mouse points on the ground plane, relative to the target.</summary>
        private Vector3 AimOffset()
        {
            var ray = Camera.ScreenPointToRay(Input.mousePosition);
            var ground = new Plane(Vector3.up, new Vector3(0f, Target.position.y, 0f));
            if (!ground.Raycast(ray, out float t)) return Vector3.zero;
            var hit = ray.GetPoint(t) - Target.position;
            return Vector3.ClampMagnitude(new Vector3(hit.x, 0f, hit.z), Distance * 0.5f);
        }

        /// <summary>Ground-plane point under a screen position, for aiming skillshots and placing telegraphs.</summary>
        public bool ScreenToGround(Vector3 screen, out Vector3 world)
        {
            var ray = Camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float t)) { world = ray.GetPoint(t); return true; }
            world = default; return false;
        }
    }
}
