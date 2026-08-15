using System;
using UnityEngine;

namespace HKSaveBackup
{
    /// <summary>
    /// The death-prompt UI, and the mod's persistent coroutine host.
    ///
    /// Deliberately an IMGUI (OnGUI) overlay rather than a vanilla MenuScreen: the prompt has to
    /// appear mid-death, while the hero is dead, the pause menu is disabled (HeroController.Die
    /// sets disablePause) and the game is between UI states. Driving UIManager's state machine
    /// from there would mean leaving the vanilla UI in a state the game never produces on its
    /// own; an overlay owns nothing but its own pixels, and can be dropped at any time without
    /// the game noticing. Input is polled directly for the same reason — the UI event system is
    /// not routing navigation events during the death sequence.
    ///
    /// Everything here is best-effort: if drawing or input reading throws, the prompt times out
    /// and the death resolves exactly as vanilla would.
    /// </summary>
    internal sealed class SalvagePromptOverlay : MonoBehaviour
    {
        internal enum Choice
        {
            None = 0,
            Salvage,
            LetItDie,
        }

        /// <summary>Ignore input for a moment: the player is mid-death and may still be mashing.</summary>
        private const float InputArmDelaySeconds = 0.6f;

        private bool _prompting;
        private float _armedAtRealtime;
        private float _deadlineRealtime;
        private string _detail = "";
        private Choice _choice;

        private string _notice;
        private float _noticeUntilRealtime;

        private Texture2D _panelTexture;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _hintStyle;

        public bool IsPrompting => _prompting;

        /// <summary>The player's answer, or None while the prompt is still open.</summary>
        public Choice Result => _choice;

        public void BeginPrompt(string detail, float seconds)
        {
            _detail = detail ?? "";
            _choice = Choice.None;
            _armedAtRealtime = Time.realtimeSinceStartup;
            _deadlineRealtime = _armedAtRealtime + Mathf.Max(1f, seconds);
            _notice = null;
            _prompting = true;
        }

        public void EndPrompt()
        {
            _prompting = false;
        }

        /// <summary>Transient status line, shown after the choice is made (survives scene loads).</summary>
        public void ShowNotice(string text, float seconds)
        {
            _notice = text;
            _noticeUntilRealtime = Time.realtimeSinceStartup + Mathf.Max(1f, seconds);
        }

        private void Update()
        {
            if (!_prompting || _choice != Choice.None)
                return;

            if (Time.realtimeSinceStartup >= _deadlineRealtime)
            {
                // Timeout resolves the way vanilla would have: the run dies.
                _choice = Choice.LetItDie;
                return;
            }

            if (Time.realtimeSinceStartup - _armedAtRealtime < InputArmDelaySeconds)
                return;

            if (ReadKeyboard(ref _choice))
                return;
            ReadController(ref _choice);
        }

        private static bool ReadKeyboard(ref Choice choice)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.Y) || Input.GetKeyDown(KeyCode.Return) ||
                    Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    choice = Choice.Salvage;
                    return true;
                }
                if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.Escape))
                {
                    choice = Choice.LetItDie;
                    return true;
                }
            }
            catch (Exception)
            {
                // No keyboard input available; the controller path and the timeout still apply.
            }
            return false;
        }

        private static bool ReadController(ref Choice choice)
        {
            try
            {
                InputHandler ih = InputHandler.Instance;
                HeroActions actions = ih != null ? ih.inputActions : null;
                if (actions == null)
                    return false;

                // menuSubmit / menuCancel keep updating during the death sequence: InputHandler's
                // StopUIInput only clears flags, it never disables the InControl action set.
                if (actions.menuSubmit != null && actions.menuSubmit.WasPressed)
                {
                    choice = Choice.Salvage;
                    return true;
                }
                if (actions.menuCancel != null && actions.menuCancel.WasPressed)
                {
                    choice = Choice.LetItDie;
                    return true;
                }
            }
            catch (Exception)
            {
                // Controller state unavailable; keyboard and the timeout still apply.
            }
            return false;
        }

        private void OnGUI()
        {
            try
            {
                bool showNotice = _notice != null && Time.realtimeSinceStartup < _noticeUntilRealtime;
                if (!_prompting && !showNotice)
                    return;

                EnsureStyles();
                GUI.depth = -1000;

                if (_prompting)
                    DrawPrompt();
                else
                    DrawNotice();
            }
            catch (Exception)
            {
                // A drawing failure must not spam the log every frame or break the death flow;
                // the prompt still resolves on its timeout.
            }
        }

        private void DrawPrompt()
        {
            float width = Mathf.Min(Screen.width * 0.78f, 1400f);
            float height = Screen.height * 0.32f;
            var panel = new Rect((Screen.width - width) / 2f, Screen.height * 0.30f, width, height);

            GUI.DrawTexture(panel, _panelTexture);

            float pad = width * 0.04f;
            var inner = new Rect(panel.x + pad, panel.y + pad, panel.width - 2f * pad, panel.height - 2f * pad);
            float line = inner.height / 4f;

            GUI.Label(new Rect(inner.x, inner.y, inner.width, line),
                "STEEL SOUL DEATH", _titleStyle);
            GUI.Label(new Rect(inner.x, inner.y + line, inner.width, line),
                _detail, _bodyStyle);

            int secondsLeft = Mathf.Max(0, Mathf.CeilToInt(_deadlineRealtime - Time.realtimeSinceStartup));
            GUI.Label(new Rect(inner.x, inner.y + 2f * line, inner.width, line),
                "[Y] / [Enter] / (A)  Salvage the run        [N] / [Esc] / (B)  Let it die",
                _bodyStyle);
            GUI.Label(new Rect(inner.x, inner.y + 3f * line, inner.width, line),
                $"Letting it die in {secondsLeft}s. Salvage returns you to the main menu with your last save intact.",
                _hintStyle);
        }

        private void DrawNotice()
        {
            float width = Mathf.Min(Screen.width * 0.78f, 1400f);
            float height = Screen.height * 0.10f;
            var panel = new Rect((Screen.width - width) / 2f, Screen.height * 0.06f, width, height);

            GUI.DrawTexture(panel, _panelTexture);
            GUI.Label(new Rect(panel.x + 20f, panel.y, panel.width - 40f, panel.height), _notice, _bodyStyle);
        }

        private void EnsureStyles()
        {
            if (_panelTexture == null)
            {
                _panelTexture = new Texture2D(1, 1);
                _panelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
                _panelTexture.Apply();
                _panelTexture.hideFlags = HideFlags.HideAndDontSave;
            }

            int bodySize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.026f));
            if (_titleStyle == null || _bodyStyle.fontSize != bodySize)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.RoundToInt(bodySize * 1.35f),
                    wordWrap = false,
                };
                _titleStyle.normal.textColor = new Color(1f, 0.92f, 0.75f);

                _bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = bodySize,
                    wordWrap = true,
                };
                _bodyStyle.normal.textColor = Color.white;

                _hintStyle = new GUIStyle(_bodyStyle)
                {
                    fontSize = Mathf.Max(10, Mathf.RoundToInt(bodySize * 0.8f)),
                };
                _hintStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
            }
        }

        private void OnDestroy()
        {
            if (_panelTexture != null)
                Destroy(_panelTexture);
        }
    }
}
