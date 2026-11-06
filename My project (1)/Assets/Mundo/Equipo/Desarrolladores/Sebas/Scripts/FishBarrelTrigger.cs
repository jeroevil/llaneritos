using UnityEngine;

/// <summary>
/// Colocar en el barril con un Collider marcado como IsTrigger.
/// Detecta cuando un pez cae adentro y notifica al FishingSilbonDirector.
///
/// Si el pez tiene TipoPez y no cuenta para el progreso (bagre amarillo, curito), solo mueve la barra (placebo).
/// Un pez sin TipoPez cuenta como cachama (comportamiento de siempre).
/// </summary>
public class FishBarrelTrigger : MonoBehaviour
{
    [Tooltip("Referencia al director de pesca y Silbon")]
    public FishingSilbonDirector progressionDirector;

    [Tooltip("Tag que identifica a los objetos pez")]
    public string fishTag = "Pez";

    private void Start()
    {
        if (progressionDirector == null)
            progressionDirector = FindFirstObjectByType<FishingSilbonDirector>();
    }

    private void OnTriggerEnter(Collider other)
    {
        // Detectar si el objeto que entro es un pez
        if (other.CompareTag(fishTag) || other.name.ToLower().Contains("pez") || other.name.ToLower().Contains("fish"))
        {
            if (progressionDirector != null)
            {
                var tipo = other.GetComponentInParent<TipoPez>();
                if (tipo == null || tipo.CuentaParaProgreso)
                {
                    progressionDirector.AddFish(1);
                }
                else
                {
                    progressionDirector.AddPlacebo(tipo.AporteBarra);
                    Debug.Log($"[Pesca] {tipo.TipoDePez} depositado: no cuenta para el Silbon ni la red, solo mueve la barra (+{tipo.AporteBarra:F2}).");
                }
            }

            // Desactivar o destruir el pez depositado en el barril
            Destroy(other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject);
        }
    }
}
