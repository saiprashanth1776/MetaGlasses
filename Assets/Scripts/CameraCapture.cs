using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples;

public class CameraCapture : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager webCamTextureManager;
    [SerializeField] private RawImage webCamImage;

    [Header("Input")]
    [SerializeField] private OVRHand hand;                 // ThumbTap source

    [Header("Outer (Capture = grey ring)")]
    [SerializeField] private RectTransform outerRect;      // "Capture"
    [SerializeField] private Graphic outerGraphic;         // Image/RawImage on Capture

    [Header("Inner (Ring = white dot)")]
    [SerializeField] private RectTransform innerRect;      // "Ring" (child)
    [SerializeField] private Graphic innerGraphic;         // Image/RawImage on Ring

    [Header("Press Feel")]
    [SerializeField] private float pressedOuterSize = 96f; // pressed W/H for outer
    [SerializeField] private float innerSizeRatio = 0.46f; // inner size = ratio * outer
    [SerializeField] private float growTime = 0.08f;       // up duration
    [SerializeField] private float holdTime = 0.04f;       // optional small hold
    [SerializeField] private float shrinkTime = 0.12f;     // down duration

    [Header("Tint")]
    [SerializeField] private Color outerPressTint = new Color(0.55f, 0.58f, 0.62f, 1f); // darker grey
    [SerializeField] private Color innerPressTint = new Color(0.80f, 0.80f, 0.80f, 1f); // slightly dim

    [Header("Flash Overlay (full-rect Image above the camera RawImage)")]
    [SerializeField] private Image flashOverlay;           // white overlay image
    [SerializeField] private float flashInTime = 0.05f;    // fade to white
    [SerializeField] private float flashHoldTime = 0.03f;  // keep bright briefly
    [SerializeField] private float flashOutTime = 0.12f;   // fade back to transparent
    [SerializeField] private float flashAlpha = 0.85f;     // peak opacity

    [Header("Digital Zoom (UV crop)")]
    [SerializeField] private float zoomMin = 1.0f;        // 1 = no zoom
    [SerializeField] private float zoomMax = 3.0f;        // 3 = 3x zoom
    [SerializeField] private float zoomStep = 0.15f;      // per swipe
    [SerializeField] private float zoomSmoothTime = 0.12f; // smoothing time

    private float _targetZoom = 1f;
    private float _zoom = 1f;
    private float _zoomVel = 0f;
    private OVRHand.MicrogestureType _lastGesture; // for edge-triggering swipes

    // cached
    private Vector2 outerOrigSize, innerOrigSize;
    private Color outerOrigColor, innerOrigColor;
    private bool init;
    private bool animating;

    private IEnumerator Start()
    {
        // Validate required references
        if (!outerRect || !innerRect)
        {
            Debug.LogWarning("[MicButtonThumbTap] Assign outer/inner Rects.");
            yield break; // don't continue or we'll NRE
        }

        // Cache initial sizes/colors
        outerOrigSize = outerRect.sizeDelta;
        innerOrigSize = innerRect.sizeDelta;
        if (outerGraphic) outerOrigColor = outerGraphic.color;
        if (innerGraphic) innerOrigColor = innerGraphic.color;

        // Ensure flash starts transparent & doesn't block raycasts
        if (flashOverlay)
        {
            var c = flashOverlay.color; c.a = 0f; flashOverlay.color = c;
            flashOverlay.raycastTarget = false;
        }

        init = true;

        // Wait for camera texture then assign
        if (webCamTextureManager)
        {
            while (webCamTextureManager.WebCamTexture == null)
                yield return null;

            if (webCamImage)
            {
                webCamImage.texture = webCamTextureManager.WebCamTexture;
                float aspect = (float)webCamTextureManager.WebCamTexture.width /
                       webCamTextureManager.WebCamTexture.height;

                RectTransform rt = webCamImage.rectTransform;
                float height = rt.sizeDelta.y;
                rt.sizeDelta = new Vector2(height * aspect, height);

            }
        }

        // Digital zoom defaults
        _zoom = _targetZoom = 1f;
        _lastGesture = hand ? hand.GetMicrogestureType() : _lastGesture;
    }

    private void Update()
    {
        if (!init || !hand) return;

        var g = hand.GetMicrogestureType();

        // ---- existing press trigger (unchanged) ----
        if (g == OVRHand.MicrogestureType.ThumbTap)
        {
            if (!animating) StartCoroutine(PressAnim());
        }

        // ---- NEW: edge-trigger zoom gestures ----
        if (g != _lastGesture)
        {
            if (g == OVRHand.MicrogestureType.SwipeForward)   // zoom in
                _targetZoom = Mathf.Clamp(_targetZoom + zoomStep, zoomMin, zoomMax);
            else if (g == OVRHand.MicrogestureType.SwipeBackward) // zoom out
                _targetZoom = Mathf.Clamp(_targetZoom - zoomStep, zoomMin, zoomMax);

            _lastGesture = g;
        }

        // ---- NEW: smooth zoom value ----
        _zoom = Mathf.SmoothDamp(_zoom, _targetZoom, ref _zoomVel, zoomSmoothTime);

        // ---- NEW: apply UV crop to RawImage ----
        if (webCamImage)
            ApplyDigitalZoomUV(_zoom);
    }

    // Center-crop the RawImage by zoom factor (1 = full frame, 2 = 2x zoom)
    private void ApplyDigitalZoomUV(float zoom)
    {
        zoom = Mathf.Max(1f, zoom);
        float w = 1f / zoom;
        float h = 1f / zoom;

        // Center the crop
        float x = (1f - w) * 0.5f;
        float y = (1f - h) * 0.5f;

        webCamImage.uvRect = new Rect(x, y, w, h);
    }

    private IEnumerator PressAnim()
    {
        animating = true;

        Vector2 pressedOuter = new Vector2(pressedOuterSize, pressedOuterSize);
        Vector2 pressedInner = new Vector2(pressedOuterSize * innerSizeRatio, pressedOuterSize * innerSizeRatio);

        // Grow + tint
        yield return Animate(
            outerRect, outerOrigSize, pressedOuter,
            outerGraphic, outerOrigColor, outerPressTint,
            innerRect, innerOrigSize, pressedInner,
            innerGraphic, innerOrigColor, innerPressTint,
            growTime);

        // Flash at peak
        if (flashOverlay) yield return Flash();

        if (holdTime > 0f) yield return new WaitForSeconds(holdTime);

        // Shrink + untint
        yield return Animate(
            outerRect, pressedOuter, outerOrigSize,
            outerGraphic, outerPressTint, outerOrigColor,
            innerRect, pressedInner, innerOrigSize,
            innerGraphic, innerPressTint, innerOrigColor,
            shrinkTime);

        animating = false;
    }

    private static IEnumerator Animate(
        RectTransform oRect, Vector2 oFrom, Vector2 oTo,
        Graphic oGfx, Color ocFrom, Color ocTo,
        RectTransform iRect, Vector2 iFrom, Vector2 iTo,
        Graphic iGfx, Color icFrom, Color icTo,
        float dur)
    {
        if (dur <= 0f)
        {
            if (oRect) oRect.sizeDelta = oTo;
            if (iRect) iRect.sizeDelta = iTo;
            if (oGfx) oGfx.color = ocTo;
            if (iGfx) iGfx.color = icTo;
            yield break;
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            if (oRect) oRect.sizeDelta = Vector2.LerpUnclamped(oFrom, oTo, k);
            if (iRect) iRect.sizeDelta = Vector2.LerpUnclamped(iFrom, iTo, k);
            if (oGfx) oGfx.color = Color.LerpUnclamped(ocFrom, ocTo, k);
            if (iGfx) iGfx.color = Color.LerpUnclamped(icFrom, icTo, k);

            yield return null;
        }

        if (oRect) oRect.sizeDelta = oTo;
        if (iRect) iRect.sizeDelta = iTo;
        if (oGfx) oGfx.color = ocTo;
        if (iGfx) iGfx.color = icTo;
    }

    private IEnumerator Flash()
    {
        if (!flashOverlay) yield break;

        // Fade in
        float t = 0f; var c = flashOverlay.color;
        while (t < flashInTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(0f, flashAlpha, flashInTime <= 0 ? 1f : t / flashInTime);
            flashOverlay.color = c;
            yield return null;
        }
        c.a = flashAlpha; flashOverlay.color = c;

        // Hold
        if (flashHoldTime > 0f) yield return new WaitForSeconds(flashHoldTime);

        // Fade out
        t = 0f;
        while (t < flashOutTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(flashAlpha, 0f, flashOutTime <= 0 ? 1f : t / flashOutTime);
            flashOverlay.color = c;
            yield return null;
        }
        c.a = 0f; flashOverlay.color = c;
    }
}