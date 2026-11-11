using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Graba las tomas que reemplazan los bocetos del storyboard, con el juego corriendo de verdad
/// (animaciones, agua, particulas). Time.captureFramerate fija 30 cuadros por segundo de juego,
/// asi que el resultado no depende de lo rapido que vaya el editor.
///
/// Uso (en Play mode): new GameObject("Grabador").AddComponent&lt;GrabadorStoryboard&gt;().
/// Deja un PNG por cuadro en carpetaSalida/&lt;toma&gt;/ y escribe listo.txt al terminar.
/// Solo toca objetos durante Play: al salir todo vuelve a como estaba.
/// </summary>
public class GrabadorStoryboard : MonoBehaviour
{
    public string carpetaSalida;
    public int ancho = 1024, alto = 576, fps = 30;
    [Tooltip("Vacio = todas. Si no, nombres separados por coma.")]
    public string soloTomas = "";
    [Tooltip("Graba solo 3 cuadros por toma (inicio, mitad, final) para revisar encuadres.")]
    public bool soloMuestras = false;

    class Toma
    {
        public string nombre;
        public float duracion;
        public Action preparar;
        public Func<float, Vector3> pos;   // u en [0,1]
        public Func<float, Vector3> mira;
        public float fov = 50f;
        public Action<float> cadaCuadro;
        public Action terminar;
    }

    Camera cam;
    RenderTexture rt;
    Texture2D tex;
    readonly List<Toma> tomas = new List<Toma>();

    // ------------------------------------------------------------------ utilidades
    static GameObject Buscar(string nombre)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == nombre && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    static Vector3 Suave(Vector3 a, Vector3 b, float u) { u = u * u * (3f - 2f * u); return Vector3.Lerp(a, b, u); }

    static Animator Animar(string objeto, string estado)
    {
        var g = Buscar(objeto);
        if (g == null) { Debug.LogWarning("[Grabador] No esta " + objeto); return null; }
        g.SetActive(true);
        var ex = g.GetComponent("ExhibidorAnimaciones") as Behaviour;
        if (ex != null) ex.enabled = false;
        var a = g.GetComponent<Animator>();
        a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        a.applyRootMotion = false;
        a.speed = 1f;
        a.Play(estado, 0, 0f);
        return a;
    }

    static void Activar(string nombre, bool activo)
    {
        var g = Buscar(nombre);
        if (g != null) g.SetActive(activo);
    }

    static void ApagarSilbones(string excepto)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t.gameObject.scene.IsValid() || t.parent != null) continue;
            string n = t.name.ToLower();
            if ((n.Contains("silvon") || n.Contains("silbon")) && t.name != excepto &&
                t.GetComponentInChildren<Animator>(true) != null)
                t.gameObject.SetActive(false);
        }
    }

    static DialogueController Dialogo(string nombre)
    {
        foreach (var d in Resources.FindObjectsOfTypeAll<DialogueController>())
            if (d.name == nombre && d.gameObject.scene.IsValid()) return d;
        return null;
    }

    static void MostrarDialogo(string controlador, int indice)
    {
        var d = Dialogo(controlador);
        if (d == null) { Debug.LogWarning("[Grabador] No esta el dialogo " + controlador); return; }
        d.gameObject.SetActive(true);
        d.ActivarDialogos();
        d.MostrarDialogo(indice);
        d.DetenerVoz();
    }

    static void OcultarDialogo(string controlador)
    {
        var d = Dialogo(controlador);
        if (d != null) d.OcultarTodosLosDialogosYFlechas();
    }

    // ------------------------------------------------------------------ escena
    void PrepararEscena()
    {
        // La intro (telon negro y logo) tapa todo.
        foreach (var n in new[] { "Telon", "Logo", "gestor intro" }) Activar(n, false);
        // La pantalla del tutorial va pegada a la cabeza del jugador y se cuela en las tomas.
        Activar("PantallaTutorial", false);

        // Los dialogos por cercania no deben arrancar solos durante la grabacion.
        foreach (var c in FindObjectsByType<ActivarDialogoPorCercania>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            c.enabled = false;

        // Peces iniciales de la mesa: el collider de la mesa esta 27 cm sobre la madera visible,
        // asi que se acomodan acostados sobre la madera.
        var raiz = Buscar("Peces iniciales mesa");
        if (raiz != null)
        {
            var pos = new Dictionary<string, Vector2>
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

        // La barra se dibuja por encima de los arboles del fondo; solo se ve en su toma.
        var barra = Buscar("Barra de progreso canvas");
        if (barra != null)
        {
            var cv = barra.GetComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = 5000;
            barra.SetActive(false);
        }

        ApagarSilbones(null);
    }

    void CrearCamara()
    {
        var go = new GameObject("CamGrabador");
        cam = go.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.depth = -100;      // no tapa la vista del juego
        cam.enabled = false;   // solo renderiza cuando se captura
        var main = Camera.main;
        if (main != null) { cam.cullingMask = main.cullingMask; cam.allowHDR = main.allowHDR; }
        rt = new RenderTexture(ancho, alto, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.aspect = (float)ancho / alto;
        tex = new Texture2D(ancho, alto, TextureFormat.RGB24, false);

        var ui = FindFirstObjectByType<FishingProgressBarUI>();
        if (ui != null) ui.camaraJugador = go.transform;
    }

    // ------------------------------------------------------------------ tomas
    void DefinirTomas()
    {
        // Hermana: mira hacia -x. Su burbuja y las flechas quedan a su lado (-z).
        Vector3 h = new Vector3(0.34f, 0.06f, 39.47f);
        tomas.Add(new Toma
        {
            nombre = "01_hermana_habla", duracion = 4f, fov = 52f,
            preparar = () => { Animar("Hermana (exhibicion)", "hermana hablando"); MostrarDialogo("Hermana primeros dialogos", 0); },
            pos = u => Suave(new Vector3(-4.8f, 1.75f, 38.5f), new Vector3(-4.1f, 1.7f, 38.35f), u),
            mira = u => new Vector3(0.1f, 1.55f, 38.2f),
            terminar = () => OcultarDialogo("Hermana primeros dialogos"),
        });

        // Llanero en la banca: mira hacia +z. Su burbuja queda a su derecha (-x).
        Vector3 hm = new Vector3(-0.66f, -0.22f, 20.63f);
        tomas.Add(new Toma
        {
            nombre = "02_llanero_habla", duracion = 4f, fov = 50f,
            preparar = () => { Animar("Hermano (exhibicion)", "hermano"); MostrarDialogo("Hermano en la banca", 0); },
            pos = u => Suave(new Vector3(-1.0f, 1.35f, 24.4f), new Vector3(-1.05f, 1.3f, 23.8f), u),
            mira = u => new Vector3(-1.15f, 0.95f, 20.65f),
            terminar = () => OcultarDialogo("Hermano en la banca"),
        });

        // Peces colgados y la mesa.
        Vector3 g = new Vector3(9.6f, 0.9f, 18f);
        tomas.Add(new Toma
        {
            nombre = "03_peces_colgados", duracion = 5f, fov = 45f,
            preparar = () => { },
            pos = u => Suave(g + new Vector3(-3.0f, 1.2f, -2.8f), g + new Vector3(-3.0f, 1.2f, -1.0f), u),
            mira = u => g + new Vector3(0f, 0.15f, 0f),
        });

        // Barra de progreso llenandose (sin tocar los peces reales, para no disparar eventos del juego).
        Vector3 bp = new Vector3(10.15f, 2.64f, 17.8f);
        FishingProgressBarUI barra = null;
        FishingSilbonDirector director = null;
        MethodInfo fill = null, fases = null;
        tomas.Add(new Toma
        {
            nombre = "04_barra_progreso", duracion = 11.5f, fov = 40f,
            preparar = () =>
            {
                Activar("Barra de progreso canvas", true);
                barra = FindFirstObjectByType<FishingProgressBarUI>();
                director = FindFirstObjectByType<FishingSilbonDirector>();
                var bf = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                fill = typeof(FishingProgressBarUI).GetMethod("ActualizarFillEnRellenos", bf);
                fases = typeof(FishingProgressBarUI).GetMethod("ActualizarFasesYRellenos", bf);
                barra.enabled = false;   // la llenamos a mano
            },
            pos = u => bp + new Vector3(-7f, 0.3f, 0f),
            mira = u => bp + new Vector3(0f, -0.2f, 0f),
            cadaCuadro = u =>
            {
                if (barra == null) return;
                float f = barra.FillParaPeces(u * director.totalFishNeeded);
                fill.Invoke(barra, new object[] { f });
                fases.Invoke(barra, null);
                barra.transform.rotation = Quaternion.LookRotation(barra.transform.position - cam.transform.position, Vector3.up);
                var e = barra.transform.eulerAngles; barra.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
            },
            terminar = () => Activar("Barra de progreso canvas", false),
        });

        // Hermana con la burbuja y las dos flechas (segundo dialogo).
        tomas.Add(new Toma
        {
            nombre = "05_hermana_opciones", duracion = 4f, fov = 55f,
            preparar = () => { Animar("Hermana (exhibicion)", "hermana hablando"); MostrarDialogo("Hermana primeros dialogos", 1); },
            pos = u => Suave(new Vector3(-4.6f, 1.6f, 39.4f), new Vector3(-4.6f, 1.65f, 38.6f), u),
            mira = u => new Vector3(0f, 1.5f, 38.1f),
            terminar = () => OcultarDialogo("Hermana primeros dialogos"),
        });

        // Caminar por la orilla hasta la red.
        tomas.Add(new Toma
        {
            nombre = "06_caminar_con_red", duracion = 6f, fov = 55f,
            preparar = () => { },
            pos = u => Suave(new Vector3(0.8f, 1.3f, 8.8f), new Vector3(3.0f, 1.1f, 11.3f), u) + new Vector3(0f, 0.03f * Mathf.Sin(u * 6f * 2f * Mathf.PI), 0f),
            mira = u => new Vector3(3.94f, 0.35f, 13.44f),
        });

        // Llanero con la burbuja y las dos flechas (dialogo mas adelante).
        tomas.Add(new Toma
        {
            nombre = "07_llanero_opciones", duracion = 4f, fov = 52f,
            preparar = () => { Animar("Hermano (exhibicion)", "hermano habla 2 "); MostrarDialogo("Hermano en la banca", 3); },
            pos = u => Suave(new Vector3(-1.6f, 1.4f, 24.1f), new Vector3(-1.4f, 1.35f, 23.6f), u),
            mira = u => new Vector3(-1.2f, 0.95f, 20.65f),
            terminar = () => OcultarDialogo("Hermano en la banca"),
        });

        // Silbon caminando con el saco.
        Vector3 sc = new Vector3(14.7f, 0.12f, 33.11f);
        tomas.Add(new Toma
        {
            nombre = "08_silbon_saco", duracion = 5f, fov = 55f,
            preparar = () => { ApagarSilbones("saco silbon"); Animar("saco silbon", "Walk"); },
            pos = u => Suave(sc + new Vector3(-7f, 2.2f, -4f), sc + new Vector3(-6f, 2.4f, -2f), u),
            mira = u => sc + new Vector3(0f, 2.5f, 0f),
            terminar = () => Activar("saco silbon", false),
        });

        // Silbon de pie, uno solo.
        Vector3 s = new Vector3(9.36f, 0.16f, 33.3f);
        tomas.Add(new Toma
        {
            nombre = "09_silbon_loma", duracion = 5f, fov = 50f,
            preparar = () => { ApagarSilbones("silvon unity (1)"); Animar("silvon unity (1)", "Idle"); DesactivarMovimiento("silvon unity (1)"); },
            pos = u => Suave(s + new Vector3(-7f, 0.5f, -6f), s + new Vector3(-5.5f, 0.5f, -5f), u),
            mira = u => s + new Vector3(0f, 2.2f, 0f),
            terminar = () => Activar("silvon unity (1)", false),
        });
    }

    static void DesactivarMovimiento(string nombre)
    {
        var g = Buscar(nombre);
        if (g == null) return;
        var agent = g.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null) agent.enabled = false;
    }

    // ------------------------------------------------------------------ grabacion
    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(carpetaSalida))
            carpetaSalida = Path.Combine(Path.GetTempPath(), "sb_frames");
        Directory.CreateDirectory(carpetaSalida);
        File.Delete(Path.Combine(carpetaSalida, "listo.txt"));

        PrepararEscena();
        CrearCamara();
        DefinirTomas();

        var filtro = new HashSet<string>();
        foreach (var n in soloTomas.Split(',')) if (n.Trim().Length > 0) filtro.Add(n.Trim());

        Time.captureFramerate = fps;
        yield return null;

        var log = new System.Text.StringBuilder();
        foreach (var t in tomas)
        {
            if (filtro.Count > 0 && !filtro.Contains(t.nombre)) continue;
            string dir = Path.Combine(carpetaSalida, t.nombre);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);

            t.preparar();
            // Un par de cuadros para que se asienten las animaciones y los dialogos.
            for (int k = 0; k < 3; k++) { Colocar(t, 0f); yield return null; }

            int n = Mathf.RoundToInt(t.duracion * fps);
            for (int i = 0; i < n; i++)
            {
                float u = n > 1 ? (float)i / (n - 1) : 0f;
                Colocar(t, u);
                if (t.cadaCuadro != null) t.cadaCuadro(u);
                yield return new WaitForEndOfFrame();
                bool guardar = !soloMuestras || i == 0 || i == n / 2 || i == n - 1;
                if (guardar) Capturar(Path.Combine(dir, i.ToString("0000") + ".png"));
                yield return null;
            }
            if (t.terminar != null) t.terminar();
            log.AppendLine(t.nombre + " " + n);
        }

        Time.captureFramerate = 0;
        File.WriteAllText(Path.Combine(carpetaSalida, "listo.txt"), log.ToString());
        Debug.Log("[Grabador] Listo:\n" + log);
    }

    void Colocar(Toma t, float u)
    {
        cam.fieldOfView = t.fov;
        cam.transform.position = t.pos(u);
        cam.transform.rotation = Quaternion.LookRotation(t.mira(u) - cam.transform.position, Vector3.up);
    }

    void Capturar(string ruta)
    {
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(ruta, tex.EncodeToPNG());
    }

    void OnDestroy()
    {
        Time.captureFramerate = 0;
        if (rt != null) rt.Release();
    }
}
