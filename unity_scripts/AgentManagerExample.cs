using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AgentManagerExample : MonoBehaviour
{
    public BackendClient backend;
    public string roomPlanPath = "examples/room_plan.example.json";
    public string agentsPath = "examples/agents.example.json";

    [Header("Runtime")]
    public string sessionId;
    public string activeAgentId = "agent_tech";

    // Tracks spawned agent GameObjects by agent id
    private readonly Dictionary<string, GameObject> _spawnedAgents = new();

    private void Start()
    {
        if (backend == null) backend = FindObjectOfType<BackendClient>();
        StartCoroutine(Setup());
    }

    IEnumerator Setup()
    {
        yield return backend.SetupFromPaths(roomPlanPath, agentsPath,
            onOk: (resp) =>
            {
                sessionId = resp.session_id;
                Debug.Log("[Setup] session_id=" + sessionId);

                GameObject[] characterPrefabs = Resources.LoadAll<GameObject>("Characters");
                if (characterPrefabs == null || characterPrefabs.Length == 0)
                {
                    Debug.LogWarning("[AgentManager] Keine Character-Prefabs in Resources/Characters gefunden.");
                    return;
                }

                if (resp.agents != null)
                {
                    foreach (var a in resp.agents)
                    {
                        SpawnAgent(a, characterPrefabs);
                    }
                }
            },
            onErr: (err) =>
            {
                Debug.LogError("[Setup] " + err);
            }
        );
    }

    private void SpawnAgent(BackendClient.AgentPlacement a, GameObject[] prefabs)
    {
        // Remove stale instance if re-spawning
        if (_spawnedAgents.TryGetValue(a.id, out var old) && old != null)
            Destroy(old);

        GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];

        Vector3 pos = new Vector3(a.position.x, a.position.y, a.position.z);
        Vector3 fwd = new Vector3(a.forward.x, a.forward.y, a.forward.z);
        Quaternion rot = fwd.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(fwd, Vector3.up)
            : Quaternion.identity;

        GameObject instance = Instantiate(prefab, pos, rot);
        instance.name = $"Agent_{a.id}_{a.display_name}";
        _spawnedAgents[a.id] = instance;

        Debug.Log($"[Agent] {a.id} ({a.display_name}) -> prefab '{prefab.name}' @ ({pos.x:F2},{pos.y:F2},{pos.z:F2})");
    }

    public void SendTestChat(string text)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogWarning("No sessionId yet.");
            return;
        }
        StartCoroutine(backend.Chat(sessionId, activeAgentId, text,
            onOk: (resp) =>
            {
                activeAgentId = resp.active_agent_id;
                if (resp.events != null)
                {
                    foreach (var ev in resp.events)
                    {
                        Debug.Log($"[{ev.agent_id}] {ev.text}");
                    }
                }
            },
            onErr: (err) =>
            {
                Debug.LogError("[Chat] " + err);
            }
        ));
    }
}
