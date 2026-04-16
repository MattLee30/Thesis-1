using UnityEngine;

public class Rotate : MonoBehaviour
{
    public GameObject target; // The object to rotate around
    public float rotationSpeed = 10f; // Speed of rotation

    public bool rotatex = false; // Rotate around the x-axis
    public bool rotatey = false; // Rotate around the y-axis
    public bool rotatez = false; // Rotate around the z-axis

    void Update()
    {
        if (target != null)
        {
            if (rotatex)
            {
                transform.Rotate(Vector3.right * rotationSpeed * Time.deltaTime);
            }
            if (rotatey)
            {
                transform.Rotate(Vector3.up * rotationSpeed * Time.deltaTime);
            }
            if (rotatez)
            {
                transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);
            }
        }
    }
}
