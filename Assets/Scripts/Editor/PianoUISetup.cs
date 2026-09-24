using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// menu Tools > Piano > Build menu and HUD
// cria na cena o menu inicial (Play / Exit -> Easy / Medium / Hard / Back) com o fundo menu.png
// o HUD do jogo (estrela + score, 3 notas de vida, bichinho do som) e o ecrã do fim (vitória / derrota)
// todos os textos do jogo ficam com a fonte Bangers; pode correr-se outra vez (refaz tudo)
public static class PianoUISetup
{
    const string UiFolder = "Assets/Art/UI";
    const string MissSoundPath = "Assets/Piano/Sounds/error-incorrect.mp3";
    const string TitleFontPath = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials/Bangers SDF.asset";
    const string MenuName = "Menu UI";
    const string HudName = "HUD";
    const string ResultName = "End Screen";
    const string Title = "Living Piano";

    // parte preta do botão (onde fica o texto), medida no button.png
    static readonly Vector2 LabelMin = new Vector2(0.02f, 0.27f);
    static readonly Vector2 LabelMax = new Vector2(0.72f, 0.76f);
    static readonly Vector2 ButtonSize = new Vector2(420f, 147f);

    [MenuItem("Tools/Piano/Build menu and HUD", priority = 25)]
    public static void BuildMenu()
    {
        PianoGame game = Object.FindFirstObjectByType<PianoGame>(FindObjectsInactive.Include);
        if (game == null)
        {
            EditorUtility.DisplayDialog("Piano", "No PianoGame in the scene: run 'Tools > Piano > Do everything' first.", "OK");
            return;
        }
        Build(game);
    }

    public static void Build(PianoGame game)
    {
        ImportSprites();
        Sprite background = LoadSprite("menu");
        Sprite button = LoadSprite("button");
        Sprite star = LoadSprite("star");
        Sprite note = LoadSprite("note");
        Sprite catSound = LoadSprite("cat_sound");
        Sprite catNoSound = LoadSprite("cat_no_sound");
        if (background == null || button == null)
        {
            Debug.LogError($"[Piano] UI images missing in {UiFolder} (menu.png, button.png...).");
            return;
        }
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);

        Undo.RecordObject(game, "Piano UI");
        GameMenu menu = BuildMenuCanvas(game, background, button, catSound, font);
        GameHud hud = BuildHud(game, star, note, catSound, catNoSound, font);
        BuildResultScreen(hud, background, button, star, catSound, catNoSound, font);
        ApplyFont(font, menu.transform, hud.GetComponentInParent<Canvas>().transform);

        game.gameMenu = menu;
        game.hud = hud;
        game.menuPanel = menu.gameObject;
        game.menuSelectionText = null;   // o texto antigo do menu já não existe
        game.missSound = AssetDatabase.LoadAssetAtPath<AudioClip>(MissSoundPath);
        if (game.missSound == null) Debug.LogWarning($"[Piano] miss sound not found at {MissSoundPath}.");

        EnsureEventSystem();

        EditorUtility.SetDirty(game);
        Scene scene = game.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = menu.gameObject;
        Debug.Log("[Piano] menu and HUD built. Press Play.", menu);
    }

    // ------------------------------------------------------------- menu

    static GameMenu BuildMenuCanvas(PianoGame game, Sprite background, Sprite button, Sprite logo, TMP_FontAsset font)
    {
        // o menu antigo (do teu amigo) fica guardado mas desligado
        if (game.menuPanel != null && game.menuPanel.GetComponent<GameMenu>() == null)
        {
            Undo.RecordObject(game.menuPanel, "Piano UI");
            game.menuPanel.SetActive(false);
            Debug.Log($"[Piano] old menu '{game.menuPanel.name}' turned off (not deleted).", game.menuPanel);
        }

        GameObject old = SceneRoots().FirstOrDefault(g => g.name == MenuName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(MenuName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(root, "Piano UI");
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;   // por cima do HUD
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GameMenu menu = root.AddComponent<GameMenu>();
        menu.game = game;

        // fundo em ecrã inteiro
        Image bg = NewImage("Background", root.transform, background);
        Stretch(bg.rectTransform);
        bg.raycastTarget = true;   // não deixa clicar no que está por trás

        // ---- ecrã inicial: bichinho, título, Play, Exit
        RectTransform start = NewPanel("Start", root.transform);
        menu.startScreen = start.gameObject;

        if (logo != null)
        {
            Image l = NewImage("Logo", start, logo);
            l.preserveAspect = true;
            Place(l.rectTransform, new Vector2(0f, 300f), new Vector2(300f, 300f));
        }
        TitleText(start, Title, new Vector2(0f, 90f), font);
        Button play = NewButton("Play", start, button, "PLAY", new Vector2(0f, -110f), font);
        Button exit = NewButton("Exit", start, button, "EXIT", new Vector2(0f, -280f), font);
        UnityEventTools.AddPersistentListener(play.onClick, menu.ShowDifficulty);
        UnityEventTools.AddPersistentListener(exit.onClick, menu.Exit);

        // ---- ecrã de dificuldade: música, Easy, Medium, Hard, Back
        RectTransform diff = NewPanel("Difficulty", root.transform);
        menu.difficultyScreen = diff.gameObject;

        // escolher a música (as músicas do teu amigo): < nome >
        TMP_Text song = NewText("Song", diff, "", 64f, Color.white, font);
        Place(song.rectTransform, new Vector2(0f, 330f), new Vector2(900f, 110f));
        menu.songText = song;
        Button prev = NewButton("Previous Song", diff, button, "<", new Vector2(-560f, 330f), font, 0.55f);
        Button next = NewButton("Next Song", diff, button, ">", new Vector2(560f, 330f), font, 0.55f);
        UnityEventTools.AddPersistentListener(prev.onClick, menu.PreviousSong);
        UnityEventTools.AddPersistentListener(next.onClick, menu.NextSong);

        string[] names = { "EASY", "MEDIUM", "HARD" };
        for (int i = 0; i < names.Length; i++)
        {
            Button b = NewButton(names[i].Substring(0, 1) + names[i].Substring(1).ToLower(), diff, button, names[i],
                                 new Vector2(0f, 160f - i * 165f), font);
            UnityEventTools.AddIntPersistentListener(b.onClick, menu.ChooseDifficulty, i);
        }
        Button back = NewButton("Back", diff, button, "BACK", new Vector2(0f, -360f), font, 0.8f);
        UnityEventTools.AddPersistentListener(back.onClick, menu.ShowStart);

        start.gameObject.SetActive(true);
        diff.gameObject.SetActive(false);
        return menu;
    }

    // ------------------------------------------------------------- HUD

    static GameHud BuildHud(PianoGame game, Sprite star, Sprite note, Sprite catSound, Sprite catNoSound, TMP_FontAsset font)
    {
        Canvas canvas = SceneRoots().Select(g => g.GetComponent<Canvas>()).FirstOrDefault(c => c != null && c.name == GameTexts.CanvasName);
        if (canvas == null && game.scoreText != null) canvas = game.scoreText.canvas;
        if (canvas == null)
        {
            var go = new GameObject(GameTexts.CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Piano UI");
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
        }

        Transform old = canvas.transform.Find(HudName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        // os botões do ecrã do fim precisam disto para serem clicáveis
        if (canvas.GetComponent<GraphicRaycaster>() == null) Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);

        RectTransform hudRoot = NewPanel(HudName, canvas.transform);
        GameHud hud = hudRoot.gameObject.AddComponent<GameHud>();
        hud.game = game;

        // estrela e o número do score logo a seguir
        if (star != null)
        {
            Image s = NewImage("Star", hudRoot, star);
            s.preserveAspect = true;
            TopLeft(s.rectTransform, new Vector2(40f, -30f), new Vector2(90f, 90f));
        }
        if (game.scoreText != null)
        {
            TMP_Text score = game.scoreText;
            Undo.RecordObject(score, "Piano UI");
            Undo.RecordObject(score.rectTransform, "Piano UI");
            TopLeft(score.rectTransform, new Vector2(145f, -30f), new Vector2(500f, 90f));
            score.alignment = TextAlignmentOptions.Left;
            score.fontSize = 72f;
            if (font != null) score.font = font;
            score.text = "0";
        }

        // 3 notas de vida por baixo da estrela (perde-se a da direita primeiro)
        hud.lifeIcons = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            Image n = NewImage($"Life {i + 1}", hudRoot, note);
            n.preserveAspect = true;
            TopLeft(n.rectTransform, new Vector2(40f + i * 80f, -140f), new Vector2(70f, 80f));
            hud.lifeIcons[i] = n;
        }

        // bichinho no canto de cima à direita: com headphones = música a tocar
        Image cat = NewImage("Sound", hudRoot, catNoSound != null ? catNoSound : catSound);
        cat.preserveAspect = true;
        RectTransform rt = cat.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-30f, -20f);
        rt.sizeDelta = new Vector2(210f, 210f);
        hud.soundIcon = cat;
        hud.soundOn = catSound;
        hud.soundOff = catNoSound;
        return hud;
    }

    // ------------------------------------------------------------- ecrã do fim

    // cartão com o fundo do menu por cima do jogo: título, bichinho, score e Retry / Back
    static void BuildResultScreen(GameHud hud, Sprite background, Sprite button, Sprite star, Sprite catWin, Sprite catLose, TMP_FontAsset font)
    {
        Transform canvas = hud.transform.parent;
        Transform old = canvas.Find(ResultName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        RectTransform root = NewPanel(ResultName, canvas);
        root.SetAsLastSibling();   // por cima do HUD e dos textos

        // escurece o jogo por trás e não deixa clicar nele
        Image dim = NewImage("Dim", root, null);
        Stretch(dim.rectTransform);
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        Image card = NewImage("Card", root, background);
        Place(card.rectTransform, Vector2.zero, new Vector2(1280f, 720f));

        TMP_Text shadow = NewText("Title Shadow", card.transform, "YOU WIN!", 140f, new Color(0f, 0f, 0f, 0.85f), font);
        Place(shadow.rectTransform, new Vector2(7f, 243f), new Vector2(1200f, 180f));
        TMP_Text title = NewText("Title", card.transform, "YOU WIN!", 140f, Color.white, font);
        Place(title.rectTransform, new Vector2(0f, 250f), new Vector2(1200f, 180f));
        // a sombra acompanha o texto do título
        var follow = shadow.gameObject.AddComponent<TextShadow>();
        follow.source = title;

        Image cat = NewImage("Cat", card.transform, catWin);
        cat.preserveAspect = true;
        Place(cat.rectTransform, new Vector2(0f, 60f), new Vector2(220f, 220f));

        if (star != null)
        {
            Image s = NewImage("Star", card.transform, star);
            s.preserveAspect = true;
            Place(s.rectTransform, new Vector2(-70f, -95f), new Vector2(85f, 85f));
        }
        TMP_Text score = NewText("Score", card.transform, "0", 90f, Color.white, font);
        score.alignment = TextAlignmentOptions.Left;
        RectTransform srt = score.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.pivot = new Vector2(0f, 0.5f);
        srt.anchoredPosition = new Vector2(-15f, -95f);
        srt.sizeDelta = new Vector2(400f, 110f);

        TMP_Text details = NewText("Details", card.transform, "", 44f, Color.white, font);
        Place(details.rectTransform, new Vector2(0f, -170f), new Vector2(1100f, 70f));

        Button retry = NewButton("Retry", card.transform, button, "RETRY", new Vector2(-230f, -270f), font, 0.85f);
        Button back = NewButton("Back", card.transform, button, "BACK", new Vector2(230f, -270f), font, 0.85f);
        UnityEventTools.AddPersistentListener(retry.onClick, hud.Retry);
        UnityEventTools.AddPersistentListener(back.onClick, hud.BackToMenu);

        hud.resultPanel = root.gameObject;
        hud.resultTitle = title;
        hud.resultScore = score;
        hud.resultDetails = details;
        hud.resultImage = cat;
        hud.winImage = catWin;
        hud.loseImage = catLose;
        root.gameObject.SetActive(false);
    }

    // a fonte do jogo em todos os textos do menu, do HUD e do ecrã do fim
    static void ApplyFont(TMP_FontAsset font, params Transform[] roots)
    {
        if (font == null)
        {
            Debug.LogWarning($"[Piano] font not found at {TitleFontPath}: texts keep their font.");
            return;
        }
        foreach (Transform r in roots)
            foreach (TMP_Text t in r.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == font) continue;
                Undo.RecordObject(t, "Piano UI");
                t.font = font;
                EditorUtility.SetDirty(t);
            }
    }

    // ------------------------------------------------------------- peças

    static RectTransform NewPanel(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Piano UI");
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        Stretch(rt);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Piano UI");
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Piano UI");
        go.transform.SetParent(parent, false);
        TMP_Text t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    // título com uma sombra preta por trás
    static void TitleText(Transform parent, string text, Vector2 pos, TMP_FontAsset font)
    {
        TMP_Text shadow = NewText("Title Shadow", parent, text, 150f, new Color(0f, 0f, 0f, 0.85f), font);
        Place(shadow.rectTransform, pos + new Vector2(7f, -7f), new Vector2(1400f, 200f));
        TMP_Text title = NewText("Title", parent, text, 150f, Color.white, font);
        Place(title.rectTransform, pos, new Vector2(1400f, 200f));
    }

    // botão com o button.png e o texto dentro da parte preta
    static Button NewButton(string name, Transform parent, Sprite sprite, string label, Vector2 pos, TMP_FontAsset font, float scale = 1f)
    {
        Image img = NewImage(name, parent, sprite);
        img.raycastTarget = true;
        img.preserveAspect = true;
        Place(img.rectTransform, pos, ButtonSize * scale);

        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock colors = b.colors;
        colors.highlightedColor = new Color(0.85f, 0.92f, 1f);
        colors.pressedColor = new Color(0.6f, 0.75f, 1f);
        b.colors = colors;

        TMP_Text t = NewText("Label", img.transform, label, 64f * scale, Color.white, font);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = LabelMin;
        rt.anchorMax = LabelMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        t.enableAutoSizing = true;
        t.fontSizeMin = 20f;
        t.fontSizeMax = 64f * scale;
        return b;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // as imagens da pasta UI passam a ser sprites (só mexe se ainda não forem)
    static void ImportSprites()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || (ti.textureType == TextureImporterType.Sprite && ti.spriteImportMode == SpriteImportMode.Single)) continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
    }

    static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{UiFolder}/{name}.png");

    static GameObject[] SceneRoots() => SceneManager.GetActiveScene().GetRootGameObjects();

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Piano UI");
    }
}
