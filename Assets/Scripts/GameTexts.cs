using TMPro;
using UnityEngine;

// cria os textos do jogo (usado em jogo e pelo menu Piano)
public static class GameTexts
{
    public const string CanvasName = "Game UI";

    public static TMP_Text Score(Transform parent)
    {
        TMP_Text t = Create(parent, "Score", 46, TextAlignmentOptions.TopLeft, "Score: 0");
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(40, -30);
        rt.sizeDelta = new Vector2(700, 170);
        return t;
    }

    public static TMP_Text Message(Transform parent)
    {
        TMP_Text t = Create(parent, "Message", 30, TextAlignmentOptions.Bottom, "");
        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(40, 30);
        rt.offsetMax = new Vector2(-40, 130);
        return t;
    }

    public static TMP_Text Center(Transform parent)
    {
        TMP_Text t = Create(parent, "Center", 110, TextAlignmentOptions.Center, "");
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.62f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(1600, 400);
        t.fontStyle = FontStyles.Bold;
        return t;
    }

    static TMP_Text Create(Transform parent, string name, float size, TextAlignmentOptions alignment, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TMP_Text t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.alignment = alignment;
        t.color = Color.white;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }
}
