using DungeonGen.Core.Combat;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Reads the New Input System and turns it into a <see cref="PlayerIntent"/> every server tick:
    /// WASD as a world-space direction (screen-relative, the camera never rotates), mouse aim as a point
    /// on the ground in simulator cells, LMB basic, RMB bolt, Q zone, E mobility, R ultimate, Space held
    /// to interact, F to ping, Tab held for the full map (read by MiniMapHud). Rebindable through the
    /// actions asset; controller sticks map to the same actions. Nothing moves locally: the server does.
    /// </summary>
    public sealed class LocalPlayerController : MonoBehaviour, IIntentSource
    {
        public DungeonBuilder Builder;
        public TopDownCamera Camera;
        public ClientWorldView World;
        public int LocalPlayerId = 1;
        public int SendRate = 20;
        [Tooltip("Optional: an Input Actions asset with Move (Vector2), Aim (Vector2 screen), Basic, Bolt, Zone, Mobility, Ultimate, Interact, Ping, Map (Buttons). Left empty, the controller builds the default bindings in code.")]
        public InputActionAsset Actions;

        public int PlayerId => LocalPlayerId;
        public bool MapHeld { get; private set; }

        private InputAction _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map;
        private IMatchTransport _transport;
        private float _accum; private uint _tick;
        private bool _basicQueued, _boltQueued, _zoneQueued, _mobilityQueued, _ultQueued, _pingQueued;

        public void Attach(IMatchTransport transport) => _transport = transport;

        private void OnEnable()
        {
            if (Actions != null)
            {
                var map = Actions.FindActionMap("Player", true);
                _move = map.FindAction("Move", true); _aim = map.FindAction("Aim", true); _basic = map.FindAction("Basic", true); _bolt = map.FindAction("Bolt", true);
                _zone = map.FindAction("Zone", true); _mobility = map.FindAction("Mobility", true); _ultimate = map.FindAction("Ultimate", true); _interact = map.FindAction("Interact", true);
                _ping = map.FindAction("Ping", true); _map = map.FindAction("Map", true);
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
                _map = new InputAction("Map", InputActionType.Button, "<Keyboard>/tab"); _map.AddBinding("<Gamepad>/select");
                foreach (var a in new[] { _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map }) a.Enable();
            }
            _basic.performed += _ => _basicQueued = true; _bolt.performed += _ => _boltQueued = true; _zone.performed += _ => _zoneQueued = true;
            _mobility.performed += _ => _mobilityQueued = true; _ultimate.performed += _ => _ultQueued = true; _ping.performed += _ => _pingQueued = true;
        }

        private void OnDisable()
        {
            if (Actions == null) foreach (var a in new[] { _move, _aim, _basic, _bolt, _zone, _mobility, _ultimate, _interact, _ping, _map }) a?.Disable();
        }

        private void Update()
        {
            MapHeld = _map != null && _map.IsPressed();
            _accum += Time.deltaTime;
            float period = 1f / Mathf.Max(1, SendRate);
            if (_accum < period) return;
            _accum -= period;
            if (TryGetIntent(out var intent) && _transport != null) _transport.SendIntent(intent.ToBytes());
        }

        public bool TryGetIntent(out PlayerIntent intent)
        {
            intent = new PlayerIntent { PlayerId = LocalPlayerId, Tick = ++_tick };
            if (Builder == null || Builder.Layout == null) return false;
            var move = _move.ReadValue<Vector2>();
            // Screen-relative becomes world-relative directly: the camera's yaw is fixed, so up on screen is +Y on the grid.
            intent.MoveX = move.x; intent.MoveY = move.y;
            Vector3 ground = Vector3.zero;
            if (Camera != null && Camera.Camera != null && Camera.ScreenToGround(_aim.ReadValue<Vector2>(), out var hit)) ground = hit;
            var aim = Builder.WorldToSim(ground);
            intent.AimX = aim.X; intent.AimY = aim.Y;
            intent.Basic = _basicQueued || _basic.IsPressed(); intent.Bolt = _boltQueued; intent.Zone = _zoneQueued; intent.Mobility = _mobilityQueued; intent.Ultimate = _ultQueued;
            intent.Interact = _interact.IsPressed(); intent.Ping = _pingQueued; intent.PingKind = (byte)PingKind.Look;
            _basicQueued = _boltQueued = _zoneQueued = _mobilityQueued = _ultQueued = _pingQueued = false;
            return true;
        }

        /// <summary>Keep the camera on the local avatar once the server has spawned it.</summary>
        private void LateUpdate()
        {
            if (Camera != null && Camera.Target == null && World != null && World.LocalAvatar != null) Camera.Target = World.LocalAvatar.transform;
        }
    }
}
