﻿using System;
using System.Collections.Generic;
using System.IO;

using System.Net;
using System.Timers;
using System.Threading.Tasks;

using MessagePack;

namespace Snowball
{
    public class ComClient : IDisposable
    {
        public bool IsOpened { get; protected set; }

        public bool acceptBeacon = false;
        public bool AcceptBeacon { get { return acceptBeacon; } set { acceptBeacon = value; } }

        string userName = "u001";
        public string UserName { get { return userName; } set { userName = value; } }

        int listenPortNumber = 59902;
        public int ListenPortNumber { get { return listenPortNumber; } set { if (!IsOpened) listenPortNumber = value; } }

        int sendPortNumber = 59901;
        public int SendPortNumber { get { return sendPortNumber; } set { if (!IsOpened) sendPortNumber = value; } }

        int maxPacketSize = 4096;
        public int MaxPacketSize { get { return maxPacketSize; } set { if (!IsOpened) maxPacketSize = value; } }

        public delegate void ConnectedHandler(ComNode node);
        public ConnectedHandler OnConnected;

        public delegate void DisconnectedHandler(ComNode node);
        public DisconnectedHandler OnDisconnected;

        protected Dictionary<short, DataChannel> dataChannelMap = new Dictionary<short, DataChannel>();

        public delegate bool BeaconAcceptFunc(string data);
        BeaconAcceptFunc BeaconAccept = (data) => { return true; };

        public void SetBeaconAcceptFunction(BeaconAcceptFunc func) { BeaconAccept = func; }

        ComNode serverNode;
        TCPConnection connection;

        UDPSender udpSender;
        UDPReceiver udpReceiver;

        TCPConnector tcpConnector;

        int healthLostCount = 0;

        int maxHealthLostCount = 5;
        public int MaxHealthLostCount { get { return maxHealthLostCount; } set { maxHealthLostCount = value; } }

        bool isConnecting = false;

        public bool IsConnected { get{ lock (this) { return serverNode != null; } } }

        public ComClient()
        {
            IsOpened = false;

            AddChannel(new DataChannel((short)PreservedChannelId.Login, QosType.Reliable, CompressionType.None, (endPointIp, deserializer) =>
            {
            }));

            AddChannel(new DataChannel((short)PreservedChannelId.Health, QosType.Reliable, CompressionType.None, (endPointIp, data) =>
            {
                healthLostCount = 0;
            }));
        }

        public void Dispose()
        {
            Close();
        }

        public virtual void Open()
        {
            if (IsOpened) return;

            udpSender = new UDPSender(sendPortNumber);
            udpReceiver = new UDPReceiver(listenPortNumber);

            tcpConnector = new TCPConnector(sendPortNumber);
            tcpConnector.OnConnected += OnConnectedInternal;

            udpReceiver.OnReceive += OnUDPReceived;

            udpReceiver.Start();

            IsOpened = true;

            HealthCheck();
        }

        public virtual void Close()
        {
            if (!IsOpened) return;

            Disconnect();

            udpReceiver.Close();

            tcpConnector = null;
            udpReceiver = null;
            udpSender = null;

            IsOpened = false;
        }

        public async Task HealthCheck()
        {
            healthLostCount = 0;

            while (IsOpened)
            {
                await Task.Delay(500);

                if(IsConnected)
                {
                    byte dummy = 0;

                    SendData((short)PreservedChannelId.Health, dummy);

                    healthLostCount++;
                    if (healthLostCount > MaxHealthLostCount)
                    {
                        Disconnect();
                        break;
                    }
                }

            }
        }

        public void AddChannel(DataChannel channel)
        {
            dataChannelMap.Add(channel.ChannelID, channel);
        }

        public void RemoveChannel(DataChannel channel)
        {
            dataChannelMap.Remove(channel.ChannelID);
        }

        public bool Connect(string ip)
        {
            if (!isConnecting && !IsConnected)
            {
                isConnecting = true;
                tcpConnector.Connect(ip);
                return true;
            }
            return false;
        }

        void OnConnectedInternal(TCPConnection connection)
        {
            this.connection = connection;
            serverNode = new ComNode(connection.IP);

            connection.OnDisconnected = OnDisconnectedInternal;
            connection.OnReceive = OnTCPReceived;

            SendData((short)PreservedChannelId.Login, UserName);

            isConnecting = false;

            if (OnConnected != null) OnConnected(serverNode);

            Util.Log("Client:Connected");
        }

        public bool Disconnect()
        {
            if (connection != null)
            {
                connection.Disconnect();
                connection = null;
                return true;
            }
            else return false;
        }

        void OnDisconnectedInternal(TCPConnection connection)
        {
       
            if (OnDisconnected != null) OnDisconnected(serverNode);

            serverNode = null;
            connection = null;

            Util.Log("Client:Disconnected");
        }

        void OnUDPReceived(string endPointIp, byte[] data, int size)
        {
            int head = 0;

            while (head < size)
            {
                short datasize = BitConverter.ToInt16(data, head + 0);
                short channelId = BitConverter.ToInt16(data, head + 2);

                if (channelId == (short)PreservedChannelId.Beacon)
                {
                    if (acceptBeacon && !isConnecting && !IsConnected)
                    {
                        MemoryStream stream = new MemoryStream(data, head + 4, (int)datasize);
                        string beaconData = (string)MessagePackSerializer.Typeless.Deserialize(stream);

                        if(BeaconAccept(beaconData)) Connect(endPointIp);
                    }
                }
                else if (!dataChannelMap.ContainsKey(channelId))
                {
                }
                else
                {
                    if (endPointIp != serverNode.IP) continue;

                    DataChannel channel = dataChannelMap[channelId];
                    MemoryStream stream = new MemoryStream(data, head + 4, (int)datasize);

                    object container = null;

                    if (channel.Compression == CompressionType.LZ4)
                    {
                        container = LZ4MessagePackSerializer.Typeless.Deserialize(stream);
                    }
                    else
                    {
                        container = MessagePackSerializer.Typeless.Deserialize(stream);
                    }

                    channel.Received(serverNode, container);
                   
                }

                head += datasize + 4;

            }

          
        }

        void OnTCPReceived(string endPointIp, byte[] data, int size)
        {
            int head = 0;

            while (head < size)
            {
                short datasize = BitConverter.ToInt16(data, head + 0);
                short channelId = BitConverter.ToInt16(data, head + 2);

                if (channelId == (short)PreservedChannelId.Beacon)
                {
                }
                else if (!dataChannelMap.ContainsKey(channelId))
                {
                }
                else
                {
                    if (endPointIp != serverNode.IP) continue;

                    DataChannel channel = dataChannelMap[channelId];
                    MemoryStream stream = new MemoryStream(data, head + 4, (int)datasize);

                    object container = null;

                    if (channel.Compression == CompressionType.LZ4)
                    {
                        container = LZ4MessagePackSerializer.Typeless.Deserialize(stream);
                    }
                    else
                    {
                        container = MessagePackSerializer.Typeless.Deserialize(stream);
                    }

                    channel.Received(serverNode, container);
                }

                head += datasize + 4;

            }
        }

        public bool SendData<T>(short channelId, T data)
        {
            if (!dataChannelMap.ContainsKey(channelId)) return false;

            DataChannel channel = dataChannelMap[channelId];

            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            writer.Write((short)0);
            writer.Write(channelId);

            int pos = (int)stream.Position;

            if (channel.Compression == CompressionType.LZ4)
            {
                LZ4MessagePackSerializer.Typeless.Serialize(stream, data);
            }
            else
            {
                MessagePackSerializer.Typeless.Serialize(stream, data);
            }

            int maxpos = (int)stream.Position;

            stream.Position = 0;

            writer.Write((short)(maxpos - pos));

            if (channel.Qos == QosType.Reliable)
            {
                connection.Send(maxpos, stream.GetBuffer());
            }
            else if (channel.Qos == QosType.Unreliable)
            {
                udpSender.Send(serverNode.IP, maxpos, stream.GetBuffer());
            }

            return true;
        }
    }

}