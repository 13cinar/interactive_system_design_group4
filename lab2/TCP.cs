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
        public string some_string;
        public int some_int;
        public float some_float;
    }

    private float timer = 0;
    private static object Lock = new object();  // lock to prevent conflict in main thread and server thread
    private List<Message> MessageQue = new List<Message>();


    private void Start()
    {
        thread = new Thread(new ThreadStart(SetupServer));
        thread.Start();
    }

    private void Update()
    {
        // Send message to client every 2 second
        if(Time.time > timer)
        {
            Message msg = new Message();
            msg.some_string = "From Server";
            msg.some_int = 1;
            msg.some_float = .1f;
            SendMessageToClient(msg);
            timer = Time.time + 2f;
        }
        // Process message que
        lock(Lock)
        {
            foreach (Message msg in MessageQue)
            {
                // Unity only allow main thread to modify GameObjects.
                // Spawn, Move, Rotate GameObjects here. 
                Debug.Log("Received Str: " + msg.some_string + " Int: " + msg.some_int + " Float: " + msg.some_float);
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
                while ((i = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    data = Encoding.UTF8.GetString(buffer, 0, i);
                    Message message = Decode(data);
                    // Add received message to que
                    lock(Lock)
                    {
                        MessageQue.Add(message);
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

    public void SendMessageToClient(Message message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(Encode(message));
        stream.Write(msg, 0, msg.Length);
        Debug.Log("Sent: " + message);
    }

    // Encode message from struct to Json String
    public string Encode(Message message)
    {
        return JsonUtility.ToJson(message, true);
    }

    // Decode messaage from Json String to struct
    public Message Decode(string json_string)
    {
        Message msg = JsonUtility.FromJson<Message>(json_string);
        return msg;
    }
}
