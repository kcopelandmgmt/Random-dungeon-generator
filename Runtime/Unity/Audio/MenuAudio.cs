using UnityEngine;

namespace DungeonGen.Unity
{
    /// <summary>
    /// Sound for the main menu, where there is no match: a looping music bed (<see cref="MusicKey"/>, faded in on unscaled time)
    /// and, through a sibling <see cref="GameAudio"/> that it ensures, the interface one-shots <c>UiSound</c> asks for (GameAudio's
    /// <c>Play2D</c> works without a round; its round layers stay idle with no world view). Honours <c>-mute</c> (nothing ever
    /// plays; the music request is still recorded in <see cref="CurrentMusic"/>) and <see cref="GameAudioSettings"/> volumes.
    /// Wired into MainMenu.unity by <c>Editor/MenuSceneBuilder</c>.
    /// </summary>
    public sealed class MenuAudio : MonoBehaviour
    {
        public AudioLibrary Library;
        public string MusicKey = "music_exploration";
        public float FadeInSeconds = 2f;

        /// <summary>The music bed asked for (set even when muted, for tests and smoke runs).</summary>
        public string CurrentMusic { get; private set; }
        public bool Muted => LaunchFlags.Mute;
        public bool MusicPlaying => _music != null && _music.isPlaying;

        private AudioSource _music;
        private float _entryVolume = 1f, _fade;

        private void Awake()
        {
            var ui = GetComponent<GameAudio>();
            if (ui == null) ui = gameObject.AddComponent<GameAudio>();
            if (ui.Library == null) ui.Library = Library;
            var go = new GameObject("MenuMusic");
            go.transform.SetParent(transform, false);
            _music = go.AddComponent<AudioSource>();
            _music.playOnAwake = false; _music.loop = true; _music.spatialBlend = 0f; _music.volume = 0f;
        }

        private void OnEnable() { GameAudioSettings.Changed += Apply; }
        private void OnDisable() { GameAudioSettings.Changed -= Apply; }

        private void Start()
        {
            CurrentMusic = MusicKey;
            if (Muted || Library == null) return;
            var clip = Library.Pick(MusicKey, out _entryVolume);
            if (clip == null) return;
            _music.clip = clip; _music.volume = 0f; _fade = 0f;
            _music.Play();
        }

        private void Update()
        {
            if (Muted) { if (_music.isPlaying) _music.Stop(); return; }
            if (!_music.isPlaying) return;
            _fade = FadeInSeconds <= 0f ? 1f : Mathf.MoveTowards(_fade, 1f, Time.unscaledDeltaTime / FadeInSeconds);
            Apply();
        }

        private void Apply()
        {
            if (_music == null) return;
            _music.volume = Muted ? 0f : Mathf.Clamp01(_fade * _entryVolume * GameAudioSettings.Master * GameAudioSettings.Music);
        }
    }
}
