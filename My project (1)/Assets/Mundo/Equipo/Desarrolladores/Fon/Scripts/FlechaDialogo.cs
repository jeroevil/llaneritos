using UnityEngine;
using UnityEngine.Events;
using Oculus.Interaction;

/// <summary>
/// Flecha 3D de los diálogos (anterior / siguiente). Solo avisa que la presionaron con
/// <see cref="alPresionar"/>: la lógica de pasar de diálogo va en otro script.
///
/// Se señala con el rayo de la mano o del control (RayInteractable + ColliderSurface)
/// y se activa al seleccionar (pinza o gatillo). Al señalarla crece y cambia de color.
/// </summary>
[RequireComponent(typeof(RayInteractable))]
public class FlechaDialogo : MonoBehaviour
{
    public enum Direccion { Anterior, Siguiente }

    [SerializeField] Direccion direccion = Direccion.Siguiente;
    [SerializeField] RayInteractable interactable;
    [SerializeField] Renderer visual;

    [Header("Aspecto")]
    [SerializeField] Color colorNormal = new Color(0.96f, 0.96f, 0.94f);
    [SerializeField] Color colorApuntada = new Color(0.98f, 0.78f, 0.25f);
    [SerializeField] Color colorPresionada = new Color(0.95f, 0.45f, 0.12f);
    [SerializeField] float escalaApuntada = 1.15f;
    [Tooltip("Brillo propio, para que se vea aunque la escena esté oscura.")]
    [SerializeField, Range(0f, 1f)] float brillo = 0.45f;

    [Header("Eventos")]
    public UnityEvent alPresionar;

    [Header("Depuración")]
    [Tooltip("Escribe en la consola cada vez que se presiona.")]
    [SerializeField] bool registrarEnConsola = false;

    public Direccion DireccionFlecha { get { return direccion; } }

    static readonly int ColorBase = Shader.PropertyToID("_BaseColor");
    static readonly int ColorEmision = Shader.PropertyToID("_EmissionColor");

    Vector3 escalaInicial;
    MaterialPropertyBlock bloque;
    int apuntadores;
    float escalaActual = 1f;
    float pulso;

    void Awake()
    {
        if (interactable == null) interactable = GetComponent<RayInteractable>();
        if (visual == null) visual = GetComponent<Renderer>();
        escalaInicial = transform.localScale;
        bloque = new MaterialPropertyBlock();
        AplicarColor(colorNormal);
    }

    void OnEnable()
    {
        interactable.WhenPointerEventRaised += AlEventoPuntero;
    }

    void OnDisable()
    {
        interactable.WhenPointerEventRaised -= AlEventoPuntero;
        apuntadores = 0;
    }

    void AlEventoPuntero(PointerEvent evento)
    {
        switch (evento.Type)
        {
            case PointerEventType.Hover: apuntadores++; break;
            case PointerEventType.Unhover: apuntadores = Mathf.Max(0, apuntadores - 1); break;
            case PointerEventType.Select: Presionar(); break;
        }
    }

    [ContextMenu("Presionar (prueba)")]
    public void Presionar()
    {
        pulso = 1f;
        if (registrarEnConsola) Debug.Log("[FlechaDialogo] " + direccion + " presionada: " + name, this);
        alPresionar.Invoke();
    }

    void Update()
    {
        bool apuntada = apuntadores > 0;
        pulso = Mathf.MoveTowards(pulso, 0f, Time.deltaTime * 4f);

        float objetivo = (apuntada ? escalaApuntada : 1f) * (1f - 0.15f * Mathf.Sin(pulso * Mathf.PI));
        escalaActual = Mathf.Lerp(escalaActual, objetivo, 1f - Mathf.Exp(-18f * Time.deltaTime));
        transform.localScale = escalaInicial * escalaActual;

        Color color = apuntada ? colorApuntada : colorNormal;
        AplicarColor(Color.Lerp(color, colorPresionada, pulso));
    }

    void AplicarColor(Color color)
    {
        if (visual == null) return;
        visual.GetPropertyBlock(bloque);
        bloque.SetColor(ColorBase, color);
        bloque.SetColor(ColorEmision, color * brillo);
        visual.SetPropertyBlock(bloque);
    }
}
