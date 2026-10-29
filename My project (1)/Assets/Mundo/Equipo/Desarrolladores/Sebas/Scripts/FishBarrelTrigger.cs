using UnityEngine;

/// <summary>
/// Colocar en el barril con un Collider marcado como IsTrigger.
/// Detecta cuando un pez cae adentro y notifica al FishingSilbonDirector.
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
                progressionDirector.AddFish(1);
            }

            // Desactivar o destruir el pez depositado en el barril
            Destroy(other.gameObject);
        }
    }
}
