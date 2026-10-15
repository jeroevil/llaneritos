using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Gestor central de audio para la experiencia del Silbón y la pesca en VR.
/// Controla transiciones ambientales dinámicas (calma -> tensión -> persecución),
/// efectos 2D (puntos pez, foley cinematográfico de parpadeo) y sonidos 3D espacializados (silbido del Silbón).
/// Puede rutear a través de un AudioMixer o funcionar de forma independiente.
/// </summary>
public class SilbonAudioManager : MonoBehaviour
{
    [Header("Audio Mixer (Opcional)")]
    [Tooltip("Mixer general del juego. Si no se asigna, funciona directamente con los AudioSources.")]
    public AudioMixer audioMixer;
    public AudioMixerGroup grupoAmbiente;
    public AudioMixerGroup grupoSFX;
    public AudioMixerGroup grupoSilbon3D;

    [Header("Clips de Música y Ambiente")]
    [Tooltip("Ambiente tranquilo de inicio (Peces 0 a 3)")]
    public AudioClip clipCalma;
    [Tooltip("Ambiente opresivo y tétrico (Peces 4 a 9)")]
    public AudioClip clipTension;
    [Tooltip("Música frenética y tensa de persecución (Pez 10)")]
    public AudioClip clipPersecucion;

    [Header("Clips de Efectos y Silbón")]
    [Tooltip("Sonido de recompensa al meter un pez al barril")]
    public AudioClip clipPezPunto;
    [Tooltip("Foley de terror cinematográfico durante el parpadeo negro")]
    public AudioClip clipCinematicBlink;
    [Tooltip("Silbido aterrador del Silbón (3D espacializado)")]
    public AudioClip clipSilbato;
    [Tooltip("Foley de aparición o esfumado entre las sombras")]
    public AudioClip clipWhoosh;
    [Tooltip("Sonido de la red cayendo al agua")]
    public AudioClip clipRedSplash;

    [Header("Ajustes de Volumen")]
    [Range(0f, 1f)] public float volumenMaster = 1.0f;
    [Range(0f, 1f)] public float volumenAmbiente = 0.8f;
    [Range(0f, 1f)] public float volumenSFX = 1.0f;
    [Range(0f, 2f)] public float volumenSilbato = 1.3f;
    [Tooltip("Distancia en metros donde el silbato suena al 100% antes de atenuarse (evita que suene demasiado apagado a 15-20m)")]
    [Range(2f, 30f)] public float distanciaMinimaSilbato3D = 12f;
    [Range(30f, 150f)] public float distanciaMaximaSilbato3D = 75f;
    [Tooltip("Velocidad de transición del crossfade de ambiente")]
    public float velocidadCrossfade = 1.5f;

    [Header("Duracion y Fade Out del Silbato")]
    [Tooltip("Duracion maxima del silbido en segundos para que no se alargue demasiado")]
    [Range(1.0f, 6.0f)] public float duracionMaximaSilbido = 2.8f;

    [Tooltip("Tiempo en segundos que tarda en desvanecerse suavemente el silbido")]
    [Range(0.2f, 2.0f)] public float duracionFadeOutSilbido = 0.8f;

    [Header("Ajustes de 3D y Ruteo")]
    [Tooltip("Grado de espacialidad 3D (0 = Estereo total directo, 1 = 3D puro). 0.6 es ideal para notar de donde viene sin que se vuelva inaudible en un oido")]
    [Range(0f, 1f)] public float espacialidadSilbato = 0.6f;

    [Tooltip("Si esta desactivado, el audio va directo al AudioListener evitando posibles faders en silencio del Mixer de Lu")]
    public bool usarAudioMixer = false;

    // AudioSources dedicados
    private AudioSource _sourceCalma;
    private AudioSource _sourceTension;
    private AudioSource _sourcePersecucion;
    private AudioSource _sourceSFX2D;
    private AudioSource _sourceSilbon3D;
    private Coroutine _rutinaFadeSilbato;

    // Estado interno
    private float _nivelTerrorObjetivo = 0f;
    private float _nivelTerrorActual = 0f;
    private bool _enPersecucion = false;

    private void Awake()
    {
        AutoCargarClipsSiFaltan();
        CrearConfigurarAudioSources();
    }

    private void AutoCargarClipsSiFaltan()
    {
#if UNITY_EDITOR
        if (clipSilbato == null) clipSilbato = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Whistle.mp3");
        if (clipCalma == null) clipCalma = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Ambience.mp3");
        if (clipTension == null) clipTension = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/horror_atmosphere.mp3");
        if (clipPersecucion == null) clipPersecucion = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Horror.mp3");
        if (clipPezPunto == null) clipPezPunto = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Pez_Point.mp3");
        if (clipCinematicBlink == null) clipCinematicBlink = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/cinematic_Horror.mp3");
        if (clipWhoosh == null) clipWhoosh = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/whoosh.mp3");
        if (clipRedSplash == null) clipRedSplash = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Red_Splash.mp3");
#endif
    }

    private void Start()
    {
        IniciarAmbienteCalma();
    }

    private void CrearConfigurarAudioSources()
    {
        // 1. Fuente de Calma (2D Stereo Loop)
        _sourceCalma = CrearAudioSource("AudioSource_Calma", 0f, true);
        _sourceCalma.clip = clipCalma;
        if (usarAudioMixer && grupoAmbiente != null) _sourceCalma.outputAudioMixerGroup = grupoAmbiente;

        // 2. Fuente de Tensión (2D Stereo Loop)
        _sourceTension = CrearAudioSource("AudioSource_Tension", 0f, true);
        _sourceTension.clip = clipTension;
        if (usarAudioMixer && grupoAmbiente != null) _sourceTension.outputAudioMixerGroup = grupoAmbiente;

        // 3. Fuente de Persecución (2D Stereo Loop)
        _sourcePersecucion = CrearAudioSource("AudioSource_Persecucion", 0f, true);
        _sourcePersecucion.clip = clipPersecucion;
        if (usarAudioMixer && grupoAmbiente != null) _sourcePersecucion.outputAudioMixerGroup = grupoAmbiente;

        // 4. Fuente de SFX 2D (Puntos, Cinematic Blink)
        _sourceSFX2D = CrearAudioSource("AudioSource_SFX_2D", 0f, false);
        _sourceSFX2D.volume = 1f; // Fundamental para que PlayOneShot no se multiplique por 0
        if (usarAudioMixer && grupoSFX != null) _sourceSFX2D.outputAudioMixerGroup = grupoSFX;

        // 5. Fuente 3D para el Silbón (Silbido posicional en VR)
        _sourceSilbon3D = CrearAudioSource("AudioSource_Silbon_3D", espacialidadSilbato, false);
        _sourceSilbon3D.volume = 1f; // ¡CRÍTICO! Debe ser 1f para que el silbido suene a volumen completo
        _sourceSilbon3D.rolloffMode = AudioRolloffMode.Linear;
        _sourceSilbon3D.minDistance = distanciaMinimaSilbato3D;
        _sourceSilbon3D.maxDistance = distanciaMaximaSilbato3D;
        _sourceSilbon3D.dopplerLevel = 0f;
        if (usarAudioMixer)
        {
            if (grupoSilbon3D != null) _sourceSilbon3D.outputAudioMixerGroup = grupoSilbon3D;
            else if (grupoSFX != null) _sourceSilbon3D.outputAudioMixerGroup = grupoSFX;
        }
    }

    private AudioSource CrearAudioSource(string nombreHijo, float spatialBlend, bool loop)
    {
        var go = new GameObject(nombreHijo);
        go.transform.SetParent(this.transform);
        go.transform.localPosition = Vector3.zero;

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = spatialBlend;
        source.volume = 1f; // Por defecto a 1f para permitir PlayOneShot
        return source;
    }

    private void Update()
    {
        if (_enPersecucion) return;

        // Crossfade suave entre Calma y Tensión basado en el nivel de terror
        if (Mathf.Abs(_nivelTerrorActual - _nivelTerrorObjetivo) > 0.001f)
        {
            _nivelTerrorActual = Mathf.MoveTowards(_nivelTerrorActual, _nivelTerrorObjetivo, Time.deltaTime * velocidadCrossfade);
        }

        float pesoCalma = Mathf.Clamp01(1f - _nivelTerrorActual);
        float pesoTension = Mathf.Clamp01(_nivelTerrorActual);

        if (_sourceCalma != null && _sourceCalma.isPlaying)
            _sourceCalma.volume = pesoCalma * volumenAmbiente * volumenMaster;

        if (_sourceTension != null)
        {
            if (pesoTension > 0.01f && !_sourceTension.isPlaying)
                _sourceTension.Play();

            _sourceTension.volume = pesoTension * volumenAmbiente * volumenMaster;
        }
    }

    /// <summary>
    /// Inicia el ambiente pacífico de los peces 0 a 3.
    /// </summary>
    public void IniciarAmbienteCalma()
    {
        _enPersecucion = false;
        _nivelTerrorObjetivo = 0f;
        _nivelTerrorActual = 0f;

        if (_sourceCalma != null && clipCalma != null)
        {
            _sourceCalma.volume = volumenAmbiente * volumenMaster;
            if (!_sourceCalma.isPlaying)
                _sourceCalma.Play();
        }

        if (_sourceTension != null)
        {
            _sourceTension.volume = 0f;
            _sourceTension.Stop();
        }

        if (_sourcePersecucion != null)
        {
            _sourcePersecucion.volume = 0f;
            _sourcePersecucion.Stop();
        }
    }

    /// <summary>
    /// Ajusta el nivel de terror (0 a 1) para el crossfade suave entre Calma y Tensión (Peces 4 a 9).
    /// </summary>
    public void SetTerrorLevel(float terrorWeight)
    {
        if (_enPersecucion) return;
        _nivelTerrorObjetivo = Mathf.Clamp01(terrorWeight);
    }

    /// <summary>
    /// Reproduce el SFX de obtención de punto al depositar un pez en el barril.
    /// </summary>
    public void PlayPezPoint()
    {
        if (_sourceSFX2D != null && clipPezPunto != null)
        {
            _sourceSFX2D.PlayOneShot(clipPezPunto, volumenSFX * volumenMaster);
        }
    }

    /// <summary>
    /// Reproduce el silbido 3D del Silbón con duración acotada y desvanecimiento suave (fade out)
    /// para que termine a la par de la desaparición del Silbón.
    /// </summary>
    public void PlaySilbonStalkingSound(Vector3 posicionSilbon, float duracionAparicion = -1f)
    {
        if (_sourceSilbon3D != null)
        {
            if (_rutinaFadeSilbato != null)
                StopCoroutine(_rutinaFadeSilbato);

            _sourceSilbon3D.transform.position = posicionSilbon;
            _sourceSilbon3D.spatialBlend = espacialidadSilbato;
            _sourceSilbon3D.minDistance = distanciaMinimaSilbato3D;
            _sourceSilbon3D.maxDistance = distanciaMaximaSilbato3D;

            if (clipSilbato == null)
            {
#if UNITY_EDITOR
                clipSilbato = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sonidos/Whistle.mp3");
#endif
            }

            if (clipSilbato != null)
            {
                // Limitar la duracion total para que no dure demasiado
                float duracionTotal = duracionMaximaSilbido;
                if (duracionAparicion > 0f)
                {
                    duracionTotal = Mathf.Min(duracionAparicion, duracionMaximaSilbido);
                }

                _rutinaFadeSilbato = StartCoroutine(RutinaReproducirSilbatoConFade(duracionTotal, duracionFadeOutSilbido));
            }
            else
            {
                Debug.LogError("[Audio] ¡ERROR! clipSilbato es NULL. No se encontro Whistle.mp3 en Assets/Sonidos/");
            }

            if (clipWhoosh != null)
            {
                _sourceSilbon3D.PlayOneShot(clipWhoosh, 0.7f * volumenSFX * volumenMaster);
            }
        }
    }

    private IEnumerator RutinaReproducirSilbatoConFade(float duracionTotal, float duracionFade)
    {
        float volBase = volumenSilbato * volumenMaster;
        _sourceSilbon3D.clip = clipSilbato;
        _sourceSilbon3D.volume = volBase;
        _sourceSilbon3D.time = 0f;
        _sourceSilbon3D.Play();

        Debug.Log($"[Audio] Silbato iniciado: {duracionTotal:F1}s total (Fade Out: {duracionFade:F1}s)");

        // 1. Mantener volumen pleno durante la mayor parte de la aparicion
        float tiempoPleno = Mathf.Max(0.2f, duracionTotal - duracionFade);
        yield return new WaitForSeconds(tiempoPleno);

        // 2. Desvanecer suavemente hacia cero (fade out)
        float t = 0f;
        while (t < duracionFade)
        {
            t += Time.deltaTime;
            float factor = Mathf.Clamp01(1f - (t / duracionFade));
            _sourceSilbon3D.volume = volBase * factor;
            yield return null;
        }

        _sourceSilbon3D.volume = 0f;
        _sourceSilbon3D.Stop();
        _rutinaFadeSilbato = null;
    }

    /// <summary>
    /// Detiene el silbido de inmediato con un fundido suave hacia el silencio.
    /// </summary>
    public void StopSilbatoConFade(float duracionFade = 0.5f)
    {
        if (_sourceSilbon3D != null && _sourceSilbon3D.isPlaying)
        {
            if (_rutinaFadeSilbato != null)
                StopCoroutine(_rutinaFadeSilbato);

            _rutinaFadeSilbato = StartCoroutine(RutinaFadeOutInmediato(duracionFade));
        }
    }

    private IEnumerator RutinaFadeOutInmediato(float duracionFade)
    {
        float volInicial = _sourceSilbon3D.volume;
        float t = 0f;
        while (t < duracionFade)
        {
            t += Time.deltaTime;
            float factor = Mathf.Clamp01(1f - (t / duracionFade));
            _sourceSilbon3D.volume = volInicial * factor;
            yield return null;
        }

        _sourceSilbon3D.volume = 0f;
        _sourceSilbon3D.Stop();
        _rutinaFadeSilbato = null;
    }

    /// <summary>
    /// Actualiza la posición 3D de la fuente del silbato en tiempo real
    /// para que el sonido acompañe al Silbón mientras gatea de un punto a otro.
    /// </summary>
    public void ActualizarPosicionSilbato(Vector3 nuevaPosicion)
    {
        if (_sourceSilbon3D != null)
        {
            _sourceSilbon3D.transform.position = nuevaPosicion;
        }
    }

    /// <summary>
    /// Transición cinemática de terror durante el parpadeo negro previo a la persecución (Pez 10).
    /// Corta los ambientes de fondo y detona el foley de terror cinemático a máximo impacto.
    /// </summary>
    public void PlayCinematicBlink()
    {
        // Silenciar de golpe la calma y la tensión previa
        if (_sourceCalma != null) _sourceCalma.volume = 0f;
        if (_sourceTension != null) _sourceTension.volume = 0f;

        // Detonar el foley cinemático de terror
        if (_sourceSFX2D != null && clipCinematicBlink != null)
        {
            _sourceSFX2D.PlayOneShot(clipCinematicBlink, 1.0f * volumenMaster);
            Debug.Log("[Audio] ¡Foley cinematográfico de parpadeo terrorífico reproducido!");
        }
    }

    /// <summary>
    /// Arranca la música de persecución frenética (Horror.mp3) cuando el Silbón abre los ojos y corre tras el jugador.
    /// </summary>
    public void StartChaseMusic()
    {
        _enPersecucion = true;

        if (_sourceCalma != null) _sourceCalma.Stop();
        if (_sourceTension != null) _sourceTension.Stop();

        if (_sourcePersecucion != null && clipPersecucion != null)
        {
            _sourcePersecucion.volume = volumenAmbiente * volumenMaster;
            if (!_sourcePersecucion.isPlaying)
                _sourcePersecucion.Play();

            Debug.Log("[Audio] ¡Música de persecución (Horror.mp3) iniciada en loop!");
        }
    }

    /// <summary>
    /// Reproduce el chapoteo de la red en el agua en 3D.
    /// </summary>
    public void PlayRedSplash(Vector3 puntoAgua)
    {
        if (clipRedSplash != null)
        {
            AudioSource.PlayClipAtPoint(clipRedSplash, puntoAgua, volumenSFX * volumenMaster);
        }
    }

    /// <summary>
    /// Reinicia todo el sistema de audio a su estado pacífico inicial.
    /// </summary>
    public void ResetAudio()
    {
        IniciarAmbienteCalma();
    }
}
