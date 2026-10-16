using System.Collections.Generic;
using Core.Services;
using Core.Utils;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Interface.Services;
using Players.Interface.Model;
using Players.LocalClient.Controls;
using Ships.Interface.Model;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using TMPro;
using Tools.Build;
using Tools.Build.Model;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldTracking.Interface.Values;

namespace CreativeMode
{
    static class BuildLine
    {
        const int MaxBlocks = 16;
        static readonly List<HullPartPlacement> Extra = new List<HullPartPlacement>();
        static readonly List<GameObject> Ghosts = new List<GameObject>();
        static RectTransform _countRoot;
        static TextMeshProUGUI _count;
        static bool _locked;
        static HullPartPlacement _first;
        static IShipPartPlacements _ship;
        static ushort _partId;
        static uint _shipId;
        static Vector3 _anchorPos;
        static Quaternion _anchorRot;
        static Vector3 _size;
        static int _shown;
        static int _blocks = 1;
        static Vector3Int _step;
        static int _scrollFrame = -1;

        public static bool Locked
        {
            get { return _locked; }
        }

        public static string Hint
        {
            get { return _locked ? "Scroll to length, click to place" : null; }
        }

        public static bool OnClick(BuildToolLocalClient tool)
        {
            if (_locked)
            {
                Confirm();
                return true;
            }
            if (!ShiftHeld() || !Capture(tool))
                return false;
            _locked = true;
            return true;
        }

        public static void Refresh(PartPreview preview)
        {
            if (!_locked || preview == null || _ship == null)
            {
                if (!_locked)
                    Clear();
                return;
            }

            GameObject block = Traverse.Create(preview).Field("_objectPreview").GetValue<GameObject>();
            if (block != null)
            {
                block.transform.position = _anchorPos;
                block.transform.rotation = _anchorRot;
                PaintValid(block);
            }

            Vector3Int step = LookStep(preview);
            List<HullPartPlacement> line = new List<HullPartPlacement>();
            Vector3Int coord = _first.Position.VoxelCoord;
            VoxelPartType type = _first.Position.Type;
            for (int i = 1; i < _blocks && step != Vector3Int.zero; i++)
            {
                coord += step;
                HullPartPosition position = new HullPartPosition(coord, type);
                if (_ship.ContainsHullPart(coord, type) || !_ship.IsHullPartAvailable(position))
                    break;
                line.Add(new HullPartPlacement(coord, type, _first.ForwardOrientation, _first.UpOrientation));
            }

            Extra.Clear();
            Extra.AddRange(line);
            if (block != null)
                Show(preview, block, line);
        }

        static bool Capture(BuildToolLocalClient tool)
        {
            PartPreview preview = tool.GetComponentInChildren<PartPreview>(true);
            if (preview == null)
                return false;
            Traverse fields = Traverse.Create(preview);
            if (!fields.Field("_isPlacementValid").GetValue<bool>())
                return false;
            GameObject block = fields.Field("_objectPreview").GetValue<GameObject>();
            PartSettings settings = fields.Field("_actualPreviewSettings").GetValue<PartSettings>();
            SnappedPreviewPlacement snapped = fields.Field("_snappedPlacement").GetValue<SnappedPreviewPlacement>();
            IShipPartPlacements ship = fields.Field("_ship").GetValue<IShipPartPlacements>();
            if (block == null || settings == null || snapped == null || ship == null || !snapped.HullPartPlacement.HasValue)
                return false;

            _first = snapped.HullPartPlacement.Value;
            _ship = ship;
            _partId = settings.id;
            _shipId = snapped.ShipId;
            _anchorPos = block.transform.position;
            _anchorRot = block.transform.rotation;
            _size = settings.size;
            _blocks = 1;
            _step = Vector3Int.zero;
            return true;
        }

        public static bool OnScroll(float value)
        {
            if (!_locked || Mathf.Abs(value) < 0.01f)
                return _locked;
            if (_scrollFrame == Time.frameCount)
                return true;
            _scrollFrame = Time.frameCount;
            if (value > 0f)
                _blocks = Mathf.Min(MaxBlocks, _blocks + 1);
            else
                _blocks = Mathf.Max(1, _blocks - 1);
            return true;
        }

        static void Confirm()
        {
            IShipInteractionNetworkService interaction = ServiceLocator.GetService<IShipInteractionNetworkService>();
            if (interaction != null)
            {
                List<BuildUndo.Piece> built = new List<BuildUndo.Piece>();
                PlaceHullPartNetworkRequest first = default;
                first.ShipId = _shipId;
                first.SettingsId = _partId;
                first.Placement = _first;
                interaction.PlaceHullPart(first);
                built.Add(BuildUndo.Piece.FromHull(_shipId, _partId, _first));
                for (int i = 0; i < Extra.Count; i++)
                {
                    PlaceHullPartNetworkRequest request = default;
                    request.ShipId = _shipId;
                    request.SettingsId = _partId;
                    request.Placement = Extra[i];
                    interaction.PlaceHullPart(request);
                    built.Add(BuildUndo.Piece.FromHull(_shipId, _partId, Extra[i]));
                }

                BuildUndo.Record(built);
            }
            _locked = false;
            Clear();
        }

        public static void Pin(PartPreview preview)
        {
            if (!_locked || preview == null)
                return;
            Traverse fields = Traverse.Create(preview);
            GameObject block = fields.Field("_objectPreview").GetValue<GameObject>();
            if (block == null)
                return;
            ICamera camera = fields.Field("_camera").GetValue<ICamera>();
            if (camera != null)
            {
                Vector3 relative = _anchorPos - camera.Position;
                fields.Field("_currentPreviewPos").SetValue(relative);
                fields.Field("_previewTargetPos").SetValue(relative);
            }
            block.transform.position = _anchorPos;
            block.transform.rotation = _anchorRot;
            fields.Field("_isPlacementValid").SetValue(true);
            PaintValid(block);
        }

        static Vector3Int LookStep(PartPreview preview)
        {
            ICamera camera = Traverse.Create(preview).Field("_camera").GetValue<ICamera>();
            if (camera == null || _ship == null)
                return _step;
            Vector3 local = Quaternion.Inverse(_ship.ToWorldRotation(Quaternion.identity)) * camera.Direction;
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);
            float max = Mathf.Max(ax, Mathf.Max(ay, az));
            float mid = ax + ay + az - max - Mathf.Min(ax, Mathf.Min(ay, az));
            if (max < 0.35f || (_step != Vector3Int.zero && max < mid + 0.2f))
                return _step;
            int sign;
            int axis;
            if (ax >= ay && ax >= az)
            {
                axis = 0;
                sign = local.x >= 0f ? 1 : -1;
            }
            else if (ay >= az)
            {
                axis = 1;
                sign = local.y >= 0f ? 1 : -1;
            }
            else
            {
                axis = 2;
                sign = local.z >= 0f ? 1 : -1;
            }
            Vector3Int span = Vector3Int.RoundToInt(_size - Vector3.one);
            Vector3Int rotated = PartOrientationHelper.RotateByOrientation(_first.ForwardOrientation, span);
            int stride = (axis == 0 ? Mathf.Abs(rotated.x) : axis == 1 ? Mathf.Abs(rotated.y) : Mathf.Abs(rotated.z)) + 1;
            _step = axis == 0
                ? new Vector3Int(sign * stride, 0, 0)
                : axis == 1
                    ? new Vector3Int(0, sign * stride, 0)
                    : new Vector3Int(0, 0, sign * stride);
            return _step;
        }

        static void PaintValid(GameObject block)
        {
            IShipHologramService hologram = ServiceLocator.GetService<IShipHologramService>();
            if (hologram != null && block != null)
                hologram.ChangeObjectStyleToHolographic(block, true);
        }

        static void Show(PartPreview preview, GameObject block, List<HullPartPlacement> line)
        {
            while (Ghosts.Count < line.Count)
            {
                GameObject ghost = Object.Instantiate(block);
                ghost.name = "Build line";
                Collider[] colliders = ghost.GetComponentsInChildren<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                    colliders[i].enabled = false;
                Ghosts.Add(ghost);
            }

            Vector3 origin = _ship.ToWorldPos(_first);
            for (int i = 0; i < Ghosts.Count; i++)
            {
                bool on = i < line.Count;
                Ghosts[i].SetActive(on);
                if (!on)
                    continue;
                Vector3 delta = _ship.ToWorldPos(line[i]) - origin;
                Ghosts[i].transform.rotation = _anchorRot;
                Ghosts[i].transform.position = _anchorPos + delta;
                PaintValid(Ghosts[i]);
            }

            _shown = line.Count + 1;
            PlaceCount(preview, _shown);
        }

        static void PlaceCount(PartPreview preview, int count)
        {
            BuildToolLocalClient tool = preview.GetComponentInParent<BuildToolLocalClient>();
            BuildToolScreenUpdater updater = tool != null ? tool.GetComponentInChildren<BuildToolScreenUpdater>(true) : null;
            if (updater == null)
            {
                HideCount();
                return;
            }

            Traverse fields = Traverse.Create(updater);
            Camera render = fields.Field("_renderCamera").GetValue<Camera>();
            GameObject shown = fields.Field("_currentObject").GetValue<GameObject>();
            if (render == null)
            {
                HideCount();
                return;
            }

            EnsureCount();
            if (_countRoot.parent != render.transform)
                _countRoot.SetParent(render.transform, false);
            SetLayer(_countRoot.gameObject, shown != null ? shown.layer : 0);
            float distance = Mathf.Max(render.nearClipPlane + 0.08f, 0.25f);
            float viewHeight = render.orthographic
                ? render.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(render.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _countRoot.localPosition = new Vector3(0f, 0f, distance);
            _countRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _countRoot.localScale = Vector3.one * (viewHeight * 0.42f / 80f);
            _count.text = count.ToString();
            _countRoot.gameObject.SetActive(true);
        }

        static void SetLayer(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayer(root.transform.GetChild(i).gameObject, layer);
        }

        static void EnsureCount()
        {
            if (_count != null)
                return;
            GameObject root = new GameObject("Build line count");
            root.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _countRoot = root.GetComponent<RectTransform>();
            _countRoot.sizeDelta = new Vector2(80f, 80f);
            GameObject label = new GameObject("Count");
            label.transform.SetParent(root.transform, false);
            _count = label.AddComponent<TextMeshProUGUI>();
            _count.alignment = TextAlignmentOptions.Center;
            _count.fontSize = 64f;
            _count.color = Color.white;
            _count.raycastTarget = false;
            _count.textWrappingMode = TextWrappingModes.NoWrap;
            _count.outlineWidth = 0.18f;
            _count.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            _count.rectTransform.anchorMin = Vector2.zero;
            _count.rectTransform.anchorMax = Vector2.one;
            _count.rectTransform.offsetMin = Vector2.zero;
            _count.rectTransform.offsetMax = Vector2.zero;
            if (TMP_Settings.defaultFontAsset != null)
                _count.font = TMP_Settings.defaultFontAsset;
            root.SetActive(false);
        }

        static void HideCount()
        {
            if (_countRoot != null)
                _countRoot.gameObject.SetActive(false);
        }

        static bool ShiftHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        static void Clear()
        {
            _locked = false;
            _ship = null;
            Extra.Clear();
            for (int i = 0; i < Ghosts.Count; i++)
            {
                if (Ghosts[i] != null)
                    Object.Destroy(Ghosts[i]);
            }
            Ghosts.Clear();
            HideCount();
            _shown = 0;
            _blocks = 1;
            _step = Vector3Int.zero;
        }
    }

    [HarmonyPatch(typeof(PlayerScrollControl), "OnScroll")]
    static class LineScroll
    {
        static bool Prefix(InputAction.CallbackContext context)
        {
            if (!BuildLine.Locked || !context.started)
                return true;
            float value = context.ReadValue<float>();
            if (Mathf.Abs(value) < 0.01f && Mouse.current != null)
                value = Mouse.current.scroll.ReadValue().y;
            BuildLine.OnScroll(value);
            return false;
        }
    }

    [HarmonyPatch(typeof(PartPreview), "UpdatePhysics")]
    static class ShowBuildLine
    {
        static void Postfix(PartPreview __instance)
        {
            BuildLine.Refresh(__instance);
            BuildLine.Pin(__instance);
        }
    }

    [HarmonyPatch(typeof(PartPreview), "Update")]
    static class HoldBuildLine
    {
        static void Postfix(PartPreview __instance)
        {
            BuildLine.Pin(__instance);
        }
    }

    [HarmonyPatch(typeof(BuildToolLocalClient), "PrimaryActionBegin")]
    static class LineBuildClick
    {
        static bool Prefix(BuildToolLocalClient __instance)
        {
            return !BuildLine.OnClick(__instance);
        }
    }
}
