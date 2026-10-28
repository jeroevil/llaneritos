#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.Media;

/// <summary>
/// Solo editor. Graba en MP4 las pruebas de la red simulando la mano (la esfera clara es la mano).
/// Usa los mismos Agarrar()/Soltar() que el agarre real. Cada prueba se comprueba sola y el
/// resultado (OK / FALLA) sale en el video y en la consola.
///
/// Uso: en Play Mode, agrega este componente a un objeto vacío de la escena Fon_Sebas.
/// Al terminar escribe el video en rutaVideo y sale del Play Mode.
/// </summary>
public class GrabadorPruebasRed : MonoBehaviour
{
    [Tooltip("Carpeta del video; el archivo lleva la fecha y hora para no pisar uno anterior.")]
    [SerializeField] string carpetaVideo = "Temp/Captures";
    [SerializeField] int ancho = 1280;
    [SerializeField] int alto = 720;
    [SerializeField] int fps = 30;

    // Tomas de cámara (posición, punto al que mira)
    static readonly Vector3[] TomaGeneral = { new Vector3(14f, 8f, 22f), new Vector3(5.5f, -1f, 12f) };
    static readonly Vector3[] TomaRoca = { new Vector3(6.5f, 2.2f, 15.5f), new Vector3(3f, -1f, 8.5f) };
    static readonly Vector3[] TomaMesa = { new Vector3(8.9f, 2.3f, 14.6f), new Vector3(8.8f, 0.3f, 18f) };
    static readonly Vector3[] TomaPasto = { new Vector3(4.5f, 2.5f, 19.5f), new Vector3(5.5f, 0f, 13.5f) };
    // Como lo ve el jugador: parado en la roca mirando hacia el agua
    static readonly Vector3[] TomaRegreso = { new Vector3(4.6f, 1.6f, 12.6f), new Vector3(2.9f, -2.6f, 7.6f) };

    // Más rápido que esto entre dos cuadros se ve como un salto en las gafas
    const float VelocidadSalto = 10f;

    RedPescaController red;
    SkinnedMeshRenderer mallaRed;
    Collider zonaLanzamiento, zonaMesa, mesa;
    Transform puntoRecoger, destino;

    Camera cam;
    RenderTexture rt;
    Texture2D cuadro;
    MediaEncoder encoder;
    Text titulo, detalle, marcador, resumen;
    string rutaVideo;
    Transform mano;
    Material matLineas;

    int saltoCuadros = 1;
    int cuadroJuego;
    int cuadrosGrabados;

    LineRenderer rastro;
    readonly List<Vector3> puntosRastro = new List<Vector3>();
    readonly List<string> resultados = new List<string>();

    void Start()
    {
        // Una recarga de scripts a mitad de la grabación corta el video: se bloquea hasta terminar
        UnityEditor.EditorApplication.LockReloadAssemblies();

        red = FindFirstObjectByType<RedPescaController>();
        mallaRed = red.GetComponentInChildren<SkinnedMeshRenderer>(true);
        zonaLanzamiento = Buscar("Red - Zona Lanzamiento").GetComponent<Collider>();
        zonaMesa = Buscar("Red - Zona Mesa").GetComponent<Collider>();
        destino = Buscar("Red - Destino Lanzamiento").transform;
        puntoRecoger = Buscar("Red - Punto Recoger").transform;
        mesa = new UnityEditor.SerializedObject(red).FindProperty("mesa").objectReferenceValue as Collider;

        PrepararCamara();
        PrepararAyudasVisuales();
        StartCoroutine(Pruebas());
    }

    static GameObject Buscar(string nombre)
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == nombre) return t.gameObject;
        throw new System.Exception("[GrabadorPruebasRed] No encuentro '" + nombre + "'");
    }

    // =========================================================================
    // GUION DE PRUEBAS
    // =========================================================================
    IEnumerator Pruebas()
    {
        Time.captureFramerate = fps;
        red.Reiniciar();

        // Presentación
        Toma(TomaGeneral);
        Textos("Pruebas de la red de pesca  ·  escena Fon_Sebas",
               "Simulado en el editor. Esfera clara = la mano.  Naranja = zona de lanzamiento.  Morado = zona de la mesa.  Amarillo = recorrido fijo.");
        yield return Segundos(5f);

        // ---------------------------------------------------------------- M4
        Toma(TomaMesa);
        Textos("M4 · Red VACÍA soltada junto a la mesa", "Sin peces no debe desplegarse: solo se cae.");
        red.Agarrar();
        PonerManoEn(new Vector3(9.95f, 1.1f, 17.3f));
        yield return Segundos(1.2f);
        red.Soltar();
        bool seDesplego = false;
        yield return Durante(3f, () => seDesplego |= red.EstadoActual == RedPescaController.Estado.Desplegando || red.EstadoActual == RedPescaController.Estado.EnMesa);
        Resultado("M4", !seDesplego, "vacía no se despliega (estado final: " + red.EstadoActual + ")");

        red.Reiniciar();
        yield return null;

        // ---------------------------------------------------------------- G1 + T1 + T2
        Toma(TomaRoca);
        Textos("G1 · Suelto la red QUIETA dentro de la zona naranja", "Sin gesto de lanzar: debe salir sola por el recorrido amarillo.");
        yield return Segundos(1f);
        red.Agarrar();
        yield return MoverMano(new Vector3(3.6f, 1.3f, 12.8f), 1.2f, true);
        yield return Segundos(0.7f);
        yield return LanzarYComprobar("G1", new Color(0.2f, 0.6f, 1f), true);

        // ---------------------------------------------------------------- G2
        Toma(TomaRoca);
        Textos("G2 · Lanzo FUERTE hacia la izquierda desde el borde de la zona", "Debe acomodarse al mismo recorrido (rastro rojo sobre el azul).");
        red.Agarrar();
        yield return MoverMano(new Vector3(4.4f, 1.2f, 12.2f), 1f, true);
        yield return Segundos(0.4f);
        yield return MoverMano(new Vector3(2.1f, 1.2f, 13.3f), 0.15f, false);
        yield return LanzarYComprobar("G2 izquierda", new Color(1f, 0.25f, 0.25f), false);

        Toma(TomaRoca);
        Textos("G2 · Ahora lo tiro HACIA ATRÁS (lejos del agua) desde la derecha", "Aun así debe ir al mismo punto del agua (rastro verde).");
        red.Agarrar();
        yield return MoverMano(new Vector3(4.5f, 1f, 12.3f), 1f, true);
        yield return Segundos(0.4f);
        yield return MoverMano(new Vector3(4.6f, 1.6f, 13.7f), 0.15f, false);
        yield return LanzarYComprobar("G2 atrás", new Color(0.3f, 1f, 0.3f), false);

        // ---------------------------------------------------------------- G3
        Toma(TomaPasto);
        Textos("G3 · Fuera de la zona: la suelto quieta en el pasto", "Comportamiento de siempre: se cae ahí y se queda (no se lanza sola).");
        red.Agarrar();
        yield return MoverMano(new Vector3(6.4f, 1.1f, 14.8f), 2f, true);
        yield return Segundos(0.6f);
        Vector3 soltadaEn = CentroRed();
        red.Soltar();
        bool guiado = false;
        yield return Durante(3f, () => guiado |= red.EstadoActual == RedPescaController.Estado.Extendida);
        Vector3 quedo = CentroRed();
        bool cerca = new Vector2(quedo.x - soltadaEn.x, quedo.z - soltadaEn.z).magnitude < 1f;
        Resultado("G3", !guiado && cerca && red.EstadoActual == RedPescaController.Estado.ListaParaRecoger,
                  "se quedó en el pasto a " + new Vector2(quedo.x - soltadaEn.x, quedo.z - soltadaEn.z).magnitude.ToString("F2") + " m de donde se soltó");

        // ---------------------------------------------------------------- M1 + M3
        int pecesTraidos = red.PecesAtrapados;
        Textos("M1 · La llevo llena (" + pecesTraidos + " peces) a la mesa", "La suelto AL LADO de la mesa, no encima.");
        red.Agarrar();
        yield return Segundos(0.3f);
        Toma(TomaMesa);
        PonerManoEn(new Vector3(10.2f, 1.2f, 16.8f));
        yield return MoverMano(new Vector3(9.95f, 1.1f, 17.0f), 0.8f, true);
        yield return Segundos(0.5f);
        red.Soltar();
        yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.EnMesa, 4f);
        Vector3 desplegadaM1 = CentroRed();
        Resultado("M1", red.EstadoActual == RedPescaController.Estado.EnMesa, "se desplegó sola sobre la mesa");
        Textos("M3 · Los peces quedan sobre el tablero", "Deben quedar apoyados en la mesa, ni flotando ni atravesándola.");
        yield return Segundos(3f);
        ComprobarPecesSobreMesa("M3", pecesTraidos);

        // ---------------------------------------------------------------- M2
        Toma(TomaRoca);
        Textos("M2 · Otra pesca para probar el otro extremo de la mesa", "Acelerado x4.");
        red.Agarrar();
        PonerManoEn(new Vector3(3.6f, 1.3f, 12.8f));
        yield return Segundos(0.3f);
        red.Soltar();
        saltoCuadros = 4;
        yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.ListaParaRecoger, 25f);
        saltoCuadros = 1;
        pecesTraidos = red.PecesAtrapados;

        red.Agarrar();
        Toma(TomaMesa);
        Textos("M2 · La suelto junto al OTRO extremo de la mesa", "Debe desplegarse en ese extremo, no siempre en la misma esquina.");
        PonerManoEn(new Vector3(9.9f, 1.2f, 20.1f));
        yield return MoverMano(new Vector3(9.8f, 1.1f, 19.9f), 0.8f, true);
        yield return Segundos(0.5f);
        red.Soltar();
        yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.EnMesa, 4f);
        Vector3 desplegadaM2 = CentroRed();
        float separacion = Mathf.Abs(desplegadaM2.z - desplegadaM1.z);
        Resultado("M2", red.EstadoActual == RedPescaController.Estado.EnMesa && separacion > 1f,
                  "se desplegó a " + separacion.ToString("F2") + " m del despliegue de M1");
        yield return Segundos(2.5f);

        // ---------------------------------------------------------------- Cierre
        Toma(TomaMesa);
        int ok = resultados.FindAll(r => r.StartsWith("OK")).Count;
        Textos("Resultados: " + ok + " de " + resultados.Count + " pruebas OK", "Simulado en el editor; falta confirmarlo en las gafas.");
        resumen.text = string.Join("\n", resultados);
        resumen.transform.parent.gameObject.SetActive(true);
        marcador.text = "";
        yield return Segundos(7f);

        Terminar();
    }

    /// <summary>Suelta la red y la sigue con la cámara hasta que vuelve a la orilla, comprobando cada fase.</summary>
    IEnumerator LanzarYComprobar(string id, Color colorRastro, bool completo)
    {
        red.Soltar();
        EmpezarRastro(colorRastro);

        bool soloGuiado = red.EstadoActual == RedPescaController.Estado.Volando;
        yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.Extendida, 6f);
        Vector3 cayo = CentroRed();
        float error = new Vector2(cayo.x - destino.position.x, cayo.z - destino.position.z).magnitude;
        Resultado(id, soloGuiado && red.EstadoActual == RedPescaController.Estado.Extendida && error < 0.3f,
                  "cayó a " + error.ToString("F2") + " m del destino");
        DetenerRastro();

        if (!completo)
        {
            Textos(titulo.text, "Vuelve sola a la orilla (acelerado x3).");
            saltoCuadros = 3;
            yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.ListaParaRecoger, 20f);
            saltoCuadros = 1;
            yield break;
        }

        Textos("G1 · Cae en el punto del agua y atrapa peces", "Se queda extendida, se cierra y se hunde un poco.");
        yield return Esperar(() => red.EstadoActual == RedPescaController.Estado.Regresando, 6f);

        // T1: jalones = la velocidad sube y baja (hay pausas), pero sin saltos entre cuadros
        Toma(TomaRegreso);
        Textos("T1 · Regreso a jalones", "Arranca fuerte, frena, pausa... se levanta un poco del agua y se inclina hacia la orilla.");
        float vMax = 0f, vMin = float.MaxValue;
        Vector3 anterior = PuntoBaseRed();
        while (red.EstadoActual == RedPescaController.Estado.Regresando)
        {
            yield return null;
            Vector3 ahora = PuntoBaseRed();
            float v = new Vector2(ahora.x - anterior.x, ahora.z - anterior.z).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            anterior = ahora;
            vMax = Mathf.Max(vMax, v);
            vMin = Mathf.Min(vMin, v);
        }
        Resultado("T1", vMax > 0.5f && vMin < vMax * 0.25f && vMax < VelocidadSalto,
                  "velocidad entre " + vMin.ToString("F2") + " y " + vMax.ToString("F2") + " m/s: con pausas y sin saltos");

        // T2: tirón final sin saltos, y llega derecha a su sitio
        Textos("T2 · Tirón final", "Toma impulso hundiéndose, sale de un tirón y llega con un pequeño rebote.");
        float vMaxSubida = 0f;
        anterior = PuntoBaseRed();
        while (red.EstadoActual == RedPescaController.Estado.Subiendo)
        {
            yield return null;
            Vector3 ahora = PuntoBaseRed();
            vMaxSubida = Mathf.Max(vMaxSubida, (ahora - anterior).magnitude / Mathf.Max(Time.deltaTime, 0.0001f));
            anterior = ahora;
        }
        float inclinacion = Vector3.Angle(red.transform.up, Vector3.up);
        Vector3 llego = CentroRed();
        float distancia = new Vector2(llego.x - puntoRecoger.position.x, llego.z - puntoRecoger.position.z).magnitude;
        Resultado("T2", red.EstadoActual == RedPescaController.Estado.ListaParaRecoger && inclinacion < 2f && distancia < 0.5f && vMaxSubida < VelocidadSalto,
                  "llegó a " + distancia.ToString("F2") + " m del punto, inclinada " + inclinacion.ToString("F1") + "°, máx. " + vMaxSubida.ToString("F1") + " m/s, con " + red.PecesAtrapados + " pez(es)");
        Toma(TomaRoca);
        yield return Segundos(1.5f);
    }

    void ComprobarPecesSobreMesa(string id, int esperados)
    {
        Bounds b = mesa.bounds;
        int sobre = 0, total = 0;
        foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (!rb.name.StartsWith("Pez Atrapado")) continue;
            total++;
            Vector3 p = rb.position;
            if (p.x > b.min.x - 0.05f && p.x < b.max.x + 0.05f && p.z > b.min.z - 0.05f && p.z < b.max.z + 0.05f
                && p.y > b.max.y - 0.05f && p.y < b.max.y + 0.3f) sobre++;
        }
        Resultado(id, total >= esperados && sobre == total, sobre + " de " + total + " peces apoyados sobre el tablero");
    }

    // =========================================================================
    // MANO SIMULADA
    // =========================================================================
    Vector3 CentroRed() { return mallaRed.bounds.center; }

    /// <summary>La base que mueve el script (la caja de la malla se mueve al inclinarse, la base no).</summary>
    Vector3 PuntoBaseRed()
    {
        var metodo = typeof(RedPescaController).GetMethod("PuntoBase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Vector3)metodo.Invoke(red, null);
    }

    Vector3 PuntoMano()
    {
        Bounds b = mallaRed.bounds;
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    void PonerManoEn(Vector3 p)
    {
        red.transform.position += p - PuntoMano();
    }

    IEnumerator MoverMano(Vector3 hasta, float duracion, bool suave)
    {
        Vector3 desde = PuntoMano();
        for (float t = 0f; t < duracion; )
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duracion);
            PonerManoEn(Vector3.Lerp(desde, hasta, suave ? Mathf.SmoothStep(0f, 1f, u) : u));
            yield return null;
        }
    }

    // =========================================================================
    // ESPERAS
    // =========================================================================
    IEnumerator Segundos(float s)
    {
        for (float t = 0f; t < s; t += Time.deltaTime) yield return null;
    }

    IEnumerator Esperar(System.Func<bool> condicion, float maximo)
    {
        for (float t = 0f; t < maximo && !condicion(); t += Time.deltaTime) yield return null;
    }

    IEnumerator Durante(float s, System.Action cadaCuadro)
    {
        for (float t = 0f; t < s; t += Time.deltaTime) { cadaCuadro(); yield return null; }
    }

    void Resultado(string id, bool ok, string detalleResultado)
    {
        string linea = (ok ? "OK      " : "FALLA  ") + id + ": " + detalleResultado;
        resultados.Add(linea);
        marcador.text = linea;
        marcador.color = ok ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f);
        Debug.Log("[GrabadorPruebasRed] " + linea);
    }

    // =========================================================================
    // CÁMARA Y VIDEO
    // =========================================================================
    void PrepararCamara()
    {
        var go = new GameObject("Camara Grabacion");
        cam = go.AddComponent<Camera>();
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.enabled = false;
        cuadro = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);

        var atributos = new VideoTrackAttributes
        {
            frameRate = new MediaRational(fps),
            width = (uint)ancho,
            height = (uint)alto,
            includeAlpha = false,
            bitRateMode = UnityEditor.VideoBitrateMode.High
        };
        System.IO.Directory.CreateDirectory(carpetaVideo);
        rutaVideo = System.IO.Path.GetFullPath(System.IO.Path.Combine(carpetaVideo, "pruebas-red-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4"));
        encoder = new MediaEncoder(rutaVideo, atributos);

        // Textos sobre el video
        var canvasGo = new GameObject("Textos Grabacion");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 0.2f;
        // Los sprites de los peces tienen orden de dibujo alto y se pintaban encima del texto
        canvas.sortingOrder = 32000;
        var escalador = canvasGo.AddComponent<CanvasScaler>();
        escalador.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        escalador.referenceResolution = new Vector2(ancho, alto);

        var fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Panel(canvas.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -150f), Vector2.zero);
        titulo = Texto(canvas.transform, fuente, 34, FontStyle.Bold, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -70f), new Vector2(-30f, -12f));
        detalle = Texto(canvas.transform, fuente, 22, FontStyle.Normal, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -145f), new Vector2(-30f, -72f));
        Panel(canvas.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 56f));
        marcador = Texto(canvas.transform, fuente, 24, FontStyle.Bold, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(30f, 8f), new Vector2(-30f, 50f));

        // Resumen final (oculto hasta el final)
        var panelResumen = Panel(canvas.transform, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.76f), Vector2.zero, Vector2.zero);
        panelResumen.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 1f);
        resumen = Texto(panelResumen.transform, fuente, 24, FontStyle.Bold, Vector2.zero, Vector2.one, new Vector2(30f, 20f), new Vector2(-30f, -20f));
        resumen.alignment = TextAnchor.MiddleLeft;
        resumen.color = new Color(0.55f, 1f, 0.55f);
        resumen.lineSpacing = 1.2f;
        panelResumen.SetActive(false);
    }

    static GameObject Panel(Transform padre, Vector2 anclaMin, Vector2 anclaMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject("Fondo", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(padre, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = anclaMin; r.anchorMax = anclaMax; r.offsetMin = offMin; r.offsetMax = offMax;
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        return go;
    }

    static Text Texto(Transform padre, Font fuente, int tamano, FontStyle estilo, Vector2 anclaMin, Vector2 anclaMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(padre, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = anclaMin; r.anchorMax = anclaMax; r.offsetMin = offMin; r.offsetMax = offMax;
        var t = go.GetComponent<Text>();
        t.font = fuente; t.fontSize = tamano; t.fontStyle = estilo; t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    void Textos(string t, string d)
    {
        titulo.text = t;
        detalle.text = d;
    }

    void Toma(Vector3[] toma)
    {
        cam.transform.position = toma[0];
        cam.transform.LookAt(toma[1]);
    }

    void LateUpdate()
    {
        if (cam == null || encoder == null) return;

        bool enMano = red.EstadoActual == RedPescaController.Estado.EnMano;
        mano.gameObject.SetActive(enMano);
        if (enMano) mano.position = PuntoMano() + Vector3.up * 0.03f;

        if (rastro != null)
        {
            Vector3 p = CentroRed();
            if (puntosRastro.Count == 0 || (puntosRastro[puntosRastro.Count - 1] - p).sqrMagnitude > 0.0025f)
            {
                puntosRastro.Add(p);
                rastro.positionCount = puntosRastro.Count;
                rastro.SetPositions(puntosRastro.ToArray());
            }
        }

        // Grabación: cada cuadro avanza 1/fps de juego; en acelerado se guarda solo 1 de cada N
        cuadroJuego++;
        if (cuadroJuego % saltoCuadros != 0) return;
        cam.Render();
        var anterior = RenderTexture.active;
        RenderTexture.active = rt;
        cuadro.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0, false);
        cuadro.Apply(false);
        RenderTexture.active = anterior;
        encoder.AddFrame(cuadro);
        cuadrosGrabados++;
    }

    // =========================================================================
    // AYUDAS VISUALES (solo existen mientras se graba)
    // =========================================================================
    void PrepararAyudasVisuales()
    {
        matLineas = new Material(Shader.Find("Sprites/Default"));

        var esfera = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(esfera.GetComponent<Collider>());
        esfera.name = "Mano Simulada";
        esfera.transform.localScale = Vector3.one * 0.1f;
        var r = esfera.GetComponent<Renderer>();
        r.material = new Material(matLineas) { color = new Color(1f, 0.85f, 0.7f) };
        mano = esfera.transform;

        DibujarCaja(zonaLanzamiento.bounds, new Color(1f, 0.6f, 0f));
        DibujarCaja(zonaMesa.bounds, new Color(0.7f, 0.35f, 1f));

        var salida = Buscar("Red - Salida Lanzamiento").transform.position;
        var fin = destino.position;
        var arco = Linea(new Color(1f, 0.95f, 0.2f, 0.8f), 0.025f);
        var puntos = new Vector3[41];
        var so = new UnityEditor.SerializedObject(red);
        float altura = so.FindProperty("alturaArcoLanzamiento").floatValue;
        fin.y = so.FindProperty("alturaAgua").floatValue;
        for (int i = 0; i <= 40; i++)
        {
            float t = i / 40f;
            puntos[i] = Vector3.Lerp(salida, fin, t) + Vector3.up * (4f * altura * t * (1f - t));
        }
        arco.positionCount = puntos.Length;
        arco.SetPositions(puntos);
    }

    LineRenderer Linea(Color color, float ancho)
    {
        var go = new GameObject("Linea Grabacion");
        var l = go.AddComponent<LineRenderer>();
        l.sharedMaterial = matLineas;
        l.startColor = l.endColor = color;
        l.startWidth = l.endWidth = ancho;
        l.useWorldSpace = true;
        l.numCapVertices = 2;
        return l;
    }

    void DibujarCaja(Bounds b, Color color)
    {
        Vector3 n = b.min, x = b.max;
        Vector3[] v =
        {
            new Vector3(n.x, n.y, n.z), new Vector3(x.x, n.y, n.z), new Vector3(x.x, n.y, x.z), new Vector3(n.x, n.y, x.z),
            new Vector3(n.x, x.y, n.z), new Vector3(x.x, x.y, n.z), new Vector3(x.x, x.y, x.z), new Vector3(n.x, x.y, x.z),
        };
        int[,] aristas = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        for (int i = 0; i < aristas.GetLength(0); i++)
        {
            var l = Linea(color, 0.03f);
            l.positionCount = 2;
            l.SetPosition(0, v[aristas[i, 0]]);
            l.SetPosition(1, v[aristas[i, 1]]);
        }
    }

    void EmpezarRastro(Color color)
    {
        rastro = Linea(color, 0.06f);
        puntosRastro.Clear();
    }

    void DetenerRastro()
    {
        rastro = null;
    }

    // =========================================================================
    // FIN
    // =========================================================================
    void Terminar()
    {
        Time.captureFramerate = 0;
        encoder.Dispose();
        encoder = null;

        var sb = new StringBuilder("[GrabadorPruebasRed] Video listo: " + rutaVideo + " (" + cuadrosGrabados + " cuadros)\n");
        foreach (var r in resultados) sb.AppendLine("  " + r);
        Debug.Log(sb.ToString());

        UnityEditor.EditorApplication.isPlaying = false;
    }

    void OnDestroy()
    {
        Time.captureFramerate = 0;
        if (encoder != null) encoder.Dispose();
        if (rt != null) rt.Release();
        UnityEditor.EditorApplication.UnlockReloadAssemblies();
    }
}
#endif
