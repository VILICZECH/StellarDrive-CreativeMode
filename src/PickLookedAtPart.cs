using System;
using Core.Services;
using Core.Utils;
using HarmonyLib;
using MelonLoader;
using Players;
using Players.Interface.Model;
using Players.Interface.Services;
using Players.Services;
using Players.Visuals.Camera;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Tools.Build;
using Tools.Common.Hover;
using Tools.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldTracking.Interface.Values;

namespace CreativeMode
{
    static class PickLookedAtPart
    {
        const float Reach = 2f;
        static readonly RaycastHit[] Hits = new RaycastHit[32];
        static bool _hooked;

        static bool _down;
        static bool _legacyBroken;
        static string _note;
        static float _noteUntil;

        public static void EnsureHooked()
        {
            if (_hooked)
                return;
            _hooked = true;
            GameObject watcher = new GameObject("Build wheel");
            UnityEngine.Object.DontDestroyOnLoad(watcher);
            watcher.AddComponent<WheelIndicator>();
        }

        public static void Note(string text)
        {
            _note = text;
            _noteUntil = Time.unscaledTime + 2f;
            MelonLogger.Msg(text);
        }

        internal static void Tick()
        {
            if (!WheelPressed() || !StellarDriveMods.Shared.GameKeys.Free())
                return;
            try
            {
                if (!CreativeSession.IsActive)
                    return;
                if (!CameraRay(out CameraPhysicsData camera))
                {
                    Note("Wheel click. Camera was not found.");
                    return;
                }
                if (!Look(camera, out ushort partId))
                {
                    Note("Wheel click. No part within 2 m.");
                    return;
                }

                ILocalSelectedPartProvider parts = ServiceLocator.GetService<ILocalSelectedPartProvider>();
                if (parts == null)
                {
                    Note("Wheel click. Part selection is missing.");
                    return;
                }
                parts.SelectPart(partId);
                Note("Wheel click. Selected " + PartName(partId) + ".");
            }
            catch (Exception ex)
            {
                Note("Wheel click failed. " + ex.Message);
            }
        }

        static string PartName(ushort partId)
        {
            try
            {
                PartSettings settings = new PartSettingsProvider().GetPartSettingById(partId);
                if (settings != null && !string.IsNullOrEmpty(settings.fullLabel))
                    return settings.fullLabel;
            }
            catch (Exception)
            {
            }
            return partId.ToString();
        }

        static bool WheelPressed()
        {
            bool down = false;
            foreach (InputDevice device in InputSystem.devices)
            {
                Mouse mouse = device as Mouse;
                if (mouse != null && mouse.middleButton.isPressed)
                    down = true;
            }
            if (!down && !_legacyBroken)
            {
                try
                {
                    down = UnityEngine.Input.GetMouseButton(2);
                }
                catch (Exception)
                {
                    _legacyBroken = true;
                }
            }
            bool clicked = down && !_down;
            _down = down;
            return clicked;
        }

        static bool CameraRay(out CameraPhysicsData camera)
        {
            camera = default;
            PlayersClientTracker players = ServiceLocator.GetService<PlayersClientTracker>();
            if (players == null)
                players = UnityEngine.Object.FindAnyObjectByType<PlayersClientTracker>();
            TrackedPlayerLocalClient player = players != null ? players.LocalPlayer : null;
            if (player == null)
                return false;
            try
            {
                camera = Traverse.Create(player).Method("CalculateCameraPhysics", new object[] { ClientOrigin.LocalPhysicsSpaceId }).GetValue<CameraPhysicsData>();
            }
            catch (Exception)
            {
                return false;
            }
            return camera.Direction.sqrMagnitude > 0.0001f;
        }

        static bool Look(CameraPhysicsData camera, out ushort partId)
        {
            partId = 0;
            int mask = Layer("Default") | Layer("Ship") | Layer("WorldObject");
            int count = ClientScene.PhysicsScene.Raycast(camera.CameraPos, camera.Direction, Hits, Reach, mask, QueryTriggerInteraction.Ignore);
            bool[] used = new bool[count];
            for (int n = 0; n < count; n++)
            {
                int chosen = -1;
                float best = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (used[i] || Hits[i].collider == null)
                        continue;
                    if (Hits[i].distance < best)
                    {
                        best = Hits[i].distance;
                        chosen = i;
                    }
                }
                if (chosen < 0)
                    return false;

                used[chosen] = true;
                RaycastHit hit = Hits[chosen];
                if (hit.collider == null)
                    continue;
                if (hit.collider.GetComponentInParent<BuildToolLocalClient>() != null || hit.collider.GetComponentInParent<PartPreview>() != null)
                    continue;
                IHoveredObject hovered = IHoveredObject.BuildHoveredObject(hit, camera.Direction);
                if (hovered is HullHover hull && HullFromHover(hull, out partId))
                    return true;
                if (hovered is FloatingPartHover floating && FloatingFromHover(floating, out partId))
                    return true;
            }
            return false;
        }

        static int Layer(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer < 0 ? 0 : 1 << layer;
        }

        static bool HullFromHover(HullHover hull, out ushort partId)
        {
            partId = 0;
            Traverse fields = Traverse.Create(hull);
            HullPartPosition position = fields.Field("_hullPartPosition").GetValue<HullPartPosition>();
            IShipPartTargeter targeter = fields.Field("_currentShip").GetValue<IShipPartTargeter>();
            if (targeter == null || !Ship(targeter.ShipId, out TrackedShip tracked))
                return false;
            if (tracked.State.TryGetHullPartState(position, out HullPartNetworkedState part))
            {
                partId = part.PartId;
                return true;
            }

            return PartOnVoxel(tracked, position.VoxelCoord, position.Type, out partId);
        }

        static bool PartOnVoxel(TrackedShip tracked, Vector3Int voxel, VoxelPartType type, out ushort partId)
        {
            partId = 0;
            foreach (HullPart hull in tracked.State.GetHullPartObjects())
            {
                if (hull.Settings == null || hull.HullPlacement.Position.Type != type)
                    continue;
                foreach (Vector3Int occupied in hull.HullPlacement.CalculateAllCoords(hull.Settings.size))
                {
                    if (occupied != voxel)
                        continue;
                    partId = hull.Settings.id;
                    return true;
                }
            }

            return false;
        }

        static bool FloatingFromHover(FloatingPartHover floating, out ushort partId)
        {
            partId = 0;
            Collider collider = Traverse.Create(floating).Field("_partCollider").GetValue<Collider>();
            if (collider == null)
                return false;
            IShipFloatingPartId id = collider.GetComponentInParent<IShipFloatingPartId>();
            IShipPartTargeter targeter = collider.GetComponentInParent<IShipPartTargeter>();
            if (id == null || targeter == null || !Ship(targeter.ShipId, out TrackedShip tracked))
                return false;
            if (!tracked.State.TryGetPart(id.Id, out StatefulPart part) || part.Settings == null)
                return false;

            partId = part.Settings.id;
            return true;
        }

        static bool Ship(uint shipId, out TrackedShip tracked)
        {
            tracked = null;
            IShipsClientProvider ships = ServiceLocator.GetService<IShipsClientProvider>();
            if (ships == null || !ships.TryGetShip(shipId, out IShipStateReadClient client))
                return false;
            tracked = client as TrackedShip;
            return tracked != null && tracked.State != null;
        }
    }

    public class WheelIndicator : MonoBehaviour
    {
        void Update()
        {
            PickLookedAtPart.Tick();
            BuildUndo.Tick();
        }
    }
}
