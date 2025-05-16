using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class EnvironmentScanManager : MonoBehaviour
{
    public HitPointManager hitPointManager;
    public ARPlaneManager arPlaneManager;
    public ARPointCloudManager arPointCloudManager;
    public ARRaycastManager arRaycastManager;

    [Header("Scanning Settings")]
    public float scanRadius = 5.0f;
    public float scanDensity = 0.25f; // Distance between scan points
    public float scanInterval = 0.5f; // Time between scans
    public float obstacleDetectionHeight = 1.5f; // Height to check for obstacles
    public int maxObstaclesPerScan = 5; // Limit obstacles per scan to prevent overload

    [Header("Environment Mapping")]
    public GameObject environmentRoot;
    public Material groundMaterial;
    public Material obstacleMaterial;
    public bool generateNavigationMesh = true;
    public bool visualizeEnvironmentMap = true;

    // Internal tracking
    private Dictionary<string, GameObject> detectedPlanes = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> detectedObstacles = new Dictionary<string, GameObject>();
    private float lastScanTime = 0f;
    private Vector3 lastScanPosition;
    private bool isScanning = false;
    private List<ARRaycastHit> raycastHitResults = new List<ARRaycastHit>();

    // Statistics
    private int totalScans = 0;
    private int totalObstaclesDetected = 0;
    private float totalAreaMapped = 0f;

    void Start()
    {
        // Get references if not set
        if (hitPointManager == null)
            hitPointManager = FindObjectOfType<HitPointManager>();

        if (arPlaneManager == null)
            arPlaneManager = FindObjectOfType<ARPlaneManager>();

        if (arPointCloudManager == null)
            arPointCloudManager = FindObjectOfType<ARPointCloudManager>();

        if (arRaycastManager == null)
            arRaycastManager = FindObjectOfType<ARRaycastManager>();

        // Create environment root if not set
        if (environmentRoot == null)
        {
            environmentRoot = new GameObject("Environment Map");
            environmentRoot.transform.SetParent(transform);
        }

        // Create default materials if not set
        if (groundMaterial == null)
        {
            groundMaterial = new Material(Shader.Find("Standard"));
            groundMaterial.color = new Color(0.3f, 0.8f, 0.3f, 0.5f);
        }

        if (obstacleMaterial == null)
        {
            obstacleMaterial = new Material(Shader.Find("Standard"));
            obstacleMaterial.color = new Color(0.8f, 0.2f, 0.2f, 0.7f);
        }

        lastScanPosition = Camera.main.transform.position;
    }

    void Update()
    {
        if (isScanning)
        {
            // Perform environment scanning at intervals
            if (Time.time - lastScanTime > scanInterval)
            {
                lastScanTime = Time.time;

                // Check if we've moved enough to perform a new scan
                Vector3 currentPosition = Camera.main.transform.position;
                if (Vector3.Distance(lastScanPosition, currentPosition) > scanDensity * 0.5f)
                {
                    ScanEnvironment();
                    lastScanPosition = currentPosition;
                }
            }
        }
    }

    public void StartScanning()
    {
        isScanning = true;

        // Ensure AR features are enabled
        if (arPlaneManager != null)
            arPlaneManager.enabled = true;

        if (arPointCloudManager != null)
            arPointCloudManager.enabled = true;

        // Start tracking planes
        StartCoroutine(TrackPlanesCoroutine());
    }

    public void StopScanning()
    {
        isScanning = false;

        // Optionally disable AR features to save performance
        if (arPlaneManager != null)
            arPlaneManager.enabled = false;
    }

    private IEnumerator TrackPlanesCoroutine()
    {
        while (isScanning)
        {
            // Track AR planes
            if (arPlaneManager != null)
            {
                foreach (ARPlane plane in arPlaneManager.trackables)
                {
                    string planeId = plane.trackableId.ToString();

                    // If this is a new plane
                    if (!detectedPlanes.ContainsKey(planeId))
                    {
                        ProcessNewPlane(plane);
                    }
                    else
                    {
                        // Update existing plane if it changed
                        if (plane.transform.hasChanged)
                        {
                            UpdatePlaneRepresentation(plane);
                            plane.transform.hasChanged = false;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void ProcessNewPlane(ARPlane plane)
    {
        string planeId = plane.trackableId.ToString();

        // Create a visual representation of the plane
        GameObject planeObject = new GameObject("Plane_" + planeId);
        planeObject.transform.SetParent(environmentRoot.transform);

        // Create mesh for the plane
        MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();

        // Copy mesh from AR plane
        meshFilter.mesh = plane.GetComponent<MeshFilter>().mesh;
        meshRenderer.material = groundMaterial;

        // Set transform
        planeObject.transform.position = plane.transform.position;
        planeObject.transform.rotation = plane.transform.rotation;
        planeObject.transform.localScale = plane.transform.localScale;

        // Add to tracked planes
        detectedPlanes.Add(planeId, planeObject);

        // Add walkable area to total
        float planeArea = CalculatePlaneArea(plane);
        totalAreaMapped += planeArea;

        // Add waypoints on horizontal planes
        if (plane.alignment == PlaneAlignment.HorizontalUp)
        {
            AddWaypointsOnPlane(plane);
        }

        // Set visualization based on settings
        planeObject.SetActive(visualizeEnvironmentMap);
    }

    private void UpdatePlaneRepresentation(ARPlane plane)
    {
        string planeId = plane.trackableId.ToString();

        if (detectedPlanes.TryGetValue(planeId, out GameObject planeObject))
        {
            // Update mesh
            MeshFilter meshFilter = planeObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                meshFilter.mesh = plane.GetComponent<MeshFilter>().mesh;
            }

            // Update transform
            planeObject.transform.position = plane.transform.position;
            planeObject.transform.rotation = plane.transform.rotation;
            planeObject.transform.localScale = plane.transform.localScale;
        }
    }

    private void ScanEnvironment()
    {
        totalScans++;
        Vector3 scanCenter = Camera.main.transform.position;

        // Scan in a grid pattern around the user
        int obstaclesDetectedThisScan = 0;
        float stepSize = scanDensity;

        for (float x = -scanRadius; x <= scanRadius && obstaclesDetectedThisScan < maxObstaclesPerScan; x += stepSize)
        {
            for (float z = -scanRadius; z <= scanRadius && obstaclesDetectedThisScan < maxObstaclesPerScan; z += stepSize)
            {
                // Skip points too close to the center to avoid detecting the user
                if (Mathf.Abs(x) < 0.5f && Mathf.Abs(z) < 0.5f)
                    continue;

                // Create scan position offset from center
                Vector3 scanPosition = scanCenter + new Vector3(x, 0, z);

                // Raycast downward to find the floor
                if (Physics.Raycast(scanPosition + Vector3.up * 2, Vector3.down, out RaycastHit floorHit, 3f))
                {
                    // Found floor, now check if there's an obstacle at this position
                    Vector3 floorPoint = floorHit.point;

                    // Cast ray upward to detect obstacles
                    if (Physics.Raycast(floorPoint, Vector3.up, out RaycastHit obstacleHit, obstacleDetectionHeight))
                    {
                        // Ignore waypoints and other AR objects
                        if (!obstacleHit.collider.CompareTag("Waypoint") &&
                            !obstacleHit.collider.CompareTag("ARPlane"))
                        {
                            // Check if we already have an obstacle nearby
                            bool obstacleExists = false;
                            foreach (var pose in hitPointManager.poseClassList)
                            {
                                if (pose.waypointType == WaypointType.Obstacle &&
                                    Vector3.Distance(pose.position, obstacleHit.point) < scanDensity * 1.5f)
                                {
                                    obstacleExists = true;
                                    break;
                                }
                            }

                            if (!obstacleExists)
                            {
                                // Create obstacle waypoint
                                CreateObstacleAt(obstacleHit.point, obstacleHit.normal);
                                obstaclesDetectedThisScan++;
                                totalObstaclesDetected++;
                            }
                        }
                    }
                }

                // Also use AR raycasting to find obstacles
                Vector2 screenPoint = Camera.main.WorldToScreenPoint(scanPosition);
                if (arRaycastManager.Raycast(screenPoint, raycastHitResults, TrackableType.AllTypes))
                {
                    foreach (ARRaycastHit hit in raycastHitResults)
                    {
                        // Convert trackable to a real obstacle if it's not a horizontal plane
                        if (hit.trackable is ARPlane plane)
                        {
                            if (plane.alignment != PlaneAlignment.HorizontalUp &&
                                plane.alignment != PlaneAlignment.HorizontalDown)
                            {
                                // Vertical plane - likely an obstacle
                                bool obstacleExists = false;
                                foreach (var pose in hitPointManager.poseClassList)
                                {
                                    if (pose.waypointType == WaypointType.Obstacle &&
                                        Vector3.Distance(pose.position, hit.pose.position) < scanDensity * 1.5f)
                                    {
                                        obstacleExists = true;
                                        break;
                                    }
                                }

                                if (!obstacleExists)
                                {
                                    CreateObstacleAt(hit.pose.position, hit.pose.rotation * Vector3.up);
                                    obstaclesDetectedThisScan++;
                                    totalObstaclesDetected++;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void CreateObstacleAt(Vector3 position, Vector3 normal)
    {
        // Add obstacle to hitPointManager
        string obstacleId = System.Guid.NewGuid().ToString();

        // Calculate approx height using raycasts
        float obstacleHeight = EstimateObstacleHeight(position);

        hitPointManager.poseClassList.Add(new PoseClass
        {
            trackingId = obstacleId,
            position = position,
            rotation = Quaternion.LookRotation(normal),
            waypointType = WaypointType.Obstacle,
            obstacleHeight = obstacleHeight,
            obstacleSeverity = 0.8f // Default severity
        });

        // Create visual representation
        GameObject obstacleObject = new GameObject("Obstacle_" + obstacleId);
        obstacleObject.transform.SetParent(environmentRoot.transform);
        obstacleObject.transform.position = position;
        obstacleObject.transform.rotation = Quaternion.LookRotation(normal);

        // Create a simple marker
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.transform.SetParent(obstacleObject.transform);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);

        // Set material
        Renderer renderer = marker.GetComponent<Renderer>();
        renderer.material = obstacleMaterial;

        // Add to tracked obstacles
        detectedObstacles.Add(obstacleId, obstacleObject);

        // Set visualization based on settings
        obstacleObject.SetActive(visualizeEnvironmentMap);
    }

    private void AddWaypointsOnPlane(ARPlane plane)
    {
        // Add potential path points on the plane
        // Get the plane's boundary
        // FIX: Convert Vector2 boundary points to Vector3
        Vector2[] boundary2D = plane.boundary.ToArray();

        if (boundary2D.Length < 3)
            return;

        // Create a simplified grid of waypoints on the plane
        float gridSize = scanDensity;
        Vector3 planeCenter = plane.center;

        // Determine plane bounds
        Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);

        foreach (Vector2 point in boundary2D)
        {
            // Convert 2D boundary point to 3D world space
            Vector3 localPoint3D = new Vector3(point.x, 0, point.y);
            Vector3 worldPoint = plane.transform.TransformPoint(localPoint3D);

            minBounds.x = Mathf.Min(minBounds.x, worldPoint.x);
            minBounds.y = Mathf.Min(minBounds.y, worldPoint.z);
            maxBounds.x = Mathf.Max(maxBounds.x, worldPoint.x);
            maxBounds.y = Mathf.Max(maxBounds.y, worldPoint.z);
        }

        // Expand bounds slightly to ensure coverage
        minBounds -= new Vector2(gridSize * 0.1f, gridSize * 0.1f);
        maxBounds += new Vector2(gridSize * 0.1f, gridSize * 0.1f);

        // Create grid of potential waypoints
        for (float x = minBounds.x; x <= maxBounds.x; x += gridSize)
        {
            for (float z = minBounds.y; z <= maxBounds.y; z += gridSize)
            {
                Vector3 potentialPoint = new Vector3(x, planeCenter.y, z);

                // Check if point is inside the plane boundary
                if (IsPointInPolygon(potentialPoint, plane))
                {
                    // Check if we already have a waypoint nearby
                    bool waypointExists = false;
                    foreach (var pose in hitPointManager.poseClassList)
                    {
                        if (Vector3.Distance(pose.position, potentialPoint) < gridSize * 0.9f)
                        {
                            waypointExists = true;
                            break;
                        }
                    }

                    if (!waypointExists)
                    {
                        // Add as a safe path point
                        hitPointManager.poseClassList.Add(new PoseClass
                        {
                            trackingId = System.Guid.NewGuid().ToString(),
                            position = potentialPoint,
                            rotation = Quaternion.identity,
                            waypointType = WaypointType.PathPoint
                        });
                    }
                }
            }
        }
    }

    private bool IsPointInPolygon(Vector3 point, ARPlane plane)
    {
        Vector3 localPoint = plane.transform.InverseTransformPoint(point);
        localPoint.y = 0; // Project onto plane

        // Convert to 2D point
        Vector2 point2D = new Vector2(localPoint.x, localPoint.z);

        // Get boundary points in local space
        // FIX: plane.boundary already returns Vector2 array
        Vector2[] boundary2D = plane.boundary.ToArray();

        return IsPointInPolygon2D(point2D, boundary2D);
    }

    private bool IsPointInPolygon2D(Vector2 point, Vector2[] polygon)
    {
        int polygonLength = polygon.Length, i = 0;
        bool inside = false;

        // x, y for tested point
        float pointX = point.x, pointY = point.y;

        // start / end point for the current polygon segment
        float startX, startY, endX, endY;
        Vector2 endPoint = polygon[polygonLength - 1];
        endX = endPoint.x;
        endY = endPoint.y;

        while (i < polygonLength)
        {
            startX = endX; startY = endY;
            endPoint = polygon[i++];
            endX = endPoint.x; endY = endPoint.y;

            // Check if point is inside the polygon
            inside ^= (endY > pointY ^ startY > pointY) &&
                     ((pointX - endX) < (pointY - endY) * (startX - endX) / (startY - endY));
        }

        return inside;
    }

    private float EstimateObstacleHeight(Vector3 position)
    {
        // Cast ray upward to estimate height
        float maxHeight = 2.5f; // Maximum height we care about
        float height = 0.5f; // Default height

        if (Physics.Raycast(position, Vector3.up, out RaycastHit topHit, maxHeight))
        {
            height = topHit.distance;
        }

        return height;
    }

    private float CalculatePlaneArea(ARPlane plane)
    {
        // Calculate approximate area of the plane
        // Note: This is a simplified calculation, not exact for non-convex polygons
        // FIX: plane.boundary returns Vector2[], properly handle it here
        Vector2[] boundaryPoints = plane.boundary.ToArray();

        if (boundaryPoints.Length < 3)
            return 0f;

        // Use shoelace formula to calculate area
        float area = 0f;
        for (int i = 0; i < boundaryPoints.Length; i++)
        {
            Vector2 current = boundaryPoints[i];
            Vector2 next = boundaryPoints[(i + 1) % boundaryPoints.Length];

            // Use the x and y components for the calculation 
            // (x,y of Vector2 corresponds to x,z in 3D space for horizontal planes)
            area += (current.x * next.y - next.x * current.y);
        }

        return Mathf.Abs(area) * 0.5f * plane.transform.lossyScale.x * plane.transform.lossyScale.z;
    }

    public void GenerateNavMesh()
    {
        // This method would generate a Unity NavMesh for the scanned environment
        // For AR applications, you might need a custom solution or NavMeshSurface component
        Debug.Log("GenerateNavMesh not implemented - requires NavMeshSurface component");
    }

    public void ToggleVisualization(bool show)
    {
        visualizeEnvironmentMap = show;

        // Update all environment objects
        foreach (var plane in detectedPlanes.Values)
        {
            plane.SetActive(show);
        }

        foreach (var obstacle in detectedObstacles.Values)
        {
            obstacle.SetActive(show);
        }
    }

    public void ClearEnvironmentMap()
    {
        // Destroy all visualizations
        foreach (var plane in detectedPlanes.Values)
        {
            Destroy(plane);
        }

        foreach (var obstacle in detectedObstacles.Values)
        {
            Destroy(obstacle);
        }

        // Clear dictionaries
        detectedPlanes.Clear();
        detectedObstacles.Clear();

        // Reset statistics
        totalScans = 0;
        totalObstaclesDetected = 0;
        totalAreaMapped = 0f;
    }

    public string GetScanningStats()
    {
        return $"Area mapped: {totalAreaMapped:F1}m² | Obstacles: {totalObstaclesDetected} | Scans: {totalScans}";
    }
}