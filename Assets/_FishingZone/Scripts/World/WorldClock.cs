using System;
using FishingZone.Core;
using Unity.Netcode;
using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Where the clock was when it last changed: the server time it was set at, how many hours the
    /// world had lived by then, and how fast it was going.
    ///
    /// One value rather than three variables, so a change — a pause, a resume, a jump — always
    /// arrives whole. Three separate variables could be read between their updates and describe a
    /// moment that never happened.
    /// </summary>
    public struct ClockAnchor : INetworkSerializeByMemcpy, IEquatable<ClockAnchor>
    {
        public double ServerTime;

        public double TotalHours;

        public float HoursPerSecond;

        public bool Equals(ClockAnchor other)
        {
            return ServerTime.Equals(other.ServerTime)
                   && TotalHours.Equals(other.TotalHours)
                   && HoursPerSecond.Equals(other.HoursPerSecond);
        }

        public override bool Equals(object obj)
        {
            return obj is ClockAnchor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ServerTime, TotalHours, HoursPerSecond);
        }
    }

    /// <summary>
    /// The time of day, for the whole session.
    ///
    /// Not a match timer. Nothing ends when a band changes and nothing is counted down; the world
    /// simply has a time, and night is a place the crew can be in rather than the end of their turn.
    /// What changes with the hour is for the content that reads it.
    ///
    /// The server owns it and almost never speaks. What travels is an anchor — when the clock was
    /// last set, to what, and at what speed — written only when one of those changes. Every peer
    /// works the present out for itself from the anchor and Netcode's shared server time, so a
    /// twenty-minute day costs no traffic at all between a pause and a resume, and a player joining
    /// late knows the hour from the anchor that arrives with the spawn.
    ///
    /// Server decisions read the server's own answer. Everybody else uses theirs for what they see —
    /// the light, the lookout's words — where arriving a few milliseconds apart changes nothing.
    ///
    /// Lives for the session, not the scene: it is spawned once when the session starts and kept
    /// through every scene load, so the hour carries from the expedition back to port and out again.
    /// It runs only while the crew is somewhere — port or at sea — and stands still in the menu and
    /// the lobby, where there is no world to be in.
    /// </summary>
    public class WorldClock : NetworkBehaviour
    {
        private const double HoursPerDay = 24.0;

        private static WorldClock _current;

        public static WorldClock Current => _current;

        /// <summary>
        /// How long a whole day lasts, in real seconds. Twenty minutes is a development and playtest
        /// default, not a design decision: the right length depends on how far apart things are,
        /// which nothing has settled yet.
        /// </summary>
        [SerializeField]
        private float _dayLengthSeconds = 1200f;

        /// <summary>The hour a session's world begins at.</summary>
        [SerializeField]
        private float _startHour = 6f;

        /// <summary>
        /// Where each band begins, in hours. Night runs from its start through midnight to dawn.
        /// Kept here rather than on whatever reads them, so the lookout, the lighting and later the
        /// fish all agree on when dusk is.
        /// </summary>
        [SerializeField]
        private float _dawnStartHour = 5f;

        [SerializeField]
        private float _dayStartHour = 7f;

        [SerializeField]
        private float _duskStartHour = 18f;

        [SerializeField]
        private float _nightStartHour = 20f;

        private readonly NetworkVariable<ClockAnchor> _anchor = new NetworkVariable<ClockAnchor>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>
        /// Raised on every peer when its own reckoning crosses into another band. Display reads this;
        /// gameplay on the server should read <see cref="Band"/> at the moment it decides.
        /// </summary>
        public event Action<TimeOfDayBand> BandChanged;

        /// <summary>Speed for debugging only, so night can be reached without waiting for it.</summary>
        private float _debugSpeedMultiplier = 1f;

        private bool _isRunningOnServer;

        private bool _hasBand;

        private TimeOfDayBand _lastBand;

        private GameFlowManager _gameFlow;

        public float DayLengthSeconds => _dayLengthSeconds;

        public float DawnStartHour => _dawnStartHour;

        public float DayStartHour => _dayStartHour;

        public float DuskStartHour => _duskStartHour;

        public float NightStartHour => _nightStartHour;

        /// <summary>Hours the world has lived since the session began, counting from its start hour.</summary>
        public double TotalHours
        {
            get
            {
                ClockAnchor anchor = _anchor.Value;
                if (!IsSpawned)
                {
                    return anchor.TotalHours;
                }

                double elapsed = NetworkManager.ServerTime.Time - anchor.ServerTime;
                return anchor.TotalHours + (Math.Max(elapsed, 0.0) * anchor.HoursPerSecond);
            }
        }

        /// <summary>The hour on a 24-hour clock, from 0 up to but not including 24.</summary>
        public float HourOfDay => (float)(TotalHours % HoursPerDay);

        /// <summary>Which day this is, starting at 1.</summary>
        public int Day => (int)Math.Floor(TotalHours / HoursPerDay) + 1;

        public TimeOfDayBand Band => BandAt(HourOfDay);

        /// <summary>Whether time is passing. False in the menu and the lobby.</summary>
        public bool IsRunning => _anchor.Value.HoursPerSecond > 0f;

        public TimeOfDayBand BandAt(float hour)
        {
            if (hour >= _nightStartHour || hour < _dawnStartHour)
            {
                return TimeOfDayBand.Night;
            }

            if (hour < _dayStartHour)
            {
                return TimeOfDayBand.Dawn;
            }

            return hour < _duskStartHour ? TimeOfDayBand.Day : TimeOfDayBand.Dusk;
        }

        // Static state outlives a play session when domain reload is disabled, exactly as the other
        // registries in this project do.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _current = null;
        }

        public override void OnNetworkSpawn()
        {
            if (_current != null && _current != this)
            {
                GameLog.Error(LogCategory.Flow,
                    $"'{name}' is a second World Clock; '{_current.name}' already keeps the time. This one is ignored.");
                return;
            }

            _current = this;

            if (!IsServer)
            {
                return;
            }

            _anchor.Value = new ClockAnchor
            {
                ServerTime = NetworkManager.ServerTime.Time,
                TotalHours = _startHour,
                HoursPerSecond = 0f
            };

            _gameFlow = ServiceRegistry.Get<GameFlowManager>();
            if (_gameFlow != null)
            {
                _gameFlow.StateChanged += HandleStateChanged;
                HandleStateChanged(_gameFlow.CurrentState);
            }

            GameLog.Info(LogCategory.Flow,
                $"World clock started at {FormatHour(_startHour)} on day 1; a day lasts {_dayLengthSeconds:F0}s.");
        }

        public override void OnNetworkDespawn()
        {
            if (_gameFlow != null)
            {
                _gameFlow.StateChanged -= HandleStateChanged;
                _gameFlow = null;
            }

            if (_current == this)
            {
                _current = null;
            }
        }

        /// <summary>
        /// Watches the band change on this peer, so display can follow it and the server's log says
        /// when it happened. Nothing is sent: every peer works its own band out.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned || _current != this)
            {
                return;
            }

            TimeOfDayBand band = Band;
            if (_hasBand && band == _lastBand)
            {
                return;
            }

            bool isFirst = !_hasBand;
            _hasBand = true;
            _lastBand = band;

            if (IsServer && !isFirst)
            {
                GameLog.Info(LogCategory.Flow, $"Day {Day}, {FormatHour(HourOfDay)}: it is {band}.");
            }

            BandChanged?.Invoke(band);
        }

        /// <summary>Time passes wherever the crew is somewhere, and nowhere else.</summary>
        private void HandleStateChanged(GameState state)
        {
            SetRunningOnServer(state == GameState.Port || state == GameState.Expedition);
        }

        private void SetRunningOnServer(bool isRunning)
        {
            // The clock is spawned paused, so the first call that matters is the first resume.
            if (!IsServer || isRunning == _isRunningOnServer)
            {
                return;
            }

            _isRunningOnServer = isRunning;
            Reanchor(TotalHours);

            GameLog.Info(LogCategory.Flow, isRunning
                ? $"World clock running: day {Day}, {FormatHour(HourOfDay)}."
                : $"World clock paused: day {Day}, {FormatHour(HourOfDay)}.");
        }

        /// <summary>
        /// The only place the anchor is written. Takes the hours the world should have now and the
        /// speed it should go from here, so every change — pause, resume, jump, speed — is the same
        /// small message and nothing in between is ever sent.
        /// </summary>
        private void Reanchor(double totalHours)
        {
            float hoursPerSecond = _isRunningOnServer && _dayLengthSeconds > 0f
                ? (float)(HoursPerDay / _dayLengthSeconds) * _debugSpeedMultiplier
                : 0f;

            _anchor.Value = new ClockAnchor
            {
                ServerTime = NetworkManager.ServerTime.Time,
                TotalHours = totalHours,
                HoursPerSecond = hoursPerSecond
            };
        }

        /// <summary>
        /// Server only. Moves the clock to an hour of the current day, for testing and for whatever
        /// later lets a crew sleep through the night. The day count is kept.
        /// </summary>
        public void SetHourOnServer(float hour)
        {
            if (!IsServer)
            {
                GameLog.Warn(LogCategory.Flow, "Ignored setting the hour: only the server moves the clock.");
                return;
            }

            double dayStart = Math.Floor(TotalHours / HoursPerDay) * HoursPerDay;
            Reanchor(dayStart + Mathf.Repeat(hour, (float)HoursPerDay));

            GameLog.Info(LogCategory.Flow, $"World clock set to day {Day}, {FormatHour(HourOfDay)}.");
        }

        /// <summary>Server only, and for debugging: how many times faster than normal the day runs.</summary>
        public void SetDebugSpeedOnServer(float multiplier)
        {
            if (!IsServer)
            {
                GameLog.Warn(LogCategory.Flow, "Ignored setting the clock speed: only the server moves the clock.");
                return;
            }

            _debugSpeedMultiplier = Mathf.Max(0f, multiplier);
            Reanchor(TotalHours);

            GameLog.Info(LogCategory.Flow, $"World clock speed set to x{_debugSpeedMultiplier:F0}.");
        }

        [ContextMenu("Debug/Jump To Dawn (05:30)")]
        private void JumpToDawn() => SetHourOnServer(5.5f);

        [ContextMenu("Debug/Jump To Day (12:00)")]
        private void JumpToDay() => SetHourOnServer(12f);

        [ContextMenu("Debug/Jump To Dusk (18:30)")]
        private void JumpToDusk() => SetHourOnServer(18.5f);

        [ContextMenu("Debug/Jump To Night (22:00)")]
        private void JumpToNight() => SetHourOnServer(22f);

        [ContextMenu("Debug/Speed x1")]
        private void SpeedNormal() => SetDebugSpeedOnServer(1f);

        [ContextMenu("Debug/Speed x60")]
        private void SpeedFast() => SetDebugSpeedOnServer(60f);

        public static string FormatHour(float hour)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }
}
