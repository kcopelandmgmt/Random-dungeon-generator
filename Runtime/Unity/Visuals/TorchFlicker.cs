using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// A fire light that breathes: intensity and a little range follow two layers of Perlin noise around their authored
    /// values, phase-shifted per instance so two braziers never pulse together. Visual only; costs one noise sample per frame.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class TorchFlicker : MonoBehaviour
    {
        [Tooltip("Fraction of the base intensity the flicker may take away or add.")] [Range(0f, 0.6f)] public float Amount = 0.18f;
        [Tooltip("Noise speed: higher is a nervous candle, lower a slow coal bed.")] public float Speed = 2.2f;

        private Light _light;
        private float _baseIntensity, _baseRange, _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _baseIntensity = _light.intensity;
            _baseRange = _light.range;
            var p = transform.position;
            _seed = (p.x * 12.9898f + p.z * 78.233f) % 100f;
        }

        private void Update()
        {
            if (_light == null || !_light.enabled) return;
            float t = Time.time * Speed + _seed;
            float n = Mathf.PerlinNoise(t, _seed) * 0.7f + Mathf.PerlinNoise(t * 3.1f, _seed + 17f) * 0.3f;   // 0..1
            float k = 1f + (n - 0.5f) * 2f * Amount;
            _light.intensity = _baseIntensity * k;
            _light.range = _baseRange * (1f + (k - 1f) * 0.25f);
        }
    }
}
