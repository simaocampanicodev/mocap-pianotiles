using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

// piano tiles no chão: os tiles vêm ter com o jogador e ele tem de estar na tecla certa
// os pés são detetados pelos colliders dos pés (PlayerFeet) nas teclas do chão (FloorKey)
// a pista começa neste objeto e vai para +Z; o jogador está em z = 0
public class PianoGame : MonoBehaviour
{
    // medidas fixas do jogo (mudar aqui, não no Inspector)
    public const float LaneWidth = 0.85f;       // largura de cada lane (a perna esticada chega à do lado)
    public const float KeyWidthFactor = 0.7f;   // largura da tecla que se vê (o collider apanha a lane toda)
    public const float LeadTime = 3f;           // segundos que o tile é visível antes de chegar
    public const float EndZ = -4f;
    public const float TileHeight = 0.2f;       // mais alto para se ver a tecla a afundar
    public const float TileWidthFactor = 0.92f;
    public const float TileLength = 1.2f;
    public const float StartDelay = 3f;
    public const float EarlyWindow = 0.15f;     // já conta um pouco antes de chegar
    public const float LateWindow = 0.3f;       // e um pouco depois
    public const float MinJumpTime = 1.2f;      // tempo mínimo para mandar um tile 2 lanes ao lado
    public const float MinStepTime = 0.6f;      // abaixo disto o tile seguinte só pode estar na lane do lado
    public const int MaxLanes = 5;

    static readonly Color KeyColor = new Color(0.55f, 0.57f, 0.62f);
    static readonly Color KeySteppedColor = new Color(1f, 0.82f, 0.3f);
    static readonly Color KeyHitColor = new Color(0.35f, 0.8f, 1f);

    public const float HoldGrace = 0.3f;        // segundos que o pé pode sair do slide sem o perder
    public const float HoldFinishWindow = 0.15f; // largar mesmo no fim do slide ainda conta

    [Header("Game")]
    [Tooltip("tile speed in meters per second")]
    public float speed = 5f;
    [Tooltip("seconds between tiles")]
    public float spawnInterval = 1.8f;
    [Tooltip("misses allowed before losing")]
    public int lives = 3;
    [Tooltip("tiles to hit to win (0 = never ends)")]
    public int tilesToWin = 20;

    [Header("Slides (hold tiles)")]
    [Tooltip("turn the long notes of the song into slides")]
    public bool slides = true;
    [Tooltip("notes at least this long (seconds) become slides")]
    public float slideMinDuration = 0.8f;
    [Tooltip("longest time the player has to stay on a slide (seconds)")]
    public float slideMaxDuration = 2.5f;

    // tudo isto é ligado pelo menu Piano, mas dá para arrastar à mão
    [Header("Player")]
    public Transform player;
    public PlayerController controller;
    public PlayerFeet playerFeet;

    [Header("Tiles")]
    public GameObject[] tileModels = new GameObject[3];
    public GameObject[] pressedTileModels = new GameObject[3];
    [Tooltip("turn the tile models upside down (use if they show the wrong side up)")]
    public bool flipTileModels = false;
    public Material tileMaterial;
    public Material hitMaterial;
    public Material missMaterial;

    [Header("Effects")]
    [Tooltip("sparks and glow when a tile is hit or a slide is completed")]
    public bool hitEffects = true;
    [Tooltip("particle material (empty = one is made when the game starts)")]
    public Material effectMaterial;

    [Header("Floor keys")]
    public Renderer[] keys = new Renderer[3];
    [Tooltip("number of lanes for easy, medium and hard")]
    public int[] lanesPerDifficulty = { 3, 4, 5 };

    // quantas lanes há agora (muda com a dificuldade)
    public int LaneCount { get; private set; } = 3;

    // centro da lane no eixo X (as lanes ficam sempre centradas na pista)
    public float LaneX(int lane) => (lane - (LaneCount - 1) * 0.5f) * LaneWidth;

    [Header("UI")]
    public TMP_Text scoreText;
    public TMP_Text messageText;
    public TMP_Text centerText;

    private string midiFile;
    [Header("Music")]
    [SerializeField] private AudioSource audioSource;

    public float audioOffset = 0.25f;
    public float minNoteInterval = 0.4f;

    public bool GameOver { get; private set; }
    public bool Won { get; private set; }

    const string Controls = "A S D F G = lanes (left to right)     Q / E = stretch leg     W = jump     R = restart     M = menu     long tiles: stay on them";

    readonly List<PianoTile> activeTiles = new List<PianoTile>();
    readonly float[] keyFlash = new float[MaxLanes];
    GameObject[] templates;
    int score, misses;
    float clock;
    int lastLane = 1, sameLaneCount;
    MaterialPropertyBlock block;
    string feedback;
    Color feedbackColor;
    float feedbackUntil;

    private List<float> noteTimes = new List<float>();
    private List<int> noteLanes = new List<int>();
    private List<float> noteHolds = new List<float>();   // 0 = tile normal, > 0 = slide (segundos)
    private int currentNoteIndex = 0;
    private bool musicStarted = false;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    [System.Serializable]
    public struct SongData
    {
        public string songName;
        public string midiFileName;
        public AudioClip audioClip;
        public float audioOffset;
    }

    [Header("Lista de Músicas e Menu")]
    public GameObject menuPanel;
    public TMP_Text menuSelectionText;
    public List<SongData> songs = new List<SongData>();
    private int selectedSongIndex = 0;
    private int difficulty = 1;
    private bool inMenu = true;

    [Header("Menu and HUD (Tools > Piano > Build menu and HUD)")]
    public GameMenu gameMenu;
    public GameHud hud;

    [Header("Sounds")]
    [Tooltip("sound played on a miss")]
    public AudioClip missSound;
    [Range(0f, 1f)] public float missVolume = 0.8f;
    AudioSource sfx;   // separado da música para não a parar

    // lido pelo menu e pelo HUD
    public int Score => score;
    public int Misses => misses;
    public int TotalNotes => noteTimes.Count;
    public bool InMenu => inMenu;
    public bool Finished => GameOver || Won;
    public bool MusicPlaying => audioSource != null && audioSource.isPlaying;
    public int SelectedSongIndex => selectedSongIndex;
    public int SongCount => songs != null ? songs.Count : 0;
    public string SongName(int i) => i >= 0 && i < SongCount ? songs[i].songName : "";

    void Start()
    {
        if (controller == null) controller = FindFirstObjectByType<PlayerController>();
        if (player == null && controller != null) player = controller.transform;
        if (playerFeet == null && player != null) playerFeet = player.GetComponent<PlayerFeet>();
        if (playerFeet == null) Debug.LogError("[Piano] no character with foot colliders. Run Tools > Piano > Do everything.", this);
        if (GetComponentsInChildren<FloorKey>(true).Length == 0)
            Debug.LogError("[Piano] no floor key colliders (FloorKey): tiles can't be hit. Run Tools > Piano > Do everything.", this);

        if (tileMaterial == null) tileMaterial = NewMaterial(new Color(0.05f, 0.05f, 0.07f), Color.black);
        if (hitMaterial == null) hitMaterial = NewMaterial(new Color(0.35f, 0.8f, 1f), new Color(0.15f, 0.45f, 0.8f));
        if (missMaterial == null) missMaterial = NewMaterial(new Color(0.9f, 0.15f, 0.15f), new Color(0.5f, 0.02f, 0.02f));

        CreateTemplates();
        SetLaneCount(LanesFor(difficulty));
        if (scoreText == null || messageText == null || centerText == null) CreateUI();

        Restart();
        OpenMenu();
    }

    void LerMidi()
    {
        noteTimes.Clear();
        noteLanes.Clear();
        noteHolds.Clear();

        if (string.IsNullOrEmpty(midiFile))
        {
            Debug.LogError("[Piano] ERRO: Nenhum ficheiro MIDI especificado na música selecionada!");
            return;
        }

        string path = Path.Combine(Application.streamingAssetsPath, midiFile);

        if (!File.Exists(path))
        {
            Debug.LogError($"[Piano] ERRO: Ficheiro MIDI não encontrado em: {path}. Verifica a pasta StreamingAssets e o nome do ficheiro!");
            return;
        }

        MidiFile midi = MidiFile.Read(path);
        TempoMap tempoMap = midi.GetTempoMap();

        var notes = midi.GetNotes().OrderBy(n => n.Time);

        float lastEnd = 0f;   // fim da última nota aceite (depois do slide, se for slide)
        int previousLane = -1;
        foreach (Note note in notes)
        {
            float time = ((float)note.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000000f) + audioOffset;

            if (noteTimes.Count > 0 && (time - lastEnd) < minNoteInterval)
                continue;

            // nota longa = slide (fica em cima enquanto a nota toca)
            float duration = (float)note.LengthAs<MetricTimeSpan>(tempoMap).TotalMicroseconds / 1000000f;
            float hold = slides && duration >= slideMinDuration ? Mathf.Min(duration, slideMaxDuration) : 0f;

            // lane pela nota; se houver pouco tempo, não manda o jogador para muito longe
            int lane = note.NoteNumber % LaneCount;
            if (previousLane >= 0)
            {
                float gap = time - lastEnd;
                int maxStep = gap < MinStepTime ? 1 : gap < MinJumpTime ? 2 : LaneCount;
                lane = Mathf.Clamp(lane, previousLane - maxStep, previousLane + maxStep);
            }

            noteTimes.Add(time);
            noteLanes.Add(lane);
            noteHolds.Add(hold);
            lastEnd = time + hold;
            previousLane = lane;
        }

        tilesToWin = noteTimes.Count;
        Debug.Log($"[Piano] MIDI carregado com sucesso! Total de notas a cair: {noteTimes.Count} (slides: {noteHolds.Count(h => h > 0f)})");
    }

    void Update()
    {
        Keyboard k = Keyboard.current;
        if (k != null && k.rKey.wasPressedThisFrame && !inMenu) Restart();


        if (k != null && (k.mKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
        {
            OpenMenu();
        }

        if (inMenu) return;

        if (!GameOver && !Won)
        {
            if (clock < 0f)
            {
                clock += Time.deltaTime;
                if (clock >= 0f && !musicStarted)
                {
                    musicStarted = true;
                    if (audioSource != null) audioSource.Play();
                }
            }
            else
            {
                if (audioSource != null && audioSource.isPlaying)
                    clock = audioSource.time;
                else
                    clock += Time.deltaTime;
            }

            while (currentNoteIndex < noteTimes.Count && noteTimes[currentNoteIndex] - clock <= LeadTime)
            {
                SpawnTile(noteLanes[currentNoteIndex], noteTimes[currentNoteIndex], noteHolds[currentNoteIndex]);
                currentNoteIndex++;
            }
        }

        UpdateTiles();
        CheckSongFinished();
        UpdateKeys();
        UpdateUI();
    }

    // ganha quando todas as teclas da música já passaram (mesmo com algumas falhas)
    void CheckSongFinished()
    {
        if (inMenu || GameOver || Won || noteTimes.Count == 0 || currentNoteIndex < noteTimes.Count) return;
        foreach (PianoTile t in activeTiles)
            if (t != null && (t.state == PianoTile.State.Incoming || t.state == PianoTile.State.Holding)) return;
        Won = true;
    }

    public void StartSelectedGame()
    {
        if (difficulty == 0) { speed = 3.0f; minNoteInterval = 0.70f; lives = 3; }
        else if (difficulty == 1) { speed = 4.5f; minNoteInterval = 0.45f; lives = 3; }
        else if (difficulty == 2) { speed = 6.5f; minNoteInterval = 0.25f; lives = 3; }
        SetLaneCount(LanesFor(difficulty));

        if (songs.Count > 0 && selectedSongIndex < songs.Count)
        {
            SongData s = songs[selectedSongIndex];
            midiFile = s.midiFileName;
            audioOffset = s.audioOffset;
            if (audioSource != null && s.audioClip != null)
                audioSource.clip = s.audioClip;
        }

        inMenu = false;
        if (menuPanel != null) menuPanel.SetActive(false);
        LerMidi();
        Restart();
    }

    int LanesFor(int diff)
    {
        int n = lanesPerDifficulty != null && diff >= 0 && diff < lanesPerDifficulty.Length ? lanesPerDifficulty[diff] : 3;
        return Mathf.Clamp(n, 1, MaxLanes);
    }

    // ------------------------------------------------------------- lanes

    // muda o número de lanes: mostra/esconde as lanes e teclas do chão e centra tudo
    // as lanes a mais são cópias da lane 1 (mesmo material e collider)
    public void SetLaneCount(int count)
    {
        LaneCount = Mathf.Clamp(count, 1, MaxLanes);

        Transform floor = transform.Find("Floor");
        if (floor == null)
        {
            Debug.LogWarning("[Piano] no 'Floor' under the game: run Tools > Piano > Do everything.", this);
            return;
        }

        var newKeys = new Renderer[LaneCount];
        for (int i = 0; i < MaxLanes; i++)
        {
            bool on = i < LaneCount;
            Transform lane = LanePart(floor, "Lane", i, on);
            Transform key = LanePart(floor, "Key", i, on);
            Transform sensor = LanePart(floor, "Key Sensor", i, on);
            float x = on ? LaneX(i) : 0f;

            if (lane != null) { lane.gameObject.SetActive(on); SetX(lane, x); }
            if (key != null) { key.gameObject.SetActive(on); SetX(key, x); if (on) newKeys[i] = key.GetComponent<Renderer>(); }
            if (sensor != null)
            {
                sensor.gameObject.SetActive(on);
                SetX(sensor, x);
                FloorKey fk = sensor.GetComponent<FloorKey>();
                if (fk != null) fk.lane = i;
            }
        }
        keys = newKeys;

        // chão e linha de chegada com a largura das lanes todas
        Transform floorBase = floor.Find("Base");
        if (floorBase != null) floorBase.localScale = new Vector3(LaneCount * LaneWidth + 0.4f, floorBase.localScale.y, floorBase.localScale.z);
        Transform line = floor.Find("Line");
        if (line != null) line.localScale = new Vector3(LaneCount * LaneWidth, line.localScale.y, line.localScale.z);
    }

    // "Lane 3", "Key 3", "Key Sensor 3"...; se não existir e for precisa, copia a nº 1
    static Transform LanePart(Transform floor, string prefix, int index, bool create)
    {
        Transform t = floor.Find($"{prefix} {index + 1}");
        if (t != null || !create) return t;
        Transform first = floor.Find($"{prefix} 1");
        if (first == null) return null;
        t = Instantiate(first.gameObject, floor).transform;
        t.name = $"{prefix} {index + 1}";
        t.localPosition = first.localPosition;
        t.localRotation = first.localRotation;
        t.localScale = first.localScale;
        return t;
    }

    static void SetX(Transform t, float x) => t.localPosition = new Vector3(x, t.localPosition.y, t.localPosition.z);

    public void Restart()
    {
        foreach (PianoTile t in activeTiles)
            if (t != null) Destroy(t.gameObject);
        activeTiles.Clear();

        score = misses = 0;
        GameOver = Won = false;
        feedback = null;
        clock = -StartDelay;

        currentNoteIndex = 0;
        musicStarted = false;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null) audioSource.Stop();

        lastLane = 1;
        sameLaneCount = 0;

        if (controller != null) controller.ResetPlayer();
    }

    // ------------------------------------------------------------- tiles

    // lane ao acaso, sem repetir demais e sem saltos de 2 teclas quando não há tempo
    int PickLane()
    {
        var options = new List<int>();
        for (int l = 0; l < LaneCount; l++) options.Add(l);
        if (spawnInterval < MinJumpTime) options.RemoveAll(l => Mathf.Abs(l - lastLane) == 2);
        if (sameLaneCount >= 2) options.Remove(lastLane);
        if (options.Count == 0) options.Add(1);

        int lane = options[Random.Range(0, options.Count)];
        sameLaneCount = lane == lastLane ? sameLaneCount + 1 : 0;
        lastLane = lane;
        return lane;
    }

    void SpawnTile(int lane, float arrivalTime, float holdDuration)
    {
        // só há 3 modelos de tecla: as lanes a mais repetem-nos
        GameObject go = Instantiate(templates[lane % templates.Length], transform);
        go.SetActive(true);

        PianoTile tile = go.GetComponent<PianoTile>();
        tile.lane = lane;
        tile.arrivalTime = arrivalTime;
        tile.holdDuration = holdDuration;
        // o slide acaba quando a parte de trás chega ao jogador
        tile.length = tile.IsSlide ? Mathf.Max(TileLength, holdDuration * speed) : TileLength;
        tile.state = PianoTile.State.Incoming;

        go.name = tile.IsSlide ? $"Slide {lane + 1}" : $"Tile {lane + 1}";
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(LaneWidth * TileWidthFactor, TileHeight, tile.length);
        if (tile.IsSlide)
        {
            tile.PaintProgress(hitMaterial);
            tile.SetProgress(0f);
        }

        PlaceTile(tile);
        activeTiles.Add(tile);
    }

    // a frente do tile chega ao jogador (z = 0) no tempo marcado
    void PlaceTile(PianoTile t) =>
        t.transform.localPosition = new Vector3(LaneX(t.lane), 0f, (t.arrivalTime - clock) * speed);

    void UpdateTiles()
    {
        for (int i = activeTiles.Count - 1; i >= 0; i--)
        {
            PianoTile t = activeTiles[i];
            if (t == null) { activeTiles.RemoveAt(i); continue; }

            PlaceTile(t);

            if (!GameOver && !Won)
            {
                bool onLane = playerFeet != null && playerFeet.IsOnLane(t.lane);
                if (t.state == PianoTile.State.Incoming)
                {
                    if (clock >= t.arrivalTime - EarlyWindow && onLane)
                    {
                        if (t.IsSlide) StartHold(t);
                        else Hit(t);
                    }
                    else if (clock > t.arrivalTime + LateWindow) Miss(t);
                }
                else if (t.state == PianoTile.State.Holding) UpdateHold(t, onLane);
            }

            if (t.transform.localPosition.z + t.length < EndZ)
            {
                activeTiles.RemoveAt(i);
                Destroy(t.gameObject);
            }
        }
    }

    void Hit(PianoTile t)
    {
        t.state = PianoTile.State.Hit;
        t.Press(hitMaterial);
        keyFlash[t.lane] = 1f;

        score++;
        ShowFeedback(t.IsSlide ? "SLIDE!" : "HIT", new Color(0.4f, 0.9f, 1f));
        PlayHitEffect(t);
        if (tilesToWin > 0 && score >= tilesToWin) Won = true;
    }

    // ------------------------------------------------------------- slides

    void StartHold(PianoTile t)
    {
        t.state = PianoTile.State.Holding;
        t.lastOnLaneTime = clock;
        ShowFeedback("HOLD", new Color(0.4f, 0.9f, 1f));
    }

    // fica em cima: a cor vai enchendo; sai durante mais de HoldGrace: perde o slide
    void UpdateHold(PianoTile t, bool onLane)
    {
        float end = t.arrivalTime + t.holdDuration;
        float progress = (clock - t.arrivalTime) / t.holdDuration;
        t.SetProgress(progress);
        keyFlash[t.lane] = Mathf.Max(keyFlash[t.lane], 0.6f);

        if (onLane) t.lastOnLaneTime = clock;

        if (clock >= end)
        {
            Hit(t);
            return;
        }
        if (clock - t.lastOnLaneTime > HoldGrace)
        {
            // largou mesmo no fim: ainda conta
            if (t.lastOnLaneTime >= end - HoldFinishWindow) Hit(t);
            else Miss(t);
        }
    }

    void Miss(PianoTile t)
    {
        t.state = PianoTile.State.Missed;
        t.Paint(missMaterial);
        t.PaintProgress(missMaterial);
        misses++;
        ShowFeedback("MISS", new Color(1f, 0.3f, 0.3f));
        PlayMissSound();
        if (misses >= lives)
        {
            GameOver = true;
            if (audioSource != null) audioSource.Stop();   // perdeu: a música pára
        }
    }

    void PlayMissSound()
    {
        if (missSound == null) return;
        if (sfx == null)
        {
            // fonte de som própria (num filho) para não mexer na da música
            var go = new GameObject("SFX");
            go.transform.SetParent(transform, false);
            sfx = go.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
        }
        sfx.PlayOneShot(missSound, missVolume);
    }

    // faíscas na tecla, junto ao jogador; o slide completo dá um efeito maior
    void PlayHitEffect(PianoTile t)
    {
        if (!hitEffects) return;
        Vector3 position = transform.TransformPoint(new Vector3(LaneX(t.lane), TileHeight, 0.3f));
        Color color = hitMaterial != null && hitMaterial.HasProperty(BaseColorId) ? hitMaterial.GetColor(BaseColorId) : KeyHitColor;
        HitEffects.Play(position, color, t.IsSlide ? 1.8f : 1f, effectMaterial);
    }

    void ShowFeedback(string text, Color color)
    {
        feedback = text;
        feedbackColor = color;
        feedbackUntil = Time.time + 0.5f;
    }

    // ------------------------------------------------------------- teclas do chão

    void UpdateKeys()
    {
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < LaneCount; i++)
        {
            keyFlash[i] = Mathf.MoveTowards(keyFlash[i], 0f, Time.deltaTime * 3f);
            if (keys == null || i >= keys.Length || keys[i] == null) continue;

            bool stepped = playerFeet != null && playerFeet.IsOnLane(i);
            Color color = Color.Lerp(stepped ? KeySteppedColor : KeyColor, KeyHitColor, keyFlash[i]);
            Color emission = Color.Lerp(stepped ? KeySteppedColor * 0.3f : Color.black, KeyHitColor * 0.8f, keyFlash[i]);

            keys[i].GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            block.SetColor(EmissionId, emission);
            keys[i].SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------- moldes dos tiles

    // cada molde fica 1 x 1 x 1 (largura X, altura Y, comprimento Z) com a frente em z = 0
    void CreateTemplates()
    {
        var folder = new GameObject("Tile Templates").transform;
        folder.SetParent(transform, false);
        folder.gameObject.SetActive(false);

        templates = new GameObject[3];
        for (int lane = 0; lane < 3; lane++)
        {
            var root = new GameObject("Tile " + (lane + 1));
            root.transform.SetParent(folder, false);
            Transform fit = new GameObject("fit").transform;
            fit.SetParent(root.transform, false);
            Transform turn = new GameObject("turn").transform;
            turn.SetParent(fit, false);

            GameObject normal = Model(lane < tileModels.Length ? tileModels[lane] : null, turn, "normal");
            GameObject pressed = lane < pressedTileModels.Length && pressedTileModels[lane] != null
                ? Model(pressedTileModels[lane], turn, "pressed") : null;

            // o lado comprido do modelo fica ao longo da pista
            Bounds b = LocalBounds(root.transform, normal);
            Quaternion yaw = b.size.x > b.size.z * 1.05f ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            // virar ao contrário (de cima para baixo) sem trocar esquerda e direita
            Quaternion flip = flipTileModels ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
            turn.localRotation = flip * yaw;
            b = LocalBounds(root.transform, normal);
            var scale = new Vector3(1f / Mathf.Max(b.size.x, 1e-4f), 1f / Mathf.Max(b.size.y, 1e-4f), 1f / Mathf.Max(b.size.z, 1e-4f));
            fit.localScale = scale;
            fit.localPosition = new Vector3(-b.center.x * scale.x, -b.min.y * scale.y, -b.min.z * scale.z);

            // a tecla afundada é mais baixa: assenta-a no chão (senão fica a flutuar)
            float pressedHeight = 0.6f;
            if (pressed != null)
            {
                Bounds pb = LocalBounds(root.transform, pressed);
                pressed.transform.position += root.transform.TransformVector(new Vector3(0f, -pb.min.y, 0f));
                pressedHeight = Mathf.Clamp(pb.size.y, 0.1f, 1f);   // a tecla normal tem altura 1
                pressed.SetActive(false);
            }

            PianoTile tile = root.AddComponent<PianoTile>();
            tile.pressedHeight = pressedHeight;
            tile.normal = normal;
            tile.pressed = pressed;
            tile.fill = CreateFill(root.transform, fit);
            tile.Paint(tileMaterial);
            templates[lane] = root;
        }
    }

    // cópia da tecla por cima da original que vai ganhando a cor de acerto (só nos slides)
    static Transform CreateFill(Transform root, Transform fit)
    {
        var fill = new GameObject("fill").transform;
        fill.SetParent(root, false);
        // um pouco maior que a tecla preta para a tapar sem piscar
        fill.localPosition = new Vector3(0f, 0f, -0.002f);
        fill.localScale = new Vector3(1.03f, 1.04f, 1f);

        GameObject copy = Instantiate(fit.gameObject, fill, false);
        copy.name = "fit";
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            if (t.name == "pressed") Destroy(t.gameObject);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        fill.gameObject.SetActive(false);
        return fill;
    }

    static GameObject Model(GameObject prefab, Transform parent, string name)
    {
        GameObject go;
        if (prefab != null) go = Instantiate(prefab, parent);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(1f, 0.25f, 3f);
        }
        go.name = name;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        return go;
    }

    // limites da malha no espaço do root (funciona com o objeto desligado)
    static Bounds LocalBounds(Transform root, GameObject obj)
    {
        bool any = false;
        var b = new Bounds();
        void Add(Transform t, Mesh m)
        {
            if (m == null) return;
            Bounds mb = m.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = root.InverseTransformPoint(t.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        foreach (MeshFilter mf in obj.GetComponentsInChildren<MeshFilter>(true)) Add(mf.transform, mf.sharedMesh);
        foreach (SkinnedMeshRenderer s in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(s.transform, s.sharedMesh);
        return any ? b : new Bounds(Vector3.zero, Vector3.one);
    }

    static Material baseMaterial;

    static Material NewMaterial(Color color, Color emission)
    {
        if (baseMaterial == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = tmp.GetComponent<Renderer>().sharedMaterial;
            Destroy(tmp);
        }
        var m = new Material(baseMaterial);
        if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, color);
        if (m.HasProperty(ColorId)) m.SetColor(ColorId, color);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionId, emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    // ------------------------------------------------------------- UI

    string lastScore, lastMessage, lastCenter;

    void UpdateUI()
    {
        // com o HUD novo o score é só o número (a estrela está ao lado e as vidas são as notas)
        SetText(scoreText, ref lastScore, hud != null ? score.ToString() : $"Score: {score}\nMisses: {misses}/{lives}");
        bool resultScreen = hud != null && hud.resultPanel != null;   // o ecrã do fim mostra isto
        SetText(messageText, ref lastMessage, GameOver || Won ? (resultScreen ? "" : "R = play again     M = menu") : Controls);

        string center = "";
        Color centerColor = Color.white;
        if ((GameOver || Won) && resultScreen) center = "";
        else if (GameOver)
        {
            center = $"GAME OVER\n<size=50%>{score} tiles hit</size>";
            centerColor = new Color(1f, 0.3f, 0.3f);
        }
        else if (Won)
        {
            center = $"YOU WIN!\n<size=50%>{score} tiles   {misses} misses</size>";
            centerColor = new Color(0.4f, 1f, 0.6f);
        }
        else if (clock < 0f) center = clock > -1f ? "1" : clock > -2f ? "2" : "3";
        else if (feedback != null && Time.time < feedbackUntil)
        {
            center = $"<size=60%>{feedback}</size>";
            centerColor = feedbackColor;
        }
        SetText(centerText, ref lastCenter, center);
        if (centerText != null) centerText.color = centerColor;
    }

    static void SetText(TMP_Text t, ref string last, string value)
    {
        if (t == null || value == last) return;
        last = value;
        t.text = value;
    }

    void CreateUI()
    {
        Canvas canvas = null;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.name == GameTexts.CanvasName) canvas = c;
        if (canvas == null)
        {
            var go = new GameObject(GameTexts.CanvasName, typeof(Canvas), typeof(CanvasScaler));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        if (scoreText == null) scoreText = GameTexts.Score(canvas.transform);
        if (messageText == null) messageText = GameTexts.Message(canvas.transform);
        if (centerText == null) centerText = GameTexts.Center(canvas.transform);

        if (scoreText.font == null)
            Debug.LogError("[Piano] TextMeshPro resources missing: Window > TextMeshPro > Import TMP Essential Resources.", this);
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.green;
        for (int i = 0; i <= LaneCount; i++)
        {
            float x = (i - LaneCount * 0.5f) * LaneWidth;
            Gizmos.DrawLine(new Vector3(x, 0f, EndZ), new Vector3(x, 0f, LeadTime * speed));
        }
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(-0.5f * LaneCount * LaneWidth, 0.01f, 0f), new Vector3(0.5f * LaneCount * LaneWidth, 0.01f, 0f));
    }

    // ------------------------------------------------------------- Menu de Músicas

    public void SelectSong(int index)
    {
        selectedSongIndex = index;
        Debug.Log($"Música selecionada: {index}");
        UpdateMenuUI();
    }

    public void SetDifficulty(int diff)
    {
        difficulty = diff;
        // mostra logo no chão quantas lanes vai ter
        SetLaneCount(LanesFor(difficulty));
        if (controller != null) controller.ResetPlayer();
        Debug.Log($"Dificuldade selecionada: {diff}");
        UpdateMenuUI();
    }

    public void OpenMenu()
    {
        inMenu = true;
        if (audioSource != null) audioSource.Stop();

        // Limpa as teclas que estavam na pista
        foreach (PianoTile t in activeTiles)
            if (t != null) Destroy(t.gameObject);
        activeTiles.Clear();

        // Limpa os avisos do meio do ecrã
        if (centerText != null) centerText.text = "";

        // Ativa o painel do Canvas e atualiza os textos
        if (menuPanel != null) menuPanel.SetActive(true);
        if (gameMenu != null) gameMenu.ShowStart();   // volta sempre ao ecrã Play / Exit
        UpdateMenuUI();
    }

    void UpdateMenuUI()
    {
        if (menuSelectionText == null) return;

        string nomeMusica = (songs.Count > 0 && selectedSongIndex < songs.Count)
            ? songs[selectedSongIndex].songName
            : "Nenhuma música";

        string nomeDificuldade = difficulty == 0 ? "Fácil" : (difficulty == 1 ? "Médio" : "Difícil");

        menuSelectionText.text = $"Música: <color=#00FF88>{nomeMusica}</color>\nDificuldade: <color=#00D8FF>{nomeDificuldade}</color> ({LanesFor(difficulty)} linhas)";
    }
}