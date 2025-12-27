\
using System.Collections;
using UnityEngine;

public class AgentManagerExample : MonoBehaviour
{
    public BackendClient backend;
    public string roomPlanPath = "examples/room_plan.example.json";
    public string agentsPath = "examples/agents.example.json";

    [Header("Runtime")]
    public string sessionId;
    public string activeAgentId = "agent_tech";

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

                if (resp.agents != null)
                {
                    foreach (var a in resp.agents)
                    {
                        Debug.Log($"[Agent] {a.id} ({a.display_name}) @ ({a.position.x},{a.position.y},{a.position.z})");
                    }
                }
            },
            onErr: (err) =>
            {
                Debug.LogError("[Setup] " + err);
            }
        );
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
