using FishingZone.Core;
using FishingZone.Networking;
using FishingZone.Roles;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace FishingZone.UI
{
    /// <summary>
    /// Button actions and crew display for the Lobby screen.
    /// It asks <see cref="GameFlowManager"/> for a state change and never touches SceneManager,
    /// so the lobby holds no knowledge of which scene comes next, and it reads the crew from
    /// <see cref="CrewRoster"/> rather than keeping any list of its own.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField]
        private CrewRoster _crewRoster;

        /// <summary>One label per crew slot, in order. Slots with nobody in them read as empty.</summary>
        [SerializeField]
        private TMP_Text[] _slotLabels;

        [SerializeField]
        private Button _readyButton;

        [SerializeField]
        private TMP_Text _readyButtonLabel;

        [SerializeField]
        private Button _startButton;

        // The three role buttons, so a job the crew has filled can stop inviting a press that the
        // server would only refuse. Every one of these may be left unassigned: the buttons then
        // simply stay enabled and the server does the refusing on its own, which is the arrangement
        // that held before they existed.
        [SerializeField]
        private Button _navigatorButton;

        [SerializeField]
        private Button _fisherButton;

        [SerializeField]
        private Button _observerButton;

        // One label per role, each drawing a mark for every place that job has: one for Navigator,
        // two for Fisher, one for Observer. They say how full a job is, never who filled it, so the
        // same text is correct on every screen and nothing here needs to know which player is local.
        // All three are optional, like the buttons above.
        [SerializeField]
        private TMP_Text _navigatorSlots;

        [SerializeField]
        private TMP_Text _fisherSlots;

        [SerializeField]
        private TMP_Text _observerSlots;

        // Serialized rather than written into the code because the two obvious choices are outside
        // the character range a static font atlas usually carries, and a missing glyph renders as a
        // box. Being able to type an ASCII pair straight into the Inspector turns that from a
        // recompile into a moment's work, and lets the marks be restyled without touching this file.
        [SerializeField]
        private string _filledSlotSymbol = "✓";

        [SerializeField]
        private string _emptySlotSymbol = "○";

        /// <summary>Placed between marks, so two of them read as two rather than as one wide one.</summary>
        [SerializeField]
        private string _slotSymbolSeparator = " ";

        [SerializeField]
        private string _emptySlotText = "Empty";

        private void OnEnable()
        {
            if (_crewRoster != null)
            {
                _crewRoster.RosterChanged += Refresh;
            }

            // The roster may have been populated before this enabled, so the current state is read
            // once rather than waiting for the next change.
            Refresh();
        }

        private void OnDisable()
        {
            if (_crewRoster != null)
            {
                _crewRoster.RosterChanged -= Refresh;
            }
        }

        /// <summary>
        /// Wired to the START button's On Click () list, which is why this is public.
        /// The manager is resolved per click rather than cached: it lives on the persistent
        /// services object from the Bootstrap scene, and Unity cannot serialize a reference
        /// across scenes. A dictionary lookup on a button press costs nothing.
        /// </summary>
        public void StartGame()
        {
            // Checked here as well as on the button, because an uninteractable button is a courtesy
            // rather than a rule: the method can still be reached by other wiring.
            if (!IsLocalPlayerHost())
            {
                GameLog.Warn(LogCategory.Network, "Ignored Start Mission: only the host starts the mission.");
                return;
            }

            if (_crewRoster != null && !_crewRoster.CanStartMission)
            {
                // Which of the two conditions failed, because "nothing happened" is a poor answer
                // when the fix is different in each case. Readiness is reported first when both are
                // outstanding, since it is the one the crew is usually working on.
                GameLog.Warn(LogCategory.Network, _crewRoster.AllReady
                    ? "Ignored Start Mission: the crew has no Navigator."
                    : "Ignored Start Mission: the crew is not all ready.");
                return;
            }

            GoTo(GameState.Port);
        }

        /// <summary>Wired to the BACK button's On Click () list.</summary>
        public void Back()
        {
            GoTo(GameState.MainMenu);
        }

        /// <summary>
        /// Wired to the READY button's On Click () list. It only ever asks about this player: the
        /// server decides, and it identifies the asker itself.
        /// </summary>
        public void ToggleReady()
        {
            if (_crewRoster == null)
            {
                return;
            }

            _crewRoster.RequestSetReady(!_crewRoster.IsLocalMemberReady);
        }

        /// <summary>
        /// Wired to the NAVIGATOR button's On Click () list. One method per role rather than one
        /// taking a parameter, because Unity's button list can only pass a value the designer types
        /// in, and a mistyped number would silently choose the wrong job.
        /// </summary>
        public void SelectNavigator()
        {
            SelectRole(PlayerRole.Navigator);
        }

        /// <summary>Wired to the FISHER button's On Click () list.</summary>
        public void SelectFisher()
        {
            SelectRole(PlayerRole.Fisher);
        }

        /// <summary>Wired to the OBSERVER button's On Click () list.</summary>
        public void SelectObserver()
        {
            SelectRole(PlayerRole.Observer);
        }

        private void SelectRole(PlayerRole role)
        {
            if (_crewRoster == null)
            {
                return;
            }

            _crewRoster.RequestSetRole(role);
        }

        private void Refresh()
        {
            RefreshSlots();
            RefreshReadyButton();
            RefreshRoleButtons();
            RefreshStartButton();

            // Last on purpose. Everything above decides what a player may do; this only describes
            // it. Were it to fail, it must not take the START button's state down with it.
            RefreshRoleOccupancy();
        }

        private void RefreshSlots()
        {
            if (_slotLabels == null)
            {
                return;
            }

            for (int i = 0; i < _slotLabels.Length; i++)
            {
                if (_slotLabels[i] == null)
                {
                    continue;
                }

                if (_crewRoster == null || !_crewRoster.IsSlotOccupied(i))
                {
                    _slotLabels[i].text = _emptySlotText;
                    continue;
                }

                string who = _crewRoster.IsLocalMemberAt(i)
                    ? $"Crew {_crewRoster.GetMemberAt(i)} (you)"
                    : $"Crew {_crewRoster.GetMemberAt(i)}";

                _slotLabels[i].text = $"{who} - {_crewRoster.GetRoleAt(i)} - {(_crewRoster.IsReadyAt(i) ? "READY" : "NOT READY")}";
            }
        }

        private void RefreshReadyButton()
        {
            if (_readyButtonLabel != null && _crewRoster != null)
            {
                _readyButtonLabel.text = _crewRoster.IsLocalMemberReady ? "NOT READY" : "READY";
            }

            if (_readyButton != null)
            {
                _readyButton.interactable = _crewRoster != null && _crewRoster.MemberCount > 0;
            }
        }

        /// <summary>
        /// Greys out the jobs the crew has already filled.
        ///
        /// Re-evaluated on every RosterChanged, which fires on every peer whenever any slot's role
        /// changes, so a job released by one player becomes available on everybody else's screen in
        /// the same moment rather than after a press that fails.
        ///
        /// A player's own job is never greyed out, because pressing it again is how they let it go.
        /// </summary>
        private void RefreshRoleButtons()
        {
            RefreshRoleButton(_navigatorButton, PlayerRole.Navigator);
            RefreshRoleButton(_fisherButton, PlayerRole.Fisher);
            RefreshRoleButton(_observerButton, PlayerRole.Observer);
        }

        private void RefreshRoleButton(Button button, PlayerRole role)
        {
            if (button == null)
            {
                return;
            }

            // The roster answers this, rather than the menu counting roles for itself. One rule with
            // one implementation cannot drift out of step with the server's version of it.
            button.interactable = _crewRoster == null || _crewRoster.CanLocalMemberTake(role);
        }

        /// <summary>
        /// Draws how full each job is: one mark beside NAVIGATOR and OBSERVER, two beside FISHER,
        /// each shown filled or empty.
        ///
        /// Refreshed from the same RosterChanged every other part of this screen listens to, so a
        /// job somebody gives up shows as free on everyone's screen in the same moment, without this
        /// class watching anything or asking anybody.
        /// </summary>
        private void RefreshRoleOccupancy()
        {
            RefreshRoleOccupancy(_navigatorSlots, PlayerRole.Navigator);
            RefreshRoleOccupancy(_fisherSlots, PlayerRole.Fisher);
            RefreshRoleOccupancy(_observerSlots, PlayerRole.Observer);
        }

        private void RefreshRoleOccupancy(TMP_Text label, PlayerRole role)
        {
            if (label == null)
            {
                return;
            }

            label.text = BuildOccupancy(role);
        }

        /// <summary>
        /// One mark per place the job has, filled from the left.
        ///
        /// Both numbers come from the roster: the marks are how many places exist, and the filled
        /// ones are how many crew hold the job right now. Neither is written down here, so the
        /// display cannot end up describing a composition the server has stopped enforcing.
        ///
        /// A crew that has not loaded yet reads as nobody rather than as an empty string, since how
        /// many places a job has is knowable before anyone has taken one.
        ///
        /// Built by plain concatenation: this runs when the roster changes, which is a few times
        /// while a crew assembles, and never once a frame.
        /// </summary>
        private string BuildOccupancy(PlayerRole role)
        {
            int capacity = CrewRoster.GetRoleCapacity(role);
            int filled = _crewRoster == null ? 0 : _crewRoster.CountRole(role);

            string text = string.Empty;

            for (int i = 0; i < capacity; i++)
            {
                if (i > 0)
                {
                    text += _slotSymbolSeparator;
                }

                text += i < filled ? _filledSlotSymbol : _emptySlotSymbol;
            }

            return text;
        }

        private void RefreshStartButton()
        {
            if (_startButton == null)
            {
                return;
            }

            // The same rule the method enforces, so the button can never invite a press that would
            // then be refused. Re-evaluated on every RosterChanged, which fires for role changes and
            // for members leaving, so losing the last Navigator disables this at once.
            _startButton.interactable = IsLocalPlayerHost() && _crewRoster != null && _crewRoster.CanStartMission;
        }

        /// <summary>
        /// Offline play has no server to be, so it is treated as allowed; that mirrors how
        /// GameFlowManager already decides whether a transition needs host authority.
        /// </summary>
        private static bool IsLocalPlayerHost()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return true;
            }

            return NetworkManager.Singleton.IsServer;
        }

        private static void GoTo(GameState target)
        {
            GameFlowManager flow = ServiceRegistry.Get<GameFlowManager>();
            if (flow == null)
            {
                // ServiceRegistry has already logged why, which is almost always
                // that play started from this scene instead of Bootstrap.
                return;
            }

            flow.GoTo(target);
        }
    }
}
