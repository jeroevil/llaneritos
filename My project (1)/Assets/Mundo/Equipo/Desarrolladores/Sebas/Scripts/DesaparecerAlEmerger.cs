using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hace desaparecer objetos cuando el Silbon emerge. Se engancha a OnPhase3Chase de FishingSilbonDirector,
/// que se dispara con la pantalla en negro (antes de abrir los ojos), asi que los objetos se van "a ojos cerrados".
/// Mismo momento que usa CambioMalvado.
///
/// No modifica el director ni busca nada por su cuenta: todo se asigna en el Inspector.
/// Colocar en un objeto aparte, NO dentro de los objetos que va a quitar (p. ej. en Director_juego).
/// </summary>
public class DesaparecerAlEmerger : MonoBehaviour
{
    [Header("Disparador")]
    [Tooltip("Director de pesca y Silbon de la escena.")]
    [SerializeField] FishingSilbonDirector director;

    [Header("Objetos")]
    [Tooltip("Objetos que desaparecen cuando el Silbon emerge.")]
    [SerializeField] List<GameObject> objetos = new List<GameObject>();

    [Tooltip("Destruirlos en vez de apagarlos. Solo si no los necesitas de nuevo en esta escena.")]
    [SerializeField] bool destruir = false;

    [Header("Eventos")]
    public UnityEvent alDesaparecer;

    public bool YaDesaparecieron { get; private set; }

    void OnEnable()
    {
        if (director != null) director.OnPhase3Chase.AddListener(Desaparecer);
        else Debug.LogWarning("[DesaparecerAlEmerger] Falta asignar el FishingSilbonDirector en " + name, this);
    }

    void OnDisable()
    {
        if (director != null) director.OnPhase3Chase.RemoveListener(Desaparecer);
    }

    /// <summary>Quita los objetos (una sola vez). Tambien sirve para probar desde el menu del componente.</summary>
    [ContextMenu("Desaparecer (prueba)")]
    public void Desaparecer()
    {
        if (YaDesaparecieron) return;
        YaDesaparecieron = true;

        foreach (var o in objetos)
        {
            if (o == null) continue;
            if (destruir) Destroy(o);
            else o.SetActive(false);
        }

        alDesaparecer.Invoke();
        Debug.Log($"[DesaparecerAlEmerger] {objetos.Count} objeto(s) quitados al emerger el Silbon.", this);
    }
}
