using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controlador completo de la Barra de Progreso de Pesca para la escena de Sebas.
/// Configurado con la estructura exacta de 4 rellenos progresivos y 2 rellenos de iconos
/// (los hitos salen de MejoraRed si está asignado; por defecto hito 1 = pez 3, hito 2 = pez 7):
/// - Relleno Bueno 1: Antes del hito 1 (Verde / Calma inicial)
/// - Relleno Bueno 2: Del hito 1 al hito 2 (Amarillo / Primera mejora de red)
/// - Relleno Bueno 3: Del hito 2 al pez 9 (Naranja / Tensión alta previa al clímax)
/// - Relleno Final Malvado: Pez 10 (Rojo sangre / Clímax persecución)
/// - El llenado va por tramos: el pez de cada hito cae justo sobre su raya divisoria.
/// - Relleno Iconos Bueno: Peces 1 a 9
/// - Relleno Iconos Malo: Pez 10 (Se corrompen los círculos de hitos)
/// - Iconos superiores, contornos y divisiones.
/// - Sistema de impacto (Punch Scale), temblor de terror y Billboard en VR.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Llaneritos/UI/Barra de Progreso de Pesca (FishingProgressBarUI)")]
public class FishingProgressBarUI : MonoBehaviour
{
    [Header("Referencias del Director")]
    [Tooltip("El director de pesca y Silbón en la escena. Si está vacío, se busca automáticamente.")]
    public FishingSilbonDirector director;

    [Header("Hitos (sincronizados con la mejora de la red)")]
    [Tooltip("Si está asignado, los hitos se toman de los niveles 1 y 2 de MejoraRed y se ignoran los dos campos de abajo.")]
    public MejoraRed mejoraRed;

    [Tooltip("Pez del hito 1 (cambio a Relleno Bueno 2) si no hay MejoraRed asignado.")]
    [Min(1)] public int pezHito1 = 3;

    [Tooltip("Pez del hito 2 (cambio a Relleno Bueno 3) si no hay MejoraRed asignado.")]
    [Min(1)] public int pezHito2 = 7;

    [Tooltip("Posición de la raya divisoria 1 dentro del sprite (0 = borde izquierdo, 1 = derecho). Medida en 'Divisiones barra.png'.")]
    [Range(0f, 1f)] public float posicionDivision1 = 0.317f;

    [Tooltip("Posición de la raya divisoria 2 dentro del sprite. Medida en 'Divisiones barra.png'.")]
    [Range(0f, 1f)] public float posicionDivision2 = 0.682f;

    [Header("Estructura de la Barra")]
    [Tooltip("El RectTransform padre de todos los elementos visuales de la barra.")]
    public RectTransform rootBarra;

    [Tooltip("Imagen de fondo base de la barra.")]
    public Image fondoBase;

    [Tooltip("Imagen del marco exterior de la barra.")]
    public Image contornoBarra;

    [Tooltip("Líneas divisorias internas de los peces.")]
    public Image divisiones;

    [Header("Los 4 Rellenos de la Barra (Llenado Horizontal Izq -> Der)")]
    [Tooltip("Relleno activo antes del hito 1 (por defecto peces 0 a 2). Color verde / calma.")]
    public Image rellenoBueno1;

    [Tooltip("Relleno activo del hito 1 al hito 2 (por defecto peces 3 a 6). Color amarillo / primera mejora.")]
    public Image rellenoBueno2;

    [Tooltip("Relleno activo del hito 2 al pez 9 (por defecto peces 7 a 9). Color naranja / tensión alta.")]
    public Image rellenoBueno3;

    [Tooltip("Relleno activo en el décimo pez (10). Color rojo sangre siniestro.")]
    public Image rellenoFinalMalvado;

    [Header("Círculos de Hitos e Iconos")]
    [Tooltip("Marco/contorno de los círculos superiores de iconos.")]
    public Image contornoCirculos;

    [Tooltip("Relleno de los círculos de hitos en estado bueno (peces 1 a 9).")]
    public Image rellenoIconosBueno;

    [Tooltip("Relleno de los círculos de hitos en estado malvado (pez 10).")]
    public Image rellenoIconosMalo;

    [Tooltip("Iconos que van encima de los círculos.")]
    public Image iconos;

    [Header("Partículas y Efectos")]
    [Tooltip("Sistema de partículas que acompaña los eventos de la barra.")]
    public ParticleSystem particulasAdicionales;

    [Header("Configuración de Llenado y Transición")]
    [Tooltip("Tiempo en segundos que tarda la barra en deslizarse suavemente hacia el nuevo valor.")]
    [Range(0.05f, 1.0f)]
    public float duracionSuavizado = 0.35f;

    [Tooltip("Velocidad de transición/disolución suave entre un color de relleno y el siguiente.")]
    [Range(1.0f, 10.0f)]
    public float velocidadTransicionRellenos = 4.0f;

    [Tooltip("Escala multiplicadora al depositar un pez (efecto de pop/punch).")]
    [Range(1.0f, 1.3f)]
    public float escalaImpacto = 1.08f;

    [Tooltip("Duración en segundos del rebote al meter un pez.")]
    [Range(0.1f, 0.6f)]
    public float duracionImpacto = 0.25f;

    [Header("Comportamiento de Terror (Silbón)")]
    [Tooltip("Tinte rojizo en marco y divisiones cuando se acerca el clímax.")]
    public Color colorTinteTerror = new Color(0.95f, 0.25f, 0.25f, 1.0f);

    [Tooltip("Si está activo, el temblor solo ocurre de arriba a abajo (eje Y), eliminando sacudidas horizontales.")]
    public bool temblorSoloVertical = true;

    [Tooltip("Intensidad del temblor errático cuando el Silbón acecha.")]
    public float intensidadTemblor = 3.0f;

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

    // Estados internos de interpolación
    private int _ultimoPeces = -1;
    private float _fillActual = 0f;
    private float _fillVelocidad = 0f;
    private float _alphaB1 = 1f;
    private float _alphaB2 = 0f;
    private float _alphaB3 = 0f;
    private float _alphaMal = 0f;
    private float _alphaIconosBueno = 1f;
    private float _alphaIconosMalo = 0f;

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

        // Asegurar modo Filled Horizontal en los 4 rellenos de la barra
        ConfigurarModoFilled(rellenoBueno1);
        ConfigurarModoFilled(rellenoBueno2);
        ConfigurarModoFilled(rellenoBueno3);
        ConfigurarModoFilled(rellenoFinalMalvado);

        // Estado visual inicial
        SetAlpha(rellenoBueno1, 1f);
        SetAlpha(rellenoBueno2, 0f);
        SetAlpha(rellenoBueno3, 0f);
        SetAlpha(rellenoFinalMalvado, 0f);

        SetAlpha(rellenoIconosBueno, 1f);
        SetAlpha(rellenoIconosMalo, 0f);
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
            _fillActual = FillParaPeces(director.currentFishCount);
            ActualizarFillEnRellenos(_fillActual);
        }
    }

    private void Update()
    {
        ActualizarProgresoPesca();
        ActualizarFasesYRellenos();
        AplicarEfectosVisualesTerror();
        AplicarBillboardVR();
    }

    /// <summary>
    /// Calcula el fillAmount según los peces y anima suavemente el avance.
    /// </summary>
    private void ActualizarProgresoPesca()
    {
        if (director == null) return;

        int pecesActuales = director.currentFishCount;
        float fillObjetivo = FillParaPeces(pecesActuales);

        // Si entró un nuevo pez, disparar pop/rebote
        if (_ultimoPeces >= 0 && pecesActuales > _ultimoPeces)
        {
            DispararImpactoPez();
        }
        _ultimoPeces = pecesActuales;

        // Deslizamiento suave
        _fillActual = Mathf.SmoothDamp(_fillActual, fillObjetivo, ref _fillVelocidad, duracionSuavizado);
        ActualizarFillEnRellenos(_fillActual);
    }

    public int Hito1
    {
        get
        {
            int h = mejoraRed != null ? mejoraRed.PezRequeridoNivel(1) : -1;
            return h > 0 ? h : pezHito1;
        }
    }

    public int Hito2
    {
        get
        {
            int h = mejoraRed != null ? mejoraRed.PezRequeridoNivel(2) : -1;
            return h > 0 ? h : pezHito2;
        }
    }

    /// <summary>
    /// fillAmount por tramos: 0 peces = 0, hito 1 = raya 1, hito 2 = raya 2, total = 1.
    /// Así el pez de cada hito queda justo sobre la raya dibujada en el sprite.
    /// </summary>
    public float FillParaPeces(float peces)
    {
        int total = director != null ? Mathf.Max(1, director.totalFishNeeded) : 10;
        int h1 = Mathf.Clamp(Hito1, 1, total - 1);
        int h2 = Mathf.Clamp(Hito2, h1 + 1, total);

        if (peces <= 0f) return 0f;
        if (peces < h1) return Mathf.Lerp(0f, posicionDivision1, peces / h1);
        if (peces < h2) return Mathf.Lerp(posicionDivision1, posicionDivision2, (peces - h1) / (h2 - h1));
        if (h2 >= total) return 1f;
        return Mathf.Lerp(posicionDivision2, 1f, Mathf.Clamp01((peces - h2) / (total - h2)));
    }

    private void ActualizarFillEnRellenos(float fill)
    {
        if (rellenoBueno1 != null) rellenoBueno1.fillAmount = fill;
        if (rellenoBueno2 != null) rellenoBueno2.fillAmount = fill;
        if (rellenoBueno3 != null) rellenoBueno3.fillAmount = fill;
        if (rellenoFinalMalvado != null) rellenoFinalMalvado.fillAmount = fill;
    }

    /// <summary>
    /// Maneja la activación y cross-fade suave entre los 4 rellenos de barra y los 2 de iconos.
    /// </summary>
    private void ActualizarFasesYRellenos()
    {
        if (director == null) return;

        int peces = director.currentFishCount;
        int total = Mathf.Max(1, director.totalFishNeeded);
        int hito1 = Hito1;
        int hito2 = Hito2;

        // Alphas objetivo según el rango de peces
        float targetB1 = 0f;
        float targetB2 = 0f;
        float targetB3 = 0f;
        float targetMal = 0f;

        float targetIconoBueno = 1f;
        float targetIconoMalo = 0f;

        if (peces < hito1)
        {
            // Fase 1: Calma (antes del hito 1)
            targetB1 = 1f;
            targetIconoBueno = 1f;
            targetIconoMalo = 0f;
        }
        else if (peces < hito2)
        {
            // Fase 2: Primera mejora de red (del hito 1 al hito 2)
            targetB2 = 1f;
            targetIconoBueno = 1f;
            targetIconoMalo = 0f;
        }
        else if (peces < total)
        {
            // Fase 3: Tensión previa al clímax (del hito 2 al pez 9)
            targetB3 = 1f;
            targetIconoBueno = 1f;
            targetIconoMalo = 0f;
        }
        else // peces >= 10
        {
            // Fase 4: Clímax terror total / Persecución
            targetMal = 1f;
            targetIconoBueno = 0f;
            targetIconoMalo = 1f;
        }

        // Transición suave de transparencia (Alpha)
        float step = Time.deltaTime * velocidadTransicionRellenos;
        _alphaB1 = Mathf.MoveTowards(_alphaB1, targetB1, step);
        _alphaB2 = Mathf.MoveTowards(_alphaB2, targetB2, step);
        _alphaB3 = Mathf.MoveTowards(_alphaB3, targetB3, step);
        _alphaMal = Mathf.MoveTowards(_alphaMal, targetMal, step);

        _alphaIconosBueno = Mathf.MoveTowards(_alphaIconosBueno, targetIconoBueno, step);
        _alphaIconosMalo = Mathf.MoveTowards(_alphaIconosMalo, targetIconoMalo, step);

        SetAlpha(rellenoBueno1, _alphaB1);
        SetAlpha(rellenoBueno2, _alphaB2);
        SetAlpha(rellenoBueno3, _alphaB3);
        SetAlpha(rellenoFinalMalvado, _alphaMal);

        SetAlpha(rellenoIconosBueno, _alphaIconosBueno);
        SetAlpha(rellenoIconosMalo, _alphaIconosMalo);

        // Tinte de marcos cuando llega al estado malvado
        if (contornoBarra != null)
            contornoBarra.color = Color.Lerp(Color.white, colorTinteTerror, _alphaMal);

        if (divisiones != null)
            divisiones.color = Color.Lerp(Color.white, colorTinteTerror, _alphaMal);
    }

    /// <summary>
    /// Escribe en la consola dónde cae cada pez en la barra y si los hitos coinciden con las rayas.
    /// </summary>
    [ContextMenu("Diagnosticar hitos")]
    public void DiagnosticarHitos()
    {
        int total = director != null ? Mathf.Max(1, director.totalFishNeeded) : 10;
        var sb = new System.Text.StringBuilder("[Barra] DIAGNOSTICO DE HITOS\n");
        sb.AppendLine($"  Hito 1: pez {Hito1} | Hito 2: pez {Hito2} | Total: {total} (fuente: {(mejoraRed != null ? "MejoraRed" : "campos de la barra")})");
        if (mejoraRed == null) sb.AppendLine("  AVISO  MejoraRed no asignado: los hitos de la barra y de la red pueden desfasarse.");
        for (int p = 0; p <= total; p++)
        {
            string marca = p == Hito1 ? "  <- raya 1" : p == Hito2 ? "  <- raya 2" : "";
            sb.AppendLine($"  Pez {p,2}: llenado {FillParaPeces(p):P1}{marca}");
        }
        Debug.Log(sb.ToString(), this);
    }

    /// <summary>
    /// Aplica temblores y pulsación siniestra conforme el terror crece.
    /// </summary>
    private void AplicarEfectosVisualesTerror()
    {
        if (rootBarra == null || director == null) return;

        int peces = director.currentFishCount;
        float factorTerror = 0f;

        if (peces >= 4)
            factorTerror = Mathf.Clamp01((float)(peces - 3) / 7f);

        // Temblor procedural (Perlin Noise) a partir de pez 7 o en pez 10 (exclusivamente vertical de arriba a abajo)
        Vector3 offsetTemblor = Vector3.zero;
        if (peces >= 7)
        {
            float noiseX = temblorSoloVertical ? 0f : (Mathf.PerlinNoise(Time.time * velocidadTemblor, 0f) - 0.5f) * 2f;
            float noiseY = (Mathf.PerlinNoise(0f, Time.time * velocidadTemblor) - 0.5f) * 2f;
            offsetTemblor = new Vector3(noiseX * (intensidadTemblor * factorTerror), noiseY * (intensidadTemblor * factorTerror), 0f);
        }

        // Latido rítmico
        float multiplicadorLatido = 1f;
        if (activarPulsacionLatido && peces >= 7)
        {
            multiplicadorLatido = 1f + Mathf.Sin(Time.time * frecuenciaLatido) * (0.035f * factorTerror);
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
    /// Reacción de rebote (pop) y partículas al meter un pez.
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

        float t = 0f;
        while (t < mitadDuracion)
        {
            t += Time.deltaTime;
            rootBarra.localScale = Vector3.Lerp(escalaInicial, escalaMax, t / mitadDuracion);
            yield return null;
        }

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
    /// Auto-vincula todas las referencias buscando por los nombres exactos en la jerarquía.
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

        // Fondo y contornos principales
        if (fondoBase == null) fondoBase = BuscarHijoImage(contenedor, "Fondo base", "fondo");
        if (contornoBarra == null) contornoBarra = BuscarHijoImage(contenedor, "Contorno barra", "contorno");
        if (divisiones == null) divisiones = BuscarHijoImage(contenedor, "Divisiones", "divisiones barra");

        // Los 4 rellenos de barra
        if (rellenoBueno1 == null) rellenoBueno1 = BuscarHijoImage(contenedor, "bueno 1", "Relleno bueno barra 1", "relleno bueno 1", "bueno1");
        if (rellenoBueno2 == null) rellenoBueno2 = BuscarHijoImage(contenedor, "bueno 2", "Relleno bueno barra 2", "relleno bueno 2", "bueno2");
        if (rellenoBueno3 == null) rellenoBueno3 = BuscarHijoImage(contenedor, "bueno 3", "Relleno bueno barra 3", "relleno bueno 3", "bueno3");
        if (rellenoFinalMalvado == null) rellenoFinalMalvado = BuscarHijoImage(contenedor, "relleno final malvado", "Relleno final malvado barra", "relleno malvado", "malvado");

        // Círculos de hitos e iconos
        if (contornoCirculos == null) contornoCirculos = BuscarHijoImage(contenedor, "Contorno circulos", "contorno de los iconos", "Contorno círculo icono");
        if (rellenoIconosBueno == null) rellenoIconosBueno = BuscarHijoImage(contenedor, "relleno iconos bueno", "Relleno bueno contorno círculo icono", "iconos bueno");
        if (rellenoIconosMalo == null) rellenoIconosMalo = BuscarHijoImage(contenedor, "relleno iconos malo", "Relleno malo", "Relleno malo .png", "iconos malo");
        if (iconos == null) iconos = BuscarHijoImage(contenedor, "Iconos", "icono");

        // Partículas
        if (particulasAdicionales == null)
        {
            Transform tPart = contenedor.Find("particulas adicionales") ?? contenedor.Find("particulas") ?? transform.Find("particulas adicionales");
            if (tPart != null) particulasAdicionales = tPart.GetComponent<ParticleSystem>();
        }

        if (director == null)
            director = FindFirstObjectByType<FishingSilbonDirector>();

        // Asegurar modo Filled en los 4 rellenos de la barra
        ConfigurarModoFilled(rellenoBueno1);
        ConfigurarModoFilled(rellenoBueno2);
        ConfigurarModoFilled(rellenoBueno3);
        ConfigurarModoFilled(rellenoFinalMalvado);
    }

    private Image BuscarHijoImage(Transform padre, params string[] posiblesNombres)
    {
        if (padre == null) return null;

        // Búsqueda directa
        foreach (var nom in posiblesNombres)
        {
            Transform hijo = padre.Find(nom);
            if (hijo != null)
            {
                Image img = hijo.GetComponent<Image>();
                if (img != null) return img;
            }
        }

        // Búsqueda insensible a mayúsculas
        foreach (Transform child in padre)
        {
            string childName = child.name.Trim().ToLower();
            foreach (var nom in posiblesNombres)
            {
                if (childName.Equals(nom.Trim().ToLower()) || childName.Contains(nom.Trim().ToLower()))
                {
                    Image img = child.GetComponent<Image>();
                    if (img != null) return img;
                }
            }
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

    [ContextMenu("Probar Pez 10 (Clímax)")]
    private void DebugPez10()
    {
        if (director != null)
            director.SetProgressNormalized(1f);
    }
#endif
}
