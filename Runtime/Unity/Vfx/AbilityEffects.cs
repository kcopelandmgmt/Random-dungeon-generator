using System;
using System.Collections.Generic;
using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Draws the effects a team's snapshot carries: a cast (prefab at the caster, a projectile that flies from the
    /// caster to where the server says it stopped, an impact there), hits and heals (floating numbers), and zones
    /// (a prefab on the ground for as long as the server keeps sending the zone). Prefabs come from the
    /// <see cref="AbilityVfxLibrary"/>; with none assigned a greybox shape stands in so every ability is visible
    /// from day one. The audition tool can override the prefab for one ability and role through <see cref="Override"/>.
    /// Nothing here decides anything: the server already resolved every hit before the client hears of it.
    /// </summary>
    public sealed class AbilityEffects : MonoBehaviour
    {
        public ClientWorldView World;
        public DungeonBuilder Builder;
        public AbilityVfxLibrary Library;
        [Tooltip("Draw spheres, discs and wedges for anything without a prefab.")]
        public bool GreyboxFallbacks = true;
        [Tooltip("Seconds a spawned effect lives when its particle systems do not say.")]
        public float DefaultLifetimeSeconds = 3f;
        public float AvatarHeight = 1f;
        public bool FloatingNumbers = true;

        /// <summary>abilityId, role → prefab, or null to use the library. Set by the audition tool.</summary>
        public Func<string, VfxRole, GameObject> Override;
        /// <summary>Every effect as it arrives, for anything else that wants to react (sound, camera shake).</summary>
        public event Action<EffectSnapshot> EffectArrived;

        private sealed class Flight { public Transform T; public Vector3 From, To; public float Speed, Started; public string Ability; public GameObject Impact; }
        private sealed class LiveZone { public GameObject Go; public float Until, LastSeen; }
        private sealed class Number { public Vector3 World; public string Text; public Color Colour; public float Born; }

        private readonly List<Flight> _flights = new List<Flight>();
        private readonly Dictionary<string, LiveZone> _zones = new Dictionary<string, LiveZone>();
        private readonly List<Number> _numbers = new List<Number>();
        private ClientWorldView _subscribed;
        private float _simTime;
        private static Material _greyMat;

        private void Update()
        {
            if (World != _subscribed)
            {
                if (_subscribed != null) _subscribed.SnapshotApplied -= OnSnapshot;
                _subscribed = World;
                if (_subscribed != null) _subscribed.SnapshotApplied += OnSnapshot;
            }
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                if (f.T == null) { _flights.RemoveAt(i); continue; }
                float dist = Vector3.Distance(f.From, f.To);
                float t = dist <= 0.01f ? 1f : Mathf.Clamp01((Time.time - f.Started) * f.Speed / dist);
                f.T.position = Vector3.Lerp(f.From, f.To, t);
                if (t >= 1f)
                {
                    Destroy(f.T.gameObject);
                    _flights.RemoveAt(i);
                    if (f.Impact != null) Spawn(f.Impact, f.To, f.T.rotation, 1f);
                    else if (GreyboxFallbacks) GreyboxFlash(f.To, 0.6f, new Color(1f, 0.9f, 0.5f));
                }
            }
            foreach (var key in new List<string>(_zones.Keys))
            {
                var z = _zones[key];
                if (z.Go == null || _simTime >= z.Until || Time.time - z.LastSeen > 0.5f) { Stop(z.Go); _zones.Remove(key); }
            }
            _numbers.RemoveAll(n => Time.time - n.Born > 1.2f);
        }

        private void OnDisable() { if (_subscribed != null) _subscribed.SnapshotApplied -= OnSnapshot; _subscribed = null; }

        private void OnSnapshot(TeamSnapshot snap)
        {
            _simTime = snap.Time;
            if (Builder == null) return;
            foreach (var e in snap.Effects)
            {
                EffectArrived?.Invoke(e);
                switch (e.Kind)
                {
                    case EffectKind.Cast: DrawCast(e); break;
                    case EffectKind.Hit: DrawNumber(e, e.Amount > 0f ? $"{e.Amount:0}" : "0", e.Target == World?.LocalPlayerId ? new Color(1f, 0.45f, 0.45f) : Color.white); break;
                    case EffectKind.Heal: DrawNumber(e, $"+{e.Amount:0}", new Color(0.5f, 1f, 0.5f)); break;
                    case EffectKind.Zone: DrawZone(e); break;
                }
            }
        }

        private GameObject PrefabFor(string ability, VfxRole role)
        {
            var o = Override?.Invoke(ability, role);
            if (o != null) return o;
            return Library != null ? Library.Prefab(ability, role) : null;
        }

        private Vector3 Ground(float x, float y) => Builder.SimToWorld(new Vec2(x, y));

        private void DrawCast(EffectSnapshot e)
        {
            var from = Ground(e.X, e.Y) + Vector3.up * AvatarHeight;
            var to = Ground(e.X2, e.Y2) + Vector3.up * AvatarHeight;
            var dir = to - from; dir.y = 0f;
            var rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
            AbilityBook.TryGet(e.Ability, out var def);
            var shape = def?.Shape ?? AbilityShape.Projectile;

            var cast = PrefabFor(e.Ability, VfxRole.Cast);
            if (cast != null) Spawn(cast, from, rot, 1f);

            var projectile = PrefabFor(e.Ability, VfxRole.Projectile);
            var impact = PrefabFor(e.Ability, VfxRole.Impact);
            bool flies = shape == AbilityShape.Projectile || shape == AbilityShape.Pierce || (projectile != null && dir.magnitude > 1.5f);
            if (flies)
            {
                GameObject go = projectile != null ? Instantiate(projectile, from, rot) : GreyboxFallbacks ? GreyboxSphere(from, 0.5f, shape == AbilityShape.Pierce ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.8f, 0.3f)) : null;
                if (go != null) _flights.Add(new Flight { T = go.transform, From = from, To = to, Speed = Library != null ? Library.ProjectileSpeed(e.Ability) : 25f, Started = Time.time, Ability = e.Ability, Impact = impact });
                return;
            }
            switch (shape)
            {
                case AbilityShape.Cone:
                    if (impact != null) Spawn(impact, from, rot, 1f);
                    else if (GreyboxFallbacks && def != null) GreyboxWedge(from, rot, def.RangeCells * Builder.CellSize, def.Size);
                    break;
                case AbilityShape.Circle:
                    if (def != null && def.IsZone) break;                       // the zone itself arrives every tick as EffectKind.Zone
                    if (impact != null) Spawn(impact, Ground(e.X2, e.Y2), Quaternion.identity, 1f);
                    else if (GreyboxFallbacks && def != null) GreyboxDisc(Ground(e.X2, e.Y2), def.Size * Builder.CellSize, new Color(1f, 0.5f, 0.2f, 0.8f), 0.5f);
                    break;
                case AbilityShape.Dash:
                    if (impact != null) Spawn(impact, to, rot, 1f);
                    else if (GreyboxFallbacks) GreyboxTrail(from, to);
                    break;
                default:                                                         // Self and Target: on the body it touched
                    if (impact != null) Spawn(impact, to, rot, 1f);
                    else if (GreyboxFallbacks) GreyboxFlash(to, 0.8f, def != null && def.Heal > 0f ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 1f, 1f));
                    break;
            }
        }

        private void DrawZone(EffectSnapshot e)
        {
            string key = $"{e.Source}:{e.Ability}:{e.X:0.#}:{e.Y:0.#}";
            if (_zones.TryGetValue(key, out var live) && live.Go != null) { live.LastSeen = Time.time; live.Until = e.Until; return; }
            var centre = Ground(e.X, e.Y) + Vector3.up * 0.06f;
            float radiusMetres = e.Amount * Builder.CellSize;
            var prefab = PrefabFor(e.Ability, VfxRole.Zone);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, centre, Quaternion.identity);
                float s = radiusMetres * (Library != null ? Library.ZoneScalePerMetre(e.Ability) : 1f);
                if (s > 0f) go.transform.localScale = prefab.transform.localScale * s;
            }
            else if (GreyboxFallbacks) go = GreyboxDisc(centre, radiusMetres, new Color(0.3f, 0.6f, 1f, 0.5f), 0f);
            else return;
            _zones[key] = new LiveZone { Go = go, Until = e.Until, LastSeen = Time.time };
        }

        private void DrawNumber(EffectSnapshot e, string text, Color colour)
        {
            if (!FloatingNumbers) return;
            _numbers.Add(new Number { World = Ground(e.X, e.Y) + Vector3.up * (AvatarHeight * 2f), Text = text, Colour = colour, Born = Time.time });
            if (GreyboxFallbacks) GreyboxFlash(Ground(e.X, e.Y) + Vector3.up * AvatarHeight, 0.4f, colour);
        }

        private void OnGUI()
        {
            if (_numbers.Count == 0) return;
            var cam = Camera.main; if (cam == null) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            foreach (var n in _numbers)
            {
                float age = Time.time - n.Born;
                var sp = cam.WorldToScreenPoint(n.World + Vector3.up * age * 1.5f);
                if (sp.z < 0f) continue;
                var c = n.Colour; c.a = 1f - age / 1.2f;
                GUI.color = c;
                GUI.Label(new Rect(sp.x - 40f, Screen.height - sp.y - 10f, 80f, 20f), n.Text, style);
            }
            GUI.color = Color.white;
        }

        // ---- spawning and lifetimes ----

        /// <summary>Instantiate a library prefab and let it die by itself: looping particle systems are stopped after their duration, the object removed once every system is quiet.</summary>
        public GameObject Spawn(GameObject prefab, Vector3 at, Quaternion rot, float scale)
        {
            var go = Instantiate(prefab, at, rot);
            if (Math.Abs(scale - 1f) > 0.001f) go.transform.localScale = prefab.transform.localScale * scale;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float life = DefaultLifetimeSeconds;
            foreach (var ps in systems)
            {
                var main = ps.main;
                float d = main.duration + main.startLifetime.constantMax + main.startDelay.constantMax;
                if (!main.loop) life = Mathf.Max(life, d);
                if (!ps.isPlaying) ps.Play();
            }
            StartCoroutine(Retire(go, systems, life));
            return go;
        }

        private System.Collections.IEnumerator Retire(GameObject go, ParticleSystem[] systems, float after)
        {
            yield return new WaitForSeconds(after);
            if (go == null) yield break;
            foreach (var ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitForSeconds(2f);
            if (go != null) Destroy(go);
        }

        private void Stop(GameObject go)
        {
            if (go == null) return;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0) { Destroy(go); return; }
            StartCoroutine(Retire(go, systems, 0f));
        }

        // ---- greybox stand-ins ----

        private static Material GreyMat()
        {
            if (_greyMat != null) return _greyMat;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _greyMat = new Material(shader);
            return _greyMat;
        }

        private GameObject Primitive(PrimitiveType type, Vector3 at, Quaternion rot, Vector3 scale, Color colour, float life)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.SetPositionAndRotation(at, rot);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null) { r.sharedMaterial = GreyMat(); var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", colour); block.SetColor("_Color", colour); r.SetPropertyBlock(block); }
            if (life > 0f) Destroy(go, life);
            return go;
        }

        private GameObject GreyboxSphere(Vector3 at, float diameter, Color colour) => Primitive(PrimitiveType.Sphere, at, Quaternion.identity, Vector3.one * diameter, colour, 0f);
        private void GreyboxFlash(Vector3 at, float diameter, Color colour) => Primitive(PrimitiveType.Sphere, at, Quaternion.identity, Vector3.one * diameter, colour, 0.25f);
        private GameObject GreyboxDisc(Vector3 at, float radius, Color colour, float life) => Primitive(PrimitiveType.Cylinder, at, Quaternion.identity, new Vector3(radius * 2f, 0.03f, radius * 2f), colour, life);
        private void GreyboxWedge(Vector3 from, Quaternion rot, float length, float degrees)
        {
            float width = 2f * length * Mathf.Tan(degrees * 0.5f * Mathf.Deg2Rad);
            Primitive(PrimitiveType.Cube, from + rot * Vector3.forward * (length * 0.5f) + Vector3.down * (AvatarHeight - 0.06f), rot, new Vector3(Mathf.Max(0.5f, width * 0.6f), 0.03f, length), new Color(1f, 0.7f, 0.3f, 0.8f), 0.3f);
        }
        private void GreyboxTrail(Vector3 from, Vector3 to)
        {
            var mid = (from + to) * 0.5f; var d = to - from;
            Primitive(PrimitiveType.Cube, mid, d.sqrMagnitude > 0.001f ? Quaternion.LookRotation(d.normalized) : Quaternion.identity, new Vector3(0.3f, 0.3f, Mathf.Max(0.3f, d.magnitude)), new Color(0.8f, 0.8f, 1f, 0.8f), 0.3f);
        }
    }
}
