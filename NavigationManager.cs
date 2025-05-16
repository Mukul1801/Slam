using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using TMPro;

public class NavigationManager : MonoBehaviour
{
    // References
    public HitPointManager hitPointManager;
    public SafePathPlanner safePathPlanner;
    public AudioSource audioSource;
    public TextToSpeech textToSpeech;

    [Header("Audio Feedback")]
    public AudioClip waypointReachedSound;
    public AudioClip turnLeftSound;
    public AudioClip turnRightSound;
    public AudioClip straightAheadSound;
    public AudioClip destinationReachedSound;
    public AudioClip obstacleNearbySound;
    public AudioClip pathStartedSound;
    public AudioClip errorSound;

    [Header("Haptic Feedback")]
    public bool useVibration = true;
    public float vibrationDuration = 0.5f;
    public float vibrationIntensity = 1.0f;

    [Header("Navigation Settings")]
    public float waypointReachedDistance = 1.0f;
    public float directionUpdateFrequency = 2.0f;
    public float obstacleWarningDistance = 2.5f;
    public bool isNavigating = false;
    public bool useDistanceBasedGuidance = true;
    public float minDistanceForUpdate = 0.5f;
    public float turnAngleThreshold = 15f; // Degrees threshold to identify a turn

    [Header("Accessibility")]
    public bool useDetailedAudioDescriptions = true;
    public bool useCoarseSpatialNavigation = false; // Simpler directions for users with severe visual impairment
    public TextMeshProUGUI debugTextDisplay;
    public bool enableTapForNextDirection = true;
    public float hazardProximityWarningFrequency = 0.5f; // Seconds between warnings when near obstacles

    [Header("Advanced Settings")]
    public bool useProgressiveGuidance = true; // More detailed guidance when moving slowly
    public float userSpeedThreshold = 0.5f; // m/s, threshold to determine if user is moving slowly
    public float pathCompletionAnnouncementDistance = 5.0f; // Start announcing distance to destination

    // Private state variables
    private float lastDirectionUpdate = 0f;
    private Vector3 lastUpdatePosition;
    private Vector3 lastUserPosition;
    private float lastUserSpeed = 0f;
    private float lastObstacleWarningTime = 0f;
    private string debugText = "";
    private List<string> spokenDirections = new List<string>(); // Avoid repeating the same instruction
    private bool destinationAnnouncementStarted = false;

    void Start()
    {
        // Find references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (safePathPlanner == null)
            safePathPlanner = GetComponent<SafePathPlanner>();

        if (safePathPlanner == null)
            safePathPlanner = gameObject.AddComponent<SafePathPlanner>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (textToSpeech == null)
            textToSpeech = FindObjectOfType<TextToSpeech>();

        if (textToSpeech == null && FindObjectOfType<TextToSpeech>() == null)
            textToSpeech = gameObject.AddComponent<TextToSpeech>();

        // Initialize path planner
        if (safePathPlanner != null)
        {
            safePathPlanner.hitPointManager = hitPointManager;
            safePathPlanner.navigationManager = this;
        }

        // Initialize state variables
        lastUpdatePosition = Camera.main.transform.position;
        lastUserPosition = Camera.main.transform.position;
    }

    void Update()
    {
        if (isNavigating)
        {
            // Track user movement speed
            float deltaTime = Time.deltaTime;
            Vector3 userPosition = Camera.main.transform.position;
            float distanceMoved = Vector3.Distance(lastUserPosition, userPosition);
            lastUserSpeed = distanceMoved / deltaTime;
            lastUserPosition = userPosition;

            // Update navigation
            NavigateAlongPath();

            // Check for tap input if enabled
            if (enableTapForNextDirection && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                // Check if tap is on UI element
                bool isTapOnUI = UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);

                // If not on UI, provide next direction
                if (!isTapOnUI)
                {
                    GiveDirectionToNextPathPoint(true); // Force an update
                }
            }
        }

        // Update debug text if enabled
        if (debugTextDisplay != null)
        {
            debugTextDisplay.text = debugText;
        }
    }

    public void StartNavigation()
    {
        if (hitPointManager.poseClassList.Count > 0)
        {
            // First check if we have at least start and end points
            bool hasStartPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.StartPoint);
            bool hasEndPoint = hitPointManager.poseClassList.Any(p => p.waypointType == WaypointType.EndPoint);

            if (!hasStartPoint || !hasEndPoint)
            {
                // If not explicitly defined, check if we can use first and last points
                if (hitPointManager.poseClassList.Count >= 2)
                {
                    // Use first point as start and last point as end
                    hitPointManager.poseClassList[0].waypointType = WaypointType.StartPoint;
                    hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1].waypointType = WaypointType.EndPoint;

                    SpeakMessage("No explicit start and end points found. Using first and last points instead.");
                }
                else
                {
                    SpeakMessage("Error: At least two points are needed for navigation - a start and an end point.");
                    PlaySound(errorSound);
                    return;
                }
            }

            // Plan safe path
            bool pathPlanned = safePathPlanner.PlanSafePath();

            if (pathPlanned)
            {
                isNavigating = true;
                lastUpdatePosition = Camera.main.transform.position;
                destinationAnnouncementStarted = false;

                // Clear previous spoken directions
                spokenDirections.Clear();

                // Play start sound
                PlaySound(pathStartedSound);

                // Announce start of navigation
                SpeakMessage("Navigation started. Follow the audio cues to safely reach your destination.");

                // Vibrate to signal start
                if (useVibration)
                    Vibrate();

                // First direction update
                GiveDirectionToNextPathPoint(true);

                UpdateDebugText("Navigation active - follow audio cues.");
            }
            else
            {
                SpeakMessage("Unable to find a safe path to your destination. Please try again in a different location.");
                PlaySound(errorSound);
                UpdateDebugText("Error: No safe path found");
            }
        }
        else
        {
            SpeakMessage("No waypoints available. Please load a path or create a new one.");
            PlaySound(errorSound);
            UpdateDebugText("Error: No waypoints available");
        }
    }

    public void StopNavigation()
    {
        if (isNavigating)
        {
            isNavigating = false;

            // Stop any ongoing speech
            if (textToSpeech != null)
                textToSpeech.StopSpeaking();

            SpeakMessage("Navigation stopped.");
            UpdateDebugText("Navigation stopped");
        }
    }

    private void NavigateAlongPath()
    {
        Vector3 userPosition = Camera.main.transform.position;

        // Update which path point we're heading toward
        safePathPlanner.UpdateCurrentPathIndex(userPosition);

        // Get the next path point to move toward
        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();

        // Calculate distance to current target (horizontal plane only)
        Vector3 horizontalUserPosition = new Vector3(userPosition.x, 0, userPosition.z);
        Vector3 horizontalPathPoint = new Vector3(nextPathPoint.x, 0, nextPathPoint.z);
        float distance = Vector3.Distance(horizontalUserPosition, horizontalPathPoint);

        UpdateDebugText("Distance to next point: " + distance.ToString("F2") + "m\n" +
                        "Speed: " + lastUserSpeed.ToString("F2") + "m/s");

        // Check for nearby obstacles and warn if needed
        if (Time.time - lastObstacleWarningTime > hazardProximityWarningFrequency)
        {
            WarnAboutNearbyObstacles(userPosition);
            lastObstacleWarningTime = Time.time;
        }

        // Update direction guidance based on configured conditions
        bool shouldUpdate = false;

        if (useDistanceBasedGuidance)
        {
            // Update based on how far the user has moved
            float distanceMoved = Vector3.Distance(lastUpdatePosition, userPosition);

            // Update if we've moved enough or if we're moving slowly and using progressive guidance
            if (distanceMoved > minDistanceForUpdate ||
                (useProgressiveGuidance && lastUserSpeed < userSpeedThreshold && Time.time - lastDirectionUpdate > directionUpdateFrequency * 2))
            {
                shouldUpdate = true;
                lastUpdatePosition = userPosition;
            }
        }
        else
        {
            // Update based on time frequency
            if (Time.time - lastDirectionUpdate > directionUpdateFrequency)
            {
                shouldUpdate = true;
            }
        }

        if (shouldUpdate)
        {
            GiveDirectionToNextPathPoint();
            lastDirectionUpdate = Time.time;
        }

        // Check if destination reached
        CheckDestinationReached(userPosition);

        // Check if we should start announcing approach to destination
        if (!destinationAnnouncementStarted)
        {
            PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);
            if (endPose != null)
            {
                float distanceToDestination = Vector3.Distance(
                    new Vector3(userPosition.x, 0, userPosition.z),
                    new Vector3(endPose.position.x, 0, endPose.position.z));

                if (distanceToDestination < pathCompletionAnnouncementDistance)
                {
                    SpeakMessage("Approaching your destination. About " +
                                distanceToDestination.ToString("F1") + " meters to go.");
                    destinationAnnouncementStarted = true;
                }
            }
        }
    }

    private void WarnAboutNearbyObstacles(Vector3 userPosition)
    {
        bool obstacleWarningGiven = false;
        List<PoseClass> nearbyObstacles = new List<PoseClass>();

        // Check distance to all obstacles
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                float obstacleDistance = Vector3.Distance(
                    new Vector3(userPosition.x, 0, userPosition.z),
                    new Vector3(pose.position.x, 0, pose.position.z));

                if (obstacleDistance < obstacleWarningDistance)
                {
                    // Add to nearby obstacles
                    nearbyObstacles.Add(pose);
                }
            }
        }

        // If multiple obstacles are nearby, warn about the closest one
        if (nearbyObstacles.Count > 0)
        {
            // Sort by distance
            nearbyObstacles.Sort((a, b) =>
                Vector3.Distance(userPosition, a.position).CompareTo(
                Vector3.Distance(userPosition, b.position)));

            // Get closest obstacle
            PoseClass closestObstacle = nearbyObstacles[0];
            float obstacleDistance = Vector3.Distance(userPosition, closestObstacle.position);

            // Determine direction to obstacle
            Vector3 directionToObstacle = closestObstacle.position - userPosition;
            directionToObstacle.y = 0;

            // Get user's forward direction
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // Calculate angle between user's forward and obstacle
            float angle = Vector3.SignedAngle(userForward, directionToObstacle, Vector3.up);

            // Determine verbal direction
            string direction = GetDirectionName(angle);

            // Only warn if obstacle is somewhat in front of user (within about 120 degrees)
            if (Mathf.Abs(angle) < 60f)
            {
                // Play obstacle warning sound with volume based on proximity
                float warningVolume = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));
                PlaySound(obstacleNearbySound, warningVolume);
                obstacleWarningGiven = true;

                // Vibrate with intensity based on proximity
                if (useVibration)
                {
                    float intensity = Mathf.Lerp(0.3f, 1.0f, 1.0f - (obstacleDistance / obstacleWarningDistance));
                    Vibrate(intensity);
                }

                // Speak warning message
                string intensityWord = "";
                if (obstacleDistance < 1.0f)
                    intensityWord = "very close ";
                else if (obstacleDistance < 1.5f)
                    intensityWord = "close ";

                SpeakMessage("Caution! " + intensityWord + "Obstacle " + direction + ", " +
                             obstacleDistance.ToString("F1") + " meters away.");

                // Update debug text
                UpdateDebugText("⚠️ Obstacle " + direction + ": " + obstacleDistance.ToString("F1") + "m");
            }
        }
    }

    private void GiveDirectionToNextPathPoint(bool forceUpdate = false)
    {
        Vector3 nextPathPoint = safePathPlanner.GetNextPathPoint();
        int currentPathIndex = safePathPlanner.GetCurrentPathIndex();
        bool isLastPoint = currentPathIndex >= safePathPlanner.GetPathCount() - 1;

        // Get user's current position and forward direction
        Transform cameraTransform = Camera.main.transform;
        Vector3 userPosition = cameraTransform.position;
        Vector3 userForward = cameraTransform.forward;
        userForward.y = 0; // Ignore vertical component for direction calculation
        userForward.Normalize();

        // Get direction to next path point
        Vector3 directionToPathPoint = nextPathPoint - userPosition;
        directionToPathPoint.y = 0; // Ignore vertical component

        // Skip if direction is zero (we're at the point)
        if (directionToPathPoint.magnitude < 0.1f && !forceUpdate)
            return;

        directionToPathPoint.Normalize();

        // Calculate angle between user's forward direction and path point direction
        float angle = Vector3.SignedAngle(userForward, directionToPathPoint, Vector3.up);

        // Determine verbal direction based on angle
        string direction = GetDirectionName(angle);
        AudioClip directionSound = GetDirectionSound(angle);

        // Calculate distance to path point
        float distance = Vector3.Distance(userPosition, nextPathPoint);
        string distanceStr = distance.ToString("F1");

        // Determine if this is a significant turn
        bool isSignificantTurn = Mathf.Abs(angle) > turnAngleThreshold;

        // Create directional message
        string directionMessage;

        // Different message format for last point (destination)
        if (isLastPoint)
        {
            directionMessage = "Your destination is " + direction + ", " + distanceStr + " meters away.";
        }
        else
        {
            // Get turns and obstacles along the path
            string pathDescription = "";
            if (useProgressiveGuidance && lastUserSpeed < userSpeedThreshold)
            {
                pathDescription = safePathPlanner.GetUpcomingPathDescription(currentPathIndex, 3);
                if (!string.IsNullOrEmpty(pathDescription))
                {
                    pathDescription = " " + pathDescription;
                }
            }

            if (useCoarseSpatialNavigation)
            {
                // Simpler directions for severe visual impairment
                directionMessage = "Go " + direction + ", " + distanceStr + " meters.";
            }
            else if (isSignificantTurn)
            {
                // More emphasis on the turn
                directionMessage = "Turn " + direction + " and continue for " + distanceStr + " meters." + pathDescription;
            }
            else
            {
                // Standard directional guidance
                directionMessage = "Continue " + direction + " for " + distanceStr + " meters." + pathDescription;
            }
        }

        // Check if this is identical to the last direction given, if so don't repeat unless forced
        if (forceUpdate || !spokenDirections.Contains(directionMessage))
        {
            // Update debug information
            UpdateDebugText("Next point: " + distance.ToString("F1") + "m" +
                            "\nDirection: " + direction +
                            "\nAngle: " + angle.ToString("F0") + "°");

            // Add to spoken directions history (limit to last 5 directions)
            spokenDirections.Add(directionMessage);
            if (spokenDirections.Count > 5)
                spokenDirections.RemoveAt(0);

            // Speak direction to user
            SpeakMessage(directionMessage);

            // Play direction sound
            if (directionSound != null)
                PlaySound(directionSound);

            // Vibrate for significant turns
            if (useVibration && isSignificantTurn)
                Vibrate();
        }
    }

    private string GetDirectionName(float angle)
    {
        if (useCoarseSpatialNavigation)
        {
            // Simpler 8-point directions for severe visual impairment
            if (Mathf.Abs(angle) < 22.5f)
            {
                return "straight ahead";
            }
            else if (angle < -157.5f || angle > 157.5f)
            {
                return "behind you";
            }
            else if (angle < -112.5f)
            {
                return "behind you to the left";
            }
            else if (angle < -67.5f)
            {
                return "to your left";
            }
            else if (angle < -22.5f)
            {
                return "slightly left";
            }
            else if (angle < 67.5f)
            {
                return "slightly right";
            }
            else if (angle < 112.5f)
            {
                return "to your right";
            }
            else
            {
                return "behind you to the right";
            }
        }
        else
        {
            // More precise directions
            if (Mathf.Abs(angle) < 10f)
            {
                return "straight ahead";
            }
            else if (angle < -170f || angle > 170f)
            {
                return "behind you";
            }
            else if (angle < -135f)
            {
                return "behind you to the left";
            }
            else if (angle < -90f)
            {
                return "to your left";
            }
            else if (angle < -45f)
            {
                return "forward left";
            }
            else if (angle < -10f)
            {
                return "slightly left";
            }
            else if (angle < 45f)
            {
                return "slightly right";
            }
            else if (angle < 90f)
            {
                return "forward right";
            }
            else if (angle < 135f)
            {
                return "to your right";
            }
            else
            {
                return "behind you to the right";
            }
        }
    }

    private AudioClip GetDirectionSound(float angle)
    {
        if (Mathf.Abs(angle) < 15f)
        {
            return straightAheadSound;
        }
        else if (angle < 0f)
        {
            return turnLeftSound;
        }
        else
        {
            return turnRightSound;
        }
    }

    private void CheckDestinationReached(Vector3 userPosition)
    {
        // Find end point
        PoseClass endPose = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);
        if (endPose == null && hitPointManager.poseClassList.Count > 0)
        {
            // Use last point if no explicit end point
            endPose = hitPointManager.poseClassList[hitPointManager.poseClassList.Count - 1];
        }

        if (endPose != null)
        {
            float distanceToDestination = Vector3.Distance(
                new Vector3(userPosition.x, 0, userPosition.z),
                new Vector3(endPose.position.x, 0, endPose.position.z));

            if (distanceToDestination < waypointReachedDistance)
            {
                // Reached destination
                EndNavigation();
            }
        }
    }

    private void EndNavigation()
    {
        isNavigating = false;

        // Play destination reached sound
        PlaySound(destinationReachedSound);

        // Vibrate device if enabled
        if (useVibration)
            Vibrate(1.0f, 0.8f);

        SpeakMessage("You have reached your destination safely. Navigation completed.");
        UpdateDebugText("✅ Destination reached!");

        // Reset variables
        destinationAnnouncementStarted = false;
    }

    public void SpeakMessage(string message)
    {
        if (textToSpeech != null)
        {
            textToSpeech.Speak(message);
            UpdateDebugText("🔊 " + message);
        }
        else
        {
            Debug.LogWarning("TextToSpeech component not found. Message: " + message);
            UpdateDebugText("TTS not found: " + message);
        }
    }

    private void PlaySound(AudioClip clip, float volume = 1.0f)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    private void Vibrate(float intensity = 1.0f, float duration = -1.0f)
    {
        if (duration < 0)
            duration = vibrationDuration;

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
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            vibrator.Call("vibrate", pattern, -1);
        }
#endif
    }

    private void UpdateDebugText(string text)
    {
        debugText = text;
    }

    // Method to provide next instruction on demand (for manual triggering)
    public void ProvideNextInstruction()
    {
        if (isNavigating)
        {
            GiveDirectionToNextPathPoint(true);
        }
    }

    // Function to reset all navigation data
    public void ResetNavigation()
    {
        StopNavigation();
        destinationAnnouncementStarted = false;
        spokenDirections.Clear();
        lastDirectionUpdate = 0;
        lastObstacleWarningTime = 0;
    }
}