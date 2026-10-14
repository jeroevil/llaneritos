using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

/// <summary>
/// Herramienta de Editor para configurar y reparar automaticamente el Animator Controller del Silbon:
/// - Convierte 'running crawl.fbx' a Generic rig (para que coincida con el rig de silvon unity).
/// - Extrae 'crawl.anim' con curvas de huesos reales (Transform) y bucle habilitado.
/// - Configura los Animator Controllers ('Nuevo control silbon' y 'New Animator Controller').
/// - Conecta la animacion de 'running crawl' al estado Crawl (acecho peces 4 a 9).
/// - Conecta la animacion de 'zombie run' al estado Run (persecucion pez 10).
/// - Conecta la animacion de 'zombie walk' al estado Walk (calma / caminata).
/// - Asigna el Animator al GameObject 'silvon unity' en la escena y lo vincula al FishingSilbonDirector.
/// </summary>
[InitializeOnLoad]
public static class SilbonAnimatorSetupTool
{
    private const string RUTA_CONTROLLER_PRINCIPAL = "Assets/Silbon/Animaciones silbon/Nuevo control silbon.controller";
    private const string RUTA_CONTROLLER_SECUNDARIO = "Assets/Silbon/Animaciones silbon/New Animator Controller.controller";
    private const string RUTA_FBX_CRAWL = "Assets/Silbon/Animaciones silbon/running crawl.fbx";
    private const string RUTA_FBX_RUN = "Assets/Silbon/Animaciones silbon/zombie run.fbx";
    private const string RUTA_FBX_WALK = "Assets/Silbon/Animaciones silbon/zombie walk.fbx";
    private const string RUTA_ANIM_WALK = "Assets/Silbon/Animaciones silbon/zombie walk.anim";
    private const string RUTA_ANIM_CRAWL = "Assets/Silbon/Animaciones silbon/crawl.anim";
    private const string RUTA_ANIM_RUN = "Assets/Silbon/Animaciones silbon/run.anim";

    [InitializeOnLoadMethod]
    private static void AutoVerificarYReparar()
    {
        EditorApplication.delayCall += () =>
        {
            var clipCrawl = AssetDatabase.LoadAssetAtPath<AnimationClip>(RUTA_ANIM_CRAWL);
            if (!EsClipValido(clipCrawl))
            {
                Debug.Log("[Silbon Tool] 'crawl.anim' no es valido o no tiene curvas genericas. Ejecutando reparacion automatica...");
                ConfigurarAnimatorCompleto();
            }
        };
    }

    [MenuItem("Llaneritos/Configurar Animator del Silbón (running crawl)", false, 1)]
    [MenuItem("GameObject/Silbón/Configurar Animator del Silbón", false, 10)]
    public static void ConfigurarAnimatorCompleto()
    {
        Debug.Log("[Silbon Tool] Iniciando configuracion automatica de animaciones del Silbon...");

        // 1. Asegurar importadores FBX como Genericos (No Avatar)
        AsegurarFbxGenerico(RUTA_FBX_CRAWL);
        AsegurarFbxGenerico(RUTA_FBX_RUN);

        // 2. Extraer o cargar clips con curvas de huesos reales
        AnimationClip clipCrawl = ObtenerOGenerarClip(RUTA_FBX_CRAWL, RUTA_ANIM_CRAWL);
        AnimationClip clipRun = ObtenerOGenerarClip(RUTA_FBX_RUN, RUTA_ANIM_RUN);
        AnimationClip clipWalk = AssetDatabase.LoadAssetAtPath<AnimationClip>(RUTA_ANIM_WALK);

        if (clipWalk == null || !EsClipValido(clipWalk))
        {
            clipWalk = ObtenerOGenerarClip(RUTA_FBX_WALK, RUTA_ANIM_WALK);
        }

        if (clipCrawl == null || clipRun == null || clipWalk == null)
        {
            Debug.LogError($"[Silbon Tool] Error: No se pudieron preparar todos los clips. Crawl: {clipCrawl != null}, Run: {clipRun != null}, Walk: {clipWalk != null}");
            return;
        }

        // 3. Configurar ambos AnimatorControllers (el que usa la escena y el alternativo)
        ConfigurarUnController(RUTA_CONTROLLER_PRINCIPAL, clipWalk, clipCrawl, clipRun);
        ConfigurarUnController(RUTA_CONTROLLER_SECUNDARIO, clipWalk, clipCrawl, clipRun);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 4. Asignar en escena
        var controllerActivo = AssetDatabase.LoadAssetAtPath<AnimatorController>(RUTA_CONTROLLER_PRINCIPAL)
                            ?? AssetDatabase.LoadAssetAtPath<AnimatorController>(RUTA_CONTROLLER_SECUNDARIO);

        if (controllerActivo != null)
        {
            VincularASilbonEnEscena(controllerActivo);
        }

        Debug.Log("[Silbon Tool] ¡Animaciones configuradas exitosamente! Crawl (running crawl), Run (zombie run), Walk (zombie walk).");
    }

    private static void AsegurarFbxGenerico(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            dirty = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[Silbon Tool] FBX '{fbxPath}' reimportado como Generic (NoAvatar) para coincidir con el Silbón.");
        }
    }

    private static bool EsClipValido(AnimationClip clip)
    {
        if (clip == null) return false;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        if (bindings == null || bindings.Length == 0) return false;

        foreach (var b in bindings)
        {
            if (b.type == typeof(Transform)) return true;
        }
        return false;
    }

    private static AnimationClip ObtenerOGenerarClip(string fbxPath, string rutaAnimDestino)
    {
        // Si el archivo .anim ya existe y tiene curvas genericas validas, lo usamos
        var clipExistente = AssetDatabase.LoadAssetAtPath<AnimationClip>(rutaAnimDestino);
        if (EsClipValido(clipExistente))
        {
            return clipExistente;
        }

        // Si no es valido, extraer desde el FBX
        AnimationClip clipEncontrado = null;
        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var sub in subAssets)
        {
            if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                clipEncontrado = clip;
                break;
            }
        }

        if (clipEncontrado != null)
        {
            if (File.Exists(rutaAnimDestino))
            {
                AssetDatabase.DeleteAsset(rutaAnimDestino);
            }

            AnimationClip copia = Object.Instantiate(clipEncontrado);
            copia.name = Path.GetFileNameWithoutExtension(rutaAnimDestino);

            var settings = AnimationUtility.GetAnimationClipSettings(copia);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copia, settings);

            AssetDatabase.CreateAsset(copia, rutaAnimDestino);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Silbon Tool] Generado clip genérico '{copia.name}' en: {rutaAnimDestino}");
            return copia;
        }

        return null;
    }

    private static void ConfigurarUnController(string rutaController, AnimationClip walk, AnimationClip crawl, AnimationClip run)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(rutaController);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(rutaController);
            Debug.Log($"[Silbon Tool] Creado nuevo AnimatorController en: {rutaController}");
        }

        AsegurarParametro(controller, "Acechando", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Corriendo", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Estado", AnimatorControllerParameterType.Int);

        var rootStateMachine = controller.layers[0].stateMachine;

        AnimatorState stateWalk = ObtenerOCrearEstado(rootStateMachine, "Walk", walk, new Vector3(300, 50, 0));
        AnimatorState stateCrawl = ObtenerOCrearEstado(rootStateMachine, "Crawl", crawl, new Vector3(300, 160, 0));
        AnimatorState stateRun = ObtenerOCrearEstado(rootStateMachine, "Run", run, new Vector3(300, 270, 0));

        rootStateMachine.defaultState = stateWalk;

        ConfigurarTransiciones(rootStateMachine, stateWalk, stateCrawl, stateRun);

        EditorUtility.SetDirty(controller);
    }

    private static void AsegurarParametro(AnimatorController controller, string nombre, AnimatorControllerParameterType tipo)
    {
        foreach (var p in controller.parameters)
        {
            if (p.name == nombre) return;
        }
        controller.AddParameter(nombre, tipo);
    }

    private static AnimatorState ObtenerOCrearEstado(AnimatorStateMachine sm, string nombre, AnimationClip clip, Vector3 posicion)
    {
        foreach (var childState in sm.states)
        {
            if (childState.state.name == nombre)
            {
                childState.state.motion = clip;
                return childState.state;
            }
        }

        var nuevoEstado = sm.AddState(nombre, posicion);
        nuevoEstado.motion = clip;
        return nuevoEstado;
    }

    private static void ConfigurarTransiciones(AnimatorStateMachine sm, AnimatorState walk, AnimatorState crawl, AnimatorState run)
    {
        while (walk.transitions.Length > 0) walk.RemoveTransition(walk.transitions[0]);
        while (crawl.transitions.Length > 0) crawl.RemoveTransition(crawl.transitions[0]);
        while (run.transitions.Length > 0) run.RemoveTransition(run.transitions[0]);
        while (sm.anyStateTransitions.Length > 0) sm.RemoveAnyStateTransition(sm.anyStateTransitions[0]);

        // Walk -> Crawl (cuando Acechando = true)
        var walkToCrawl = walk.AddTransition(crawl);
        walkToCrawl.hasExitTime = false;
        walkToCrawl.duration = 0.2f;
        walkToCrawl.AddCondition(AnimatorConditionMode.If, 0, "Acechando");

        // Crawl -> Walk (cuando Acechando = false)
        var crawlToWalk = crawl.AddTransition(walk);
        crawlToWalk.hasExitTime = false;
        crawlToWalk.duration = 0.25f;
        crawlToWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "Acechando");

        // Walk -> Run (cuando Corriendo = true)
        var walkToRun = walk.AddTransition(run);
        walkToRun.hasExitTime = false;
        walkToRun.duration = 0.2f;
        walkToRun.AddCondition(AnimatorConditionMode.If, 0, "Corriendo");

        // Run -> Walk (cuando Corriendo = false)
        var runToWalk = run.AddTransition(walk);
        runToWalk.hasExitTime = false;
        runToWalk.duration = 0.25f;
        runToWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "Corriendo");

        // Crawl -> Run (cuando Corriendo = true)
        var crawlToRun = crawl.AddTransition(run);
        crawlToRun.hasExitTime = false;
        crawlToRun.duration = 0.15f;
        crawlToRun.AddCondition(AnimatorConditionMode.If, 0, "Corriendo");

        // Run -> Crawl (cuando Corriendo = false && Acechando = true)
        var runToCrawl = run.AddTransition(crawl);
        runToCrawl.hasExitTime = false;
        runToCrawl.duration = 0.2f;
        runToCrawl.AddCondition(AnimatorConditionMode.IfNot, 0, "Corriendo");
        runToCrawl.AddCondition(AnimatorConditionMode.If, 0, "Acechando");
    }

    private static void VincularASilbonEnEscena(AnimatorController controller)
    {
        var silbon = GameObject.Find("silvon unity") ?? GameObject.Find("silbon");
        if (silbon != null)
        {
            var animator = silbon.GetComponent<Animator>();
            if (animator == null) animator = silbon.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.avatar = null; // Rig generico
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(silbon);

            var director = Object.FindAnyObjectByType<FishingSilbonDirector>();
            if (director != null)
            {
                director.animatorSilbon = animator;
                EditorUtility.SetDirty(director);
            }

            Debug.Log($"[Silbon Tool] Asignado AnimatorController '{controller.name}' a '{silbon.name}' y vinculado a FishingSilbonDirector.");
        }
    }
}
#endif
