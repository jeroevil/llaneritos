using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;

/// <summary>
/// Boton de teletransporte entre zonas. Se señala con el rayo de la mano o del control
/// (mismo sistema que FlechaDialogo: RayInteractable + ColliderSurface) y al seleccionar
/// (pinza o gatillo) lleva al jugador al destino asignado, sin fundido (salto instantaneo).
///
/// No busca nada por su cuenta: todas las referencias se asignan en el Inspector.
/// Solo mueve el rig en XZ y lo gira en Y; la altura de la cabeza la sigue dando el tracking.
/// </summary>
public class PuntoTeletransporte : MonoBehaviour
{
    [Header("Interaccion VR")]
    [Tooltip("RayInteractable de este boton (necesita su ColliderSurface).")]
    [SerializeField] RayInteractable interactable;

    [Header("Destino")]
    [Tooltip("Zona a la que lleva este boton. Su flecha azul (forward) indica hacia donde mirara el jugador.")]
    [SerializeField] Transform destino;

    [Tooltip("Girar al jugador para que mire hacia el forward del destino.")]
    [SerializeField] bool orientarHaciaDestino = true;

    [Tooltip("Poner la Y del rig (nivel del suelo) igual a la Y del destino. Solo actívalo si el pivote del destino está a ras del suelo.")]
    [SerializeField] bool usarAlturaDelDestino = false;

    [Header("Jugador")]
    [Tooltip("Raiz que se mueve (OVRCameraRig / objeto con PinchToWalk).")]
    [SerializeField] Transform rig;

    [Tooltip("Camara de los ojos (CenterEyeAnchor). Se usa para dejar la cabeza, no el centro del guardián, sobre el destino.")]
    [SerializeField] Transform cabeza;

    [Tooltip("Opcional: re-centra el limite de caminata de PinchToWalk en la zona nueva.")]
    [SerializeField] PinchToWalk caminata;

    [Header("Aspecto (opcional)")]
    [SerializeField] Renderer visual;
    [SerializeField] Color colorNormal = new Color(0.96f, 0.96f, 0.94f);
    [SerializeField] Color colorApuntado = new Color(0.98f, 0.78f, 0.25f);
    [SerializeField] float escalaApuntado = 1.15f;
    [SerializeField, Range(0f, 1f)] float brillo = 0.45f;

    [Header("Eventos")]
    public UnityEvent alTeletransportar;

    static readonly int ColorBase = Shader.PropertyToID("_BaseColor");
    static readonly int ColorEmision = Shader.PropertyToID("_EmissionColor");

    // Compartido entre todos los botones: evita dos saltos a la vez por doble pellizco
    static bool teletransportando;

    // Pose del rig antes de un teletransporte con recuerdo (ver TeletransportarYRecordar)
    bool hayPosicionGuardada;
    Vector3 posicionGuardada;
    Quaternion rotacionGuardada;

    Vector3 escalaInicial;
    MaterialPropertyBlock bloque;
    int apuntadores;
    float escalaActual = 1f;

    void Awake()
    {
        escalaInicial = transform.localScale;
        bloque = new MaterialPropertyBlock();
        AplicarColor(colorNormal);
    }

    void OnEnable()
    {
        if (interactable != null) interactable.WhenPointerEventRaised += AlEventoPuntero;
        else Debug.LogWarning("[Teletransporte] Falta asignar el RayInteractable en " + name, this);
    }

    void OnDisable()
    {
        if (interactable != null) interactable.WhenPointerEventRaised -= AlEventoPuntero;
        apuntadores = 0;
    }

    void AlEventoPuntero(PointerEvent evento)
    {
        switch (evento.Type)
        {
            case PointerEventType.Hover: apuntadores++; break;
            case PointerEventType.Unhover: apuntadores = Mathf.Max(0, apuntadores - 1); break;
            case PointerEventType.Select: Teletransportar(); break;
        }
    }

    [ContextMenu("Teletransportar (prueba)")]
    public void Teletransportar()
    {
        if (teletransportando) return;
        if (destino == null || rig == null || cabeza == null)
        {
            Debug.LogWarning("[Teletransporte] Faltan referencias (destino, rig o cabeza) en " + name, this);
            return;
        }
        StartCoroutine(RutinaTeletransporte(false));
    }

    /// <summary>
    /// Igual que Teletransportar, pero antes guarda donde estaba el jugador para poder regresarlo
    /// despues con <see cref="VolverAlOrigen"/>.
    /// </summary>
    public void TeletransportarYRecordar()
    {
        if (teletransportando) return;
        if (destino == null || rig == null || cabeza == null)
        {
            Debug.LogWarning("[Teletransporte] Faltan referencias (destino, rig o cabeza) en " + name, this);
            return;
        }
        GuardarPosicion();
        StartCoroutine(RutinaTeletransporte(false));
    }

    /// <summary>
    /// Regresa al jugador a donde estaba cuando se uso TeletransportarYRecordar. Si no hay posicion
    /// guardada no hace nada. Si hay un salto en curso, espera a que termine.
    /// </summary>
    public void VolverAlOrigen()
    {
        if (!hayPosicionGuardada || rig == null) return;
        StartCoroutine(RutinaTeletransporte(true));
    }

    void GuardarPosicion()
    {
        posicionGuardada = rig.position;
        rotacionGuardada = rig.rotation;
        hayPosicionGuardada = true;
    }

    void RestaurarRig()
    {
        rig.SetPositionAndRotation(posicionGuardada, rotacionGuardada);
        hayPosicionGuardada = false;
        Physics.SyncTransforms();
        if (caminata != null) caminata.ReubicarCentro(rig.position);
    }

    IEnumerator RutinaTeletransporte(bool volver)
    {
        if (volver) while (teletransportando) yield return null;
        teletransportando = true;

        // Salto instantaneo, sin fundido: el parpadeo queda solo para el Silbon
        yield return null;

        if (volver) RestaurarRig();
        else MoverRig();
        alTeletransportar.Invoke();

        teletransportando = false;
    }

    void MoverRig()
    {
        // 1. Girar en Y alrededor de la cabeza para mirar hacia el forward del destino
        if (orientarHaciaDestino)
        {
            Vector3 mirada = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up);
            Vector3 objetivo = Vector3.ProjectOnPlane(destino.forward, Vector3.up);
            if (mirada.sqrMagnitude > 0.0001f && objetivo.sqrMagnitude > 0.0001f)
                rig.RotateAround(cabeza.position, Vector3.up, Vector3.SignedAngle(mirada, objetivo, Vector3.up));
        }

        // 2. Desplazar en XZ para que la cabeza quede sobre el destino
        Vector3 desplazamiento = destino.position - cabeza.position;
        desplazamiento.y = 0f;
        Vector3 posicion = rig.position + desplazamiento;
        if (usarAlturaDelDestino) posicion.y = destino.position.y;
        rig.position = posicion;

        // Para que un CharacterController en el rig no devuelva la posicion anterior
        Physics.SyncTransforms();

        if (caminata != null) caminata.ReubicarCentro(rig.position);
    }

    void Update()
    {
        bool apuntado = apuntadores > 0;
        float objetivo = apuntado ? escalaApuntado : 1f;
        escalaActual = Mathf.Lerp(escalaActual, objetivo, 1f - Mathf.Exp(-18f * Time.deltaTime));
        transform.localScale = escalaInicial * escalaActual;
        AplicarColor(apuntado ? colorApuntado : colorNormal);
    }

    void AplicarColor(Color color)
    {
        if (visual == null) return;
        visual.GetPropertyBlock(bloque);
        bloque.SetColor(ColorBase, color);
        bloque.SetColor(ColorEmision, color * brillo);
        visual.SetPropertyBlock(bloque);
    }

    void OnDrawGizmosSelected()
    {
        if (destino == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, destino.position);
        Gizmos.DrawWireSphere(destino.position, 0.3f);
        Gizmos.DrawRay(destino.position, Vector3.ProjectOnPlane(destino.forward, Vector3.up).normalized);
    }
}
