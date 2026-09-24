using TMPro;
using UnityEngine;

// menu inicial: ecrã Play / Exit e ecrã de dificuldade (Easy / Medium / Hard / Back)
// é criado por Tools > Piano > Build menu and HUD
public class GameMenu : MonoBehaviour
{
    public PianoGame game;

    [Header("Screens")]
    public GameObject startScreen;        // logo, título, Play e Exit
    public GameObject difficultyScreen;   // música, Easy, Medium, Hard e Back

    [Header("Song")]
    public TMP_Text songText;

    // o PianoGame liga este objeto quando abre o menu: começa sempre no ecrã inicial
    void OnEnable() => ShowStart();

    public void ShowStart()
    {
        if (startScreen != null) startScreen.SetActive(true);
        if (difficultyScreen != null) difficultyScreen.SetActive(false);
    }

    public void ShowDifficulty()
    {
        if (startScreen != null) startScreen.SetActive(false);
        if (difficultyScreen != null) difficultyScreen.SetActive(true);
        UpdateSong();
    }

    // 0 = easy, 1 = medium, 2 = hard; começa logo o jogo
    public void ChooseDifficulty(int difficulty)
    {
        if (game == null) return;
        game.SetDifficulty(difficulty);
        game.StartSelectedGame();
    }

    public void NextSong() => ChangeSong(1);
    public void PreviousSong() => ChangeSong(-1);

    void ChangeSong(int step)
    {
        if (game == null || game.SongCount == 0) return;
        int index = (game.SelectedSongIndex + step + game.SongCount) % game.SongCount;
        game.SelectSong(index);
        UpdateSong();
    }

    void UpdateSong()
    {
        if (songText == null || game == null) return;
        songText.text = game.SongCount > 0 ? game.SongName(game.SelectedSongIndex) : "";
    }

    public void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
