using System;
using TMPro;
using UnityEngine;

// The fight's time limit: a countdown (2:30, 2:29 ...) at the top of the
// player's view, on the head-locked HUD canvas the breath warning is on. For
// the last seconds it turns red and gives a beat each second. At 0:00 it
// fires TimeUp, and GameFlow ends the fight with TIME'S UP!.
//
// Built from code. GameFlow adds this component and starts it with each
// fight; the rest of the time it is hidden.
public class FightTimer : MonoBehaviour
{
    const float AboveCenter = 190f;  // canvas units (millimetres on the head-locked canvas) above the middle of the view
    const float FontSize = 40f;
    const float HurryFrom = 10f;     // seconds left when it turns red
    const float BeatGrowth = 0.35f;  // how much bigger each of the last seconds starts
    static readonly Color NormalColor = Color.white;
    static readonly Color HurryColor = new Color(1f, 0.3f, 0.25f);

    public event Action TimeUp;

    RectTransform root;
    TextMeshProUGUI text;
    float left;          // seconds
    Func<bool> started;  // the countdown waits for this
    bool running;
    int shown = -1;      // whole seconds on display

    // seconds: the time limit. waitFor: the countdown holds at the full time
    // until this is true (the dragon has landed); null = it starts right away.
    public void Begin(float seconds, Func<bool> waitFor = null)
    {
        if (root == null && !Build()) return;
        left = seconds;
        started = waitFor;
        running = true;
        shown = -1;
        root.gameObject.SetActive(true);
        Show();
    }

    public void Stop()
    {
        running = false;
        if (root != null) root.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!running) return;
        if (started != null)
        {
            if (!started()) return;
            started = null;
        }

        left -= Time.deltaTime;
        if (left <= 0f)
        {
            left = 0f;
            running = false;
            Show();
            TimeUp?.Invoke();
            return;
        }
        Show();
    }

    void Show()
    {
        int seconds = Mathf.CeilToInt(left);
        if (seconds != shown)
        {
            shown = seconds;
            text.text = $"{seconds / 60}:{seconds % 60:00}";
        }

        // The last seconds: red, each one starting big and settling back.
        bool hurry = running && left <= HurryFrom;
        float intoSecond = seconds - left; // 0 when the number has just changed
        float beat = hurry ? 1f + BeatGrowth * (1f - intoSecond) * (1f - intoSecond) : 1f;
        text.color = hurry || left <= 0f ? HurryColor : NormalColor;
        root.localScale = Vector3.one * beat;
    }

    bool Build()
    {
        Camera cam = Camera.main;
        Canvas canvas = cam != null ? cam.GetComponentInChildren<Canvas>() : null;
        if (canvas == null)
        {
            Debug.LogWarning("[FightTimer] no head-locked Canvas under the camera -- the fight has no time limit.");
            return false;
        }

        var go = new GameObject("Fight Timer", typeof(RectTransform));
        root = (RectTransform)go.transform;
        root.SetParent(canvas.rootCanvas.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f); // beats grow from its middle
        root.sizeDelta = new Vector2(200f, 60f);
        root.anchoredPosition = new Vector2(0f, AboveCenter);

        // Drawn after everything else, like the breath warning.
        var own = go.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 100;

        text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = FontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        // Readable over any passthrough: a dark outline and a soft shadow.
        text.outlineWidth = 0.22f;
        text.outlineColor = new Color32(0, 0, 0, 230);
        Material material = text.fontMaterial; // its own copy
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);

        go.SetActive(false);
        return true;
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}
