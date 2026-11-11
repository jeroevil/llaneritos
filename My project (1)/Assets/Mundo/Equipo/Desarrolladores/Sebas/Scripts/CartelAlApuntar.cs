using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// Muestra un cartel (por ejemplo la descripcion de un pez colgado) solo mientras el rayo
/// de la mano o del control esta encima del objeto. Funciona igual que las flechas de los
/// dialogos: RayInteractable + ColliderSurface en el mismo objeto.
///
/// El cartel aparece y desaparece con un fundido corto y un leve crecimiento.
/// </summary>
[RequireComponent(typeof(RayInteractable))]
public class CartelAlApuntar : MonoBehaviour
{
    [Tooltip("Cartel que se muestra al apuntar (SpriteRenderer, o cualquier objeto con renderers).")]
    [SerializeField] GameObject cartel;
    [SerializeField] RayInteractable interactable;

    [Header("Aspecto")]
    [SerializeField, Min(0.01f)] float duracionFundido = 0.15f;
    [Tooltip("Escala del cartel cuando esta oculto, relativa a la normal (crece al aparecer).")]
    [SerializeField, Range(0.5f, 1f)] float escalaOculto = 0.85f;
    [Tooltip("Segundos que se mantiene visible despues de que el rayo se va (evita parpadeos).")]
    [SerializeField, Min(0f)] float retardoOcultar = 0.1f;

    [Header("Depuracion")]
    [Tooltip("Muestra el cartel siempre (para revisar la posicion en el editor).")]
    [SerializeField] bool mostrarSiempre = false;

    SpriteRenderer[] sprites;
    Color[] coloresOriginales;
    Vector3 escalaCartel;
    int apuntadores;
    float visible;          // 0 = oculto, 1 = visible
    float tiempoSinRayo;

    public bool Apuntado => apuntadores > 0;

    void Awake()
    {
        if (interactable == null) interactable = GetComponent<RayInteractable>();
        if (cartel == null) return;

        escalaCartel = cartel.transform.localScale;
        sprites = cartel.GetComponentsInChildren<SpriteRenderer>(true);
        coloresOriginales = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) coloresOriginales[i] = sprites[i].color;

        visible = mostrarSiempre ? 1f : 0f;
        Aplicar();
    }

    void OnEnable()
    {
        if (interactable != null) interactable.WhenPointerEventRaised += AlEventoPuntero;
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
            case PointerEventType.Cancel: apuntadores = Mathf.Max(0, apuntadores - 1); break;
        }
    }

    void Update()
    {
        if (cartel == null) return;

        if (Apuntado) tiempoSinRayo = 0f;
        else tiempoSinRayo += Time.deltaTime;

        bool mostrar = mostrarSiempre || Apuntado || tiempoSinRayo < retardoOcultar;
        float objetivo = mostrar ? 1f : 0f;
        if (Mathf.Approximately(visible, objetivo)) return;

        visible = Mathf.MoveTowards(visible, objetivo, Time.deltaTime / duracionFundido);
        Aplicar();
    }

    void Aplicar()
    {
        bool activo = visible > 0.001f;
        if (cartel.activeSelf != activo) cartel.SetActive(activo);
        if (!activo) return;

        float t = visible * visible * (3f - 2f * visible);
        cartel.transform.localScale = escalaCartel * Mathf.Lerp(escalaOculto, 1f, t);
        for (int i = 0; i < sprites.Length; i++)
        {
            Color c = coloresOriginales[i];
            c.a *= t;
            sprites[i].color = c;
        }
    }
}
