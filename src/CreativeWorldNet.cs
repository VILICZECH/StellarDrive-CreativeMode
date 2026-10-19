using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using MelonLoader;
using UnityEngine;

namespace CreativeMode
{
    public struct CreativeWorldAsk : IBroadcast
    {
        public byte Unused;
    }

    public struct CreativeWorldSignal : IBroadcast
    {
        public byte Creative;
    }

    // The host's save holds creative.mode. Joined players do not have that folder, so the host tells them.
    static class CreativeWorldNet
    {
        static NetworkManager _manager;
        static float _nextAsk;

        static CreativeWorldNet()
        {
            GenericWriter<CreativeWorldAsk>.SetWrite((writer, value) => writer.WriteUInt8Unpacked(value.Unused));
            GenericReader<CreativeWorldAsk>.SetRead(reader => new CreativeWorldAsk { Unused = reader.ReadUInt8Unpacked() });
            GenericWriter<CreativeWorldSignal>.SetWrite((writer, value) => writer.WriteUInt8Unpacked(value.Creative));
            GenericReader<CreativeWorldSignal>.SetRead(reader => new CreativeWorldSignal { Creative = reader.ReadUInt8Unpacked() });
        }

        public static void Tick()
        {
            Ensure();
            if (!InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted || CreativeSession.RemoteKnown)
                return;
            if (Time.unscaledTime < _nextAsk)
                return;
            _nextAsk = Time.unscaledTime + 1f;
            InstanceFinder.ClientManager.Broadcast(new CreativeWorldAsk(), Channel.Reliable);
        }

        public static void Ensure()
        {
            NetworkManager manager = InstanceFinder.NetworkManager;
            if (manager == null || manager == _manager)
                return;
            _manager = manager;
            manager.ServerManager.RegisterBroadcast<CreativeWorldAsk>(OnAsk, false);
            manager.ClientManager.RegisterBroadcast<CreativeWorldSignal>(OnSignal);
            manager.ServerManager.OnRemoteConnectionState += OnRemote;
        }

        static void OnRemote(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (connection == null || args.ConnectionState != RemoteConnectionState.Started)
                return;
            Send(connection);
        }

        static void OnAsk(NetworkConnection connection, CreativeWorldAsk ask, Channel channel)
        {
            Send(connection);
        }

        static void OnSignal(CreativeWorldSignal signal, Channel channel)
        {
            if (InstanceFinder.IsServerStarted)
                return;
            bool creative = signal.Creative != 0;
            bool changed = !CreativeSession.RemoteKnown || CreativeSession.IsActive != creative;
            CreativeSession.ApplyRemote(creative);
            if (!changed)
                return;
            MelonLogger.Msg(creative
                ? "Creative world, from the host."
                : "Survival world, from the host.");
        }

        static void Send(NetworkConnection connection)
        {
            Ensure();
            if (connection == null || !InstanceFinder.IsServerStarted)
                return;
            InstanceFinder.ServerManager.Broadcast(connection, new CreativeWorldSignal
            {
                Creative = (byte)(CreativeSession.IsActive ? 1 : 0)
            }, false, Channel.Reliable);
        }
    }
}
