using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;

namespace CreativeMode
{
    public struct UndoSignal : IBroadcast
    {
        public byte Kind;
    }

    // The cable change itself stays on the host. Each client only keeps a marker and asks for its own step back.
    static class BuildUndoNet
    {
        public const byte Commit = 1;
        public const byte Reject = 2;
        public const byte UndoCable = 3;
        public const byte RedoCable = 4;
        public const byte ClearRedo = 5;

        static NetworkManager _manager;

        static BuildUndoNet()
        {
            GenericWriter<UndoSignal>.SetWrite((writer, value) => writer.WriteUInt8Unpacked(value.Kind));
            GenericReader<UndoSignal>.SetRead(reader => new UndoSignal { Kind = reader.ReadUInt8Unpacked() });
        }

        public static void Ensure()
        {
            NetworkManager manager = InstanceFinder.NetworkManager;
            if (manager == null || manager == _manager)
                return;
            _manager = manager;
            manager.ServerManager.RegisterBroadcast<UndoSignal>(OnAsk, false);
            manager.ClientManager.RegisterBroadcast<UndoSignal>(OnTell);
            manager.ServerManager.OnRemoteConnectionState += OnRemote;
        }

        public static void Ask(byte kind)
        {
            Ensure();
            if (!InstanceFinder.IsClientStarted)
                return;
            InstanceFinder.ClientManager.Broadcast(new UndoSignal { Kind = kind }, Channel.Reliable);
        }

        public static void Tell(NetworkConnection connection, byte kind)
        {
            Ensure();
            if (connection == null || !InstanceFinder.IsServerStarted)
                return;
            InstanceFinder.ServerManager.Broadcast(connection, new UndoSignal { Kind = kind }, false, Channel.Reliable);
        }

        static void OnAsk(NetworkConnection connection, UndoSignal signal, Channel channel)
        {
            if (connection == null || !CreativeSession.IsActive)
                return;
            if (signal.Kind != UndoCable && signal.Kind != RedoCable && signal.Kind != ClearRedo)
                return;
            BuildUndo.ServerCable(connection.ClientId, signal.Kind);
        }

        static void OnTell(UndoSignal signal, Channel channel)
        {
            if (signal.Kind == Commit)
                BuildUndo.OnCableCommit();
            else if (signal.Kind == Reject)
                BuildUndo.OnCableReject();
        }

        static void OnRemote(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (connection == null || args.ConnectionState == RemoteConnectionState.Started)
                return;
            BuildUndo.ForgetRemote(connection.ClientId);
        }
    }
}
