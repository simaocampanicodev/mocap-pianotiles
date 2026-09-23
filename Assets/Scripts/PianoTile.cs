using UnityEngine;

// um tile na pista; quem o mexe e avalia é o PianoGame
public class PianoTile : MonoBehaviour
{
    public enum State { Incoming, Hit, Missed }

    [HideInInspector] public GameObject normal;    // modelo Piano_Tile_N
    [HideInInspector] public GameObject pressed;   // modelo Piano_Tile_N_Pressed
    [HideInInspector] public int lane;
    [HideInInspector] public float arrivalTime;    // segundo em que a frente do tile chega ao jogador
    [HideInInspector] public State state;

    public void Paint(Material material)
    {
        if (material == null) return;
        Paint(normal, material);
        Paint(pressed, material);
    }

    // troca para a tecla afundada
    public void Press(Material material)
    {
        if (pressed != null)
        {
            if (normal != null) normal.SetActive(false);
            pressed.SetActive(true);
        }
        else
        {
            Vector3 s = transform.localScale;
            transform.localScale = new Vector3(s.x, s.y * 0.6f, s.z);
        }
        Paint(material);
    }

    static void Paint(GameObject go, Material material)
    {
        if (go == null) return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }
    }
}
