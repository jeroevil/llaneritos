using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Oculus.Interaction;

/// <summary>
/// Red de pesca (atarraya).
/// - Si la sueltas, se cae donde esté (con la velocidad de tu mano).
/// - Si la lanzas con fuerza, se abre en el aire.
/// - Si cae al agua: queda extendida, se cierra atrapando peces y vuelve sola a la orilla.
/// - Si cae llena sobre la mesa: se despliega y los peces quedan encima como objetos físicos.
///
/// La forma de la red viene de los blendshapes de "animacion red tirar (1).fbx"
/// (simulación de tela de Blender). El clip también trae movimiento de raíz en
/// unidades de Blender, así que solo se usan las formas y el recorrido lo calcula este script.
///
/// En el editor (Play Mode) hay un panel de prueba: [Espacio] lanzar, [M] soltar llena sobre la mesa, [R] reiniciar.
/// </summary>
public class RedPescaController : MonoBehaviour
{
    public enum Estado { ListaParaRecoger, EnMano, Volando, Asentando, Extendida, Cerrandose, Regresando, Subiendo, Desplegando, EnMesa }

    [System.Serializable] public class EventoPeces : UnityEvent<int> { }

    [Header("Referencias")]
    [SerializeField] SkinnedMeshRenderer mallaRed;
    [SerializeField] AnimationClip animacionRed;
    [SerializeField] Grabbable grabbable;
    [Tooltip("Objeto con HandGrabInteractable / GrabInteractable. Se apaga mientras la red no se puede agarrar.")]
    [SerializeField] GameObject interactables;
    [SerializeField] BoxCollider colliderAgarre;

    [Header("Puntos")]
    [Tooltip("Donde queda la red cuando vuelve del agua (arriba, en la orilla).")]
    [SerializeField] Transform puntoRecoger;
    [Tooltip("Punto sobre el agua al pie de la orilla, adonde llega la red flotando antes de subir.")]
    [SerializeField] Transform puntoOrillaAgua;
    [Tooltip("Altura (Y) de la superficie del agua tal como se ve. El shader del agua la sube unos 3 m sobre la malla de WaterBlock_50m.")]
    [SerializeField] float alturaAgua = -3.1f;

    [Header("Lanzamiento")]
    [Tooltip("Multiplica la velocidad de la mano al lanzar, para que la red llegue al agua.")]
    [SerializeField] float multiplicadorLanzamiento = 1.5f;
    [Tooltip("Por debajo de esta velocidad soltar la red no es un lanzamiento: solo se cae y no se abre.")]
    [SerializeField] float velocidadMinima = 1.2f;
    [SerializeField] float gravedad = 9.81f;
    [Tooltip("Si después de esto sigue cayendo (se salió del mapa), vuelve a la orilla.")]
    [SerializeField] float tiempoMaximoVuelo = 6f;

    [Header("Forma de la red")]
    [Tooltip("Segundo del clip en que la red queda totalmente abierta.")]
    [SerializeField] float tiempoClipAbierta = 2f;
    [Tooltip("Segundos de vuelo que tarda en abrirse del todo.")]
    [SerializeField] float tiempoApertura = 0.9f;
    [SerializeField] float tiempoExtendida = 1.5f;
    [SerializeField] float tiempoCierre = 1.6f;
    [Tooltip("Cuánto se hunde la red mientras se cierra.")]
    [SerializeField] float hundimiento = 0.35f;
    [Tooltip("Segundos que tarda en cerrarse si cae abierta en tierra.")]
    [SerializeField] float tiempoAsentar = 0.5f;

    [Header("Regreso desde el agua")]
    [SerializeField] float velocidadRegreso = 1.5f;
    [SerializeField] float tiempoSubida = 1.8f;
    [SerializeField] float alturaArcoSubida = 0.8f;

    [Header("Peces")]
    [Tooltip("Prefab del pez físico que aparece en la mesa.")]
    [SerializeField] GameObject prefabPez;
    [SerializeField] int pecesMinimos = 1;
    [SerializeField] int pecesMaximos = 3;
    [Tooltip("Tope de peces que caben en la red si se lanza varias veces sin vaciarla.")]
    [SerializeField] int pecesTope = 6;
    [Tooltip("Tamaño de los peces que se ven dentro de la red (1 = tamaño real).")]
    [SerializeField] float escalaPezEnRed = 0.5f;

    [Header("Mesa")]
    [SerializeField] Collider mesa;
    [Tooltip("Tamaño de la red desplegada en la mesa (1 = 2,5 m de diámetro).")]
    [SerializeField] float escalaEnMesa = 0.42f;
    [SerializeField] float tiempoDespliegue = 0.6f;

    [Header("Eventos")]
    public UnityEvent alCaerAlAgua;
    public EventoPeces alAtraparPeces;
    public UnityEvent alQuedarListaParaRecoger;
    public EventoPeces alDesplegarEnMesa;

    [Header("Prueba en el editor")]
    [SerializeField] bool mostrarPanelPrueba = true;
    [Tooltip("Escribe en la consola cada cambio de estado de la red.")]
    [SerializeField] bool registrarEstados = false;
    [Tooltip("Lanzamiento simulado: velocidad hacia el agua (m/s).")]
    [SerializeField] float pruebaVelocidadHorizontal = 5f;
    [SerializeField] float pruebaVelocidadVertical = 3.5f;

    public Estado EstadoActual { get; private set; } = Estado.ListaParaRecoger;
    public int PecesAtrapados { get; private set; }

    // Transform original de la malla (el clip lo pisa al muestrear)
    Vector3 posLocalMalla;
    Quaternion rotLocalMalla;
    Vector3 escalaLocalMalla;

    // Límites de la red en espacio local de este objeto (con escala visual 1)
    Bounds limitesCerrada;
    Bounds limitesAbierta;
    float apertura;
    float escalaVisual = 1f;

    // Vuelo y movimientos
    Vector3 posicionBase;
    Vector3 velocidad;
    bool esLanzamiento;
    float tiempoEstado;
    Vector3 inicioMovimiento;
    Vector3 finMovimiento;
    float aperturaInicial;
    float escalaInicial;
    float alturaInicioCierre;

    readonly List<GameObject> pecesEnRed = new List<GameObject>();
    readonly List<GameObject> pecesEnMesa = new List<GameObject>();

    // Velocidad de la mano mientras sostiene la red
    readonly Queue<KeyValuePair<float, Vector3>> historialMano = new Queue<KeyValuePair<float, Vector3>>();
    const float VentanaVelocidad = 0.1f;

    // PlayableGraph para evaluar el clip en Android (Mecanim nativo compatible con Quest)
    PlayableGraph grafoAnimacion;
    AnimationClipPlayable clipPlayable;
    bool grafoInicializado;

    string ultimoEvento = "";

    void Awake()
    {
        var t = mallaRed.transform;
        posLocalMalla = t.localPosition;
        rotLocalMalla = t.localRotation;
        escalaLocalMalla = t.localScale;

        if (mallaRed != null)
            mallaRed.updateWhenOffscreen = true;

        InicializarGrafoAnimacion();

        limitesAbierta = MedirLimites(1f);
        limitesCerrada = MedirLimites(0f);
        AplicarApertura(0f);
    }

    void InicializarGrafoAnimacion()
    {
        if (animacionRed == null || mallaRed == null) return;

        // El PlayableGraph de animacion requiere un componente Animator
        var animator = mallaRed.GetComponent<Animator>();
        if (animator == null)
            animator = mallaRed.GetComponentInParent<Animator>();

        if (animator == null)
            animator = mallaRed.gameObject.AddComponent<Animator>();

        animator.applyRootMotion = false;

        // Grafo en modo manual: se evalua unicamente cuando AplicarApertura() lo solicita
        grafoAnimacion = PlayableGraph.Create("GrafoRedPesca");
        grafoAnimacion.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        var output = AnimationPlayableOutput.Create(grafoAnimacion, "SalidaRed", animator);
        clipPlayable = AnimationClipPlayable.Create(grafoAnimacion, animacionRed);
        clipPlayable.SetTime(0);
        clipPlayable.SetSpeed(0);

        output.SetSourcePlayable(clipPlayable);
        grafoAnimacion.Play();

        grafoInicializado = true;
    }

    void OnEnable()
    {
        if (grabbable != null) grabbable.WhenPointerEventRaised += AlEventoAgarre;
    }

    void OnDisable()
    {
        if (grabbable != null) grabbable.WhenPointerEventRaised -= AlEventoAgarre;
    }

    void OnDestroy()
    {
        if (grafoInicializado && grafoAnimacion.IsValid())
        {
            grafoAnimacion.Destroy();
        }
    }

    void Start()
    {
        if (puntoRecoger != null) ColocarBaseEn(puntoRecoger.position);
        CambiarEstado(Estado.ListaParaRecoger);
    }

    // =========================================================================
    // AGARRE (Meta Interaction SDK)
    // =========================================================================
    void AlEventoAgarre(PointerEvent evento)
    {
        if (evento.Type == PointerEventType.Select && (EstadoActual == Estado.ListaParaRecoger || EstadoActual == Estado.EnMesa))
        {
            historialMano.Clear();
            aperturaInicial = apertura;
            escalaInicial = escalaVisual;
            CambiarEstado(Estado.EnMano);
        }
        else if ((evento.Type == PointerEventType.Unselect || evento.Type == PointerEventType.Cancel) && EstadoActual == Estado.EnMano)
        {
            Vector3 v = VelocidadMano();
            Lanzar(v.magnitude >= velocidadMinima ? v * multiplicadorLanzamiento : v);
        }
    }

    Vector3 VelocidadMano()
    {
        if (historialMano.Count < 2) return Vector3.zero;
        var primero = historialMano.Peek();
        KeyValuePair<float, Vector3> ultimo = primero;
        foreach (var muestra in historialMano) ultimo = muestra;
        float dt = ultimo.Key - primero.Key;
        return dt > 0f ? (ultimo.Value - primero.Value) / dt : Vector3.zero;
    }

    // =========================================================================
    // API PÚBLICA
    // =========================================================================
    /// <summary>Suelta la red con esa velocidad. Si es rápida se abre en el aire; si no, solo cae.</summary>
    public void Lanzar(Vector3 velocidadInicial)
    {
        AplicarApertura(apertura, 1f);
        posicionBase = PuntoBase();
        velocidad = velocidadInicial;
        esLanzamiento = velocidadInicial.magnitude >= velocidadMinima;
        CambiarEstado(Estado.Volando);
    }

    public void LanzarPrueba()
    {
        Reiniciar();
        Lanzar(VelocidadPrueba());
    }

    /// <summary>Prueba: llena la red y la suelta sobre la mesa.</summary>
    public void SoltarEnMesaPrueba()
    {
        if (mesa == null) return;
        Reiniciar();
        AgregarPeces(Random.Range(pecesMinimos, pecesMaximos + 1));
        ColocarBaseEn(mesa.bounds.center + Vector3.up * 2f);
        Lanzar(Vector3.zero);
    }

    public void Reiniciar()
    {
        foreach (var pez in pecesEnMesa) if (pez != null) Destroy(pez);
        pecesEnMesa.Clear();
        VaciarRed();
        AplicarApertura(0f, 1f);
        transform.rotation = RotacionDerecha();
        if (puntoRecoger != null) ColocarBaseEn(puntoRecoger.position);
        CambiarEstado(Estado.ListaParaRecoger);
    }

    /// <summary>Cambia cuántos peces atrapa la red por lanzamiento (lo usa MejoraRed).</summary>
    public void ConfigurarCaptura(int minimo, int maximo, int tope)
    {
        pecesMinimos = Mathf.Max(0, minimo);
        pecesMaximos = Mathf.Max(pecesMinimos, maximo);
        pecesTope = Mathf.Max(pecesMaximos, tope);
    }

    // =========================================================================
    // BUCLE
    // =========================================================================
    void Update()
    {
        LeerTeclasPrueba();

        float dt = Time.deltaTime;
        tiempoEstado += dt;

        // Fuera de la mano la red siempre vuelve a quedar derecha (solo conserva el giro horizontal)
        if (EstadoActual != Estado.EnMano && EstadoActual != Estado.ListaParaRecoger && EstadoActual != Estado.EnMesa)
        {
            Vector3 baseAntes = PuntoBase();
            transform.rotation = Quaternion.Slerp(transform.rotation, RotacionDerecha(), 1f - Mathf.Exp(-10f * dt));
            ColocarBaseEn(baseAntes);
        }

        switch (EstadoActual)
        {
            case Estado.EnMano: ActualizarEnMano(); break;
            case Estado.Volando: ActualizarVuelo(dt); break;
            case Estado.Asentando: ActualizarAsentando(); break;
            case Estado.Extendida: ActualizarExtendida(); break;
            case Estado.Cerrandose: ActualizarCierre(); break;
            case Estado.Regresando: ActualizarRegreso(dt); break;
            case Estado.Subiendo: ActualizarSubida(); break;
            case Estado.Desplegando: ActualizarDespliegue(); break;
        }
    }

    void LateUpdate()
    {
        if (EstadoActual != Estado.EnMano) return;

        historialMano.Enqueue(new KeyValuePair<float, Vector3>(Time.time, transform.position));
        while (historialMano.Count > 2 && Time.time - historialMano.Peek().Key > VentanaVelocidad)
            historialMano.Dequeue();
    }

    void ActualizarEnMano()
    {
        // Si se agarró desplegada en la mesa, se cierra en la mano
        float t = Mathf.Clamp01(tiempoEstado / 0.4f);
        if (apertura > 0f || escalaVisual != 1f)
            AplicarApertura(Mathf.Lerp(aperturaInicial, 0f, t), Mathf.Lerp(escalaInicial, 1f, t));
    }

    void ActualizarVuelo(float dt)
    {
        if (esLanzamiento) AplicarApertura(Mathf.Max(apertura, tiempoEstado / tiempoApertura));

        velocidad.y -= gravedad * dt;
        Vector3 siguiente = posicionBase + velocidad * dt;

        // Toca algo sólido antes de llegar al agua: se queda ahí (o se despliega si es la mesa)
        RaycastHit golpe;
        if (Physics.Linecast(posicionBase, siguiente, out golpe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && golpe.point.y > alturaAgua + 0.05f)
        {
            if (EsMesa(golpe.collider) && PecesAtrapados > 0)
            {
                EmpezarDespliegue(golpe.point);
            }
            else
            {
                ultimoEvento = "Cayó sobre " + golpe.collider.name;
                posicionBase = golpe.point;
                ColocarBaseEn(posicionBase);
                aperturaInicial = apertura;
                CambiarEstado(Estado.Asentando);
            }
            return;
        }

        if (siguiente.y <= alturaAgua)
        {
            siguiente.y = alturaAgua;
            posicionBase = siguiente;
            AplicarApertura(1f);
            ColocarBaseEn(posicionBase);
            ultimoEvento = "Cayó al agua";
            CambiarEstado(Estado.Extendida);
            alCaerAlAgua.Invoke();
            return;
        }

        posicionBase = siguiente;
        ColocarBaseEn(posicionBase);

        if (tiempoEstado > tiempoMaximoVuelo) EmpezarSubida();
    }

    void ActualizarAsentando()
    {
        float t = tiempoAsentar > 0f ? Mathf.Clamp01(tiempoEstado / tiempoAsentar) : 1f;
        AplicarApertura(Mathf.Lerp(aperturaInicial, 0f, Mathf.SmoothStep(0f, 1f, t)));
        ColocarBaseEn(posicionBase);
        if (t >= 1f) CambiarEstado(Estado.ListaParaRecoger);
    }

    void ActualizarExtendida()
    {
        posicionBase.y = alturaAgua + Mathf.Sin(tiempoEstado * 2.5f) * 0.02f;
        ColocarBaseEn(posicionBase);

        if (tiempoEstado >= tiempoExtendida)
        {
            alturaInicioCierre = posicionBase.y;
            CambiarEstado(Estado.Cerrandose);
        }
    }

    void ActualizarCierre()
    {
        float t = Mathf.Clamp01(tiempoEstado / tiempoCierre);
        AplicarApertura(1f - Mathf.SmoothStep(0f, 1f, t));
        posicionBase.y = Mathf.Lerp(alturaInicioCierre, alturaAgua - hundimiento, t);
        ColocarBaseEn(posicionBase);

        if (t >= 1f)
        {
            int nuevos = AgregarPeces(Random.Range(pecesMinimos, pecesMaximos + 1));
            ultimoEvento = "Atrapó " + nuevos + " pez(es)";
            alAtraparPeces.Invoke(nuevos);

            if (puntoOrillaAgua != null) CambiarEstado(Estado.Regresando);
            else EmpezarSubida();
        }
    }

    void ActualizarRegreso(float dt)
    {
        Vector3 destino = puntoOrillaAgua.position;
        Vector3 horizontal = new Vector3(destino.x - posicionBase.x, 0f, destino.z - posicionBase.z);
        float paso = velocidadRegreso * dt;

        if (horizontal.magnitude <= paso)
        {
            posicionBase.x = destino.x;
            posicionBase.z = destino.z;
            ColocarBaseEn(posicionBase);
            EmpezarSubida();
            return;
        }

        posicionBase += horizontal.normalized * paso;
        posicionBase.y = alturaAgua - hundimiento + Mathf.Sin(tiempoEstado * 3f) * 0.03f;
        ColocarBaseEn(posicionBase);
    }

    void EmpezarSubida()
    {
        inicioMovimiento = PuntoBase();
        aperturaInicial = apertura;
        CambiarEstado(Estado.Subiendo);
    }

    void ActualizarSubida()
    {
        if (puntoRecoger == null) { CambiarEstado(Estado.ListaParaRecoger); return; }

        float t = Mathf.Clamp01(tiempoEstado / tiempoSubida);
        float suave = Mathf.SmoothStep(0f, 1f, t);
        AplicarApertura(Mathf.Lerp(aperturaInicial, 0f, suave));

        Vector3 p = Vector3.Lerp(inicioMovimiento, puntoRecoger.position, suave);
        p.y += Mathf.Sin(t * Mathf.PI) * alturaArcoSubida;
        ColocarBaseEn(p);

        if (t >= 1f)
        {
            AplicarApertura(0f);
            ColocarBaseEn(puntoRecoger.position);
            CambiarEstado(Estado.ListaParaRecoger);
            alQuedarListaParaRecoger.Invoke();
        }
    }

    // =========================================================================
    // MESA
    // =========================================================================
    bool EsMesa(Collider c)
    {
        return mesa != null && c != null && (c == mesa || c.transform.IsChildOf(mesa.transform));
    }

    void EmpezarDespliegue(Vector3 puntoGolpe)
    {
        // Centro de la red sobre la mesa, sin que se salga por los lados
        Bounds b = mesa.bounds;
        float radio = limitesAbierta.extents.x * escalaEnMesa;
        Vector3 centro = puntoGolpe;
        centro.x = b.extents.x > radio ? Mathf.Clamp(centro.x, b.min.x + radio, b.max.x - radio) : b.center.x;
        centro.z = b.extents.z > radio ? Mathf.Clamp(centro.z, b.min.z + radio, b.max.z - radio) : b.center.z;
        centro.y = b.max.y + 0.005f;

        inicioMovimiento = PuntoBase();
        finMovimiento = centro;
        aperturaInicial = apertura;
        escalaInicial = escalaVisual;
        ultimoEvento = "Desplegando en la mesa";
        CambiarEstado(Estado.Desplegando);
    }

    void ActualizarDespliegue()
    {
        float t = Mathf.Clamp01(tiempoEstado / tiempoDespliegue);
        float suave = Mathf.SmoothStep(0f, 1f, t);
        AplicarApertura(Mathf.Lerp(aperturaInicial, 1f, suave), Mathf.Lerp(escalaInicial, escalaEnMesa, suave));
        ColocarBaseEn(Vector3.Lerp(inicioMovimiento, finMovimiento, suave));

        if (t < 1f) return;

        transform.rotation = RotacionDerecha();
        ColocarBaseEn(finMovimiento);
        int cantidad = PecesAtrapados;
        SoltarPecesEnMesa(finMovimiento);
        CambiarEstado(Estado.EnMesa);
        alDesplegarEnMesa.Invoke(cantidad);
    }

    void SoltarPecesEnMesa(Vector3 centro)
    {
        int cantidad = PecesAtrapados;
        VaciarRed();
        if (prefabPez == null) return;

        float radio = limitesAbierta.extents.x * escalaEnMesa * 0.4f;
        for (int i = 0; i < cantidad; i++)
        {
            Vector3 p = centro + new Vector3(Random.Range(-radio, radio) * 0.5f, 0.12f + i * 0.08f, Random.Range(-radio, radio));
            Quaternion r = Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f) * prefabPez.transform.rotation;
            pecesEnMesa.Add(Instantiate(prefabPez, p, r));
        }
    }

    // =========================================================================
    // PECES DENTRO DE LA RED (solo visuales)
    // =========================================================================
    int AgregarPeces(int cantidad)
    {
        int antes = PecesAtrapados;
        PecesAtrapados = Mathf.Min(PecesAtrapados + cantidad, pecesTope);

        var sprite = prefabPez != null ? prefabPez.GetComponentInChildren<SpriteRenderer>() : null;
        if (sprite != null)
        {
            Vector3 escalaMundo = Vector3.Scale(prefabPez.transform.localScale, sprite.transform.localScale) * escalaPezEnRed;
            for (int i = antes; i < PecesAtrapados; i++)
            {
                var visual = new GameObject("Pez en red");
                visual.transform.SetParent(transform, false);
                var sr = visual.AddComponent<SpriteRenderer>();
                sr.sprite = sprite.sprite;
                sr.sharedMaterial = sprite.sharedMaterial;
                sr.color = sprite.color;

                // Colgando dentro de la red, cabeza abajo
                Vector3 c = limitesCerrada.center;
                visual.transform.localPosition = new Vector3(c.x + Random.Range(-0.08f, 0.08f),
                    limitesCerrada.min.y + 0.35f + Random.Range(0f, 0.25f), c.z + Random.Range(-0.08f, 0.08f));
                visual.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(270f, 0f, 0f);
                visual.transform.localScale = escalaMundo;
                pecesEnRed.Add(visual);
            }
        }
        ActualizarPecesVisibles();
        return PecesAtrapados - antes;
    }

    void VaciarRed()
    {
        foreach (var v in pecesEnRed) if (v != null) Destroy(v);
        pecesEnRed.Clear();
        PecesAtrapados = 0;
    }

    void ActualizarPecesVisibles()
    {
        // Con la red abierta los peces quedarían flotando fuera de ella
        bool visibles = apertura < 0.35f;
        foreach (var v in pecesEnRed) if (v != null) v.SetActive(visibles);
    }

    void CambiarEstado(Estado nuevo)
    {
        if (registrarEstados) Debug.Log("[RedPesca] " + EstadoActual + " -> " + nuevo + " (t=" + Time.time.ToString("F2") + ", peces=" + PecesAtrapados + ")", this);
        EstadoActual = nuevo;
        tiempoEstado = 0f;

        bool agarrable = nuevo == Estado.ListaParaRecoger || nuevo == Estado.EnMano || nuevo == Estado.EnMesa;
        if (interactables != null) interactables.SetActive(agarrable);
        if (colliderAgarre != null)
        {
            colliderAgarre.enabled = agarrable;
            AjustarCollider(nuevo == Estado.EnMesa ? limitesAbierta : limitesCerrada, nuevo == Estado.EnMesa ? escalaEnMesa : 1f);
        }
    }

    void AjustarCollider(Bounds limites, float escala)
    {
        Vector3 centro = posLocalMalla + (limites.center - posLocalMalla) * escala;
        Vector3 tam = limites.size * escala;
        tam.y = Mathf.Max(tam.y, 0.04f);
        colliderAgarre.center = centro;
        colliderAgarre.size = tam;
    }

    // =========================================================================
    // FORMA DE LA RED
    // =========================================================================
    void AplicarApertura(float valor)
    {
        AplicarApertura(valor, escalaVisual);
    }

    void AplicarApertura(float valor, float escala)
    {
        apertura = Mathf.Clamp01(valor);
        escalaVisual = escala;

        float tiempoMuestreo = apertura * tiempoClipAbierta;

        if (grafoInicializado && grafoAnimacion.IsValid())
        {
            clipPlayable.SetTime(tiempoMuestreo);
            grafoAnimacion.Evaluate();
        }
        else if (animacionRed != null && mallaRed != null)
        {
            // Fallback en caso de que el grafo no este disponible
            animacionRed.SampleAnimation(mallaRed.gameObject, tiempoMuestreo);
        }

        // El clip mueve y gira la malla (movimiento de raíz de Blender): se descarta
        var t = mallaRed.transform;
        t.localPosition = posLocalMalla;
        t.localRotation = rotLocalMalla;
        t.localScale = escalaLocalMalla * escalaVisual;

        ActualizarPecesVisibles();
    }

    /// <summary>Punto inferior-centro de la red, en espacio local, con la forma y escala actuales.</summary>
    Vector3 BaseActual()
    {
        Bounds a = limitesCerrada, b = limitesAbierta;
        Vector3 baseCerrada = new Vector3(a.center.x, a.min.y, a.center.z);
        Vector3 baseAbierta = new Vector3(b.center.x, b.min.y, b.center.z);
        Vector3 base1 = Vector3.Lerp(baseCerrada, baseAbierta, apertura);
        return posLocalMalla + (base1 - posLocalMalla) * escalaVisual;
    }

    Vector3 PuntoBase()
    {
        return transform.TransformPoint(BaseActual());
    }

    void ColocarBaseEn(Vector3 puntoMundo)
    {
        transform.position = puntoMundo - transform.TransformVector(BaseActual());
    }

    Quaternion RotacionDerecha()
    {
        Vector3 adelante = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (adelante.sqrMagnitude < 0.0001f) adelante = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (adelante.sqrMagnitude < 0.0001f) adelante = Vector3.forward;
        return Quaternion.LookRotation(adelante.normalized, Vector3.up);
    }

    Bounds MedirLimites(float valorApertura)
    {
        AplicarApertura(valorApertura, 1f);
        var mesh = new Mesh();
        mallaRed.BakeMesh(mesh, true);

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;
        foreach (var v in mesh.vertices)
        {
            Vector3 local = transform.InverseTransformPoint(mallaRed.transform.TransformPoint(v));
            min = Vector3.Min(min, local);
            max = Vector3.Max(max, local);
        }
        Destroy(mesh);
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    // =========================================================================
    // PRUEBA EN EL EDITOR
    // =========================================================================
    Vector3 VelocidadPrueba()
    {
        Vector3 direccion = Vector3.back;
        if (puntoRecoger != null && puntoOrillaAgua != null)
        {
            direccion = puntoOrillaAgua.position - puntoRecoger.position;
            direccion.y = 0f;
            direccion = direccion.sqrMagnitude > 0.0001f ? direccion.normalized : Vector3.back;
        }
        return direccion * pruebaVelocidadHorizontal + Vector3.up * pruebaVelocidadVertical;
    }

    void LeerTeclasPrueba()
    {
        if (!mostrarPanelPrueba || !Application.isEditor) return;
#if ENABLE_INPUT_SYSTEM
        var teclado = UnityEngine.InputSystem.Keyboard.current;
        if (teclado == null) return;
        if (teclado.spaceKey.wasPressedThisFrame) LanzarPrueba();
        if (teclado.mKey.wasPressedThisFrame) SoltarEnMesaPrueba();
        if (teclado.rKey.wasPressedThisFrame) Reiniciar();
#endif
    }

    void OnGUI()
    {
        if (!mostrarPanelPrueba || !Application.isEditor) return;

        GUILayout.BeginArea(new Rect(20, 20, 380, 160), GUI.skin.box);
        GUILayout.Label("<b>RED DE PESCA - prueba</b>");
        GUILayout.Label("Estado: <b>" + EstadoActual + "</b>   peces: " + PecesAtrapados + "   apertura: " + apertura.ToString("F2"));
        if (!string.IsNullOrEmpty(ultimoEvento)) GUILayout.Label("<i>" + ultimoEvento + "</i>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Lanzar [Espacio]", GUILayout.Height(28))) LanzarPrueba();
        if (GUILayout.Button("Llena a la mesa [M]", GUILayout.Height(28))) SoltarEnMesaPrueba();
        if (GUILayout.Button("Reiniciar [R]", GUILayout.Height(28))) Reiniciar();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void OnDrawGizmosSelected()
    {
        if (puntoRecoger != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(puntoRecoger.position, 0.2f);
        }
        if (puntoOrillaAgua != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(puntoOrillaAgua.position, 0.3f);
        }

        if (puntoRecoger == null) return;

        // Trayectoria del lanzamiento de prueba
        Gizmos.color = Color.yellow;
        Vector3 p = puntoRecoger.position;
        Vector3 v = VelocidadPrueba();
        for (int i = 0; i < 120 && p.y > alturaAgua; i++)
        {
            Vector3 siguiente = p + v * 0.03f;
            v.y -= gravedad * 0.03f;
            Gizmos.DrawLine(p, siguiente);
            p = siguiente;
        }
    }
}
