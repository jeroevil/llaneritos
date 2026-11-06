using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Textos (sprites) y audio de un final. Los usan FinalSaco (final malvado: el Silbon te lleva)
/// y MejoraRed (final bueno: el jugador decide dejar de pescar).
///
/// Los textos se colocan flotando frente al jugador, a la altura de los ojos, y se muestran uno tras otro.
/// El fundido (FundidoParpadeo) oscurece todo, textos incluidos: por eso quien llama abre los ojos
/// antes de mostrar los textos y vuelve a negro despues.
/// </summary>
public class TextosFinal : MonoBehaviour
{
    /// <summary>True mientras algun final esta en curso (evita que dos finales se pisen).</summary>
    public static bool AlgunoEnCurso { get; private set; }

    [Header("Jugador")]
    [Tooltip("Camara de los ojos (CenterEyeAnchor). Los textos se colocan frente a ella.")]
    [SerializeField] Transform cabeza;
    [Tooltip("Metros frente al jugador.")]
    [SerializeField, Min(0.3f)] float distancia = 1.6f;
    [Tooltip("Metros por encima (+) o por debajo (-) de los ojos.")]
    [SerializeField] float altura = 0.1f;

    [Header("Punto de vista (opcional)")]
    [Tooltip("Raiz del jugador (OVRCameraRig). Solo hace falta si se usa un punto de vista.")]
    [SerializeField] Transform rigJugador;
    [Tooltip("Si se asigna, a oscuras el jugador se lleva aqui mirando hacia su flecha azul (forward) antes de ver los textos.")]
    [SerializeField] Transform puntoVista;

    [Header("Peces en el rio (opcional)")]
    [Tooltip("Se ponen a nadar al colocar los textos (final bueno: la vida del rio sigue).")]
    [SerializeField] PecesNadando peces;

    [Header("Textos (en orden)")]
    [Tooltip("Objetos con SpriteRenderer. Se ocultan al iniciar y se muestran uno tras otro.")]
    [SerializeField] List<GameObject> textos = new List<GameObject>();
    [Tooltip("Segundos que se ve cada texto.")]
    [SerializeField, Min(0.1f)] float segundosPorTexto = 6f;
    [Tooltip("Dejar visible el ultimo texto (no ocultarlo al terminar su tiempo).")]
    [SerializeField] bool mantenerUltimo = true;

    [Header("Efectos del texto")]
    [Tooltip("Segundos que tarda cada texto en aparecer (de transparente a opaco).")]
    [SerializeField, Min(0f)] float fadeEntrada = 1.6f;
    [Tooltip("Segundos que tarda en desvanecerse antes de dar paso al siguiente texto (no aplica al ultimo si se mantiene visible).")]
    [SerializeField, Min(0f)] float fadeSalida = 1.1f;
    [Tooltip("Cuanto crece el texto mientras se lee (0.05 = 5%). Un acercamiento muy lento da sensacion de vida.")]
    [SerializeField, Range(0f, 0.2f)] float derivaEscala = 0.05f;
    [Tooltip("Metros que sube el texto mientras se lee.")]
    [SerializeField, Range(0f, 0.3f)] float derivaAltura = 0.04f;

    [Header("Resplandor suave detras de las letras")]
    [Tooltip("Halo claro y difuminado calculado a partir de la propia imagen. Ayuda a leer sobre el agua oscura, sin rectangulo.")]
    [SerializeField] bool resplandor = true;
    [Tooltip("Color del halo cuando las letras son OSCURAS (por ejemplo el cafe del final bueno).")]
    [SerializeField] Color colorResplandor = new Color(1f, 0.96f, 0.86f, 0.9f);
    [Tooltip("Color del halo cuando las letras son CLARAS (por ejemplo el dorado del final malvado). Se detecta solo a partir de la imagen.")]
    [SerializeField] Color colorResplandorTextoClaro = new Color(0.02f, 0.005f, 0.005f, 0.85f);
    [Tooltip("Radio del desenfoque en pixeles de la imagen.")]
    [SerializeField, Range(2, 24)] int radioResplandor = 9;
    [Tooltip("Que tan fuerte brilla el halo (multiplica el desenfoque).")]
    [SerializeField, Range(0.5f, 6f)] float intensidadResplandor = 2.6f;

    [Header("Audio (opcional)")]
    [SerializeField] AudioClip audioFinal;
    [Tooltip("Si esta vacio se crea uno en este objeto (sonido 2D).")]
    [SerializeField] AudioSource fuente;
    [SerializeField, Range(0f, 1f)] float volumen = 1f;

    [Header("Durante el final")]
    [Tooltip("Objetos que se ocultan a oscuras, justo antes de ver los textos (red de pesca, pantallas, flechas...).")]
    [SerializeField] List<GameObject> ocultarDuranteFinal = new List<GameObject>();
    [Tooltip("Componentes que se apagan al empezar el final (PinchToWalk, teletransportes, etc.).")]
    [SerializeField] List<Behaviour> desactivarDuranteFinal = new List<Behaviour>();

    readonly Dictionary<GameObject, SpriteRenderer> brillos = new Dictionary<GameObject, SpriteRenderer>();
    readonly Dictionary<GameObject, Color> coloresBrillo = new Dictionary<GameObject, Color>();
    readonly List<Object> creados = new List<Object>();

    void Awake()
    {
        if (resplandor) foreach (var t in textos) CrearResplandor(t);
        OcultarTextos();
    }

    void OnDestroy()
    {
        AlgunoEnCurso = false;
        foreach (var o in creados) if (o != null) Destroy(o);
    }

    // ---------------------------------------------------------------------------------------------
    // Resplandor: copia difuminada de la propia imagen (solo su canal alfa), en un color claro, detras.
    // ---------------------------------------------------------------------------------------------
    void CrearResplandor(GameObject texto)
    {
        if (texto == null || !texto.TryGetComponent(out SpriteRenderer sr) || sr.sprite == null) return;
        var sprite = CrearSpriteResplandor(sr.sprite, out float luminancia);
        if (sprite == null) return; // textura no legible: se muestra sin resplandor

        // El halo debe ser lo contrario de las letras: oscuro si el texto es claro, claro si es oscuro
        Color colorHalo = luminancia > 0.5f ? colorResplandorTextoClaro : colorResplandor;
        coloresBrillo[texto] = colorHalo;

        var go = new GameObject("resplandor");
        go.transform.SetParent(texto.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        var gr = go.AddComponent<SpriteRenderer>();
        gr.sprite = sprite; gr.color = colorHalo; gr.sortingLayerID = sr.sortingLayerID; gr.sortingOrder = sr.sortingOrder - 1;
        brillos[texto] = gr;
    }

    Sprite CrearSpriteResplandor(Sprite origen, out float luminanciaTexto)
    {
        luminanciaTexto = 0f;
        var tex = origen.texture;
        if (tex == null || !tex.isReadable)
        {
            Debug.LogWarning("[TextosFinal] La imagen '" + (tex != null ? tex.name : "?") + "' no es legible (Read/Write): el texto sale sin resplandor.", this);
            return null;
        }
        int w = Mathf.RoundToInt(origen.rect.width), h = Mathf.RoundToInt(origen.rect.height);
        int x0 = Mathf.RoundToInt(origen.rect.x), y0 = Mathf.RoundToInt(origen.rect.y);
        int p = radioResplandor * 2;               // margen para que el halo no se corte en los bordes
        int W = w + p * 2, H = h + p * 2;

        Color32[] px = tex.GetPixels32();
        var a = new float[W * H];
        double sumaLuz = 0, sumaPeso = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color32 c = px[(y0 + y) * tex.width + (x0 + x)];
                a[(y + p) * W + (x + p)] = c.a / 255f;
                // Luminosidad media de las letras (solo pixeles bien opacos)
                if (c.a > 200) { sumaLuz += (0.299 * c.r + 0.587 * c.g + 0.114 * c.b) / 255.0; sumaPeso += 1; }
            }
        luminanciaTexto = sumaPeso > 0 ? (float)(sumaLuz / sumaPeso) : 0f;

        for (int pasada = 0; pasada < 2; pasada++) DesenfocarCaja(a, W, H, radioResplandor);

        var salida = new Color32[W * H];
        for (int i = 0; i < a.Length; i++)
            salida[i] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(a[i] * intensidadResplandor * 255f), 0, 255));

        var resultado = new Texture2D(W, H, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        resultado.SetPixels32(salida); resultado.Apply(false, true);
        creados.Add(resultado);

        Vector2 pivote = new Vector2((origen.pivot.x + p) / W, (origen.pivot.y + p) / H);
        var sprite = Sprite.Create(resultado, new Rect(0, 0, W, H), pivote, origen.pixelsPerUnit);
        creados.Add(sprite);
        return sprite;
    }

    /// <summary>Desenfoque de caja separable (horizontal y vertical) sobre un arreglo de alfa.</summary>
    static void DesenfocarCaja(float[] a, int w, int h, int radio)
    {
        var tmp = new float[a.Length];
        float n = radio * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            int b = y * w; float suma = 0f;
            for (int x = -radio; x <= radio; x++) suma += a[b + Mathf.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[b + x] = suma / n;
                suma += a[b + Mathf.Clamp(x + radio + 1, 0, w - 1)] - a[b + Mathf.Clamp(x - radio, 0, w - 1)];
            }
        }
        for (int x = 0; x < w; x++)
        {
            float suma = 0f;
            for (int y = -radio; y <= radio; y++) suma += tmp[Mathf.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                a[y * w + x] = suma / n;
                suma += tmp[Mathf.Clamp(y + radio + 1, 0, h - 1) * w + x] - tmp[Mathf.Clamp(y - radio, 0, h - 1) * w + x];
            }
        }
    }

    void PonerAlfa(GameObject texto, float alfa)
    {
        if (texto.TryGetComponent(out SpriteRenderer sr)) { var c = sr.color; c.a = alfa; sr.color = c; }
        if (brillos.TryGetValue(texto, out var br) && br != null)
        {
            var baseC = coloresBrillo.TryGetValue(texto, out var cb) ? cb : colorResplandor;
            baseC.a *= alfa; br.color = baseC;
        }
    }

    static IntroMoricheTelon intro;

    /// <summary>
    /// True mientras la intro del tutorial (video, logo y telon) sigue en curso. Los finales no arrancan en ese momento:
    /// el logo y la pantalla del tutorial van pegados a la camara y taparian los textos.
    /// </summary>
    public static bool IntroEnCurso
    {
        get
        {
            if (intro == null) intro = Object.FindFirstObjectByType<IntroMoricheTelon>();
            return intro != null && intro.IntroEnProgreso;
        }
    }

    /// <summary>Marca el final como en curso y apaga los componentes indicados.</summary>
    public void Empezar()
    {
        AlgunoEnCurso = true;
        foreach (var b in desactivarDuranteFinal) if (b != null) b.enabled = false;
    }

    /// <summary>Pone este objeto (padre de los textos) frente al jugador, mirandolo.</summary>
    public void ColocarFrenteAlJugador()
    {
        foreach (var go in ocultarDuranteFinal) Ocultar(go);
        LlevarJugadorAlPuntoDeVista();
        if (peces != null) peces.Empezar();

        if (cabeza == null)
        {
            Debug.LogWarning("[TextosFinal] Falta asignar la cabeza (CenterEyeAnchor) en " + name + ": los textos quedan donde estan.", this);
            return;
        }
        Vector3 adelante = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up);
        if (adelante.sqrMagnitude < 0.0001f) adelante = Vector3.ProjectOnPlane(cabeza.up, Vector3.up);
        adelante.Normalize();

        transform.position = cabeza.position + adelante * distancia + Vector3.up * altura;
        // El sprite se ve de frente desde -Z: su +Z apunta en la direccion en que mira el jugador.
        transform.rotation = Quaternion.LookRotation(adelante, Vector3.up);
    }

    /// <summary>
    /// Oculta un objeto apagando solo lo visible e interactivo (renderers, colliders, canvas), sin desactivarlo:
    /// el objeto puede ser el que esta corriendo el final (por ejemplo MejoraRed vive en 'Red Pesca').
    /// </summary>
    static void Ocultar(GameObject go)
    {
        if (go == null) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
    }

    /// <summary>Mueve el rig para que la cabeza quede sobre el punto de vista mirando hacia su forward (solo XZ y giro en Y).</summary>
    void LlevarJugadorAlPuntoDeVista()
    {
        if (puntoVista == null) return;
        if (rigJugador == null || cabeza == null)
        {
            Debug.LogWarning("[TextosFinal] Hay punto de vista pero falta el rig o la cabeza en " + name + ": el jugador se queda donde esta.", this);
            return;
        }
        Vector3 mirada = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up);
        Vector3 objetivo = Vector3.ProjectOnPlane(puntoVista.forward, Vector3.up);
        if (mirada.sqrMagnitude > 0.0001f && objetivo.sqrMagnitude > 0.0001f)
            rigJugador.RotateAround(cabeza.position, Vector3.up, Vector3.SignedAngle(mirada, objetivo, Vector3.up));

        Vector3 desplazamiento = puntoVista.position - cabeza.position;
        desplazamiento.y = 0f;
        rigJugador.position += desplazamiento;
        Physics.SyncTransforms();
    }

    public void ReproducirAudio()
    {
        if (audioFinal == null) return;
        if (fuente == null)
        {
            fuente = gameObject.AddComponent<AudioSource>();
            fuente.playOnAwake = false;
            fuente.spatialBlend = 0f;
        }
        fuente.clip = audioFinal;
        fuente.volume = volumen;
        fuente.Play();
    }

    public bool AudioSonando => fuente != null && fuente.isPlaying;

    /// <summary>Corrutina: muestra los textos uno tras otro.</summary>
    public IEnumerator MostrarTextos()
    {
        for (int i = 0; i < textos.Count; i++)
        {
            var texto = textos[i];
            if (texto == null) continue;

            bool ultimo = i == textos.Count - 1;
            bool seDesvanece = !ultimo || !mantenerUltimo;

            // Los dos fundidos nunca pueden sumar mas que el tiempo total del texto
            float total = segundosPorTexto;
            float fin = seDesvanece ? Mathf.Min(fadeSalida, total * 0.5f) : 0f;
            float ini = Mathf.Min(fadeEntrada, total - fin);

            Vector3 escalaBase = texto.transform.localScale;
            Vector3 posBase = texto.transform.localPosition;

            PonerAlfa(texto, 0f);
            texto.SetActive(true);

            float t = 0f;
            while (t < total)
            {
                t += Time.deltaTime;
                float alfa = ini > 0.001f ? Mathf.SmoothStep(0f, 1f, t / ini) : 1f;
                if (seDesvanece && fin > 0.001f) alfa = Mathf.Min(alfa, Mathf.SmoothStep(0f, 1f, (total - t) / fin));
                PonerAlfa(texto, alfa);

                // Deriva muy lenta: se acerca y sube un poco mientras se lee
                float u = Mathf.Clamp01(t / total);
                texto.transform.localScale = escalaBase * (1f + derivaEscala * u);
                texto.transform.localPosition = posBase + Vector3.up * (derivaAltura * u);
                yield return null;
            }

            PonerAlfa(texto, seDesvanece ? 0f : 1f);
            if (seDesvanece)
            {
                texto.SetActive(false);
                texto.transform.localScale = escalaBase;
                texto.transform.localPosition = posBase;
            }
        }
    }

    /// <summary>Corrutina: espera a que termine el audio (si sigue sonando).</summary>
    public IEnumerator EsperarAudio()
    {
        while (AudioSonando) yield return null;
    }

    public void OcultarTextos()
    {
        foreach (var t in textos) if (t != null) { PonerAlfa(t, 0f); t.SetActive(false); }
    }
}
