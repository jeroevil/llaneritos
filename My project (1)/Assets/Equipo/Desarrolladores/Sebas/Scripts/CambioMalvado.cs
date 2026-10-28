using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Intercambia los objetos normales por su version "malvada" en el climax (pez 10).
/// Cada par tiene un objeto que se desactiva y otro que se activa.
///
/// Por defecto se engancha al evento OnPhase3Chase de FishingSilbonDirector, que se dispara
/// con la pantalla en negro durante el parpadeo: el cambio ocurre "a ojos cerrados", igual que el skybox.
/// No modifica el director ni busca nada por su cuenta: todo se asigna en el Inspector.
/// </summary>
public class CambioMalvado : MonoBehaviour
{
    public enum Momento
    {
        AlParpadeoDelDirector, // Durante el parpadeo a negro del director (recomendado)
        AlLlegarAlPez          // En cuanto el barril llega al pez indicado, sin esperar al parpadeo
    }

    [System.Serializable]
    public class ParMalvado
    {
        [Tooltip("Objeto normal: se DESACTIVA en el climax.")]
        public GameObject normal;
        [Tooltip("Objeto malvado: se ACTIVA en el climax.")]
        public GameObject malvado;
    }

    [Header("Progresion")]
    [Tooltip("Director de pesca y Silbon de la escena.")]
    [SerializeField] FishingSilbonDirector director;

    [Tooltip("Cuando hacer el cambio.")]
    [SerializeField] Momento momento = Momento.AlParpadeoDelDirector;

    [Tooltip("Solo para 'Al Llegar Al Pez'.")]
    [SerializeField, Min(1)] int pezRequerido = 10;

    [Header("Objetos")]
    [SerializeField] List<ParMalvado> pares = new List<ParMalvado>();

    [Tooltip("Al iniciar: normales activos y malvados apagados. Desactivalo si prefieres dejarlos a mano.")]
    [SerializeField] bool aplicarEstadoInicial = true;

    [System.Serializable]
    public class ParPasto
    {
        [Tooltip("Textura de la brocha de pasto normal (la que esta en Paint Details).")]
        public Texture2D normal;
        [Tooltip("Textura que la reemplaza en el climax.")]
        public Texture2D malvado;
    }

    [Header("Pasto del Terrain")]
    [Tooltip("Terrain con el pasto pintado. Vacio = no se toca el pasto.")]
    [SerializeField] Terrain terreno;

    [Tooltip("Cada brocha cuya textura sea 'normal' pasa a usar 'malvado'. Todo el pasto pintado cambia a la vez.")]
    [SerializeField] List<ParPasto> pastos = new List<ParPasto>();

    [Tooltip("Al iniciar, toda brocha que haya quedado con la textura 'malvado' vuelve a 'normal'. " +
             "Repara el asset si el editor se cerro en pleno climax. Desactivalo solo si pintas a proposito con la textura malvada.")]
    [SerializeField] bool repararPastoAlIniciar = true;

    // El TerrainData es un asset: en el editor los cambios en Play quedarian guardados, asi que se restaura al salir.
    // Se guarda la referencia al asset (no al Terrain, que puede destruirse antes que este script al salir de Play)
    // y solo las texturas originales por indice de brocha.
    TerrainData datosTerreno;
    readonly Dictionary<int, Texture2D> texturasOriginales = new Dictionary<int, Texture2D>();

    [Header("Eventos")]
    public UnityEvent alCambiar;

    public bool YaCambio { get; private set; }

    void Awake()
    {
        if (aplicarEstadoInicial) AplicarEstado(false);
        if (terreno != null) datosTerreno = terreno.terrainData;
        if (repararPastoAlIniciar) RepararPasto();
    }

    void OnEnable()
    {
        if (director == null)
        {
            Debug.LogWarning("[CambioMalvado] Falta asignar el FishingSilbonDirector en " + name, this);
            return;
        }
        if (momento == Momento.AlParpadeoDelDirector)
            director.OnPhase3Chase.AddListener(Cambiar);
    }

    void OnDisable()
    {
        if (director != null) director.OnPhase3Chase.RemoveListener(Cambiar);
    }

    void OnDestroy()
    {
        RestaurarPasto();
    }

    void OnApplicationQuit()
    {
        RestaurarPasto();
    }

    void Update()
    {
        if (momento != Momento.AlLlegarAlPez || YaCambio || director == null) return;
        if (director.currentFishCount >= pezRequerido) Cambiar();
    }

    /// <summary>Apaga los normales y enciende los malvados (una sola vez).</summary>
    [ContextMenu("Cambiar a malvado (prueba)")]
    public void Cambiar()
    {
        if (YaCambio) return;
        YaCambio = true;
        AplicarEstado(true);
        CambiarPasto();
        alCambiar.Invoke();
        Debug.Log($"[CambioMalvado] Climax: {pares.Count} objetos cambiados a su version malvada.", this);
    }

    /// <summary>Vuelve al estado normal (util si se reinicia la pesca).</summary>
    [ContextMenu("Restaurar normales (prueba)")]
    public void Restaurar()
    {
        YaCambio = false;
        AplicarEstado(false);
        RestaurarPasto();
    }

    void CambiarPasto()
    {
        if (datosTerreno == null && terreno != null) datosTerreno = terreno.terrainData;
        if (datosTerreno == null || pastos.Count == 0) return;

        var prototipos = datosTerreno.detailPrototypes;
        for (int i = 0; i < prototipos.Length; i++)
        {
            var malvado = Reemplazo(prototipos[i].prototypeTexture, deNormalAMalvado: true);
            if (malvado == null) continue;
            if (!texturasOriginales.ContainsKey(i)) texturasOriginales[i] = prototipos[i].prototypeTexture;
            prototipos[i].prototypeTexture = malvado;
        }

        datosTerreno.detailPrototypes = prototipos;
        if (texturasOriginales.Count == 0)
            Debug.LogWarning("[CambioMalvado] Ninguna brocha de pasto del Terrain usa las texturas 'normal' asignadas.", this);
    }

    void RestaurarPasto()
    {
        if (datosTerreno == null || texturasOriginales.Count == 0) return;

        var prototipos = datosTerreno.detailPrototypes;
        foreach (var original in texturasOriginales)
            if (original.Key < prototipos.Length) prototipos[original.Key].prototypeTexture = original.Value;

        datosTerreno.detailPrototypes = prototipos;
        texturasOriginales.Clear();
    }

    /// <summary>
    /// Devuelve toda brocha que tenga la textura 'malvado' a su 'normal'. Funciona tambien fuera de Play
    /// (clic derecho en el componente) para arreglar el asset si quedo con el pasto malvado.
    /// </summary>
    [ContextMenu("Reparar pasto (volver a bueno)")]
    public void RepararPasto()
    {
        var datos = datosTerreno != null ? datosTerreno : (terreno != null ? terreno.terrainData : null);
        if (datos == null || pastos.Count == 0) return;

        var prototipos = datos.detailPrototypes;
        int reparados = 0;
        foreach (var proto in prototipos)
        {
            var normal = Reemplazo(proto.prototypeTexture, deNormalAMalvado: false);
            if (normal == null) continue;
            proto.prototypeTexture = normal;
            reparados++;
        }
        if (reparados == 0) return;

        datos.detailPrototypes = prototipos;
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(datos);
#endif
        Debug.Log($"[CambioMalvado] Pasto reparado: {reparados} brocha(s) devueltas a la textura normal.", this);
    }

    Texture2D Reemplazo(Texture textura, bool deNormalAMalvado)
    {
        if (textura == null) return null;
        foreach (var par in pastos)
        {
            if (par == null || par.normal == null || par.malvado == null) continue;
            if (deNormalAMalvado && textura == par.normal) return par.malvado;
            if (!deNormalAMalvado && textura == par.malvado) return par.normal;
        }
        return null;
    }

    void AplicarEstado(bool malvado)
    {
        foreach (var par in pares)
        {
            if (par == null) continue;
            if (par.normal != null) par.normal.SetActive(!malvado);
            if (par.malvado != null) par.malvado.SetActive(malvado);
        }
    }
}
