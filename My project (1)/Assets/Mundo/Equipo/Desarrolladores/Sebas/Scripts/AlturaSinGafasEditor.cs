using UnityEngine;

/// <summary>
/// Solo para pruebas en el Editor SIN gafas puestas.
/// Sin casco, Meta XR no tiene tracking y la camara queda a ras del suelo (se ve "bajo tierra" / gris).
/// Este script busca el suelo con un rayo hacia abajo y coloca los ojos a 'alturaSimulada' sobre el.
/// Con gafas puestas, o en la build del Quest, no hace absolutamente nada.
/// Ponlo en el [BuildingBlock] Camera Rig.
/// </summary>
public class AlturaSinGafasEditor : MonoBehaviour
{
    [Tooltip("Altura de los ojos sobre el suelo cuando juegas sin gafas (metros)")]
    public float alturaSimulada = 1.6f;

    [Tooltip("Desde que altura por encima del rig se lanza el rayo hacia abajo para encontrar el suelo")]
    public float alturaInicioRayo = 30f;

    [Tooltip("Capas que cuentan como suelo")]
    public LayerMask capasSuelo = ~0;

    [Tooltip("El TrackingSpace del Camera Rig. Si se deja vacio se busca solo.")]
    public Transform trackingSpace;

    private float _yLocalOriginal;
    private bool _listo;
    private bool _ultimoEstadoCasco;
    private bool _primerLog = true;

    private void Start()
    {
        // En la build del Quest no hacemos nada
        if (!Application.isEditor)
        {
            enabled = false;
            return;
        }

        if (trackingSpace == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "TrackingSpace") { trackingSpace = t; break; }
            }
        }

        if (trackingSpace == null)
        {
            Debug.LogWarning("[AlturaSinGafasEditor] No se encontro TrackingSpace. Asignalo a mano.");
            enabled = false;
            return;
        }

        _yLocalOriginal = trackingSpace.localPosition.y;
        _listo = true;
    }

    private void LateUpdate()
    {
        if (!_listo) return;

        bool hayCasco = HayCasco();

        if (_primerLog || hayCasco != _ultimoEstadoCasco)
        {
            Debug.Log(hayCasco
                ? "[AlturaSinGafasEditor] Casco detectado: no se modifica la altura."
                : "[AlturaSinGafasEditor] Sin casco: colocando la camara sobre el suelo.");
            _primerLog = false;
            _ultimoEstadoCasco = hayCasco;
        }

        Vector3 local = trackingSpace.localPosition;

        if (hayCasco)
        {
            // Devolver todo a como estaba
            local.y = _yLocalOriginal;
            trackingSpace.localPosition = local;
            return;
        }

        // Buscar el suelo debajo del rig
        float ySuelo;
        if (BuscarSuelo(out ySuelo))
        {
            Vector3 pos = trackingSpace.position;
            pos.y = ySuelo + alturaSimulada;
            trackingSpace.position = pos;
        }
        else
        {
            // Sin suelo detectado: al menos subir la altura fija
            local.y = _yLocalOriginal + alturaSimulada;
            trackingSpace.localPosition = local;
        }
    }

    private bool BuscarSuelo(out float ySuelo)
    {
        ySuelo = 0f;
        Vector3 origen = transform.position + Vector3.up * alturaInicioRayo;

        var hits = Physics.RaycastAll(origen, Vector3.down, alturaInicioRayo * 2f, capasSuelo, QueryTriggerInteraction.Ignore);
        float mejorDistancia = float.MaxValue;
        bool encontrado = false;

        foreach (var h in hits)
        {
            // Ignorar colliders del propio jugador (manos, character controller, etc.)
            if (h.collider.transform.IsChildOf(transform)) continue;

            if (h.distance < mejorDistancia)
            {
                mejorDistancia = h.distance;
                ySuelo = h.point.y;
                encontrado = true;
            }
        }
        return encontrado;
    }

    private bool HayCasco()
    {
        if (!OVRManager.isHmdPresent) return false;

        // Casco conectado (ej. Link) pero sin ponerse: tambien cuenta como "sin gafas"
        if (OVRManager.instance != null && !OVRManager.instance.isUserPresent) return false;

        return true;
    }
}
