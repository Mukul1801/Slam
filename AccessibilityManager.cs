using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AccessibilityManager : MonoBehaviour
{
    [Header("References")]
    public HitPointManager hitPointManager;
    public NavigationManager navigationManager;
    public TextToSpeech textToSpeech;
    public Canvas accessibilityCanvas;

    [Header("Gesture Settings")]
    public bool enableGestureControls = true;
    public float doubleTapMaxDelay = 0.3f;
    public float longPressTime = 0.8f;
    public float swipeMinDistance = 50f;

    [Header("Audio Feedback")]
    public AudioSource audioSource;
    public AudioClip tapSound;
    public AudioClip swipeSound;
    public AudioClip menuOpenSound;
    public AudioClip menuCloseSound;
    public AudioClip errorSound;

    [Header("Speech Feedback")]
    public bool announceFeatures = true;
    public bool verboseMode = false;
    public float initialInstructionDelay = 2.0f;

    [Header("Accessibility UI")]
    public GameObject accessibilityMenuPanel;
    public Button increaseFontSizeButton;
    public Button decreaseFontSizeButton;
    public Button increaseSpeechRateButton;
    public Button decreaseSpeechRateButton;
    public Button highContrastModeButton;
    public Button enableVibrationButton;
    public TextMeshProUGUI accessibilityStatusText;

    // Gesture state tracking
    private bool isLongPressing = false;
    private float longPressStart = 0f;
    private Vector2 touchStartPosition;
    private float lastTapTime = 0f;
    private int consecutiveTaps = 0;

    // Accessibility state
    [SerializeField] private float textScale = 1.0f;
    [SerializeField] private bool highContrastMode = false;
    [SerializeField] private bool vibrationEnabled = true;
    private bool accessibilityMenuOpen = false;

    // UI element cache
    private List<TextMeshProUGUI> allTextElements = new List<TextMeshProUGUI>();
    private List<Image> allImageElements = new List<Image>();

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        if (textToSpeech == null)
            textToSpeech = FindObjectOfType<TextToSpeech>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Initialize UI elements
        if (accessibilityMenuPanel != null)
            accessibilityMenuPanel.SetActive(false);

        // Set up button listeners
        SetupButtonListeners();

        // Cache all text and image elements for accessibility adjustments
        CacheUIElements();

        // Initialize accessibility settings
        ApplyTextScaling();
        ApplyContrastMode();

        // Set vibration mode on navigation manager
        if (navigationManager != null)
            navigationManager.useVibration = vibrationEnabled;

        // Announce initial instructions after a delay
        if (announceFeatures)
            Invoke("AnnounceInitialInstructions", initialInstructionDelay);
    }

    void Update()
    {
        // Process gesture controls if enabled
        if (enableGestureControls)
            ProcessGestures();
    }

    private void ProcessGestures()
    {
        // Only process gestures if no UI elements are being interacted with
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        // Get touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            // Handle touch phases
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    touchStartPosition = touch.position;
                    longPressStart = Time.time;
                    isLongPressing = true;
                    break;

                case TouchPhase.Moved:
                    // Check if movement exceeds threshold for a swipe
                    float touchDistance = Vector2.Distance(touch.position, touchStartPosition);
                    if (touchDistance > swipeMinDistance)
                    {
                        // Determine swipe direction
                        Vector2 direction = touch.position - touchStartPosition;
                        ProcessSwipe(direction);

                        // Reset long press detection
                        isLongPressing = false;
                    }
                    break;

                case TouchPhase.Ended:
                    // Check for tap
                    float touchDuration = Time.time - longPressStart;

                    if (isLongPressing)
                    {
                        if (touchDuration >= longPressTime)
                        {
                            // Long press detected
                            ProcessLongPress(touch.position);
                        }
                        else
                        {
                            // Short tap detected
                            ProcessTap(touch.position);
                        }
                    }

                    isLongPressing = false;
                    break;
            }
        }
    }

    private void ProcessTap(Vector2 position)
    {
        // Check for double/triple tap
        float timeSinceLastTap = Time.time - lastTapTime;

        if (timeSinceLastTap <= doubleTapMaxDelay)
        {
            consecutiveTaps++;

            if (consecutiveTaps == 1)
            {
                // Double tap
                OnDoubleTap(position);
            }
            else if (consecutiveTaps == 2)
            {
                // Triple tap
                OnTripleTap(position);
                consecutiveTaps = 0; // Reset after triple tap
            }
        }
        else
        {
            // Single tap
            consecutiveTaps = 0;
            OnSingleTap(position);
        }

        lastTapTime = Time.time;
    }

    private void ProcessLongPress(Vector2 position)
    {
        PlaySound(tapSound);

        // Toggle accessibility menu
        ToggleAccessibilityMenu();
    }

    private void ProcessSwipe(Vector2 direction)
    {
        PlaySound(swipeSound);

        // Normalize direction
        direction.Normalize();

        // Determine swipe direction (simplified to 4 directions)
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            // Horizontal swipe
            if (direction.x > 0)
            {
                // Right swipe
                OnSwipeRight();
            }
            else
            {
                // Left swipe
                OnSwipeLeft();
            }
        }
        else
        {
            // Vertical swipe
            if (direction.y > 0)
            {
                // Up swipe
                OnSwipeUp();
            }
            else
            {
                // Down swipe
                OnSwipeDown();
            }
        }
    }

    // Gesture handlers
    private void OnSingleTap(Vector2 position)
    {
        PlaySound(tapSound);

        // Request next navigation instruction
        if (navigationManager != null && navigationManager.isNavigating)
        {
            navigationManager.ProvideNextInstruction();
        }
        else
        {
            // If not navigating, announce what's in front
            AnnounceWhatsAhead();
        }
    }

    private void OnDoubleTap(Vector2 position)
    {
        PlaySound(tapSound, 2);

        // Start/stop navigation
        if (navigationManager != null)
        {
            if (navigationManager.isNavigating)
            {
                navigationManager.StopNavigation();
                SpeakMessage("Navigation stopped");
            }
            else if (hitPointManager.poseClassList.Count > 0)
            {
                navigationManager.StartNavigation();
            }
            else
            {
                SpeakMessage("No path available. Please load or create a path first.");
                PlaySound(errorSound);
            }
        }
    }

    private void OnTripleTap(Vector2 position)
    {
        PlaySound(tapSound, 3);

        // Announce current location and status
        AnnounceCurrentStatus();
    }

    private void OnSwipeRight()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: increase value
            IncreaseValue();
        }
        else
        {
            // Not in menu: next path/waypoint
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Skipping to next waypoint");
                // Logic to skip to next waypoint would go here
            }
        }
    }

    private void OnSwipeLeft()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: decrease value
            DecreaseValue();
        }
        else
        {
            // Not in menu: previous path/waypoint
            if (navigationManager != null && !navigationManager.isNavigating)
            {
                SpeakMessage("Going back to previous waypoint");
                // Logic to go back to previous waypoint would go here
            }
        }
    }

    private void OnSwipeUp()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: previous option
            SelectPreviousOption();
        }
        else
        {
            // Load path
            SpeakMessage("Loading saved paths");
            if (hitPointManager != null)
            {
                hitPointManager.PromptFilenameToLoad();
            }
        }
    }

    private void OnSwipeDown()
    {
        if (accessibilityMenuOpen)
        {
            // In menu: next option
            SelectNextOption();
        }
        else
        {
            // Save current path
            SpeakMessage("Saving current path");
            if (hitPointManager != null && hitPointManager.poseClassList.Count > 0)
            {
                hitPointManager.SaveAllTheInformationToFile();
            }
            else
            {
                SpeakMessage("No path to save");
                PlaySound(errorSound);
            }
        }
    }

    // Menu operations
    private void ToggleAccessibilityMenu()
    {
        accessibilityMenuOpen = !accessibilityMenuOpen;

        if (accessibilityMenuPanel != null)
            accessibilityMenuPanel.SetActive(accessibilityMenuOpen);

        PlaySound(accessibilityMenuOpen ? menuOpenSound : menuCloseSound);

        if (accessibilityMenuOpen)
        {
            SpeakMessage("Accessibility menu opened. Swipe up or down to navigate options, left or right to change values.");
            UpdateAccessibilityStatusText();
        }
        else
        {
            SpeakMessage("Accessibility menu closed");
        }
    }

    private void SelectNextOption()
    {
        // Logic to select next option in accessibility menu
        // Would need UI elements to implement
        SpeakMessage("Next option");
        PlaySound(tapSound);
    }

    private void SelectPreviousOption()
    {
        // Logic to select previous option in accessibility menu
        // Would need UI elements to implement
        SpeakMessage("Previous option");
        PlaySound(tapSound);
    }

    private void IncreaseValue()
    {
        // Increase value of current option
        // Example implementation for text size
        IncreaseTextSize();
    }

    private void DecreaseValue()
    {
        // Decrease value of current option
        // Example implementation for text size
        DecreaseTextSize();
    }

    // Accessibility features
    public void IncreaseTextSize()
    {
        textScale += 0.1f;
        textScale = Mathf.Clamp(textScale, 1.0f, 2.0f);
        ApplyTextScaling();

        SpeakMessage("Text size increased");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void DecreaseTextSize()
    {
        textScale -= 0.1f;
        textScale = Mathf.Clamp(textScale, 1.0f, 2.0f);
        ApplyTextScaling();

        SpeakMessage("Text size decreased");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void IncreaseSpeechRate()
    {
        if (textToSpeech != null)
        {
            float newRate = textToSpeech.speechRate + 0.1f;
            newRate = Mathf.Clamp(newRate, 0.5f, 2.0f);
            textToSpeech.UpdateSpeechRate(newRate);

            SpeakMessage("Speech rate increased");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }

    public void DecreaseSpeechRate()
    {
        if (textToSpeech != null)
        {
            float newRate = textToSpeech.speechRate - 0.1f;
            newRate = Mathf.Clamp(newRate, 0.5f, 2.0f);
            textToSpeech.UpdateSpeechRate(newRate);

            SpeakMessage("Speech rate decreased");
            PlaySound(tapSound);
            UpdateAccessibilityStatusText();
        }
    }

    public void ToggleHighContrastMode()
    {
        highContrastMode = !highContrastMode;
        ApplyContrastMode();

        SpeakMessage(highContrastMode ? "High contrast mode enabled" : "High contrast mode disabled");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    public void ToggleVibration()
    {
        vibrationEnabled = !vibrationEnabled;

        if (navigationManager != null)
            navigationManager.useVibration = vibrationEnabled;

        SpeakMessage(vibrationEnabled ? "Vibration enabled" : "Vibration disabled");
        PlaySound(tapSound);
        UpdateAccessibilityStatusText();
    }

    // Helper methods
    private void ApplyTextScaling()
    {
        foreach (TextMeshProUGUI text in allTextElements)
        {
            if (text != null)
                text.fontSize = text.fontSize * textScale / text.transform.localScale.x;
        }
    }

    private void ApplyContrastMode()
    {
        if (highContrastMode)
        {
            // Apply high contrast theme
            foreach (Image image in allImageElements)
            {
                if (image != null)
                {
                    // Increase contrast by making dark colors darker and light colors lighter
                    Color color = image.color;
                    float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                    if (luminance > 0.5f)
                    {
                        // Make light colors lighter
                        color = Color.Lerp(color, Color.white, 0.3f);
                    }
                    else
                    {
                        // Make dark colors darker
                        color = Color.Lerp(color, Color.black, 0.3f);
                    }

                    image.color = color;
                }
            }

            // Make text more contrasty
            foreach (TextMeshProUGUI text in allTextElements)
            {
                if (text != null)
                {
                    Color color = text.color;
                    float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;

                    if (luminance > 0.5f)
                    {
                        text.color = Color.white;
                    }
                    else
                    {
                        text.color = Color.black;
                    }

                    // Increase outline for better readability
                    text.outlineWidth = 0.2f;
                    text.outlineColor = luminance > 0.5f ? Color.black : Color.white;
                }
            }
        }
        else
        {
            // Reset to normal contrast
            // Would need to store original colors to properly implement
        }
    }

    private void CacheUIElements()
    {
        // Find all text elements
        TextMeshProUGUI[] texts = FindObjectsOfType<TextMeshProUGUI>();
        allTextElements.AddRange(texts);

        // Find all image elements
        Image[] images = FindObjectsOfType<Image>();
        allImageElements.AddRange(images);
    }

    private void SetupButtonListeners()
    {
        if (increaseFontSizeButton != null)
            increaseFontSizeButton.onClick.AddListener(IncreaseTextSize);

        if (decreaseFontSizeButton != null)
            decreaseFontSizeButton.onClick.AddListener(DecreaseTextSize);

        if (increaseSpeechRateButton != null)
            increaseSpeechRateButton.onClick.AddListener(IncreaseSpeechRate);

        if (decreaseSpeechRateButton != null)
            decreaseSpeechRateButton.onClick.AddListener(DecreaseSpeechRate);

        if (highContrastModeButton != null)
            highContrastModeButton.onClick.AddListener(ToggleHighContrastMode);

        if (enableVibrationButton != null)
            enableVibrationButton.onClick.AddListener(ToggleVibration);
    }

    private void UpdateAccessibilityStatusText()
    {
        if (accessibilityStatusText != null)
        {
            string status = "Text Size: " + (textScale * 100).ToString("F0") + "%\n";

            if (textToSpeech != null)
                status += "Speech Rate: " + (textToSpeech.speechRate * 100).ToString("F0") + "%\n";

            status += "High Contrast: " + (highContrastMode ? "On" : "Off") + "\n";
            status += "Vibration: " + (vibrationEnabled ? "On" : "Off");

            accessibilityStatusText.text = status;
        }
    }

    private void PlaySound(AudioClip clip, int repetitions = 1)
    {
        if (audioSource != null && clip != null)
        {
            // Play sound the specified number of times
            StartCoroutine(PlaySoundRepeatedly(clip, repetitions));
        }
    }

    private IEnumerator PlaySoundRepeatedly(AudioClip clip, int repetitions)
    {
        for (int i = 0; i < repetitions; i++)
        {
            audioSource.PlayOneShot(clip);

            if (i < repetitions - 1)
                yield return new WaitForSeconds(clip.length * 0.5f);
        }
    }

    private void SpeakMessage(string message)
    {
        if (textToSpeech != null)
        {
            textToSpeech.Speak(message);
        }
    }

    private void AnnounceInitialInstructions()
    {
        string instructions = "Welcome to the Accessible Navigation System. ";
        instructions += "Single tap to get directions. Double tap to start or stop navigation. ";
        instructions += "Triple tap for current status. Long press to open accessibility menu. ";
        instructions += "Swipe up to load paths, down to save paths.";

        SpeakMessage(instructions);
    }

    private void AnnounceCurrentStatus()
    {
        string status = "Current status: ";

        if (navigationManager != null && navigationManager.isNavigating)
        {
            status += "Currently navigating. ";
        }
        else
        {
            status += "Not navigating. ";
        }

        if (hitPointManager != null)
        {
            int pathPoints = 0;
            int obstacles = 0;
            bool hasStartPoint = false;
            bool hasEndPoint = false;

            foreach (var pose in hitPointManager.poseClassList)
            {
                switch (pose.waypointType)
                {
                    case WaypointType.PathPoint:
                        pathPoints++;
                        break;
                    case WaypointType.Obstacle:
                        obstacles++;
                        break;
                    case WaypointType.StartPoint:
                        hasStartPoint = true;
                        break;
                    case WaypointType.EndPoint:
                        hasEndPoint = true;
                        break;
                }
            }

            if (hitPointManager.poseClassList.Count > 0)
            {
                status += "Path loaded with " + pathPoints + " path points, " + obstacles + " obstacles. ";
                status += hasStartPoint ? "Start point defined. " : "No start point defined. ";
                status += hasEndPoint ? "End point defined. " : "No end point defined. ";
            }
            else
            {
                status += "No path loaded. ";
            }
        }

        SpeakMessage(status);
    }

    private void AnnounceWhatsAhead()
    {
        if (hitPointManager == null || Camera.main == null)
            return;

        // Cast a ray forward to detect obstacles
        Vector3 rayStart = Camera.main.transform.position;
        Vector3 rayDirection = Camera.main.transform.forward;

        string message = "";
        bool foundSomething = false;

        // Check for obstacles in front
        RaycastHit hit;
        if (Physics.Raycast(rayStart, rayDirection, out hit, 10f))
        {
            message = "There is ";

            if (hit.collider.CompareTag("Obstacle"))
            {
                message += "an obstacle about " + hit.distance.ToString("F1") + " meters ahead.";
            }
            else if (hit.collider.CompareTag("Waypoint"))
            {
                message += "a waypoint about " + hit.distance.ToString("F1") + " meters ahead.";
            }
            else
            {
                message += "something about " + hit.distance.ToString("F1") + " meters ahead.";
            }

            foundSomething = true;
        }

        // Check for nearby obstacles in any direction
        float nearestObstacleDistance = float.MaxValue;
        Vector3 nearestObstacleDirection = Vector3.zero;

        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float distance = Vector3.Distance(rayStart, pose.position);
                if (distance < 3.0f && distance < nearestObstacleDistance) // Within 3 meters
                {
                    nearestObstacleDistance = distance;
                    nearestObstacleDirection = pose.position - rayStart;
                }
            }
        }

        if (nearestObstacleDistance < float.MaxValue)
        {
            if (foundSomething)
                message += " Also, ";
            else
                message = "Caution, ";

            // Determine direction to nearest obstacle
            nearestObstacleDirection.y = 0;
            nearestObstacleDirection.Normalize();

            Vector3 forward = Camera.main.transform.forward;
            forward.y = 0;
            forward.Normalize();

            float angle = Vector3.SignedAngle(forward, nearestObstacleDirection, Vector3.up);
            string direction = GetRelativeDirection(angle);

            message += "obstacle " + direction + ", " + nearestObstacleDistance.ToString("F1") + " meters away.";
            foundSomething = true;
        }

        if (!foundSomething)
        {
            message = "No obstacles detected nearby. The path appears clear.";
        }

        SpeakMessage(message);
    }

    private string GetRelativeDirection(float angle)
    {
        if (Mathf.Abs(angle) < 22.5f)
            return "straight ahead";
        else if (angle < -157.5f || angle > 157.5f)
            return "behind you";
        else if (angle < -112.5f)
            return "behind you to the left";
        else if (angle < -67.5f)
            return "to your left";
        else if (angle < -22.5f)
            return "slightly to the left";
        else if (angle < 67.5f)
            return "slightly to the right";
        else if (angle < 112.5f)
            return "to your right";
        else
            return "behind you to the right";
    }
}