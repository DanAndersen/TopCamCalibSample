using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using UnityEngine;
using System.Net.Sockets;
using System.Text;

// based on tutorial at https://github.com/WorldOfZero/Data-Sphere/blob/master/Assets/DataSphere/Scripts/Stream/NamedServerStream.cs
public class TopDownCalibManager : MonoBehaviour {

    public int Port = 4434;

    Thread _tcpListenerThread;

    const int READ_BUFFER_SIZE = 1048576;

    private Queue<string> incomingMessageQueue = new Queue<string>();
    private object queueLock = new System.Object();

    // Use this for initialization
    void Start () {
        Debug.Log("Listening for top-down calib messages on port " + Port);
        _tcpListenerThread = new Thread(() => ListenForMessages(Port));
        _tcpListenerThread.Start();
    }
	
	// Update is called once per frame
	void Update () {
        Queue<string> tempqueue;
        lock (queueLock)
        {
            tempqueue = incomingMessageQueue;
            incomingMessageQueue = new Queue<string>();
        }
        foreach (var msg in tempqueue)
        {
            Debug.Log(String.Format("Read Message from top-down camera helper: {0}", msg));

            // can do something with the incoming data in this thread
        }
	}

    public void ListenForMessages(int port)
    {
        TcpListener server = null;
        try
        {
            IPAddress localAddr = IPAddress.Parse("127.0.0.1");

            server = new TcpListener(localAddr, port);

            // start listening for client requests.
            server.Start();

            Byte[] bytes = new Byte[READ_BUFFER_SIZE];
            String data = null;

            // enter the listening loop
            while (true)
            {
                Debug.Log(String.Format("Waiting for a connection on port {0}...", port));

                // perform a blocking call to accept requests.
                using (TcpClient client = server.AcceptTcpClient())
                {
                    Debug.Log("Remote client connected");

                    data = null;

                    // get a stream object for reading/writing
                    NetworkStream stream = client.GetStream();

                    int i;

                    // loop to receive all data sent by client
                    while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        // translate data bytes into (UTF8) string
                        data = System.Text.Encoding.UTF8.GetString(bytes, 0, i);
                        Debug.Log(String.Format("Received: {0}", data));

                        // here we could transform the incoming data into another format
                        lock (queueLock)
                        {
                            incomingMessageQueue.Enqueue(data);
                        }

                        // here we could send back a response
                    }
                }
            }
        }
        catch (SocketException e)
        {
            Debug.LogError(String.Format("SocketException: {0}", e));
        }
        finally
        {
            // stop listening for new clients
            server.Stop();
        }
    }
}
