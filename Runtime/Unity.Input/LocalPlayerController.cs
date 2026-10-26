using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Reads the New Input System and turns it into a <see cref="PlayerIntent"/> every server tick:
    /// WASD as a screen-relative direction turned into world space under the camera's fixed yaw, mouse aim as a point
    /// on the ground in simulator cells, LMB basic, RMB bolt, Q zone, E mobility, R ultimate, 1 health potion, 2 antidote, Space held
    /// to interact, F to ping, Tab held for the full map (pushed into MiniMapHud). The base assembly
    /// reads no input, so this component also feeds the camera's aim point and scroll-wheel zoom each frame. Rebindable through the
    /// actions asset; controller sticks map to the same actions. Nothing moves locally: the server does.
    /// </summary>
    public sealed class LocalPlayerController : MonoBehaviour, IIntentSource
    {
        public DungeonBuilder Builder;
        public TopDownCamera Camera;
        public ClientWorldView World;
        [Tooltip("Optional: receives FullMapHeld while the Map action is pressed.")]
        public MiniMapHud MiniMap;
        public int LocalPlayerId = 1;
        public int SendRate = 20;
        [Tooltip("Optional: an Input Actions asset with Move (Vector2), Aim (Vector2 screen), Basic, Bolt, Zone, Mobility, Ultimate, Interact, Ping, Map (Buttons), Zoom (Axis, scroll) and optionally ZoomHold (Axis, held). Left empty, the controller builds the default bindings in code.")]
        public InputActionAsset Actions;

        public int PlayerId => LocalPlayerId;
        public bool MapHeld { get; private set; }

        private InputAction _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map, _zoom, _zoomHold;
        [Tooltip("Zoom notches per second while a held zoom control (gamepad d-pad) is pressed.")]
        public float HeldZoomNotchesPerSecond = 6f;
        private IMatchTransport _transport;
        private float _accum; private uint _tick;
        private bool _basicQueued, _boltQueued, _zoneQueued, _mobilityQueued, _ultQueued, _pingQueued;
        private InputAction _potion, _antidote;
        private byte _consumableQueued;   // PlayerIntent.ConsumableSlot: 1 Health Potion, 2 Antidote

        public void Attach(IMatchTransport transport) => _transport = transport;

        private void OnEnable()
        {
            if (Actions != null)
            {
                var map = Actions.FindActionMap("Player", true);
                _move = map.FindAction("Move", true); _aim = map.FindAction("Aim", true); _basic = map.FindAction("Basic", true); _bolt = map.FindAction("Bolt", true);
                _zone = map.FindAction("Zone", true); _mobility = map.FindAction("Mobility", true); _ultimate = map.FindAction("Ultimate", true); _interact = map.FindAction("Interact", true);
                _ping = map.FindAction("Ping", true); _map = map.FindAction("Map", true);
                _zoom = map.FindAction("Zoom", false); _zoomHold = map.FindAction("ZoomHold", false);
                map.Enable();
            }
            else
            {
                _move = new InputAction("Move", InputActionType.Value);
                _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                _move.AddBinding("<Gamepad>/leftStick");
                _aim = new InputAction("Aim", InputActionType.Value, "<Mouse>/position");
                _basic = new InputAction("Basic", InputActionType.Button, "<Mouse>/leftButton"); _basic.AddBinding("<Gamepad>/rightTrigger");
                _bolt = new InputAction("Bolt", InputActionType.Button, "<Mouse>/rightButton"); _bolt.AddBinding("<Gamepad>/leftTrigger");
                _zone = new InputAction("Zone", InputActionType.Button, "<Keyboard>/q"); _zone.AddBinding("<Gamepad>/rightShoulder");
                _mobility = new InputAction("Mobility", InputActionType.Button, "<Keyboard>/e"); _mobility.AddBinding("<Gamepad>/leftShoulder");
                _ultimate = new InputAction("Ultimate", InputActionType.Button, "<Keyboard>/r"); _ultimate.AddBinding("<Gamepad>/buttonNorth");
                _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/space"); _interact.AddBinding("<Gamepad>/buttonSouth");
                _ping = new InputAction("Ping", InputActionType.Button, "<Keyboard>/f"); _ping.AddBinding("<Gamepad>/buttonWest");
                _potion = new InputAction("Potion", InputActionType.Button, "<Keyboard>/1"); _potion.AddBinding("<Gamepad>/dpad/left");
                _antidote = new InputAction("Antidote", InputActionType.Button, "<Keyboard>/2"); _antidote.AddBinding("<Gamepad>/dpad/right");
                _potion.Enable(); _antidote.Enable();
                _map = new InputAction("Map", InputActionType.Button, "<Keyboard>/tab"); _map.AddBinding("<Gamepad>/select");
                _zoom = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
                _zoomHold = new InputAction("ZoomHold", InputActionType.Value, "<Gamepad>/dpad/y");
                foreach (var a in new[] { _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map, _zoom, _zoomHold }) a.Enable();
            }
            _basic.performed += _ => _basicQueued = true; _bolt.performed += _ => _boltQueued = true; _zone.performed += _ => _zoneQueued = true;
            _mobility.performed += _ => _mobilityQueued = true; _ultimate.performed += _ => _ultQueued = true; _ping.performed += _ => _pingQueued = true;
            if (_potion != null) _potion.performed += _ => _consumableQueued = 1;
            if (_antidote != null) _antidote.performed += _ => _consumableQueued = 2;
        }

        private void OnDisable()
        {
            if (Actions == null) foreach (var a in new[] { _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map, _zoom, _zoomHold }) a?.Disable();
            _potion?.Disable(); _antidote?.Disable();
        }

        private void Update()
        {
            FeedZoom();
            MapHeld = _map != null && _map.IsPressed();
            if (MiniMap != null) MiniMap.FullMapHeld = MapHeld;
            if (Camera != null && _aim != null) Camera.AimScreenPosition = _aim.ReadValue<Vector2>();
            _accum += Time.deltaTime;
            float period = 1f / Mathf.Max(1, SendRate);
            if (_accum < period) return;
            _accum -= period;
            if (TryGetIntent(out var intent) && _transport != null) _transport.SendIntent(intent.ToBytes());
        }

        /// <summary>Scroll notches (Windows reports 120 per notch, some platforms 1) and the held gamepad axis go to the camera rig.</summary>
        private void FeedZoom()
        {
            if (Camera == null) return;
            float scroll = _zoom != null ? _zoom.ReadValue<float>() : 0f;
            if (Mathf.Abs(scroll) >= 1f) Camera.Zoom(Mathf.Sign(scroll) * Mathf.Max(1f, Mathf.Round(Mathf.Abs(scroll) / 120f)));
            else if (Mathf.Abs(scroll) > 0.001f) Camera.Zoom(scroll);
            float held = _zoomHold != null ? _zoomHold.ReadValue<float>() : 0f;
            if (Mathf.Abs(held) > 0.2f) Camera.Zoom(held * HeldZoomNotchesPerSecond * Time.deltaTime);
        }

        public bool TryGetIntent(out PlayerIntent intent)
        {
            intent = new PlayerIntent { PlayerId = LocalPlayerId, Tick = ++_tick };
            if (Builder == null || Builder.Layout == null) return false;
            var move = _move.ReadValue<Vector2>();
            // Up on screen is the camera's forward; the rig rotates the stick by its fixed yaw (45 degrees: north-east).
            var world = Camera != null ? Camera.ScreenToWorldDirection(move) : move;
            intent.MoveX = world.x; intent.MoveY = world.y;
            Vector3 ground = Vector3.zero;
            if (Camera != null && Camera.Camera != null && Camera.ScreenToGround(_aim.ReadValue<Vector2>(), out var hit)) ground = hit;
            var aim = Builder.WorldToSim(ground);
            intent.AimX = aim.X; intent.AimY = aim.Y;
            intent.Basic = _basicQueued || _basic.IsPressed(); intent.Bolt = _boltQueued; intent.Zone = _zoneQueued; intent.Mobility = _mobilityQueued; intent.Ultimate = _ultQueued;
            intent.Interact = _interact.IsPressed(); intent.Ping = _pingQueued; intent.PingKind = (byte)PingKind.Look;
            intent.ConsumableSlot = _consumableQueued;
            PendingItemActions.ApplyTo(ref intent);   // item choices clicked in the UI ride this intent
            _basicQueued = _boltQueued = _zoneQueued = _mobilityQueued = _ultQueued = _pingQueued = false; _consumableQueued = 0;
            return true;
        }

        /// <summary>Keep the camera on the local avatar once the server has spawned it.</summary>
        private void LateUpdate()
        {
            if (Camera != null && Camera.Target == null && World != null && World.LocalAvatar != null) Camera.Target = World.LocalAvatar.transform;
        }
    }
}
