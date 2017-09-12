﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MQTTnet.Core.Adapter;
using MQTTnet.Core.Client;
using MQTTnet.Core.Packets;
using MQTTnet.Core.Serializer;

namespace MQTTnet.Core.Tests
{
    public class TestMqttCommunicationAdapter : IMqttCommunicationAdapter
    {
        private readonly BlockingCollection<MqttBasePacket> _incomingPackets = new BlockingCollection<MqttBasePacket>();

        public TestMqttCommunicationAdapter Partner { get; set; }

        public IMqttPacketSerializer PacketSerializer { get; } = new MqttPacketSerializer();

        public Task ConnectAsync(MqttClientOptions options, TimeSpan timeout)
        {
            return Task.FromResult(0);
        }

        public Task DisconnectAsync()
        {
            return Task.FromResult(0);
        }

        public Task SendPacketAsync(MqttBasePacket packet, TimeSpan timeout)
        {
            ThrowIfPartnerIsNull();

            Partner.SendPacketInternal(packet);
            return Task.FromResult(0);
        }

        public Task<MqttBasePacket> ReceivePacketAsync(TimeSpan timeout)
        {
            ThrowIfPartnerIsNull();

            return Task.Run(() => _incomingPackets.Take());
        }

        private void SendPacketInternal(MqttBasePacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));

            _incomingPackets.Add(packet);
        }

        private void ThrowIfPartnerIsNull()
        {
            if (Partner == null)
            {
                throw new InvalidOperationException("Partner is not set.");
            }
        }
    }
}