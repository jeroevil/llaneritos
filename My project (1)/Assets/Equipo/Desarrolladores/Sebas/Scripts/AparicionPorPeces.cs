using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Muestra un grupo de objetos cuando el jugador deposita cierta cantidad de peces en el barril
/// (p. ej. los hermanos hablando de mejorar la red y su flecha de teletransporte al cuarto pez).
///
/// Lee el conteo de FishingSilbonDirector sin modificarlo (igual que FishingProgressBarUI).
/// No busca nada por su cuenta: todas las referencias se asignan en el Inspector.
/// Colocar en un objeto aparte, NO dentro de los objetos que va a ocultar.
/// </summary>
public class AparicionPorPeces : MonoBehaviour
{
    [Header("Progresion")]
    [Tooltip("Director de pesca y Silbon de la escena.")]
    [SerializeField] FishingSilbonDirector director;

    [Tooltip("Cantidad de peces en el barril con la que aparecen los objetos.")]
    [SerializeField, Min(1)] int pezRequerido = 4;

    [Header("Objetos")]
    [Tooltip("Objetos que se activan al llegar al pez requerido.")]
    [SerializeField] List<GameObject> objetosAMostrar = new List<GameObject>();

    [Tooltip("Ocultar los objetos al iniciar la escena. Desactivalo si prefieres dejarlos apagados a mano.")]
    [SerializeField] bool ocultarAlIniciar = true;

    [Header("Eventos")]
    public UnityEvent alAparecer;

    public bool YaAparecieron { get; private set; }

    void Awake()
    {
        // En Awake para que no se vean ni un frame
        if (ocultarAlIniciar) CambiarVisibilidad(false);
    }

    void OnEnable()
    {
        if (director == null)
            Debug.LogWarning("[Aparicion] Falta asignar el FishingSilbonDirector en " + name, this);
    }

    void Update()
    {
        if (YaAparecieron || director == null) return;

        // >= y no == por si entran varios peces de golpe (3 -> 5)
        if (director.currentFishCount >= pezRequerido)
            Mostrar();
    }

    /// <summary>
    /// Activa los objetos una sola vez. No se vuelve a llamar despues para no reactivar
    /// paneles que se ocultan solos (DialogueController al terminar sus dialogos).
    /// </summary>
    [ContextMenu("Mostrar ahora (prueba)")]
    public void Mostrar()
    {
        if (YaAparecieron) return;
        YaAparecieron = true;

        CambiarVisibilidad(true);
        alAparecer.Invoke();
        Debug.Log($"[Aparicion] Pez {pezRequerido} alcanzado: mostrando {objetosAMostrar.Count} objetos ({name}).", this);
    }

    void CambiarVisibilidad(bool visible)
    {
        foreach (var objeto in objetosAMostrar)
        {
            if (objeto != null) objeto.SetActive(visible);
        }
    }
}
