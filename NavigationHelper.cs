using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class NavigationHelper : MonoBehaviour
{
    [Header("References")]
    public NavigationManager navigationManager;
    public HitPointManager hitPointManager;
    public AccessibilityManager accessibilityManager;

    [Header("Navigation Settings")]
    public float proximityTriggerDistance = 1.5f; // Distance to trigger proximity alerts
    public float destinationUpdateInterval = 5.0f; // How often to update distance to destination
    public float obstacleScanInterval = 0.5f; // How often to scan for obstacles
    public float obstacleWarningThreshold = 2.0f; // Distance to trigger obstacle warnings
    public float obstacleHighAlertThreshold = 1.0f; // Distance for high priority obstacle warnings
    public float offRouteThreshold = 3.0f; // Distance from path to trigger off-route warning

    [Header("Audio Feedback")]
    public AudioClip proximityBeep;
    public AudioClip obstacleScanSound;
    public AudioClip pathClearSound;
    public AudioClip[] countdownSounds; // Sounds for countdown when approaching destination
    public AudioClip offRouteSound;

    [Header("Vibration Patterns")]
    public bool useDirectionalVibration = true;
    public float leftVibrationDuration = 0.2f;
    public float rightVibrationDuration = 0.2f;
    public float frontObstacleVibrationDuration = 0.5f;

    // Internal variables
    private float lastDestinationUpdate = 0f;
    private float lastObstacleScan = 0f;
    private float lastOffRouteCheck = 0f;
    private List<Vector3> obstaclesToAvoid = new List<Vector3>();
    private float previousDistanceToDestination = float.MaxValue;
    private bool destinationAnnouncementStarted = false;
    private int lastCountdownIndex = -1;
    private bool isOffRoute = false;

    void Start()
    {
        // Find references if not set
        if (navigationManager == null)
            navigationManager = FindObjectOfType<NavigationManager>();

        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (accessibilityManager == null)
            accessibilityManager = FindObjectOfType<AccessibilityManager>();
    }

    void Update()
    {
        if (navigationManager != null && navigationManager.isNavigating)
        {
            // Update obstacle detection
            if (Time.time - lastObstacleScan > obstacleScanInterval)
            {
                ScanForObstacles();
                lastObstacleScan = Time.time;
            }

            // Update destination information
            if (Time.time - lastDestinationUpdate > destinationUpdateInterval)
            {
                UpdateDestinationInfo();
                lastDestinationUpdate = Time.time;
            }

            // Check if user is off route
            if (Time.time - lastOffRouteCheck > 2.0f)
            {
                CheckIfOffRoute();
                lastOffRouteCheck = Time.time;
            }

            // Check for proximity warnings continuously
            CheckProximityWarnings();
        }
    }

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

    private void ScanForObstacles()
    {
        // Clear previous obstacles
        obstaclesToAvoid.Clear();

        // Current position (ignoring height)
        Vector3 currentPosition = new Vector3(
            Camera.main.transform.position.x,
            0,
            Camera.main.transform.position.z
        );

        // Check all obstacle waypoints
        foreach (var pose in hitPointManager.poseClassList)
        {
            if (pose.waypointType == WaypointType.Obstacle)
            {
                // Get position (ignoring height differences)
                Vector3 obstaclePosition = new Vector3(
                    pose.position.x,
                    0,
                    pose.position.z
                );

                float distanceToObstacle = Vector3.Distance(currentPosition, obstaclePosition);

                // If obstacle is close enough to care about
                if (distanceToObstacle < obstacleWarningThreshold)
                {
                    obstaclesToAvoid.Add(pose.position);

                    // Only warn about very close obstacles
                    if (distanceToObstacle < obstacleHighAlertThreshold)
                    {
                        // Get direction to obstacle
                        Vector3 directionToObstacle = obstaclePosition - currentPosition;
                        directionToObstacle.y = 0;
                        directionToObstacle.Normalize();

                        // Get user's forward vector
                        Vector3 userForward = Camera.main.transform.forward;
                        userForward.y = 0;
                        userForward.Normalize();

                        // Calculate angle between user forward and obstacle
                        float angle = Vector3.SignedAngle(userForward, directionToObstacle, Vector3.up);

                        // Determine if obstacle is in front, left, or right
                        string directionName = GetDirectionName(angle);

                        // Priority warning for obstacles ahead
                        if (Mathf.Abs(angle) < 45f)
                        {
                            // Play warning sound
                            if (navigationManager.audioSource != null && navigationManager.obstacleNearbySound != null)
                            {
                                navigationManager.audioSource.PlayOneShot(navigationManager.obstacleNearbySound);
                            }

                            // Use vibration for directional feedback
                            if (useDirectionalVibration && navigationManager.useVibration)
                            {
                                if (angle < -15f)
                                {
                                    // Obstacle to the left
                                    PerformVibration(1.0f, leftVibrationDuration);
                                }
                                else if (angle > 15f)
                                {
                                    // Obstacle to the right
                                    PerformVibration(1.0f, rightVibrationDuration);
                                }
                                else
                                {
                                    // Obstacle directly ahead
                                    PerformVibration(1.0f, frontObstacleVibrationDuration);
                                }
                            }

                            // Speak warning
                            string intensityWord = distanceToObstacle < 1.0f ? "very close" : "nearby";
                            navigationManager.SpeakMessage("Warning! " + intensityWord + " obstacle " + directionName +
                                                         ", " + distanceToObstacle.ToString("F1") + " meters.");
                        }
                    }
                }
            }
        }
    }

    private void UpdateDestinationInfo()
    {
        // Find end point waypoint
        PoseClass endPoint = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);

        if (endPoint != null)
        {
            // Calculate distance to destination (ignoring height)
            Vector3 currentPosition = new Vector3(
                Camera.main.transform.position.x,
                0,
                Camera.main.transform.position.z
            );

            Vector3 destinationPosition = new Vector3(
                endPoint.position.x,
                0,
                endPoint.position.z
            );

            float distanceToDestination = Vector3.Distance(currentPosition, destinationPosition);

            // Only update if distance has changed significantly
            if (Mathf.Abs(distanceToDestination - previousDistanceToDestination) > 2.0f)
            {
                // Announce distance to destination
                navigationManager.SpeakMessage("Distance to destination: " +
                                              distanceToDestination.ToString("F1") + " meters.");

                previousDistanceToDestination = distanceToDestination;
            }

            // Handle countdown when getting close
            if (distanceToDestination < 5.0f && !destinationAnnouncementStarted)
            {
                navigationManager.SpeakMessage("Approaching destination. " +
                                              distanceToDestination.ToString("F1") + " meters remaining.");
                destinationAnnouncementStarted = true;
            }

            // Countdown sounds for final approach
            if (distanceToDestination < 5.0f && countdownSounds != null && countdownSounds.Length > 0)
            {
                int countdownIndex = Mathf.Clamp(Mathf.FloorToInt(distanceToDestination), 0, countdownSounds.Length - 1);

                // Only play if index changed
                if (countdownIndex != lastCountdownIndex && countdownIndex < countdownSounds.Length)
                {
                    if (navigationManager.audioSource != null && countdownSounds[countdownIndex] != null)
                    {
                        navigationManager.audioSource.PlayOneShot(countdownSounds[countdownIndex]);
                    }

                    lastCountdownIndex = countdownIndex;
                }
            }
        }
    }

    private void CheckProximityWarnings()
    {
        // Get current waypoint
        int currentWaypointIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();

        if (currentWaypointIndex < navigationManager.safePathPlanner.GetPathCount())
        {
            Vector3 nextWaypoint = navigationManager.safePathPlanner.GetNextPathPoint();

            // Calculate distance to next waypoint
            float distanceToWaypoint = Vector3.Distance(
                new Vector3(Camera.main.transform.position.x, 0, Camera.main.transform.position.z),
                new Vector3(nextWaypoint.x, 0, nextWaypoint.z)
            );

            // Play proximity sound when getting close to waypoint
            if (distanceToWaypoint < proximityTriggerDistance && navigationManager.audioSource != null)
            {
                // Scale volume based on proximity
                float volume = Mathf.Lerp(0.3f, 1.0f, 1.0f - (distanceToWaypoint / proximityTriggerDistance));

                if (proximityBeep != null)
                {
                    navigationManager.audioSource.PlayOneShot(proximityBeep, volume);
                }
            }
        }
    }

    private void CheckIfOffRoute()
    {
        // Get current location
        Vector3 currentPosition = Camera.main.transform.position;

        // Get current path segment
        int currentWaypointIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();

        // If we have a valid path
        if (navigationManager.safePathPlanner.GetPathCount() > 0)
        {
            // Get closest point on current path segment
            Vector3 closestPoint = GetClosestPointOnPath(currentPosition);

            // Calculate distance to path
            float distanceToPath = Vector3.Distance(
                new Vector3(currentPosition.x, 0, currentPosition.z),
                new Vector3(closestPoint.x, 0, closestPoint.z)
            );

            // Check if we're too far from the path
            if (distanceToPath > offRouteThreshold)
            {
                // Only notify if this is a new off-route event
                if (!isOffRoute)
                {
                    isOffRoute = true;

                    // Play alert sound
                    if (navigationManager.audioSource != null && offRouteSound != null)
                    {
                        navigationManager.audioSource.PlayOneShot(offRouteSound);
                    }

                    // Vibrate to alert user
                    if (navigationManager.useVibration)
                    {
                        PerformVibration(1.0f, 0.5f);
                    }

                    // Provide guidance to return to path
                    Vector3 directionToPath = closestPoint - currentPosition;
                    directionToPath.y = 0;

                    // Get user's forward vector
                    Vector3 userForward = Camera.main.transform.forward;
                    userForward.y = 0;
                    userForward.Normalize();

                    // Calculate angle between user forward and path
                    float angle = Vector3.SignedAngle(userForward, directionToPath, Vector3.up);

                    // Get direction name
                    string directionName = GetDirectionName(angle);

                    // Speak guidance
                    navigationManager.SpeakMessage("Warning! You are " + distanceToPath.ToString("F1") +
                                                 " meters away from the safe path. Please turn " +
                                                 directionName + " to return to the path.");
                }
            }
            else
            {
                // Back on route
                if (isOffRoute)
                {
                    isOffRoute = false;

                    // Confirm return to path
                    navigationManager.SpeakMessage("Back on the safe path. Continue following guidance.");

                    // Play positive feedback sound
                    if (navigationManager.audioSource != null && pathClearSound != null)
                    {
                        navigationManager.audioSource.PlayOneShot(pathClearSound);
                    }
                }
            }
        }
    }

    private Vector3 GetClosestPointOnPath(Vector3 position)
    {
        // Get current path segment
        int currentIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();
        Vector3 closestPoint = Vector3.zero;
        float minDistance = float.MaxValue;

        // Check current and next few segments
        for (int i = Mathf.Max(0, currentIndex - 1);
             i < Mathf.Min(currentIndex + 3, navigationManager.safePathPlanner.GetPathCount() - 1);
             i++)
        {
            // Get segment start and end
            Vector3 segmentStart = navigationManager.safePathPlanner.GetPathPointAt(i);
            Vector3 segmentEnd = navigationManager.safePathPlanner.GetPathPointAt(i + 1);

            // Find closest point on this segment
            Vector3 pointOnSegment = FindNearestPointOnSegment(position, segmentStart, segmentEnd);

            // Calculate distance
            float distance = Vector3.Distance(position, pointOnSegment);

            // Update if this is closer
            if (distance < minDistance)
            {
                minDistance = distance;
                closestPoint = pointOnSegment;
            }
        }

        return closestPoint;
    }

    private Vector3 FindNearestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        // Get direction vector of the line segment
        Vector3 lineDirection = end - start;
        float lineLength = lineDirection.magnitude;
        lineDirection.Normalize();

        // Calculate vector from segment start to point
        Vector3 pointVector = point - start;

        // Project point onto line segment
        float projection = Vector3.Dot(pointVector, lineDirection);

        // Clamp to segment bounds
        projection = Mathf.Clamp(projection, 0, lineLength);

        // Get the nearest point on segment
        return start + lineDirection * projection;
    }

    private string GetDirectionName(float angle)
    {
        // Translate angle to human-readable direction
        if (Mathf.Abs(angle) < 22.5f)
        {
            return "ahead";
        }
        else if (angle < -157.5f || angle > 157.5f)
        {
            return "behind";
        }
        else if (angle < -112.5f)
        {
            return "behind to your left";
        }
        else if (angle < -67.5f)
        {
            return "to your left";
        }
        else if (angle < -22.5f)
        {
            return "ahead to your left";
        }
        else if (angle < 67.5f)
        {
            return "ahead to your right";
        }
        else if (angle < 112.5f)
        {
            return "to your right";
        }
        else
        {
            return "behind to your right";
        }
    }

    // Method to provide path quality feedback
    public void DescribePathQuality()
    {
        if (navigationManager != null && navigationManager.safePathPlanner != null)
        {
            int pathPoints = hitPointManager.poseClassList.Count(p => p.waypointType == WaypointType.PathPoint);
            int obstacles = hitPointManager.poseClassList.Count(p => p.waypointType == WaypointType.Obstacle);

            string pathDescription = "Current path has " + pathPoints + " waypoints and avoids " +
                                    obstacles + " detected obstacles. ";

            // Describe path complexity
            if (navigationManager.safePathPlanner.GetPathCount() > 10)
            {
                pathDescription += "This is a complex path with multiple turns. Take your time and follow directions carefully.";
            }
            else
            {
                pathDescription += "This is a relatively simple path. Follow the audio guidance to reach your destination.";
            }

            navigationManager.SpeakMessage(pathDescription);
        }
    }

    // Method to describe surrounding environment
    public void DescribeSurroundings()
    {
        // Current position
        Vector3 userPosition = Camera.main.transform.position;

        // Find nearby points of interest (within 5 meters)
        var nearbyPathPoints = hitPointManager.poseClassList
            .Where(p => p.waypointType == WaypointType.PathPoint &&
                   Vector3.Distance(userPosition, p.position) < 5.0f)
            .Count();

        var nearbyObstacles = hitPointManager.poseClassList
            .Where(p => p.waypointType == WaypointType.Obstacle &&
                   Vector3.Distance(userPosition, p.position) < 5.0f)
            .Count();

        string description = "Environmental scan: There are " + nearbyPathPoints +
                            " path points and " + nearbyObstacles + " obstacles within 5 meters of you.";

        // Add path suggestion if user is off-path
        Vector3 nextPathPoint = navigationManager.safePathPlanner.GetNextPathPoint();
        float distanceToPath = Vector3.Distance(userPosition, nextPathPoint);

        if (distanceToPath > offRouteThreshold)
        {
            Vector3 directionToPath = nextPathPoint - userPosition;
            directionToPath.y = 0;

            // Get user's forward vector
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // Calculate angle between user forward and path
            float angle = Vector3.SignedAngle(userForward, directionToPath, Vector3.up);

            // Get direction name
            string directionName = GetDirectionName(angle);

            description += " You are off the safe path. Turn " + directionName +
                          " and proceed " + distanceToPath.ToString("F1") + " meters to return to the path.";
        }
        else
        {
            // Find nearby obstacles in front of user
            bool obstacleAhead = false;

            foreach (var pose in hitPointManager.poseClassList)
            {
                if (pose.waypointType == WaypointType.Obstacle)
                {
                    // Vector from user to obstacle
                    Vector3 directionToObstacle = pose.position - userPosition;
                    directionToObstacle.y = 0;

                    // User's forward direction
                    Vector3 userForward = Camera.main.transform.forward;
                    userForward.y = 0;
                    userForward.Normalize();

                    // Calculate angle
                    float angle = Vector3.Angle(userForward, directionToObstacle);

                    // Check if obstacle is in front within 60 degree cone
                    if (angle < 30f && Vector3.Distance(userPosition, pose.position) < 3f)
                    {
                        obstacleAhead = true;
                        break;
                    }
                }
            }

            if (obstacleAhead)
            {
                description += " Caution: There are obstacles ahead on your path.";
            }
            else
            {
                description += " The path ahead appears clear.";
            }
        }

        navigationManager.SpeakMessage(description);
    }

    // Method to provide emergency help
    public void ProvideEmergencyGuidance()
    {
        // Current position
        Vector3 userPosition = Camera.main.transform.position;

        // Find nearest safe point
        PoseClass nearestSafePoint = hitPointManager.poseClassList
            .Where(p => p.waypointType == WaypointType.PathPoint)
            .OrderBy(p => Vector3.Distance(userPosition, p.position))
            .FirstOrDefault();

        if (nearestSafePoint != null)
        {
            float distanceToSafe = Vector3.Distance(userPosition, nearestSafePoint.position);

            // Direction to safe point
            Vector3 directionToSafe = nearestSafePoint.position - userPosition;
            directionToSafe.y = 0;

            // Get user's forward vector
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // Calculate angle 
            float angle = Vector3.SignedAngle(userForward, directionToSafe, Vector3.up);

            // Get direction name
            string directionName = GetDirectionName(angle);

            // Provide emergency guidance
            string guidance = "Emergency guidance: The nearest safe point is " +
                             directionName + ", " + distanceToSafe.ToString("F1") +
                             " meters away. Proceed carefully in that direction.";

            navigationManager.SpeakMessage(guidance);

            // Vibrate in pulses for emergency
            if (navigationManager.useVibration)
            {
                StartCoroutine(EmergencyVibrationPattern());
            }
        }
        else
        {
            // No safe points found
            navigationManager.SpeakMessage("Emergency guidance: No nearby safe points found. Please remain still and call for assistance.");
        }
    }

    private IEnumerator EmergencyVibrationPattern()
    {
        for (int i = 0; i < 3; i++)
        {
            PerformVibration(1.0f, 0.3f);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Method to announce user's orientation relative to the path
    public void AnnounceOrientation()
    {
        // Current position and orientation
        Vector3 userPosition = Camera.main.transform.position;
        Vector3 userForward = Camera.main.transform.forward;
        userForward.y = 0;
        userForward.Normalize();

        // Get current path segment
        int currentIndex = navigationManager.safePathPlanner.GetCurrentPathIndex();

        if (currentIndex < navigationManager.safePathPlanner.GetPathCount() - 1)
        {
            // Get current path segment direction
            Vector3 pathStart = navigationManager.safePathPlanner.GetPathPointAt(currentIndex);
            Vector3 pathEnd = navigationManager.safePathPlanner.GetPathPointAt(currentIndex + 1);

            Vector3 pathDirection = pathEnd - pathStart;
            pathDirection.y = 0;
            pathDirection.Normalize();

            // Calculate alignment angle
            float alignmentAngle = Vector3.SignedAngle(userForward, pathDirection, Vector3.up);

            // Describe orientation
            string orientation;

            if (Mathf.Abs(alignmentAngle) < 15f)
            {
                orientation = "You are facing directly along the path.";
            }
            else if (Mathf.Abs(alignmentAngle) < 45f)
            {
                orientation = "You are facing slightly " + (alignmentAngle < 0 ? "left" : "right") + " of the path.";
            }
            else if (Mathf.Abs(alignmentAngle) < 135f)
            {
                orientation = "You are facing away from the path toward the " + (alignmentAngle < 0 ? "left" : "right") + ".";
            }
            else
            {
                orientation = "You are facing away from the path.";
            }

            // Get end point to describe overall direction
            PoseClass endPoint = hitPointManager.poseClassList.FirstOrDefault(p => p.waypointType == WaypointType.EndPoint);

            if (endPoint != null)
            {
                // Direction to destination
                Vector3 directionToDestination = endPoint.position - userPosition;
                directionToDestination.y = 0;
                directionToDestination.Normalize();

                // Angle to destination
                float destinationAngle = Vector3.SignedAngle(userForward, directionToDestination, Vector3.up);

                // Add destination info
                string destinationDirection = GetDirectionName(destinationAngle);
                float destinationDistance = Vector3.Distance(userPosition, endPoint.position);

                orientation += " Your destination is " + destinationDirection + ", " +
                              destinationDistance.ToString("F1") + " meters away.";
            }

            navigationManager.SpeakMessage(orientation);
        }
    }
}