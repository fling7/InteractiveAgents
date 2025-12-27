\
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class BackendClient : MonoBehaviour
{
    [Header("Backend")]
    public string backendBaseUrl = "http://127.0.0.1:8787";

    [Serializable]
    public class Vector3Data { public float x; public float y; public float z; }

    [Serializable]
    public class SetupRequestPaths
    {
        public string room_plan_path;
        public string agents_path;
        public string session_id; // optional
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
        public string type;     // "say"
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

    public IEnumerator Health(Action<string> onOk, Action<string> onErr)
    {
        var url = $"{backendBaseUrl}/health";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) onErr?.Invoke(req.error);
            else onOk?.Invoke(req.downloadHandler.text);
        }
    }

    public IEnumerator SetupFromPaths(string roomPlanPath, string agentsPath, Action<SetupResponse> onOk, Action<string> onErr)
    {
        var url = $"{backendBaseUrl}/setup";
        var payload = new SetupRequestPaths { room_plan_path = roomPlanPath, agents_path = agentsPath };
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
                onErr?.Invoke(req.error + " | " + req.downloadHandler.text);
                yield break;
            }
            var resp = JsonUtility.FromJson<SetupResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }
    }

    public IEnumerator Chat(string sessionId, string activeAgentId, string userText, Action<ChatResponse> onOk, Action<string> onErr)
    {
        var url = $"{backendBaseUrl}/chat";
        var payload = new ChatRequest { session_id = sessionId, active_agent_id = activeAgentId, user_text = userText };
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
                onErr?.Invoke(req.error + " | " + req.downloadHandler.text);
                yield break;
            }
            var resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
            onOk?.Invoke(resp);
        }
    }
}
