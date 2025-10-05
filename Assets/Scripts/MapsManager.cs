using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class MapsManager : MonoBehaviour
{
    [Serializable]
    public class Styler
    {
        public string hue;
        public string color;
        public int saturation;
        public int lightness;
        public string visibility;
        public int weight;
    }

    [Serializable]
    public class MapStyleRule
    {
        public string featureType;
        public string elementType;
        public Styler[] stylers;
    }

    [Serializable]
    public class CreateSessionRequest
    {
        public string mapType = "roadmap"; // roadmap | satellite | terrain
        public string language = "en-US";
        public string region = "us";
        public string[] layerTypes; // e.g. ["layerRoadmap"]
        public bool overlay = true;
        public string scale = "scaleFactor1x"; // or scaleFactor2x
        public MapStyleRule[] styles; // optional
    }

    [Header("Marker Settings")]
    [SerializeField] private RectTransform markerPrefab;  // assign a UI Image/Rect in Inspector
    private RectTransform _spawnedMarker;

    [Header("Google Maps Tiles API")]
    [SerializeField] private string apiKey = "PASTE_KEY_HERE";

    [Header("Session Request (serialized → JSON)")]
    [Tooltip("roadmap | satellite | terrain")]
    [SerializeField] private string mapType = "roadmap";
    [SerializeField] private string language = "en-US";
    [SerializeField] private string region = "us";
    [SerializeField] private string[] layerTypes = new string[] { "layerRoadmap" };
    [SerializeField] private bool overlay = true;
    [SerializeField] private string scale = "scaleFactor1x";
    [Tooltip("Optional style rules (leave empty if you don't need styling).")]
    [SerializeField] private MapStyleRule[] styles;

    [Header("View Settings")]
    [Tooltip("Center latitude (degrees)")]
    public double centerLat = 37.4542;
    [Tooltip("Center longitude (degrees)")]
    public double centerLon = -122.1800;
    [Range(0, 22)] public int zoom = 15;

    [Tooltip("Odd number (3,5,7...). Bigger = more coverage (more downloads).")]
    [SerializeField] private int tileGridSize = 3;
    [SerializeField] private int tilePixels = 256; // Google 2D tiles are 256px

    [Header("UI Target")]
    [SerializeField] private RawImage mapTarget; // assign your RawImage
    [Tooltip("If > 0, RawImage height is kept and width adjusted to preserve aspect.")]
    [SerializeField] private float preserveHeightPixels = 512f;

    [Header("Waypoint Direction")]
    [SerializeField] private Transform headsetTransform; // assign your VR camera (CenterEyeAnchor)
    [SerializeField] private float rotationSmoothness = 10f;

    private RectTransform headingRotator;   // <<< add this
    private RectTransform heading;          // keep (not used for rotation anymore, but okay)
    private RectTransform arrow;

    private string _session;
    private DateTime _sessionExpiryUtc = DateTime.MinValue;

    private Texture2D _stitched;
    private int _lastZoomUsed;
    private int _lastGridUsed;

    public event Action<Texture2D> OnMapUpdated;

    private void Start()
    {
        if (!mapTarget)
            Debug.LogWarning("[MapsManager] Assign a RawImage as the mapTarget.");

        StartCoroutine(RefreshMap());
    }

    public void SetCenter(double lat, double lon, int? newZoom = null)
    {
        centerLat = lat;
        centerLon = lon;
        if (newZoom.HasValue) zoom = Mathf.Clamp(newZoom.Value, 0, 22);
        StartCoroutine(RefreshMap());
    }

    public void PanByDegrees(double dLat, double dLon)
    {
        centerLat += dLat;
        centerLon += dLon;
        StartCoroutine(RefreshMap());
    }

    public void SetZoom(int newZoom)
    {
        zoom = Mathf.Clamp(newZoom, 0, 22);
        StartCoroutine(RefreshMap());
    }

    public void NudgeZoom(int delta) => SetZoom(zoom + delta);

    public Vector2 LatLonToLocalPixel(double lat, double lon)
    {
        int N = Mathf.Max(1, tileGridSize | 1);
        int totalW = N * tilePixels;
        int totalH = N * tilePixels;

        double world = 256.0 * Math.Pow(2, zoom);

        double wxC = (centerLon + 180.0) / 360.0 * world;
        double sC = Math.Sin(centerLat * Math.PI / 180.0);
        double wyC = (0.5 - Math.Log((1 + sC) / (1 - sC)) / (4 * Math.PI)) * world;

        double wxP = (lon + 180.0) / 360.0 * world;
        double sP = Math.Sin(lat * Math.PI / 180.0);
        double wyP = (0.5 - Math.Log((1 + sP) / (1 - sP)) / (4 * Math.PI)) * world;

        double dx = wxP - wxC;
        double dy = wyP - wyC;

        float localX = (float)(totalW / 2.0 + dx);
        float localY = (float)(totalH / 2.0 + dy);
        return new Vector2(localX, localY);
    }

    public IEnumerator RefreshMap()
    {
        // 1) Always ensure a valid session (no persistent caching)
        yield return EnsureSession();

        // 2) Tile grid
        int N = Mathf.Max(1, tileGridSize | 1); // force odd
        int half = N / 2;

        int cx, cy;
        LatLonToTileXY(centerLat, centerLon, zoom, out cx, out cy);

        // 3) Prepare stitched texture
        int width = N * tilePixels;
        int height = N * tilePixels;
        if (_stitched == null || _stitched.width != width || _stitched.height != height ||
            _lastZoomUsed != zoom || _lastGridUsed != N)
        {
            if (_stitched != null) Destroy(_stitched);
            _stitched = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _lastZoomUsed = zoom;
            _lastGridUsed = N;
        }

        // 4) Download tiles and stitch
        for (int gy = 0; gy < N; gy++)
        {
            for (int gx = 0; gx < N; gx++)
            {
                int tx = cx + (gx - half);
                int ty = cy + (gy - half);
                string url = "https://tile.googleapis.com/v1/2dtiles/" + zoom + "/" + tx + "/" + ty +
                             "?session=" + _session + "&key=" + apiKey;

                Texture2D tileTexture;
                using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
                {
                    yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                    if (req.result != UnityWebRequest.Result.Success)
#else
                    if (req.isNetworkError || req.isHttpError)
#endif
                    {
                        Debug.LogError("[MapsManager] Tile fail z" + zoom + "/" + tx + "/" + ty + ": " + req.error);
                        yield break;
                    }

                    tileTexture = DownloadHandlerTexture.GetContent(req);
                }

                // Copy into the stitched atlas (flip Y so north is up)
                Color32[] pixels = tileTexture.GetPixels32();
                int px = gx * tilePixels;
                int py = (N - 1 - gy) * tilePixels; // flip row order
                _stitched.SetPixels32(px, py, tilePixels, tilePixels, pixels);
            }
        }
        _stitched.Apply(false, false);

        // 5) Assign to UI
        if (mapTarget)
        {
            mapTarget.texture = _stitched;

            if (preserveHeightPixels > 0f)
            {
                RectTransform rt = mapTarget.rectTransform;
                float aspect = (float)_stitched.width / _stitched.height;
                rt.sizeDelta = new Vector2(preserveHeightPixels * aspect, preserveHeightPixels);
            }

            if (markerPrefab)
            {
                if (_spawnedMarker == null)
                {
                    _spawnedMarker = Instantiate(markerPrefab, mapTarget.rectTransform);
                    _spawnedMarker.gameObject.SetActive(true);

                    // Work in bottom-left anchored space for simple math (unchanged)
                    var mrt = _spawnedMarker;
                    mrt.anchorMin = Vector2.zero;
                    mrt.anchorMax = Vector2.zero;
                    mrt.pivot = Vector2.zero;
                    mrt.localRotation = Quaternion.identity;
                    mrt.localPosition = new Vector3(mrt.localPosition.x, mrt.localPosition.y, 0f);

                    // Keep prefab scale (so Unity’s auto-reparent doesn’t shrink it)
                    mrt.localScale = markerPrefab.localScale;

                    // NEW: fetch rotator + arrow (and optional heading if you still need it)
                    headingRotator = _spawnedMarker.transform.Find("HeadingRotator")?.GetComponent<RectTransform>();
                    heading = _spawnedMarker.transform.Find("HeadingRotator/Heading")?.GetComponent<RectTransform>();
                    arrow = _spawnedMarker.transform.Find("Arrow")?.GetComponent<RectTransform>();

                    // Fallback if headset not assigned
                    if (!headsetTransform) headsetTransform = Camera.main ? Camera.main.transform : null;

                    if (!headingRotator || !arrow)
                        Debug.LogWarning("MapsManager: Couldn't find HeadingRotator and/or Arrow under the marker prefab.");
                }

                // Compute pixel position
                Vector2 localPos = LatLonToLocalPixel(centerLat, centerLon);

                var mapRT = mapTarget.rectTransform;

                // how much the RawImage was scaled compared to the stitched texture
                float sx = mapRT.sizeDelta.x / _stitched.width;
                float sy = mapRT.sizeDelta.y / _stitched.height;

                // convert to UI space (bottom-left origin because of anchor/pivot above)
                Vector2 uiPos = new Vector2(localPos.x * sx, localPos.y * sy);

                // place the marker; keep Z = 0 so it doesn’t hide behind
                _spawnedMarker.anchoredPosition = uiPos;
                _spawnedMarker.localPosition = new Vector3(_spawnedMarker.localPosition.x, _spawnedMarker.localPosition.y, 0f);
            }
        }

        if (OnMapUpdated != null) OnMapUpdated(_stitched);
    }

    private IEnumerator EnsureSession()
    {
        if (!string.IsNullOrEmpty(_session) && DateTime.UtcNow < _sessionExpiryUtc)
            yield break;

        string url = "https://tile.googleapis.com/v1/createSession?key=" + apiKey;
        string body = BuildSessionRequestBody();

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogError("[MapsManager] createSession failed: " + req.error + "\n" + req.downloadHandler.text);
                yield break;
            }

            string json = req.downloadHandler.text;
            _session = ExtractJsonString(json, "session");
            string expiryStr = ExtractJsonString(json, "expiry");

            if (!string.IsNullOrEmpty(expiryStr))
            {
                DateTime dt;
                if (DateTime.TryParse(expiryStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
                    _sessionExpiryUtc = dt.ToUniversalTime();
                else
                    _sessionExpiryUtc = DateTime.UtcNow.AddDays(10);
            }
            else
            {
                _sessionExpiryUtc = DateTime.UtcNow.AddDays(10);
            }

            if (string.IsNullOrEmpty(_session))
                Debug.LogError("[MapsManager] Session token missing in response: " + json);
        }
    }

    private string BuildSessionRequestBody()
    {
        var req = new CreateSessionRequest
        {
            mapType = this.mapType,
            language = this.language,
            region = this.region,
            layerTypes = (this.layerTypes != null && this.layerTypes.Length > 0) ? this.layerTypes : null,
            overlay = this.overlay,
            scale = this.scale,
            styles = (this.styles != null && this.styles.Length > 0) ? this.styles : null
        };
        return JsonUtility.ToJson(req);
    }

    public static void LatLonToTileXY(double lat, double lon, int z, out int x, out int y)
    {
        double latRad = lat * Math.PI / 180.0;
        int n = 1 << z;
        x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        y = (int)Math.Floor((1.0 - Math.Log((Math.Tan(latRad) + 1.0 / Math.Cos(latRad))) / Math.PI) / 2.0 * n);
    }

    private static string ExtractJsonString(string json, string field)
    {
        // Minimal parser for "field":"value"
        string key = "\"" + field + "\":";
        int i = json.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        int start = json.IndexOf('"', i + key.Length);
        if (start < 0) return null;
        int end = json.IndexOf('"', start + 1);
        if (end < 0) return null;
        return json.Substring(start + 1, end - (start + 1));
    }

    private void Update()
    {
        if (!headsetTransform) return;

        // If these are not yet available (e.g., before first spawn), just wait
        if (headingRotator == null && _spawnedMarker != null)
            headingRotator = _spawnedMarker.transform.Find("HeadingRotator")?.GetComponent<RectTransform>();
        if (arrow == null && _spawnedMarker != null)
            arrow = _spawnedMarker.transform.Find("Arrow")?.GetComponent<RectTransform>();

        if (headingRotator == null || arrow == null) return;

        // Head yaw (Y in world) - UI Z rotation (negative to keep “up” = facing forward)
        float yaw = headsetTransform.eulerAngles.y;
        Quaternion target = Quaternion.Euler(0f, 0f, -yaw);

        headingRotator.localRotation = Quaternion.Lerp(
            headingRotator.localRotation, target, Time.deltaTime * rotationSmoothness);

        arrow.localRotation = Quaternion.Lerp(
            arrow.localRotation, target, Time.deltaTime * rotationSmoothness);
    }
}