using UnityEngine;

namespace WatchInput.Samples
{
    /// <summary>
    /// Lightweight zero-allocation HUD demonstrating all public Watch input events,
    /// per-frame polling properties, and haptic feedback triggers.
    /// Uses IMGUI for rendering independence across Universal, HDRP, and Built-in pipelines.
    /// </summary>
    public class WatchInputDemo : MonoBehaviour
    {
        // Which haptic plays for each input. Change them in the Inspector — or call
        // Watch.PlayHaptic(...) yourself from any script; the SDK doesn't decide the feel, your game does.
        [Header("Haptic feedback")]
        [SerializeField] private WatchHaptic tapHaptic = WatchHaptic.Click;
        [SerializeField] private WatchHaptic swipeUpHaptic = WatchHaptic.DirectionUp;
        [SerializeField] private WatchHaptic swipeDownHaptic = WatchHaptic.DirectionDown;
        [SerializeField] private WatchHaptic swipeLeftHaptic = WatchHaptic.Click;
        [SerializeField] private WatchHaptic swipeRightHaptic = WatchHaptic.Click;
        [SerializeField] private WatchHaptic pressStartHaptic = WatchHaptic.Start;
        [SerializeField] private WatchHaptic pressEndHaptic = WatchHaptic.Stop;

        private const float EventFadeDuration = 0.6f;

        // Preallocated immutable string constants to prevent GC allocation in OnGUI/Update
        private static readonly string TextStatusReachable = "Status: Connected (Reachable)";
        private static readonly string TextStatusUnreachable = "Status: Disconnected (Not Reachable)";
        private static readonly string TextEventTap = "TAP";
        private static readonly string TextEventSwipeUp = "SWIPE ↑";
        private static readonly string TextEventSwipeDown = "SWIPE ↓";
        private static readonly string TextEventSwipeLeft = "SWIPE ←";
        private static readonly string TextEventSwipeRight = "SWIPE →";
        private static readonly string TextEventPress = "PRESS";
        private static readonly string TextEventRelease = "RELEASE";
        private static readonly string TextHoldHeader = "Hold Bar:";
        private static readonly string TextTitle = "Apple Watch Input Demo";

        private string _lastEventText = "";
        private float _eventAlpha = 0f;

        private int _totalCrownSteps = 0;
        private float _lastCrownDelta = 0f;
        private string _cachedCrownString = "Crown Steps: 0 (Delta: 0.00)";

        private float _holdProgress = 0f;

        private GUIStyle _titleStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _eventStyle;
        private GUIStyle _crownStyle;
        private GUIStyle _barBgStyle;
        private GUIStyle _barFillStyle;
        private Texture2D _whiteTexture;
        private bool _stylesInitialized = false;

        private void OnEnable()
        {
            Watch.Tap += OnTap;
            Watch.Swiped += OnSwiped;
            Watch.PressBegan += OnPressBegan;
            Watch.PressReleased += OnPressReleased;
            Watch.CrownRotated += OnCrownRotated;
        }

        private void OnDisable()
        {
            Watch.Tap -= OnTap;
            Watch.Swiped -= OnSwiped;
            Watch.PressBegan -= OnPressBegan;
            Watch.PressReleased -= OnPressReleased;
            Watch.CrownRotated -= OnCrownRotated;
        }

        private void OnDestroy()
        {
            if (_whiteTexture != null)
            {
                DestroyImmediate(_whiteTexture);
                _whiteTexture = null;
            }
        }

        private void OnTap()
        {
            _lastEventText = TextEventTap;
            _eventAlpha = 1f;
            Watch.PlayHaptic(tapHaptic);
        }

        private void OnSwiped(WatchSwipe direction)
        {
            _eventAlpha = 1f;
            switch (direction)
            {
                case WatchSwipe.Up:
                    _lastEventText = TextEventSwipeUp;
                    Watch.PlayHaptic(swipeUpHaptic);
                    break;
                case WatchSwipe.Down:
                    _lastEventText = TextEventSwipeDown;
                    Watch.PlayHaptic(swipeDownHaptic);
                    break;
                case WatchSwipe.Left:
                    _lastEventText = TextEventSwipeLeft;
                    Watch.PlayHaptic(swipeLeftHaptic);
                    break;
                case WatchSwipe.Right:
                    _lastEventText = TextEventSwipeRight;
                    Watch.PlayHaptic(swipeRightHaptic);
                    break;
            }
        }

        private void OnPressBegan()
        {
            _lastEventText = TextEventPress;
            _eventAlpha = 1f;
            Watch.PlayHaptic(pressStartHaptic);
        }

        private void OnPressReleased()
        {
            _lastEventText = TextEventRelease;
            _eventAlpha = 1f;
            Watch.PlayHaptic(pressEndHaptic);
        }

        private void OnCrownRotated(float delta)
        {
            _totalCrownSteps += Watch.CrownSteps;
            _lastCrownDelta = delta;
            // String formatting only occurs on rotation event, never per-frame in OnGUI/Update
            _cachedCrownString = $"Crown Steps: {_totalCrownSteps} (Delta: {_lastCrownDelta:F2})";
        }

        private void Update()
        {
            // Smoothly fade event indicator
            if (_eventAlpha > 0f)
            {
                _eventAlpha = Mathf.Max(0f, _eventAlpha - (Time.deltaTime / EventFadeDuration));
            }

            // Fill or drain hold bar based on live IsPressed state
            float targetHold = Watch.IsPressed ? 1f : 0f;
            _holdProgress = Mathf.MoveTowards(_holdProgress, targetHold, Time.deltaTime * 2.5f);
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _titleStyle.normal.textColor = Color.white;

            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _eventStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _crownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            _crownStyle.normal.textColor = Color.white;

            _barBgStyle = new GUIStyle();
            _barBgStyle.normal.background = _whiteTexture;

            _barFillStyle = new GUIStyle();
            _barFillStyle.normal.background = _whiteTexture;

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            // Screen scale factor based on DPI for crisp readability on mobile and high-DPI displays
            float scale = Screen.dpi > 0 ? Screen.dpi / 160f : Screen.height / 640f;
            if (scale < 1f) scale = 1f;

            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1.0f));

            float vWidth = Screen.width / scale;
            float vHeight = Screen.height / scale;

            // Respect the device safe area (notch / Dynamic Island / home indicator),
            // then center the content block vertically inside it.
            Rect safe = Screen.safeArea;
            float safeTop = (Screen.height - (safe.y + safe.height)) / scale;
            float safeHeight = safe.height / scale;
            const float ContentHeight = 240f;
            float oy = safeTop + Mathf.Max(0f, (safeHeight - ContentHeight) * 0.5f);

            // Background panel (full screen)
            GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);
            GUI.DrawTexture(new Rect(0, 0, vWidth, vHeight), _whiteTexture);

            // Title
            GUI.color = Color.white;
            GUI.Label(new Rect(20, oy, vWidth - 40, 30), TextTitle, _titleStyle);

            // Status label
            bool reachable = Watch.IsReachable;
            _statusStyle.normal.textColor = reachable ? new Color(0.2f, 0.85f, 0.3f) : new Color(0.85f, 0.3f, 0.2f);
            GUI.Label(new Rect(20, oy + 35, vWidth - 40, 25), reachable ? TextStatusReachable : TextStatusUnreachable, _statusStyle);

            // Event banner
            if (_eventAlpha > 0f)
            {
                _eventStyle.normal.textColor = new Color(0.3f, 0.7f, 1f, _eventAlpha);
                GUI.Label(new Rect(20, oy + 75, vWidth - 40, 50), _lastEventText, _eventStyle);
            }

            // Hold bar
            float barY = oy + 160;
            float barWidth = vWidth - 80;
            float barHeight = 24;
            GUI.color = Color.white;
            GUI.Label(new Rect(40, barY - 22, barWidth, 20), TextHoldHeader, _crownStyle);

            // Bar background
            GUI.color = new Color(0.2f, 0.2f, 0.25f, 1f);
            GUI.DrawTexture(new Rect(40, barY, barWidth, barHeight), _whiteTexture);

            // Bar fill
            if (_holdProgress > 0f)
            {
                GUI.color = new Color(0.25f, 0.65f, 1f, 1f);
                GUI.DrawTexture(new Rect(40, barY, barWidth * _holdProgress, barHeight), _whiteTexture);
            }

            // Crown display
            GUI.color = Color.white;
            GUI.Label(new Rect(20, barY + 40, vWidth - 40, 30), _cachedCrownString, _crownStyle);

            GUI.matrix = prevMatrix;
        }
    }
}
