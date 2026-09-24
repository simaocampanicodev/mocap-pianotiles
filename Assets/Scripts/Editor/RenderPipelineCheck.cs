using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// sem render pipeline (URP) ligado os materiais ficam todos roxos
// ao abrir o projeto verifica isso e volta a ligar o URP sozinho
[InitializeOnLoad]
static class RenderPipelineCheck
{
    const string PreferredAsset = "Assets/Settings/PC_RPAsset.asset";

    static RenderPipelineCheck() => EditorApplication.delayCall += () => Fix(false);

    [MenuItem("Tools/Piano/Fix purple materials (URP)", priority = 50)]
    static void FixMenu() => Fix(true);

    static void Fix(bool verbose)
    {
        if (GraphicsSettings.defaultRenderPipeline != null)
        {
            if (verbose) Debug.Log($"[Piano] URP is already on ({GraphicsSettings.defaultRenderPipeline.name}).");
            return;
        }

        RenderPipelineAsset asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PreferredAsset);
        if (asset == null)
        {
            // outro asset do URP qualquer que exista no projeto
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) break;
            }
        }
        if (asset == null)
        {
            Debug.LogError("[Piano] no URP asset in the project: materials will stay purple. " +
                           "Create one in Assets > Create > Rendering > URP Asset (with Universal Renderer).");
            return;
        }

        GraphicsSettings.defaultRenderPipeline = asset;
        EditorUtility.SetDirty(GraphicsSettings.GetGraphicsSettings());
        AssetDatabase.SaveAssets();
        Debug.Log($"[Piano] render pipeline was empty (purple materials): set to {AssetDatabase.GetAssetPath(asset)}.");
    }
}
