using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Peces que nadan libres en el rio (y saltan de vez en cuando) durante el final bueno:
/// el jugador deja de pescar y ve la vida del rio seguir.
///
/// Este objeto marca el centro de la zona y la altura de la superficie del agua (su Y).
/// Los peces se crean al llamar Empezar() (lo hace TextosFinal) a partir de un modelo sin animacion:
/// el nado (avance, coleteo y saltos) se hace por codigo.
/// </summary>
public class PecesNadando : MonoBehaviour
{
    [System.Serializable]
    public class ModeloPez
    {
        [Tooltip("Modelo del pez (bagre.fbx, pescado1normal.fbx, ...). Su camara y su luz se quitan solas.")]
        public GameObject modelo;
        [Tooltip("Largo del pez en metros (el modelo se escala solo para medir esto).")]
        [Min(0.1f)] public float largoMetros = 1.5f;
        [Tooltip("Rotacion que corrige el modelo para que su cabeza mire hacia +Z (bagre.fbx: cabeza en +X -> Y = -90).")]
        public Vector3 rotacion = Vector3.zero;
    }

    [Header("Modelos (se reparten entre los peces para que haya variedad)")]
    [SerializeField] List<ModeloPez> modelos = new List<ModeloPez>();
    [SerializeField, Min(1)] int cantidad = 8;
    [Tooltip("Variacion aleatoria de tamano de cada pez (0.2 = entre 80% y 120%).")]
    [SerializeField, Range(0f, 0.5f)] float variacionTamano = 0.2f;

    [Header("Modelo unico (antiguo, solo se usa si la lista de modelos esta vacia)")]
    [SerializeField] GameObject modeloPez;
    [Tooltip("Escala del modelo (el bagre.fbx mide ~15 m a escala 1).")]
    [SerializeField, Min(0.001f)] float escala = 0.08f;
    [SerializeField] Vector3 rotacionModelo = new Vector3(0f, -90f, 0f);

    [Header("Zona (alrededor de este objeto; su Y es la superficie del agua)")]
    [SerializeField] Vector2 tamanoZona = new Vector2(16f, 10f);
    [Tooltip("Metros bajo la superficie a los que nadan (poco, para que se vean).")]
    [SerializeField, Min(0f)] float profundidad = 0.12f;

    [Header("Nado")]
    [SerializeField] Vector2 velocidad = new Vector2(0.8f, 1.8f);
    [SerializeField, Min(0f)] float coleteoGrados = 8f;
    [SerializeField, Min(0f)] float coleteoVelocidad = 7f;
    [SerializeField, Min(0.1f)] float giroGradosPorSegundo = 70f;

    [Header("Saltos")]
    [Tooltip("Segundos entre saltos de cada pez (minimo, maximo).")]
    [SerializeField] Vector2 esperaEntreSaltos = new Vector2(2.5f, 7f);
    [SerializeField, Min(0f)] float alturaSalto = 0.9f;
    [SerializeField, Min(0.1f)] float duracionSalto = 1.1f;

    readonly List<GameObject> peces = new List<GameObject>();

    public bool Activo { get; private set; }

    [ContextMenu("Empezar (prueba)")]
    public void Empezar()
    {
        if (Activo) return;
        bool hayLista = false;
        foreach (var m in modelos) if (m != null && m.modelo != null) hayLista = true;
        if (!hayLista && modeloPez == null)
        {
            Debug.LogWarning("[PecesNadando] Falta asignar al menos un modelo de pez en " + name, this);
            return;
        }
        Activo = true;
        int desfase = Random.Range(0, 1000); // para que no siempre salga primero el mismo modelo
        for (int i = 0; i < cantidad; i++)
        {
            var pez = CrearPez(i + desfase);
            peces.Add(pez);
            StartCoroutine(Nadar(pez.transform));
        }
    }

    public void Detener()
    {
        Activo = false;
        StopAllCoroutines();
        foreach (var p in peces) if (p != null) Destroy(p);
        peces.Clear();
    }

    GameObject CrearPez(int i)
    {
        // Reparto por turnos entre los modelos validos: todos los tipos aparecen
        var validos = new List<ModeloPez>();
        foreach (var m in modelos) if (m != null && m.modelo != null) validos.Add(m);
        ModeloPez elegido = validos.Count > 0 ? validos[i % validos.Count] : null;
        GameObject prefab = elegido != null ? elegido.modelo : modeloPez;
        Vector3 rot = elegido != null ? elegido.rotacion : rotacionModelo;

        var raiz = new GameObject("pez nadando (" + prefab.name + ")");
        raiz.transform.SetParent(transform, false);
        raiz.transform.position = PuntoAleatorio();
        raiz.transform.rotation = Quaternion.identity; // se gira al azar despues de medir el modelo

        var modelo = Instantiate(prefab, raiz.transform);
        LimpiarExtrasDelModelo(modelo);
        modelo.transform.localRotation = Quaternion.Euler(rot);
        modelo.transform.localScale = Vector3.one;
        modelo.transform.localPosition = Vector3.zero;

        float factor = Random.Range(1f - variacionTamano, 1f + variacionTamano);
        float escalaFinal = escala * factor;
        if (elegido != null)
        {
            // Medir el modelo (ya girado, con la cabeza hacia +Z) y escalarlo para que mida 'largoMetros'
            var medida = modelo.GetComponentsInChildren<Renderer>();
            if (medida.Length > 0)
            {
                var mb = medida[0].bounds; foreach (var r in medida) mb.Encapsulate(r.bounds);
                float largoActual = Mathf.Max(mb.size.z, 0.0001f);
                escalaFinal = elegido.largoMetros / largoActual * factor;
            }
        }
        modelo.transform.localScale = Vector3.one * escalaFinal;
        raiz.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Centrar el modelo en su raiz (el pivote del fbx no esta en el centro del pez)
        var renderers = modelo.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            modelo.transform.position += raiz.transform.position - b.center;
        }
        foreach (var c in modelo.GetComponentsInChildren<Collider>()) c.enabled = false;

        // El agua es transparente y se dibuja antes que los objetos opacos de cola 3000+: dibujar el pez justo despues
        // (cola 3001) lo deja visible aunque nade apenas bajo la superficie, sin depender de la transparencia del agua.
        foreach (var r in modelo.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) m.renderQueue = 3001;
        return raiz;
    }

    /// <summary>
    /// Los fbx exportados de Blender (bagre.fbx) traen una Camera y una Light como hijos.
    /// Si se clonan, la camara del pez se dibuja encima de la del jugador y las luces iluminan la escena.
    /// Se destruyen los objetos que sean solo camara, luz o escucha de audio.
    /// </summary>
    static void LimpiarExtrasDelModelo(GameObject modelo)
    {
        foreach (var c in modelo.GetComponentsInChildren<Camera>(true)) QuitarComponente(c);
        foreach (var l in modelo.GetComponentsInChildren<Light>(true)) QuitarComponente(l);
        foreach (var a in modelo.GetComponentsInChildren<AudioListener>(true)) QuitarComponente(a);
    }

    static void QuitarComponente(Component c)
    {
        if (c == null) return;
        var go = c.gameObject;
        // Un hijo que solo existe para ser camara/luz se borra entero; si es la raiz o tiene malla, solo se apaga el componente.
        bool tieneMalla = go.GetComponent<MeshRenderer>() != null || go.GetComponent<SkinnedMeshRenderer>() != null;
        if (go.transform.parent != null && !tieneMalla) { go.SetActive(false); Destroy(go); return; }
        if (c is Behaviour b) b.enabled = false;
        Destroy(c);
    }

    Vector3 PuntoAleatorio()
    {
        var c = transform.position;
        return new Vector3(
            c.x + Random.Range(-tamanoZona.x, tamanoZona.x) * 0.5f,
            c.y - profundidad,
            c.z + Random.Range(-tamanoZona.y, tamanoZona.y) * 0.5f);
    }

    IEnumerator Nadar(Transform pez)
    {
        float vel = Random.Range(velocidad.x, velocidad.y);
        float fase = Random.Range(0f, 10f);
        float proximoSalto = Time.time + Random.Range(esperaEntreSaltos.x, esperaEntreSaltos.y);
        Vector3 objetivo = PuntoAleatorio();
        float yaw = pez.eulerAngles.y;

        while (true)
        {
            // Girar suave hacia el objetivo y avanzar
            Vector3 dir = objetivo - pez.position; dir.y = 0f;
            if (dir.magnitude < 0.6f) { objetivo = PuntoAleatorio(); continue; }
            float yawObjetivo = Quaternion.LookRotation(dir).eulerAngles.y;
            yaw = Mathf.MoveTowardsAngle(yaw, yawObjetivo, giroGradosPorSegundo * Time.deltaTime);

            Vector3 adelante = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 pos = pez.position + adelante * vel * Time.deltaTime;
            pos.y = transform.position.y - profundidad;
            pez.position = pos;

            float coleteo = Mathf.Sin((Time.time + fase) * coleteoVelocidad) * coleteoGrados;
            pez.rotation = Quaternion.Euler(0f, yaw + coleteo, 0f);

            if (Time.time >= proximoSalto)
            {
                yield return Saltar(pez, yaw, vel);
                proximoSalto = Time.time + Random.Range(esperaEntreSaltos.x, esperaEntreSaltos.y);
            }
            yield return null;
        }
    }

    IEnumerator Saltar(Transform pez, float yaw, float vel)
    {
        float t = 0f;
        float altura = alturaSalto * Random.Range(0.7f, 1.2f);
        Vector3 adelante = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        float baseY = transform.position.y - profundidad;
        while (t < duracionSalto)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duracionSalto);
            Vector3 pos = pez.position + adelante * vel * 1.6f * Time.deltaTime;
            pos.y = baseY + Mathf.Sin(u * Mathf.PI) * altura;
            pez.position = pos;
            // Nariz arriba al subir, abajo al caer
            float cabeceo = Mathf.Lerp(-40f, 40f, u);
            pez.rotation = Quaternion.Euler(cabeceo, yaw, 0f);
            yield return null;
        }
    }
}
