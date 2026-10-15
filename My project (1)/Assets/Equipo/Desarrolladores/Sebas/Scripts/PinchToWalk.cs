using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Componente de locomocion continua para Meta XR (Hand Tracking o Mandos).
/// Permite al jugador caminar en linea recta horizontal hacia donde mira manteniendo un pellizco (indice + pulgar).
/// 
/// Solucion integral para VR y colisiones:
/// - Mantiene la altura Y exactamente a la altura natural real del visor (sin elevarte, flotar ni ser gigante).
/// - Calibra la capsula del CharacterController para que empiece a 15 cm del suelo (no toca el piso, no te empuja hacia arriba),
///   pero cubre todo tu cuerpo hasta la cabeza para COLISIONAR con la orilla del agua, arboles, rocas y muros.
/// - Sigue dinamicamente la posicion horizontal de la cabeza (CenterEyeAnchor).
/// </summary>
[RequireComponent(typeof(CharacterController))]
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
    public ManoSeleccionada manoActiva = ManoSeleccionada.AmbasManos;

    [Header("Control Estricto de Altura (Eje Y)")]
    [Tooltip("Bloquea la altura Y del Rig al suelo original para que NUNCA cambie tu altura en VR")]
    public bool bloquearAlturaY = true;

    [Tooltip("Solo si bloquearAlturaY esta desactivado: busca el suelo con raycast")]
    public bool adherirAlSuelo = false;

    [Tooltip("Capas consideradas suelo si adherirAlSuelo esta activo")]
    public LayerMask capasSuelo = ~0;

    [Header("Colisionador VR (CharacterController)")]
    [Tooltip("Auto-calibra la capsula para que coincida con tu cuerpo en VR sin tocar el suelo")]
    public bool autoCalibrarCapsulaVR = true;

    [Tooltip("Radio del cuerpo en VR (0.25 - 0.35m es ideal para no atascarse en pasos estrechos)")]
    [Range(0.15f, 0.5f)]
    public float radioColision = 0.3f;

    [Tooltip("Elevacion de la base de la capsula sobre el suelo (15 cm). Evita que el terreno empuje al jugador hacia arriba.")]
    [Range(0.05f, 0.3f)]
    public float elevacionBase = 0.15f;

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
    private float _alturaFijadaY;
    private bool _alturaInicialCapturada = false;

    private void Awake()
    {
        AutoBuscarReferencias();
    }

    private void Start()
    {
        AutoBuscarReferencias();

        if (rigTransform != null)
        {
            _alturaFijadaY = rigTransform.position.y;
            _alturaInicialCapturada = true;
        }

        ConfigurarCharacterControllerVR();
    }

    private void OnValidate()
    {
        if (_characterController == null)
            _characterController = GetComponent<CharacterController>();

        if (_characterController != null && autoCalibrarCapsulaVR)
        {
            _characterController.stepOffset = 0f;
            _characterController.skinWidth = 0.03f;
            _characterController.minMoveDistance = 0f;
            _characterController.radius = radioColision;

            if (!Application.isPlaying)
            {
                float alturaDefault = 1.6f;
                _characterController.height = alturaDefault;
                _characterController.center = new Vector3(0f, elevacionBase + (alturaDefault * 0.5f), 0f);
            }
        }
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

        if (_characterController == null)
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

    private void ConfigurarCharacterControllerVR()
    {
        if (_characterController == null) return;

        // Configuracion vital para VR:
        // stepOffset en 0 para que JAMAS trepe bordes ni se eleve en Y al chocar con colliders horizontales
        _characterController.stepOffset = 0f;
        _characterController.skinWidth = 0.03f;
        _characterController.minMoveDistance = 0f;
        _characterController.slopeLimit = 45f;

        ActualizarCapsulaVR();
    }

    private void ActualizarCapsulaVR()
    {
        if (_characterController == null || !autoCalibrarCapsulaVR) return;

        float alturaOjos = 1.75f;
        Vector3 posLocalOjos = Vector3.up * 1.75f;

        if (camaraOcular != null)
        {
            posLocalOjos = camaraOcular.localPosition;
            if (posLocalOjos.y > 0.4f)
                alturaOjos = posLocalOjos.y;
        }

        // La capsula empieza a 'elevacionBase' (15 cm) del suelo para NUNCA enterrarse en el terreno
        // y llega hasta la altura de los ojos
        float alturaCapsula = Mathf.Max(0.5f, alturaOjos - elevacionBase);

        _characterController.height = alturaCapsula;
        _characterController.radius = radioColision;

        // Centrar la capsula exactamente bajo la cabeza y entre elevacionBase y la cabeza
        _characterController.center = new Vector3(
            posLocalOjos.x,
            elevacionBase + (alturaCapsula * 0.5f),
            posLocalOjos.z
        );
    }

    private void Update()
    {
        // 1. Sincronizar la capsula con la posicion de la cabeza en el espacio fisico
        ActualizarCapsulaVR();

        // 2. Detectar entrada de caminata (Pellizco o W)
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

        // Aceleracion y frenado suave para evitar mareo en VR
        _velocidadActual = Vector3.MoveTowards(_velocidadActual, velocidadObjetivo, aceleracion * Time.deltaTime);

        // 3. Aplicar movimiento
        if (_velocidadActual.sqrMagnitude > 0.0001f)
        {
            Vector3 desplazamientoHorizontal = new Vector3(_velocidadActual.x, 0f, _velocidadActual.z) * Time.deltaTime;

            if (_characterController != null && _characterController.enabled)
            {
                // Mover con el CharacterController para chocar con la orilla, muros y barreras
                _characterController.Move(desplazamientoHorizontal);

                // Bloqueo estricto de altura: no permitir que ninguna colision altere la altura Y del jugador
                if (bloquearAlturaY && _alturaInicialCapturada && rigTransform != null)
                {
                    Vector3 pos = rigTransform.position;
                    pos.y = _alturaFijadaY;
                    rigTransform.position = pos;
                }
            }
            else if (rigTransform != null)
            {
                float alturaY = _alturaInicialCapturada ? _alturaFijadaY : rigTransform.position.y;
                Vector3 nuevaPos = rigTransform.position + desplazamientoHorizontal;

                if (bloquearAlturaY)
                {
                    nuevaPos.y = alturaY;
                }
                else if (adherirAlSuelo)
                {
                    Vector3 origenRayo = new Vector3(nuevaPos.x, alturaY + 0.5f, nuevaPos.z);
                    RaycastHit[] hits = Physics.RaycastAll(origenRayo, Vector3.down, 2.5f, capasSuelo, QueryTriggerInteraction.Ignore);
                    
                    float mejorY = -999f;
                    foreach (var h in hits)
                    {
                        if (h.transform == rigTransform || h.transform.IsChildOf(rigTransform))
                            continue;

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

    private void LateUpdate()
    {
        // Forzar que el Rig Y se mantenga siempre matematicamente en su altura fijada
        if (bloquearAlturaY && _alturaInicialCapturada && rigTransform != null)
        {
            Vector3 pos = rigTransform.position;
            if (Mathf.Abs(pos.y - _alturaFijadaY) > 0.0001f)
            {
                pos.y = _alturaFijadaY;
                rigTransform.position = pos;
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

        // 2. Deteccion de Pellizco de Manos de Meta XR o Gatillo de Mando
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
