using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Ending: cuando el Silbon atrapa al jugador, fundido a negro, el jugador queda en un punto de vista
/// y ve al Silbon con el saco caminar en linea recta hacia la oscuridad. Al terminar, se cierra el juego
/// (en el editor se detiene el Play).
///
/// Se engancha a OnJugadorAtrapado de FishingSilbonDirector sin modificarlo.
/// No busca nada por su cuenta: todas las referencias se asignan en el Inspector.
/// El jugador se mueve solo en XZ y gira solo en Y, siempre a oscuras; la altura la sigue dando el tracking.
/// </summary>
public class FinalSaco : MonoBehaviour
{
    [Header("Disparador")]
    [Tooltip("Director de pesca y Silbon: el ending empieza con su evento OnJugadorAtrapado.")]
    [SerializeField] FishingSilbonDirector director;

    [Tooltip("Segundos tras la captura antes del fundido (deja ver la animacion de atrapar).")]
    [SerializeField, Min(0f)] float esperaTrasAtrapar = 1.5f;

    [Header("Jugador")]
    [Tooltip("Raiz del jugador (OVRCameraRig / objeto con PinchToWalk).")]
    [SerializeField] Transform rigJugador;
    [Tooltip("Camara de los ojos (CenterEyeAnchor).")]
    [SerializeField] Transform cabeza;
    [Tooltip("Donde queda el jugador para ver el final. Su flecha azul (forward) indica hacia donde mira.")]
    [SerializeField] Transform puntoVista;
    [Tooltip("Componentes que se apagan durante el final (PinchToWalk, botones de teletransporte, etc.).")]
    [SerializeField] List<Behaviour> desactivarDuranteFinal = new List<Behaviour>();
    [Tooltip("Objetos que se ocultan durante el final (el Silbon que persigue, la barra, etc.).")]
    [SerializeField] List<GameObject> ocultarDuranteFinal = new List<GameObject>();

    [Header("Silbon con el Saco")]
    [Tooltip("Objeto 'saco silbon'. Se oculta al iniciar la escena.")]
    [SerializeField] GameObject saco;
    [Tooltip("Animator del saco silbon.")]
    [SerializeField] Animator animatorSaco;
    [Tooltip("Estado del Animator que se reproduce mientras camina.")]
    [SerializeField] string estadoCaminar = "Walk";
    [Tooltip("Parametro bool que se pone en true para que el controller no saque al saco de Walk. " +
             "En 'Nuevo control silbon' la transicion Walk -> Idle salta cuando 'Acechando' es false. Vacio = no se toca ningun parametro.")]
    [SerializeField] string parametroMantenerCaminar = "Acechando";
    [SerializeField] Transform inicioRecorrido;
    [SerializeField] Transform finRecorrido;
    [Tooltip("Segundos que tarda en ir del inicio al fin.")]
    [SerializeField, Min(0.1f)] float duracionRecorrido = 5f;

    [Header("Niebla que consume al Silbon")]
    [Tooltip("Mientras el Silbon se aleja, la niebla del mundo se cierra hasta tragarselo por completo.")]
    [SerializeField] bool nieblaConsume = true;
    [Tooltip("Cuanta niebla hay sobre el Silbon al terminar el recorrido (1 = ya no se ve).")]
    [SerializeField, Range(0.5f, 1f)] float nieblaFinal = 1f;
    [Tooltip("Forma de la curva: >1 = al principio se ve casi limpio y se traga al final; 1 = progresivo.")]
    [SerializeField, Range(0.5f, 3f)] float curvaNiebla = 1.4f;
    [Tooltip("Metros a los que empieza la niebla cuando ya es total (cerca de la camara).")]
    [SerializeField, Min(0f)] float nieblaInicioCerrada = 3f;

    [Header("Fundidos")]
    // Usa el parpadeo del Silbon (FundidoParpadeo): no hay que asignar nada.
    [SerializeField, Min(0f)] float duracionFundidoEntrada = 0.6f;
    [SerializeField, Min(0f)] float tiempoEnNegro = 0.5f;
    [SerializeField, Min(0f)] float duracionFundidoApertura = 0.8f;
    [SerializeField, Min(0f)] float duracionFundidoFinal = 1.2f;

    [Header("Texto y audio del final malvado")]
    [Tooltip("Opcional. El audio empieza al abrir los ojos (durante el recorrido) y el texto aparece al terminar el recorrido, antes de cerrar.")]
    [SerializeField] TextosFinal textosFinal;
    [Tooltip("Segundos del fundido para abrir y cerrar los ojos alrededor del texto final.")]
    [SerializeField, Min(0f)] float duracionFundidoTexto = 0.8f;

    [Header("Cierre")]
    [Tooltip("Cerrar el juego al terminar (en el editor detiene el Play).")]
    [SerializeField] bool cerrarAlTerminar = true;

    [Header("Eventos")]
    public UnityEvent alEmpezarFinal;
    public UnityEvent alTerminarFinal;

    bool enCurso;

    void Awake()
    {
        if (saco != null) saco.SetActive(false);
    }

    void OnEnable()
    {
        if (director != null) director.OnJugadorAtrapado.AddListener(EmpezarFinal);
        else Debug.LogWarning("[FinalSaco] Falta asignar el FishingSilbonDirector en " + name, this);
    }

    void OnDisable()
    {
        if (director != null) director.OnJugadorAtrapado.RemoveListener(EmpezarFinal);
    }

    [ContextMenu("Empezar final (prueba)")]
    public void EmpezarFinal()
    {
        if (enCurso || TextosFinal.AlgunoEnCurso || TextosFinal.IntroEnCurso) return;
        enCurso = true;
        if (textosFinal != null) textosFinal.Empezar();
        StartCoroutine(RutinaFinal());
    }

    IEnumerator RutinaFinal()
    {
        Debug.Log("[FinalSaco] El Silbon atrapo al jugador: empieza el final.", this);
        alEmpezarFinal.Invoke();

        if (esperaTrasAtrapar > 0f) yield return new WaitForSeconds(esperaTrasAtrapar);

        // 1. A negro
        yield return Fundir(0f, 1f, duracionFundidoEntrada);

        // 2. A oscuras: preparar el final
        foreach (var b in desactivarDuranteFinal) if (b != null) b.enabled = false;
        foreach (var go in ocultarDuranteFinal) if (go != null) go.SetActive(false);

        ColocarJugador();

        if (saco != null && inicioRecorrido != null && finRecorrido != null)
        {
            saco.transform.position = inicioRecorrido.position;
            saco.transform.rotation = RotacionHacia(inicioRecorrido.position, finRecorrido.position, saco.transform.rotation);
            saco.SetActive(true);

            if (animatorSaco != null)
            {
                // El script mueve al saco: el root motion de la animacion no debe desplazarlo
                animatorSaco.applyRootMotion = false;
                if (!string.IsNullOrEmpty(parametroMantenerCaminar))
                {
                    bool existe = false;
                    foreach (var p in animatorSaco.parameters)
                        if (p.name == parametroMantenerCaminar && p.type == AnimatorControllerParameterType.Bool) { existe = true; break; }
                    if (existe) animatorSaco.SetBool(parametroMantenerCaminar, true);
                    else Debug.LogWarning($"[FinalSaco] El Animator no tiene el parametro bool '{parametroMantenerCaminar}'.", this);
                }
                int hash = Animator.StringToHash(estadoCaminar);
                if (animatorSaco.HasState(0, hash)) animatorSaco.Play(hash, 0, 0f);
                else Debug.LogWarning($"[FinalSaco] El Animator no tiene el estado '{estadoCaminar}'.", this);
            }
        }
        else
        {
            Debug.LogWarning("[FinalSaco] Faltan referencias del saco (objeto, inicio o fin del recorrido).", this);
        }

        if (tiempoEnNegro > 0f) yield return new WaitForSeconds(tiempoEnNegro);

        // 3. Abrir los ojos mientras empieza a caminar
        float t = 0f;
        bool hayRecorrido = saco != null && inicioRecorrido != null && finRecorrido != null;
        bool usarNiebla = nieblaConsume && hayRecorrido && cabeza != null && RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear;
        float nieblaIni0 = RenderSettings.fogStartDistance, nieblaFin0 = RenderSettings.fogEndDistance;
        StartCoroutine(Fundir(1f, 0f, duracionFundidoApertura));
        if (textosFinal != null) textosFinal.ReproducirAudio();

        // 4. Camina en linea recta hacia la oscuridad
        while (t < duracionRecorrido)
        {
            t += Time.deltaTime;
            if (hayRecorrido)
                saco.transform.position = Vector3.Lerp(inicioRecorrido.position, finRecorrido.position, t / duracionRecorrido);

            if (usarNiebla) AplicarNieblaSobreSilbon(Mathf.Clamp01(t / duracionRecorrido), nieblaIni0, nieblaFin0);

            // Fundido final solapado con el ultimo tramo del recorrido
            if (t >= duracionRecorrido - duracionFundidoFinal)
                FundidoParpadeo.Poner(Mathf.InverseLerp(duracionRecorrido - duracionFundidoFinal, duracionRecorrido, t));
            yield return null;
        }
        FundidoParpadeo.Poner(1f);

        // 5. Texto final malvado: a oscuras se coloca frente al jugador, se abren los ojos,
        //    se queda hasta que termine el audio del narrador y se vuelve a negro.
        if (textosFinal != null)
        {
            textosFinal.ColocarFrenteAlJugador();
            yield return Fundir(1f, 0f, duracionFundidoTexto);
            yield return textosFinal.MostrarTextos();
            yield return textosFinal.EsperarAudio();
            yield return Fundir(0f, 1f, duracionFundidoTexto);
        }

        alTerminarFinal.Invoke();
        Debug.Log("[FinalSaco] Fin del recorrido.", this);

        if (cerrarAlTerminar) Cerrar();
    }

    /// <summary>
    /// Ajusta la niebla lineal para que el Silbon (a su distancia actual de la camara) quede con una fraccion de niebla
    /// que crece de lo que ya tenia hasta nieblaFinal. Asi se va disolviendo en el fondo conforme se aleja.
    /// </summary>
    void AplicarNieblaSobreSilbon(float p, float inicio0, float fin0)
    {
        float d = Vector3.Distance(cabeza.position, saco.transform.position + Vector3.up);
        float inicio = Mathf.Lerp(inicio0, Mathf.Min(inicio0, nieblaInicioCerrada), p);
        inicio = Mathf.Min(inicio, d * 0.8f);

        float f0 = Mathf.Clamp01((d - inicio0) / Mathf.Max(0.01f, fin0 - inicio0));
        float f = Mathf.Lerp(f0, nieblaFinal, Mathf.Pow(p, curvaNiebla));
        if (f <= 0.001f) return;

        float fin = inicio + (d - inicio) / f;
        RenderSettings.fogStartDistance = inicio;
        RenderSettings.fogEndDistance = Mathf.Min(fin, fin0);
    }

    /// <summary>Lleva al jugador al punto de vista (a oscuras): cabeza sobre el punto y mirando hacia su forward.</summary>
    void ColocarJugador()
    {
        if (rigJugador == null || cabeza == null || puntoVista == null)
        {
            Debug.LogWarning("[FinalSaco] Faltan referencias del jugador (rig, cabeza o punto de vista): se queda donde esta.", this);
            return;
        }

        Vector3 mirada = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up);
        Vector3 objetivo = Vector3.ProjectOnPlane(puntoVista.forward, Vector3.up);
        if (mirada.sqrMagnitude > 0.0001f && objetivo.sqrMagnitude > 0.0001f)
            rigJugador.RotateAround(cabeza.position, Vector3.up, Vector3.SignedAngle(mirada, objetivo, Vector3.up));

        Vector3 desplazamiento = puntoVista.position - cabeza.position;
        desplazamiento.y = 0f;
        rigJugador.position += desplazamiento;
        Physics.SyncTransforms();
    }

    static Quaternion RotacionHacia(Vector3 desde, Vector3 hacia, Quaternion porDefecto)
    {
        Vector3 dir = hacia - desde;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : porDefecto;
    }

    IEnumerator Fundir(float desde, float hasta, float duracion)
    {
        return FundidoParpadeo.Fundir(desde, hasta, duracion);
    }

    void Cerrar()
    {
        Debug.Log("[FinalSaco] Cerrando el juego.", this);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
