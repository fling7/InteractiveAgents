using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
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
    private const string ChatInputControlName = "chatInputField";
    private Vector2 agentScroll;
    private Vector2 chatScroll;
    private Vector2 uiScroll;

    private void Start()
    {
        EnsureSceneBasics();
        StartCoroutine(SetupFromServer());
    }

    private void Update()
    {
        if (TryGetSelectPosition(out var screenPosition))
        {
            TrySelectAgentFromClick(screenPosition);
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

    private bool TryGetSelectPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }
#endif

        screenPosition = default;
        return false;
    }

    private void TrySelectAgentFromClick(Vector2 screenPosition)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        var ray = cam.ScreenPointToRay(screenPosition);
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
            AppendChatEvents(resp.events);
        }
    }

    private void AppendChatEvents(ChatEvent[] events)
    {
        if (events == null)
        {
            return;
        }

        foreach (var ev in events)
        {
            var agentLabel = string.IsNullOrWhiteSpace(ev.agent_id) ? "System" : ev.agent_id;
            if (!string.IsNullOrWhiteSpace(ev.type))
            {
                agentLabel = $"{agentLabel}/{ev.type}";
            }

            var text = NormalizeChatText(ev.text);
            chatLog.Add($"[{agentLabel}] {text}");
        }
    }

    private string NormalizeChatText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var normalized = text.Replace("\\n", "\n").Trim();
        if (normalized.StartsWith("{") && normalized.EndsWith("}"))
        {
            var extracted = TryExtractSayFromJson(normalized);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }
        }

        var jsonStart = normalized.IndexOf('{');
        var jsonEnd = normalized.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonCandidate = normalized.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var extracted = TryExtractSayFromJson(jsonCandidate);
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                return extracted.Trim();
            }
        }

        return normalized;
    }

    private string TryExtractSayFromJson(string json)
    {
        try
        {
            var parsed = JsonUtility.FromJson<StructuredNpcReply>(json);
            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.say))
            {
                return parsed.say;
            }
            if (parsed != null)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(parsed.antwort))
                {
                    parts.Add(parsed.antwort.Trim());
                }
                if (!string.IsNullOrWhiteSpace(parsed.rueckfrage))
                {
                    var followUp = parsed.rueckfrage.Trim();
                    if (!string.IsNullOrWhiteSpace(followUp))
                    {
                        parts.Add($"Rückfrage: {followUp}");
                    }
                }
                if (parts.Count > 0)
                {
                    return string.Join("\n\n", parts);
                }
            }
        }
        catch
        {
            // Ignore JSON parse failures and fall back to raw text.
        }

        return null;
    }

    private void OnGUI()
    {
        if (!showUi)
        {
            return;
        }

        var maxWidth = Mathf.Min(uiRect.width, Screen.width - uiRect.x - 10f);
        var maxHeight = Mathf.Min(uiRect.height, Screen.height - uiRect.y - 10f);
        var clampedRect = new Rect(uiRect.x, uiRect.y, maxWidth, maxHeight);

        GUILayout.BeginArea(clampedRect, GUI.skin.box);
        uiScroll = GUILayout.BeginScrollView(uiScroll);
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
        GUI.SetNextControlName(ChatInputControlName);
        chatInput = GUILayout.TextField(chatInput);
        if (Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return
                || Event.current.keyCode == KeyCode.KeypadEnter
                || Event.current.character == '\n'
                || Event.current.character == '\r')
            && GUI.GetNameOfFocusedControl() == ChatInputControlName)
        {
            TrySendChatFromInput();
            Event.current.Use();
        }
        if (GUILayout.Button("Senden"))
        {
            TrySendChatFromInput();
        }
        if (GUILayout.Button("Chat leeren"))
        {
            chatLog.Clear();
        }

        chatScroll = GUILayout.BeginScrollView(chatScroll, GUILayout.Height(160));
        var chatText = string.Join("\n", chatLog);
        GUILayout.TextArea(chatText, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        GUILayout.Label("Interaktion: Linksklick auf Box wählt Agenten.");
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void TrySendChatFromInput()
    {
        if (string.IsNullOrWhiteSpace(chatInput))
        {
            return;
        }

        var toSend = chatInput.Trim();
        chatLog.Add($"[Du] {toSend}");
        chatInput = "";
        StartCoroutine(SendChat(toSend));
    }

    [System.Serializable]
    private class StructuredNpcReply
    {
        public string say;
        public string handoff_to;
        public string handoff_reason;
        public float confidence;
        public string antwort;
        public string rueckfrage;
    }
}
