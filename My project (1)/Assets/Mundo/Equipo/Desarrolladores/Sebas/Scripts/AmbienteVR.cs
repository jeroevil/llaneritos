using UnityEngine;

/// <summary>
/// Da profundidad al mundo sin gastar GPU en VR: niebla de aire con el color del horizonte, luz ambiente con degradado
/// (cielo / horizonte / suelo) y colores del agua que siguen al cielo.
///
/// Todo es gratis para el procesador grafico: la niebla se calcula dentro de los shaders que ya existen y la luz ambiente
/// es un degradado (armonicos esfericos), sin texturas ni pasadas extra.
///
/// Hay dos estados, bueno y malvado, y se pasa del uno al otro con un fundido cuando el director cambia el skybox
/// (FishingSilbonDirector.skyboxMalo). Los colores de cielo salen de las texturas reales de cada skybox.
/// </summary>
public class AmbienteVR : MonoBehaviour
{
    [System.Serializable]
    public class Estado
    {
        [Header("Cielo (colores de la textura del skybox)")]
        public Color horizonte = new Color(0.78f, 0.66f, 0.40f);
        [Tooltip("Cielo a media altura (lo que mas se refleja en el agua desde la orilla).")]
        public Color medio = new Color(0.85f, 0.73f, 0.55f);
        public Color zenit = new Color(0.41f, 0.55f, 0.78f);

        [Header("Niebla de aire")]
        [Tooltip("Color de la niebla: el del horizonte, asi lo lejano se funde con el cielo.")]
        public Color niebla = new Color(0.70f, 0.58f, 0.36f);
        [Tooltip("Metros a los que empieza a verse la niebla.")]
        [Min(0f)] public float nieblaInicio = 18f;
        [Tooltip("Metros a los que todo es del color de la niebla.")]
        [Min(1f)] public float nieblaFin = 260f;

        [Header("Luz ambiente (degradado)")]
        public Color ambienteCielo = new Color(0.34f, 0.40f, 0.52f);
        public Color ambienteHorizonte = new Color(0.40f, 0.34f, 0.28f);
        public Color ambienteSuelo = new Color(0.20f, 0.15f, 0.10f);

        [Header("Agua")]
        public Color aguaProfunda = new Color(0.02f, 0.07f, 0.11f);
        public Color aguaSuperficial = new Color(0.07f, 0.22f, 0.24f);
    }

    [Header("Estados")]
    public Estado bueno = new Estado();
    public Estado malvado = new Estado();

    [Header("Referencias")]
    [Tooltip("Director de pesca y Silbon: se mira su skyboxMalo para saber en que mundo estamos. Vacio = se busca solo.")]
    [SerializeField] FishingSilbonDirector director;
    [Tooltip("Renderer del agua (WaterBlock_50m) con el material 'Agua VR'.")]
    [SerializeField] Renderer agua;

    [Header("Transicion")]
    [Tooltip("Segundos que tarda en pasar del mundo bueno al malvado.")]
    [SerializeField, Min(0.1f)] float duracionTransicion = 2f;

    float mezcla;      // 0 = bueno, 1 = malvado
    float ultimaMezcla = -1f;
    Material materialAgua;

    static readonly int IdHorizonte = Shader.PropertyToID("_ColorHorizonte");
    static readonly int IdMedio = Shader.PropertyToID("_ColorMedio");
    static readonly int IdZenit = Shader.PropertyToID("_ColorZenit");
    static readonly int IdProfundo = Shader.PropertyToID("_ColorProfundo");
    static readonly int IdSuperficial = Shader.PropertyToID("_ColorSuperficial");

    void Start()
    {
        if (director == null) director = FindFirstObjectByType<FishingSilbonDirector>();
        // .material (no sharedMaterial): se crea una copia para no modificar el archivo del material al jugar
        if (agua != null) materialAgua = agua.material;

        mezcla = EsMalvado() ? 1f : 0f;
        Aplicar(mezcla);
    }

    void Update()
    {
        float objetivo = EsMalvado() ? 1f : 0f;
        if (Mathf.Approximately(mezcla, objetivo) && Mathf.Approximately(ultimaMezcla, mezcla)) return;

        mezcla = Mathf.MoveTowards(mezcla, objetivo, Time.deltaTime / duracionTransicion);
        Aplicar(mezcla);
    }

    bool EsMalvado() => director != null && director.skyboxMalo != null && director.skyboxMalo.activeInHierarchy;

    [ContextMenu("Aplicar mundo bueno (editor)")]
    void AplicarBuenoEnEditor() => Aplicar(0f);

    [ContextMenu("Aplicar mundo malvado (editor)")]
    void AplicarMalvadoEnEditor() => Aplicar(1f);

    void Aplicar(float k)
    {
        ultimaMezcla = k;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(bueno.niebla, malvado.niebla, k);
        RenderSettings.fogStartDistance = Mathf.Lerp(bueno.nieblaInicio, malvado.nieblaInicio, k);
        RenderSettings.fogEndDistance = Mathf.Lerp(bueno.nieblaFin, malvado.nieblaFin, k);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(bueno.ambienteCielo, malvado.ambienteCielo, k);
        RenderSettings.ambientEquatorColor = Color.Lerp(bueno.ambienteHorizonte, malvado.ambienteHorizonte, k);
        RenderSettings.ambientGroundColor = Color.Lerp(bueno.ambienteSuelo, malvado.ambienteSuelo, k);

        if (materialAgua != null)
        {
            // Los colores se guardan como se ven en el Inspector (gamma); el shader los espera en lineal
            materialAgua.SetColor(IdHorizonte, Color.Lerp(bueno.horizonte, malvado.horizonte, k).linear);
            materialAgua.SetColor(IdMedio, Color.Lerp(bueno.medio, malvado.medio, k).linear);
            materialAgua.SetColor(IdZenit, Color.Lerp(bueno.zenit, malvado.zenit, k).linear);
            materialAgua.SetColor(IdProfundo, Color.Lerp(bueno.aguaProfunda, malvado.aguaProfunda, k).linear);
            materialAgua.SetColor(IdSuperficial, Color.Lerp(bueno.aguaSuperficial, malvado.aguaSuperficial, k).linear);
        }
    }
}
