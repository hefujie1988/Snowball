﻿using System;
using Gtk;

using Snowball;

public partial class MainWindow : Gtk.Window
{

    ComClient client = new ComClient();

    public MainWindow() : base(Gtk.WindowType.Toplevel)
    {
        Build();

        buttonSend.Clicked += (sender, e) =>
        {
            if(textviewInput.Buffer.Text.Length > 0)
            {
                client.Send(0, textviewInput.Buffer.Text);
                textviewInput.Buffer.Text = "";
            }

        };

        client.OnConnected += (node) =>
        {
            textviewInput.Sensitive = true;
            buttonSend.Sensitive = true;
        };

        client.OnDisconnected += (node) =>
        {
            textviewInput.Sensitive = false;
            buttonSend.Sensitive = false;
        };

        client.AddChannel(new DataChannel<string>(0, QosType.Reliable, Compression.None, (endPointIp, data) => { OnReceive(data); }));
        client.AcceptBeacon = true;
        client.Open();
    }

    protected void OnDeleteEvent(object sender, DeleteEventArgs a)
    {
        client.Close();

        Application.Quit();
        a.RetVal = true;
    }

    void OnReceive(string text)
    {
        textviewDisplay.Buffer.Text += text + "\n";
    }

}
