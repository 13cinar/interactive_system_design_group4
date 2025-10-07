/*
Reference
Implementing a Basic TCP Server in Unity: A Step-by-Step Guide
By RabeeQiblawi Nov 20, 2023
https://medium.com/@rabeeqiblawi/implementing-a-basic-tcp-server-in-unity-a-step-by-step-guide-449d8504d1c5
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class TCP : MonoBehaviour
{
    const string hostIP = "0.0.0.0"; // Select your IP
    const int port = 50456; // Select your port
    TcpListener server = null;
    TcpClient client = null;
    NetworkStream stream = null;
    Thread thread;

    public Transform LHand;
    public Transform RHand;
    public Transform Head;

    // Define your own message
    [Serializable]
    public class Message
    {
        public float LHand_x;
        public float LHand_y;
        public float LHand_z;
        public float RHand_x;
        public float RHand_y;
        public float RHand_z;
        public float Head_x;
        public float Head_y;
        public float Head_z;
    }

    private float timer = 0;
    private static object Lock = new object();
    private List<Message> MessageQue = new List<Message>();


    private void Start()
    {
        thread = new Thread(new ThreadStart(SetupServer));
        thread.Start();
    }

    private void Update()
    {
        lock(Lock)
        {
            foreach (Message msg in MessageQue)
            {
                Move(msg);
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
            msg = JsonUtility.FromJson<Message>(json_string);
            return msg;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Err: " + ex.Message);
            Debug.LogError("Error formatting json: " + json_string);
            return null;
        }
    }

    public void Move(Message message)
    {
        LHand.localPosition = new Vector3(message.LHand_x, message.LHand_y, message.LHand_z);
        RHand.localPosition = new Vector3(message.RHand_x, message.RHand_y, message.RHand_z);
        Head.localPosition = new Vector3(message.Head_x, message.Head_y, message.Head_z);
        Debug.Log("Left Hand: " + LHand.position.ToString());
        Debug.Log("Right Hand: " + RHand.position.ToString());
        Debug.Log("Head: " + Head.position.ToString());
    }
}
