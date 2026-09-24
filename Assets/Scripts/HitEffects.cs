using UnityEngine;
using UnityEngine.Rendering;

// efeito de acerto: faíscas a saltar + um brilho rápido, feitos por código (não precisa de prefab)
public static class HitEffects
{
    static Material defaultMaterial;

    // strength 1 = tile normal, maior = slide completo
    public static void Play(Vector3 position, Color color, float strength, Material material)
    {
        if (material == null) material = DefaultMaterial();
        if (material == null) return;

        var root = new GameObject("Hit Effect");
        root.transform.position = position;

        ParticleSystem sparks = Sparks(root.transform, color, strength, material);
        ParticleSystem glow = Glow(root.transform, color, strength, material);
        sparks.Play();
        glow.Play();
        Object.Destroy(root, 2f);
    }

    // faíscas que saltam para cima e caem
    static ParticleSystem Sparks(Transform parent, Color color, float strength, Material material)
    {
        ParticleSystem ps = NewSystem(parent, "sparks", material);
        ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // a semiesfera aponta para cima

        var main = ps.main;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * strength, 4f * strength);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 1f;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(28 * strength)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.2f * strength;

        FadeOut(ps, 1f);
        return ps;
    }

    // clarão redondo que cresce e desaparece
    static ParticleSystem Glow(Transform parent, Color color, float strength, Material material)
    {
        ParticleSystem ps = NewSystem(parent, "glow", material);

        var main = ps.main;
        main.duration = 0.1f;
        main.startLifetime = 0.3f;
        main.startSpeed = 0f;
        main.startSize = 1.1f * strength;
        main.startColor = new Color(color.r, color.g, color.b, 0.8f);

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });

        var shape = ps.shape;
        shape.enabled = false;

        FadeOut(ps, 0.3f, grow: true);
        return ps;
    }

    static ParticleSystem NewSystem(Transform parent, string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    // transparente até desaparecer; o clarão cresce, as faíscas encolhem
    static void FadeOut(ParticleSystem ps, float startSize, bool grow = false)
    {
        var colors = ps.colorOverLifetime;
        colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colors.color = gradient;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, grow
            ? AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f)
            : AnimationCurve.Linear(0f, startSize, 1f, 0f));
    }

    // material de partículas do URP, aditivo (brilha), com uma bolinha suave como textura
    public static Material DefaultMaterial()
    {
        if (defaultMaterial != null) return defaultMaterial;
        defaultMaterial = CreateMaterial(CreateTexture());
        return defaultMaterial;
    }

    public static Material CreateMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[Piano] URP particle shader not found: no hit effect.");
            return null;
        }
        var m = new Material(shader) { name = "Hit Effect" };
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);   // aditivo
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        return m;
    }

    public static Texture2D CreateTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Hit Effect Dot", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
