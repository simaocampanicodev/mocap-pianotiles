using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// converte uma animação de um esqueleto para outro (eixos e poses de repouso diferentes)
// mesma conversão do jogo do Subway

public class Bone
{
    public string name;
    public Transform t;
    public Bone parent;
    public string path;
    public Quaternion restLocal;
    public Vector3 restLocalPos;
    public Quaternion restWorld;    // em relação à raiz do modelo
    public Vector3 restWorldPos;

    public bool IsDescendantOf(Bone other)
    {
        for (Bone p = parent; p != null; p = p.parent)
            if (p == other) return true;
        return false;
    }
}

public class Skeleton
{
    public Transform root;
    public Bone Hips;
    public List<Bone> Bones = new List<Bone>();
    public Dictionary<string, Bone> ByName = new Dictionary<string, Bone>();
    public Quaternion hipsParentRestWorld = Quaternion.identity;
    public Vector3 hipsParentRestWorldPos = Vector3.zero;

    public static Skeleton From(Transform root)
    {
        Transform hips = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => PianoSetup.Clean(t.name) == "Hips" && HasDescendant(t, "Neck1"));
        if (hips == null) return null;

        var s = new Skeleton { root = root };
        s.Hips = s.Add(hips, null);
        s.AddChildren(hips, s.Hips);

        Transform parent = hips.parent;
        if (parent != null)
        {
            s.hipsParentRestWorld = Quaternion.Inverse(root.rotation) * parent.rotation;
            s.hipsParentRestWorldPos = root.InverseTransformPoint(parent.position);
        }
        return s;
    }

    void AddChildren(Transform t, Bone bone)
    {
        foreach (Transform child in t)
        {
            Bone b = Add(child, bone);
            AddChildren(child, b);
        }
    }

    Bone Add(Transform t, Bone parent)
    {
        var b = new Bone
        {
            name = PianoSetup.Clean(t.name),
            t = t,
            parent = parent,
            path = AnimationUtility.CalculateTransformPath(t, root),
            restLocal = t.localRotation,
            restLocalPos = t.localPosition,
            restWorld = Quaternion.Inverse(root.rotation) * t.rotation,
            restWorldPos = root.InverseTransformPoint(t.position),
        };
        Bones.Add(b);
        if (!ByName.ContainsKey(b.name)) ByName[b.name] = b;
        return b;
    }

    public Bone ByPath(string path) => Bones.FirstOrDefault(b => b.path == path);

    static bool HasDescendant(Transform t, string name)
    {
        foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
            if (child != t && PianoSetup.Clean(child.name) == name) return true;
        return false;
    }
}

public class Retargeter
{
    readonly Skeleton source, target;
    readonly Quaternion W;
    readonly Dictionary<string, Quaternion> delta = new Dictionary<string, Quaternion>();
    readonly float scale;
    readonly Vector3 upLocal, sideLocal, forwardLocal;

    public Retargeter(Skeleton source, Skeleton target)
    {
        this.source = source;
        this.target = target;

        // 1. alinhar os dois corpos (um pode ter a coluna em Y e o outro em Z)
        W = BodyFrame(target) * Quaternion.Inverse(BodyFrame(source));

        // 2. pôr o repouso do alvo na pose de repouso da gravação (T-pose)
        var P = target.Bones.ToDictionary(b => b, b => b.restWorldPos);
        var Q = target.Bones.ToDictionary(b => b, b => b.restWorld);
        foreach (Bone b in target.Bones)
        {
            if (!source.ByName.TryGetValue(b.name, out Bone sb)) continue;
            Bone child = SharedChild(b);
            if (child == null) continue;
            Vector3 dT = P[child] - P[b];
            Vector3 dS = W * (source.ByName[child.name].restWorldPos - sb.restWorldPos);
            if (dT.sqrMagnitude < 1e-8f || dS.sqrMagnitude < 1e-8f) continue;

            Quaternion R = Quaternion.FromToRotation(dT, dS);
            Vector3 pivot = P[b];
            foreach (Bone j in target.Bones)
                if (j == b || j.IsDescendantOf(b))
                {
                    P[j] = pivot + R * (P[j] - pivot);
                    Q[j] = R * Q[j];
                }
        }

        // 3. diferença entre cada osso da gravação e o mesmo osso do boneco
        foreach (Bone b in target.Bones)
            if (source.ByName.TryGetValue(b.name, out Bone sb))
                delta[b.name] = Quaternion.Inverse(W * sb.restWorld) * Q[b];

        // 4. tamanhos diferentes: a altura da anca acompanha o comprimento da perna
        float legS = Length(source, "Hips", "LeftFoot");
        float legT = Length(target, "Hips", "LeftFoot");
        scale = legS > 0.0001f ? legT / legS : 1f;

        // eixos do corpo do boneco no espaço do pai da anca
        Quaternion toLocal = Quaternion.Inverse(target.hipsParentRestWorld);
        Vector3 up = target.ByName.TryGetValue("Head", out Bone head)
            ? (head.restWorldPos - target.Hips.restWorldPos).normalized
            : Vector3.up;
        upLocal = (toLocal * up).normalized;

        Vector3 side = target.ByName.ContainsKey("LeftUpLeg") && target.ByName.ContainsKey("RightUpLeg")
            ? target.ByName["RightUpLeg"].restWorldPos - target.ByName["LeftUpLeg"].restWorldPos
            : Vector3.right;
        sideLocal = Vector3.ProjectOnPlane(toLocal * side, upLocal).normalized;
        if (sideLocal.sqrMagnitude < 0.5f) sideLocal = Vector3.ProjectOnPlane(Vector3.right, upLocal).normalized;
        forwardLocal = Vector3.Cross(upLocal, sideLocal).normalized;
    }

    static float Length(Skeleton s, string a, string b)
    {
        return s.ByName.ContainsKey(a) && s.ByName.ContainsKey(b)
            ? Vector3.Distance(s.ByName[a].restWorldPos, s.ByName[b].restWorldPos) : 0f;
    }

    public static string MissingBone(Skeleton s)
    {
        foreach (string n in new[] { "Head", "LeftUpLeg", "RightUpLeg", "LeftFoot", "RightFoot" })
            if (!s.ByName.ContainsKey(n)) return n;
        return null;
    }

    static Quaternion BodyFrame(Skeleton s)
    {
        Vector3 hips = s.Hips.restWorldPos;
        Vector3 up = (s.ByName["Head"].restWorldPos - hips).normalized;
        Vector3 side = (s.ByName["RightUpLeg"].restWorldPos - s.ByName["LeftUpLeg"].restWorldPos).normalized;
        Vector3 forward = Vector3.Cross(side, up).normalized;
        return Quaternion.LookRotation(forward, up);
    }

    Bone SharedChild(Bone b)
    {
        var queue = new Queue<Bone>(target.Bones.Where(x => x.parent == b));
        while (queue.Count > 0)
        {
            Bone c = queue.Dequeue();
            if (source.ByName.ContainsKey(c.name)) return c;
            foreach (Bone n in target.Bones.Where(x => x.parent == c)) queue.Enqueue(n);
        }
        return null;
    }

    // curvas: [0..3] quaternião, [4..6] posição da anca, [7..9] ângulos
    public void Convert(Dictionary<string, AnimationCurve[]> curves, float t,
                        Dictionary<string, Quaternion[]> outRot, Vector3[] outPos, int frame)
    {
        // pose da gravação
        var qSource = new Dictionary<string, Quaternion>();
        foreach (Bone b in source.Bones)
        {
            Quaternion local = b.restLocal;
            if (curves.TryGetValue(b.name, out AnimationCurve[] c))
            {
                if (c[0] != null && c[1] != null && c[2] != null && c[3] != null)
                {
                    var q = new Quaternion(c[0].Evaluate(t), c[1].Evaluate(t), c[2].Evaluate(t), c[3].Evaluate(t));
                    if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.0001f) local = q.normalized;
                }
                else if (c[7] != null || c[8] != null || c[9] != null)
                {
                    Vector3 e = b.restLocal.eulerAngles;
                    if (c[7] != null) e.x = c[7].Evaluate(t);
                    if (c[8] != null) e.y = c[8].Evaluate(t);
                    if (c[9] != null) e.z = c[9].Evaluate(t);
                    local = Quaternion.Euler(e);
                }
            }
            Quaternion qParent = b.parent != null && qSource.ContainsKey(b.parent.name) ? qSource[b.parent.name] : source.hipsParentRestWorld;
            qSource[b.name] = qParent * local;
        }

        // mesma pose no boneco
        var qTarget = new Dictionary<string, Quaternion>();
        foreach (Bone b in target.Bones)
        {
            Quaternion q;
            if (delta.ContainsKey(b.name) && qSource.ContainsKey(b.name))
                q = W * qSource[b.name] * delta[b.name];
            else if (b.parent != null && qTarget.ContainsKey(b.parent.name))
                q = qTarget[b.parent.name] * b.restLocal;
            else
                q = b.restWorld;
            qTarget[b.name] = q;

            Quaternion qParent = b.parent != null && qTarget.ContainsKey(b.parent.name) ? qTarget[b.parent.name] : target.hipsParentRestWorld;
            Quaternion local = Quaternion.Inverse(qParent) * q;
            if (frame > 0)
            {
                Quaternion prev = outRot[b.name][frame - 1];
                if (local.x * prev.x + local.y * prev.y + local.z * prev.z + local.w * prev.w < 0f)
                    local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
            }
            outRot[b.name][frame] = local;
        }

        // posição da anca
        Vector3 pSource = source.Hips.restWorldPos;
        if (curves.TryGetValue(source.Hips.name, out AnimationCurve[] ch) && ch[4] != null && ch[5] != null && ch[6] != null)
        {
            var localPos = new Vector3(ch[4].Evaluate(t), ch[5].Evaluate(t), ch[6].Evaluate(t));
            pSource = source.hipsParentRestWorldPos + source.hipsParentRestWorld * localPos;
        }
        Vector3 pTarget = W * (pSource * scale);
        outPos[frame] = Quaternion.Inverse(target.hipsParentRestWorld) * (pTarget - target.hipsParentRestWorldPos);
    }

    // a altura da anca fica sempre; lado/frente só se pedido (relativo ao 1º frame)
    // quem muda de lane é o código; no split o corpo vai e volta, por isso fica
    public void CleanDisplacement(Vector3[] positions, bool keepSide, bool keepForward)
    {
        if (positions.Length == 0) return;
        Vector3 basePos = target.Hips.restLocalPos;
        Vector3 start = positions[0];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = positions[i] - start;
            Vector3 r = basePos + upLocal * Vector3.Dot(positions[i] - basePos, upLocal);
            if (keepSide) r += sideLocal * Vector3.Dot(d, sideLocal);
            if (keepForward) r += forwardLocal * Vector3.Dot(d, forwardLocal);
            positions[i] = r;
        }
    }
}
