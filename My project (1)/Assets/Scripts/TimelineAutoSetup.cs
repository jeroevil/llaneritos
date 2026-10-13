using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Configura automaticamente todas las pistas de la Timeline:
/// - Intercambio de Skybox (Bueno a los 0-10s, Malo a los 10-20s).
/// - Curva de oscuridad del Volume_Terror (de 0.0 a 1.0).
/// - Apariciones escalonadas del Silbon (acecho 1, acecho 2 y persecucion final).
/// - Activacion progresiva de las barras de meta (opcional).
/// </summary>
[ExecuteInEditMode]
public class TimelineAutoSetup : MonoBehaviour
{
    [Header("Director Principal")]
    [Tooltip("El PlayableDirector del objeto actual")]
    public PlayableDirector playableDirector;

    [Header("Elementos de Escena para Vincular")]
    [Tooltip("Objeto del cielo bueno (skybox esfera silbon (Bueno))")]
    public GameObject skyboxBueno;

    [Tooltip("Objeto del cielo malo (skybox esfera silbon (malo))")]
    public GameObject skyboxMalo;

    [Tooltip("Objeto del Volume de Terror para animar su peso (Volume_Terror)")]
    public Volume volumeTerror;

    [Tooltip("Objeto del Silbon en escena (silvon unity)")]
    public GameObject silbonEnemigo;

    [Header("Barras de Progreso (Opcional)")]
    [Tooltip("Arrastra en orden: barra 0, barra 1, barra 1.5, barra 2, barra 3 final")]
    public GameObject[] barrasProgreso;

    [Header("Tiempos de Calibracion")]
    [Tooltip("Segundo donde termina la pesca y arranca la persecucion")]
    public float segundoClimax = 10f;

    [Tooltip("Duracion total de la Timeline")]
    public float duracionTotal = 20f;

    private void Reset()
    {
        playableDirector = GetComponent<PlayableDirector>();
    }

    [ContextMenu("Construir Timeline Ahora")]
    public void BuildTimeline()
    {
        if (playableDirector == null)
            playableDirector = GetComponent<PlayableDirector>();

        if (playableDirector == null)
        {
            Debug.LogError("[TimelineAutoSetup] No hay PlayableDirector asignado.");
            return;
        }

        TimelineAsset timeline = playableDirector.playableAsset as TimelineAsset;
        if (timeline == null)
        {
#if UNITY_EDITOR
            // Si no tiene un asset creado, lo creamos
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            string path = "Assets/Timeline_Silbon_Auto.playable";
            AssetDatabase.CreateAsset(timeline, path);
            AssetDatabase.SaveAssets();
            playableDirector.playableAsset = timeline;
            Debug.Log($"[TimelineAutoSetup] Creado nuevo TimelineAsset en {path}");
#else
            Debug.LogError("[TimelineAutoSetup] El PlayableDirector no tiene un TimelineAsset asignado.");
            return;
#endif
        }

#if UNITY_EDITOR
        Undo.RecordObject(timeline, "Auto-configurar Timeline");
        Undo.RecordObject(playableDirector, "Auto-configurar Bindings");
#endif

        // 1. Limpiar pistas anteriores para evitar duplicados
        List<TrackAsset> tracksToRemove = new List<TrackAsset>(timeline.GetOutputTracks());
        foreach (var track in tracksToRemove)
        {
            timeline.DeleteTrack(track);
        }

        // 2. PISTA: Skybox Bueno (0s a 10s)
        if (skyboxBueno != null)
        {
            ActivationTrack trackBueno = timeline.CreateTrack<ActivationTrack>(null, "Skybox Bueno");
            TimelineClip clipBueno = trackBueno.CreateDefaultClip();
            clipBueno.start = 0.0;
            clipBueno.duration = segundoClimax;
            playableDirector.SetGenericBinding(trackBueno, skyboxBueno);
        }

        // 3. PISTA: Skybox Malo (10s a 20s)
        if (skyboxMalo != null)
        {
            ActivationTrack trackMalo = timeline.CreateTrack<ActivationTrack>(null, "Skybox Malo (Climax)");
            TimelineClip clipMalo = trackMalo.CreateDefaultClip();
            clipMalo.start = segundoClimax;
            clipMalo.duration = Mathf.Max(1f, duracionTotal - segundoClimax);
            playableDirector.SetGenericBinding(trackMalo, skyboxMalo);
        }

        // 4. PISTA: Volume Terror - Animacion automatica de Weight (0.0 a 1.0)
        if (volumeTerror != null)
        {
            AnimationTrack trackVolume = timeline.CreateTrack<AnimationTrack>(null, "Volume Terror (Oscurecimiento)");
            
            AnimationClip volumeClip = new AnimationClip();
            volumeClip.name = "Curva_Oscurecimiento";

            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 0f);                               // Inicio: completamente claro
            curve.AddKey(segundoClimax * 0.45f, 0.35f);         // Medio: empieza a ponerse siniestro
            curve.AddKey(segundoClimax, 1f);                    // Al llegar al 100%: oscuridad total
            curve.AddKey(duracionTotal, 1f);

            volumeClip.SetCurve("", typeof(Volume), "weight", curve);

#if UNITY_EDITOR
            AssetDatabase.AddObjectToAsset(volumeClip, timeline);
#endif

            TimelineClip clipVol = trackVolume.CreateClip(volumeClip);
            clipVol.start = 0.0;
            clipVol.duration = duracionTotal;

            playableDirector.SetGenericBinding(trackVolume, volumeTerror.gameObject);
        }

        // 5. PISTA: Silbon - Acecho progresivo y Persecucion final
        if (silbonEnemigo != null)
        {
            ActivationTrack trackSilbon = timeline.CreateTrack<ActivationTrack>(null, "Silbon Presencia");

            // Primer acecho (vistazo lejano entre arboles): de 4.5s a 6.5s
            TimelineClip stalk1 = trackSilbon.CreateDefaultClip();
            stalk1.start = 4.5;
            stalk1.duration = 2.0;

            // Segundo acecho (mas cerca de la orilla): de 7.5s a 9.5s
            TimelineClip stalk2 = trackSilbon.CreateDefaultClip();
            stalk2.start = 7.5;
            stalk2.duration = 2.0;

            // Persecucion final: a partir del segundo 10 continuo
            TimelineClip chase = trackSilbon.CreateDefaultClip();
            chase.start = segundoClimax;
            chase.duration = Mathf.Max(1f, duracionTotal - segundoClimax);

            playableDirector.SetGenericBinding(trackSilbon, silbonEnemigo);
        }

        // 6. PISTAS: Barras de progreso por etapas (si estan asignadas)
        if (barrasProgreso != null && barrasProgreso.Length > 0)
        {
            float tiempoPorBarra = segundoClimax / barrasProgreso.Length;
            for (int i = 0; i < barrasProgreso.Length; i++)
            {
                if (barrasProgreso[i] == null) continue;

                ActivationTrack trackBarra = timeline.CreateTrack<ActivationTrack>(null, $"Barra_{i}_{barrasProgreso[i].name}");
                TimelineClip clipBarra = trackBarra.CreateDefaultClip();
                clipBarra.start = i * tiempoPorBarra;

                // La ultima barra se queda encendida hasta el final
                if (i == barrasProgreso.Length - 1)
                    clipBarra.duration = duracionTotal - clipBarra.start;
                else
                    clipBarra.duration = tiempoPorBarra;

                playableDirector.SetGenericBinding(trackBarra, barrasProgreso[i]);
            }
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(timeline);
        EditorUtility.SetDirty(playableDirector);
        AssetDatabase.SaveAssets();
#endif

        Debug.Log("<color=green>[TimelineAutoSetup] Exito: La Timeline fue construida y calibrada correctamente.</color>");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TimelineAutoSetup))]
public class TimelineAutoSetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TimelineAutoSetup setup = (TimelineAutoSetup)target;

        GUILayout.Space(15);
        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button("CONSTRUIR Y CALIBRAR TIMELINE", GUILayout.Height(40)))
        {
            setup.BuildTimeline();
        }
        GUI.backgroundColor = Color.white;
    }
}
#endif
