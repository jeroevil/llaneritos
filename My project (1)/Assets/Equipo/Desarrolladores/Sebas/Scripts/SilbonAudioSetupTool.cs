using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Audio;
using System.IO;

/// <summary>
/// Herramienta de Editor para configurar automáticamente el sistema de audio del juego:
/// - Asigna automáticamente los 8 archivos de sonido de 'Assets/Sonidos/' a SilbonAudioManager.
/// - Conecta el AudioMixer (si existe 'GameAudioMixer.mixer' o similar) y sus grupos.
/// - Conecta SilbonAudioManager al FishingSilbonDirector en la escena activa.
/// </summary>
[InitializeOnLoad]
public static class SilbonAudioSetupTool
{
    private const string RUTA_CARPETA_SONIDOS = "Assets/Sonidos";

    [InitializeOnLoadMethod]
    private static void AutoVerificarAudio()
    {
        EditorApplication.delayCall += () =>
        {
            var director = Object.FindAnyObjectByType<FishingSilbonDirector>();
            if (director != null && director.audioManager == null)
            {
                Debug.Log("[Audio Tool] Conectando SilbonAudioManager automáticamente a FishingSilbonDirector...");
                ConfigurarAudioCompleto();
            }
        };
    }

    [MenuItem("Llaneritos/Configurar Sistema de Sonido (Mixer y AudioSources)", false, 2)]
    [MenuItem("GameObject/Silbón/Configurar Sistema de Sonido", false, 11)]
    public static void ConfigurarAudioCompleto()
    {
        Debug.Log("[Audio Tool] Iniciando configuración automática del sistema de audio...");

        // 1. Cargar todos los clips de Assets/Sonidos/
        var clipCalma = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/Ambience.mp3");
        var clipTension = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/horror_atmosphere.mp3");
        var clipPersecucion = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/Horror.mp3");
        var clipPezPunto = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/Pez_Point.mp3");
        var clipCinematicBlink = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/cinematic_Horror.mp3");
        var clipSilbato = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/Whistle.mp3");
        var clipWhoosh = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/whoosh.mp3");
        var clipRedSplash = AssetDatabase.LoadAssetAtPath<AudioClip>($"{RUTA_CARPETA_SONIDOS}/Red_Splash.mp3");

        // 2. Buscar si existe un AudioMixer en Assets/Sonidos o en el proyecto
        AudioMixer mixer = null;
        string[] guidsMixer = AssetDatabase.FindAssets("t:AudioMixer");
        if (guidsMixer != null && guidsMixer.Length > 0)
        {
            string pathMixer = AssetDatabase.GUIDToAssetPath(guidsMixer[0]);
            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(pathMixer);
            Debug.Log($"[Audio Tool] Encontrado AudioMixer: {pathMixer}");
        }

        // 3. Localizar o crear GameObject para SilbonAudioManager en escena
        var director = Object.FindAnyObjectByType<FishingSilbonDirector>();
        GameObject goAudio = null;

        if (director != null)
        {
            goAudio = director.gameObject;
        }
        else
        {
            goAudio = GameObject.Find("Sistema_Audio") ?? new GameObject("Sistema_Audio");
        }

        var audioManager = goAudio.GetComponent<SilbonAudioManager>();
        if (audioManager == null)
            audioManager = goAudio.AddComponent<SilbonAudioManager>();

        // 4. Asignar los clips cargados
        audioManager.clipCalma = clipCalma;
        audioManager.clipTension = clipTension;
        audioManager.clipPersecucion = clipPersecucion;
        audioManager.clipPezPunto = clipPezPunto;
        audioManager.clipCinematicBlink = clipCinematicBlink;
        audioManager.clipSilbato = clipSilbato;
        audioManager.clipWhoosh = clipWhoosh;
        audioManager.clipRedSplash = clipRedSplash;

        // 5. Asignar AudioMixer y grupos si existen
        if (mixer != null)
        {
            audioManager.audioMixer = mixer;
            var grupos = mixer.FindMatchingGroups(string.Empty);
            foreach (var g in grupos)
            {
                string n = g.name.ToLower();
                if (n.Contains("amb") || n.Contains("music")) audioManager.grupoAmbiente = g;
                else if (n.Contains("silbon") || n.Contains("voice") || n.Contains("3d")) audioManager.grupoSilbon3D = g;
                else if (n.Contains("sfx") || n.Contains("efecto")) audioManager.grupoSFX = g;
            }
        }

        // 6. Vincular con FishingSilbonDirector
        if (director != null)
        {
            director.audioManager = audioManager;
            EditorUtility.SetDirty(director);
        }

        EditorUtility.SetDirty(audioManager);
        EditorUtility.SetDirty(goAudio);

        Debug.Log("[Audio Tool] ¡Sistema de audio configurado exitosamente en escena!");
    }
}
#endif
