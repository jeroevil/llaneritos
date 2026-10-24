using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Orquesta el avance de la pesca, el comportamiento del Silbon y la atmosfera ambiental.
/// Incluye la Opcion 2: Transicion de Climax con Parpadeo Cinematografico (Fade to Black / Blink de Terror)
/// para que el cambio de Skybox al pez 10 ocurra "a ojos cerrados" y se sienta natural y aterrador.
/// </summary>
public class FishingSilbonDirector : MonoBehaviour
{
    [Header("Referencias de Timeline")]
    [Tooltip("El PlayableDirector de la Timeline que orquesta el ambiente")]
    public PlayableDirector playableDirector;

    [Header("Post-Procesado y Volumenes")]
    [Tooltip("El volumen del ambiente normal (se ira apagando en los ultimos peces)")]
    public Volume volumeNormal;

    [Tooltip("El volumen de terror (su peso subira en los ultimos peces)")]
    public Volume volumeTerror;

    [Tooltip("Cuantos peces antes del final empieza a aumentar el peso del terror (ej. los ultimos 3 peces)")]
    public int pecesFinalesDeTerror = 3;

    [Tooltip("Velocidad con la que el ambiente se funde y oscurece")]
    public float velocidadTransicionAmbiente = 2.0f;

    [Header("Skyboxes")]
    [Tooltip("Objeto de la esfera del cielo bueno (skybox esfera silbon (Bueno))")]
    public GameObject skyboxBueno;

    [Tooltip("Objeto de la esfera del cielo malo (skybox esfera silbon (malo))")]
    public GameObject skyboxMalo;

    [Header("Transicion de Terror (Opcion 2: Parpadeo Cinematografico)")]
    [Tooltip("Volumen URP dedicado para el parpadeo a negro. Si esta vacio, se genera automaticamente en ejecucion.")]
    public Volume volumeBlink;

    [Tooltip("Si esta activo, hace un micro-pestaneo rapido de sobresalto antes del apagon total")]
    public bool dobleParpadeo = true;

    [Tooltip("Duracion en segundos del cierre de ojos (fundido a negro)")]
    [Range(0.05f, 1.0f)]
    public float duracionCierreOjos = 0.22f;

    [Tooltip("Tiempo en segundos que se queda totalmente a oscuras mientras cambia la realidad")]
    [Range(0.05f, 1.0f)]
    public float tiempoEnOscuridad = 0.15f;

    [Tooltip("Duracion en segundos de la apertura de ojos hacia el nuevo cielo")]
    [Range(0.05f, 1.5f)]
    public float duracionAperturaOjos = 0.35f;

    [Header("Silbon Enemigo")]
    [Tooltip("El GameObject del Silbon en escena (silvon unity)")]
    public GameObject silbonEnemigo;

    [Header("Acecho Dinamico Progresivo")]
    [Tooltip("A partir de que pez empieza a acechar el Silbon (por defecto 4). Antes de este pez habra calma.")]
    public int pezInicioAcecho = 4;

    [Tooltip("Lista de puntos de acecho creados por ti donde el Silbon puede asomarse.")]
    public List<Transform> puntosDeAcecho = new List<Transform>();

    [Tooltip("Objeto padre que contiene los puntos de acecho. Si lo asignas, sus hijos se agregan automaticamente a la lista.")]
    public Transform contenedorPuntosAcecho;

    [Header("Prioridad de Campo de Vision (FOV)")]
    [Tooltip("Prioriza puntos que esten dentro del campo visual del jugador para que siempre vea que lo stalkea.")]
    public bool priorizarCampoDeVision = true;

    [Tooltip("Angulo maximo en grados desde el centro de la mirada del jugador para considerar un punto 'en vision' (75° cubre un FOV amplio de 150°).")]
    [Range(30f, 90f)]
    public float anguloVisionMaximo = 75f;

    [Header("Duracion del Vistazo Progresivo")]
    [Tooltip("Segundos que se deja ver en el primer acecho (pez 4) - vistazo fugaz donde dudas de si lo viste.")]
    public float duracionVistazoMinimo = 1.3f;

    [Tooltip("Segundos que se deja ver en el ultimo acecho antes de la persecucion (pez 9) - se queda mirandote fijamente aterrador.")]
    public float duracionVistazoMaximo = 4.5f;

    [Header("Desplazamiento Lateral en Acecho (Gateo A -> B)")]
    [Tooltip("Hace que el Silbon no se quede quieto en el lugar, sino que gatee lateralmente en el fondo de un lado a otro")]
    public bool moverSilbonLateralmente = true;

    [Tooltip("Distancia en metros que recorre lateralmente en el fondo (ej. 5 a 8 metros)")]
    [Range(2f, 15f)]
    public float distanciaRecorridoLateral = 6.0f;

    [Tooltip("Alterna aleatoriamente la direccion del gateo (izquierda a derecha o viceversa)")]
    public bool alternarDireccionLateral = true;

    [Header("Punto de Persecucion Final (Pez 10)")]
    [Tooltip("Lugar donde aparecera el Silbon para arrancar a correr al 100%")]
    public Transform puntoInicioPersecucion;

    [Tooltip("La camara del jugador hacia donde el Silbon debe mirar fijamente")]
    public Transform jugadorOcular;

    [Header("Animaciones del Silbon (Mixamo)")]
    [Tooltip("El componente Animator del Silbon. Si esta vacio, se busca automaticamente en silbonEnemigo.")]
    public Animator animatorSilbon;

    [Header("Persecucion con NavMesh (Pez 10)")]
    [Tooltip("El componente NavMeshAgent del Silbon. Si esta vacio, se busca automaticamente en silbonEnemigo.")]
    public NavMeshAgent navMeshAgentSilbon;

    [Tooltip("Velocidad a la que el Silbon persigue al jugador tras el apagon del pez 10.")]
    public float velocidadPersecucion = 4.2f;

    [Tooltip("Segundos que dura la animacion de emerger (Silbon_Emerge_003) antes de arrancar a correr a toda velocidad.")]
    [Range(1f, 10f)]
    public float duracionEmergencia = 3.5f;

    [Tooltip("Distancia en metros para considerar que el Silbon atrapo al jugador.")]
    public float distanciaCapturaJugador = 1.3f;

    [Tooltip("Evento que se dispara cuando el Silbon alcanza al jugador (Jumpscare / Game Over).")]
    public UnityEvent OnJugadorAtrapado;

    [HideInInspector] public Transform puntoAcecho1;
    [HideInInspector] public Transform puntoAcecho2;

    [Header("Sistema de Audio")]
    [Tooltip("Gestor central de audio (ambientes, silbido 3D, recompensa de pez y musica de persecucion)")]
    public SilbonAudioManager audioManager;

    [Header("Progreso de Pesca")]
    [Tooltip("Total de peces necesarios para llenar la barra al 100%")]
    public int totalFishNeeded = 10;

    [Tooltip("Cantidad actual de peces depositados en el barril")]
    public int currentFishCount = 0;

    [Header("Comportamiento de la Timeline")]
    [Tooltip("Segundo de la Timeline donde termina la fase de acecho y arranca la persecucion")]
    public float chaseStartSecond = 10f;

    [Tooltip("Si esta activo, al llegar al 100% la Timeline se reproduce sola hacia adelante (Play) para la persecucion")]
    public bool autoPlayChaseAtMax = true;

    [Header("Testing en Editor (Debug)")]
    [Tooltip("Presiona ESPACIO para sumar un pez de prueba. Presiona R para reiniciar.")]
    public bool enableKeyboardDebug = true;

    [Header("Eventos de Fase (Opcionales)")]
    public UnityEvent OnPhase1Calm;       // Calma
    public UnityEvent OnPhase2Stalking;   // Acecho
    public UnityEvent OnPhase3Chase;      // 100% Caceria

    private int _currentPhase = 1;
    private bool _chaseTriggered = false;
    private float _targetTerrorWeight = 0f;
    private float _currentTerrorWeight = 0f;
    private Coroutine _rutinaVistazo;
    private Coroutine _rutinaClimax;
    private Coroutine _rutinaPersecucion;
    private Transform _ultimoPuntoAcecho;

    private void Awake()
    {
        AutoBuscarReferencias();
    }

    private void Start()
    {
        AutoBuscarReferencias();

        // 1. Congelar Timeline al inicio en t = 0
        if (playableDirector != null)
        {
            playableDirector.playOnAwake = false;
            playableDirector.Stop();
            playableDirector.time = 0;
            playableDirector.Evaluate();
            playableDirector.Pause();
        }

        // 2. Silbon oculto inicialmente
        if (silbonEnemigo != null)
            silbonEnemigo.SetActive(false);

        // 3. Skyboxes: Bueno activo, Malo inactivo
        if (skyboxBueno != null) skyboxBueno.SetActive(true);
        if (skyboxMalo != null) skyboxMalo.SetActive(false);

        // 4. Inicializar pesos: Normal al 100%, Terror al 0%, Blink en 0
        _targetTerrorWeight = 0f;
        _currentTerrorWeight = 0f;
        AplicarPesosInmediatos(0f);

        if (volumeBlink != null)
            volumeBlink.weight = 0f;

        SetProgressNormalized(0f);
    }

    /// <summary>
    /// Localiza automaticamente todos los objetos de la escena por nombre para evitar fallos si no se arrastran en el Inspector.
    /// Funciona incluso si los GameObjects estan inactivos.
    /// </summary>
    private void AutoBuscarReferencias()
    {
        // 1. Camara del jugador
        if (jugadorOcular == null)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                jugadorOcular = cam.transform;
            }
            else
            {
                var goCam = BuscarObjetoEnEscena("CenterEyeAnchor") ?? BuscarObjetoEnEscena("Main Camera");
                if (goCam != null) jugadorOcular = goCam.transform;
            }
        }

        // 2. Director de Timeline
        if (playableDirector == null)
            playableDirector = GetComponent<PlayableDirector>();

        // 3. Silbon Enemigo
        if (silbonEnemigo == null)
        {
            silbonEnemigo = BuscarObjetoEnEscena("silvon unity") ?? BuscarObjetoEnEscena("silbon");
            if (silbonEnemigo != null)
                Debug.Log("[Pesca] Encontrado automáticamente el objeto Silbón: " + silbonEnemigo.name);
        }

        if (navMeshAgentSilbon == null && silbonEnemigo != null)
        {
            navMeshAgentSilbon = silbonEnemigo.GetComponent<NavMeshAgent>();
            if (navMeshAgentSilbon != null)
                Debug.Log("[Pesca] Encontrado automáticamente NavMeshAgent en: " + silbonEnemigo.name);
        }

        if (animatorSilbon == null && silbonEnemigo != null)
        {
            animatorSilbon = silbonEnemigo.GetComponent<Animator>();
            if (animatorSilbon == null)
                animatorSilbon = silbonEnemigo.GetComponentInChildren<Animator>();
            if (animatorSilbon != null)
                Debug.Log("[Pesca] Encontrado automáticamente Animator en: " + silbonEnemigo.name);
        }

        // 4. Skyboxes
        if (skyboxBueno == null)
        {
            skyboxBueno = BuscarObjetoEnEscena("skybox esfera silbon (Bueno)") ?? BuscarObjetoEnEscena("skybox esfera buena");
            if (skyboxBueno != null)
                Debug.Log("[Pesca] Encontrado automáticamente: " + skyboxBueno.name);
        }

        if (skyboxMalo == null)
        {
            skyboxMalo = BuscarObjetoEnEscena("skybox esfera silbon (malo)") ?? BuscarObjetoEnEscena("skybox esfera silbon");
            if (skyboxMalo != null)
                Debug.Log("[Pesca] Encontrado automáticamente: " + skyboxMalo.name);
        }

        // Desactivar colliders de las esferas para que no bloqueen el raycast ni el agarrado en VR
        DesactivarColisionadorSkybox(skyboxBueno);
        DesactivarColisionadorSkybox(skyboxMalo);

        // 5. Volumenes
        if (volumeTerror == null)
        {
            var goTerror = BuscarObjetoEnEscena("volumen terror") ?? BuscarObjetoEnEscena("Volume_Terror");
            if (goTerror != null)
            {
                volumeTerror = goTerror.GetComponent<Volume>();
                Debug.Log("[Pesca] Encontrado automáticamente: 'volumen terror'.");
            }
        }

        if (volumeNormal == null)
        {
            var goNormal = BuscarObjetoEnEscena("Volumen normal") ?? BuscarObjetoEnEscena("Volume_Normal");
            if (goNormal != null)
            {
                volumeNormal = goNormal.GetComponent<Volume>();
                Debug.Log("[Pesca] Encontrado automáticamente: 'Volumen normal'.");
            }
        }

        if (volumeTerror != null && volumeNormal != null)
        {
            if (volumeTerror.priority <= volumeNormal.priority)
                volumeTerror.priority = volumeNormal.priority + 1;
        }

        // 6. Volumen dedicado de Parpadeo / Blink (negro absoluto)
        InicializarVolumenBlink();

        // 7. Puntos de Acecho y Persecucion
        AutoBuscarOInstanciarPuntos();

        // 8. Gestor de Audio
        if (audioManager == null)
        {
            audioManager = FindFirstObjectByType<SilbonAudioManager>();
            if (audioManager != null)
                Debug.Log("[Pesca] Encontrado automáticamente SilbonAudioManager: " + audioManager.name);
        }
    }

    private void DesactivarColisionadorSkybox(GameObject goSkybox)
    {
        if (goSkybox == null) return;
        var col = goSkybox.GetComponent<Collider>();
        if (col != null && col.enabled)
        {
            col.enabled = false;
            Debug.Log($"[Pesca] Desactivado Collider en {goSkybox.name} para no obstruir interacciones VR.");
        }
    }

    private void InicializarVolumenBlink()
    {
        if (volumeBlink != null) return;

        var goBlink = BuscarObjetoEnEscena("Volume_Blink_Runtime");
        if (goBlink != null)
        {
            volumeBlink = goBlink.GetComponent<Volume>();
            return;
        }

        // Crear volumen URP dinamico con maxima prioridad (1000)
        GameObject nuevoBlink = new GameObject("Volume_Blink_Runtime");
        nuevoBlink.transform.SetParent(this.transform);
        volumeBlink = nuevoBlink.AddComponent<Volume>();
        volumeBlink.isGlobal = true;
        volumeBlink.priority = 1000;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "BlinkProfile_Generated";

        var colorAdjustments = profile.Add<ColorAdjustments>(true);
        colorAdjustments.postExposure.overrideState = true;
        colorAdjustments.postExposure.value = -15f; // Negro absoluto

        volumeBlink.profile = profile;
        volumeBlink.weight = 0f;
    }

    private void AutoBuscarOInstanciarPuntos()
    {
        // 1. Sincronizar puntos desde contenedor si fue asignado
        if (contenedorPuntosAcecho != null)
        {
            for (int i = 0; i < contenedorPuntosAcecho.childCount; i++)
            {
                var hijo = contenedorPuntosAcecho.GetChild(i);
                if (!puntosDeAcecho.Contains(hijo))
                    puntosDeAcecho.Add(hijo);
            }
        }
        else
        {
            // Intentar encontrar un contenedor con nombres comunes en escena
            var goContenedor = BuscarObjetoEnEscena("Puntos Acecho") ?? BuscarObjetoEnEscena("Puntos Silbon") ?? BuscarObjetoEnEscena("PuntosAcecho") ?? BuscarObjetoEnEscena("Spawnpoints Silbon") ?? BuscarObjetoEnEscena("Puntos de Acecho");
            if (goContenedor != null)
            {
                contenedorPuntosAcecho = goContenedor.transform;
                for (int i = 0; i < contenedorPuntosAcecho.childCount; i++)
                {
                    var hijo = contenedorPuntosAcecho.GetChild(i);
                    if (!puntosDeAcecho.Contains(hijo))
                        puntosDeAcecho.Add(hijo);
                }
                Debug.Log($"[Silbon] Encontrado contenedor '{goContenedor.name}' con {contenedorPuntosAcecho.childCount} puntos de acecho.");
            }
        }

        // Limpiar nulos
        puntosDeAcecho.RemoveAll(item => item == null);

        // Si aún no hay puntos asignados en la lista, buscar puntos sueltos o generarlos de reserva
        if (puntosDeAcecho.Count == 0)
        {
            Vector3 basePos = jugadorOcular != null ? jugadorOcular.position : Vector3.zero;
            Vector3 forward = jugadorOcular != null ? jugadorOcular.forward : Vector3.forward;
            Vector3 right = jugadorOcular != null ? jugadorOcular.right : Vector3.right;
            forward.y = 0; right.y = 0;
            forward.Normalize(); right.Normalize();

            // Puntos de fallback a diferentes distancias y ángulos
            GameObject rootFallback = new GameObject("Puntos Silbon (Auto-Generados)");
            contenedorPuntosAcecho = rootFallback.transform;

            Vector3[] offsets = new Vector3[]
            {
                (forward * 36f) + (right * 16f),   // Lejano derecha (pez 4)
                (forward * 32f) - (right * 18f),   // Lejano izquierda (pez 5)
                (forward * 22f) + (right * 10f),   // Medio derecha (pez 6)
                (forward * 18f) - (right * 12f),   // Medio izquierda (pez 7)
                (forward * 11f) + (right * 5f),    // Cercano derecha (pez 8)
                (forward * 7f) - (right * 3f)      // Muy cercano (pez 9)
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject p = new GameObject($"Punto_Acecho_{i + 1}");
                p.transform.SetParent(rootFallback.transform);
                p.transform.position = basePos + offsets[i];
                puntosDeAcecho.Add(p.transform);
            }

            Debug.Log("[Silbon] Se generaron puntos de acecho de respaldo automáticos.");
        }

        // Punto de persecución para el 100%
        if (puntoInicioPersecucion == null)
        {
            var go = BuscarObjetoEnEscena("PuntoInicioPersecucion") ?? BuscarObjetoEnEscena("Punto_Persecucion");
            if (go != null) puntoInicioPersecucion = go.transform;
            else
            {
                Vector3 basePos = jugadorOcular != null ? jugadorOcular.position : Vector3.zero;
                Vector3 forward = jugadorOcular != null ? jugadorOcular.forward : Vector3.forward;
                forward.y = 0; forward.Normalize();
                GameObject pChase = new GameObject("PuntoInicioPersecucion");
                pChase.transform.position = basePos + (forward * 14f);
                puntoInicioPersecucion = pChase.transform;
            }
        }
    }

    private GameObject BuscarObjetoEnEscena(string nombre)
    {
        var escena = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (escena.isLoaded)
        {
            foreach (var root in escena.GetRootGameObjects())
            {
                if (root.name == nombre) return root;
                var res = BuscarRecursivo(root.transform, nombre);
                if (res != null) return res.gameObject;
            }
        }

        var todos = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in todos)
        {
            if (t.name == nombre) return t.gameObject;
        }

        return null;
    }

    private Transform BuscarRecursivo(Transform padre, string nombre)
    {
        for (int i = 0; i < padre.childCount; i++)
        {
            var hijo = padre.GetChild(i);
            if (hijo.name == nombre) return hijo;
            var res = BuscarRecursivo(hijo, nombre);
            if (res != null) return res;
        }
        return null;
    }

    private void Update()
    {
        // Interpolacion suave del peso del volumen de terror
        if (Mathf.Abs(_currentTerrorWeight - _targetTerrorWeight) > 0.001f)
        {
            _currentTerrorWeight = Mathf.MoveTowards(_currentTerrorWeight, _targetTerrorWeight, Time.deltaTime * velocidadTransicionAmbiente);
            AplicarPesosInmediatos(_currentTerrorWeight);
        }

        if (!enableKeyboardDebug) return;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                AddFish(1);
            }
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                ResetProgress();
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Space))
        {
            AddFish(1);
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetProgress();
        }
#endif
    }

    private void AplicarPesosInmediatos(float terrorWeight)
    {
        if (volumeTerror != null)
            volumeTerror.weight = Mathf.Clamp01(terrorWeight);

        if (volumeNormal != null)
            volumeNormal.weight = Mathf.Clamp01(1f - terrorWeight);

        if (audioManager != null)
            audioManager.SetTerrorLevel(terrorWeight);
    }

    /// <summary>
    /// Llamar a este metodo cada vez que un pez entra al barril.
    /// </summary>
    public void AddFish(int amount = 1)
    {
        if (_chaseTriggered) return;

        currentFishCount = Mathf.Clamp(currentFishCount + amount, 0, totalFishNeeded);
        float progress = (float)currentFishCount / totalFishNeeded;

        CalcularPesoTerror(currentFishCount);

        Debug.Log($"[Pesca] Pez depositado ({currentFishCount}/{totalFishNeeded}) - Progreso: {progress * 100f:F0}% | Peso Terror Objetivo: {_targetTerrorWeight:F2}");

        if (audioManager != null)
            audioManager.PlayPezPoint();

        ApplyProgress(progress);
    }

    private void CalcularPesoTerror(int pecesActuales)
    {
        int pezInicioTerror = Mathf.Max(1, totalFishNeeded - pecesFinalesDeTerror);

        if (pecesActuales < pezInicioTerror)
        {
            _targetTerrorWeight = 0f; // Calma total
        }
        else
        {
            float paso = (float)(pecesActuales - pezInicioTerror) / (totalFishNeeded - pezInicioTerror);
            _targetTerrorWeight = Mathf.Clamp01(paso);
        }
    }

    public void SetProgressNormalized(float progress)
    {
        currentFishCount = Mathf.RoundToInt(progress * totalFishNeeded);
        CalcularPesoTerror(currentFishCount);
        ApplyProgress(progress);
    }

    private void ApplyProgress(float progress)
    {
        CheckPhaseAndStalking(progress);

        if (progress >= 1f)
        {
            TriggerChasePhase();
        }
        else
        {
            if (playableDirector != null)
            {
                float targetTime = Mathf.Clamp(progress * chaseStartSecond, 0f, chaseStartSecond);
                playableDirector.time = targetTime;
                playableDirector.Evaluate();
                playableDirector.Pause();
            }
        }
    }

    private void TriggerChasePhase()
    {
        if (_chaseTriggered) return;
        _chaseTriggered = true;

        if (_rutinaVistazo != null)
            StopCoroutine(_rutinaVistazo);

        if (_rutinaClimax != null)
            StopCoroutine(_rutinaClimax);

        _rutinaClimax = StartCoroutine(RutinaClimaxConParpadeo());
    }

    /// <summary>
    /// Corrutina del Climax:
    /// Parpadeo / apagon de terror para que el intercambio de Skybox y aparicion del Silbon ocurran a oscuras.
    /// </summary>
    private IEnumerator RutinaClimaxConParpadeo()
    {
        Debug.Log("[Silbon] ¡100% completado! Ejecutando transicion cinematografica con parpadeo de terror...");

        // 1. Opcional: Micro-pestaneo preliminar de alerta/sobresalto
        if (dobleParpadeo)
        {
            yield return StartCoroutine(RutinaFundidoBlink(0.08f, 0f, 0.65f));
            yield return StartCoroutine(RutinaFundidoBlink(0.08f, 0.65f, 0.1f));
            yield return new WaitForSeconds(0.05f);
        }

        // 2. Fundido a negro (cierre de ojos rápido de terror)
        float pesoInicial = volumeBlink != null ? volumeBlink.weight : 0f;
        yield return StartCoroutine(RutinaFundidoBlink(duracionCierreOjos, pesoInicial, 1.0f));

        if (audioManager != null)
            audioManager.PlayCinematicBlink();

        // 3. CAMBIO A OSCURAS: Toda la realidad muta mientras la pantalla esta negra
        _targetTerrorWeight = 1.0f;
        _currentTerrorWeight = 1.0f;
        AplicarPesosInmediatos(1.0f);

        // Intercambiar skyboxes a ciegas
        if (skyboxBueno != null) skyboxBueno.SetActive(false);
        if (skyboxMalo != null) skyboxMalo.SetActive(true);

        // Posicionar y activar al Silbon frente al jugador
        if (silbonEnemigo != null)
        {
            if (puntoInicioPersecucion != null)
            {
                silbonEnemigo.transform.position = puntoInicioPersecucion.position;
                silbonEnemigo.transform.rotation = puntoInicioPersecucion.rotation;
            }

            if (jugadorOcular != null)
            {
                Vector3 mirad = (jugadorOcular.position - silbonEnemigo.transform.position);
                mirad.y = 0;
                if (mirad.sqrMagnitude > 0.001f)
                    silbonEnemigo.transform.rotation = Quaternion.LookRotation(mirad);
            }

            silbonEnemigo.SetActive(true);

            // El NavMeshAgent se activa DESPUES de emerger
            if (navMeshAgentSilbon != null)
            {
                navMeshAgentSilbon.enabled = false;
            }
        }

        // Si la Timeline maneja pistas de animacion, posicionarla en el clímax
        if (playableDirector != null && autoPlayChaseAtMax)
        {
            playableDirector.time = chaseStartSecond;
            playableDirector.Evaluate();
            playableDirector.Play();
        }

        OnPhase3Chase?.Invoke();

        // Breve pausa en negro absoluto (sensacion de haber cerrado los ojos en shock)
        yield return new WaitForSeconds(tiempoEnOscuridad);

        // 4. Apertura de ojos: la vista regresa y el mundo ya es la pesadilla roja
        yield return StartCoroutine(RutinaFundidoBlink(duracionAperturaOjos, 1.0f, 0.0f));

        Debug.Log("[Silbon] ¡Ojos abiertos! El Silbón emerge frente al jugador...");

        // Activar animacion de emerger (Emerge) en el Animator
        if (animatorSilbon == null && silbonEnemigo != null)
        {
            animatorSilbon = silbonEnemigo.GetComponent<Animator>() ?? silbonEnemigo.GetComponentInChildren<Animator>();
        }

        if (animatorSilbon != null)
        {
            animatorSilbon.enabled = true;
            animatorSilbon.SetBool("Acechando", false);
            animatorSilbon.SetBool("Emergiendo", true);
            animatorSilbon.SetBool("Corriendo", false);
            animatorSilbon.SetInteger("Estado", 2);
            if (animatorSilbon.HasState(0, Animator.StringToHash("Emerge")))
                animatorSilbon.Play("Emerge", 0, 0f);
            else if (animatorSilbon.HasState(0, Animator.StringToHash("Silbon_Emerge_003")))
                animatorSilbon.Play("Silbon_Emerge_003", 0, 0f);
            animatorSilbon.Update(0f);
        }

        // Tiempo en que el Silbon emerge y se levanta amenazante
        if (duracionEmergencia > 0f)
        {
            yield return new WaitForSeconds(duracionEmergencia);
        }

        if (audioManager != null)
            audioManager.StartChaseMusic();

        Debug.Log("[Silbon] ¡Ha emergido completamente! La persecución con NavMeshAgent ha comenzado.");

        // Activar animacion de correr (Run) en el Animator
        if (animatorSilbon != null)
        {
            animatorSilbon.SetBool("Emergiendo", false);
            animatorSilbon.SetBool("Corriendo", true);
            animatorSilbon.SetInteger("Estado", 3);
            if (animatorSilbon.HasState(0, Animator.StringToHash("Run")))
                animatorSilbon.CrossFadeInFixedTime("Run", 0.2f);
            animatorSilbon.Update(0f);
        }

        // Activar NavMeshAgent para la persecución implacable
        if (navMeshAgentSilbon != null)
        {
            navMeshAgentSilbon.enabled = true;
            navMeshAgentSilbon.speed = velocidadPersecucion;
            if (puntoInicioPersecucion != null)
                navMeshAgentSilbon.Warp(puntoInicioPersecucion.position);
        }

        // Iniciar persecución continua tras el jugador
        if (_rutinaPersecucion != null)
            StopCoroutine(_rutinaPersecucion);
        _rutinaPersecucion = StartCoroutine(RutinaPersecucionJugador());
    }

    /// <summary>
    /// Bucle de persecucion en tiempo real: el Silbon sigue la posicion del jugador con su NavMeshAgent
    /// esquivando arboles, rocas y agua. Si alcanza al jugador, dispara el evento de captura.
    /// </summary>
    private IEnumerator RutinaPersecucionJugador()
    {
        while (_chaseTriggered && silbonEnemigo != null)
        {
            if (jugadorOcular != null && navMeshAgentSilbon != null && navMeshAgentSilbon.isActiveAndEnabled)
            {
                if (navMeshAgentSilbon.isOnNavMesh)
                {
                    navMeshAgentSilbon.isStopped = false;
                    navMeshAgentSilbon.SetDestination(jugadorOcular.position);

                    float dist = Vector3.Distance(silbonEnemigo.transform.position, jugadorOcular.position);
                    if (dist <= distanciaCapturaJugador)
                    {
                        Debug.Log("[Silbon] ¡El Silbón te ha atrapado! (Jumpscare)");
                        if (animatorSilbon != null)
                        {
                            animatorSilbon.SetBool("Corriendo", false);
                            animatorSilbon.SetBool("Atrapando", true);
                            animatorSilbon.SetInteger("Estado", 4);
                            if (animatorSilbon.HasState(0, Animator.StringToHash("Atrapa")))
                                animatorSilbon.CrossFadeInFixedTime("Atrapa", 0.1f);
                            else if (animatorSilbon.HasState(0, Animator.StringToHash("Silbon_Atrapa")))
                                animatorSilbon.CrossFadeInFixedTime("Silbon_Atrapa", 0.1f);
                        }
                        OnJugadorAtrapado?.Invoke();
                        navMeshAgentSilbon.isStopped = true;
                        yield break;
                    }
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    private IEnumerator RutinaFundidoBlink(float duracion, float pesoInicio, float pesoFin)
    {
        if (volumeBlink == null || duracion <= 0.001f)
        {
            if (volumeBlink != null) volumeBlink.weight = pesoFin;
            yield break;
        }

        float t = 0f;
        while (t < duracion)
        {
            t += Time.deltaTime;
            float factor = Mathf.Clamp01(t / duracion);
            volumeBlink.weight = Mathf.Lerp(pesoInicio, pesoFin, factor);
            yield return null;
        }

        volumeBlink.weight = pesoFin;
    }

    private void CheckPhaseAndStalking(float progress)
    {
        if (progress >= 1f)
        {
            // Pez 10: persecución
            return;
        }

        if (currentFishCount < pezInicioAcecho)
        {
            // Fase de Calma (antes del pez 4)
            if (_currentPhase != 1)
            {
                _currentPhase = 1;
                OnPhase1Calm?.Invoke();
            }

            if (silbonEnemigo != null && !_chaseTriggered)
                silbonEnemigo.SetActive(false);
            return;
        }

        // Si llegamos aqui, currentFishCount >= pezInicioAcecho (peces 4 a 9)
        if (_currentPhase != 2)
        {
            _currentPhase = 2;
            Debug.Log($"[Silbon] ¡Fase de Acecho Iniciada! (Pez {currentFishCount}). El Silbón comienza a vigilarte desde las sombras.");
            OnPhase2Stalking?.Invoke();
        }

        // 1. Normalizar avance del acecho de 0.0 (pez 4) a 1.0 (pez 9)
        int pecesAcechoTotal = Mathf.Max(1, (totalFishNeeded - 1) - pezInicioAcecho);
        float tAcecho = Mathf.Clamp01((float)(currentFishCount - pezInicioAcecho) / pecesAcechoTotal);

        // 2. Calcular duracion progresiva del vistazo (aumenta con mas peces pescados)
        float duracionVistazo = Mathf.Lerp(duracionVistazoMinimo, duracionVistazoMaximo, tAcecho);

        // 3. Seleccionar el mejor punto de acecho (por proximidad dinamica y campo de vision)
        Transform puntoElegido = SeleccionarMejorPuntoDeAcecho(tAcecho);

        // 4. Iniciar vistazo fugaz
        if (puntoElegido != null)
        {
            IniciarVistazoFugaz(puntoElegido, duracionVistazo);
        }
    }

    private struct CandidatoPunto
    {
        public Transform transform;
        public float distancia;
        public bool enVision;
        public float angulo;
    }

    /// <summary>
    /// Selecciona el punto optimo segun el progreso del acecho (tAcecho: 0=lejano, 1=cercano),
    /// priorizando puntos dentro del campo de vision del jugador (FOV) y evitando repeticiones inmediatas.
    /// </summary>
    public Transform SeleccionarMejorPuntoDeAcecho(float tAcecho)
    {
        if (puntosDeAcecho == null || puntosDeAcecho.Count == 0)
            AutoBuscarOInstanciarPuntos();

        puntosDeAcecho.RemoveAll(p => p == null);
        if (puntosDeAcecho.Count == 0) return null;
        if (puntosDeAcecho.Count == 1) return puntosDeAcecho[0];

        Vector3 posJugador = jugadorOcular != null ? jugadorOcular.position : transform.position;
        Vector3 fwdJugador = jugadorOcular != null ? jugadorOcular.forward : Vector3.forward;
        fwdJugador.y = 0;
        if (fwdJugador.sqrMagnitude > 0.001f) fwdJugador.Normalize();
        else fwdJugador = Vector3.forward;

        float distMinima = float.MaxValue;
        float distMaxima = float.MinValue;

        List<CandidatoPunto> todos = new List<CandidatoPunto>();
        foreach (var p in puntosDeAcecho)
        {
            float dist = Vector3.Distance(p.position, posJugador);
            Vector3 dir = (p.position - posJugador);
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f) dir.Normalize();

            float angulo = Vector3.Angle(fwdJugador, dir);
            bool enVision = angulo <= anguloVisionMaximo;

            if (dist < distMinima) distMinima = dist;
            if (dist > distMaxima) distMaxima = dist;

            todos.Add(new CandidatoPunto { transform = p, distancia = dist, enVision = enVision, angulo = angulo });
        }

        // Distancia objetivo: a tAcecho = 0 (pez 4) queremos los más lejanos, a tAcecho = 1 (pez 9) queremos los más cercanos
        float distObjetivo = Mathf.Lerp(distMaxima, distMinima, tAcecho);

        // Filtrar puntos que están dentro del campo de visión del jugador
        List<CandidatoPunto> candidatosEnVision = new List<CandidatoPunto>();
        foreach (var c in todos)
        {
            if (c.enVision) candidatosEnVision.Add(c);
        }

        // Si hay puntos en el campo de visión, se priorizan estrictamente esos
        List<CandidatoPunto> grupoElegible = (priorizarCampoDeVision && candidatosEnVision.Count > 0)
            ? candidatosEnVision
            : todos;

        // Dentro del grupo elegible, encontrar el más cercano a distObjetivo
        CandidatoPunto mejor = default;
        float menorDiferencia = float.MaxValue;
        bool encontrado = false;

        foreach (var c in grupoElegible)
        {
            // Evitar repetir exactamente el mismo punto consecutivo si hay más opciones en el grupo
            if (grupoElegible.Count > 1 && _ultimoPuntoAcecho != null && c.transform == _ultimoPuntoAcecho)
                continue;

            float diff = Mathf.Abs(c.distancia - distObjetivo);
            if (diff < menorDiferencia)
            {
                menorDiferencia = diff;
                mejor = c;
                encontrado = true;
            }
        }

        // Fallback por si todos eran el anterior
        if (!encontrado)
        {
            menorDiferencia = float.MaxValue;
            foreach (var c in grupoElegible)
            {
                float diff = Mathf.Abs(c.distancia - distObjetivo);
                if (diff < menorDiferencia)
                {
                    menorDiferencia = diff;
                    mejor = c;
                    encontrado = true;
                }
            }
        }

        if (encontrado && mejor.transform != null)
        {
            _ultimoPuntoAcecho = mejor.transform;
            Debug.Log($"[Silbon] Acecho Pez {currentFishCount}: Asomándose en '{mejor.transform.name}' a {mejor.distancia:F1}m (En visión: {mejor.enVision}, Ángulo: {mejor.angulo:F0}°) durante {duracionVistazoMinimo + (duracionVistazoMaximo - duracionVistazoMinimo) * tAcecho:F1}s.");
            return mejor.transform;
        }

        return puntosDeAcecho[0];
    }

    private void IniciarVistazoFugaz(Transform punto, float tiempoVisible)
    {
        if (_chaseTriggered || punto == null) return;

        if (_rutinaVistazo != null)
            StopCoroutine(_rutinaVistazo);

        _rutinaVistazo = StartCoroutine(RutinaVistazoFugaz(punto, tiempoVisible));
    }

    private void OnDrawGizmosSelected()
    {
        if (puntosDeAcecho == null || puntosDeAcecho.Count == 0) return;

        Vector3 posJugador = jugadorOcular != null ? jugadorOcular.position : transform.position;

        for (int i = 0; i < puntosDeAcecho.Count; i++)
        {
            var p = puntosDeAcecho[i];
            if (p == null) continue;

            float dist = Vector3.Distance(p.position, posJugador);

            // Color gradiente: Amarillo verdoso (lejos) -> Rojo sangre (cerca)
            Gizmos.color = Color.Lerp(Color.red, Color.yellow, Mathf.Clamp01(dist / 35f));
            Gizmos.DrawWireSphere(p.position, 0.8f);
            Gizmos.DrawLine(p.position, p.position + Vector3.up * 2.5f);

            // Línea hacia el jugador
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f);
            Gizmos.DrawLine(p.position, posJugador);

            // Dibujar trayectoria lateral de gateo A -> B en la Scene View
            if (moverSilbonLateralmente)
            {
                Vector3 dir = (p.position - posJugador);
                dir.y = 0;
                if (dir.sqrMagnitude > 0.001f)
                {
                    Vector3 lat = Vector3.Cross(Vector3.up, dir.normalized).normalized;
                    Vector3 a = p.position - lat * (distanciaRecorridoLateral * 0.5f);
                    Vector3 b = p.position + lat * (distanciaRecorridoLateral * 0.5f);
                    Gizmos.color = new Color(0f, 1f, 1f, 0.8f); // Trayectoria cian
                    Gizmos.DrawLine(a, b);
                    Gizmos.DrawWireSphere(a, 0.25f);
                    Gizmos.DrawWireSphere(b, 0.25f);
                }
            }
        }

        if (puntoInicioPersecucion != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(puntoInicioPersecucion.position, 1.5f);
            Gizmos.DrawLine(puntoInicioPersecucion.position, puntoInicioPersecucion.position + Vector3.up * 3f);
        }
    }

    private IEnumerator RutinaVistazoFugaz(Transform punto, float tiempoVisible)
    {
        if (silbonEnemigo == null) yield break;

        // Desactivar temporalmente NavMeshAgent mientras gatea lateralmente de A a B
        if (navMeshAgentSilbon != null && navMeshAgentSilbon.enabled)
            navMeshAgentSilbon.enabled = false;

        Vector3 posJugador = jugadorOcular != null ? jugadorOcular.position : transform.position;
        Vector3 dirAlPunto = (punto.position - posJugador);
        dirAlPunto.y = 0;
        if (dirAlPunto.sqrMagnitude > 0.001f) dirAlPunto.Normalize();
        else dirAlPunto = Vector3.forward;

        // Vector lateral perpendicular a la mirada del jugador (cruza el fondo horizontalmente)
        Vector3 vectorLateral = Vector3.Cross(Vector3.up, dirAlPunto).normalized;

        Vector3 puntoInicio = punto.position;
        Vector3 puntoFin = punto.position;

        if (moverSilbonLateralmente)
        {
            float mitad = distanciaRecorridoLateral * 0.5f;
            bool deIzquierdaADerecha = alternarDireccionLateral ? (Random.value > 0.5f) : true;

            if (deIzquierdaADerecha)
            {
                puntoInicio = punto.position - vectorLateral * mitad;
                puntoFin = punto.position + vectorLateral * mitad;
            }
            else
            {
                puntoInicio = punto.position + vectorLateral * mitad;
                puntoFin = punto.position - vectorLateral * mitad;
            }

            // Adaptar altura al terreno para que no empiece flotando ni bajo tierra
            if (Physics.Raycast(puntoInicio + Vector3.up * 5f, Vector3.down, out RaycastHit hitIni, 10f, ~0, QueryTriggerInteraction.Ignore))
                puntoInicio.y = hitIni.point.y;
            if (Physics.Raycast(puntoFin + Vector3.up * 5f, Vector3.down, out RaycastHit hitFin, 10f, ~0, QueryTriggerInteraction.Ignore))
                puntoFin.y = hitFin.point.y;
        }

        silbonEnemigo.transform.position = puntoInicio;

        // Encarar la dirección hacia donde va gateando
        Vector3 dirAvance = (puntoFin - puntoInicio);
        dirAvance.y = 0;
        if (dirAvance.sqrMagnitude > 0.01f)
        {
            silbonEnemigo.transform.rotation = Quaternion.LookRotation(dirAvance.normalized, Vector3.up);
        }
        else if (dirAlPunto.sqrMagnitude > 0.01f)
        {
            silbonEnemigo.transform.rotation = Quaternion.LookRotation(dirAlPunto, Vector3.up);
        }

        silbonEnemigo.SetActive(true);

        if (audioManager == null)
            audioManager = FindFirstObjectByType<SilbonAudioManager>();

        if (audioManager != null)
            audioManager.PlaySilbonStalkingSound(silbonEnemigo.transform.position, tiempoVisible);

        // Activar animacion de caminata/acecho (Walk) en el Animator
        if (animatorSilbon == null && silbonEnemigo != null)
        {
            animatorSilbon = silbonEnemigo.GetComponent<Animator>() ?? silbonEnemigo.GetComponentInChildren<Animator>();
        }

        if (animatorSilbon != null)
        {
            animatorSilbon.enabled = true;
            animatorSilbon.SetBool("Acechando", true);
            animatorSilbon.SetBool("Corriendo", false);
            animatorSilbon.SetInteger("Estado", 1);
            if (animatorSilbon.HasState(0, Animator.StringToHash("Crawl")))
                animatorSilbon.Play("Crawl", 0, 0f);
            else if (animatorSilbon.HasState(0, Animator.StringToHash("Walk")))
                animatorSilbon.Play("Walk", 0, 0f);
            else if (animatorSilbon.HasState(0, Animator.StringToHash("Silbon_Caminando_Loop")))
                animatorSilbon.Play("Silbon_Caminando_Loop", 0, 0f);

            animatorSilbon.Update(0f);
            Debug.Log($"[Silbon] Acechando lateralmente de A a B ({(puntoFin - puntoInicio).magnitude:F1}m en {tiempoVisible:F1}s).");
        }
        else
        {
            Debug.LogWarning("[Silbon] No se encontró componente Animator en silbonEnemigo.");
        }

        // Desplazamiento lateral fluido de Punto A a Punto B durante tiempoVisible
        float tiempoPasado = 0f;
        while (tiempoPasado < tiempoVisible)
        {
            tiempoPasado += Time.deltaTime;
            float factor = Mathf.Clamp01(tiempoPasado / tiempoVisible);

            if (moverSilbonLateralmente)
            {
                Vector3 posActual = Vector3.Lerp(puntoInicio, puntoFin, factor);

                // Pegar al suelo suavemente siguiendo el relieve del terreno
                if (Physics.Raycast(posActual + Vector3.up * 5f, Vector3.down, out RaycastHit hitSuelo, 10f, ~0, QueryTriggerInteraction.Ignore))
                {
                    posActual.y = hitSuelo.point.y;
                }

                silbonEnemigo.transform.position = posActual;

                // Actualizar la fuente 3D del silbido para que acompañe el gateo del Silbón
                if (audioManager != null)
                    audioManager.ActualizarPosicionSilbato(posActual);
            }

            yield return null;
        }

        // Desvanecer el silbato suavemente hacia el silencio
        if (audioManager != null)
            audioManager.StopSilbatoConFade(0.6f);

        if (!_chaseTriggered && silbonEnemigo != null)
        {
            if (animatorSilbon != null)
                animatorSilbon.SetBool("Acechando", false);
            silbonEnemigo.SetActive(false);
            Debug.Log("[Silbon] Se esfumo entre las sombras tras gatear lateralmente...");
        }
    }

    /// <summary>
    /// Reinicia la pesca, el ambiente, los skyboxes y al Silbon a su estado inicial.
    /// </summary>
    public void ResetProgress()
    {
        currentFishCount = 0;
        _chaseTriggered = false;
        _currentPhase = 1;
        _targetTerrorWeight = 0f;
        _currentTerrorWeight = 0f;

        if (_rutinaVistazo != null)
            StopCoroutine(_rutinaVistazo);

        if (_rutinaClimax != null)
            StopCoroutine(_rutinaClimax);

        if (_rutinaPersecucion != null)
            StopCoroutine(_rutinaPersecucion);

        if (navMeshAgentSilbon != null)
        {
            if (navMeshAgentSilbon.isActiveAndEnabled && navMeshAgentSilbon.isOnNavMesh)
                navMeshAgentSilbon.isStopped = true;
            navMeshAgentSilbon.enabled = false;
        }

        if (animatorSilbon != null)
        {
            animatorSilbon.SetBool("Acechando", false);
            animatorSilbon.SetBool("Corriendo", false);
            animatorSilbon.SetInteger("Estado", 0);
            animatorSilbon.Play("Walk", 0, 0f);
        }

        if (volumeBlink != null)
            volumeBlink.weight = 0f;

        if (skyboxBueno != null)
            skyboxBueno.SetActive(true);

        if (skyboxMalo != null)
            skyboxMalo.SetActive(false);

        if (silbonEnemigo != null)
            silbonEnemigo.SetActive(false);

        AplicarPesosInmediatos(0f);

        if (audioManager != null)
            audioManager.ResetAudio();

        if (playableDirector != null)
        {
            playableDirector.Stop();
            playableDirector.time = 0;
            playableDirector.Evaluate();
            playableDirector.Pause();
        }

        Debug.Log("[Pesca] Progreso y ambiente reiniciados a 0.");
    }
}
