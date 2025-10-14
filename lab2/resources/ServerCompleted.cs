/*
Reference
Implementing a Basic TCP Server in Unity: A Step-by-Step Guide
By RabeeQiblawi Nov 20, 2023
https://medium.com/@rabeeqiblawi/implementing-a-basic-tcp-server-in-unity-a-step-by-step-guide-449d8504d1c5
*/

using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;

public class TCPCompleted : MonoBehaviour
{
    public static TCPCompleted Instance { get; private set; }

    const string hostIP = "0.0.0.0"; // Listen on all interfaces
    const int port = 50555;          // Port must match Python
    TcpListener server = null;
    TcpClient client = null;
    NetworkStream stream = null;
    Thread thread;
    private readonly StringBuilder recvBuffer = new StringBuilder();

    // Demo message (kept for backward compatibility)
    [Serializable]
    public class Message
    {
        public string some_string;
        public int some_int;
        public float some_float;
    }

    // ArUco (raw) � for reference / debugging
    [Serializable]
    public class ArucoMarker
    {
        public int id;
        public float X, Y, Z;
        public float depth_m;
        public int pixel_x, pixel_y;
    }

    [Serializable]
    public class ArucoFrame
    {
        public string type;       // "aruco_frame"
        public double timestamp;
        public List<ArucoMarker> markers;
    }

    // Unity-space markers coming from Python after calibration
    [Serializable] public class UMarker { public int id; public float x, y, z; }
    [Serializable] public class UFrame { public string type; public double timestamp; public List<UMarker> markers; }

    // Apply movement on main thread
    private struct PoseUpdate { public int id; public Vector3 pos; }
    private readonly Queue<PoseUpdate> poseQueue = new Queue<PoseUpdate>();

    private static readonly object Lock = new object();

    void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        thread = new Thread(new ThreadStart(SetupServer));
        thread.IsBackground = true;
        thread.Start();
    }

    private void Update()
    {
        // Drain and apply queued poses (Unity objects must be touched on main thread)
        lock (Lock)
        {
            while (poseQueue.Count > 0)
            {
                var u = poseQueue.Dequeue();
                if (SpatialAnchorRegistry.anchorsById.TryGetValue(u.id, out var t) && t != null)
                {
                    // Many prefabs have the visible mesh/canvas as a child:
                    var target = t.childCount > 0 ? t.GetChild(0) : t;
                    target.position = u.pos;
                    Debug.Log($"[Apply] moved {(target == t ? "root" : "child0")} for id {u.id} to {u.pos}");
                }
                else
                {
                    Debug.LogWarning($"[Apply] no anchor registered for id {u.id}. " +
                                     $"Known IDs: {string.Join(",", SpatialAnchorRegistry.anchorsById.Keys)}");
                }
            }
        }
    }

    private void SetupServer()
    {
        try
        {
            IPAddress localAddr = IPAddress.Parse(hostIP);
            server = new TcpListener(localAddr, port);
            server.Start();
            Debug.Log($"[TCPCompleted] Server started, listening on {hostIP}:{port}");

            byte[] buffer = new byte[4096];

            while (true)
            {
                Debug.Log("[TCP] Waiting for connection...");
                client = server.AcceptTcpClient();
                Debug.Log("[TCP] Connected!");

                stream = client.GetStream();

                int i;
                while ((i = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    recvBuffer.Append(Encoding.UTF8.GetString(buffer, 0, i));

                    // Process complete newline-delimited JSON messages (NDJSON)
                    string all = recvBuffer.ToString();
                    int nl;
                    int start = 0;
                    while ((nl = all.IndexOf('\n', start)) >= 0)
                    {
                        string one = all.Substring(start, nl - start).Trim();
                        start = nl + 1;
                        if (one.Length == 0) continue;

                        // Always log the raw line while debugging transport
                        Debug.Log($"[TCP] line: {one}");

                        try
                        {
                            // Probe for .type to route correctly
                            var typeProbe = JsonUtility.FromJson<UFrame>(one);
                            if (typeProbe != null && !string.IsNullOrEmpty(typeProbe.type))
                            {
                                if (typeProbe.type == "aruco_unity")
                                {
                                    var uf = JsonUtility.FromJson<UFrame>(one);
                                    Debug.Log($"[TCP] aruco_unity received: {uf.markers?.Count ?? 0} markers");

                                    if (uf.markers != null)
                                    {
                                        foreach (var m in uf.markers)
                                        {
                                            bool have = SpatialAnchorRegistry.anchorsById.ContainsKey(m.id);
                                            Debug.Log($"[TCP] id {m.id} -> ({m.x:F3},{m.y:F3},{m.z:F3}) registryHas={have}");
                                            lock (Lock)
                                            {
                                                poseQueue.Enqueue(new PoseUpdate
                                                {
                                                    id = m.id,
                                                    pos = new Vector3(m.x, m.y, m.z)
                                                });
                                            }
                                        }
                                    }
                                    continue;
                                }

                                if (typeProbe.type == "aruco_frame")
                                {
                                    continue;
                                }
                            }

                            var demo = Decode(one);
                            Debug.Log($"[TCP] demo message: {demo.some_string}, {demo.some_int}, {demo.some_float}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[TCP] Unrecognized JSON. Raw: {one}\n{ex.Message}");
                        }
                    }

                    // Keep any remainder (partial JSON) in the buffer
                    recvBuffer.Length = 0;
                    if (start < all.Length) recvBuffer.Append(all.Substring(start));
                }

                client.Close();
            }
        }
        catch (SocketException e)
        {
            Debug.Log("SocketException: " + e);
        }
        finally
        {
            try { server?.Stop(); } catch { }
        }
    }

    private void OnApplicationQuit()
    {
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        try { server?.Stop(); } catch { }
        try { thread?.Abort(); } catch { }
    }

    public void SendJson(string json)
    {
        if (client == null || stream == null || !client.Connected) return;
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(json + "\n"); // newline for framing
            lock (Lock) { stream.Write(data, 0, data.Length); }
        }
        catch (Exception e)
        {
            Debug.LogWarning("SendJson failed: " + e.Message);
        }
    }

    public void SendMessageToClient(Message message)
    {
        if (client == null || stream == null || !client.Connected) return;
        try
        {
            string payload = Encode(message) + "\n";
            byte[] msg = Encoding.UTF8.GetBytes(payload);
            stream.Write(msg, 0, msg.Length);
            Debug.Log("Sent: " + payload);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SendMessageToClient failed: " + e.Message);
        }
    }

    public string Encode(Message message)
    {
        return JsonUtility.ToJson(message, true);
    }

    public Message Decode(string json_string)
    {
        return JsonUtility.FromJson<Message>(json_string);
    }
}
