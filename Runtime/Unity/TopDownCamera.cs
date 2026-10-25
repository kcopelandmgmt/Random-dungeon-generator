using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// The locked camera, Albion Online's model: a 3D scene seen from a fixed angle, yaw 45° so grid
    /// north-east is up-right on screen, pitch around 50°. No player rotation: an eight-team fight is
    /// only readable if everyone shares the frame. The player zooms along the view direction with the
    /// scroll wheel (fed by the input layer; this assembly reads no input). The vertical field of view is
    /// fixed, so a wider monitor sees more to the sides, up to <see cref="MaxAspect"/>; beyond that the
    /// view is pillarboxed so an ultra-wide stops paying off. Follows a target with a little lead toward
    /// the aim point so a Marksman can see down a corridor.
    /// </summary>
    public sealed class TopDownCamera : MonoBehaviour
    {
        public Transform Target;

        [Header("Angle (locked, never exposed to the player)")]
        [Range(35f, 70f)] public float PitchDegrees = 50f;
        [Tooltip("Fixed world yaw. 45 puts grid north-east up-right on screen, Albion style. 0 would keep corridors axis-aligned to the screen.")]
        public float YawDegrees = 45f;

        [Header("Zoom (scroll wheel, along the view direction)")]
        [Tooltip("Target distance from the focus point. ~20 m shows a 13-cell boss arena with margin at 2 m cells.")]
        public float Distance = 20f;
        public float MinDistance = 12f, MaxDistance = 28f;
        [Tooltip("Metres per scroll notch.")]
        public float ZoomStep = 2f;
        public float ZoomSharpness = 10f;

        [Header("View")]
        [Tooltip("Vertical field of view in degrees. Fixed, so the horizontal view scales with the monitor's aspect ratio.")]
        [Range(20f, 60f)] public float VerticalFov = 40f;
        [Tooltip("Widest aspect ratio that gains horizontal view. Wider screens are pillarboxed to this. 21:9 = 2.333.")]
        public float MaxAspect = 21f / 9f;

        [Header("Follow")]
        [Tooltip("How far the camera leads toward the mouse aim point, as a fraction of the aim offset.")]
        [Range(0f, 0.6f)] public float AimLead = 0.25f;
        public float FollowSharpness = 12f;
        public Camera Camera;

        /// <summary>The smoothed distance actually in use this frame.</summary>
        public float CurrentDistance { get; private set; }

        private Vector3 _focus;
        private Vector2 _aimScreen;
        private bool _hasAim;

        /// <summary>
        /// Screen position the camera leads toward, fed each frame by the input layer
        /// (LocalPlayerController in DungeonGen.Unity.Input). Until something sets it the aim is the
        /// screen centre and the camera applies no lead.
        /// </summary>
        public Vector2 AimScreenPosition
        {
            get => _hasAim ? _aimScreen : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            set { _aimScreen = value; _hasAim = true; }
        }

        /// <summary>Zoom by a number of scroll notches: positive zooms in, negative out. Clamped to the limits.</summary>
        public void Zoom(float notches)
        {
            Distance = Mathf.Clamp(Distance - notches * ZoomStep, MinDistance, MaxDistance);
        }

        private void Awake()
        {
            if (Camera == null) Camera = GetComponentInChildren<Camera>();
            if (Target != null) _focus = Target.position;
            CurrentDistance = Distance;
        }

        private void LateUpdate()
        {
            if (Camera == null) return;
            ApplyView();
            if (Target == null) return;
            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            CurrentDistance = Mathf.Lerp(CurrentDistance, Distance, 1f - Mathf.Exp(-ZoomSharpness * Time.deltaTime));
            Vector3 desired = Target.position + AimOffset() * AimLead;
            _focus = Vector3.Lerp(_focus, desired, 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            var rot = Quaternion.Euler(PitchDegrees, YawDegrees, 0f);
            Camera.transform.SetPositionAndRotation(_focus - rot * Vector3.forward * CurrentDistance, rot);
        }

        /// <summary>Fixed vertical FOV; pillarbox past the widest supported aspect so the horizontal view stops growing.</summary>
        private void ApplyView()
        {
            Camera.usePhysicalProperties = false;
            Camera.fieldOfView = VerticalFov;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1f;
            Rect rect;
            if (MaxAspect > 0f && aspect > MaxAspect)
            {
                float w = MaxAspect / aspect;
                rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else rect = new Rect(0f, 0f, 1f, 1f);
            if (Camera.rect != rect) Camera.rect = rect;
        }

        /// <summary>Where <see cref="AimScreenPosition"/> points on the ground plane, relative to the target.</summary>
        private Vector3 AimOffset()
        {
            var ray = Camera.ScreenPointToRay(AimScreenPosition);
            var ground = new Plane(Vector3.up, new Vector3(0f, Target.position.y, 0f));
            if (!ground.Raycast(ray, out float t)) return Vector3.zero;
            var hit = ray.GetPoint(t) - Target.position;
            return Vector3.ClampMagnitude(new Vector3(hit.x, 0f, hit.z), CurrentDistance * 0.5f);
        }

        /// <summary>Ground-plane point under a screen position, for aiming skillshots and placing telegraphs.</summary>
        public bool ScreenToGround(Vector3 screen, out Vector3 world)
        {
            var ray = Camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float t)) { world = ray.GetPoint(t); return true; }
            world = default; return false;
        }

        /// <summary>Screen-relative stick or WASD input turned into a world-space ground direction under this yaw: up on screen is the camera's forward.</summary>
        public Vector2 ScreenToWorldDirection(Vector2 screenMove)
        {
            float yaw = YawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            return new Vector2(screenMove.x * c + screenMove.y * s, -screenMove.x * s + screenMove.y * c);
        }
    }
}
