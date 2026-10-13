using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Componente de locomocion continua para Meta XR (Hand Tracking sin mandos).
/// Permite al jugador caminar en linea recta horizontal hacia donde mira manteniendo un pellizco (indice + pulgar).
/// Se detiene suavemente al abrir la mano. El eje Y se mantiene bloqueado para no elevar al jugador.
/// </summary>
public class PinchToWalk : MonoBehaviour
{
    public enum ManoSeleccionada
    {
        ManoIzquierda,
        ManoDerecha,
        AmbasManos
    }

    [Header("Configuracion de Caminata")]
    [Tooltip("Velocidad de caminata en metros por segundo (1.8 - 2.5 m/s es ideal para VR)")]
    [Range(0.5f, 6.0f)]
    public float velocidadCaminata = 2.0f;

    [Tooltip("Suavidad de aceleracion y frenado para evitar tirones y mareo cinetico")]
    [Range(2f, 20f)]
    public float aceleracion = 8.0f;

    [Header("Mano Activa")]
    [Tooltip("Cual mano activa el movimiento de caminata")]
    public ManoSeleccionada manoActiva = ManoSeleccionada.ManoIzquierda;

    [Header("Control de Altura (Eje Y)")]
    [Tooltip("Si esta activo, el jugador camina plano en el horizonte sin cambiar su altura Y (Recomendado para evitar salir volando)")]
    public bool bloquearAlturaY = true;

    [Tooltip("Solo si bloquearAlturaY esta desactivado: busca el suelo con raycast suave")]
    public bool adherirAlSuelo = false;

    [Tooltip("Capas consideradas suelo para el raycast")]
    public LayerMask capasSuelo = ~0;

    [Header("Referencias (Se buscan solas si estan vacias)")]
    [Tooltip("La camara del casco (CenterEyeAnchor). Define la direccion hacia donde se camina.")]
    public Transform camaraOcular;

    [Tooltip("El objeto raiz que se desplaza (por defecto este mismo objeto / Camera Rig)")]
    public Transform rigTransform;

    [Header("Prueba en Editor (PC)")]
    [Tooltip("Permite presionar la tecla W para simular la caminata sin casco puesto")]
    public bool permitirTecladoEnEditor = true;

    // Estado interno
    private Vector3 _velocidadActual = Vector3.zero;
    private CharacterController _characterController;
    private OVRHand _ovrHandIzquierda;
    private OVRHand _ovrHandDerecha;

    private void Awake()
    {
        AutoBuscarReferencias();
    }

    private void Start()
    {
        AutoBuscarReferencias();
    }

    private void AutoBuscarReferencias()
    {
        if (rigTransform == null)
            rigTransform = this.transform;

        if (camaraOcular == null)
        {
            var cam = Camera.main;
            if (cam != null)
                camaraOcular = cam.transform;
            else
            {
                var goCam = GameObject.Find("CenterEyeAnchor");
                if (goCam != null) camaraOcular = goCam.transform;
            }
        }

        _characterController = GetComponent<CharacterController>();

        // Localizar componentes OVRHand si existen en la escena
        if (_ovrHandIzquierda == null || _ovrHandDerecha == null)
        {
            var manos = FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var m in manos)
            {
                string nombre = m.gameObject.name.ToLower();
                if (nombre.Contains("left") && _ovrHandIzquierda == null)
                    _ovrHandIzquierda = m;
                else if (nombre.Contains("right") && _ovrHandDerecha == null)
                    _ovrHandDerecha = m;
            }
        }
    }

    private void Update()
    {
        bool quiereAvanzar = DetectarEntradaCaminata();

        Vector3 direccionDeseada = Vector3.zero;

        if (quiereAvanzar && camaraOcular != null)
        {
            // Solo tomar la direccion horizontal XZ (cero inclinacion vertical)
            Vector3 fwd = camaraOcular.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
                direccionDeseada = fwd.normalized;
        }

        Vector3 velocidadObjetivo = direccionDeseada * velocidadCaminata;

        // Aceleracion y frenado suave para no marear
        _velocidadActual = Vector3.MoveTowards(_velocidadActual, velocidadObjetivo, aceleracion * Time.deltaTime);

        // Aplicar movimiento
        if (_velocidadActual.sqrMagnitude > 0.0001f)
        {
            Vector3 desplazamientoHorizontal = new Vector3(_velocidadActual.x, 0f, _velocidadActual.z) * Time.deltaTime;

            if (_characterController != null && _characterController.enabled)
            {
                _characterController.Move(desplazamientoHorizontal);
            }
            else if (rigTransform != null)
            {
                float alturaY = rigTransform.position.y;
                Vector3 nuevaPos = rigTransform.position + desplazamientoHorizontal;

                // Control estricto de altura: no elevarse jamas
                if (bloquearAlturaY)
                {
                    nuevaPos.y = alturaY;
                }
                else if (adherirAlSuelo)
                {
                    // Raycast seguro filtrando los objetos del propio jugador
                    Vector3 origenRayo = new Vector3(nuevaPos.x, alturaY + 0.5f, nuevaPos.z);
                    RaycastHit[] hits = Physics.RaycastAll(origenRayo, Vector3.down, 2.5f, capasSuelo, QueryTriggerInteraction.Ignore);
                    
                    float mejorY = -999f;
                    foreach (var h in hits)
                    {
                        if (h.transform == rigTransform || h.transform.IsChildOf(rigTransform))
                            continue; // Ignorar manos, cabeza y cuerpo propio

                        if (Mathf.Abs(h.point.y - alturaY) < 0.35f && h.point.y > mejorY)
                            mejorY = h.point.y;
                    }

                    if (mejorY > -900f)
                        nuevaPos.y = Mathf.MoveTowards(alturaY, mejorY, 2.0f * Time.deltaTime);
                    else
                        nuevaPos.y = alturaY;
                }

                rigTransform.position = nuevaPos;
            }
        }
    }

    /// <summary>
    /// Consulta si el jugador esta haciendo el gesto de pellizco con la mano activa o presionando W en PC.
    /// </summary>
    private bool DetectarEntradaCaminata()
    {
        // 1. Deteccion de Teclado en Editor para pruebas rapidas sin gafas puestas
        if (permitirTecladoEnEditor && Application.isEditor)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                    return true;
            }
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                return true;
#endif
        }

        // 2. Deteccion de Pellizco de Manos de Meta XR
        bool pellizcoIzq = EsPellizcoManoIzquierda();
        bool pellizcoDer = EsPellizcoManoDerecha();

        switch (manoActiva)
        {
            case ManoSeleccionada.ManoIzquierda:
                return pellizcoIzq;
            case ManoSeleccionada.ManoDerecha:
                return pellizcoDer;
            case ManoSeleccionada.AmbasManos:
                return pellizcoIzq || pellizcoDer;
            default:
                return false;
        }
    }

    private bool EsPellizcoManoIzquierda()
    {
        if (_ovrHandIzquierda != null && _ovrHandIzquierda.IsTracked)
        {
            if (_ovrHandIzquierda.GetFingerIsPinching(OVRHand.HandFinger.Index))
                return true;
        }

        if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LHand) ||
            OVRInput.Get(OVRInput.NearTouch.PrimaryIndexTrigger, OVRInput.Controller.LHand))
        {
            return true;
        }

        return false;
    }

    private bool EsPellizcoManoDerecha()
    {
        if (_ovrHandDerecha != null && _ovrHandDerecha.IsTracked)
        {
            if (_ovrHandDerecha.GetFingerIsPinching(OVRHand.HandFinger.Index))
                return true;
        }

        if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RHand) ||
            OVRInput.Get(OVRInput.NearTouch.PrimaryIndexTrigger, OVRInput.Controller.RHand))
        {
            return true;
        }

        return false;
    }
}
