using UnityEngine;

/// <summary>
/// Arranca una conversacion (DialogueController) cuando el jugador se acerca a un personaje.
/// Despues los dialogos solo avanzan con las flechas de siguiente/anterior (o las teclas de prueba del editor).
///
/// - Espera a que termine la intro del tutorial.
/// - Espera a que no haya otra conversacion en curso (asi las voces no se pisan).
/// - Arranca una sola vez.
///
/// El DialogueController debe tener 'Iniciar Desactivado' y 'Solo Activacion Manual' activos.
/// Poner este componente en un objeto aparte, NO dentro del grupo de dialogos (que se apaga al terminar).
/// </summary>
public class ActivarDialogoPorCercania : MonoBehaviour
{
    [Header("Conversacion")]
    [SerializeField] DialogueController dialogo;

    [Header("Personaje")]
    [Tooltip("Personaje al que hay que acercarse (se mide la distancia en el plano del suelo, sin contar la altura).")]
    [SerializeField] Transform personaje;
    [Tooltip("Metros a la redonda del personaje a partir de los cuales arranca la conversacion.")]
    [SerializeField, Min(0.5f)] float radio = 3.5f;

    [Header("Jugador")]
    [Tooltip("Camara de los ojos (CenterEyeAnchor). Vacio = la camara principal.")]
    [SerializeField] Transform cabeza;

    [Header("Condiciones")]
    [Tooltip("No arrancar mientras la intro del tutorial siga en curso.")]
    [SerializeField] bool esperarFinDeIntro = true;
    [Tooltip("No arrancar mientras otra conversacion este en curso.")]
    [SerializeField] bool esperarOtrasConversaciones = true;

    bool arranco;
    IntroMoricheTelon intro;
    DialogueController[] otros;

    public bool Arranco => arranco;

    void Start()
    {
        if (esperarFinDeIntro) intro = FindFirstObjectByType<IntroMoricheTelon>();
        if (esperarOtrasConversaciones) otros = FindObjectsByType<DialogueController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (dialogo == null) Debug.LogWarning("[CercaniaDialogo] Falta asignar el DialogueController en " + name, this);
        if (personaje == null) Debug.LogWarning("[CercaniaDialogo] Falta asignar el personaje en " + name, this);
    }

    void Update()
    {
        if (arranco || dialogo == null || personaje == null) return;
        if (intro != null && intro.IntroEnProgreso) return;

        if (cabeza == null && Camera.main != null) cabeza = Camera.main.transform;
        if (cabeza == null) return;

        Vector3 d = cabeza.position - personaje.position;
        d.y = 0f;
        if (d.sqrMagnitude > radio * radio) return;

        if (otros != null)
            foreach (var o in otros)
                if (o != null && o != dialogo && o.EnCurso) return;

        arranco = true;
        dialogo.ActivarDialogos();
        Debug.Log($"[CercaniaDialogo] El jugador se acerco a '{personaje.name}': arranca '{dialogo.name}'.", this);
    }

    void OnDrawGizmosSelected()
    {
        if (personaje == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(personaje.position, radio);
    }
}
