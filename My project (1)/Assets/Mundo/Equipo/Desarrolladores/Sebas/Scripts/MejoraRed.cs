using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;

/// <summary>
/// Mejora de la red de pesca por niveles. Cada nivel cambia el material de la red y
/// cuántos peces atrapa por lanzamiento.
///
/// El boton (mismo sistema que FlechaDialogo: RayInteractable + ColliderSurface) solo se ve
/// cuando hay una mejora disponible: el siguiente nivel pide X peces en el barril y aun no se compro.
/// Al presionarlo se sube un nivel y el boton se oculta hasta la siguiente mejora.
///
/// Lee el conteo de FishingSilbonDirector sin modificarlo (igual que FishingProgressBarUI).
/// No busca nada por su cuenta: todas las referencias se asignan en el Inspector.
/// </summary>
public class MejoraRed : MonoBehaviour
{
    [System.Serializable]
    public class NivelRed
    {
        [Tooltip("Peces en el barril necesarios para poder comprar este nivel (el primero es el inicial: 0).")]
        [Min(0)] public int pezRequerido;
        [Tooltip("Material de la red animada en este nivel (red animacion 1/2/3).")]
        public Material material;
        [Min(0)] public int pecesMinimos = 1;
        [Min(0)] public int pecesMaximos = 2;
        [Tooltip("Tope de peces que caben en la red si se lanza varias veces sin vaciarla.")]
        [Min(1)] public int pecesTope = 6;
    }

    [System.Serializable] public class EventoNivel : UnityEvent<int> { }

    [Header("Referencias")]
    [Tooltip("Director de pesca y Silbon de la escena.")]
    [SerializeField] FishingSilbonDirector director;
    [Tooltip("RedPescaController de la red (objeto 'Red Pesca').")]
    [SerializeField] RedPescaController redPesca;
    [Tooltip("SkinnedMeshRenderer de la malla de la red (hijo 'animacion red tirar (1)').")]
    [SerializeField] SkinnedMeshRenderer mallaRed;

    [Header("Boton de Mejora")]
    [Tooltip("RayInteractable del boton.")]
    [SerializeField] RayInteractable interactable;
    [Tooltip("Objeto que se muestra/oculta segun haya mejora disponible (normalmente la raiz del boton).")]
    [SerializeField] GameObject objetoBoton;

    [Header("Boton de Finalizar")]
    [Tooltip("RayInteractable del boton que termina la experiencia. Vacio = no hay boton de finalizar.")]
    [SerializeField] RayInteractable interactableFinalizar;
    [Tooltip("Objeto que se muestra/oculta junto con el boton de mejora (raiz del boton finalizar).")]
    [SerializeField] GameObject objetoBotonFinalizar;
    [Tooltip("OVRScreenFade de la camara. Si esta vacio, se cierra sin fundido.")]
    [SerializeField] OVRScreenFade fundido;
    [SerializeField, Min(0f)] float duracionFundidoFinal = 1f;

    [Header("Teletransporte a la decision")]
    [Tooltip("Boton de teletransporte cuyo destino es 'teletransporte decicion hermanos' (la Cuarta flecha). " +
             "Al aparecer la mejora lleva ahi al jugador; al presionar Continuar lo regresa a donde estaba. Vacio = no se teletransporta.")]
    [SerializeField] PuntoTeletransporte puntoDecision;

    [Header("Objetos que desaparecen")]
    [Tooltip("Se apagan la primera vez que aparece el boton de mejora (el pez del nivel 1, por defecto el tercero), " +
             "justo cuando aparecen los botones Continuar y Finalizar.")]
    [SerializeField] List<GameObject> ocultarAlAparecerBotones = new List<GameObject>();

    [Header("Niveles (el 0 es la red inicial)")]
    [SerializeField] List<NivelRed> niveles = new List<NivelRed>
    {
        new NivelRed { pezRequerido = 0, pecesMinimos = 1, pecesMaximos = 2, pecesTope = 6 },
        new NivelRed { pezRequerido = 3, pecesMinimos = 2, pecesMaximos = 4, pecesTope = 8 },
        new NivelRed { pezRequerido = 7, pecesMinimos = 3, pecesMaximos = 5, pecesTope = 10 },
    };

    [Header("Pruebas en Editor (PC)")]
    [Tooltip("Tecla U: presiona el boton de mejora (solo si esta disponible). Tecla I: escribe el diagnostico en la consola. Tecla F: finaliza (cierra el Play).")]
    [SerializeField] bool habilitarTeclasDebug = true;

    [Header("Eventos")]
    [Tooltip("Se dispara al aplicar una mejora (pasa el indice del nivel nuevo: 1, 2...).")]
    public EventoNivel alMejorar;
    [Tooltip("Se dispara cuando aparece el boton porque hay una mejora disponible.")]
    public UnityEvent alHaberMejoraDisponible;
    [Tooltip("Se dispara al presionar Finalizar, antes de cerrar el juego.")]
    public UnityEvent alFinalizar;

    public int NivelActual { get; private set; }
    public bool HayMejoraDisponible { get; private set; }

    bool mejoraPendiente;
    bool finalizando;
    bool finalizarVisible;

    void Awake()
    {
        if (objetoBoton != null) objetoBoton.SetActive(false);
        if (objetoBotonFinalizar != null) objetoBotonFinalizar.SetActive(false);
    }

    void Start()
    {
        AplicarNivel(0);
    }

    void OnEnable()
    {
        if (interactable != null) interactable.WhenPointerEventRaised += AlEventoPuntero;
        else Debug.LogWarning("[MejoraRed] Falta asignar el RayInteractable del boton en " + name, this);

        if (interactableFinalizar != null) interactableFinalizar.WhenPointerEventRaised += AlEventoPunteroFinalizar;
    }

    void OnDisable()
    {
        if (interactable != null) interactable.WhenPointerEventRaised -= AlEventoPuntero;
        if (interactableFinalizar != null) interactableFinalizar.WhenPointerEventRaised -= AlEventoPunteroFinalizar;
    }

    void AlEventoPunteroFinalizar(PointerEvent evento)
    {
        if (evento.Type == PointerEventType.Select) Finalizar();
    }

    void AlEventoPuntero(PointerEvent evento)
    {
        if (evento.Type == PointerEventType.Select) Mejorar();
    }

    void Update()
    {
        LeerTeclasDebug();
        if (director == null) return;

        // Mostrar el boton solo cuando el siguiente nivel ya se alcanzo con los peces
        int siguiente = NivelActual + 1;
        bool disponible = !mejoraPendiente && siguiente < niveles.Count
                          && director.currentFishCount >= niveles[siguiente].pezRequerido;

        if (disponible != HayMejoraDisponible)
        {
            HayMejoraDisponible = disponible;
            if (objetoBoton != null) objetoBoton.SetActive(disponible);
            if (disponible)
            {
                OcultarObjetos();
                if (puntoDecision != null) puntoDecision.TeletransportarYRecordar();
                Debug.Log($"[MejoraRed] Mejora al nivel {siguiente} disponible (pez {director.currentFishCount}): boton visible. Presionalo para mejorar.", this);
                alHaberMejoraDisponible.Invoke();
            }
        }

        // Finalizar se ofrece junto con la mejora, y se queda si ya no quedan mas mejoras
        bool sinMasMejoras = siguiente >= niveles.Count && !mejoraPendiente;
        ActualizarBotonFinalizar((disponible || sinMasMejoras) && !finalizando);

        // La red no se toca en pleno lanzamiento: se espera a que vuelva a la orilla
        if (mejoraPendiente && RedEnReposo())
        {
            mejoraPendiente = false;
            AplicarNivel(NivelActual + 1);
            alMejorar.Invoke(NivelActual);
        }
    }

    /// <summary>
    /// Sube un nivel si hay mejora disponible. Lo llama el boton; tambien sirve desde UnityEvents.
    /// </summary>
    [ContextMenu("Mejorar (prueba)")]
    public void Mejorar()
    {
        if (!HayMejoraDisponible || mejoraPendiente) return;

        mejoraPendiente = true;
        HayMejoraDisponible = false;
        if (puntoDecision != null) puntoDecision.VolverAlOrigen();
        if (objetoBoton != null) objetoBoton.SetActive(false);
        ActualizarBotonFinalizar(false);
        Debug.Log($"[MejoraRed] Mejora al nivel {NivelActual + 1} solicitada" + (RedEnReposo() ? "." : " (se aplica cuando la red vuelva a la orilla)."), this);
    }

    void OcultarObjetos()
    {
        foreach (var o in ocultarAlAparecerBotones)
            if (o != null) o.SetActive(false);
    }

    void ActualizarBotonFinalizar(bool visible)
    {
        if (visible == finalizarVisible) return;
        finalizarVisible = visible;
        if (objetoBotonFinalizar != null) objetoBotonFinalizar.SetActive(visible);
    }

    /// <summary>
    /// Termina la experiencia: fundido a negro y cierre del juego (en el editor detiene el Play).
    /// Lo llama el boton Finalizar; tambien sirve desde UnityEvents.
    /// </summary>
    [ContextMenu("Finalizar (prueba)")]
    public void Finalizar()
    {
        if (finalizando) return;
        finalizando = true;
        HayMejoraDisponible = false;
        if (objetoBoton != null) objetoBoton.SetActive(false);
        ActualizarBotonFinalizar(false);
        StartCoroutine(RutinaFinalizar());
    }

    IEnumerator RutinaFinalizar()
    {
        Debug.Log("[MejoraRed] Finalizar presionado: cerrando la experiencia.", this);
        alFinalizar.Invoke();

        if (fundido != null)
        {
            float t = 0f;
            while (t < duracionFundidoFinal)
            {
                t += Time.deltaTime;
                fundido.SetExplicitFade(Mathf.Clamp01(t / duracionFundidoFinal));
                yield return null;
            }
            fundido.SetExplicitFade(1f);
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void LeerTeclasDebug()
    {
        if (!habilitarTeclasDebug || !Application.isEditor) return;
#if ENABLE_INPUT_SYSTEM
        var teclado = UnityEngine.InputSystem.Keyboard.current;
        if (teclado == null) return;
        if (teclado.uKey.wasPressedThisFrame) Mejorar();
        if (teclado.iKey.wasPressedThisFrame) Diagnosticar();
        if (teclado.fKey.wasPressedThisFrame) Finalizar();
#else
        if (Input.GetKeyDown(KeyCode.U)) Mejorar();
        if (Input.GetKeyDown(KeyCode.I)) Diagnosticar();
        if (Input.GetKeyDown(KeyCode.F)) Finalizar();
#endif
    }

    /// <summary>
    /// Revisa cada pieza del sistema y escribe en la consola que esta bien y que falla.
    /// </summary>
    [ContextMenu("Diagnosticar")]
    public void Diagnosticar()
    {
        var sb = new System.Text.StringBuilder("[MejoraRed] DIAGNOSTICO\n");
        void Linea(bool ok, string texto) => sb.AppendLine((ok ? "  OK    " : "  FALLA ") + texto);

        Linea(director != null, "Director asignado" + (director != null ? $" (peces: {director.currentFishCount})" : ""));
        Linea(redPesca != null, "RedPescaController asignado" + (redPesca != null ? $" (estado: {redPesca.EstadoActual})" : ""));
        Linea(mallaRed != null, "Malla de la red asignada" + (mallaRed != null ? $" ('{mallaRed.name}', material actual: '{(mallaRed.sharedMaterial != null ? mallaRed.sharedMaterial.name : "ninguno")}', submallas: {mallaRed.sharedMaterials.Length})" : ""));
        if (mallaRed != null) Linea(mallaRed.gameObject.activeInHierarchy && mallaRed.enabled, "Malla de la red visible en la jerarquia");
        Linea(interactable != null, "RayInteractable del boton asignado");
        if (interactable != null) Linea(interactable.isActiveAndEnabled || objetoBoton == null || !objetoBoton.activeSelf, "RayInteractable habilitado cuando el boton esta visible");
        Linea(objetoBoton != null, "Objeto del boton asignado" + (objetoBoton != null ? $" ('{objetoBoton.name}', activo: {objetoBoton.activeSelf}, visible en jerarquia: {objetoBoton.activeInHierarchy})" : ""));
        if (objetoBoton != null && objetoBoton.activeSelf && !objetoBoton.activeInHierarchy)
            Linea(false, "El boton esta activo pero un PADRE esta apagado: '" + PadreApagado(objetoBoton.transform) + "'");
        if (objetoBoton != null && interactable != null)
            Linea(interactable.transform.IsChildOf(objetoBoton.transform), "El RayInteractable esta dentro del objeto del boton");

        Linea(puntoDecision != null, "Punto de teletransporte a la decision asignado" + (puntoDecision != null ? $" ('{puntoDecision.name}')" : " (opcional)"));
        Linea(interactableFinalizar != null, "RayInteractable del boton Finalizar asignado");
        Linea(objetoBotonFinalizar != null, "Objeto del boton Finalizar asignado");
        if (objetoBotonFinalizar != null && interactableFinalizar != null)
            Linea(interactableFinalizar.transform.IsChildOf(objetoBotonFinalizar.transform), "El RayInteractable de Finalizar esta dentro de su objeto");

        for (int i = 0; i < ocultarAlAparecerBotones.Count; i++)
            Linea(ocultarAlAparecerBotones[i] != null, $"Objeto a ocultar {i}: " + (ocultarAlAparecerBotones[i] != null ? ocultarAlAparecerBotones[i].name : "VACIO"));

        Linea(niveles.Count >= 2, $"Niveles configurados: {niveles.Count}");
        for (int i = 0; i < niveles.Count; i++)
        {
            var n = niveles[i];
            Linea(n.material != null, $"Nivel {i}: pez {n.pezRequerido}, peces {n.pecesMinimos}-{n.pecesMaximos} (tope {n.pecesTope}), material '{(n.material != null ? n.material.name : "VACIO")}'");
        }

        sb.AppendLine($"  Estado: nivel actual {NivelActual}, mejora disponible: {HayMejoraDisponible}, mejora pendiente (esperando que la red vuelva): {mejoraPendiente}");
        if (NivelActual + 1 < niveles.Count && director != null)
            sb.AppendLine($"  Siguiente mejora: nivel {NivelActual + 1} a partir del pez {niveles[NivelActual + 1].pezRequerido}. La red SOLO cambia al presionar el boton (o tecla U).");
        Debug.Log(sb.ToString(), this);
    }

    static string PadreApagado(Transform t)
    {
        for (var p = t.parent; p != null; p = p.parent)
            if (!p.gameObject.activeSelf) return p.name;
        return "?";
    }

    /// <summary>Peces requeridos para un nivel (lo usa la barra para sincronizar sus hitos). -1 si no existe.</summary>
    public int PezRequeridoNivel(int indice)
    {
        return indice >= 0 && indice < niveles.Count ? niveles[indice].pezRequerido : -1;
    }

    bool RedEnReposo()
    {
        if (redPesca == null) return true;
        var estado = redPesca.EstadoActual;
        return estado == RedPescaController.Estado.ListaParaRecoger
            || estado == RedPescaController.Estado.EnMano
            || estado == RedPescaController.Estado.EnMesa;
    }

    void AplicarNivel(int indice)
    {
        if (indice < 0 || indice >= niveles.Count) return;
        NivelActual = indice;
        var nivel = niveles[indice];

        if (mallaRed != null && nivel.material != null)
            mallaRed.sharedMaterial = nivel.material;

        if (redPesca != null)
            redPesca.ConfigurarCaptura(nivel.pecesMinimos, nivel.pecesMaximos, nivel.pecesTope);

        Debug.Log($"[MejoraRed] Red en nivel {indice} ({nivel.pecesMinimos}-{nivel.pecesMaximos} peces).", this);
    }
}
