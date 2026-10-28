using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controla la secuencia de dialogos/tutoriales en VR.
/// Conecta las flechas 3D interactables de Fon (FlechaDialogo) para avanzar y retroceder entre tarjetas de dialogo.
/// Al finalizar el ultimo dialogo, puede ocultar el panel y avisar que el jugador ya puede pescar.
/// </summary>
public class DialogueController : MonoBehaviour
{
    [Header("Tarjetas de Dialogo")]
    [Tooltip("Lista ordenada de objetos de dialogo (dialogo 1, dialogo 2, dialogo 3...). Si esta vacia, busca automaticamente en los hijos.")]
    public List<GameObject> listaDialogos = new List<GameObject>();

    [Header("Flechas Interactables VR")]
    [Tooltip("Flecha hacia la izquierda (retroceder)")]
    public FlechaDialogo flechaAnterior;

    [Tooltip("Flecha hacia la derecha (avanzar / completar)")]
    public FlechaDialogo flechaSiguiente;

    [Header("Comportamiento al Finalizar")]
    [Tooltip("Si esta activo, apaga este panel al presionar Siguiente en el ultimo dialogo")]
    public bool ocultarAlCompletar = true;

    [Tooltip("Ocultar la flecha Anterior cuando estas en el primer dialogo")]
    public bool ocultarFlechaAnteriorEnInicio = true;

    [Header("Eventos")]
    [Tooltip("Se dispara cada vez que cambia el dialogo actual (pasa el indice 0, 1, 2...)")]
    public UnityEvent<int> alCambiarDialogo;

    [Tooltip("Se dispara cuando se completa el ultimo dialogo")]
    public UnityEvent alCompletarTodosLosDialogos;

    [Header("Inicio / Activacion")]
    [Tooltip("Si esta activo, los dialogos y flechas inician totalmente desactivados (ocultos) y NO se muestra el primer dialogo en Start. Se activara cuando se llame a ActivarDialogos() o en sincronia con AparicionPorPeces.")]
    public bool iniciarDesactivado = false;

    [Tooltip("Opcional: Componente AparicionPorPeces con el que sincronizar la activacion automatica. Si esta vacio y 'iniciarDesactivado' esta activo, se buscara solo.")]
    public AparicionPorPeces sincronizarConAparicion;

    [Header("Pruebas en Editor (PC)")]
    [Tooltip("Permite presionar Flecha Derecha / D para avanzar y Flecha Izquierda / A para retroceder")]
    public bool habilitarTeclasDebug = true;

    public int IndiceActual { get; private set; } = 0;
    public bool DialogosCompletados { get; private set; } = false;
    public bool EsperandoActivacion => _esperandoActivacion;

    private bool _esperandoActivacion = false;

    private void Awake()
    {
        AutoBuscarReferencias();
        if (iniciarDesactivado)
        {
            _esperandoActivacion = true;
            OcultarTodosLosDialogosYFlechas();
        }
    }

    private void Start()
    {
        AutoBuscarReferencias();
        ConectarFlechas();

        if (iniciarDesactivado)
        {
            _esperandoActivacion = true;
            OcultarTodosLosDialogosYFlechas();

            // Buscar automaticamente si hay un AparicionPorPeces vinculado
            if (sincronizarConAparicion == null)
            {
                var apariciones = FindObjectsByType<AparicionPorPeces>(FindObjectsSortMode.None);
                foreach (var ap in apariciones)
                {
                    if (ap.ObjetosAMostrar.Contains(gameObject))
                    {
                        sincronizarConAparicion = ap;
                        break;
                    }
                    foreach (var diag in listaDialogos)
                    {
                        if (diag != null && ap.ObjetosAMostrar.Contains(diag))
                        {
                            sincronizarConAparicion = ap;
                            break;
                        }
                    }
                    if (sincronizarConAparicion != null) break;
                }

                if (sincronizarConAparicion == null && apariciones.Length == 1)
                {
                    sincronizarConAparicion = apariciones[0];
                }
            }

            if (sincronizarConAparicion != null)
            {
                sincronizarConAparicion.alAparecer.RemoveListener(ActivarDialogos);
                sincronizarConAparicion.alAparecer.AddListener(ActivarDialogos);

                if (sincronizarConAparicion.YaAparecieron)
                {
                    ActivarDialogos();
                }
            }
        }
        else
        {
            MostrarDialogo(0);
        }
    }

    private void AutoBuscarReferencias()
    {
        // 1. Auto-buscar dialogos hijos si la lista esta vacia
        if (listaDialogos.Count == 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var hijo = transform.GetChild(i).gameObject;
                string nombre = hijo.name.ToLower();
                // Ignorar el contenedor de flechas
                if (nombre.Contains("dialogo") && !nombre.Contains("flecha"))
                {
                    listaDialogos.Add(hijo);
                }
            }
            // Ordenar por nombre natural (dialogo 1, dialogo 2, dialogo 3)
            listaDialogos.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        }

        // 2. Auto-buscar flechas si estan vacias
        if (flechaAnterior == null || flechaSiguiente == null)
        {
            var flechas = GetComponentsInChildren<FlechaDialogo>(true);
            foreach (var f in flechas)
            {
                if (f.DireccionFlecha == FlechaDialogo.Direccion.Anterior && flechaAnterior == null)
                    flechaAnterior = f;
                else if (f.DireccionFlecha == FlechaDialogo.Direccion.Siguiente && flechaSiguiente == null)
                    flechaSiguiente = f;
            }
        }
    }

    private void ConectarFlechas()
    {
        if (flechaAnterior != null)
        {
            flechaAnterior.alPresionar.RemoveListener(RetrocederDialogo);
            flechaAnterior.alPresionar.AddListener(RetrocederDialogo);
        }

        if (flechaSiguiente != null)
        {
            flechaSiguiente.alPresionar.RemoveListener(AvanzarDialogo);
            flechaSiguiente.alPresionar.AddListener(AvanzarDialogo);
        }
    }

    private void Update()
    {
        if (_esperandoActivacion)
        {
            if (sincronizarConAparicion != null && sincronizarConAparicion.YaAparecieron)
            {
                ActivarDialogos();
                return;
            }

            if (listaDialogos.Count > 0 && listaDialogos[0] != null && listaDialogos[0].activeSelf)
            {
                ActivarDialogos();
                return;
            }
            return;
        }

        if (!habilitarTeclasDebug || DialogosCompletados) return;

#if ENABLE_INPUT_SYSTEM
        var teclado = Keyboard.current;
        if (teclado != null)
        {
            if (teclado.dKey.wasPressedThisFrame || teclado.rightArrowKey.wasPressedThisFrame || teclado.spaceKey.wasPressedThisFrame)
                AvanzarDialogo();
            if (teclado.aKey.wasPressedThisFrame || teclado.leftArrowKey.wasPressedThisFrame)
                RetrocederDialogo();
        }
#else
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Space))
            AvanzarDialogo();
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            RetrocederDialogo();
#endif
    }

    /// <summary>
    /// Pasa al siguiente dialogo o completa la conversacion si era el ultimo.
    /// </summary>
    public void AvanzarDialogo()
    {
        if (DialogosCompletados || listaDialogos.Count == 0) return;

        if (IndiceActual < listaDialogos.Count - 1)
        {
            MostrarDialogo(IndiceActual + 1);
        }
        else
        {
            // Era el ultimo dialogo
            FinalizarSecuenciaDialogos();
        }
    }

    /// <summary>
    /// Regresa al dialogo anterior.
    /// </summary>
    public void RetrocederDialogo()
    {
        if (DialogosCompletados || listaDialogos.Count == 0) return;

        if (IndiceActual > 0)
        {
            MostrarDialogo(IndiceActual - 1);
        }
    }

    /// <summary>
    /// Muestra un dialogo especifico por indice (0, 1, 2...).
    /// </summary>
    public void MostrarDialogo(int indice)
    {
        if (listaDialogos.Count == 0) return;

        IndiceActual = Mathf.Clamp(indice, 0, listaDialogos.Count - 1);

        for (int i = 0; i < listaDialogos.Count; i++)
        {
            if (listaDialogos[i] != null)
                listaDialogos[i].SetActive(i == IndiceActual);
        }

        // Control de visibilidad de la flecha anterior
        if (flechaAnterior != null)
        {
            bool mostrarAnterior = !ocultarFlechaAnteriorEnInicio || IndiceActual > 0;
            flechaAnterior.gameObject.SetActive(mostrarAnterior);
        }

        alCambiarDialogo?.Invoke(IndiceActual);
        Debug.Log($"[Dialogo] Mostrando tarjeta {IndiceActual + 1} de {listaDialogos.Count}: {listaDialogos[IndiceActual]?.name}");
    }

    private void FinalizarSecuenciaDialogos()
    {
        DialogosCompletados = true;
        Debug.Log("[Dialogo] Todos los dialogos completados con exito.");

        alCompletarTodosLosDialogos?.Invoke();

        if (ocultarAlCompletar)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Oculta todas las tarjetas de dialogo y las flechas.
    /// </summary>
    public void OcultarTodosLosDialogosYFlechas()
    {
        for (int i = 0; i < listaDialogos.Count; i++)
        {
            if (listaDialogos[i] != null)
                listaDialogos[i].SetActive(false);
        }

        if (flechaAnterior != null) flechaAnterior.gameObject.SetActive(false);
        if (flechaSiguiente != null) flechaSiguiente.gameObject.SetActive(false);
    }

    /// <summary>
    /// Activa e inicia la secuencia de dialogos (muestra el primer dialogo y flechas correspondientes).
    /// Puede ser llamado desde eventos de Unity (como AparicionPorPeces.alAparecer) o desde codigo.
    /// </summary>
    [ContextMenu("Activar e Iniciar Dialogos")]
    public void ActivarDialogos()
    {
        _esperandoActivacion = false;
        DialogosCompletados = false;
        gameObject.SetActive(true);
        MostrarDialogo(0);
        Debug.Log($"[Dialogo] '{name}' activado e iniciado en dialogo 1.", this);
    }

    /// <summary>
    /// Alias de ActivarDialogos para compatibilidad.
    /// </summary>
    public void IniciarDialogos() => ActivarDialogos();

    /// <summary>
    /// Reinicia los dialogos al principio (util si se desea reabrir el tutorial).
    /// </summary>
    public void ReiniciarDialogos()
    {
        DialogosCompletados = false;
        gameObject.SetActive(true);
        MostrarDialogo(0);
    }

    private void OnDestroy()
    {
        if (sincronizarConAparicion != null)
        {
            sincronizarConAparicion.alAparecer.RemoveListener(ActivarDialogos);
        }
    }
}
