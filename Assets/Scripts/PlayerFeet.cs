using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// colliders só nos pés; ficam dentro dos ossos e seguem a animação ou o mocap
public class PlayerFeet : MonoBehaviour
{
    public const string ColliderName = "Foot Collider";
    const float Radius = 0.045f;

    // osso do tornozelo, osso da ponta do pé e nome do collider
    static readonly (string bone, string tip, string label)[] Bones =
    {
        ("LeftFoot", "LeftToeBaseEnd", "Left"),
        ("RightFoot", "RightToeBaseEnd", "Right"),
    };

    [HideInInspector] public List<FootCollider> colliders = new List<FootCollider>();

    // pés a tocar numa tecla (0, 1 ou 2)
    public int GroundedCount
    {
        get
        {
            int n = 0;
            foreach (FootCollider c in colliders)
                if (c != null && c.IsGrounded) n++;
            return n;
        }
    }

    // há um pé nesta tecla? cada pé só pode estar numa tecla (há espaço entre elas)
    public bool IsOnLane(int lane)
    {
        foreach (FootCollider c in colliders)
            if (c != null && c.Lane == lane) return true;
        return false;
    }

    void Awake()
    {
        EnsureRigidbody();
        colliders.RemoveAll(c => c == null);
        if (colliders.Count < Bones.Length) CreateColliders();
    }

    // os triggers precisam de um rigidbody; kinematic porque quem mexe é a animação
    public void EnsureRigidbody()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // cria (ou refaz) os colliders dos pés; devolve os objetos criados
    public List<GameObject> CreateColliders()
    {
        var created = new List<GameObject>();
        colliders.Clear();

        foreach (var foot in Bones)
        {
            Transform bone = Find(transform, foot.bone);
            if (bone == null)
            {
                Debug.LogWarning($"[Piano] bone '{foot.bone}' not found on '{name}'.", this);
                continue;
            }

            for (int i = bone.childCount - 1; i >= 0; i--)
            {
                Transform child = bone.GetChild(i);
                if (child.GetComponent<FootCollider>() == null) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }

            Transform tip = Find(transform, foot.tip) ?? Find(transform, foot.bone.Replace("Foot", "ToeBase"));

            // do tornozelo até à ponta do pé
            float scale = Mathf.Max(0.0001f, Mathf.Abs(bone.lossyScale.x));
            Vector3 local = tip != null ? bone.InverseTransformPoint(tip.position) : Vector3.zero;
            if (local.sqrMagnitude < 1e-8f) local = bone.InverseTransformDirection(transform.forward) * (0.15f / scale);

            var go = new GameObject($"{ColliderName} {foot.label}");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = local * 0.5f;
            Vector3 dir = local.normalized;
            go.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up);

            float r = Radius / scale;
            CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 2;
            capsule.radius = r;
            capsule.height = local.magnitude + 2f * r;

            colliders.Add(go.AddComponent<FootCollider>());
            created.Add(go);
        }
        return created;
    }

    static string Clean(string n) => Regex.Replace(n, @"(\s*\(\d+\)|[ _.]\d+)$", "");

    static Transform Find(Transform root, string boneName)
    {
        if (root.name == boneName || Clean(root.name) == boneName) return root;
        foreach (Transform child in root)
        {
            Transform r = Find(child, boneName);
            if (r != null) return r;
        }
        return null;
    }

    void OnDrawGizmos()
    {
        foreach (FootCollider f in colliders)
        {
            if (f == null) continue;
            CapsuleCollider c = f.GetComponent<CapsuleCollider>();
            if (c == null) continue;
            Gizmos.color = Application.isPlaying && f.IsGrounded ? Color.yellow : Color.green;
            float half = Mathf.Max(0f, c.height * 0.5f - c.radius);
            float r = c.radius * Mathf.Abs(c.transform.lossyScale.x);
            Gizmos.DrawWireSphere(c.transform.TransformPoint(new Vector3(0, 0, -half)), r);
            Gizmos.DrawWireSphere(c.transform.TransformPoint(new Vector3(0, 0, half)), r);
        }
    }
}
