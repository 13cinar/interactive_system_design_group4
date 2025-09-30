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
using Unity.VisualScripting;
using TMPro;

public class TCP : MonoBehaviour
{
    const string hostIP = "0.0.0.0"; // Select your IP
    const int port = 54750; // Select your port
    TcpListener server = null;
    TcpClient client = null;
    NetworkStream stream = null;
    Thread thread;


    // Define your own message
    [Serializable]
    public class Message
    {
        public String resp;
        public int id;

        public float x, y, z;
    }
    public class MessageContainer
    {
        public Message msg;
    }

    private float timer = 0;
    private static object Lock = new object();  // lock to prevent conflict in main thread and server thread
    private List<Message> MessageQue = new List<Message>();

    // Creating ArUco Markers
    public GameObject realSenseParentObject;
    public GameObject rsMarkerPrefab;
    private Dictionary<int, GameObject> arucoObjects = new Dictionary<int, GameObject>();
    private int createdMarkers = 0;

    private void Start()
    {
        thread = new Thread(new ThreadStart(SetupServer));
        thread.Start();
    }

    private void Update()
    {
        // Send message to client every 2 second
        if (Time.time > timer)
        {
            Message msg = new Message();
            msg.resp = "From Server";
            msg.id = 0;
            msg.x = 0;
            msg.y = 0;
            msg.z = 0;
            //     msg.position_y = 1;
            //  msg.position_z = 1;

            SendMessageToClient(msg);
            timer = Time.time + 2f;
        }
        // Dissapear original anchors
        if (createdMarkers == 3)
        {
            for (int i = 0; i < createdMarkers; i++)
            {
                var el = GameObject.Find($"TempAnchor_{i}");
                if (el != null)
                {
                    el.SetActive(false);
                }
            }
            createdMarkers = 0;
        }
        // Process message que
        lock (Lock)
        {
            foreach (Message msg in MessageQue)
            {
                // Unity only allow main thread to modify GameObjects.
                // Spawn, Move, Rotate GameObjects here. 
                Debug.LogWarning("Received str: " + msg.resp + " ID: " + msg.id + " coords: " + msg.x + "X, " + msg.y + "Y, " + msg.z + "Z, ");
                if (msg.resp == "aruco_pose")
                {
                    int id = msg.id;
                    Vector3 p = new Vector3(msg.x, msg.y, msg.z);

                    //The marker needs to be created & modified here as indicated
                    if (!arucoObjects.TryGetValue(id, out var go) || go == null)
                    {
                        go = CreateMarker(p, id);
                        go.name = $"ArUco_{id}";
                        arucoObjects[id] = go;
                    }
                    else
                    {
                        //Improvement: Check if values exceed our sensibility threshold (Epsilon Comparison/Tolerance check)
                        bool a = epsilonCheck(go.transform.position.x, p.x);
                        bool b = epsilonCheck(go.transform.position.y, p.y);
                        bool c = epsilonCheck(go.transform.position.z, p.z);
                        if (a || b || c)
                            go.transform.position = p;
                    }
                }

            }
            MessageQue.Clear();
        }
    }

    private void SetupServer()
    {
        try
        {
            IPAddress localAddr = IPAddress.Parse(hostIP);
            server = new TcpListener(localAddr, port);
            server.Start();

            byte[] buffer = new byte[1024];
            string data = null;

            while (true)
            {
                Debug.Log("Waiting for connection...");
                client = server.AcceptTcpClient();
                Debug.Log("Connected!");

                data = null;
                stream = client.GetStream();

                // Receive message from client    
                int i;
                string bufferedData = "";
                while ((i = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    data = Encoding.UTF8.GetString(buffer, 0, i);
                    //Debug.LogWarning("data is:" + data);

                    //partially read and store new data
                    bufferedData += data;
                    // 1 or more complete messages read, splitting
                    while (bufferedData.Contains("\n"))
                    {
                        // 1st find first ocurrence of our msg separator, 
                        int tempidx = bufferedData.IndexOf("\n");
                        string tempToSend = bufferedData.Substring(0, tempidx);

                        // Decode and add message to our valid list of messages
                        handleSingleMessage(tempToSend);
                        // cut string and continue
                        bufferedData = bufferedData.Substring(tempidx +1);
                    }
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
            server.Stop();
        }
    }

    private void OnApplicationQuit()
    {
        stream.Close();
        client.Close();
        server.Stop();
        thread.Abort();
    }
    private void handleSingleMessage(string input) {
        Message message = Decode(input);
        if (message != null)
            // Add received message to que
            lock (Lock)
            {
                MessageQue.Add(message);
            }
    }
    public void SendMessageToClient(Message message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(Encode(message));
        stream.Write(msg, 0, msg.Length);
        //Debug.Log("Sent: " + message);
    }

    // Encode message from struct to Json String
    public string Encode(Message message)
    {
        return JsonUtility.ToJson(message, true);
    }

    // Decode messaage from Json String to struct
    public Message Decode(string json_string)
    {
        Message msg;
        try
        {
            msg = JsonUtility.FromJson<MessageContainer>(json_string).msg;
            return msg;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Err: " + ex.Message);
            Debug.LogError("Error formatting json: " + json_string);
            return null;
        }
    }

    private GameObject CreateMarker(Vector3 pos, int id)
    {
        GameObject go;
        go = Instantiate(rsMarkerPrefab, pos, Quaternion.identity, realSenseParentObject.transform);
        go.name = $"ArUco_{id}";
        createdMarkers += 1;
        return go;
    }

    private bool epsilonCheck(float a, float b)
    {
        float E = 0.01F;
        if (Mathf.Abs(a - b) > E)
            return true;
        else return false;
    }

    public void SendAnchorCreated(int id, Vector3 pos)
    {
        Message msg = new Message
        {
            resp = "anchor_created",
            id = id,
            x = pos.x,
            y = pos.y,
            z = pos.z
        };
        SendMessageToClient(msg);
    }
}