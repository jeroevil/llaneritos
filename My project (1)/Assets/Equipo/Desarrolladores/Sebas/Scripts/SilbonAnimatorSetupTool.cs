using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

/// <summary>
/// Herramienta de Editor para configurar y reparar el Animator del Silbón con las animaciones de Mixamo y MoCap:
/// - Stalking / Acecho (Peces 4 a 9): running crawl (Mixamo)
/// - Idle: Silbon_Idle_001
/// - Walk: Silbon_Caminando_Loop / zombie walk
/// - Emerge: Silbon_Emerge_003 (Clímax pez 10)
/// - Run: zombie run (Persecución pez 10)
/// - Atrapa: Silbon_Atrapa (Jumpscare / captura)
///
/// Además:
/// - Asegura que todos los FBX estén configurados en Humanoid con Avatar válido (¡elimina la pose T por completo!).
/// - Asigna el Avatar Humanoid al GameObject del Silbón en la escena.
/// - Configura las transiciones para que el acecho use running crawl.
/// - Marca la escena como sucia y la guarda automáticamente.
/// </summary>
public static class SilbonAnimatorSetupTool
{
    private const string RUTA_FBX_MODELO_UNITY = "Assets/Silbon/Modelo/silvon unity.fbx";
    private const string RUTA_FBX_MODELO_RIGGED = "Assets/_Characters_Silbon/FBX/silvon rigged.fbx";
    private const string RUTA_FBX_IDLE = "Assets/_Characters_Silbon/Animations/Silbon_Idle_001.fbx";
    private const string RUTA_FBX_WALK = "Assets/_Characters_Silbon/Animations/Silbon_Caminando_Loop.fbx";
    private const string RUTA_FBX_EMERGE = "Assets/_Characters_Silbon/Animations/Silbon_Emerge_003.fbx";
    private const string RUTA_FBX_ATRAPA = "Assets/_Characters_Silbon/Animations/Silbon_Atrapa.fbx";
    private const string RUTA_FBX_WALK_LONG = "Assets/_Characters_Silbon/Animations/Silbon_Caminando_005.fbx";
    private const string RUTA_FBX_ZOMBIE_RUN = "Assets/Silbon/Animaciones silbon/zombie run.fbx";
    private const string RUTA_FBX_ZOMBIE_WALK = "Assets/Silbon/Animaciones silbon/zombie walk.fbx";
    private const string RUTA_FBX_CRAWL = "Assets/Silbon/Animaciones silbon/running crawl.fbx";

    private const string RUTA_CONTROLLER_MOCAP = "Assets/_Characters_Silbon/Animations/Controllers/Silbon.controller";
    private const string RUTA_CONTROLLER_ESCENA = "Assets/Silbon/Animaciones silbon/Nuevo control silbon.controller";

    [MenuItem("Llaneritos/Configurar Animator del Silbón (Animaciones y Avatar Humanoid)", false, 1)]
    [MenuItem("GameObject/Silbón/Configurar Animator (MoCap y Mixamo)", false, 10)]
    public static void ConfigurarAnimatorCompleto()
    {
        Debug.Log("[Silbon Tool] === Iniciando configuración de animaciones y Avatar del Silbón ===");

        // 1. Asegurar que modelos y animaciones estén en Humanoid con Avatar creado
        AsegurarFbxHumanoid(RUTA_FBX_MODELO_UNITY, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_MODELO_RIGGED, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_CRAWL, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_ZOMBIE_RUN, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_ZOMBIE_WALK, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_IDLE, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_WALK, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_EMERGE, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_ATRAPA, crearAvatar: true);
        AsegurarFbxHumanoid(RUTA_FBX_WALK_LONG, crearAvatar: true);

        // 2. Ajustar loops
        ConfigurarLoopClip(RUTA_FBX_IDLE, true);
        ConfigurarLoopClip(RUTA_FBX_CRAWL, true);
        ConfigurarLoopClip(RUTA_FBX_ZOMBIE_RUN, true);
        ConfigurarLoopClip(RUTA_FBX_ZOMBIE_WALK, true);
        ConfigurarLoopClip(RUTA_FBX_WALK, true);
        ConfigurarLoopClip(RUTA_FBX_EMERGE, false);
        ConfigurarLoopClip(RUTA_FBX_ATRAPA, false);

        // 3. Cargar Avatar Humanoid (priorizar el del modelo en escena)
        Avatar avatarSilbon = ObtenerAvatarDeFbx(RUTA_FBX_MODELO_UNITY)
                           ?? ObtenerAvatarDeFbx(RUTA_FBX_MODELO_RIGGED)
                           ?? ObtenerAvatarDeFbx(RUTA_FBX_CRAWL)
                           ?? ObtenerAvatarDeFbx(RUTA_FBX_ZOMBIE_RUN);

        if (avatarSilbon == null)
        {
            Debug.LogError("[Silbon Tool] No se pudo encontrar un Avatar Humanoid para el Silbón.");
        }
        else
        {
            Debug.Log($"[Silbon Tool] Avatar asignado: '{avatarSilbon.name}' (Válido: {avatarSilbon.isValid}, Human: {avatarSilbon.isHuman})");
        }

        // 4. Cargar AnimationClips
        AnimationClip clipCrawl = ObtenerClipDeFbx(RUTA_FBX_CRAWL, "running crawl");
        AnimationClip clipIdle = ObtenerClipDeFbx(RUTA_FBX_IDLE, "Silbon_Idle_001") ?? ObtenerClipDeFbx(RUTA_FBX_MODELO_UNITY, "Idle");
        AnimationClip clipWalk = ObtenerClipDeFbx(RUTA_FBX_WALK, "Silbon_Caminando_Loop") ?? ObtenerClipDeFbx(RUTA_FBX_ZOMBIE_WALK, "zombie walk");
        AnimationClip clipEmerge = ObtenerClipDeFbx(RUTA_FBX_EMERGE, "Silbon_Emerge_003");
        AnimationClip clipRun = ObtenerClipDeFbx(RUTA_FBX_ZOMBIE_RUN, "zombie run") ?? ObtenerClipDeFbx(RUTA_FBX_WALK_LONG, "Silbon_Caminando_005");
        AnimationClip clipAtrapa = ObtenerClipDeFbx(RUTA_FBX_ATRAPA, "Silbon_Atrapa");

        if (clipCrawl == null) Debug.LogWarning($"[Silbon Tool] Clip Crawl no encontrado en {RUTA_FBX_CRAWL}");
        if (clipIdle == null) Debug.LogWarning($"[Silbon Tool] Clip Idle no encontrado en {RUTA_FBX_IDLE}");
        if (clipWalk == null) Debug.LogWarning($"[Silbon Tool] Clip Walk no encontrado en {RUTA_FBX_WALK}");
        if (clipEmerge == null) Debug.LogWarning($"[Silbon Tool] Clip Emerge no encontrado en {RUTA_FBX_EMERGE}");
        if (clipRun == null) Debug.LogWarning($"[Silbon Tool] Clip Run no encontrado en {RUTA_FBX_ZOMBIE_RUN}");
        if (clipAtrapa == null) Debug.LogWarning($"[Silbon Tool] Clip Atrapa no encontrado en {RUTA_FBX_ATRAPA}");

        // 5. Configurar los Animator Controllers
        ConfigurarController(RUTA_CONTROLLER_MOCAP, clipIdle, clipWalk, clipEmerge, clipRun, clipAtrapa, clipCrawl);
        ConfigurarController(RUTA_CONTROLLER_ESCENA, clipIdle, clipWalk, clipEmerge, clipRun, clipAtrapa, clipCrawl);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 6. Asignar en la escena activa
        var controllerActivo = AssetDatabase.LoadAssetAtPath<AnimatorController>(RUTA_CONTROLLER_ESCENA)
                            ?? AssetDatabase.LoadAssetAtPath<AnimatorController>(RUTA_CONTROLLER_MOCAP);

        if (controllerActivo != null)
        {
            VincularASilbonEnEscena(controllerActivo, avatarSilbon);
        }

        Debug.Log("[Silbon Tool] ¡Configuración completada con éxito! Ya no estará en Pose T y usará running crawl al acechar.");
    }

    private static void AsegurarFbxHumanoid(string fbxPath, bool crearAvatar)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            dirty = true;
        }

        if (crearAvatar && importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[Silbon Tool] FBX '{fbxPath}' configurado como Humanoid.");
        }
    }

    private static void ConfigurarLoopClip(string fbxPath, bool loop)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips != null && clips.Length > 0)
        {
            bool modified = false;
            foreach (var clip in clips)
            {
                if (clip.loopTime != loop)
                {
                    clip.loopTime = loop;
                    modified = true;
                }
            }
            if (modified)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
            }
        }
    }

    private static Avatar ObtenerAvatarDeFbx(string fbxPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var obj in assets)
        {
            if (obj is Avatar av && av.isValid) return av;
        }
        return null;
    }

    private static AnimationClip ObtenerClipDeFbx(string fbxPath, string preferedName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        AnimationClip fallback = null;
        foreach (var obj in assets)
        {
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                if (!string.IsNullOrEmpty(preferedName) && clip.name.IndexOf(preferedName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip;
                if (fallback == null) fallback = clip;
            }
        }
        return fallback;
    }

    private static void ConfigurarController(string rutaController, AnimationClip idle, AnimationClip walk, AnimationClip emerge, AnimationClip run, AnimationClip atrapa, AnimationClip crawl)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(rutaController);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(rutaController);
            Debug.Log($"[Silbon Tool] Creado nuevo AnimatorController en: {rutaController}");
        }

        // Parámetros
        AsegurarParametro(controller, "Acechando", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Emergiendo", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Corriendo", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Atrapando", AnimatorControllerParameterType.Bool);
        AsegurarParametro(controller, "Estado", AnimatorControllerParameterType.Int);

        var rootStateMachine = controller.layers[0].stateMachine;

        // Estados
        AnimatorState stateIdle = ObtenerOCrearEstado(rootStateMachine, "Idle", idle, new Vector3(300, 50, 0));
        AnimatorState stateCrawl = ObtenerOCrearEstado(rootStateMachine, "Crawl", crawl ?? walk, new Vector3(300, 150, 0));
        AnimatorState stateWalk = ObtenerOCrearEstado(rootStateMachine, "Walk", walk, new Vector3(550, 150, 0));
        AnimatorState stateEmerge = ObtenerOCrearEstado(rootStateMachine, "Emerge", emerge, new Vector3(300, 250, 0));
        AnimatorState stateRun = ObtenerOCrearEstado(rootStateMachine, "Run", run, new Vector3(300, 350, 0));
        AnimatorState stateAtrapa = ObtenerOCrearEstado(rootStateMachine, "Atrapa", atrapa, new Vector3(300, 450, 0));

        // Ajustar velocidades
        if (stateRun != null) stateRun.speed = 1.0f;
        if (stateEmerge != null) stateEmerge.speed = 1.0f;
        if (stateWalk != null) stateWalk.speed = 1.0f;
        if (stateCrawl != null) stateCrawl.speed = 1.2f;

        rootStateMachine.defaultState = stateIdle;

        // Limpiar transiciones previas
        LimpiarTransiciones(stateIdle);
        LimpiarTransiciones(stateCrawl);
        LimpiarTransiciones(stateWalk);
        LimpiarTransiciones(stateEmerge);
        LimpiarTransiciones(stateRun);
        LimpiarTransiciones(stateAtrapa);
        while (rootStateMachine.anyStateTransitions.Length > 0)
            rootStateMachine.RemoveAnyStateTransition(rootStateMachine.anyStateTransitions[0]);

        // --- Transiciones ---

        // Idle <-> Crawl (Acecho prioritario con running crawl para peces 4..9)
        var idleToCrawl = stateIdle.AddTransition(stateCrawl);
        idleToCrawl.hasExitTime = false;
        idleToCrawl.duration = 0.2f;
        idleToCrawl.AddCondition(AnimatorConditionMode.If, 0, "Acechando");

        var crawlToIdle = stateCrawl.AddTransition(stateIdle);
        crawlToIdle.hasExitTime = false;
        crawlToIdle.duration = 0.2f;
        crawlToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Acechando");

        // Idle <-> Walk (Caminata secundaria o por estado)
        var idleToWalk = stateIdle.AddTransition(stateWalk);
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.25f;
        idleToWalk.AddCondition(AnimatorConditionMode.Equals, 1, "Estado");

        var walkToIdle = stateWalk.AddTransition(stateIdle);
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.25f;
        walkToIdle.AddCondition(AnimatorConditionMode.Equals, 0, "Estado");

        // Crawl <-> Walk
        var walkToCrawl = stateWalk.AddTransition(stateCrawl);
        walkToCrawl.hasExitTime = false;
        walkToCrawl.duration = 0.2f;
        walkToCrawl.AddCondition(AnimatorConditionMode.If, 0, "Acechando");

        // AnyState -> Emerge (Disparo del Pez 10)
        var anyToEmerge = rootStateMachine.AddAnyStateTransition(stateEmerge);
        anyToEmerge.hasExitTime = false;
        anyToEmerge.duration = 0.15f;
        anyToEmerge.AddCondition(AnimatorConditionMode.If, 0, "Emergiendo");

        // Emerge -> Run (Cuando empieza la carrera tras emerger)
        var emergeToRun = stateEmerge.AddTransition(stateRun);
        emergeToRun.hasExitTime = false;
        emergeToRun.duration = 0.25f;
        emergeToRun.AddCondition(AnimatorConditionMode.If, 0, "Corriendo");

        // Emerge -> Run por ExitTime de respaldo (si la animación termina)
        var emergeToRunExit = stateEmerge.AddTransition(stateRun);
        emergeToRunExit.hasExitTime = true;
        emergeToRunExit.exitTime = 0.95f;
        emergeToRunExit.duration = 0.25f;

        // Run -> Atrapa (Cuando el Silbón alcanza al jugador)
        var runToAtrapa = stateRun.AddTransition(stateAtrapa);
        runToAtrapa.hasExitTime = false;
        runToAtrapa.duration = 0.1f;
        runToAtrapa.AddCondition(AnimatorConditionMode.If, 0, "Atrapando");

        // AnyState -> Atrapa (Respaldo por si atrapa mientras emerge o corre)
        var anyToAtrapa = rootStateMachine.AddAnyStateTransition(stateAtrapa);
        anyToAtrapa.hasExitTime = false;
        anyToAtrapa.duration = 0.1f;
        anyToAtrapa.AddCondition(AnimatorConditionMode.If, 0, "Atrapando");

        EditorUtility.SetDirty(controller);
    }

    private static void LimpiarTransiciones(AnimatorState state)
    {
        if (state == null) return;
        while (state.transitions.Length > 0)
            state.RemoveTransition(state.transitions[0]);
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
                if (clip != null) childState.state.motion = clip;
                return childState.state;
            }
        }

        var nuevoEstado = sm.AddState(nombre, posicion);
        if (clip != null) nuevoEstado.motion = clip;
        return nuevoEstado;
    }

    private static void VincularASilbonEnEscena(AnimatorController controller, Avatar avatarSilbon)
    {
        // Buscar el Silbón en la escena
        var silbon = GameObject.Find("silvon unity") 
                  ?? GameObject.Find("Silbon") 
                  ?? GameObject.Find("silbon");

        if (silbon != null)
        {
            var animator = silbon.GetComponent<Animator>();
            if (animator == null) animator = silbon.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            if (avatarSilbon != null)
            {
                animator.avatar = avatarSilbon;
            }
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(silbon);

            var director = Object.FindAnyObjectByType<FishingSilbonDirector>();
            if (director != null)
            {
                director.animatorSilbon = animator;
                EditorUtility.SetDirty(director);
            }

            // Marcar la escena como modificada y guardarla
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(silbon.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(silbon.scene);

            Debug.Log($"[Silbon Tool] Asignado Controller '{controller.name}' y Avatar '{animator.avatar?.name}' a '{silbon.name}'. Vinculado a FishingSilbonDirector y escena guardada.");
        }
        else
        {
            Debug.LogWarning("[Silbon Tool] No se encontró el GameObject 'silvon unity' o 'Silbon' en la escena activa.");
        }
    }
}
#endif
