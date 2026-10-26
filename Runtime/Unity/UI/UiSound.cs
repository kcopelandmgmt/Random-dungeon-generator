using UnityEngine;
using UnityEngine.EventSystems;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// Interface sounds through the scene's <see cref="GameAudio"/> (<c>ui_click</c>, <c>ui_hover</c>, <c>ui_confirm</c>, <c>ui_error</c>,
    /// <c>countdown_tick</c>). Silent when the scene has no GameAudio; GameAudio itself honours <c>-mute</c> and the volumes.
    /// </summary>
    public static class UiSound
    {
        public const string Click = "ui_click", Hover = "ui_hover", Confirm = "ui_confirm", Error = "ui_error", CountdownTick = "countdown_tick";

        private static GameAudio _audio;

        public static void Play(string key)
        {
            if (_audio == null) _audio = Object.FindFirstObjectByType<GameAudio>();
            if (_audio != null) _audio.Play2D(key);
        }
    }

    /// <summary>On every button: hover and click sounds (click, or a custom key such as confirm).</summary>
    public sealed class UiButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public string ClickKey = UiSound.Click;
        public void OnPointerEnter(PointerEventData e) { var b = GetComponent<UnityEngine.UI.Selectable>(); if (b == null || b.interactable) UiSound.Play(UiSound.Hover); }
        public void OnPointerClick(PointerEventData e) { if (!string.IsNullOrEmpty(ClickKey)) UiSound.Play(ClickKey); }
    }
}
