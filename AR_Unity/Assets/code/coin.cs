using UnityEngine;

public class coin : MonoBehaviour
{
    public float pickupRadius = 1.5f;

    private Transform dron;

    void Start()
    {
        GameObject dronObj = GameObject.Find("dron");
        if (dronObj != null)
            dron = dronObj.transform;
    }

    void Update()
    {
        if (dron == null) return;

        if (Vector3.Distance(transform.position, dron.position) <= pickupRadius)
        {
            Destroy(gameObject);
        }
    }
}
