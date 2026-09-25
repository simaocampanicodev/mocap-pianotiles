using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// menu Tools > Piano (como o menu do jogo do Subway)
// 1 - converte as gravações PianoPractice_*.fbx para o boneco e cria o Animator Controller
// 2 - monta a cena: pista, teclas com collider, colliders nos pés, textos e câmara
public static class PianoSetup
{
    const string Folder = "Assets/Piano";
    const string ClipsFolder = Folder + "/Clips";
    const string MaterialsFolder = Folder + "/Materials";
    const string ControllerPath = Folder + "/Player.controller";
    const string CharacterPath = "Assets/Character/ViconActor.fbx";
    const string TilesFolder = "Assets/Art/Piano";
    const string FeetOnGroundParameter = "FeetOnGround";

    struct ClipInfo
    {
        public string fbx, state;
        public float start, end;
        public bool loop;
        public bool keepSide;   // o corpo vai e volta sozinho (split)
    }

    // tempos medidos nas gravações: corta a preparação longa e o fim parado
    static readonly ClipInfo[] Clips =
    {
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_Idle.fbx",        state = "idle",        start = 1.5f,  end = 4.4f, loop = true },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_HopLeft.fbx",     state = "hop_left",    start = 2.3f,  end = 3.6f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_HopRight.fbx",    state = "hop_right",   start = 1.85f, end = 3.3f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_HopMiddle2.fbx",  state = "hop_middle",  start = 2.35f, end = 3.6f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_JumpInPlace.fbx", state = "jump",        start = 1.45f, end = 3.1f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_JumpLeft.fbx",    state = "jump_left",   start = 1.35f, end = 2.9f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_JumpRight2.fbx",  state = "jump_right",  start = 1.4f,  end = 3.18f },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_SplitLeft.fbx",   state = "split_left",  start = 1.2f,  end = 3.2f, keepSide = true },
        new ClipInfo { fbx = "Assets/Animations/PianoPractice_SplitRight.fbx",  state = "split_right", start = 1.1f,  end = 3.0f, keepSide = true },
    };

    // ------------------------------------------------------------- menus

    [MenuItem("Tools/Piano/Do everything (1 + 2)", priority = 0)]
    public static void DoEverything()
    {
        Transform character = PickCharacter();
        if (character == null) return;
        if (PrepareAnimations(character)) BuildGame(character);
    }

    [MenuItem("Tools/Piano/1 - Prepare animations", priority = 20)]
    public static void PrepareAnimationsMenu()
    {
        Transform character = PickCharacter();
        if (character != null) PrepareAnimations(character);
    }

    [MenuItem("Tools/Piano/2 - Build game in scene", priority = 21)]
    public static void BuildGameMenu()
    {
        Transform character = PickCharacter();
        if (character != null) BuildGame(character);
    }

    [MenuItem("Tools/Piano/Put camera behind player", priority = 30)]
    public static void CameraMenu()
    {
        PianoGame game = AllInScene<PianoGame>().FirstOrDefault();
        if (game == null) Debug.LogWarning("[Piano] no game in the scene yet: run 'Tools > Piano > Do everything'.");
        else PlaceCamera(game.transform, true);
    }

    [MenuItem("Tools/Piano/Show status", priority = 40)]
    public static void ShowStatus()
    {
        var txt = new System.Text.StringBuilder("[Piano] STATUS\n");

        int clipCount = 0;
        foreach (ClipInfo info in Clips)
        {
            bool fbx = AssetDatabase.LoadAssetAtPath<GameObject>(info.fbx) != null;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsFolder}/{info.state}.anim");
            if (clip != null) clipCount++;
            txt.AppendLine($"  {info.state,-12} recording: {(fbx ? "ok" : "MISSING")}   clip: {(clip != null ? $"ok ({clip.length:0.0}s)" : "NO")}");
        }
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        txt.AppendLine($"  Animator Controller: {(ctrl != null ? $"ok ({ctrl.parameters.Length} parameters)" : "NO - run step 1")}");
        for (int i = 1; i <= 3; i++)
        {
            bool n = AssetDatabase.LoadAssetAtPath<GameObject>($"{TilesFolder}/Piano_Tile_{i}.fbx") != null;
            bool p = AssetDatabase.LoadAssetAtPath<GameObject>($"{TilesFolder}/Piano_Tile_{i}_Pressed.fbx") != null;
            txt.AppendLine($"  Piano_Tile_{i}: {(n ? "ok" : "MISSING")}   pressed: {(p ? "ok" : "MISSING")}");
        }

        PianoGame game = AllInScene<PianoGame>().FirstOrDefault();
        PlayerController player = AllInScene<PlayerController>().FirstOrDefault();
        txt.AppendLine($"  PianoGame in scene: {(game != null ? "ok" : "NO")}");
        if (game != null)
        {
            txt.AppendLine($"    character: {(game.player != null ? game.player.name : "NO")}");
            txt.AppendLine($"    tile models: {game.tileModels.Count(m => m != null)}/3   pressed: {game.pressedTileModels.Count(m => m != null)}/3");
            txt.AppendLine($"    slides: {(game.slides ? $"on (held notes {game.slideLengthFactor:0.0}x longer than usual, {game.slideMinHold:0.0}-{game.slideMaxDuration:0.0}s)" : "off")}");
            txt.AppendLine($"    floor key colliders: {game.GetComponentsInChildren<FloorKey>(true).Length}/3");
        }
        if (player == null) txt.AppendLine("  PlayerController in scene: NO - run step 2");
        else
        {
            Animator an = player.GetComponent<Animator>();
            PlayerFeet feet = player.GetComponent<PlayerFeet>();
            txt.AppendLine($"  Player: {player.name}   live mocap: {(player.liveMocap ? "yes" : "no (keyboard)")}");
            txt.AppendLine($"    Animator: {(an == null ? "NO" : an.runtimeAnimatorController == null ? "no controller" : an.runtimeAnimatorController.name)}");
            txt.AppendLine($"    foot colliders: {(feet == null ? "NO" : feet.colliders.Count(c => c != null) + "/2")}");
        }
        txt.AppendLine(clipCount == Clips.Length && ctrl != null && game != null && player != null
            ? "  >> everything is set up: press Play"
            : "  >> run 'Tools > Piano > Do everything (1 + 2)'");
        Debug.Log(txt.ToString());
    }

    // boneco selecionado; senão um que já esteja na cena; senão põe o ViconActor
    static Transform PickCharacter()
    {
        GameObject sel = Selection.activeGameObject;
        if (sel != null && sel.scene.IsValid() && Find(sel.transform, "Hips") != null)
            return sel.transform;

        Transform[] candidates = AllInScene<Transform>()
            .Where(t => t.parent == null && Find(t, "Hips") != null).ToArray();

        Transform chosen = candidates.FirstOrDefault(t => t.GetComponent<PlayerController>() != null)
                        ?? candidates.FirstOrDefault(t => t.name.StartsWith("ViconActor"))
                        ?? candidates.FirstOrDefault(t => t.name.StartsWith("ViconMale"))
                        ?? candidates.FirstOrDefault();

        if (chosen == null)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            if (model != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, SceneManager.GetActiveScene());
                Undo.RegisterCreatedObjectUndo(go, "Piano");
                chosen = go.transform;
                Debug.Log($"[Piano] added {CharacterPath} to the scene.", go);
            }
        }

        if (chosen == null)
            EditorUtility.DisplayDialog("Piano", "No character with a 'Hips' bone in the scene and no " + CharacterPath +
                                                 ".\nDrag a character into the scene, select it and run the menu again.", "OK");
        else
            Debug.Log($"[Piano] character: {chosen.name} (select another one in the Hierarchy to change)", chosen);
        return chosen;
    }

    // ------------------------------------------------------------- 1: animations

    public static bool PrepareAnimations(Transform character)
    {
        // a pose de repouso vem do ficheiro do boneco (se for prefab), não da pose na cena
        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(character.gameObject);
        Transform targetRoot = source != null && source.transform.parent == null ? source.transform : character;

        Skeleton target = Skeleton.From(targetRoot);
        if (target == null)
        {
            Debug.LogError($"[Piano] '{character.name}' has no skeleton with Hips and Neck1. Select the right character and run the menu again.", character);
            return false;
        }
        string missing = Retargeter.MissingBone(target);
        if (missing != null)
        {
            Debug.LogError($"[Piano] character '{character.name}' has no '{missing}' bone.", character);
            return false;
        }

        CreateFolder(Folder);
        CreateFolder(ClipsFolder);

        var done = new Dictionary<string, AnimationClip>();
        foreach (ClipInfo info in Clips)
        {
            try
            {
                AnimationClip clip = ConvertClip(info, target);
                if (clip != null) done[info.state] = clip;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Piano] failed to prepare '{info.state}': {e.Message}\n{e.StackTrace}");
            }
        }

        if (done.Count == 0) return false;
        CreateController(done);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return true;
    }

    static AnimationClip ConvertClip(ClipInfo info, Skeleton target)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(info.fbx);
        AnimationClip original = AssetDatabase.LoadAllAssetsAtPath(info.fbx).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (model == null || original == null)
        {
            // sem a gravação: usa a animação que já foi convertida antes (se existir)
            var ready = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsFolder}/{info.state}.anim");
            if (ready != null)
            {
                Debug.Log($"[Piano] {info.state}: recording {info.fbx} not in the project, using the clip already made.");
                return ready;
            }
            Debug.LogError($"[Piano] no animation found in {info.fbx} and no {ClipsFolder}/{info.state}.anim");
            return null;
        }

        Skeleton source = Skeleton.From(model.transform);
        if (source == null || Retargeter.MissingBone(source) != null)
        {
            Debug.LogError($"[Piano] {info.fbx}: incomplete skeleton" +
                           (source != null ? $" (missing {Retargeter.MissingBone(source)})" : " (no Hips/Neck1)"));
            return null;
        }

        var retargeter = new Retargeter(source, target);
        var sourceCurves = ReadCurves(original, source);
        if (sourceCurves.Count == 0)
        {
            Debug.LogError($"[Piano] {info.fbx}: no bone curves (check that the FBX Rig is Generic).");
            return null;
        }

        float end = info.end > 0 ? Mathf.Min(info.end, original.length) : original.length;
        float start = Mathf.Clamp(info.start, 0f, end - 0.2f);
        float fps = original.frameRate > 1f ? original.frameRate : 60f;
        int frames = Mathf.Max(2, Mathf.RoundToInt((end - start) * fps) + 1);

        var rotations = new Dictionary<string, Quaternion[]>();
        var positions = new Vector3[frames];
        foreach (Bone b in target.Bones) rotations[b.name] = new Quaternion[frames];

        for (int f = 0; f < frames; f++)
        {
            float t = start + (end - start) * f / (frames - 1);
            retargeter.Convert(sourceCurves, t, rotations, positions, f);
        }
        retargeter.CleanDisplacement(positions, info.keepSide, !info.loop);

        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        float[] times = new float[frames];
        for (int f = 0; f < frames; f++) times[f] = (end - start) * f / (frames - 1);

        foreach (Bone b in target.Bones)
        {
            Quaternion[] qs = rotations[b.name];
            for (int c = 0; c < 4; c++)
            {
                float[] v = new float[frames];
                for (int f = 0; f < frames; f++) v[f] = c == 0 ? qs[f].x : c == 1 ? qs[f].y : c == 2 ? qs[f].z : qs[f].w;
                bindings.Add(new EditorCurveBinding { path = b.path, type = typeof(Transform), propertyName = "m_LocalRotation." + "xyzw"[c] });
                curves.Add(Curve(times, v));
            }
        }
        for (int c = 0; c < 3; c++)
        {
            float[] v = new float[frames];
            for (int f = 0; f < frames; f++) v[f] = positions[f][c];
            bindings.Add(new EditorCurveBinding { path = target.Hips.path, type = typeof(Transform), propertyName = "m_LocalPosition." + "xyz"[c] });
            curves.Add(Curve(times, v));
        }

        var clipOut = new AnimationClip { name = info.state, frameRate = fps };
        AnimationUtility.SetEditorCurves(clipOut, bindings.ToArray(), curves.ToArray());
        clipOut.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clipOut);
        settings.loopTime = info.loop;
        settings.loopBlend = info.loop;
        AnimationUtility.SetAnimationClipSettings(clipOut, settings);

        string path = $"{ClipsFolder}/{info.state}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(clipOut, existing);
            EditorUtility.SetDirty(existing);
            clipOut = existing;
        }
        else AssetDatabase.CreateAsset(clipOut, path);

        Debug.Log($"[Piano] {info.state}: {end - start:0.0}s, {frames} frames, {target.Bones.Count} bones{(info.loop ? " (loop)" : "")}");
        return clipOut;
    }

    static AnimationCurve Curve(float[] times, float[] values)
    {
        var keys = new Keyframe[times.Length];
        for (int i = 0; i < times.Length; i++)
        {
            float slope;
            if (i == 0) slope = (values[1] - values[0]) / (times[1] - times[0]);
            else if (i == times.Length - 1) slope = (values[i] - values[i - 1]) / (times[i] - times[i - 1]);
            else slope = (values[i + 1] - values[i - 1]) / (times[i + 1] - times[i - 1]);
            keys[i] = new Keyframe(times[i], values[i], slope, slope);
        }
        return new AnimationCurve(keys);
    }

    // [0..3] quaternião, [4..6] posição da anca, [7..9] ângulos
    static Dictionary<string, AnimationCurve[]> ReadCurves(AnimationClip clip, Skeleton source)
    {
        var result = new Dictionary<string, AnimationCurve[]>();
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
        {
            if (b.type != typeof(Transform)) continue;
            Bone bone = source.ByPath(b.path);
            if (bone == null) continue;

            string p = b.propertyName;
            char axis = p[p.Length - 1];
            int k = "xyz".IndexOf(axis);
            int idx = -1;
            if (p.StartsWith("m_LocalRotation.")) idx = "xyzw".IndexOf(axis);
            else if (p.StartsWith("m_LocalPosition.") && bone == source.Hips) idx = k < 0 ? -1 : 4 + k;
            else if (p.StartsWith("localEulerAngles")) idx = k < 0 ? -1 : 7 + k;
            if (idx < 0) continue;

            if (!result.TryGetValue(bone.name, out AnimationCurve[] arr))
            {
                arr = new AnimationCurve[10];
                result[bone.name] = arr;
            }
            arr[idx] = AnimationUtility.GetEditorCurve(clip, b);
        }
        return result;
    }

    // ------------------------------------------------------------- Animator

    // um estado por animação, um trigger com o mesmo nome, Any State -> animação -> idle
    static void CreateController(Dictionary<string, AnimationClip> clips)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray()) sm.RemoveAnyStateTransition(t);
        foreach (ChildAnimatorState s in sm.states.ToArray()) sm.RemoveState(s.state);
        foreach (AnimatorControllerParameter p in ctrl.parameters.ToArray()) ctrl.RemoveParameter(p);

        ctrl.AddParameter(FeetOnGroundParameter, AnimatorControllerParameterType.Int);

        sm.anyStatePosition = new Vector3(20, 200, 0);
        sm.entryPosition = new Vector3(20, 40, 0);
        sm.exitPosition = new Vector3(20, 360, 0);
        AnimatorState idle = sm.AddState("idle", new Vector3(300, 40, 0));
        idle.motion = clips.TryGetValue("idle", out AnimationClip idleClip) ? idleClip : null;
        sm.defaultState = idle;

        int n = 0;
        foreach (var pair in clips)
        {
            if (pair.Key == "idle") continue;
            AnimatorState state = sm.AddState(pair.Key, new Vector3(n % 2 == 0 ? 300 : 560, 130 + (n / 2) * 70, 0));
            state.motion = pair.Value;
            n++;

            ctrl.AddParameter(pair.Key, AnimatorControllerParameterType.Trigger);
            AnimatorStateTransition go = sm.AddAnyStateTransition(state);
            go.AddCondition(AnimatorConditionMode.If, 0f, pair.Key);
            go.hasExitTime = false;
            go.hasFixedDuration = true;
            go.duration = 0.12f;
            go.canTransitionToSelf = true;

            AnimatorStateTransition back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.hasFixedDuration = true;
            back.duration = 0.25f;
        }
        EditorUtility.SetDirty(ctrl);
        Debug.Log($"[Piano] Animator Controller: {ControllerPath} ({clips.Count} states, one trigger per animation)");
    }

    // ------------------------------------------------------------- 2: scene

    public static void BuildGame(Transform character)
    {
        Scene scene = SceneManager.GetActiveScene();

        PianoGame game = AllInScene<PianoGame>().FirstOrDefault();
        if (game == null)
        {
            var obj = new GameObject("Piano");
            Undo.RegisterCreatedObjectUndo(obj, "Piano");
            game = obj.AddComponent<PianoGame>();
        }
        Transform track = game.transform;
        Undo.RecordObject(game, "Piano");

        // boneco
        Undo.RecordObject(character, "Piano");
        if (!character.gameObject.activeSelf) character.gameObject.SetActive(true);
        character.position = track.position;
        FaceTrack(character, track);

        // o mocap ao vivo e o Bake mexiam nos mesmos ossos: ficam desligados
        foreach (MonoBehaviour mb in character.GetComponentsInChildren<MonoBehaviour>(true))
        {
            string type = mb == null ? "" : mb.GetType().Name;
            if ((type == "SubjectScript" || type == "Bake") && mb.enabled)
            {
                Undo.RecordObject(mb, "Piano");
                mb.enabled = false;
                Debug.Log($"[Piano] disabled {type} on the character (it would fight the Animator)", mb);
            }
        }

        Animator animator = character.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(character.gameObject);
        Undo.RecordObject(animator, "Piano");
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        animator.avatar = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (animator.runtimeAnimatorController == null)
            Debug.LogWarning("[Piano] no Animator Controller yet: run 'Tools > Piano > 1 - Prepare animations'.");

        // pose de pé no editor (só visual; em jogo manda o Animator)
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipsFolder}/idle.anim");
        if (idle != null && PrefabUtility.GetCorrespondingObjectFromSource(character.gameObject) != null)
        {
            Undo.RegisterFullObjectHierarchyUndo(character.gameObject, "Piano");
            character.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
            idle.SampleAnimation(character.gameObject, 0f);
            character.SetPositionAndRotation(pos, rot);
        }

        // colliders só nos pés + rigidbody kinematic para os triggers funcionarem
        PlayerFeet feet = character.GetComponent<PlayerFeet>();
        if (feet == null) feet = Undo.AddComponent<PlayerFeet>(character.gameObject);
        Undo.RegisterFullObjectHierarchyUndo(character.gameObject, "Piano");
        feet.EnsureRigidbody();
        foreach (GameObject go in feet.CreateColliders()) Undo.RegisterCreatedObjectUndo(go, "Piano");
        EditorUtility.SetDirty(feet);

        PlayerController player = character.GetComponent<PlayerController>();
        if (player == null) player = Undo.AddComponent<PlayerController>(character.gameObject);
        Undo.RecordObject(player, "Piano");
        player.game = game;
        player.track = track;
        player.animator = animator;
        player.feet = feet;

        // jogo
        game.player = character;
        game.controller = player;
        game.playerFeet = feet;
        if (game.tileModels == null || game.tileModels.Length != 3) game.tileModels = new GameObject[3];
        if (game.pressedTileModels == null || game.pressedTileModels.Length != 3) game.pressedTileModels = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            game.tileModels[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{TilesFolder}/Piano_Tile_{i + 1}.fbx");
            game.pressedTileModels[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{TilesFolder}/Piano_Tile_{i + 1}_Pressed.fbx");
            if (game.tileModels[i] == null)
                Debug.LogWarning($"[Piano] {TilesFolder}/Piano_Tile_{i + 1}.fbx not found (a cube will be used).");
        }

        CreateFolder(MaterialsFolder);
        game.tileMaterial = GetMaterial("Tile", new Color(0.05f, 0.05f, 0.07f), Color.black, 0.75f);
        game.effectMaterial = GetEffectMaterial();
        game.hitMaterial = GetMaterial("Tile Hit", new Color(0.35f, 0.8f, 1f), new Color(0.15f, 0.45f, 0.8f), 0.6f);
        game.missMaterial = GetMaterial("Tile Miss", new Color(0.9f, 0.15f, 0.15f), new Color(0.5f, 0.02f, 0.02f), 0.4f);

        BuildFloor(game);
        CreateTexts(game);
        PlaceCamera(track, false);
        ImportTmpResources();

        // bonecos repetidos só atrapalham
        foreach (Transform t in AllInScene<Transform>().ToArray())
        {
            if (t == null || t == character || t.parent != null || !t.gameObject.activeSelf) continue;
            if (!(t.name.StartsWith("ViconActor") || t.name.StartsWith("ViconMale"))) continue;
            Undo.RecordObject(t.gameObject, "Piano");
            t.gameObject.SetActive(false);
            Debug.Log($"[Piano] disabled '{t.name}' (duplicate character)", t);
        }

        EditorUtility.SetDirty(game);
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = game.gameObject;
        Debug.Log($"[Piano] game built with '{character.name}'. Press Play.\n" +
                  "A S D F G = lanes, Q / E = stretch leg, W = jump, R = restart, M = menu.", game);

        // menu inicial e HUD (imagens em Assets/Art/UI)
        PianoUISetup.Build(game);
    }

    // roda o boneco para ficar de frente para os tiles (a esquerda dele = lane da esquerda)
    static void FaceTrack(Transform character, Transform track)
    {
        Transform left = Find(character, "LeftUpLeg");
        Transform right = Find(character, "RightUpLeg");
        if (left == null || right == null)
        {
            character.rotation = Quaternion.LookRotation(-track.forward, track.up);
            return;
        }
        Vector3 current = Vector3.ProjectOnPlane(left.position - right.position, track.up);
        if (current.sqrMagnitude < 1e-6f) return;
        float angle = Vector3.SignedAngle(current, -track.right, track.up);
        character.rotation = Quaternion.AngleAxis(angle, track.up) * character.rotation;
    }

    // 3 lanes, as teclas onde se pisa (com collider trigger) e a linha de chegada
    static void BuildFloor(PianoGame game)
    {
        Transform old = game.transform.Find("Floor");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var floor = new GameObject("Floor");
        Undo.RegisterCreatedObjectUndo(floor, "Piano");
        floor.transform.SetParent(game.transform, false);

        float L = PianoGame.LaneWidth;
        float zStart = PianoGame.EndZ;
        float zEnd = PianoGame.LeadTime * game.speed + 1f;
        float length = zEnd - zStart;
        float zMid = (zStart + zEnd) * 0.5f;

        Material baseMat = GetMaterial("Floor", new Color(0.08f, 0.08f, 0.1f), Color.black, 0.2f);
        Material laneMat = GetMaterial("Lane", new Color(0.9f, 0.9f, 0.93f), Color.black, 0.3f);
        Material keyMat = GetMaterial("Key", new Color(0.55f, 0.57f, 0.62f), Color.black, 0.5f, true);
        Material lineMat = GetMaterial("Line", new Color(0.3f, 0.8f, 1f), new Color(0.2f, 0.6f, 1f), 0.5f);

        Block(floor.transform, "Base", new Vector3(0f, -0.011f, zMid), new Vector3(3f * L + 0.4f, 0.02f, length), baseMat);
        for (int i = 0; i < 3; i++)
            Block(floor.transform, $"Lane {i + 1}", new Vector3((i - 1) * L, -0.005f, zMid), new Vector3(L * 0.96f, 0.01f, length), laneMat);

        // a tecla que se vê é mais estreita, mas o collider apanha a lane toda
        // (cada pé só conta numa tecla: a que estiver mais perto do centro do pé)
        float keyWidth = L * PianoGame.KeyWidthFactor;
        var keys = new Renderer[3];
        for (int i = 0; i < 3; i++)
        {
            keys[i] = Block(floor.transform, $"Key {i + 1}", new Vector3((i - 1) * L, 0.001f, 0f), new Vector3(keyWidth, 0.012f, 1.2f), keyMat);

            // sensor da tecla: um pé pousado toca aqui, um pé no ar não
            var sensor = new GameObject($"Key Sensor {i + 1}");
            Undo.RegisterCreatedObjectUndo(sensor, "Piano");
            sensor.transform.SetParent(floor.transform, false);
            sensor.transform.localPosition = new Vector3((i - 1) * L, 0f, 0f);
            BoxCollider box = sensor.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, -0.04f, 0f);   // de -0.12 m a +0.04 m
            box.size = new Vector3(L, 0.16f, 1.4f);
            sensor.AddComponent<FloorKey>().lane = i;
        }
        Block(floor.transform, "Line", new Vector3(0f, 0.004f, 0.62f), new Vector3(3f * L, 0.014f, 0.05f), lineMat);

        game.keys = keys;
    }

    static Renderer Block(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(go, "Piano");
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    // cria o material se não existir (se existir não mexe, para guardar as tuas mudanças)
    static Material GetMaterial(string name, Color color, Color emission, float smoothness, bool emissive = false)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        CreateFolder(MaterialsFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        m = new Material(shader) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (emissive || emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // material das faíscas de acerto (partículas do URP com uma bolinha suave como textura)
    static Material GetEffectMaterial()
    {
        string path = $"{MaterialsFolder}/Hit Effect.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        Texture2D tex = HitEffects.CreateTexture();
        m = HitEffects.CreateMaterial(tex);
        if (m == null) return null;
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.AddObjectToAsset(tex, m);
        AssetDatabase.SaveAssets();
        return m;
    }

    // câmara atrás do jogador; só mexe se estiver no sítio de origem (ou pelo menu)
    static void PlaceCamera(Transform track, bool force)
    {
        Camera cam = Camera.main;
        if (cam == null) cam = AllInScene<Camera>().FirstOrDefault();
        if (cam == null) return;

        bool isDefault = (cam.transform.position - new Vector3(0f, 1f, -10f)).sqrMagnitude < 0.01f;
        if (!force && !isDefault) return;

        Undo.RecordObject(cam.transform, "Piano");
        Undo.RecordObject(cam, "Piano");
        cam.transform.position = track.TransformPoint(new Vector3(0f, 2.3f, -2.9f));
        cam.transform.rotation = Quaternion.LookRotation(track.TransformPoint(new Vector3(0f, 0.5f, 4.5f)) - cam.transform.position, track.up);
        cam.fieldOfView = 60f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 200f);
    }

    // textos como objetos da cena (dá para mexer no Inspector)
    static void CreateTexts(PianoGame game)
    {
        Canvas canvas = AllInScene<Canvas>().FirstOrDefault(c => c.name == GameTexts.CanvasName);
        if (canvas == null)
        {
            var go = new GameObject(GameTexts.CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Piano");
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        // o texto "Status" existia numa versão antiga do jogo
        Transform old = canvas.transform.Find("Status");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        if (game.scoreText == null) game.scoreText = Register(GameTexts.Score(canvas.transform));
        if (game.messageText == null) game.messageText = Register(GameTexts.Message(canvas.transform));
        if (game.centerText == null) game.centerText = Register(GameTexts.Center(canvas.transform));
    }

    static TMP_Text Register(TMP_Text t)
    {
        Undo.RegisterCreatedObjectUndo(t.gameObject, "Piano");
        return t;
    }

    // os textos precisam das fontes do TextMeshPro (TMP Essential Resources)
    static void ImportTmpResources()
    {
        if (Resources.Load("TMP Settings") != null) return;

        if (EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources"))
            Debug.Log("[Piano] importing TextMeshPro fonts (TMP Essential Resources)...");
        else
            Debug.LogWarning("[Piano] TextMeshPro resources missing: Window > TextMeshPro > Import TMP Essential Resources.");
    }

    // ------------------------------------------------------------- util

    static void CreateFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        CreateFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    static IEnumerable<T> AllInScene<T>() where T : Component
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) yield break;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T c in root.GetComponentsInChildren<T>(true))
                yield return c;
    }

    public static string Clean(string name) => Regex.Replace(name, @"(\s*\(\d+\)|[ _.]\d+)$", "");

    static Transform Find(Transform root, string name)
    {
        if (Clean(root.name) == name) return root;
        foreach (Transform child in root)
        {
            Transform r = Find(child, name);
            if (r != null) return r;
        }
        return null;
    }
}
