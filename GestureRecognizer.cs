using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class GestureEvent : UnityEvent<GestureType, Vector2> { }

public enum GestureType
{
    Tap,
    DoubleTap,
    TripleTap,
    LongPress,
    SwipeUp,
    SwipeDown,
    SwipeLeft,
    SwipeRight,
    TwoFingerTap,
    TwoFingerSwipeUp,
    TwoFingerSwipeDown,
    TwoFingerPinch,
    TwoFingerSpread
}

/// <summary>
/// Advanced gesture recognizer for accessibility
/// </summary>
public class GestureRecognizer : MonoBehaviour
{
    [Header("General Settings")]
    public bool enableGestureRecognition = true;
    public bool ignoreUIElements = true;

    [Header("Single Touch Gestures")]
    public bool detectTap = true;
    public bool detectDoubleTap = true;
    public bool detectTripleTap = true;
    public bool detectLongPress = true;
    public bool detectSwipes = true;

    [Header("Multi-Touch Gestures")]
    public bool detectTwoFingerTap = true;
    public bool detectTwoFingerSwipe = true;
    public bool detectPinchAndSpread = true;

    [Header("Timing Parameters")]
    public float doubleTapMaxDelay = 0.3f;
    public float longPressMinTime = 0.7f;
    public float maxTapDuration = 0.3f;

    [Header("Touch Parameters")]
    public float minSwipeDistance = 50f;
    public float maxTapDistance = 30f; // Maximum finger movement for a tap
    public float pinchThreshold = 20f; // Min distance change for pinch/spread

    [Header("Accessibility Parameters")]
    public float gestureCooldown = 0.2f; // Time before next gesture is recognized
    public bool vibrateOnGesture = true;
    public bool announceGesturesViaAudio = true;

    [Header("Events")]
    public GestureEvent onGestureDetected;
    public UnityEvent<string> onGestureDebug; // For debug information

    // Internal state tracking
    private float lastTapTime = 0f;
    private int consecutiveTaps = 0;
    private bool isLongPressing = false;
    private float touchStartTime = 0f;
    private Vector2 touchStartPosition;
    private bool gestureCooldownActive = false;
    private float lastGestureTime = 0f;
    private Dictionary<int, Vector2> activeTouches = new Dictionary<int, Vector2>();
    private Dictionary<int, float> touchStartTimes = new Dictionary<int, float>();
    private AccessibilityManager accessibilityManager;

    private void Start()
    {
        // Find accessibility manager for TTS and vibration
        accessibilityManager = FindObjectOfType<AccessibilityManager>();
    }

    private void Update()
    {
        if (!enableGestureRecognition)
            return;

        // Process cooldown
        if (gestureCooldownActive && Time.time - lastGestureTime > gestureCooldown)
        {
            gestureCooldownActive = false;
        }

        // Process long press detection
        if (isLongPressing && Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            float touchDuration = Time.time - touchStartTime;

            // If we've held long enough, trigger long press
            if (touchDuration > longPressMinTime && !gestureCooldownActive)
            {
                Vector2 touchPosition = touch.position;

                // Check if we haven't moved too far from original position
                if (Vector2.Distance(touchPosition, touchStartPosition) < maxTapDistance)
                {
                    TriggerGesture(GestureType.LongPress, touchPosition);
                    isLongPressing = false;
                }
            }
        }

        // Process all touches
        ProcessTouches();
    }

    private void ProcessTouches()
    {
        // Process new touches
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            int touchId = touch.fingerId;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    // Ignore UI touches if specified
                    if (ignoreUIElements && IsPointerOverUI(touchId))
                        continue;

                    // Record the touch start
                    touchStartPosition = touch.position;
                    touchStartTime = Time.time;
                    isLongPressing = true;

                    // Store information for multi-touch gestures
                    activeTouches[touchId] = touch.position;
                    touchStartTimes[touchId] = Time.time;
                    break;

                case TouchPhase.Moved:
                    // Update position
                    if (activeTouches.ContainsKey(touchId))
                    {
                        // Check if the touch has moved enough to no longer be a tap
                        float distanceMoved = Vector2.Distance(touch.position, activeTouches[touchId]);
                        if (distanceMoved > maxTapDistance)
                        {
                            isLongPressing = false; // Too much movement for a long press
                        }

                        // Check for swipe or pinch/spread if moved far enough
                        if (distanceMoved > minSwipeDistance && !gestureCooldownActive)
                        {
                            if (Input.touchCount == 1 && detectSwipes)
                            {
                                ProcessSwipe(activeTouches[touchId], touch.position);
                                activeTouches.Remove(touchId); // Remove to prevent multiple triggers
                            }
                            else if (Input.touchCount == 2 && detectTwoFingerSwipe)
                            {
                                ProcessMultiTouchGesture();
                            }
                        }

                        // Update active touch position
                        activeTouches[touchId] = touch.position;
                    }
                    break;

                case TouchPhase.Ended:
                    if (activeTouches.ContainsKey(touchId))
                    {
                        // Calculate touch duration
                        float touchDuration = Time.time - touchStartTimes[touchId];

                        // Check if this was a short tap
                        if (touchDuration < maxTapDuration &&
                            Vector2.Distance(touch.position, activeTouches[touchId]) < maxTapDistance)
                        {
                            // Process tap based on how many consecutive taps
                            ProcessTap(touch.position);
                        }

                        // Clean up
                        activeTouches.Remove(touchId);
                        touchStartTimes.Remove(touchId);
                        isLongPressing = false;
                    }
                    break;

                case TouchPhase.Canceled:
                    // Clean up on cancel
                    if (activeTouches.ContainsKey(touchId))
                    {
                        activeTouches.Remove(touchId);
                        touchStartTimes.Remove(touchId);
                        isLongPressing = false;
                    }
                    break;
            }
        }

        // Reset if no touches
        if (Input.touchCount == 0)
        {
            isLongPressing = false;
        }
    }

    private void ProcessTap(Vector2 position)
    {
        if (gestureCooldownActive)
            return;

        float timeSinceLastTap = Time.time - lastTapTime;

        // Check for consecutive taps
        if (timeSinceLastTap <= doubleTapMaxDelay)
        {
            consecutiveTaps++;

            if (consecutiveTaps == 1 && detectDoubleTap)
            {
                // Double tap
                TriggerGesture(GestureType.DoubleTap, position);
                consecutiveTaps = 0; // Reset counter after recognizing double tap
            }
            else if (consecutiveTaps == 2 && detectTripleTap)
            {
                // Triple tap
                TriggerGesture(GestureType.TripleTap, position);
                consecutiveTaps = 0; // Reset counter after recognizing triple tap
            }
        }
        else
        {
            // First tap in a sequence
            consecutiveTaps = 0;

            // Only trigger single tap immediately if not detecting multi-taps
            if (detectTap && (!detectDoubleTap || timeSinceLastTap > doubleTapMaxDelay * 2))
            {
                TriggerGesture(GestureType.Tap, position);
            }
            else if (detectTap && !detectDoubleTap)
            {
                TriggerGesture(GestureType.Tap, position);
            }
            else if (detectTap)
            {
                // Wait briefly to confirm this isn't the start of a double tap
                StartCoroutine(DelayedSingleTap(position));
            }
        }

        lastTapTime = Time.time;
    }

    private IEnumerator DelayedSingleTap(Vector2 position)
    {
        // Wait to see if another tap comes in
        yield return new WaitForSeconds(doubleTapMaxDelay);

        // If no new tap has come in, trigger the single tap
        if (Time.time - lastTapTime >= doubleTapMaxDelay && consecutiveTaps == 0)
        {
            TriggerGesture(GestureType.Tap, position);
        }
    }

    private void ProcessSwipe(Vector2 startPosition, Vector2 endPosition)
    {
        Vector2 direction = endPosition - startPosition;

        // Determine horizontal vs vertical
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            // Horizontal swipe
            if (direction.x > 0)
            {
                TriggerGesture(GestureType.SwipeRight, endPosition);
            }
            else
            {
                TriggerGesture(GestureType.SwipeLeft, endPosition);
            }
        }
        else
        {
            // Vertical swipe
            if (direction.y > 0)
            {
                TriggerGesture(GestureType.SwipeUp, endPosition);
            }
            else
            {
                TriggerGesture(GestureType.SwipeDown, endPosition);
            }
        }
    }

    private void ProcessMultiTouchGesture()
    {
        if (Input.touchCount != 2 || activeTouches.Count != 2 || gestureCooldownActive)
            return;

        // Get the two touches
        Touch touch1 = Input.GetTouch(0);
        Touch touch2 = Input.GetTouch(1);

        // Get start positions
        Vector2 touch1StartPos = Vector2.zero;
        Vector2 touch2StartPos = Vector2.zero;

        foreach (var touchEntry in activeTouches)
        {
            if (touchEntry.Key == touch1.fingerId)
                touch1StartPos = touchEntry.Value;
            else if (touchEntry.Key == touch2.fingerId)
                touch2StartPos = touchEntry.Value;
        }

        // Calculate distances
        float startDistance = Vector2.Distance(touch1StartPos, touch2StartPos);
        float currentDistance = Vector2.Distance(touch1.position, touch2.position);
        float deltaDistance = currentDistance - startDistance;

        // Check for pinch/spread
        if (detectPinchAndSpread && Mathf.Abs(deltaDistance) > pinchThreshold)
        {
            if (deltaDistance < 0)
            {
                // Pinch gesture
                TriggerGesture(GestureType.TwoFingerPinch, (touch1.position + touch2.position) / 2);
            }
            else
            {
                // Spread gesture
                TriggerGesture(GestureType.TwoFingerSpread, (touch1.position + touch2.position) / 2);
            }
        }
        else
        {
            // Check for two-finger swipe (using the average movement)
            Vector2 touch1Direction = touch1.position - touch1StartPos;
            Vector2 touch2Direction = touch2.position - touch2StartPos;

            // If both fingers are moving in roughly the same direction
            if (Vector2.Dot(touch1Direction.normalized, touch2Direction.normalized) > 0.7f)
            {
                Vector2 avgDirection = (touch1Direction + touch2Direction) / 2;

                if (avgDirection.magnitude > minSwipeDistance)
                {
                    // Vertical two-finger swipe
                    if (Mathf.Abs(avgDirection.y) > Mathf.Abs(avgDirection.x))
                    {
                        if (avgDirection.y > 0)
                        {
                            TriggerGesture(GestureType.TwoFingerSwipeUp, (touch1.position + touch2.position) / 2);
                        }
                        else
                        {
                            TriggerGesture(GestureType.TwoFingerSwipeDown, (touch1.position + touch2.position) / 2);
                        }
                    }
                }
            }
        }
    }

    private void TriggerGesture(GestureType gestureType, Vector2 position)
    {
        // Prevent triggering gestures too quickly
        if (gestureCooldownActive)
            return;

        // Invoke the gesture event
        onGestureDetected.Invoke(gestureType, position);

        // Send debug info
        onGestureDebug.Invoke("Gesture detected: " + gestureType.ToString());

        // Provide vibration feedback
        if (vibrateOnGesture)
        {
            ProvideFeedback(gestureType);
        }

        // Announce gesture via audio
        if (announceGesturesViaAudio && accessibilityManager != null && accessibilityManager.textToSpeech != null)
        {
            // Only announce when accessibility is enabled
            if (accessibilityManager.announceFeatures)
            {
                accessibilityManager.textToSpeech.Speak("Gesture: " + GestureTypeToSpeech(gestureType));
            }
        }

        // Set cooldown
        gestureCooldownActive = true;
        lastGestureTime = Time.time;
    }

    private void ProvideFeedback(GestureType gestureType)
    {
        if (accessibilityManager == null)
            return;

        // Play appropriate sounds
        if (accessibilityManager.audioSource != null)
        {
            switch (gestureType)
            {
                case GestureType.Tap:
                case GestureType.DoubleTap:
                case GestureType.TripleTap:
                case GestureType.TwoFingerTap:
                    if (accessibilityManager.tapSound != null)
                    {
                        accessibilityManager.audioSource.PlayOneShot(accessibilityManager.tapSound);
                    }
                    break;

                case GestureType.SwipeLeft:
                case GestureType.SwipeRight:
                case GestureType.SwipeUp:
                case GestureType.SwipeDown:
                case GestureType.TwoFingerSwipeUp:
                case GestureType.TwoFingerSwipeDown:
                    if (accessibilityManager.swipeSound != null)
                    {
                        accessibilityManager.audioSource.PlayOneShot(accessibilityManager.swipeSound);
                    }
                    break;

                case GestureType.LongPress:
                    if (accessibilityManager.menuOpenSound != null)
                    {
                        accessibilityManager.audioSource.PlayOneShot(accessibilityManager.menuOpenSound);
                    }
                    break;
            }
        }

        // Vibrate if enabled - use our custom vibration method
        if (vibrateOnGesture)
        {
            // Try to find NavigationManager if not already cached
            NavigationManager navManager = FindObjectOfType<NavigationManager>();

            if (navManager != null && navManager.useVibration)
            {
                switch (gestureType)
                {
                    case GestureType.LongPress:
                        PerformVibration(1.0f, 0.3f);
                        break;
                    case GestureType.DoubleTap:
                        StartCoroutine(MultiVibrate(2, 0.06f, 0.06f));
                        break;
                    case GestureType.TripleTap:
                        StartCoroutine(MultiVibrate(3, 0.06f, 0.06f));
                        break;
                    case GestureType.SwipeLeft:
                    case GestureType.SwipeRight:
                    case GestureType.SwipeUp:
                    case GestureType.SwipeDown:
                        PerformVibration(0.7f, 0.15f);
                        break;
                    case GestureType.TwoFingerPinch:
                    case GestureType.TwoFingerSpread:
                        PerformVibration(0.8f, 0.2f);
                        break;
                    default:
                        PerformVibration(0.5f, 0.08f);
                        break;
                }
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            else
            {
                // Fallback to direct vibration if NavigationManager not available
                switch (gestureType)
                {
                    case GestureType.LongPress:
                        AndroidVibrate(300);
                        break;
                    case GestureType.DoubleTap:
                        AndroidVibrate(new long[] { 0, 60, 60, 60 }, -1);
                        break;
                    case GestureType.TripleTap:
                        AndroidVibrate(new long[] { 0, 60, 60, 60, 60, 60 }, -1);
                        break;
                    case GestureType.SwipeLeft:
                    case GestureType.SwipeRight:
                    case GestureType.SwipeUp:
                    case GestureType.SwipeDown:
                        AndroidVibrate(150);
                        break;
                    case GestureType.TwoFingerPinch:
                    case GestureType.TwoFingerSpread:
                        AndroidVibrate(200);
                        break;
                    default:
                        AndroidVibrate(80);
                        break;
                }
            }
#endif
        }
    }

    // Helper for multiple vibrations in sequence
    private IEnumerator MultiVibrate(int count, float duration, float pauseDuration)
    {
        for (int i = 0; i < count; i++)
        {
            PerformVibration(0.7f, duration);
            if (i < count - 1)
                yield return new WaitForSeconds(pauseDuration);
        }
    }

    /// <summary>
    /// Triggers device vibration with the specified intensity and duration
    /// </summary>
    /// <param name="intensity">Vibration intensity from 0 to 1</param>
    /// <param name="duration">Duration in seconds</param>
    private void PerformVibration(float intensity, float duration)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (intensity >= 1.0f)
        {
            Handheld.Vibrate();
        }
        else
        {
            // On Android, there's no direct way to control vibration intensity
            // So we use a pattern of short vibrations to simulate lower intensity
            long[] pattern = { 0, (long)(duration * 1000 * intensity) };
            
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    using (AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                    {
                        vibrator.Call("vibrate", pattern, -1);
                    }
                }
            }
        }
#endif
    }

    /// <summary>
    /// Checks if a touch is over a UI element
    /// </summary>
    /// <param name="fingerId">The finger ID of the touch</param>
    /// <returns>True if the touch is over a UI element</returns>
    private bool IsPointerOverUI(int fingerId)
    {
        // Check if the touch is over a UI element
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(fingerId);
        }
        return false;
    }

    private string GestureTypeToSpeech(GestureType gestureType)
    {
        // Convert gesture type to a more readable/speakable form
        switch (gestureType)
        {
            case GestureType.Tap:
                return "tap";
            case GestureType.DoubleTap:
                return "double tap";
            case GestureType.TripleTap:
                return "triple tap";
            case GestureType.LongPress:
                return "long press";
            case GestureType.SwipeUp:
                return "swipe up";
            case GestureType.SwipeDown:
                return "swipe down";
            case GestureType.SwipeLeft:
                return "swipe left";
            case GestureType.SwipeRight:
                return "swipe right";
            case GestureType.TwoFingerTap:
                return "two finger tap";
            case GestureType.TwoFingerSwipeUp:
                return "two finger swipe up";
            case GestureType.TwoFingerSwipeDown:
                return "two finger swipe down";
            case GestureType.TwoFingerPinch:
                return "pinch";
            case GestureType.TwoFingerSpread:
                return "spread";
            default:
                return gestureType.ToString();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void AndroidVibrate(long milliseconds)
    {
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                using (AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    vibrator.Call("vibrate", milliseconds);
                }
            }
        }
    }

    private void AndroidVibrate(long[] pattern, int repeat)
    {
        using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                using (AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    vibrator.Call("vibrate", pattern, repeat);
                }
            }
        }
    }
#endif
}