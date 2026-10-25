using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestiona la introducción cinematográfica inicial de Moriche:
/// 1. Mantiene al jugador dentro de una caja negra (telón) con el movimiento bloqueado.
/// 2. Hace flotar el logo de Moriche a una distancia cómoda frente a la mirada del jugador en VR.
/// 3. Mantiene el logo visible por unos segundos y luego lo desvanece suavemente (Fade-Out de transparencia).
/// 4. Desactiva la caja negra (telón) de golpe, revelando el juego y habilitando la caminata.
/// No modifica ni interfiere con los scripts existentes.
/// </summary>
public class IntroMoricheTelon : MonoBehaviour
{
    [Header("Objetos de la Intro")]
    [Tooltip("Arrastra aqui el GameObject de la caja negra que encierra al jugador como telon.")]
    public GameObject cajaNegraTelon;

    [Tooltip("Arrastra aqui el objeto del logo de Moriche (SpriteRenderer, UI Image o MeshRenderer).")]
    public GameObject logoMoriche;

    [Header("Posicionamiento en VR (Frente a los Ojos)")]
    [Tooltip("Distancia en metros frente a la camara donde flotara el logo (1.5m a 2.0m es ideal para VR)")]
    [Range(0.8f, 3.5f)]
    public float distanciaFrente = 1.8f;

    [Tooltip("Si esta activo, el logo flota siguiendo suavemente hacia donde gires la cabeza. Si esta apagado, el logo queda fijo al centro de la vista (pegado a la camara) durante toda la intro.")]
    public bool seguirMiradaSuave = true;

    [Tooltip("Suavidad con la que el logo se reacomoda frente a los ojos")]
    [Range(2f, 20f)]
    public float suavidadSeguimiento = 8f;

    [Header("Tiempos de la Intro")]
    [Tooltip("Segundos que el logo permanece totalmente visible para que el jugador lo lea")]
    [Range(1f, 6f)]
    public float tiempoVisible = 2.5f;

    [Tooltip("Segundos que tarda el logo en desvanecerse suavemente a transparente")]
    [Range(0.3f, 3f)]
    public float duracionFade = 1.0f;

    [Tooltip("Segundos que tarda el logo en aparecer (de transparente a opaco)")]
    [Range(0.3f, 3f)]
    public float duracionFadeIn = 1.0f;

    [Header("Video Tutorial (antes del logo)")]
    [Tooltip("Opcional: VideoPlayer del tutorial de controles. Si esta asignado, el video se reproduce completo dentro del telon y despues aparece el logo.")]
    public UnityEngine.Video.VideoPlayer videoTutorial;

    [Tooltip("Pantalla donde se ve el video (el objeto con el VideoPlayer). Queda fija frente a los ojos mientras suena.")]
    public GameObject pantallaTutorial;

    [Tooltip("Distancia en metros de la pantalla del video frente a los ojos")]
    [Range(1f, 4f)]
    public float distanciaPantalla = 2.5f;

    [Header("Control de Jugador")]
    [Tooltip("Desactiva temporalmente el script PinchToWalk durante la intro y lo reactiva al abrir el telon")]
    public bool bloquearCaminataEnIntro = true;

    [Tooltip("Componentes de movimiento que se apagan durante la intro (joystick para caminar y girar del rig de Meta). Si se deja vacio, se busca el FirstPersonLocomotor de la escena.")]
    public Behaviour[] locomocionABloquear;

    [Tooltip("Referencia opcional a la camara de los ojos (CenterEyeAnchor). Si se deja vacio, se busca sola.")]
    public Transform camaraOcular;

    [Header("Eventos")]
    [Tooltip("Se dispara en el momento exacto en que desaparece la caja negra")]
    public UnityEngine.Events.UnityEvent OnTelonAbierto;

    // Estado interno
    private bool _introEnProgreso = true;
    private PinchToWalk _pinchToWalk;
    private SpriteRenderer[] _sprites;
    private Image[] _uiImages;
    private CanvasGroup[] _canvasGroups;
    private Renderer[] _renderers;
    private Vector3 _posicionObjetivoLogo;
    private Quaternion _rotacionObjetivoLogo;
    private Transform _padreOriginalLogo;
    private bool _logoPegadoACamara;
    private Transform _padreOriginalPantalla;

    private void Awake()
    {
        BuscarReferencias();
    }

    private void Start()
    {
        BuscarReferencias();
        ConfigurarEstadoInicial();
        StartCoroutine(RutinaIntro());
    }

    private void BuscarReferencias()
    {
        if (camaraOcular == null)
        {
            var cam = Camera.main;
            if (cam != null) camaraOcular = cam.transform;
            else
            {
                var goCenter = GameObject.Find("CenterEyeAnchor");
                if (goCenter != null) camaraOcular = goCenter.transform;
            }
        }

        if (_pinchToWalk == null)
        {
            _pinchToWalk = FindFirstObjectByType<PinchToWalk>();
        }

        if (locomocionABloquear == null || locomocionABloquear.Length == 0)
        {
            // el rig de Meta mueve al jugador con el FirstPersonLocomotor (desplazamiento y giro con joystick)
            var encontrados = new List<Behaviour>();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "FirstPersonLocomotor") encontrados.Add(mb);
            locomocionABloquear = encontrados.ToArray();
        }

        if (logoMoriche != null)
        {
            _sprites = logoMoriche.GetComponentsInChildren<SpriteRenderer>(true);
            _uiImages = logoMoriche.GetComponentsInChildren<Image>(true);
            _canvasGroups = logoMoriche.GetComponentsInChildren<CanvasGroup>(true);
            _renderers = logoMoriche.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void ConfigurarEstadoInicial()
    {
        // 1. Asegurar que la caja negra este prendida
        if (cajaNegraTelon != null)
            cajaNegraTelon.SetActive(true);

        // 2. Bloquear la caminata
        if (bloquearCaminataEnIntro && _pinchToWalk != null)
            _pinchToWalk.enabled = false;
        if (bloquearCaminataEnIntro)
            ActivarLocomocion(false);

        // 3. Colocar el logo justo frente a la mirada inicial
        if (logoMoriche != null)
        {
            logoMoriche.SetActive(true);
            ActualizarAlfaLogo(1.0f);

            if (camaraOcular != null && !seguirMiradaSuave)
            {
                // Logo fijo a la vista: se vuelve hijo de la camara para que no se mueva al girar la cabeza
                _padreOriginalLogo = logoMoriche.transform.parent;
                logoMoriche.transform.SetParent(camaraOcular, false);
                logoMoriche.transform.localPosition = Vector3.forward * distanciaFrente;
                logoMoriche.transform.localRotation = Quaternion.identity;
                _logoPegadoACamara = true;
            }
            else if (camaraOcular != null)
            {
                Vector3 fwd = camaraOcular.forward;
                _posicionObjetivoLogo = camaraOcular.position + fwd * distanciaFrente;
                _rotacionObjetivoLogo = Quaternion.LookRotation(_posicionObjetivoLogo - camaraOcular.position);

                logoMoriche.transform.position = _posicionObjetivoLogo;
                logoMoriche.transform.rotation = _rotacionObjetivoLogo;
            }
        }
    }

    private void LateUpdate()
    {
        if (!_introEnProgreso || _logoPegadoACamara || logoMoriche == null || camaraOcular == null) return;

        // Calcular la posicion ideal frente a los ojos
        Vector3 posicionDeseada = camaraOcular.position + camaraOcular.forward * distanciaFrente;
        Quaternion rotacionDeseada = Quaternion.LookRotation(posicionDeseada - camaraOcular.position);

        if (seguirMiradaSuave)
        {
            logoMoriche.transform.position = Vector3.Lerp(logoMoriche.transform.position, posicionDeseada, Time.deltaTime * suavidadSeguimiento);
            logoMoriche.transform.rotation = Quaternion.Slerp(logoMoriche.transform.rotation, rotacionDeseada, Time.deltaTime * suavidadSeguimiento);
        }
        else
        {
            logoMoriche.transform.position = posicionDeseada;
            logoMoriche.transform.rotation = rotacionDeseada;
        }
    }

    private IEnumerator RutinaIntro()
    {
        _introEnProgreso = true;

        // 0. Video tutorial de controles (si hay): el logo espera escondido hasta que termine
        if (videoTutorial != null)
            yield return RutinaVideoTutorial();

        Vector3 escalaInicial = (logoMoriche != null) ? logoMoriche.transform.localScale : Vector3.one;

        // 1. Fade In: el logo aparece de transparente a opaco, creciendo un poco (el fade out al reves)
        float t = 0f;
        while (t < duracionFadeIn)
        {
            t += Time.deltaTime;
            float factor = Mathf.SmoothStep(0f, 1f, t / duracionFadeIn);
            ActualizarAlfaLogo(factor);
            if (logoMoriche != null)
                logoMoriche.transform.localScale = escalaInicial * Mathf.Lerp(0.92f, 1.0f, factor);
            yield return null;
        }
        ActualizarAlfaLogo(1f);
        if (logoMoriche != null)
            logoMoriche.transform.localScale = escalaInicial;

        // 2. Tiempo que el logo permanece 100% visible
        yield return new WaitForSeconds(tiempoVisible);
        // 3. Fade Out de transparencia del logo
        t = 0f;

        while (t < duracionFade)
        {
            t += Time.deltaTime;
            float factor = Mathf.Clamp01(1f - (t / duracionFade));

            ActualizarAlfaLogo(factor);

            // Reducción sutil de escala durante el desvanecimiento para un efecto más pulido
            if (logoMoriche != null)
            {
                logoMoriche.transform.localScale = escalaInicial * Mathf.Lerp(0.92f, 1.0f, factor);
            }

            yield return null;
        }

        // 3. Ocultar el logo por completo
        if (logoMoriche != null)
        {
            ActualizarAlfaLogo(0f);
            logoMoriche.SetActive(false);

            if (_logoPegadoACamara)
            {
                logoMoriche.transform.SetParent(_padreOriginalLogo, true);
                _logoPegadoACamara = false;
            }
        }

        // 4. ¡Apertura del Telón! La caja negra desaparece de una
        if (cajaNegraTelon != null)
        {
            cajaNegraTelon.SetActive(false);
            Debug.Log("[Intro] Telón de caja negra abierto de golpe. ¡Comienza la experiencia!");
        }

        // 5. Reactivar caminata del jugador
        if (bloquearCaminataEnIntro && _pinchToWalk != null)
        {
            _pinchToWalk.enabled = true;
        }
        if (bloquearCaminataEnIntro)
            ActivarLocomocion(true);

        _introEnProgreso = false;
        OnTelonAbierto?.Invoke();
    }

    private void ActivarLocomocion(bool activa)
    {
        if (locomocionABloquear == null) return;
        foreach (var componente in locomocionABloquear)
            if (componente != null) componente.enabled = activa;
    }

    /// <summary>
    /// Reproduce el video tutorial completo frente a los ojos y al terminar muestra el logo.
    /// </summary>
    private IEnumerator RutinaVideoTutorial()
    {
        if (logoMoriche != null)
            logoMoriche.SetActive(false);

        GameObject pantalla = pantallaTutorial != null ? pantallaTutorial : videoTutorial.gameObject;
        pantalla.SetActive(true);
        if (camaraOcular != null)
        {
            // pantalla fija a la vista, igual que el logo
            _padreOriginalPantalla = pantalla.transform.parent;
            pantalla.transform.SetParent(camaraOcular, false);
            pantalla.transform.localPosition = Vector3.forward * distanciaPantalla;
            pantalla.transform.localRotation = Quaternion.identity;
        }

        videoTutorial.isLooping = false;
        videoTutorial.Prepare();
        float espera = 0f;
        while (!videoTutorial.isPrepared && espera < 15f)
        {
            espera += Time.unscaledDeltaTime;
            yield return null;
        }

        bool terminado = false;
        UnityEngine.Video.VideoPlayer.EventHandler alTerminar = _ => terminado = true;
        videoTutorial.loopPointReached += alTerminar;
        videoTutorial.Play();

        // si la carga fue lenta, Play puede perderse: se reintenta hasta que el video avance
        float arranque = 0f;
        while (videoTutorial.frame < 0 && arranque < 5f)
        {
            arranque += Time.unscaledDeltaTime;
            if (!videoTutorial.isPlaying && videoTutorial.isPrepared) videoTutorial.Play();
            yield return null;
        }

        // respaldo por si el evento no llega: la duracion del clip mas un margen
        double duracion = videoTutorial.clip != null ? videoTutorial.clip.length : videoTutorial.length;
        float limite = duracion > 0 ? (float)duracion + 2f : 60f;
        float t = 0f;
        while (!terminado && t < limite)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        videoTutorial.loopPointReached -= alTerminar;
        videoTutorial.Stop();

        if (camaraOcular != null)
            pantalla.transform.SetParent(_padreOriginalPantalla, true);
        pantalla.SetActive(false);
        Debug.Log("[Intro] Video tutorial terminado. Ahora el logo.");

        if (logoMoriche != null)
        {
            // se prende transparente: el fade in de RutinaIntro lo hace aparecer
            logoMoriche.SetActive(true);
            ActualizarAlfaLogo(0f);
        }
    }

    /// <summary>
    /// Modifica el canal alfa en cualquier tipo de componente (SpriteRenderer, CanvasGroup, Image, Material).
    /// </summary>
    private void ActualizarAlfaLogo(float alfa)
    {
        // A) CanvasGroups de UI
        if (_canvasGroups != null)
        {
            for (int i = 0; i < _canvasGroups.Length; i++)
            {
                if (_canvasGroups[i] != null) _canvasGroups[i].alpha = alfa;
            }
        }

        // B) SpriteRenderers 2D
        if (_sprites != null)
        {
            for (int i = 0; i < _sprites.Length; i++)
            {
                if (_sprites[i] != null)
                {
                    Color c = _sprites[i].color;
                    c.a = alfa;
                    _sprites[i].color = c;
                }
            }
        }

        // C) UI Images
        if (_uiImages != null)
        {
            for (int i = 0; i < _uiImages.Length; i++)
            {
                if (_uiImages[i] != null)
                {
                    Color c = _uiImages[i].color;
                    c.a = alfa;
                    _uiImages[i].color = c;
                }
            }
        }

        // D) Renderers 3D / Materiales con _BaseColor o _Color
        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                {
                    foreach (var mat in _renderers[i].materials)
                    {
                        if (mat == null) continue;
                        if (mat.HasProperty("_BaseColor"))
                        {
                            Color c = mat.GetColor("_BaseColor");
                            c.a = alfa;
                            mat.SetColor("_BaseColor", c);
                        }
                        else if (mat.HasProperty("_Color"))
                        {
                            Color c = mat.color;
                            c.a = alfa;
                            mat.color = c;
                        }
                    }
                }
            }
        }
    }
}
