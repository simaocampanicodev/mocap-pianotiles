using UnityEngine;

// um tile na pista; quem o mexe e avalia é o PianoGame
// slide = tile comprido em que é preciso ficar em cima até ele passar todo
public class PianoTile : MonoBehaviour
{
    public enum State { Incoming, Holding, Hit, Missed }

    [HideInInspector] public GameObject normal;        // modelo Piano_Tile_N
    [HideInInspector] public GameObject pressed;       // modelo Piano_Tile_N_Pressed
    [HideInInspector] public Transform fill;           // cópia da tecla que vai ganhando cor no slide
    [HideInInspector] public int lane;
    [HideInInspector] public float arrivalTime;        // segundo em que a frente do tile chega ao jogador
    [HideInInspector] public float holdDuration;       // 0 = tile normal, > 0 = slide
    [HideInInspector] public float length;             // comprimento na pista (metros)
    [HideInInspector] public float lastOnLaneTime;     // último momento com o pé na tecla (slide)
    [HideInInspector] public State state;
    [HideInInspector] public float pressedHeight = 0.6f; // altura da tecla afundada (a normal = 1)

    const float PressTime = 0.12f;   // tempo a afundar

    public bool IsSlide => holdDuration > 0f;

    public void Paint(Material material)
    {
        if (material == null) return;
        Paint(normal, material);
        Paint(pressed, material);
    }

    // afunda a tecla aos poucos (dá para ver a parte de baixo a encolher) e troca para a tecla afundada
    public void Press(Material material)
    {
        ShowProgress(false);
        Paint(material);
        if (isActiveAndEnabled) StartCoroutine(PressAnimation());
        else FinishPress();
    }

    System.Collections.IEnumerator PressAnimation()
    {
        Vector3 start = transform.localScale;
        for (float t = 0f; t < PressTime; t += Time.deltaTime)
        {
            float p = t / PressTime;
            p = 1f - (1f - p) * (1f - p);   // começa rápido e trava no fim
            transform.localScale = new Vector3(start.x, start.y * Mathf.Lerp(1f, pressedHeight, p), start.z);
            yield return null;
        }
        transform.localScale = start;
        FinishPress();
    }

    void FinishPress()
    {
        if (pressed != null)
        {
            if (normal != null) normal.SetActive(false);
            pressed.SetActive(true);
        }
        else
        {
            Vector3 s = transform.localScale;
            transform.localScale = new Vector3(s.x, s.y * pressedHeight, s.z);
        }
    }

    // uma malha da cópia colorida: guarda os vértices no espaço do tile para os poder cortar
    class FillPart
    {
        public Mesh mesh;
        public Vector3[] tileSpace;   // vértices originais no espaço do tile (Z de 0 a 1)
        public Vector3[] work;
        public Matrix4x4 toMesh;      // espaço do tile -> espaço da malha
    }

    FillPart[] fillParts;
    bool fillScaled;   // malha sem Read/Write: encolhe a cópia em vez de a cortar

    // parte do slide já feita (0..1): a cor vai enchendo a partir da frente
    // a cópia tem a mesma forma que a tecla; o que passa do corte fica achatado no corte
    public void SetProgress(float amount)
    {
        if (fill == null) return;
        amount = Mathf.Clamp01(amount);
        fill.gameObject.SetActive(amount > 0f);
        if (amount <= 0f) return;

        if (fillParts == null) SetupFill();
        if (fillScaled)
        {
            fill.localScale = new Vector3(fill.localScale.x, fill.localScale.y, amount);
            return;
        }
        foreach (FillPart p in fillParts)
        {
            for (int i = 0; i < p.work.Length; i++)
            {
                Vector3 v = p.tileSpace[i];
                if (v.z > amount) v.z = amount;
                p.work[i] = p.toMesh.MultiplyPoint3x4(v);
            }
            p.mesh.vertices = p.work;
            p.mesh.RecalculateBounds();
        }
    }

    void SetupFill()
    {
        MeshFilter[] filters = fill.GetComponentsInChildren<MeshFilter>(true);
        var parts = new System.Collections.Generic.List<FillPart>();
        foreach (MeshFilter mf in filters)
        {
            Mesh source = mf.sharedMesh;
            if (source == null) continue;
            if (!source.isReadable)
            {
                Debug.LogWarning($"[Piano] mesh '{source.name}' has no Read/Write: slide fill will look squashed. " +
                                 "Turn on Read/Write in the tile model import settings.", this);
                fillScaled = true;
                fillParts = new FillPart[0];
                return;
            }

            var mesh = Instantiate(source);
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;

            Matrix4x4 toTile = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;
            var tileSpace = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) tileSpace[i] = toTile.MultiplyPoint3x4(verts[i]);
            parts.Add(new FillPart { mesh = mesh, tileSpace = tileSpace, work = new Vector3[verts.Length], toMesh = toTile.inverse });
        }
        fillParts = parts.ToArray();
    }

    void OnDestroy()
    {
        if (fillParts == null) return;
        foreach (FillPart p in fillParts)
            if (p.mesh != null) Destroy(p.mesh);
    }

    public void ShowProgress(bool show)
    {
        if (fill != null) fill.gameObject.SetActive(show);
    }

    public void PaintProgress(Material material)
    {
        if (fill != null && material != null) Paint(fill.gameObject, material);
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
