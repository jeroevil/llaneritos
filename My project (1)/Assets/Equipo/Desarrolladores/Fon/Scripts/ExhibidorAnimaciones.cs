using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Muestra en bucle todas las animaciones de un Animator, una tras otra, para exhibirlas.
/// Si el controller tiene una sola animación, simplemente la deja en loop.
///
/// Toma los estados de la capa base del controller (en el orden en que vienen) o la lista
/// <see cref="estados"/> si se llena a mano. Mantiene al personaje en su sitio: el movimiento
/// de raíz se descarta para que las de caminar no lo saquen de la exhibición.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ExhibidorAnimaciones : MonoBehaviour
{
    [Tooltip("Estados a mostrar, en orden. Vacío = todos los de la capa base del controller.")]
    [SerializeField] List<string> estados = new List<string>();
    [Tooltip("Veces que se repite cada animación en loop antes de pasar a la siguiente.")]
    [SerializeField, Min(1)] int repeticionesLoop = 2;
    [Tooltip("Segundos de mezcla entre una animación y la siguiente.")]
    [SerializeField, Min(0f)] float mezcla = 0.3f;

    Animator animator;
    readonly List<float> duraciones = new List<float>();
    int indice;
    float tiempoRestante;
    Vector3 posicionInicial;
    Quaternion rotacionInicial;

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        posicionInicial = transform.localPosition;
        rotacionInicial = transform.localRotation;
        CargarEstados();
    }

    void OnEnable()
    {
        indice = 0;
        if (estados.Count > 0) Reproducir(0, 0f);
    }

    void CargarEstados()
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null) return;

        var clips = new Dictionary<string, AnimationClip>();
        foreach (var clip in controller.animationClips) clips[clip.name] = clip;

        if (estados.Count == 0)
        {
            // Sin lista manual: los clips del controller, que tienen el mismo nombre que sus estados.
            foreach (var clip in controller.animationClips)
                if (!estados.Contains(clip.name)) estados.Add(clip.name);
        }

        duraciones.Clear();
        foreach (var estado in estados)
        {
            AnimationClip clip;
            float duracion = clips.TryGetValue(estado, out clip) ? clip.length : 3f;
            if (clip != null && clip.isLooping) duracion *= repeticionesLoop;
            duraciones.Add(Mathf.Max(0.5f, duracion));
        }
    }

    void Reproducir(int i, float tiempoMezcla)
    {
        indice = i;
        tiempoRestante = duraciones[i];
        animator.CrossFadeInFixedTime(estados[i], tiempoMezcla, 0, 0f);
    }

    void Update()
    {
        if (estados.Count <= 1) return; // una sola: el controller ya la deja en loop

        tiempoRestante -= Time.deltaTime;
        if (tiempoRestante <= mezcla) Reproducir((indice + 1) % estados.Count, mezcla);
    }

    void LateUpdate()
    {
        // Algunas animaciones mueven la raíz igual; se queda en su sitio de exhibición.
        transform.localPosition = posicionInicial;
        transform.localRotation = rotacionInicial;
    }
}
