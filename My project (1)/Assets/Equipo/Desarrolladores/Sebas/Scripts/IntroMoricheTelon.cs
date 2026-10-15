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

    [Tooltip("Si esta activo, el logo flota siguiendo suavemente hacia donde gires la cabeza")]
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

    [Header("Control de Jugador")]
    [Tooltip("Desactiva temporalmente el script PinchToWalk durante la intro y lo reactiva al abrir el telon")]
    public bool bloquearCaminataEnIntro = true;

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

        // 3. Colocar el logo justo frente a la mirada inicial
        if (logoMoriche != null)
        {
            logoMoriche.SetActive(true);
            ActualizarAlfaLogo(1.0f);

            if (camaraOcular != null)
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
        if (!_introEnProgreso || logoMoriche == null || camaraOcular == null) return;

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

        // 1. Tiempo que el logo permanece 100% visible
        yield return new WaitForSeconds(tiempoVisible);

        // 2. Fade Out de transparencia del logo
        float t = 0f;
        Vector3 escalaInicial = (logoMoriche != null) ? logoMoriche.transform.localScale : Vector3.one;

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

        _introEnProgreso = false;
        OnTelonAbierto?.Invoke();
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
