\
using System;
using System.Collections.Generic;
using UnityEngine;

public class RoomPlanExporter : MonoBehaviour
{
    [Serializable]
    public class Vector3Data { public float x; public float y; public float z; }

    [Serializable]
    public class SpawnPoint
    {
        public string id;
        public Vector3Data position;
        public Vector3Data forward;
        public string zone_id;
        public string[] tags;
    }

    [Serializable]
    public class Zone
    {
        public string id;
        public string name;
        public string[] tags;
    }

    [Serializable]
    public class RoomPlan
    {
        public Zone[] zones;
        public SpawnPoint[] spawn_points;
    }

    [Header("Optional Zones (for nicer placement heuristics)")]
    public Zone[] zones = new Zone[0];

    public string ExportRoomPlanJson()
    {
        var sps = GameObject.FindObjectsOfType<AgentSpawnPoint>();

        var list = new List<SpawnPoint>();
        foreach (var sp in sps)
        {
            var pos = sp.transform.position;
            var fwd = sp.GetForward();
            list.Add(new SpawnPoint
            {
                id = sp.spawnPointId,
                zone_id = sp.zoneId,
                tags = sp.tags,
                position = new Vector3Data { x = pos.x, y = pos.y, z = pos.z },
                forward = new Vector3Data { x = fwd.x, y = fwd.y, z = fwd.z },
            });
        }

        var rp = new RoomPlan { zones = zones, spawn_points = list.ToArray() };
        return JsonUtility.ToJson(rp, prettyPrint: true);
    }

#if UNITY_EDITOR
    [ContextMenu("Print RoomPlan JSON")]
    public void PrintRoomPlanJson()
    {
        Debug.Log(ExportRoomPlanJson());
    }
#endif
}
