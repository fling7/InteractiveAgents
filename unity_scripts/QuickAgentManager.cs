using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class QuickAgentManager : MonoBehaviour
{
    [Header("Backend")]
    public string backendBaseUrl = "http://127.0.0.1:8787";
    public string roomPlanPath = "examples/room_plan.example.json";
    public string agentsPath = "examples/agents.example.json";

    [Header("Spawn")]
    public Vector3 spawnArea = new Vector3(12f, 0f, 12f);
    public float spawnHeight = 0.5f;
    public Vector2 boxScaleRange = new Vector2(0.8f, 1.2f);

    [Header("UI")]
    public bool showUi = true;
    public Rect uiRect = new Rect(10, 10, 420, 520);

    [Serializable]
    public class Vector3Data { public float x; public float y; public float z; }

    [Serializable]
    public class SetupRequestPaths
    {
        public string room_plan_path;
        public string agents_path;
        public string session_id;
    }

    [Serializable]
    public class AgentPlacement
    {
        public string id;
        public string display_name;
        public Vector3Data position;
        public Vector3Data forward;
        public string spawn_point_id;
        public string zone_id;
        public string[] tags;
    }

    [Serializable]
    public class SetupResponse
    {
        public string session_id;
        public AgentPlacement[] agents;
    }

    [Serializable]
    public class ChatRequest
    {
        public string session_id;
        public string active_agent_id;
        public string user_text;
    }

    [Serializable]
    public class ChatEvent
    {
        public string type;
        public string agent_id;
        public string text;
    }

    [Serializable]
    public class Handoff
    {
        public string from;
        public string to;
        public string reason;
    }

    [Serializable]
    public class ChatResponse
    {
        public string session_id;
        public string active_agent_id;
        public Handoff handoff;
        public ChatEvent[] events;
    }

    [Header("Runtime")]
    public string sessionId;
    public string activeAgentId;

    private readonly Dictionary<string, GameObject> agentObjects = new Dictionary<string, GameObject>();
    private readonly List<string> chatLog = new List<string>();
    private AgentPlacement[] lastAgents;
    private string statusMessage = "";
    private string chatInput = "";
    private Vector2 agentScroll;
    private Vector2 chatScroll;

    private void Start()
    {
        EnsureSceneBasics();
        StartCoroutine(SetupFromServer());
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            TrySelectAgentFromClick();
        }
    }

    private void EnsureSceneBasics()
    {
        if (Camera.main == null)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 12f, -12f);
            camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        }

        if (FindObjectOfType<Light>() == null)
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    private IEnumerator SetupFromServer()
    {
        statusMessage = "Setup läuft...";
        var url = $"{backendBaseUrl}/setup";
        var payload = new SetupRequestPaths
        {
            room_plan_path = roomPlanPath,
            agents_path = agentsPath
        };
        var json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Setup fehlgeschlagen: " + req.error;
                chatLog.Add(statusMessage + " | " + req.downloadHandler.text);
                yield break;
            }

            var resp = JsonUtility.FromJson<SetupResponse>(req.downloadHandler.text);
            sessionId = resp.session_id;
            lastAgents = resp.agents ?? Array.Empty<AgentPlacement>();
            statusMessage = $"Setup OK. Agents: {lastAgents.Length}";
            SpawnAgents(lastAgents);
            if (lastAgents.Length > 0)
            {
                activeAgentId = lastAgents[0].id;
            }
        }
    }

    private void SpawnAgents(AgentPlacement[] agents)
    {
        foreach (var entry in agentObjects)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value);
            }
        }
        agentObjects.Clear();

        for (var i = 0; i < agents.Length; i++)
        {
            var agent = agents[i];
            var id = string.IsNullOrEmpty(agent.id) ? $"agent_{i + 1}" : agent.id;
            var displayName = string.IsNullOrEmpty(agent.display_name) ? id : agent.display_name;

            var pos = new Vector3(
                UnityEngine.Random.Range(-spawnArea.x * 0.5f, spawnArea.x * 0.5f),
                spawnHeight,
                UnityEngine.Random.Range(-spawnArea.z * 0.5f, spawnArea.z * 0.5f)
            );

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Agent_{displayName}";
            cube.transform.position = pos;
            var scale = UnityEngine.Random.Range(boxScaleRange.x, boxScaleRange.y);
            cube.transform.localScale = Vector3.one * scale;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.Lerp(new Color(0.3f, 0.6f, 1f), Color.white, 0.2f * i);
            }

            agentObjects[id] = cube;
        }
    }

    private void TrySelectAgentFromClick()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit))
        {
            foreach (var pair in agentObjects)
            {
                if (pair.Value == hit.collider.gameObject)
                {
                    activeAgentId = pair.Key;
                    statusMessage = $"Aktiver Agent: {activeAgentId}";
                    return;
                }
            }
        }
    }

    private IEnumerator SendChat(string message)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            statusMessage = "Kein sessionId. Setup zuerst ausführen.";
            yield break;
        }

        if (string.IsNullOrEmpty(activeAgentId))
        {
            statusMessage = "Kein aktiver Agent ausgewählt.";
            yield break;
        }

        var url = $"{backendBaseUrl}/chat";
        var payload = new ChatRequest
        {
            session_id = sessionId,
            active_agent_id = activeAgentId,
            user_text = message
        };
        var json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            var body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                statusMessage = "Chat fehlgeschlagen: " + req.error;
                chatLog.Add(statusMessage + " | " + req.downloadHandler.text);
                yield break;
            }

            var resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
            sessionId = resp.session_id;
            activeAgentId = resp.active_agent_id;
            if (resp.events != null)
            {
                foreach (var ev in resp.events)
                {
                    chatLog.Add($"[{ev.agent_id}] {ev.text}");
                }
            }
        }
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        GUILayout.BeginArea(uiRect, GUI.skin.box);
        GUILayout.Label("Quick Agent Manager");
        GUILayout.Space(4);

        GUILayout.Label($"Status: {statusMessage}");
        GUILayout.Label($"Session: {sessionId}");
        GUILayout.Label($"Aktiv: {activeAgentId}");

        if (GUILayout.Button("Setup erneut vom Server"))
        {
            StartCoroutine(SetupFromServer());
        }

        GUILayout.Space(6);
        GUILayout.Label("Agenten wählen:");
        agentScroll = GUILayout.BeginScrollView(agentScroll, GUILayout.Height(120));
        if (lastAgents != null)
        {
            foreach (var agent in lastAgents)
            {
                var id = string.IsNullOrEmpty(agent.id) ? "(unbekannt)" : agent.id;
                var label = string.IsNullOrEmpty(agent.display_name) ? id : $"{agent.display_name} ({id})";
                if (GUILayout.Button(label))
                {
                    activeAgentId = id;
                    statusMessage = $"Aktiver Agent: {activeAgentId}";
                }
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.Label("Chat:");
        chatInput = GUILayout.TextField(chatInput);
        if (GUILayout.Button("Senden"))
        {
            if (!string.IsNullOrWhiteSpace(chatInput))
            {
                var toSend = chatInput;
                chatLog.Add($"[Du] {toSend}");
                chatInput = "";
                StartCoroutine(SendChat(toSend));
            }
        }

        chatScroll = GUILayout.BeginScrollView(chatScroll, GUILayout.Height(160));
        foreach (var line in chatLog)
        {
            GUILayout.Label(line);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.Label("Interaktion: Linksklick auf Box wählt Agenten.");
        GUILayout.EndArea();
    }
}
