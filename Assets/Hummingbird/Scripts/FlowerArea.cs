using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// Manages a collection of flower plants and attached buds
/// </summary>
public class FlowerArea : MonoBehaviour
{
    // The diameter of where agent and flowers can be used for observing relative distance from agent to flower
    public const float AreaDiameter = 20f;

    // List of all flowers in the flower area
    private List<GameObject> flowerPlants;

    // lookup flower from nectar collider
    private Dictionary<Collider, Flower> nectarFlowerDictionary;

    /// <summary>
    /// List of all flowers in the area
    /// </summary>
    public List<Flower> Flowers { get; private set; }

    public void ResetFlowers()
    {
        // Rotate flower around
        foreach (GameObject flowerPlant in flowerPlants)
        {
            float xRotation = UnityEngine.Random.Range(-5f, 5f);
            float yRotation = UnityEngine.Random.Range(-180f, 180f);
            float zRotation = UnityEngine.Random.Range(-5f, 5f);
            flowerPlant.transform.localRotation = Quaternion.Euler(xRotation, yRotation, zRotation);
        }
        foreach (Flower flower in Flowers)
        {
            flower.ResetFlower();
        }
    }
    public Flower GetFlowerFromNectar(Collider collider)
    {
        return nectarFlowerDictionary[collider];
    }

    private void Awake()
    {
        flowerPlants = new List<GameObject>();
        nectarFlowerDictionary = new Dictionary<Collider, Flower>();
        Flowers = new List<Flower>();
    }

    private void Start()
    {
        FindChildFlowers(transform);
    }

    private void FindChildFlowers(Transform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            
            if (child.CompareTag("flower_plant"))
            {
                flowerPlants.Add(child.gameObject);

                FindChildFlowers(child);
            }
            else
            {
                Flower flower = child.GetComponent<Flower>();
                if (flower != null)
                {
                    Flowers.Add(flower);

                    nectarFlowerDictionary.Add(flower.nectarCollider, flower);
                }
                else
                {
                    FindChildFlowers(child);
                }
            }
        }
        
    }
}
