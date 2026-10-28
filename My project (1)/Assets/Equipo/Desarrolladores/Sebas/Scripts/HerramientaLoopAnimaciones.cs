#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Activa "Loop Time" en las animaciones del Silbon que se reproducen en bucle (correr, gatear, idle, caminar).
/// Emerge y Atrapa se dejan sin loop a proposito: deben pasar una sola vez.
///
/// - Llaneritos/Herramientas/Loopear animaciones del Silbon: aplica a la lista fija de abajo.
/// - Clic derecho sobre FBX en Project > Llaneritos/Loopear clips seleccionados: aplica a los que elijas.
/// </summary>
public static class HerramientaLoopAnimaciones
{
    static readonly string[] AnimacionesEnBucle =
    {
        "Assets/Silbon/Animaciones silbon/zombie run.fbx",        // Run (persecucion)
        "Assets/Silbon/Animaciones silbon/running crawl.fbx",     // Crawl (acecho)
        "Assets/_Characters_Silbon/Animations/Silbon_Idle_001.fbx",
        "Assets/_Characters_Silbon/Animations/Silbon_Caminando_Loop.fbx", // Walk (tambien el saco)
    };

    [MenuItem("Llaneritos/Herramientas/Loopear animaciones del Silbon")]
    static void LoopearSilbon()
    {
        int cambiados = 0;
        foreach (var ruta in AnimacionesEnBucle)
            if (Loopear(ruta)) cambiados++;
        Debug.Log($"[Loop Animaciones] Listo: {cambiados} FBX actualizados (los demas ya estaban en loop o no se encontraron).");
    }

    [MenuItem("Assets/Llaneritos/Loopear clips seleccionados")]
    static void LoopearSeleccionados()
    {
        int cambiados = 0;
        foreach (var guid in Selection.assetGUIDs)
            if (Loopear(AssetDatabase.GUIDToAssetPath(guid))) cambiados++;
        Debug.Log($"[Loop Animaciones] Listo: {cambiados} FBX seleccionados actualizados.");
    }

    [MenuItem("Assets/Llaneritos/Loopear clips seleccionados", true)]
    static bool ValidarSeleccion()
    {
        foreach (var guid in Selection.assetGUIDs)
            if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is ModelImporter) return true;
        return false;
    }

    /// <summary>Activa Loop Time en todos los clips del FBX. Devuelve true si cambio algo.</summary>
    static bool Loopear(string ruta)
    {
        var importer = AssetImporter.GetAtPath(ruta) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[Loop Animaciones] No se encontro el FBX: {ruta}");
            return false;
        }

        // Si nunca se edito el import, los clips viven en defaultClipAnimations
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning($"[Loop Animaciones] '{ruta}' no tiene clips de animacion.");
            return false;
        }

        bool cambio = false;
        foreach (var clip in clips)
        {
            if (clip.loopTime) continue;
            clip.loopTime = true;
            cambio = true;
        }
        if (!cambio) return false;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[Loop Animaciones] Loop activado: {ruta}");
        return true;
    }
}
#endif
