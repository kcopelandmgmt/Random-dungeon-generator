using System;
using DungeonGen.Core.Net;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonGen.Unity.UI
{
    /// <summary>
    /// The main menu's Tutorial screen (wave 5.10), a <see cref="MainMenuUi.ExtraScreen"/>: a short intro card listing the seven lessons with the
    /// player's own key names, what is and is not at stake, Start tutorial and Back. It needs no character (a temporary Warden is seated). It also
    /// answers <see cref="GameFlow.OfferTutorial"/>: a brand-new account's first Play opens this screen once, with a third button to skip
    /// the tutorial and enter the dungeon.
    /// </summary>
    public static class TutorialScreen
    {
        public const string ScreenName = "Tutorial";
        /// <summary>True while the screen is up as the first-Play offer (shows the Skip button).</summary>
        public static bool Offered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            MainMenuUi.Register(new MainMenuUi.ExtraScreen { Name = ScreenName, Label = "Tutorial", Order = 0, NeedsCharacter = false, Build = Build });
            GameFlow.OfferTutorial = Offer;
        }

        /// <summary>Open this screen as the first-Play offer. False when there is no menu to show it on.</summary>
        private static bool Offer()
        {
            var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuUi>();
            if (menu == null || !menu.isActiveAndEnabled) return false;
            Offered = true;
            menu.ShowScreen(ScreenName);
            return true;
        }

        public static void Build(RectTransform screen, GameFlow flow, Action back, Action<string> show)
        {
            var panel = UiStyle.Panel(screen, "TutorialPanel");
            panel.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(1240f, 780f));
            var header = UiStyle.Label(panel, "Header", Offered ? "NEW TO THE DUNGEON?" : "THE TUTORIAL", UiStyle.Size.Title, UiStyle.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            header.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(1000f, 56f));
            UiStyle.Rule(panel, "HeaderRule", 520f, -96f);

            string who = flow.Character != null ? $"You play {UiStyle.Colour(flow.Character.Name, UiStyle.TextMain)}'s class, the {flow.TutorialClass.Name}, in a borrowed starter kit."
                                                 : $"You have no character yet, so you play a {UiStyle.Colour(flow.TutorialClass.Name, UiStyle.TextMain)} who is not saved.";
            var sub = UiStyle.Label(panel, "Sub", "One short guided round, five to eight minutes. Two companions fight beside you; there are no rival teams.\n" + who,
                UiStyle.Size.Body, UiStyle.TextDim, TextAnchor.MiddleCenter);
            sub.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(1120f, 60f));
            sub.lineSpacing = 1.15f;

            // The seven lessons, two columns, each with its number and its keys.
            var lessons = TutorialScript.Lessons;
            float colW = 540f, rowH = 104f, top = 196f;
            for (int i = 0; i < lessons.Length; i++)
            {
                var o = TutorialScript.Objective(lessons[i]);
                int col = i < 4 ? 0 : 1, row = i < 4 ? i : i - 4;
                float x = col == 0 ? -colW / 2f - 20f : colW / 2f + 20f;
                var cell = UiStyle.Panel(panel, "Lesson-" + (i + 1), UiStyle.PanelBgLight, false, false);
                cell.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -(top + row * rowH)), new Vector2(colW, rowH - 12f));
                var num = UiStyle.Img(cell, "Number", UiStyle.GoldDim, UiStyle.Circle);
                num.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(44f, 44f));
                var nt = UiStyle.Label(num.transform, "N", (i + 1).ToString(), UiStyle.Size.Label, UiStyle.Backdrop, TextAnchor.MiddleCenter, FontStyle.Bold, false);
                nt.rectTransform.Stretch();
                var title = UiStyle.Label(cell, "Title", o.Title, UiStyle.Size.Label, UiStyle.TextMain, TextAnchor.UpperLeft, FontStyle.Bold);
                title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -10f), new Vector2(colW - 92f, 30f));
                var text = UiStyle.Label(cell, "Text", TutorialKeys.Fill(o.Text), UiStyle.Size.Small, UiStyle.TextSoft, TextAnchor.UpperLeft);
                text.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -40f), new Vector2(colW - 92f, 48f));
            }

            // The eighth cell: what is at stake (nothing) and the one-time reward.
            {
                var cell = UiStyle.Panel(panel, "Stakes", new Color(0.16f, 0.12f, 0.06f, 0.92f), true, false);
                cell.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(colW / 2f + 20f, -(top + 3 * rowH)), new Vector2(colW, rowH - 12f));
                var title = UiStyle.Label(cell, "Title", "Nothing at risk", UiStyle.Size.Label, UiStyle.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
                title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -10f), new Vector2(colW - 40f, 30f));
                string reward = flow.HasProfile
                    ? (flow.TutorialRewarded ? "You have already earned its reward." : $"Finish it once for {UiStyle.Colour(TutorialScript.RewardSilver + " silver", UiStyle.Gold)}.")
                    : "Your account starts when you create a character.";
                var text = UiStyle.Label(cell, "Text", "Tutorial gear is not real loot: nothing found is kept, nothing of yours is lost. " + reward,
                    UiStyle.Size.Small, UiStyle.TextSoft, TextAnchor.UpperLeft);
                text.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -40f), new Vector2(colW - 40f, 48f));
            }

            var backBtn = UiStyle.MakeButton(panel, "BackButton", "Back", () => { if (Offered) flow.Message = null; Offered = false; back(); }, UiStyle.Size.Heading);
            ((RectTransform)backBtn.transform).Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(260f, 64f));
            if (Offered && flow.Character != null)
            {
                var skip = UiStyle.MakeButton(panel, "SkipTutorialButton", "Skip: enter the dungeon", () =>
                {
                    Offered = false;
                    var why = flow.Play();
                    if (why != null) { flow.Message = why; UiSound.Play(UiSound.Error); show(ScreenName); }
                }, UiStyle.Size.Label);
                ((RectTransform)skip.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-440f, 36f), new Vector2(330f, 64f));
            }
            var start = UiStyle.MakeButton(panel, "StartTutorialButton", "Start tutorial", () =>
            {
                Offered = false;
                var why = flow.PlayTutorial();
                if (why != null) { flow.Message = why; UiSound.Play(UiSound.Error); show(ScreenName); }
            }, UiStyle.Size.Heading);
            ((RectTransform)start.transform).Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(380f, 64f));
            start.GetComponent<UiButtonSound>().ClickKey = UiSound.Confirm;
            // Start is the obvious next step: give it the keyboard / gamepad focus.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(start.gameObject);
        }
    }
}
