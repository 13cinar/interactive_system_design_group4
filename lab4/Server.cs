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
using System.Runtime.CompilerServices;

public class TCP : MonoBehaviour
{
    public static TCP Instance { get; private set; }

    const string hostIP = "0.0.0.0"; // Listen on all interfaces
    const int port = 13456;          // Port must match Python
    TcpListener server = null;
    TcpClient client = null;
    NetworkStream stream = null;
    Thread thread;
    private readonly StringBuilder recvBuffer = new StringBuilder();

    public Transform LHand;
    public Transform RHand;
    public Transform Head;

    public GameObject peasantMan;
    public event System.Action<int, Vector3> OnMarker;
    private struct MarkerEvent { public int id; public Vector3 pos; }
    private readonly Queue<MarkerEvent> eventQueue = new Queue<MarkerEvent>();



    // Demo message (kept for backward compatibility)
    // [Serializable]
    // public class Message
    // {
    //     public string type;
    //     public List<UMarker> mediapipe_markers = new List<UMarker>();
    // }

    // ArUco (raw) � for reference / debugging

    // [Serializable]
    // public class MediaPipeMarker
    // {
    //     public String name;
    //     public float X, Y, Z;
    // }

    // [Serializable]
    // public class TransformedMessage
    // {
    //     public List<MediaPipeMarker> trans_mediapipe_markers = new List<MediaPipeMarker>();
    // }
    [Serializable]
    public class ArucoMarker
    {
        public int id;
        public float X, Y, Z;
        // public float depth_m;
        // public int pixel_x, pixel_y;
    }

    [Serializable]
    public class ArucoFrame
    {
        public string type;// "anchors"　（ＵＮＩＴＹ　ｔｏ　ｃｌｉｅｎｔ）  OR　"aruco_unity" (ｃｌｉｅｎｔ　ｔｏ　ＵＮＩＴＹ）
        public double timestamp;
        public List<ArucoMarker> anchors; // "anchors"　（ＵＮＩＴＹ　ｔｏ　ｃｌｉｅｎｔ）  OR　"markers" (ｃｌｉｅｎｔ　ｔｏ　ＵＮＩＴＹ）
    }


    // {
    //     type: "aruco_unity",
    //     timestamp: 1234567890.123,
    //     anchors: [
    //         { id: 0, x: 1.0, y: 2.0, z: 3.0 },
    //         { id: 1, x: 4.0, y: 5.0, z: 6.0 }
    //     ]
    // }

    // Unity-space markers coming from Python after calibration
    [Serializable] public class UMarker { public int id; public float x, y, z; }
    [Serializable] public class UFrame { public string type; public double timestamp; public List<UMarker> markers; }

    // Apply movement on main thread
    private struct PoseUpdate { public int id; public Vector3 pos; }
    private readonly Queue<PoseUpdate> poseQueue = new Queue<PoseUpdate>();

    private float timer = 0;
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

        if (Time.time > timer)
        {
            SendAnchorsToClient();
            timer = Time.time + 0.5f;
        }

        lock (Lock)
        {
            while (poseQueue.Count > 0)
            {
                var u = poseQueue.Dequeue();
                UpdateModelPosition(u);
                // if (SpatialAnchorRegistry.anchorsById.TryGetValue(u.id, out var t) && t != null)
                // {
                //     // Many prefabs have the visible mesh/canvas as a child:
                //     var target = t.childCount > 0 ? t.GetChild(0) : t;
                //     target.position = u.pos;
                //     Debug.Log($"[Apply] moved {(target == t ? "root" : "child0")} for id {u.id} to {u.pos}");
                // }
                // else
                // {
                //     Debug.LogWarning($"[Apply] no anchor registered for id {u.id}. " +
                //                      $"Known IDs: {string.Join(",", SpatialAnchorRegistry.anchorsById.Keys)}");
                // }
            }
            while (eventQueue.Count > 0)
            {
                var e = eventQueue.Dequeue();
                OnMarker?.Invoke(e.id, e.pos);   // cart follower listens for id=5
            }
        }
    }
     // receive message
     // parse message
     // add to posequeue
    // TODO: update position
    // TODO: ping client for new position
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
                                            //bool have = SpatialAnchorRegistry.anchorsById.ContainsKey(m.id);
                                            Debug.Log($"[TCP] id {m.id} -> ({m.x:F3},{m.y:F3},{m.z:F3})");// registryHas={have}");
                                            lock (Lock)
                                            {
                                                poseQueue.Enqueue(new PoseUpdate
                                                {
                                                    id = m.id,
                                                    pos = new Vector3(m.x, m.y, m.z)
                                                });
                                                    eventQueue.Enqueue(new MarkerEvent { id = m.id, pos = new Vector3(m.x, m.y, m.z) });
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
                            Debug.Log($"[TCP] demo message: {demo.markers} markers");
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

    public void SendMessageToClient(ArucoFrame message)
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

    public string Encode(ArucoFrame message)
    {
        return JsonUtility.ToJson(message, false);
    }

    public UFrame Decode(string json_string)
    {
        return JsonUtility.FromJson<UFrame>(json_string);
    }

    private void UpdateModelPosition(PoseUpdate uMarker)
    {
        if (peasantMan == null)
        {
            Debug.LogWarning("PeasantMan model is not assigned.");
            return;
        }
        

        // Assuming the id corresponds to a specific body part
        switch (uMarker.id)
        {
            case 0: // Head
                Head.position = uMarker.pos;
                print("Head position updated to: " + uMarker.pos);
                break;
            case 1: // Left Hand
                LHand.position = uMarker.pos;
                print("Left Hand position updated to: " + uMarker.pos);
                break;
            case 2: // Right Hand
                RHand.position = uMarker.pos;
                print("Right Hand position updated to: " + uMarker.pos);
                break;
            default:
                return;
        }
    }

    private void SendAnchorsToClient()
    {
        // send pos of head, lhand, rhand as anchors
        if (client == null || stream == null || !client.Connected) return;
        try
        {
            ArucoFrame message = new ArucoFrame
            {
                type = "anchors",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                anchors = new List<ArucoMarker>
                {
                    new ArucoMarker { id = 0, X = Head.position.x, Y = Head.position.y, Z = Head.position.z },
                    new ArucoMarker { id = 1, X = LHand.position.x, Y = LHand.position.y, Z = LHand.position.z },
                    new ArucoMarker { id = 2, X = RHand.position.x, Y = RHand.position.y, Z = RHand.position.z }
                }
            };

            SendMessageToClient(message);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SendAnchorsToClient failed: " + e.Message);
        }
    }
}
