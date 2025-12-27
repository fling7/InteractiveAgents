\
using UnityEngine;

public class AgentSpawnPoint : MonoBehaviour
{
    [Header("Spawn Meta")]
    public string spawnPointId = "sp_1";
    public string zoneId = "demo_area";
    public string[] tags = new string[] { "demo" };

    [Header("Forward")]
    public Transform forwardTransform; // optional: if null, uses object's forward

    public Vector3 GetForward()
    {
        if (forwardTransform != null) return forwardTransform.forward.normalized;
        return transform.forward.normalized;
    }
}
