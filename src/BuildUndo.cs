using System.Collections.Generic;
using Core.Services;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Interface.Services;
using Ships;
using Ships.Interface.Model;
using Ships.Cables;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using TMPro;
using Tools.Build;
using Tools.Build.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CreativeMode
{
    static class BuildUndo
    {
        const int MaxSteps = 64;
        static readonly List<Step> UndoSteps = new List<Step>();
        static readonly List<Step> RedoSteps = new List<Step>();
        static readonly Dictionary<uint, uint> CableIds = new Dictionary<uint, uint>();
        static int _watch;
        static bool _applying;
        static int _quietFrame = -1;
        static string _note;
        static float _noteUntil;

        public static bool Applying
        {
            get { return _applying || _quietFrame == Time.frameCount || _quietFrame + 1 == Time.frameCount; }
        }

        public static bool WatchingCables
        {
            get { return _watch > 0 && !Applying; }
        }

        public static string Note
        {
            get { return Time.unscaledTime < _noteUntil ? _note : null; }
        }

        public struct Piece
        {
            public bool Floating;
            public uint ShipId;
            public ushort SettingsId;
            public HullPartPlacement Hull;
            public FloatingPartPlacement Float;

            public static Piece FromHull(uint shipId, ushort settingsId, HullPartPlacement placement)
            {
                Piece piece = default;
                piece.ShipId = shipId;
                piece.SettingsId = settingsId;
                piece.Hull = placement;
                return piece;
            }

            public static Piece FromFloating(uint shipId, ushort settingsId, FloatingPartPlacement placement)
            {
                Piece piece = default;
                piece.Floating = true;
                piece.ShipId = shipId;
                piece.SettingsId = settingsId;
                piece.Float = placement;
                return piece;
            }
        }

        public static void ClearHistory()
        {
            UndoSteps.Clear();
            RedoSteps.Clear();
            _note = null;
            _noteUntil = 0f;
        }

        public static void Record(Piece piece)
        {
            Record(new[] { piece });
        }

        public static void Record(IList<Piece> pieces)
        {
            if (!CreativeSession.IsActive || Applying || pieces == null || pieces.Count == 0)
                return;
            Step step = new Step();
            for (int i = 0; i < pieces.Count; i++)
                step.Pieces.Add(pieces[i]);
            Push(step);
        }

        public static void RecordRemoval(Piece piece)
        {
            if (!CreativeSession.IsActive || Applying)
                return;
            Step step = new Step();
            step.Removal = true;
            step.Pieces.Add(piece);
            Push(step);
        }

        public static void WatchCables(bool on)
        {
            if (on)
                _watch++;
            else if (_watch > 0)
                _watch--;
        }

        public static void RecordCableCreated(uint id, CableState state)
        {
            if (!WatchingCables)
                return;
            Step step = new Step();
            step.Cable = new CableChange();
            step.Cable.Id = id;
            step.Cable.Created = true;
            step.Cable.After = state;
            Push(step);
        }

        public static void RecordCableUpdated(uint id, CableState before, CableState after)
        {
            if (!WatchingCables)
                return;
            Step step = new Step();
            step.Cable = new CableChange();
            step.Cable.Id = id;
            step.Cable.Before = before;
            step.Cable.After = after;
            Push(step);
        }

        public static void RecordCableDeleted(uint id, CableState state)
        {
            if (!WatchingCables)
                return;
            Step step = new Step();
            step.Cable = new CableChange();
            step.Cable.Id = id;
            step.Cable.Deleted = true;
            step.Cable.Before = state;
            Push(step);
        }

        public static void Tick()
        {
            if (!CreativeSession.IsActive || !StellarDriveMods.Shared.GameKeys.Free())
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            if (!ctrl || keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                return;
            KeyControl undo = keyboard.FindKeyOnCurrentKeyboardLayout("z");
            KeyControl redo = keyboard.FindKeyOnCurrentKeyboardLayout("y");
            bool undoPressed = undo != null ? undo.wasPressedThisFrame : keyboard.zKey.wasPressedThisFrame;
            bool redoPressed = redo != null ? redo.wasPressedThisFrame : keyboard.yKey.wasPressedThisFrame;
            if (undoPressed)
                Undo();
            else if (redoPressed)
                Redo();
        }

        static void Undo()
        {
            if (UndoSteps.Count == 0)
            {
                Show("Nothing to undo");
                return;
            }

            Step step = UndoSteps[UndoSteps.Count - 1];
            UndoSteps.RemoveAt(UndoSteps.Count - 1);
            Apply(step, false);
            RedoSteps.Add(step);
            Show(step.Pieces.Count > 1 ? "Undone " + step.Pieces.Count : "Undone");
        }

        static void Redo()
        {
            if (RedoSteps.Count == 0)
            {
                Show("Nothing to redo");
                return;
            }

            Step step = RedoSteps[RedoSteps.Count - 1];
            RedoSteps.RemoveAt(RedoSteps.Count - 1);
            Apply(step, true);
            UndoSteps.Add(step);
            Show(step.Pieces.Count > 1 ? "Redone " + step.Pieces.Count : "Redone");
        }

        static void Apply(Step step, bool redo)
        {
            _applying = true;
            _quietFrame = Time.frameCount;
            try
            {
                if (step.Cable != null)
                {
                    if (redo)
                        RedoCable(step.Cable);
                    else
                        UndoCable(step.Cable);
                    return;
                }

                bool place = redo ^ step.Removal;
                IShipInteractionNetworkService interaction = ServiceLocator.GetService<IShipInteractionNetworkService>();
                if (interaction == null)
                    return;
                if (place)
                {
                    for (int i = 0; i < step.Pieces.Count; i++)
                        Place(interaction, step.Pieces[i]);
                }
                else
                {
                    for (int i = step.Pieces.Count - 1; i >= 0; i--)
                        Remove(interaction, step.Pieces[i]);
                }
            }
            finally
            {
                _applying = false;
            }
        }

        static void Push(Step step)
        {
            RedoSteps.Clear();
            UndoSteps.Add(step);
            if (UndoSteps.Count > MaxSteps)
                UndoSteps.RemoveAt(0);
        }

        static void UndoCable(CableChange change)
        {
            CablesServerTracker cables = Object.FindAnyObjectByType<CablesServerTracker>();
            if (cables == null)
                return;
            uint id = CurrentCable(change.Id);
            if (change.Created)
                cables.DeleteCable(id);
            else if (change.Deleted)
            {
                uint fresh = cables.AddCable(change.Before);
                CableIds[change.Id] = fresh;
                change.Id = fresh;
            }
            else
                cables.UpdateCableState(id, change.Before);
        }

        static void RedoCable(CableChange change)
        {
            CablesServerTracker cables = Object.FindAnyObjectByType<CablesServerTracker>();
            if (cables == null)
                return;
            uint id = CurrentCable(change.Id);
            if (change.Deleted)
            {
                cables.DeleteCable(id);
                return;
            }
            if (change.Created)
            {
                uint fresh = cables.AddCable(change.After);
                CableIds[change.Id] = fresh;
                change.Id = fresh;
                return;
            }
            cables.UpdateCableState(id, change.After);
        }

        static uint CurrentCable(uint id)
        {
            uint next;
            int guard = 0;
            while (CableIds.TryGetValue(id, out next) && next != id && guard < 8)
            {
                id = next;
                guard++;
            }
            return id;
        }

        static void Remove(IShipInteractionNetworkService interaction, Piece piece)
        {
            if (!TryShip(piece.ShipId, out TrackedShip ship))
                return;
            if (!piece.Floating)
            {
                if (!ship.State.ContainsHullPart(piece.Hull.Position))
                    return;
                if (!ship.State.TryGetHullPartState(piece.Hull.Position, out HullPartNetworkedState state) || state.PartId != piece.SettingsId)
                    return;
                RemoveHullPartNetworkRequest request = default;
                request.ShipId = piece.ShipId;
                request.Position = piece.Hull.Position;
                interaction.RemoveHullPart(request);
                return;
            }

            if (!TryFloatingId(ship, piece, out ushort partId))
                return;
            RemoveFloatingPartNetworkRequest floating = default;
            floating.ShipId = piece.ShipId;
            floating.PartId = partId;
            interaction.RemoveFloatingPart(floating);
        }

        static void Place(IShipInteractionNetworkService interaction, Piece piece)
        {
            if (!piece.Floating)
            {
                if (TryShip(piece.ShipId, out TrackedShip ship) && ship.State.ContainsHullPart(piece.Hull.Position))
                    return;
                PlaceHullPartNetworkRequest request = default;
                request.ShipId = piece.ShipId;
                request.SettingsId = piece.SettingsId;
                request.Placement = piece.Hull;
                interaction.PlaceHullPart(request);
                return;
            }

            PlaceFloatingPartNetworkRequest floating = default;
            floating.ShipId = piece.ShipId;
            floating.SettingsId = piece.SettingsId;
            floating.Placement = piece.Float;
            interaction.PlaceFloatingPart(floating);
        }

        static bool TryFloatingId(TrackedShip ship, Piece piece, out ushort partId)
        {
            partId = 0;
            foreach (StatefulPart part in ship.StatefulParts)
            {
                if (part.Settings == null || part.Settings.id != piece.SettingsId || part.Placement == null)
                    continue;
                IPartPlacement placement = part.Placement;
                if (placement.ForwardOrientation != piece.Float.ForwardOrientation || placement.UpOrientation != piece.Float.UpOrientation)
                    continue;
                if ((placement.LocalPosition - piece.Float.LocalPosition).sqrMagnitude > 0.0025f)
                    continue;
                partId = part.Id;
                return true;
            }

            return false;
        }

        static bool TryShip(uint shipId, out TrackedShip ship)
        {
            ship = null;
            IShipsClientProvider ships = ServiceLocator.GetService<IShipsClientProvider>();
            if (ships == null || !ships.TryGetShip(shipId, out IShipStateReadClient client))
                return false;
            ship = client as TrackedShip;
            return ship != null && ship.State != null;
        }

        static void Show(string text)
        {
            _note = text;
            _noteUntil = Time.unscaledTime + 1.6f;
        }

        class Step
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public bool Removal;
            public CableChange Cable;
        }

        sealed class CableChange
        {
            public uint Id;
            public bool Created;
            public bool Deleted;
            public CableState Before;
            public CableState After;
        }
    }

    [HarmonyPatch(typeof(BuildToolLocalClient), "PlacePart")]
    static class RecordPlacedPart
    {
        static void Postfix(SnappedPreviewPlacement snappedPlacement)
        {
            if (!CreativeSession.IsActive || snappedPlacement == null)
                return;
            PartSettings settings = snappedPlacement.PartSettings;
            if (settings == null)
                return;
            if (snappedPlacement.HullPartPlacement.HasValue)
            {
                BuildUndo.Record(BuildUndo.Piece.FromHull(snappedPlacement.ShipId, settings.id, snappedPlacement.HullPartPlacement.Value));
                return;
            }

            if (snappedPlacement.FloatingPartPlacement.HasValue)
                BuildUndo.Record(BuildUndo.Piece.FromFloating(snappedPlacement.ShipId, settings.id, snappedPlacement.FloatingPartPlacement.Value));
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___AddNewCable_ServerRPC___1176123958")]
    static class WatchNewCable
    {
        static void Prefix()
        {
            BuildUndo.WatchCables(true);
        }

        static void Postfix()
        {
            BuildUndo.WatchCables(false);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___PickUpCable_ServerRPC___1176123958")]
    static class WatchPickupCable
    {
        static void Prefix()
        {
            BuildUndo.WatchCables(true);
        }

        static void Postfix()
        {
            BuildUndo.WatchCables(false);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___PlugInCable_ServerRPC___1176123958")]
    static class WatchPlugCable
    {
        static void Prefix()
        {
            BuildUndo.WatchCables(true);
        }

        static void Postfix()
        {
            BuildUndo.WatchCables(false);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___DropCable_ServerRPC___328543758")]
    static class WatchDropCable
    {
        static void Prefix()
        {
            BuildUndo.WatchCables(true);
        }

        static void Postfix()
        {
            BuildUndo.WatchCables(false);
        }
    }

    [HarmonyPatch(typeof(CablesServerTracker), "AddCable")]
    static class RecordNewCable
    {
        static void Postfix(CableState cableState, uint __result)
        {
            BuildUndo.RecordCableCreated(__result, cableState);
        }
    }

    [HarmonyPatch(typeof(CablesServerTracker), "UpdateCableState")]
    static class RecordCablePlug
    {
        static void Prefix(CablesServerTracker __instance, uint cableId, out CableState __state)
        {
            __state = default;
            if (!BuildUndo.WatchingCables)
                return;
            __instance.TryGetCableState(cableId, out __state);
        }

        static void Postfix(uint cableId, CableState state, CableState __state)
        {
            if (!BuildUndo.WatchingCables)
                return;
            BuildUndo.RecordCableUpdated(cableId, __state, state);
        }
    }

    [HarmonyPatch(typeof(CablesServerTracker), "DeleteCable")]
    static class RecordDroppedCable
    {
        static void Prefix(CablesServerTracker __instance, uint id, out CableState __state)
        {
            __state = default;
            if (!BuildUndo.WatchingCables)
                return;
            __instance.TryGetCableState(id, out __state);
        }

        static void Postfix(uint id, CableState __state)
        {
            if (!BuildUndo.WatchingCables)
                return;
            BuildUndo.RecordCableDeleted(id, __state);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___RemoveHullPart_ServerRPC___930806135")]
    static class RecordHullRemoval
    {
        static void Prefix(RemoveHullPartNetworkRequest __0)
        {
            if (!CreativeSession.IsActive || BuildUndo.Applying)
                return;
            IShipsClientProvider ships = ServiceLocator.GetService<IShipsClientProvider>();
            if (ships == null || !ships.TryGetShip(__0.ShipId, out IShipStateReadClient client))
                return;
            TrackedShip tracked = client as TrackedShip;
            if (tracked == null || tracked.State == null)
                return;
            foreach (HullPart hull in tracked.State.GetHullPartObjects())
            {
                if (hull.Settings == null)
                    continue;
                if (hull.HullPlacement.Position.VoxelCoord != __0.Position.VoxelCoord || hull.HullPlacement.Position.Type != __0.Position.Type)
                    continue;
                BuildUndo.RecordRemoval(BuildUndo.Piece.FromHull(__0.ShipId, hull.Settings.id, hull.HullPlacement));
                return;
            }
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___RemoveFloatingPart_ServerRPC___1471205784")]
    static class RecordFloatingRemoval
    {
        static void Prefix(RemoveFloatingPartNetworkRequest __0)
        {
            if (!CreativeSession.IsActive || BuildUndo.Applying)
                return;
            IShipsClientProvider ships = ServiceLocator.GetService<IShipsClientProvider>();
            if (ships == null || !ships.TryGetShip(__0.ShipId, out IShipStateReadClient client))
                return;
            TrackedShip tracked = client as TrackedShip;
            if (tracked == null || tracked.State == null || !tracked.State.TryGetPart(__0.PartId, out StatefulPart part) || part.Settings == null)
                return;
            if (!(part.Placement is FloatingPartPlacement placement))
                return;
            BuildUndo.RecordRemoval(BuildUndo.Piece.FromFloating(__0.ShipId, part.Settings.id, placement));
        }
    }
}
