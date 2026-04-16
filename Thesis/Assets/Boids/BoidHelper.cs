using UnityEngine;

public static class BoidHelper
{
    private const int NumDirections = 100;
    private static Vector3[] directions;

    public static Vector3[] Directions
    {
        get
        {
            if (directions == null)
                directions = GenerateDirections(NumDirections);
            return directions;
        }
    }

    private static Vector3[] GenerateDirections(int count)
    {
        // Golden ratio sphere point distribution for even spacing
        Vector3[] dirs = new Vector3[count];
        float goldenRatio = (1f + Mathf.Sqrt(5f)) / 2f;
        float angleIncrement = Mathf.PI * 2f * goldenRatio;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            float inclination = Mathf.Acos(1f - 2f * t);
            float azimuth = angleIncrement * i;

            float x = Mathf.Sin(inclination) * Mathf.Cos(azimuth);
            float y = Mathf.Sin(inclination) * Mathf.Sin(azimuth);
            float z = Mathf.Cos(inclination);

            dirs[i] = new Vector3(x, y, z);
        }

        return dirs;
    }
}
