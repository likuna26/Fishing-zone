using UnityEngine;

namespace FishingZone.World
{
    /// <summary>
    /// Moves the sun with the world's clock. A placeholder: enough that night is visibly night, and
    /// nothing more until the look of the game is worked on properly.
    ///
    /// Visual only and local only. Every peer reads its own clock and lights its own scene; nothing
    /// here is sent, and no gameplay may read the lighting — ask the clock instead.
    ///
    /// The sun rises in the middle of dawn and sets in the middle of dusk, so both bands are the
    /// light changing rather than one side of a switch. Below the horizon it keeps turning but gives
    /// almost no light.
    ///
    /// A scene with no clock keeps whatever lighting it was authored with.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class DayNightLighting : MonoBehaviour
    {
        [SerializeField]
        private float _noonIntensity = 1f;

        [SerializeField]
        private float _nightIntensity = 0.05f;

        [SerializeField]
        private Color _middayColor = Color.white;

        /// <summary>The colour of a low sun, at dawn and dusk.</summary>
        [SerializeField]
        private Color _lowSunColor = new Color(1f, 0.6f, 0.35f);

        [SerializeField]
        private float _dayAmbientIntensity = 1f;

        [SerializeField]
        private float _nightAmbientIntensity = 0.15f;

        private Light _light;

        /// <summary>The authored compass direction of the sun, kept so only its height changes.</summary>
        private float _sunYaw;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _sunYaw = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            WorldClock clock = WorldClock.Current;
            if (clock == null || !clock.IsSpawned)
            {
                return;
            }

            float sunrise = (clock.DawnStartHour + clock.DayStartHour) * 0.5f;
            float sunset = (clock.DuskStartHour + clock.NightStartHour) * 0.5f;
            float elevation = SunElevation(clock.HourOfDay, sunrise, sunset);

            // Height above the horizon, negative below it on either side, so twilight fades in before
            // sunrise and out after sunset rather than switching.
            float height = elevation <= 180f
                ? Mathf.Min(elevation, 180f - elevation)
                : -Mathf.Min(elevation - 180f, 360f - elevation);

            // How much daylight there is: none well below the horizon, full once the sun is well up.
            float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 25f, height));

            transform.rotation = Quaternion.Euler(elevation, _sunYaw, 0f);
            _light.intensity = Mathf.Lerp(_nightIntensity, _noonIntensity, daylight);
            _light.color = Color.Lerp(_lowSunColor, _middayColor, Mathf.InverseLerp(0.2f, 0.8f, daylight));
            RenderSettings.ambientIntensity = Mathf.Lerp(_nightAmbientIntensity, _dayAmbientIntensity, daylight);
        }

        /// <summary>
        /// Degrees above the horizon: 0 at sunrise, 90 at the middle of the day, 180 at sunset, and on
        /// round underneath through the night back to 360 at the next sunrise.
        /// </summary>
        private static float SunElevation(float hour, float sunrise, float sunset)
        {
            float dayLength = Mathf.Max(sunset - sunrise, 0.01f);
            float nightLength = Mathf.Max(24f - dayLength, 0.01f);

            if (hour >= sunrise && hour < sunset)
            {
                return (hour - sunrise) / dayLength * 180f;
            }

            float sinceSunset = Mathf.Repeat(hour - sunset, 24f);
            return 180f + (sinceSunset / nightLength * 180f);
        }
    }
}
