using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD do jogo: vidas (notas musicais), o bichinho que mostra se a música está a tocar
// e o ecrã do fim (vitória ou derrota) com Retry e Back
// o score (estrela + número) é o texto Score do PianoGame
// é criado por Tools > Piano > Build menu and HUD
public class GameHud : MonoBehaviour
{
    public PianoGame game;

    [Header("Lives")]
    [Tooltip("one note per life, from left to right (the one on the right is lost first)")]
    public Image[] lifeIcons = new Image[3];

    [Header("Sound")]
    public Image soundIcon;
    public Sprite soundOn;    // bichinho com headphones
    public Sprite soundOff;   // bichinho sem headphones

    [Header("End screen")]
    public GameObject resultPanel;
    public TMP_Text resultTitle;
    public TMP_Text resultScore;
    public TMP_Text resultDetails;
    public Image resultImage;
    public Sprite winImage;
    public Sprite loseImage;
    public Color winColor = new Color(1f, 0.85f, 0.25f);
    public Color loseColor = new Color(1f, 0.35f, 0.35f);

    bool? lastSound;
    bool resultShown;

    void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
    }

    void Update()
    {
        if (game == null) return;

        int left = Mathf.Max(0, game.lives - game.Misses);
        for (int i = 0; i < lifeIcons.Length; i++)
            if (lifeIcons[i] != null) lifeIcons[i].enabled = i < left;

        bool playing = game.MusicPlaying;
        if (soundIcon != null && lastSound != playing)
        {
            lastSound = playing;
            Sprite s = playing ? soundOn : soundOff;
            if (s != null) soundIcon.sprite = s;
        }

        bool finished = game.Finished && !game.InMenu;
        if (finished != resultShown) ShowResult(finished);
    }

    void ShowResult(bool show)
    {
        resultShown = show;
        if (resultPanel == null) return;
        resultPanel.SetActive(show);
        if (!show) return;

        bool won = game.Won;
        if (resultTitle != null)
        {
            resultTitle.text = won ? "YOU WIN!" : "GAME OVER";
            resultTitle.color = won ? winColor : loseColor;
        }
        if (resultScore != null) resultScore.text = game.Score.ToString();
        if (resultDetails != null)
            resultDetails.text = $"{game.Score} / {game.TotalNotes} notes     {game.Misses} misses";
        if (resultImage != null)
        {
            Sprite s = won ? winImage : loseImage;
            if (s != null) resultImage.sprite = s;
        }
    }

    // botões do ecrã do fim
    public void Retry()
    {
        if (game != null) game.StartSelectedGame();   // mesma música e dificuldade
    }

    public void BackToMenu()
    {
        if (game != null) game.OpenMenu();
    }
}
