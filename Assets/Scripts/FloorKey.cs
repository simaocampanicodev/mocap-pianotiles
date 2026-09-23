using UnityEngine;

// collider (trigger) de uma tecla do chão; um pé a tocar aqui está no chão nesta lane
[RequireComponent(typeof(BoxCollider))]
public class FloorKey : MonoBehaviour
{
    [HideInInspector] public int lane;   // 0 = esquerda, 1 = meio, 2 = direita
}
