using System.Collections.Generic;
using UnityEngine;

// collider de um pé; guarda as teclas do chão em que está a tocar
[RequireComponent(typeof(Collider))]
public class FootCollider : MonoBehaviour
{
    readonly List<FloorKey> touching = new List<FloorKey>();
    Collider col;

    public bool IsGrounded
    {
        get
        {
            touching.RemoveAll(k => k == null || !k.isActiveAndEnabled);
            return touching.Count > 0;
        }
    }

    public int Lane => TryGetLane(out int lane, out _) ? lane : -1;

    // tecla onde o pé está e a que distância fica do centro dela (-1 = pé no ar)
    public bool TryGetLane(out int lane, out float distance)
    {
        lane = -1;
        distance = float.MaxValue;
        if (!IsGrounded) return false;

        if (col == null) col = GetComponent<Collider>();
        Vector3 center = col.bounds.center;
        foreach (FloorKey k in touching)
        {
            float d = Mathf.Abs(k.transform.InverseTransformPoint(center).x * k.transform.lossyScale.x);
            if (d >= distance) continue;
            distance = d;
            lane = k.lane;
        }
        return lane >= 0;
    }

    void OnTriggerEnter(Collider other)
    {
        FloorKey k = other.GetComponent<FloorKey>();
        if (k != null && !touching.Contains(k)) touching.Add(k);
    }

    void OnTriggerExit(Collider other)
    {
        FloorKey k = other.GetComponent<FloorKey>();
        if (k != null) touching.Remove(k);
    }

    void OnDisable() => touching.Clear();
}
