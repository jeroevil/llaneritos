using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Componente de locomocion continua para Meta XR (Hand Tracking o Mandos).
/// Permite al jugador caminar en linea recta horizontal hacia donde mira manteniendo un pellizco (indice + pulgar).
/// Se detiene suavemente al soltar.
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
    public ManoSeleccionada manoActiva = ManoSeleccionada.AmbasManos;

    [Header("Referencias")]
    [Tooltip("La camara del casco (CenterEyeAnchor). Define la direccion hacia donde se camina.")]
    public Transform camaraOcular;

    [Tooltip("El objeto raiz que se desplaza (por defecto este mismo objeto / Camera Rig)")]
    public Transform rigTransform;

    [Header("Prueba en Editor (PC)")]
    [Tooltip("Permite presionar la tecla W para simular la caminata sin casco puesto")]
    public bool permitirTecladoEnEditor = true;

    [Header("Deteccion de Paredes por Raycast (Desde el Pecho)")]
    [Tooltip("Lanza un rayo desde el pecho hacia el frente para detectar colliders/paredes (como la orilla) y frenar sin tocar la fisica ni la altura")]
    public bool detectarParedesConRaycast = true;

    [Tooltip("Distancia hacia abajo desde los ojos donde se ubica el pecho (habitualmente 0.35m - 0.45m)")]
    [Range(0.1f, 0.8f)]
    public float distanciaPechoOjos = 0.4f;

    [Tooltip("Distancia de seguridad para detenerte antes de tocar la pared (ej. 0.35m)")]
    [Range(0.1f, 1.0f)]
    public float distanciaFrenadoPared = 0.35f;

    [Tooltip("Usa una esfera/cuerpo en vez de una linea delgada para no atravesar esquinas ni colliders bajos")]
    public bool usarRayoGrueso = true;

    [Tooltip("Radio del rayo grueso (ancho del torso)")]
    [Range(0.05f, 0.4f)]
    public float radioTorso = 0.2f;

    [Tooltip("Permite deslizarse suavemente a lo largo de la pared si caminas en diagonal")]
    public bool deslizarPorParedes = true;

    [Tooltip("Capas de colision que bloquean el paso (por defecto todas excepto Triggers)")]
    public LayerMask capasObstaculos = ~0;

    [Header("Delimitador de Zona Opcional")]
    [Tooltip("Opcional: Si quieres tener un perimetro circular de seguridad adicional")]
    public bool limitarAreaCaminata = false;

    public enum ModoDelimitador
    {
        RadioCircular,
        CajaPermitida
    }

    public ModoDelimitador modoDelimitador = ModoDelimitador.RadioCircular;

    [Range(1f, 25f)]
    public float radioMaximo = 5.0f;
    public Transform centroZona;
    public BoxCollider cajaZonaSegura;

    [Header("Visualizacion en Scene View")]
    [Tooltip("Dibuja el rayo del pecho y los impactos en la ventana Scene de Unity")]
    public bool mostrarRaycastEnEscena = true;

    // Estado interno
    private Vector3 _velocidadActual = Vector3.zero;
    private CharacterController _characterController;
    private OVRHand _ovrHandIzquierda;
    private OVRHand _ovrHandDerecha;
    private Vector3 _posicionOrigen;

    // Para debug visual del Raycast
    private Vector3 _debugOrigenPecho;
    private Vector3 _debugDireccionRayo;
    private bool _debugHuboImpacto;
    private Vector3 _debugPuntoImpacto;

    private void Awake()
    {
        AutoBuscarReferencias();
    }

    private void Start()
    {
        AutoBuscarReferencias();
        if (rigTransform != null)
        {
            _posicionOrigen = rigTransform.position;
        }
        else
        {
            _posicionOrigen = transform.position;
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
        // 1. Detectar entrada de caminata (Pellizco o W)
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

        // Aceleracion y frenado suave para no marear en VR
        _velocidadActual = Vector3.MoveTowards(_velocidadActual, velocidadObjetivo, aceleracion * Time.deltaTime);

        // 2. Aplicar desplazamiento horizontal plano
        if (_velocidadActual.sqrMagnitude > 0.0001f && rigTransform != null)
        {
            Vector3 desplazamientoHorizontal = new Vector3(_velocidadActual.x, 0f, _velocidadActual.z) * Time.deltaTime;

            // A) Filtro de Raycast desde el pecho hacia el frente para detectar la pared
            if (detectarParedesConRaycast)
            {
                desplazamientoHorizontal = FiltrarMovimientoConRaycastPecho(desplazamientoHorizontal);
            }

            // B) Filtro opcional de limite perimetral (si esta activado)
            if (limitarAreaCaminata)
            {
                Vector3 posicionDeseada = rigTransform.position + desplazamientoHorizontal;
                Vector3 posicionLimitada = AplicarLimitesDeZona(posicionDeseada);
                desplazamientoHorizontal = posicionLimitada - rigTransform.position;
            }

            desplazamientoHorizontal.y = 0f; // Asegurar que la altura NUNCA se altere

            if (desplazamientoHorizontal.sqrMagnitude > 0.000001f)
            {
                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(desplazamientoHorizontal);
                }
                else
                {
                    rigTransform.position += desplazamientoHorizontal;
                }
            }
        }
        else
        {
            _debugHuboImpacto = false;
        }
    }

    /// <summary>
    /// Lanza un rayo/esfera horizontal desde el pecho en la direccion del movimiento.
    /// Si detecta un collider o pared en frente (como la orilla), detiene el avance justo a la distancia de seguridad.
    /// </summary>
    private Vector3 FiltrarMovimientoConRaycastPecho(Vector3 desplazamientoDeseado)
    {
        if (desplazamientoDeseado.sqrMagnitude < 0.000001f)
            return Vector3.zero;

        // Calcular el punto exacto del pecho (debajo de los ojos del casco VR)
        Vector3 puntoPecho;
        if (camaraOcular != null)
        {
            puntoPecho = camaraOcular.position - Vector3.up * distanciaPechoOjos;
        }
        else
        {
            puntoPecho = rigTransform.position + Vector3.up * 1.2f;
        }

        Vector3 direccion = desplazamientoDeseado.normalized;
        float distanciaPaso = desplazamientoDeseado.magnitude;
        float distanciaChequeo = distanciaPaso + distanciaFrenadoPared;

        _debugOrigenPecho = puntoPecho;
        _debugDireccionRayo = direccion;

        RaycastHit hit;
        bool impacto;

        if (usarRayoGrueso)
        {
            // SphereCast actúa como un cilindro/pecho con volumen real
            impacto = Physics.SphereCast(puntoPecho, radioTorso, direccion, out hit, distanciaChequeo, capasObstaculos, QueryTriggerInteraction.Ignore);
        }
        else
        {
            // Raycast directo delgado
            impacto = Physics.Raycast(puntoPecho, direccion, out hit, distanciaChequeo, capasObstaculos, QueryTriggerInteraction.Ignore);
        }

        _debugHuboImpacto = impacto;

        if (impacto)
        {
            _debugPuntoImpacto = hit.point;

            // Calcular cuanto podemos avanzar hacia la pared antes de llegar a la distancia de frenado
            float distanciaDisponible = Mathf.Max(0f, hit.distance - distanciaFrenadoPared);
            float avanceHaciaPared = Mathf.Min(distanciaPaso, distanciaDisponible);

            Vector3 pasoRestringido = direccion * avanceHaciaPared;

            // Si queremos deslizarnos a lo largo de la pared (ej. si caminamos en diagonal)
            if (deslizarPorParedes && hit.normal.sqrMagnitude > 0.01f)
            {
                Vector3 normalHorizontal = hit.normal;
                normalHorizontal.y = 0f;
                if (normalHorizontal.sqrMagnitude > 0.01f)
                {
                    normalHorizontal.Normalize();
                    Vector3 deslizamiento = Vector3.ProjectOnPlane(desplazamientoDeseado, normalHorizontal);
                    deslizamiento.y = 0f;

                    // Verificar que no choque inmediatamente con otra pared en la direccion de deslizamiento
                    if (deslizamiento.sqrMagnitude > 0.0001f)
                    {
                        bool chocaDeslizamiento = Physics.Raycast(puntoPecho, deslizamiento.normalized, deslizamiento.magnitude + distanciaFrenadoPared, capasObstaculos, QueryTriggerInteraction.Ignore);
                        if (!chocaDeslizamiento)
                        {
                            return deslizamiento;
                        }
                    }
                }
            }

            return pasoRestringido;
        }

        return desplazamientoDeseado;
    }

    /// <summary>
    /// Limita la posicion en los ejes XZ sin alterar nunca la altura Y del jugador.
    /// </summary>
    private Vector3 AplicarLimitesDeZona(Vector3 posicionObjetivo)
    {
        if (!limitarAreaCaminata) return posicionObjetivo;

        if (modoDelimitador == ModoDelimitador.RadioCircular)
        {
            Vector3 centro = centroZona != null ? centroZona.position : _posicionOrigen;
            Vector2 centro2D = new Vector2(centro.x, centro.z);
            Vector2 pos2D = new Vector2(posicionObjetivo.x, posicionObjetivo.z);
            Vector2 offset = pos2D - centro2D;

            if (offset.magnitude > radioMaximo)
            {
                offset = offset.normalized * radioMaximo;
                return new Vector3(centro2D.x + offset.x, posicionObjetivo.y, centro2D.y + offset.y);
            }
            return posicionObjetivo;
        }
        else if (modoDelimitador == ModoDelimitador.CajaPermitida && cajaZonaSegura != null)
        {
            Bounds b = cajaZonaSegura.bounds;
            float clampedX = Mathf.Clamp(posicionObjetivo.x, b.min.x, b.max.x);
            float clampedZ = Mathf.Clamp(posicionObjetivo.z, b.min.z, b.max.z);
            return new Vector3(clampedX, posicionObjetivo.y, clampedZ);
        }

        return posicionObjetivo;
    }

    private void OnDrawGizmosSelected()
    {
        DibujarGizmosDelimitador();
        DibujarGizmoRayoPecho();
    }

    private void OnDrawGizmos()
    {
        if (mostrarRaycastEnEscena)
        {
            DibujarGizmoRayoPecho();
        }
        if (mostrarRaycastEnEscena && limitarAreaCaminata)
        {
            DibujarGizmosDelimitador();
        }
    }

    private void DibujarGizmoRayoPecho()
    {
        if (!detectarParedesConRaycast) return;

        Vector3 puntoPecho;
        Vector3 direccionFrente;

        if (camaraOcular != null)
        {
            puntoPecho = camaraOcular.position - Vector3.up * distanciaPechoOjos;
            direccionFrente = camaraOcular.forward;
        }
        else if (rigTransform != null)
        {
            puntoPecho = rigTransform.position + Vector3.up * 1.2f;
            direccionFrente = rigTransform.forward;
        }
        else
        {
            puntoPecho = transform.position + Vector3.up * 1.2f;
            direccionFrente = transform.forward;
        }

        direccionFrente.y = 0f;
        if (direccionFrente.sqrMagnitude > 0.001f)
            direccionFrente.Normalize();
        else
            direccionFrente = Vector3.forward;

        // Dibujar origen en el pecho
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(puntoPecho, usarRayoGrueso ? radioTorso : 0.08f);

        // Lanzar comprobacion visual
        float distanciaVista = Mathf.Max(1.5f, distanciaFrenadoPared * 2f);
        RaycastHit hit;
        bool choca;

        if (usarRayoGrueso)
        {
            choca = Physics.SphereCast(puntoPecho, radioTorso, direccionFrente, out hit, distanciaVista, capasObstaculos, QueryTriggerInteraction.Ignore);
        }
        else
        {
            choca = Physics.Raycast(puntoPecho, direccionFrente, out hit, distanciaVista, capasObstaculos, QueryTriggerInteraction.Ignore);
        }

        if (choca)
        {
            // Rayo impactando pared
            Gizmos.color = Color.red;
            Gizmos.DrawLine(puntoPecho, hit.point);
            Gizmos.DrawWireSphere(hit.point, 0.1f);

            // Marca de distancia de frenado
            Vector3 puntoFrenado = hit.point - direccionFrente * distanciaFrenadoPared;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(puntoFrenado - Vector3.up * 0.2f, puntoFrenado + Vector3.up * 0.2f);
        }
        else
        {
            // Rayo libre hacia el frente
            Gizmos.color = Color.green;
            Gizmos.DrawLine(puntoPecho, puntoPecho + direccionFrente * distanciaVista);
        }
    }

    private void DibujarGizmosDelimitador()
    {
        if (!limitarAreaCaminata) return;

        Vector3 centro = centroZona != null ? centroZona.position : (Application.isPlaying ? _posicionOrigen : transform.position);

        if (modoDelimitador == ModoDelimitador.RadioCircular)
        {
            Gizmos.color = new Color(0f, 0.9f, 1f, 0.75f); // Cian brillante
            int segmentos = 48;
            Vector3 prevPunto = centro + new Vector3(Mathf.Cos(0) * radioMaximo, 0, Mathf.Sin(0) * radioMaximo);
            for (int i = 1; i <= segmentos; i++)
            {
                float angulo = i * (Mathf.PI * 2f / segmentos);
                Vector3 nuevoPunto = centro + new Vector3(Mathf.Cos(angulo) * radioMaximo, 0, Mathf.Sin(angulo) * radioMaximo);
                Gizmos.DrawLine(prevPunto, nuevoPunto);
                prevPunto = nuevoPunto;
            }

            // Cruz central de referencia
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawLine(centro - Vector3.right * 0.4f, centro + Vector3.right * 0.4f);
            Gizmos.DrawLine(centro - Vector3.forward * 0.4f, centro + Vector3.forward * 0.4f);
        }
        else if (modoDelimitador == ModoDelimitador.CajaPermitida && cajaZonaSegura != null)
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.8f); // Verde zona segura
            Gizmos.DrawWireCube(cajaZonaSegura.bounds.center, cajaZonaSegura.bounds.size);
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

        // 2. Deteccion de Pellizco de Manos de Meta XR o Boton/Gatillo de Mando
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
