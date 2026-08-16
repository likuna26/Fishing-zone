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
            RefreshStartButton();
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
