using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controlador de la Barra de Progreso de Pesca para la escena de Sebas.
/// Se vincula automáticamente con la jerarquía del Canvas y con FishingSilbonDirector:
/// - Aumenta el progreso suavemente conforme se meten peces al barril.
/// - Genera un efecto de rebote ("Punch Scale") y partículas al depositar un pez.
/// - Reacciona al cambio de atmósfera (de calma a terror/Silbón):
///     * Transición y disolución alfa entre Relleno Bueno y Relleno Malvado.
///     * Cambio de color progresivo en contornos e iconos hacia tonos de sangre/pesadilla.
///     * Temblor y pulsación siniestra (latido) al acercarse al clímax.
/// - Incluye modo Billboard para Realidad Virtual (VR).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Llaneritos/UI/Barra de Progreso de Pesca (FishingProgressBarUI)")]
public class FishingProgressBarUI : MonoBehaviour
{
    [Header("Referencias del Director")]
    [Tooltip("El director de pesca y Silbón en la escena. Si está vacío, se busca automáticamente.")]
    public FishingSilbonDirector director;

    [Header("Elementos de la Jerarquía (Auto-buscados si están vacíos)")]
    [Tooltip("El RectTransform padre de todos los elementos visuales de la barra.")]
    public RectTransform rootBarra;

    [Tooltip("Imagen de fondo base de la barra.")]
    public Image fondoBase;

    [Tooltip("Imagen del relleno bueno (progreso normal en calma). Debe estar en modo Filled Horizontal.")]
    public Image rellenoBueno;

    [Tooltip("Imagen del relleno malvado (progreso siniestro de terror). Debe estar en modo Filled Horizontal.")]
    public Image rellenoMalvado;

    [Tooltip("Imagen de las líneas divisorias de la barra.")]
    public Image divisiones;

    [Tooltip("Imagen del contorno/marco de la barra.")]
    public Image contornoBarra;

    [Tooltip("Imagen de los contornos de los círculos superiores de hitos.")]
    public Image contornoCirculos;

    [Tooltip("Imagen de los iconos dentro de los círculos.")]
    public Image iconos;

    [Tooltip("Sistema de partículas que acompaña los eventos de la barra.")]
    public ParticleSystem particulasAdicionales;

    [Header("Configuración de Llenado Suave")]
    [Tooltip("Tiempo en segundos que tarda la barra en deslizarse suavemente hacia el nuevo valor.")]
    [Range(0.05f, 1.0f)]
    public float duracionSuavizado = 0.35f;

    [Tooltip("Escala multiplicadora al depositar un pez (efecto de pop/punch).")]
    [Range(1.0f, 1.3f)]
    public float escalaImpacto = 1.08f;

    [Tooltip("Duración en segundos del rebote al meter un pez.")]
    [Range(0.1f, 0.6f)]
    public float duracionImpacto = 0.25f;

    [Header("Comportamiento del Estado Malvado / Terror")]
    [Tooltip("Color de contornos e iconos durante la fase de calma diurna.")]
    public Color colorCalma = Color.white;

    [Tooltip("Color de contornos e iconos cuando el ambiente se vuelve hostil/rojo.")]
    public Color colorTerror = new Color(0.95f, 0.28f, 0.28f, 1.0f);

    [Tooltip("Velocidad de transición entre el estado bueno y el malvado.")]
    public float velocidadTransicionMala = 2.0f;

    [Tooltip("Intensidad del temblor errático cuando el Silbón acecha.")]
    public float intensidadTemblor = 4.0f;

    [Tooltip("Velocidad de oscilación del temblor siniestro.")]
    public float velocidadTemblor = 18.0f;

    [Tooltip("Activa una pulsación rítmica tipo latido en la barra cuando el terror es alto.")]
    public bool activarPulsacionLatido = true;

    [Tooltip("Frecuencia del latido de terror.")]
    public float frecuenciaLatido = 3.5f;

    [Header("Modo VR (Billboard hacia el Jugador)")]
    [Tooltip("Hace que la barra siempre esté orientada de frente hacia la cabeza del jugador.")]
    public bool orientarHaciaJugador = true;

    [Tooltip("Bloquea la inclinación vertical para que la barra se mantenga recta en el horizonte.")]
    public bool bloquearEjeVertical = true;

    [Tooltip("Transform de la cámara del jugador. Si está vacío, busca Camera.main.")]
    public Transform camaraJugador;

    // Estados internos
    private int _ultimoPeces = -1;
    private float _fillActual = 0f;
    private float _fillVelocidad = 0f;
    private float _pesoMaloActual = 0f;
    private Vector3 _escalaOriginalRoot = Vector3.one;
    private Vector3 _posicionOriginalRoot = Vector3.zero;
    private Coroutine _rutinaRebote;

    private void Reset()
    {
        AutoVincularReferencias();
    }

    private void Awake()
    {
        AutoVincularReferencias();

        if (rootBarra != null)
        {
            _escalaOriginalRoot = rootBarra.localScale;
            _posicionOriginalRoot = rootBarra.localPosition;
        }

        // Configurar imágenes de relleno en modo Filled
        ConfigurarModoFilled(rellenoBueno);
        ConfigurarModoFilled(rellenoMalvado);

        // Estado inicial de rellenos
        if (rellenoBueno != null)
        {
            rellenoBueno.fillAmount = 0f;
            SetAlpha(rellenoBueno, 1f);
        }
        if (rellenoMalvado != null)
        {
            rellenoMalvado.fillAmount = 0f;
            SetAlpha(rellenoMalvado, 0f);
        }
    }

    private void Start()
    {
        if (director == null)
            director = FindFirstObjectByType<FishingSilbonDirector>();

        if (camaraJugador == null && Camera.main != null)
            camaraJugador = Camera.main.transform;

        if (director != null)
        {
            _ultimoPeces = director.currentFishCount;
            float targetFill = director.totalFishNeeded > 0 ? (float)director.currentFishCount / director.totalFishNeeded : 0f;
            _fillActual = targetFill;
            if (rellenoBueno != null) rellenoBueno.fillAmount = _fillActual;
            if (rellenoMalvado != null) rellenoMalvado.fillAmount = _fillActual;
        }
    }

    private void Update()
    {
        ActualizarProgresoPesca();
        ActualizarEstadoMalvado();
        AplicarEfectosVisualesTerror();
        AplicarBillboardVR();
    }

    /// <summary>
    /// Monitorea los peces en el barril y anima suavemente el avance.
    /// </summary>
    private void ActualizarProgresoPesca()
    {
        if (director == null) return;

        int pecesActuales = director.currentFishCount;
        int totalNecesario = Mathf.Max(1, director.totalFishNeeded);
        float fillObjetivo = Mathf.Clamp01((float)pecesActuales / totalNecesario);

        // Detectar si entró un nuevo pez para disparar impacto visual
        if (_ultimoPeces >= 0 && pecesActuales > _ultimoPeces)
        {
            DispararImpactoPez();
        }
        _ultimoPeces = pecesActuales;

        // Interpolación suave del fillAmount
        _fillActual = Mathf.SmoothDamp(_fillActual, fillObjetivo, ref _fillVelocidad, duracionSuavizado);

        if (rellenoBueno != null)
            rellenoBueno.fillAmount = _fillActual;

        if (rellenoMalvado != null)
            rellenoMalvado.fillAmount = _fillActual;
    }

    /// <summary>
    /// Calcula y transiciona el peso de transformación malvada según el director.
    /// </summary>
    private void ActualizarEstadoMalvado()
    {
        if (director == null) return;

        // Leer el peso de terror del director
        float pesoTerrorObjetivo = director.CurrentTerrorWeight;

        // Respaldo de cálculo si el director aún no elevó el terror
        if (director.totalFishNeeded > 0)
        {
            int pezInicioAcecho = director.pezInicioAcecho;
            if (director.currentFishCount >= pezInicioAcecho)
            {
                float progresoAcecho = (float)(director.currentFishCount - pezInicioAcecho) / (director.totalFishNeeded - pezInicioAcecho);
                pesoTerrorObjetivo = Mathf.Max(pesoTerrorObjetivo, Mathf.Clamp01(progresoAcecho));
            }
        }

        // Si ya comenzó la persecución o llegamos al 100%, terror total
        if (director.currentFishCount >= director.totalFishNeeded)
            pesoTerrorObjetivo = 1.0f;

        // Suavizar la transición hacia el estado malvado
        _pesoMaloActual = Mathf.MoveTowards(_pesoMaloActual, pesoTerrorObjetivo, Time.deltaTime * velocidadTransicionMala);

        // Cross-fade entre Relleno Bueno y Relleno Malvado
        if (rellenoBueno != null)
            SetAlpha(rellenoBueno, 1f - _pesoMaloActual);

        if (rellenoMalvado != null)
            SetAlpha(rellenoMalvado, _pesoMaloActual);

        // Tinte de color en los contornos e iconos
        Color colorActual = Color.Lerp(colorCalma, colorTerror, _pesoMaloActual);

        if (contornoBarra != null) contornoBarra.color = colorActual;
        if (contornoCirculos != null) contornoCirculos.color = colorActual;
        if (iconos != null) iconos.color = colorActual;
        if (divisiones != null) divisiones.color = colorActual;
    }

    /// <summary>
    /// Aplica temblores siniestros y latido rítmico conforme el terror se apodera del entorno.
    /// </summary>
    private void AplicarEfectosVisualesTerror()
    {
        if (rootBarra == null) return;

        // Temblor por vibración del Silbón (Perlin Noise)
        Vector3 offsetTemblor = Vector3.zero;
        if (_pesoMaloActual > 0.15f)
        {
            float noiseX = (Mathf.PerlinNoise(Time.time * velocidadTemblor, 0f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0f, Time.time * velocidadTemblor) - 0.5f) * 2f;
            offsetTemblor = new Vector3(noiseX, noiseY, 0f) * (intensidadTemblor * _pesoMaloActual);
        }

        // Latido o pulsación siniestra
        float multiplicadorLatido = 1f;
        if (activarPulsacionLatido && _pesoMaloActual > 0.35f)
        {
            multiplicadorLatido = 1f + Mathf.Sin(Time.time * frecuenciaLatido) * (0.04f * _pesoMaloActual);
        }

        if (_rutinaRebote == null)
        {
            rootBarra.localScale = _escalaOriginalRoot * multiplicadorLatido;
        }

        rootBarra.localPosition = _posicionOriginalRoot + offsetTemblor;
    }

    /// <summary>
    /// Hace que el Canvas en World Space mire siempre al jugador en VR.
    /// </summary>
    private void AplicarBillboardVR()
    {
        if (!orientarHaciaJugador || camaraJugador == null) return;

        Vector3 direccion = transform.position - camaraJugador.position;
        if (bloquearEjeVertical)
            direccion.y = 0f;

        if (direccion.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(direccion);
        }
    }

    /// <summary>
    /// Reacción jugosa al meter un pez: rebote de escala y ráfaga de partículas.
    /// </summary>
    public void DispararImpactoPez()
    {
        if (_rutinaRebote != null)
            StopCoroutine(_rutinaRebote);

        _rutinaRebote = StartCoroutine(RutinaReboteScale());

        if (particulasAdicionales != null)
        {
            particulasAdicionales.Play();
        }
    }

    private IEnumerator RutinaReboteScale()
    {
        if (rootBarra == null) yield break;

        Vector3 escalaInicial = _escalaOriginalRoot;
        Vector3 escalaMax = _escalaOriginalRoot * escalaImpacto;
        float mitadDuracion = duracionImpacto * 0.4f;

        // Subida rápida
        float t = 0f;
        while (t < mitadDuracion)
        {
            t += Time.deltaTime;
            rootBarra.localScale = Vector3.Lerp(escalaInicial, escalaMax, t / mitadDuracion);
            yield return null;
        }

        // Regreso suave
        t = 0f;
        float duracionRegreso = duracionImpacto - mitadDuracion;
        while (t < duracionRegreso)
        {
            t += Time.deltaTime;
            rootBarra.localScale = Vector3.Lerp(escalaMax, escalaInicial, t / duracionRegreso);
            yield return null;
        }

        rootBarra.localScale = escalaInicial;
        _rutinaRebote = null;
    }

    private void ConfigurarModoFilled(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private void SetAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = Mathf.Clamp01(alpha);
        img.color = c;
    }

    /// <summary>
    /// Busca y enlaza automáticamente todos los componentes de la jerarquía por nombre.
    /// </summary>
    [ContextMenu("Auto-Vincular Jerarquía")]
    public void AutoVincularReferencias()
    {
        if (rootBarra == null)
        {
            Transform tRoot = transform.Find("Root de la barra") ?? transform;
            rootBarra = tRoot as RectTransform ?? tRoot.GetComponent<RectTransform>();
        }

        Transform contenedor = rootBarra != null ? rootBarra.transform : transform;

        if (fondoBase == null) fondoBase = BuscarHijoImage(contenedor, "Fondo base");
        if (rellenoBueno == null) rellenoBueno = BuscarHijoImage(contenedor, "Relleno bueno");
        if (rellenoMalvado == null) rellenoMalvado = BuscarHijoImage(contenedor, "Relleno malvado");
        if (divisiones == null) divisiones = BuscarHijoImage(contenedor, "Divisiones");
        if (contornoBarra == null) contornoBarra = BuscarHijoImage(contenedor, "Contorno barra");
        if (contornoCirculos == null) contornoCirculos = BuscarHijoImage(contenedor, "Contorno circulos");
        if (iconos == null) iconos = BuscarHijoImage(contenedor, "Iconos");

        if (particulasAdicionales == null)
        {
            Transform tPart = contenedor.Find("particulas adicionales") ?? transform.Find("particulas adicionales");
            if (tPart != null) particulasAdicionales = tPart.GetComponent<ParticleSystem>();
        }

        if (director == null)
            director = FindFirstObjectByType<FishingSilbonDirector>();

        // Asegurar modo Filled en los rellenos
        ConfigurarModoFilled(rellenoBueno);
        ConfigurarModoFilled(rellenoMalvado);
    }

    private Image BuscarHijoImage(Transform padre, string nombre)
    {
        if (padre == null) return null;
        Transform hijo = padre.Find(nombre);
        if (hijo != null) return hijo.GetComponent<Image>();

        // Búsqueda insensible a mayúsculas/espacios
        foreach (Transform child in padre)
        {
            if (child.name.Trim().ToLower().Equals(nombre.Trim().ToLower()))
                return child.GetComponent<Image>();
        }
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("Probar Añadir Pez (+1)")]
    private void DebugSumarPez()
    {
        if (director != null)
            director.AddFish(1);
    }

    [ContextMenu("Probar Reiniciar Barra (0)")]
    private void DebugReiniciar()
    {
        if (director != null)
            director.SetProgressNormalized(0f);
    }

    [ContextMenu("Probar Terror al 100%")]
    private void DebugTerrorTotal()
    {
        _pesoMaloActual = 1.0f;
    }
#endif
}
