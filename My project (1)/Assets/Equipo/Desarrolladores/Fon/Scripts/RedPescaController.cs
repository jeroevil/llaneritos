using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Oculus.Interaction;

/// <summary>
/// Red de pesca (atarraya).
/// - Si la sueltas dentro de la zona de lanzamiento, se lanza sola siempre por el mismo recorrido.
/// - Si la sueltas llena cerca de la mesa (zona de la mesa), se despliega sobre ella con los peces.
/// - Fuera de las zonas: si la sueltas se cae donde esté; si la lanzas con fuerza, se abre en el aire.
/// - Si cae al agua: queda extendida, se cierra atrapando peces y la jalan a tirones hasta la orilla.
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

    [Header("Lanzamiento guiado")]
    [Tooltip("Trigger (Box/Sphere/Capsule) de la zona de lanzamiento. Si sueltas la red dentro, se lanza sola por el recorrido fijo, sin importar el gesto.")]
    [SerializeField] Collider zonaLanzamiento;
    [Tooltip("Donde cae la red en el agua con el lanzamiento guiado (solo se usa su X/Z: la altura es la del agua).")]
    [SerializeField] Transform destinoLanzamiento;
    [Tooltip("Opcional: punto fijo desde donde sale el recorrido. Vacío = sale de donde se soltó la red.")]
    [SerializeField] Transform salidaLanzamiento;
    [Tooltip("Segundos que tarda la red en llegar al agua.")]
    [SerializeField] float duracionLanzamiento = 1.3f;
    [Tooltip("Altura del arco por encima de la línea recta entre la salida y el destino.")]
    [SerializeField] float alturaArcoLanzamiento = 1.5f;
    [Tooltip("Segundos en que la red pasa de la mano al recorrido fijo.")]
    [SerializeField] float tiempoAcomodo = 0.3f;

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

    [Header("Regreso desde el agua (a tirones)")]
    [Tooltip("Metros que avanza la red en cada jalón de la cuerda.")]
    [SerializeField] float distanciaJalon = 0.9f;
    [Tooltip("Segundos de cada jalón: arranca fuerte y frena.")]
    [SerializeField] float duracionJalon = 0.55f;
    [Tooltip("Pausa entre jalones, como cuando se recoge la cuerda mano a mano.")]
    [SerializeField] float pausaEntreJalones = 0.2f;
    [Tooltip("Cuánto se levanta la red del agua en cada jalón.")]
    [SerializeField] float elevacionJalon = 0.12f;
    [Tooltip("Segundos del tirón final que la saca del agua hasta la orilla.")]
    [SerializeField] float tiempoSubida = 1.4f;
    [SerializeField] float alturaArcoSubida = 0.8f;
    [Tooltip("Cuánto se hunde la red justo antes del tirón final (toma impulso).")]
    [SerializeField] float anticipacionSubida = 0.15f;
    [Tooltip("Cuánto se pasa de la orilla y regresa al llegar (0 = sin rebote).")]
    [SerializeField] float reboteLlegada = 1.3f;
    [Tooltip("Grados máximos que se inclina la red hacia donde la jalan.")]
    [SerializeField] float inclinacionMaxima = 25f;

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
    [Tooltip("Trigger (Box/Sphere/Capsule) alrededor de la mesa. Si sueltas la red llena dentro, se despliega sola sobre la mesa.")]
    [SerializeField] Collider zonaMesa;
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

    // Lanzamiento guiado
    bool vueloGuiado;
    Vector3 salidaGuiada;
    Vector3 desfaseGuiado;

    // Inclinación de la red mientras la jalan
    Vector3 ultimaBase;
    Vector3 velocidadSuave;

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
        if (evento.Type == PointerEventType.Select) Agarrar();
        else if (evento.Type == PointerEventType.Unselect || evento.Type == PointerEventType.Cancel) Soltar();
    }

    /// <summary>La mano agarra la red (lo llama el Grabbable; también sirve para pruebas).</summary>
    public void Agarrar()
    {
        if (EstadoActual != Estado.ListaParaRecoger && EstadoActual != Estado.EnMesa) return;
        historialMano.Clear();
        aperturaInicial = apertura;
        escalaInicial = escalaVisual;
        CambiarEstado(Estado.EnMano);
    }

    /// <summary>La mano suelta la red: decide entre mesa, lanzamiento guiado o lanzamiento libre.</summary>
    public void Soltar()
    {
        if (EstadoActual != Estado.EnMano) return;

        // Llena y cerca de la mesa: se pone sola sobre la mesa
        if (mesa != null && PecesAtrapados > 0 && DentroDeZona(zonaMesa))
        {
            EmpezarDespliegue(CentroRed());
            return;
        }

        // En la zona de lanzamiento: se lanza sola por el recorrido fijo
        if (destinoLanzamiento != null && DentroDeZona(zonaLanzamiento))
        {
            LanzarGuiado();
            return;
        }

        Vector3 v = VelocidadMano();
        Lanzar(v.magnitude >= velocidadMinima ? v * multiplicadorLanzamiento : v);
    }

    /// <summary>Centro de la red en el mundo (lo que se compara con las zonas).</summary>
    Vector3 CentroRed()
    {
        return colliderAgarre != null ? colliderAgarre.bounds.center : transform.position;
    }

    bool DentroDeZona(Collider zona)
    {
        if (zona == null || !zona.enabled || !zona.gameObject.activeInHierarchy) return false;
        Vector3 p = CentroRed();

        // ClosestPoint no sirve con MeshCollider cóncavo: ahí basta la caja que lo envuelve
        var malla = zona as MeshCollider;
        if (malla != null && !malla.convex) return zona.bounds.Contains(p);
        return (zona.ClosestPoint(p) - p).sqrMagnitude < 0.0001f;
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
        vueloGuiado = false;
        CambiarEstado(Estado.Volando);
    }

    /// <summary>Lanza la red por el recorrido fijo hasta destinoLanzamiento, sin importar cómo se soltó.</summary>
    public void LanzarGuiado()
    {
        if (destinoLanzamiento == null) { Lanzar(VelocidadPrueba()); return; }

        AplicarApertura(apertura, 1f);
        posicionBase = PuntoBase();
        salidaGuiada = salidaLanzamiento != null ? salidaLanzamiento.position : posicionBase;
        // La red arranca donde se soltó y en tiempoAcomodo queda encima del recorrido fijo
        desfaseGuiado = posicionBase - salidaGuiada;
        esLanzamiento = true;
        vueloGuiado = true;
        ultimoEvento = "Lanzamiento guiado";
        CambiarEstado(Estado.Volando);
    }

    public void LanzarPrueba()
    {
        Reiniciar();
        if (destinoLanzamiento != null) LanzarGuiado();
        else Lanzar(VelocidadPrueba());
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
            case Estado.Subiendo: ActualizarSubida(dt); break;
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

        if (vueloGuiado) { ActualizarVueloGuiado(); return; }

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
            CaerAlAgua(siguiente);
            return;
        }

        posicionBase = siguiente;
        ColocarBaseEn(posicionBase);

        if (tiempoEstado > tiempoMaximoVuelo) EmpezarSubida();
    }

    /// <summary>Recorrido fijo: no choca con nada, siempre termina en el mismo punto del agua.</summary>
    void ActualizarVueloGuiado()
    {
        float t = duracionLanzamiento > 0f ? Mathf.Clamp01(tiempoEstado / duracionLanzamiento) : 1f;
        float acomodo = tiempoAcomodo > 0f ? Mathf.Clamp01(tiempoEstado / tiempoAcomodo) : 1f;

        Vector3 p = PuntoRecorridoGuiado(salidaGuiada, t) + desfaseGuiado * (1f - Mathf.SmoothStep(0f, 1f, acomodo));

        if (t >= 1f)
        {
            CaerAlAgua(p);
            return;
        }

        posicionBase = p;
        ColocarBaseEn(posicionBase);
    }

    /// <summary>Parábola entre la salida y el destino (avance horizontal constante, como con gravedad).</summary>
    Vector3 PuntoRecorridoGuiado(Vector3 salida, float t)
    {
        Vector3 destino = destinoLanzamiento.position;
        destino.y = alturaAgua;
        return Vector3.Lerp(salida, destino, t) + Vector3.up * (4f * alturaArcoLanzamiento * t * (1f - t));
    }

    void CaerAlAgua(Vector3 punto)
    {
        punto.y = alturaAgua;
        posicionBase = punto;
        vueloGuiado = false;
        AplicarApertura(1f);
        ColocarBaseEn(posicionBase);
        ultimoEvento = "Cayó al agua";
        CambiarEstado(Estado.Extendida);
        alCaerAlAgua.Invoke();
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

            if (puntoOrillaAgua != null) EmpezarRegreso();
            else EmpezarSubida();
        }
    }

    void EmpezarRegreso()
    {
        inicioMovimiento = posicionBase;
        ultimaBase = posicionBase;
        velocidadSuave = Vector3.zero;
        CambiarEstado(Estado.Regresando);
    }

    /// <summary>La jalan hacia la orilla a tirones: arranque fuerte, frenado y una pausa entre jalones.</summary>
    void ActualizarRegreso(float dt)
    {
        Vector3 destino = puntoOrillaAgua.position;
        Vector3 total = new Vector3(destino.x - inicioMovimiento.x, 0f, destino.z - inicioMovimiento.z);
        float distancia = total.magnitude;

        float ciclo = Mathf.Max(0.01f, duracionJalon + pausaEntreJalones);
        int jalon = Mathf.FloorToInt(tiempoEstado / ciclo);
        float enCiclo = tiempoEstado - jalon * ciclo;
        float u = duracionJalon > 0f ? Mathf.Clamp01(enCiclo / duracionJalon) : 1f;
        // Acelera rápido (pico al 25 % del jalón) y frena largo; sin saltos de velocidad entre cuadros
        float s = u * u * (3f - 2f * u);
        float avanceJalon = 1f - (1f - s) * (1f - s) * (1f - s);
        float recorrido = Mathf.Min((jalon + avanceJalon) * distanciaJalon, distancia);

        Vector3 p = inicioMovimiento + (distancia > 0.0001f ? total / distancia * recorrido : Vector3.zero);
        // Cada jalón la saca un poco del agua (sube rápido al principio) y en la pausa se vuelve a hundir
        float fase = enCiclo / ciclo;
        p.y = alturaAgua - hundimiento + elevacionJalon * Mathf.Sin(Mathf.PI * Mathf.Pow(fase, 0.6f));

        Inclinar(p, dt);
        posicionBase = p;
        ColocarBaseEn(posicionBase);

        // Llega a la orilla en pleno jalón: el tirón final sigue con ese impulso
        if (recorrido >= distancia) EmpezarSubida();
    }

    void EmpezarSubida()
    {
        inicioMovimiento = PuntoBase();
        ultimaBase = inicioMovimiento;
        if (EstadoActual != Estado.Regresando) velocidadSuave = Vector3.zero;
        aperturaInicial = apertura;
        CambiarEstado(Estado.Subiendo);
    }

    /// <summary>Toma impulso hundiéndose, un tirón la saca del agua y llega a la orilla con un pequeño rebote.</summary>
    void ActualizarSubida(float dt)
    {
        if (puntoRecoger == null) { CambiarEstado(Estado.ListaParaRecoger); return; }

        const float finAnticipacion = 0.18f;
        float t = tiempoSubida > 0f ? Mathf.Clamp01(tiempoEstado / tiempoSubida) : 1f;
        Vector3 desde = inicioMovimiento + Vector3.down * anticipacionSubida;
        Vector3 p;

        if (t < finAnticipacion)
        {
            // La cuerda se tensa: la red se hunde un poco y empieza a cerrarse
            float a = t / finAnticipacion;
            p = Vector3.Lerp(inicioMovimiento, desde, Mathf.Sin(a * Mathf.PI * 0.5f));
            AplicarApertura(Mathf.Lerp(aperturaInicial, aperturaInicial * 0.5f, a));
        }
        else
        {
            float u = (t - finAnticipacion) / (1f - finAnticipacion);
            // u² hace que el tirón arranque desde quieta (sin salto) y acelere fuerte enseguida
            p = Vector3.LerpUnclamped(desde, puntoRecoger.position, EaseOutBack(u * u, reboteLlegada));
            p.y += 4f * alturaArcoSubida * u * (1f - u);
            AplicarApertura(Mathf.Lerp(aperturaInicial * 0.5f, 0f, Mathf.Clamp01(u * 3f)));
        }

        Inclinar(p, dt);
        ColocarBaseEn(p);

        if (t >= 1f)
        {
            AplicarApertura(0f);
            transform.rotation = RotacionDerecha();
            ColocarBaseEn(puntoRecoger.position);
            CambiarEstado(Estado.ListaParaRecoger);
            alQuedarListaParaRecoger.Invoke();
        }
    }

    static float EaseOutBack(float x, float rebote)
    {
        float c3 = rebote + 1f;
        float m = x - 1f;
        return 1f + c3 * m * m * m + rebote * m * m;
    }

    /// <summary>Inclina la red hacia donde la jalan, con algo de retraso, como un bulto colgando de la cuerda.</summary>
    void Inclinar(Vector3 nuevaBase, float dt)
    {
        Vector3 v = dt > 0f ? (nuevaBase - ultimaBase) / dt : Vector3.zero;
        ultimaBase = nuevaBase;
        v.y = 0f;
        velocidadSuave = Vector3.Lerp(velocidadSuave, v, 1f - Mathf.Exp(-8f * dt));

        float angulo = Mathf.Min(velocidadSuave.magnitude * 12f, inclinacionMaxima);
        Vector3 eje = Vector3.Cross(Vector3.up, velocidadSuave);
        Quaternion derecha = RotacionDerecha();
        transform.rotation = eje.sqrMagnitude > 0.000001f ? Quaternion.AngleAxis(angulo, eje.normalized) * derecha : derecha;
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

        DibujarZona(zonaLanzamiento, new Color(1f, 0.6f, 0f));
        DibujarZona(zonaMesa, new Color(0.6f, 0.3f, 1f));

        // Recorrido fijo del lanzamiento guiado
        if (destinoLanzamiento != null)
        {
            Vector3 salida = salidaLanzamiento != null ? salidaLanzamiento.position
                : zonaLanzamiento != null ? zonaLanzamiento.bounds.center
                : puntoRecoger != null ? puntoRecoger.position : transform.position;
            Gizmos.color = Color.yellow;
            Vector3 anterior = salida;
            for (int i = 1; i <= 40; i++)
            {
                Vector3 siguiente = PuntoRecorridoGuiado(salida, i / 40f);
                Gizmos.DrawLine(anterior, siguiente);
                anterior = siguiente;
            }
            Gizmos.DrawWireSphere(anterior, 0.4f);
            return;
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

    static void DibujarZona(Collider zona, Color color)
    {
        if (zona == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireCube(zona.bounds.center, zona.bounds.size);
    }
}
