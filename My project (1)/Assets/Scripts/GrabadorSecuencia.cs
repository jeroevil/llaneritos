using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Graba una partida guiada dejando que el juego dispare sus propios eventos (red, acecho,
/// emergencia del Silbon, persecucion, finales), con camaras que siguen cada momento.
/// Sirve para mostrar el juego a quien no lo puede probar con las gafas.
///
/// Time.captureFramerate fija 30 cuadros por segundo de juego. Cada cuadro se guarda como PNG en
/// carpetaSalida y los momentos importantes quedan en marcas.txt (cuadro y nombre) para cortar.
///
/// Uso (en Play mode): new GameObject("Secuencia").AddComponent&lt;GrabadorSecuencia&gt;().modo = ...
/// </summary>
public class GrabadorSecuencia : MonoBehaviour
{
    public enum Modo { FinalMalo, FinalBueno }

    public Modo modo = Modo.FinalMalo;
    public string carpetaSalida;
    public int ancho = 1024, alto = 576, fps = 30;
    [Tooltip("Tope de seguridad en segundos de juego.")]
    public float duracionMaxima = 200f;

    Camera cam;
    RenderTexture rt;
    Texture2D tex;
    Transform ojos, rig;
    int cuadro;
    bool grabando;
    string rutaMarcas;
    FishingSilbonDirector director;

    // Camara: una funcion de posicion y otra de punto mirado. Si son null, usa los ojos del jugador.
    Func<Vector3> camPos;
    Func<Vector3> camMira;
    float camFov = 55f;
    bool corte = true;            // el proximo cuadro salta directo (sin suavizado)
    Vector3 posSuave;
    Quaternion rotSuave;

    // ------------------------------------------------------------------ utilidades
    static GameObject Buscar(string nombre)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == nombre && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    void Marca(string nombre)
    {
        File.AppendAllText(rutaMarcas, cuadro + " " + nombre + "\n");
        Debug.Log("[Secuencia] cuadro " + cuadro + ": " + nombre);
    }

    IEnumerator Esperar(float segundos)
    {
        float fin = Time.time + segundos;
        while (Time.time < fin) yield return null;
    }

    IEnumerator EsperarQue(Func<bool> condicion, float maximo)
    {
        float fin = Time.time + maximo;
        while (Time.time < fin && !condicion()) yield return null;
    }

    void Camara(Func<Vector3> pos, Func<Vector3> mira, float fov, bool cortar = true)
    {
        camPos = pos; camMira = mira; camFov = fov;
        if (cortar) corte = true;
    }

    void ColocarJugador(Vector3 pos, float yaw)
    {
        var cc = rig.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        rig.position = pos;
        rig.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
    }

    static Vector3 Plano(Vector3 v) { v.y = 0f; return v; }

    // ------------------------------------------------------------------ preparacion
    void Preparar()
    {
        foreach (var n in new[] { "Telon", "Logo", "gestor intro", "PantallaTutorial" })
        {
            var g = Buscar(n);
            if (g != null) g.SetActive(false);
        }
        foreach (var c in FindObjectsByType<ActivarDialogoPorCercania>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            c.enabled = false;

        // Los dialogos ya tienen sus propios clips: aca taparian lo que pasa.
        var dialogos = Buscar("dialogos");
        if (dialogos != null) dialogos.SetActive(false);
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.gameObject.scene.IsValid() && t.parent == null && t.name.StartsWith("Flecha")) t.gameObject.SetActive(false);

        // Peces iniciales sobre la madera de la mesa (el collider de la mesa esta 27 cm arriba).
        var raiz = Buscar("Peces iniciales mesa");
        if (raiz != null)
        {
            var pos = new System.Collections.Generic.Dictionary<string, Vector2>
            {
                { "Pez Cachama (mesa 1)", new Vector2(8.75f, 16.85f) },
                { "Pez Cachama (mesa 4)", new Vector2(8.80f, 17.45f) },
                { "Pez Bagre Amarillo (mesa 2)", new Vector2(8.75f, 17.95f) },
                { "Pez Curito (mesa 3)", new Vector2(8.70f, 18.35f) },
            };
            foreach (Transform ch in raiz.transform)
            {
                var rb = ch.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                Vector2 p;
                if (!pos.TryGetValue(ch.name, out p)) continue;
                ch.position = new Vector3(p.x, ch.position.y, p.y);
                ch.rotation = Quaternion.Euler(0f, ch.eulerAngles.y, 0f);
                var b = new Bounds(ch.position, Vector3.zero);
                foreach (var r in ch.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                ch.position += new Vector3(0f, 0.445f - b.min.y, 0f);
            }
        }

        // La barra se dibuja por encima de los arboles del fondo.
        var barra = Buscar("Barra de progreso canvas");
        if (barra != null)
        {
            var cv = barra.GetComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 5000;
        }

        director = FindFirstObjectByType<FishingSilbonDirector>();
        ojos = Buscar("CenterEyeAnchor").transform;
        rig = Buscar("[BuildingBlock] Camera Rig").transform;

        // Camara de grabacion con el mismo post-proceso que los ojos (parpadeos, terror).
        var go = new GameObject("CamSecuencia");
        cam = go.AddComponent<Camera>();
        var main = ojos.GetComponent<Camera>();
        cam.CopyFrom(main);
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.depth = -100;
        cam.enabled = false;
        cam.nearClipPlane = 0.05f;
        var datosMain = main.GetUniversalAdditionalCameraData();
        var datos = cam.GetUniversalAdditionalCameraData();
        datos.renderPostProcessing = true;
        datos.volumeLayerMask = datosMain.volumeLayerMask;
        datos.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.aspect = (float)ancho / alto;
        tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
    }

    // ------------------------------------------------------------------ grabacion por cuadro
    IEnumerator Grabar()
    {
        while (grabando)
        {
            yield return new WaitForEndOfFrame();
            if (!grabando) break;

            Vector3 p = camPos != null ? camPos() : ojos.position;
            Vector3 m = camMira != null ? camMira() : ojos.position + Plano(ojos.forward).normalized * 5f;
            Quaternion r = Quaternion.LookRotation(m - p, Vector3.up);
            if (corte) { posSuave = p; rotSuave = r; corte = false; }
            else
            {
                float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
                posSuave = Vector3.Lerp(posSuave, p, k);
                rotSuave = Quaternion.Slerp(rotSuave, r, k);
            }
            cam.fieldOfView = camFov;
            cam.transform.SetPositionAndRotation(posSuave, rotSuave);

            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(carpetaSalida, cuadro.ToString("00000") + ".png"), tex.EncodeToPNG());
            cuadro++;

            if (cuadro / (float)fps > duracionMaxima) { Marca("tope"); Terminar(); }
        }
    }

    void Terminar()
    {
        if (!grabando) return;
        grabando = false;
        Time.captureFramerate = 0;
        File.WriteAllText(Path.Combine(carpetaSalida, "listo.txt"), cuadro + " cuadros");
    }

    void OnDestroy()
    {
        // Los finales cierran el Play mode: dejar constancia de hasta donde se grabo.
        if (grabando && !string.IsNullOrEmpty(carpetaSalida))
        {
            try
            {
                File.AppendAllText(rutaMarcas, cuadro + " fin_play\n");
                File.WriteAllText(Path.Combine(carpetaSalida, "listo.txt"), cuadro + " cuadros (cerro el juego)");
            }
            catch { }
        }
        Time.captureFramerate = 0;
    }

    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(carpetaSalida)) carpetaSalida = Path.Combine(Path.GetTempPath(), "sb_secuencia_" + modo);
        Directory.CreateDirectory(carpetaSalida);
        foreach (var f in Directory.GetFiles(carpetaSalida)) File.Delete(f);
        rutaMarcas = Path.Combine(carpetaSalida, "marcas.txt");

        Preparar();
        Time.captureFramerate = fps;
        yield return null;
        yield return null;

        grabando = true;
        StartCoroutine(Grabar());

        if (modo == Modo.FinalMalo) yield return GuionFinalMalo();
        else yield return GuionFinalBueno();

        Marca("fin");
        Terminar();
    }

    // ------------------------------------------------------------------ la red
    IEnumerator LanzarRed()
    {
        var red = FindFirstObjectByType<RedPescaController>();
        if (red == null) yield break;
        Renderer malla = null;
        foreach (var r in red.GetComponentsInChildren<Renderer>()) if (malla == null || r.bounds.size.sqrMagnitude > malla.bounds.size.sqrMagnitude) malla = r;
        Func<Vector3> centroRed = () => malla.bounds.center;
        Func<Vector3> mano = () => new Vector3(malla.bounds.center.x, malla.bounds.max.y, malla.bounds.center.z);

        // Igual que el agarre real: la mano toma la red, la lleva hacia atras y la suelta (lanzamiento guiado).
        Camara(() => new Vector3(6.3f, 2.1f, 15.6f), () => Vector3.Lerp(centroRed(), new Vector3(3f, 0f, 9f), 0.35f), 50f);
        Marca("red_agarrar");
        yield return Esperar(0.8f);
        red.Agarrar();
        Vector3 desde = mano(), hasta = new Vector3(3.6f, 1.3f, 12.8f);
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.2f));
            red.transform.position += Vector3.Lerp(desde, hasta, u) - mano();
            yield return null;
        }
        yield return Esperar(0.5f);
        Marca("red_lanzar");
        red.Soltar();

        // Vuela, cae, se abre en el agua y se cierra con los peces.
        Camara(() => new Vector3(6.3f, 2.1f, 15.6f), centroRed, 50f, false);
        yield return EsperarQue(() => red.EstadoActual == RedPescaController.Estado.Regresando, 10f);

        // Regreso a jalones hasta la orilla.
        Marca("red_regreso");
        Camara(() => new Vector3(4.8f, 1.7f, 13.2f), centroRed, 55f);
        yield return EsperarQue(() => red.EstadoActual == RedPescaController.Estado.Desplegando
                                   || red.EstadoActual == RedPescaController.Estado.EnMesa
                                   || red.EstadoActual == RedPescaController.Estado.ListaParaRecoger, 25f);

        // Los peces quedan en la mesa.
        Marca("red_mesa");
        Camara(() => new Vector3(7.0f, 2.0f, 15.2f), () => new Vector3(8.8f, 0.5f, 17.8f), 50f);
        yield return Esperar(3.5f);
    }

    // ------------------------------------------------------------------ final malo
    IEnumerator GuionFinalMalo()
    {
        // El jugador queda en la orilla, junto al punto donde se recoge la red, mirando al agua.
        ColocarJugador(new Vector3(2.9f, 0.05f, 12.6f), 180f);
        director.OnJugadorAtrapado.AddListener(() => Marca("atrapado"));
        Transform silbon = director.silbonEnemigo != null ? director.silbonEnemigo.transform : null;

        yield return LanzarRed();

        // Las cachamas entran al barril: la barra avanza.
        Vector3 bp = new Vector3(10.15f, 2.64f, 17.8f);
        Camara(() => bp + new Vector3(-7f, 0.3f, 0f), () => bp + new Vector3(0f, -0.25f, 0f), 42f);
        Marca("barra");
        for (int pez = 1; pez <= 3; pez++)
        {
            yield return Esperar(1.2f);
            director.AddFish(1);
            Marca("pez_" + pez);
        }
        yield return Esperar(1.5f);

        // Del pez 4 al 9 el Silbon se asoma cada vez mas cerca y el mundo se va oscureciendo.
        for (int pez = 4; pez <= 9; pez++)
        {
            Camara(() => bp + new Vector3(-7f, 0.3f, 0f), () => bp + new Vector3(0f, -0.25f, 0f), 42f);
            yield return Esperar(0.8f);
            director.AddFish(1);
            Marca("pez_" + pez);
            yield return Esperar(0.4f);
            if (silbon != null && silbon.gameObject.activeInHierarchy)
            {
                // Plano del Silbon asomado, visto desde el lado del jugador.
                Vector3 s = silbon.position;
                Vector3 haciaJug = Plano(ojos.position - s).normalized;
                if (haciaJug.sqrMagnitude < 0.01f) haciaJug = Vector3.back;
                Vector3 lado = Vector3.Cross(Vector3.up, haciaJug) * 1.5f;
                Vector3 c = s + haciaJug * 8f + lado + Vector3.up * 1.5f;
                Marca("acecho_" + pez);
                Camara(() => c, () => silbon.position + Vector3.up * 1.5f, 40f);
                yield return EsperarQue(() => !silbon.gameObject.activeInHierarchy, 6f);
                yield return Esperar(0.6f);
            }
        }

        // Pez 10: el climax. Parpadeo, el mundo cambia y el Silbon emerge del agua.
        Marca("climax");
        Camara(() => new Vector3(3.6f, 1.55f, 13.4f), () => new Vector3(2.17f, 1.4f, 6.7f), 50f);
        director.AddFish(1);
        var campoAgente = typeof(FishingSilbonDirector).GetField("navMeshAgentSilbon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var agente = campoAgente != null ? campoAgente.GetValue(director) as UnityEngine.AI.NavMeshAgent : null;
        yield return EsperarQue(() => agente != null && agente.enabled, 15f);

        // Persecucion: por encima del hombro del jugador, el Silbon corre hacia la camara.
        Marca("persecucion");
        Camara(() =>
        {
            Vector3 hacia = Plano(silbon.position - ojos.position).normalized;
            return ojos.position - hacia * 1.6f + Vector3.Cross(Vector3.up, hacia) * 0.5f + Vector3.up * 0.15f;
        }, () => Vector3.Lerp(silbon.position + Vector3.up * 1.6f, ojos.position, 0.25f), 55f);
        bool atrapado = false;
        director.OnJugadorAtrapado.AddListener(() => atrapado = true);
        yield return EsperarQue(() => atrapado, 20f);
        yield return Esperar(1.6f);

        // Final del saco: el Silbon se aleja con el saco y la niebla se lo traga.
        var saco = Buscar("saco silbon");
        yield return EsperarQue(() => saco != null && saco.activeInHierarchy, 6f);
        Marca("final_saco");
        if (saco != null)
            Camara(() => saco.transform.position + new Vector3(4.5f, 2.4f, -7f), () => saco.transform.position + Vector3.up * 2.2f, 45f);
        yield return EsperarQue(() => saco == null || !saco.activeInHierarchy || saco.transform.position.z > 51.5f, 20f);

        // Los textos del final, frente al jugador.
        Marca("textos");
        Camara(null, null, 55f);
        yield return Esperar(60f);   // el juego cierra el Play al terminar
    }

    // ------------------------------------------------------------------ final bueno
    IEnumerator GuionFinalBueno()
    {
        ColocarJugador(new Vector3(2.9f, 0.05f, 12.6f), 180f);

        // El rayo pasa por cada pez colgado y aparece su cartel.
        Camara(() => new Vector3(7.3f, 1.55f, 17.95f), () => new Vector3(9.6f, 1.3f, 17.95f), 48f);
        yield return Esperar(1f);
        Marca("carteles");
        var campo = typeof(CartelAlApuntar).GetField("apuntadores", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var nombre in new[] { "bagre colgado", "pescado1normal", "PESCADO 2 NORMAL " })
        {
            var pez = Buscar(nombre);
            var cartel = pez != null ? pez.GetComponentInChildren<CartelAlApuntar>() : null;
            if (cartel == null) continue;
            yield return Esperar(0.5f);
            campo.SetValue(cartel, 1);
            yield return Esperar(2.6f);
            campo.SetValue(cartel, 0);
        }
        yield return Esperar(0.8f);
        Marca("fin_carteles");

        // Final bueno: el jugador presiona Finalizar.
        Marca("final_bueno");
        Camara(null, null, 55f);
        var mejora = FindFirstObjectByType<MejoraRed>();
        if (mejora != null) mejora.Finalizar();
        yield return Esperar(80f);   // el juego cierra el Play al terminar
    }
}
