using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Life for an unrigged model: a slow bob (a hover above the floor for floating creatures) and a slight sway. Sits on
    /// the model child of a static creature's prefab, so it moves only the mesh, never the avatar the view places.
    /// </summary>
    public sealed class IdleBob : MonoBehaviour
    {
        [Tooltip("Metres above its resting height the model floats (0 for walkers).")] public float Hover;
        [Tooltip("Bob amplitude, metres.")] public float Amplitude = 0.05f;
        [Tooltip("Bobs per second.")] public float Frequency = 0.8f;
        [Tooltip("Yaw sway, degrees either side.")] public float SwayDegrees = 3f;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private float _phase;

        private void Awake()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _phase = Random.value * Mathf.PI * 2f;
        }

        private void Update()
        {
            float t = Time.time * Frequency * Mathf.PI * 2f + _phase;
            // localPosition is in the parent's space; the model child carries the normalising scale itself, so metres are metres here.
            transform.localPosition = _basePos + Vector3.up * (Hover + Mathf.Sin(t) * Amplitude);
            transform.localRotation = _baseRot * Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * SwayDegrees, 0f);
        }
    }
}
