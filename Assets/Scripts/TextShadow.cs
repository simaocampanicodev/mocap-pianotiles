using TMPro;
using UnityEngine;

// sombra de um texto: copia o texto do original (a cor e a posição ficam as desta sombra)
[RequireComponent(typeof(TMP_Text))]
public class TextShadow : MonoBehaviour
{
    public TMP_Text source;
    TMP_Text self;

    void LateUpdate()
    {
        if (source == null) return;
        if (self == null) self = GetComponent<TMP_Text>();
        if (self.text != source.text) self.text = source.text;
    }
}
