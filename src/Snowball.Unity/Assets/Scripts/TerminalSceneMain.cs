﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Snowball;

public class TerminalSceneMain : MonoBehaviour
{
    [SerializeField]
    Terminal server;

    [SerializeField]
    Terminal client;

    [SerializeField]
    GameObject serverObject;

    [SerializeField]
    GameObject clientObject;

    // Start is called before the first frame update
    void Start()
    {
        if (Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.OSXPlayer ||
            Application.platform == RuntimePlatform.LinuxPlayer)
        {
            Screen.SetResolution(1280, 1024, false);
        }

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        server.AddChannel(new DataChannel<ObjState>(0, QosType.Unreliable, Snowball.Compression.LZ4, (node, data) => {
            serverObject.transform.localPosition = data.Position;
            serverObject.transform.localRotation = data.Rotation;
        }, CheckMode.Speedy));

        client.AddChannel(new DataChannel<ObjState>(0, QosType.Unreliable, Snowball.Compression.LZ4, (node, data) => {
        }, CheckMode.Speedy));


        //client.AddAcceptList("127.0.0.1");
        client.Open();

        //server.AddAcceptList("127.0.0.1");
        server.Open();

    }


    float time = 0.0f;
    ComNode lnode = new ComNode("127.0.0.1");


    // Update is called once per frame
    void Update()
    {
        clientObject.transform.localPosition = new Vector3(0.0f, Mathf.Sin(time), 0.0f);

        time += 0.1f;

        ObjState state = new ObjState(clientObject.transform.localPosition, clientObject.transform.localRotation);

        for (int i = 0; i < 70; i++)
        {
            client.Send(lnode, 0, state);
        }

    }

}
