using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Graba los clips que reemplazan los bocetos del storyboard. Se usa en Play mode:
///   StoryboardClips.Preview("01_hermana_habla", 1.5f)  -> deja la toma puesta para sacar captura.
///   StoryboardClips.RecordAll() / Record("nombre")      -> graba mp4 en Assets/Silbon/Clips storyboard.
/// No guarda nada en la escena (todo se revierte al salir de Play).
/// </summary>
public static class StoryboardClips
{
    const string CarpetaSalida = "D:/juego taller 5/llaneritos/grabacion_temp";

    class Toma
    {
        public string nombre;
        public float duracion;
        public Action preparar;                 // deja personajes/objetos listos
        public Func<float, Vector3> posCam;     // t en [0,1] -> posicion de la camara
        public Func<float, Vector3> miraA;      // t en [0,1] -> punto al que mira
        public float fov = 50f;
        public Action<float> cadaFrame;         // t en [0,1]
    }

    static Camera cam;
    static readonly List<Toma> tomas = new List<Toma>();
    static int indiceActual;
    static Toma actual;
    static float inicio;
    static bool grabando;
    static RecorderController controlador;
    static List<string> cola = new List<string>();
    public static string Estado = "";

    // ---------- utilidades ----------
    static GameObject Buscar(string nombre)
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            if (t.name == nombre && t.gameObject.scene.IsValid()) return t.gameObject;
        return null;
    }

    static void Anim(string objeto, string estado)
    {
        var g = Buscar(objeto);
        if (g == null) { Debug.LogWarning("No esta " + objeto); return; }
        g.SetActive(true);
        var ex = g.GetComponent("ExhibidorAnimaciones") as Behaviour;
        if (ex != null) ex.enabled = false;
        var a = g.GetComponent<Animator>();
        a.applyRootMotion = false;
        a.Rebind();
        a.Play(estado, 0, 0f);
        a.Update(0f);
    }

    static Vector3 L(Vector3 a, Vector3 b, float t) { t = t * t * (3f - 2f * t); return Vector3.Lerp(a, b, t); }

    static void Definir()
    {
        if (tomas.Count > 0) return;

        // 01 y 05: la hermana
        Vector3 h = new Vector3(0.34f, 0.06f, 39.47f);
        tomas.Add(new Toma
        {
            nombre = "01_hermana_habla", duracion = 4f,
            preparar = () => Anim("Hermana (exhibicion)", "hermana hablando"),
            posCam = t => L(h + new Vector3(3.6f, 1.6f, 0.3f), h + new Vector3(2.6f, 1.55f, 0.1f), t),
            miraA = t => h + new Vector3(0, 1.45f, 0),
        });
        tomas.Add(new Toma
        {
            nombre = "05_hermana_opciones", duracion = 4f,
            preparar = () => Anim("Hermana (exhibicion)", "hermana hablando"),
            posCam = t => L(h + new Vector3(3.0f, 1.4f, 1.6f), h + new Vector3(3.0f, 1.5f, 0.8f), t),
            miraA = t => h + new Vector3(0, 1.3f, 0),
            fov = 55f,
        });

        // 02 y 07: el llanero (hermano)
        Vector3 hm = new Vector3(-0.66f, -0.22f, 20.63f);
        tomas.Add(new Toma
        {
            nombre = "02_llanero_brazos", duracion = 4f,
            preparar = () => Anim("Hermano (exhibicion)", "hermano"),
            posCam = t => L(hm + new Vector3(0.4f, 1.5f, 3.6f), hm + new Vector3(0.2f, 1.5f, 2.8f), t),
            miraA = t => hm + new Vector3(0, 1.4f, 0),
        });
        // El hermano solo tiene poses sentadas: se usa el de la banca, desde otro angulo.
        tomas.Add(new Toma
        {
            nombre = "07_llanero_pulgar", duracion = 4f,
            preparar = () => Anim("Hermano (exhibicion)", "hermano habla 2 "),
            posCam = t => L(hm + new Vector3(-1.8f, 1.2f, 2.2f), hm + new Vector3(-1.3f, 1.2f, 1.7f), t),
            miraA = t => hm + new Vector3(0, 0.9f, 0),
        });

        // 03: peces colgados en el gancho y la mesa
        Vector3 g = new Vector3(9.6f, 0.9f, 18f);
        tomas.Add(new Toma
        {
            nombre = "03_peces_colgados", duracion = 5f,
            preparar = () => { },
            posCam = t => L(g + new Vector3(-3.4f, 1.0f, -1.6f), g + new Vector3(-3.4f, 0.9f, 1.4f), t),
            miraA = t => g + new Vector3(0, 0.3f, 0),
            fov = 50f,
        });

        // 04: barra de progreso llenandose
        Vector3 b = new Vector3(10.15f, 2.64f, 17.8f);
        tomas.Add(new Toma
        {
            nombre = "04_barra_progreso", duracion = 12f,
            preparar = () => { Director().currentFishCount = 0; Director().bonoPlacebo = 0; },
            posCam = t => b + new Vector3(-7f, 0.3f, 0f),
            miraA = t => b,
            fov = 40f,
            cadaFrame = t => { Director().currentFishCount = Mathf.Min(10, Mathf.FloorToInt(t * 10.99f)); },
        });

        // 06: caminar junto a la red por la orilla
        tomas.Add(new Toma
        {
            nombre = "06_caminar_con_red", duracion = 6f,
            preparar = () => { },
            posCam = t => L(new Vector3(0.5f, 1.3f, 9.5f), new Vector3(3.5f, 1.3f, 12.8f), t),
            miraA = t => new Vector3(3.94f, 0.4f, 13.44f),
            fov = 60f,
        });

        // 08: el Silbon con el saco caminando
        Vector3 sc = new Vector3(14.7f, 0.12f, 33.11f);
        tomas.Add(new Toma
        {
            nombre = "08_silbon_saco", duracion = 5f,
            preparar = () => Anim("saco silbon", "Walk"),
            posCam = t => L(sc + new Vector3(-7f, 2.2f, -4f), sc + new Vector3(-6f, 2.4f, -2f), t),
            miraA = t => sc + new Vector3(0, 2.5f, 0),
            fov = 55f,
        });

        // 09: el Silbon de pie
        Vector3 s = new Vector3(9.36f, 0.16f, 33.3f);
        tomas.Add(new Toma
        {
            nombre = "09_silbon_loma", duracion = 5f,
            preparar = () => Anim("silvon unity (1)", "Idle"),
            posCam = t => L(s + new Vector3(-7f, 0.5f, -6f), s + new Vector3(-5.5f, 0.5f, -5f), t),
            miraA = t => s + new Vector3(0, 2.2f, 0),
            fov = 50f,
        });
    }

    static FishingSilbonDirector Director() { return UnityEngine.Object.FindFirstObjectByType<FishingSilbonDirector>(); }

    static void AsegurarCamara()
    {
        if (cam != null) return;
        // La intro (telon negro + logo) tapa la escena: se apaga solo durante la grabacion.
        foreach (var n in new[] { "Telon", "Logo", "gestor intro" })
        {
            var o = Buscar(n);
            if (o != null) o.SetActive(false);
        }
        var go = new GameObject("CamStoryboard");
        cam = go.AddComponent<Camera>();
        cam.depth = 50;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1500f;
        cam.clearFlags = CameraClearFlags.Skybox;
        var main = Camera.main;
        if (main != null)
        {
            cam.cullingMask = main.cullingMask;
            cam.allowHDR = main.allowHDR;
            // La camara del jugador deja de ser MainCamera para que el Recorder tome la nuestra.
            main.tag = "Untagged";
        }
        go.tag = "MainCamera";
        var listener = go.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = false;
        var bar = UnityEngine.Object.FindFirstObjectByType<FishingProgressBarUI>();
        if (bar != null) bar.camaraJugador = go.transform;
    }

    static Toma Buscar2(string n) { Definir(); return tomas.Find(x => x.nombre == n); }

    static void Aplicar(Toma t, float u)
    {
        cam.fieldOfView = t.fov;
        cam.transform.position = t.posCam(u);
        cam.transform.rotation = Quaternion.LookRotation(t.miraA(u) - cam.transform.position, Vector3.up);
        if (t.cadaFrame != null) t.cadaFrame(u);
    }

    /// <summary>Cambia el encuadre de una toma sin recompilar: camara de inicio, camara final, punto al que mira y fov.</summary>
    public static string Ajustar(string nombre, Vector3 camA, Vector3 camB, Vector3 mira, float fov)
    {
        var t = Buscar2(nombre);
        if (t == null) return "No existe " + nombre;
        t.posCam = u => L(camA, camB, u);
        t.miraA = u => mira;
        t.fov = fov;
        return "ajustada " + nombre;
    }

    public static string[] Nombres() { Definir(); return tomas.ConvertAll(x => x.nombre).ToArray(); }

    // ---------- vista previa ----------
    public static string Preview(string nombre, float segundos)
    {
        if (!Application.isPlaying) return "Hace falta Play mode";
        var t = Buscar2(nombre);
        if (t == null) return "No existe " + nombre;
        AsegurarCamara();
        t.preparar();
        // Deja avanzar la animacion hasta 'segundos' desde el comienzo de la toma.
        Aplicar(t, Mathf.Clamp01(segundos / t.duracion));
        return "ok " + nombre;
    }

    public static void Avanzar(string objeto, float segundos)
    {
        var g = Buscar(objeto);
        if (g != null) g.GetComponent<Animator>().Update(segundos);
    }

    // ---------- grabacion ----------
    public static string RecordAll() { Definir(); return Record(string.Join(",", Nombres())); }

    public static string Record(string lista)
    {
        if (!Application.isPlaying) return "Hace falta Play mode";
        if (grabando || cola.Count > 0) return "Ya hay una grabacion en curso";
        Definir();
        AsegurarCamara();
        cola = new List<string>(lista.Split(','));
        Directory.CreateDirectory(CarpetaSalida);
        EditorApplication.update += Tick;
        Estado = "iniciando";
        return "En cola: " + string.Join(", ", cola);
    }

    static float t0;
    static System.Text.StringBuilder segmentos;

    // Una sola grabacion continua con todas las tomas; despues se corta con ffmpeg usando segmentos.txt
    static void Tick()
    {
        if (!Application.isPlaying) { Parar(); return; }

        if (actual == null)
        {
            if (cola.Count == 0)
            {
                if (grabando)
                {
                    controlador.StopRecording();
                    grabando = false;
                    File.WriteAllText(Path.Combine(CarpetaSalida, "segmentos.txt"), segmentos.ToString());
                    Estado = "terminado";
                }
                EditorApplication.update -= Tick;
                return;
            }
            actual = Buscar2(cola[0].Trim());
            cola.RemoveAt(0);
            if (actual == null) return;
            actual.preparar();
            Aplicar(actual, 0f);
            if (!grabando)
            {
                IniciarGrabador("tomas_" + DateTime.Now.ToString("HHmmss"));
                t0 = Time.time;
                segmentos = new System.Text.StringBuilder();
                grabando = true;
            }
            Estado = "grabando " + actual.nombre;
            inicio = Time.time;
            segmentos.AppendLine(actual.nombre + " " + (inicio - t0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " " + actual.duracion.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        float u = (Time.time - inicio) / actual.duracion;
        if (u >= 1f) { actual = null; return; }
        Aplicar(actual, u);
    }

    static void Parar()
    {
        if (controlador != null && controlador.IsRecording()) controlador.StopRecording();
        grabando = false; actual = null; cola.Clear();
        EditorApplication.update -= Tick;
    }

    static void IniciarGrabador(string nombre)
    {
        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = nombre;
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            Codec = CoreEncoderSettings.OutputCodec.MP4
        };
        movie.CaptureAlpha = false;
        movie.ImageInputSettings = new CameraInputSettings
        {
            Source = ImageSource.MainCamera,
            OutputWidth = 1024,
            OutputHeight = 576,
            CaptureUI = false
        };
        movie.AudioInputSettings.PreserveAudio = false;
        movie.OutputFile = Path.Combine(CarpetaSalida, nombre);
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.ExitPlayMode = false;
        settings.FrameRate = 30f;
        settings.CapFrameRate = true;
        controlador = new RecorderController(settings);
        controlador.PrepareRecording();
        controlador.StartRecording();
    }
}
