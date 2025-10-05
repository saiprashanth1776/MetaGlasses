using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Oculus.Voice;

[Serializable]
public class GoogleTranslateResponse
{
    [Serializable] public class Data { public Translation[] translations; }
    [Serializable] public class Translation { public string translatedText; public string detectedSourceLanguage; }
    public Data data;
}

public class VoiceScript : MonoBehaviour
{
    [Header("Wit / Voice")]
    public AppVoiceExperience voice;
    public OVRHand hand; // the hand you’re using for microgestures

    [Header("UI")]
    public TMP_Text translatedText; // shows translated text
    [SerializeField] private GameObject listeningText;

    [Header("Translation (Google Cloud v2)")]
    [SerializeField] private string googleApiKey = "PASTE_KEY_HERE";
    [SerializeField] private string targetLang = "en"; // always translate to English
    [SerializeField] private string sourceLang = ""; // empty = auto-detect

    [Header("Partial Translation Tuning")]
    [Range(0.1f, 1.5f)] public float partialDebounceSec = 0.5f;
    [Range(0, 50)] public int minNewChars = 12;
    public bool punctuationGate = true;

    [Header("Subtitle Window")]
    [SerializeField] private int windowChars = 144; // keep only the newest N chars

    // internals
    string _currentPartial = "";
    string _lastPartialTranslated = "";
    Coroutine _debounceCo;

    private void BeginNewSession()
    {
        // Clear previous subtitles ONLY when starting a new listen
        if (translatedText) translatedText.text = string.Empty;

        // Reset internal buffers
        _currentPartial = "";
        _lastPartialTranslated = "";

        // Show "listening…" UI
        if (listeningText) listeningText.SetActive(true);

        // Start Wit
        if (!voice.Active) voice.Activate(); // or ActivateImmediately()
    }

    void OnEnable()
    {
        if (!voice) { Debug.LogError("[VoiceTranslate] AppVoiceExperience not assigned."); return; }

        var ve = voice.VoiceEvents;

        // When Wit stops, just clear UI/state
        ve.OnStoppedListening.AddListener(HandleStopped);
        ve.OnStoppedListeningDueToInactivity.AddListener(HandleStopped);
        ve.OnStoppedListeningDueToTimeout.AddListener(HandleStopped);
        ve.OnAborted.AddListener(HandleStopped);

        ve.OnPartialTranscription.AddListener(OnPartial);
        ve.OnFullTranscription.AddListener(OnFull);
        ve.OnError.AddListener((code, msg) => Debug.LogWarning($"[VoiceTranslate] Wit error: {code} {msg}"));
    }

    void OnDisable()
    {
        if (!voice) return;
        var ve = voice.VoiceEvents;

        ve.OnStoppedListening.RemoveListener(HandleStopped);
        ve.OnStoppedListeningDueToInactivity.RemoveListener(HandleStopped);
        ve.OnStoppedListeningDueToTimeout.RemoveListener(HandleStopped);
        ve.OnAborted.RemoveListener(HandleStopped);

        ve.OnPartialTranscription.RemoveListener(OnPartial);
        ve.OnFullTranscription.RemoveListener(OnFull);
    }

    void Update()
    {
        if (!hand || !voice) return;

        // Start listening on ThumbTap
        if (hand.GetMicrogestureType() == OVRHand.MicrogestureType.ThumbTap)
        {
            BeginNewSession();
        }
    }

    private void HandleStopped()
    {
        if (_debounceCo != null) { StopCoroutine(_debounceCo); _debounceCo = null; }

        // Hide "listening" indicator
        if (listeningText) listeningText.SetActive(false);

        _currentPartial = "";
        _lastPartialTranslated = "";
    }

    private void OnPartial(string text)
    {
        _currentPartial = text ?? "";

        if (_debounceCo != null) StopCoroutine(_debounceCo);
        _debounceCo = StartCoroutine(DebouncePartialCo());
    }

    private void OnFull(string text)
    {
        if (_debounceCo != null) { StopCoroutine(_debounceCo); _debounceCo = null; }

        StartCoroutine(Translate(text, translated =>
        {
            SetWindowFromTail(translated);
            _lastPartialTranslated = text ?? "";
        }));
    }

    private IEnumerator DebouncePartialCo()
    {
        yield return new WaitForSeconds(partialDebounceSec);

        var current = _currentPartial ?? "";
        int delta = current.Length - _lastPartialTranslated.Length;
        bool endsWithPunct = punctuationGate && (current.EndsWith(".") || current.EndsWith("?") ||
                                                 current.EndsWith("!") || current.EndsWith(",") ||
                                                 current.EndsWith(":") || current.EndsWith(";"));

        if (delta < minNewChars && !endsWithPunct) yield break;

        yield return Translate(current, translated =>
        {
            SetWindowFromTail(translated);   // provisional
            _lastPartialTranslated = current;
        });
    }

    private void SetWindowFromTail(string s)
    {
        if (translatedText == null) return;
        if (string.IsNullOrEmpty(s)) { translatedText.text = ""; return; }

        translatedText.text = (s.Length <= windowChars)
            ? s
            : s.Substring(s.Length - windowChars, windowChars);
    }

    private IEnumerator Translate(string input, Action<string> onDone)
    {
        if (string.IsNullOrEmpty(input)) { onDone?.Invoke(""); yield break; }
        if (string.IsNullOrEmpty(googleApiKey))
        {
            Debug.LogWarning("[VoiceTranslate] Missing Google API key.");
            onDone?.Invoke(input); // fallback
            yield break;
        }

        if (string.IsNullOrEmpty(targetLang)) targetLang = "en";

        var url = $"https://translation.googleapis.com/language/translate/v2?key={googleApiKey}";
        string json = string.IsNullOrEmpty(sourceLang)
            ? $"{{\"q\":\"{EscapeJson(input)}\",\"target\":\"{targetLang}\",\"format\":\"text\"}}"
            : $"{{\"q\":\"{EscapeJson(input)}\",\"source\":\"{sourceLang}\",\"target\":\"{targetLang}\",\"format\":\"text\"}}";
        var body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=UTF-8");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool hasError = req.result != UnityWebRequest.Result.Success;
#else
            bool hasError = req.isNetworkError || req.isHttpError;
#endif
            if (hasError)
            {
                Debug.LogWarning($"[VoiceTranslate] Translate error {req.responseCode}: {req.error}\n{req.downloadHandler.text}");
                onDone?.Invoke(input);
                yield break;
            }

            var raw = req.downloadHandler.text;
            var resp = JsonUtility.FromJson<GoogleTranslateResponse>(raw);
            var tr = (resp?.data?.translations != null && resp.data.translations.Length > 0)
                        ? resp.data.translations[0] : null;

            var detected = tr?.detectedSourceLanguage;
            var translated = tr != null ? System.Net.WebUtility.HtmlDecode(tr.translatedText) : "";

            if (!string.IsNullOrEmpty(detected) && detected.Equals("en", StringComparison.OrdinalIgnoreCase))
                onDone?.Invoke(input);
            else
                onDone?.Invoke(string.IsNullOrEmpty(translated) ? input :
                               string.Equals(translated, input, StringComparison.Ordinal) ? input : translated);
        }
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}