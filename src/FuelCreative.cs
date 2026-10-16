using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using Core.Services;
using HarmonyLib;
using Items.Interface.Service;
using Items.Model;
using Players.Interface.Model;
using Players.Interface.Services;
using Ships;
using Ships.Cables;
using Ships.Cables.Fluids;
using Ships.Cables.Power;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.State;
using Ships.Interface.Services;
using Ships.Parts.EthanolDistiller;
using Ships.Parts.WoodPowerGenerator;
using Ships.Parts.WoodThruster;
using Ships.Services;
using TMPro;
using UI.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CreativeMode
{
    static class InfiniteFuel
    {
        static readonly HashSet<ulong> Parts = new HashSet<ulong>();

        public static ulong Key(uint shipId, ushort partId)
        {
            return ((ulong)shipId << 16) | partId;
        }

        public static bool IsOn(uint shipId, ushort partId)
        {
            return Parts.Contains(Key(shipId, partId));
        }

        public static void Clear()
        {
            Parts.Clear();
        }

        public static bool Toggle(uint shipId, ushort partId)
        {
            ulong key = Key(shipId, partId);
            if (!Parts.Add(key))
            {
                Parts.Remove(key);
                return false;
            }

            return true;
        }
    }

    static class FuelActions
    {
        public static bool TryGetOpenPart(out uint shipId, out ushort partId, out IStatefulPartState state)
        {
            shipId = 0;
            partId = 0;
            state = null;
            IPlayersProviderClient players = ServiceLocator.GetService<IPlayersProviderClient>();
            IShipsClientProvider ships = ServiceLocator.GetService<IShipsClientProvider>();
            if (players == null || ships == null || !players.TryGetLocalPlayerState(out PlayerState player))
                return false;

            shipId = player.EntityId;
            partId = player.PartId;
            return ships.TryGetPartState(new PartContext(shipId, partId), out state);
        }

        public static bool UsesWood(IStatefulPartState state)
        {
            return state is WoodThrusterState || state is WoodPowerGeneratorState || state is EthanolDistillerState;
        }

        public static void Refill(uint shipId, ushort partId)
        {
            ShipsServerTracker tracker = ServiceLocator.GetService<ShipsServerTracker>();
            if (tracker == null || !tracker.TryGetTrackedShip(shipId, out TrackedShipServer ship))
                return;
            if (!tracker.TryGetStatefulPart(new PartContext(shipId, partId), out StatefulPart part))
                return;

            InventorySlot slot = FuelSupply.Full();

            if (part.State is WoodThrusterState thruster)
                ship.UpdatePartState(partId, thruster with { ItemSlot = slot, PartialItemConsumptionLevel = 0f });
            else if (part.State is WoodPowerGeneratorState generator)
                ship.UpdatePartState(partId, generator with { ItemSlot = slot, PartialItemConsumptionLevel = 0f });
            else if (part.State is EthanolDistillerState distiller)
                ship.UpdatePartState(partId, distiller.WithUpdatedSlot(0, slot) is EthanolDistillerState filled
                    ? filled with { PartialBiomass = 0f }
                    : distiller);

            StorageInventoryProvider storage = ServiceLocator.GetService<StorageInventoryProvider>();
            if (storage != null)
                storage.OnShipStorageChange(new PartContext(shipId, partId));
            FuelButtons.ShowWood(slot);
        }
    }

    [HarmonyPatch(typeof(StorageMenuActivator), "ShowMenu")]
    static class ShowFuelControls
    {
        static void Postfix(StorageMenuActivator __instance)
        {
            if (!CreativeSession.IsActive)
                return;

            GameObject menu = Traverse.Create(__instance).Field("_currentMenu").GetValue<GameObject>();
            if (menu == null || !FuelActions.TryGetOpenPart(out uint shipId, out ushort partId, out IStatefulPartState state))
                return;
            if (!FuelActions.UsesWood(state))
                return;

            FuelButtons.Attach(menu, shipId, partId);
        }
    }

    static class FuelSupply
    {
        public static InventorySlot Full()
        {
            return new InventorySlot
            {
                HasItem = true,
                Quantity = Stack(),
                ItemId = ItemsConstants.WoodItemId
            };
        }

        static byte Stack()
        {
            IItemSettingsProvider items = ServiceLocator.GetService<IItemSettingsProvider>();
            if (items == null)
                return 20;

            ItemSettings settings = items.GetItemSettingsById(ItemsConstants.WoodItemId);
            if (settings == null || settings.maxStackSize == 0)
                return 20;
            return settings.maxStackSize;
        }
    }

    static class FuelButtons
    {
        static Transform _input;
        static GameObject _menu;
        static RectTransform _frame;
        static FuelIconButton _toggle;
        static FuelIconButton _refill;

        public static void Realign()
        {
            if (_frame == null || _toggle == null || _refill == null)
                return;
            Transform slot = InputSlot();
            if (slot != null && !slot.gameObject.activeInHierarchy)
                return;
            PlaceOutside(_frame, _menu, _toggle, _refill);
        }

        public static void ShowWood(InventorySlot slot)
        {
            if (_input == null)
                return;
            InventorySlotUI view = _input.GetComponent<InventorySlotUI>();
            if (view == null)
                view = _input.GetComponentInChildren<InventorySlotUI>(true);
            if (view != null)
                view.UpdateSlot(slot);
        }

        public static void Attach(GameObject menu, uint shipId, ushort partId)
        {
            RectTransform frame = FrameOf(menu);
            if (frame == null)
                return;

            _menu = menu;
            _input = FindInput(menu);
            Canvas canvas = menu.GetComponentInParent<Canvas>();
            Transform parent = canvas != null ? canvas.transform : menu.transform;
            ClearButtons(parent);

            DestroySlotUI trash = Object.FindAnyObjectByType<DestroySlotUI>();
            FuelIconButton toggle = Create(trash, parent, "InfiniteFuel", Icon(false));
            FuelIconButton refill = Create(trash, parent, "RefillFuel", PlusIcon());
            bool on = InfiniteFuel.IsOn(shipId, partId);
            toggle.SetIcon(Icon(on));
            toggle.SetPressed(on);
            SetInputVisible(!on);
            toggle.Click = () =>
            {
                bool next = InfiniteFuel.Toggle(shipId, partId);
                toggle.SetIcon(Icon(next));
                toggle.SetPressed(next);
                SetInputVisible(!next);
                if (next)
                    FuelActions.Refill(shipId, partId);
            };
            refill.Click = () =>
            {
                if (InfiniteFuel.IsOn(shipId, partId))
                    return;
                FuelActions.Refill(shipId, partId);
            };

            _frame = frame;
            _toggle = toggle;
            _refill = refill;
            Canvas.ForceUpdateCanvases();
            PlaceOutside(frame, menu, toggle, refill);
            toggle.gameObject.AddComponent<FuelAlign>();
        }

        static void ClearButtons(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == "InfiniteFuel" || child.name == "RefillFuel")
                    Object.Destroy(child.gameObject);
            }
        }

        static void SetInputVisible(bool visible)
        {
            Transform slot = InputSlot();
            if (slot != null)
                slot.gameObject.SetActive(visible);
            if (_menu == null)
                return;

            TMP_Text[] labels = _menu.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (slot != null && labels[i].transform.IsChildOf(slot))
                    continue;
                string text = labels[i].text;
                if (string.IsNullOrEmpty(text) || text.Length > 24)
                    continue;
                if (text.IndexOf("Wood", StringComparison.OrdinalIgnoreCase) < 0
                    && text.IndexOf("Bois", StringComparison.OrdinalIgnoreCase) < 0
                    && text.IndexOf("Biomass", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                labels[i].gameObject.SetActive(visible);
            }
        }

        static Transform InputSlot()
        {
            if (_input == null)
                return null;
            InventorySlotUI view = _input.GetComponent<InventorySlotUI>();
            if (view == null)
                view = _input.GetComponentInParent<InventorySlotUI>();
            return view != null ? view.transform : _input;
        }

        static Transform FindInput(GameObject menu)
        {
            InventorySlotUI[] slots = menu.GetComponentsInChildren<InventorySlotUI>(true);
            if (slots.Length == 1)
                return slots[0].transform;
            if (slots.Length > 1)
                return SmallestSlot(slots).transform;

            RawImage[] icons = menu.GetComponentsInChildren<RawImage>(true);
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i].GetComponentInParent<DestroySlotUI>() != null)
                    continue;
                return icons[i].transform;
            }

            return null;
        }

        static InventorySlotUI SmallestSlot(InventorySlotUI[] slots)
        {
            InventorySlotUI best = slots[0];
            float bestArea = float.MaxValue;
            for (int i = 0; i < slots.Length; i++)
            {
                RectTransform rect = slots[i].GetComponent<RectTransform>();
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area > 0f && area < bestArea)
                {
                    best = slots[i];
                    bestArea = area;
                }
            }

            return best;
        }

        static RectTransform FrameOf(GameObject menu)
        {
            RectTransform best = menu.transform as RectTransform;
            float bestArea = 0f;
            Image[] images = menu.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].GetComponentInParent<InventorySlotUI>() != null)
                    continue;
                RectTransform rect = images[i].rectTransform;
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area > bestArea)
                {
                    best = rect;
                    bestArea = area;
                }
            }

            return best;
        }

        static void PlaceOutside(RectTransform frame, GameObject menu, FuelIconButton toggle, FuelIconButton refill)
        {
            Vector3[] corners = new Vector3[4];
            frame.GetWorldCorners(corners);
            Vector3 right = (corners[2] + corners[3]) * 0.5f;
            float height = TrashSize(Object.FindAnyObjectByType<DestroySlotUI>(), 48f);
            float gap = height * 0.28f;
            Vector3 anchor = right + frame.right * (gap + height * 0.5f);
            anchor.y = SlotCenterY(menu);
            toggle.Rect.position = anchor;
            refill.Rect.position = anchor + frame.up * (height + gap);
            toggle.Rect.SetAsLastSibling();
            refill.Rect.SetAsLastSibling();
        }

        static float SlotCenterY(GameObject menu)
        {
            RectTransform visual = InputSlot() as RectTransform;
            if (visual == null)
                visual = _input as RectTransform;
            if (visual == null)
                visual = SquareSlot(menu);
            if (visual == null)
                return 0f;

            return VisibleFuelCenter(visual).y;
        }

        static Vector3 VisibleFuelCenter(RectTransform slot)
        {
            Vector3 slotCenter = CenterOf(slot);
            InventorySlotUI view = slot.GetComponent<InventorySlotUI>();
            if (view == null)
                view = slot.GetComponentInChildren<InventorySlotUI>(true);
            if (view == null)
                return slotCenter;

            RawImage icon = Traverse.Create(view).Field("icon").GetValue<RawImage>();
            if (icon == null)
                return slotCenter;

            float height = WorldHeight(icon.rectTransform);
            float width = WorldWidth(icon.rectTransform);
            if (height < 8f || width < 8f)
                return slotCenter;

            Vector3 iconCenter = CenterOf(icon.rectTransform);
            float slotHeight = WorldHeight(slot);
            float slotWidth = WorldWidth(slot);
            if (slotHeight > slotWidth * 1.15f || Mathf.Abs(iconCenter.y - slotCenter.y) > 6f)
                return iconCenter;
            return slotCenter;
        }

        static Vector3 CenterOf(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        static RectTransform SquareSlot(GameObject menu)
        {
            RectTransform best = null;
            float bestAspect = 10f;
            Image[] images = menu.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                RectTransform rect = images[i].rectTransform;
                float height = WorldHeight(rect);
                float width = WorldWidth(rect);
                if (height < 12f || width < 12f)
                    continue;
                float aspect = width > height ? width / height : height / width;
                if (aspect < bestAspect)
                {
                    best = rect;
                    bestAspect = aspect;
                }
            }

            return best;
        }

        static float WorldWidth(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Vector3.Distance(corners[0], corners[3]);
        }

        static float WorldHeight(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Vector3.Distance(corners[0], corners[1]);
        }

        static float TrashSize(DestroySlotUI trash, float slotHeight)
        {
            if (trash == null)
                return slotHeight * 0.72f;

            Vector3[] corners = new Vector3[4];
            trash.GetComponent<RectTransform>().GetWorldCorners(corners);
            float height = Vector3.Distance(corners[0], corners[1]);
            if (height < 8f || height > 160f)
                return 48f;
            return height;
        }

        static FuelIconButton Create(DestroySlotUI trash, Transform parent, string name, Sprite sprite)
        {
            float worldSize = TrashSize(trash, 48f);
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            float scale = Mathf.Abs(rect.lossyScale.y);
            if (scale < 0.0001f)
                scale = 1f;
            float local = worldSize / scale;
            rect.sizeDelta = new Vector2(local, local);

            Image background = go.GetComponent<Image>();
            background.color = new Color(0.22f, 0.33f, 0.42f, 1f);
            background.raycastTarget = true;
            if (trash != null)
            {
                Image trashBackground = trash.GetComponent<Image>();
                if (trashBackground != null)
                {
                    background.sprite = trashBackground.sprite;
                    background.type = trashBackground.type;
                    background.color = trashBackground.color;
                }
            }

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(go.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.16f, 0.16f);
            iconRect.anchorMax = new Vector2(0.84f, 0.84f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = Color.white;

            GameObject highlightObject = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
            highlightObject.transform.SetParent(go.transform, false);
            RectTransform highlightRect = highlightObject.GetComponent<RectTransform>();
            highlightRect.anchorMin = Vector2.zero;
            highlightRect.anchorMax = Vector2.one;
            highlightRect.offsetMin = Vector2.zero;
            highlightRect.offsetMax = Vector2.zero;
            Image highlightImage = highlightObject.GetComponent<Image>();
            highlightImage.color = new Color(1f, 1f, 1f, 0.28f);
            highlightImage.raycastTarget = false;
            highlightObject.SetActive(false);

            GameObject plateObject = new GameObject("Plate", typeof(RectTransform), typeof(Image));
            plateObject.transform.SetParent(go.transform, false);
            RectTransform plateRect = plateObject.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0.16f, 0.16f);
            plateRect.anchorMax = new Vector2(0.84f, 0.84f);
            plateRect.offsetMin = Vector2.zero;
            plateRect.offsetMax = Vector2.zero;
            Image plate = plateObject.GetComponent<Image>();
            plate.raycastTarget = false;
            plate.color = new Color(1f, 1f, 1f, 0.12f);
            if (background.sprite != null)
            {
                plate.sprite = background.sprite;
                plate.type = background.type;
            }

            highlightObject.transform.SetAsLastSibling();
            iconObject.transform.SetAsLastSibling();

            FuelIconButton button = go.AddComponent<FuelIconButton>();
            button.Rect = rect;
            button.Highlight = highlightObject;
            button.Icon = icon;
            button.Background = background;
            button.Plate = plate;
            button.NormalColor = background.color;
            return button;
        }

        static Sprite Icon(bool on)
        {
            return Art.Infinity();
        }

        static Sprite PlusIcon()
        {
            return Art.Plus();
        }
    }

    static class Art
    {
        const int Size = 128;
        static Sprite _plus;
        static Sprite _infinity;
        static Vector2[] _curve;

        public static Sprite Plus()
        {
            if (_plus == null)
                _plus = Make(PaintPlus);
            return _plus;
        }

        public static Sprite Infinity()
        {
            if (_infinity == null)
                _infinity = Make(texture => PaintInfinity(texture, 6.8f));
            return _infinity;
        }

        static Sprite Make(System.Action<Texture2D> paint)
        {
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;
            texture.SetPixels(pixels);
            paint(texture);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        static void PaintPlus(Texture2D texture)
        {
            const float cx = 63.5f;
            const float cy = 63.5f;
            const float arm = 34f;
            const float radius = 7.2f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float horizontal = DistToSegment(px, py, cx - arm, cy, cx + arm, cy);
                    float vertical = DistToSegment(px, py, cx, cy - arm, cx, cy + arm);
                    Cover(texture, x, y, Mathf.Min(horizontal, vertical), radius);
                }
            }
        }

        static void PaintInfinity(Texture2D texture, float radius)
        {
            Vector2[] curve = Curve();
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float best = float.MaxValue;
                    for (int i = 0; i < curve.Length; i++)
                    {
                        Vector2 a = curve[i];
                        Vector2 b = curve[(i + 1) % curve.Length];
                        float dist = DistToSegment(px, py, a.x, a.y, b.x, b.y);
                        if (dist < best)
                            best = dist;
                    }
                    Cover(texture, x, y, best, radius);
                }
            }
        }

        static Vector2[] Curve()
        {
            if (_curve != null)
                return _curve;

            const int count = 360;
            const float cx = 63.5f;
            const float cy = 63.5f;
            const float scaleX = 44f;
            const float scaleY = 68f;
            _curve = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count * Mathf.PI * 2f;
                float s = Mathf.Sin(t);
                float c = Mathf.Cos(t);
                float denom = 1f + s * s;
                _curve[i] = new Vector2(cx + scaleX * c / denom, cy + scaleY * s * c / denom);
            }
            return _curve;
        }

        static void Cover(Texture2D texture, int x, int y, float dist, float radius)
        {
            float alpha = Mathf.Clamp01(radius + 0.65f - dist);
            if (alpha <= 0f)
                return;
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }

        static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float apx = px - ax;
            float apy = py - ay;
            float ab2 = abx * abx + aby * aby;
            float t = ab2 <= 0f ? 0f : Mathf.Clamp01((apx * abx + apy * aby) / ab2);
            float dx = px - (ax + abx * t);
            float dy = py - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }

    public class FuelAlign : MonoBehaviour
    {
        void LateUpdate()
        {
            FuelButtons.Realign();
        }
    }

    public class FuelIconButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public RectTransform Rect;
        public GameObject Highlight;
        public Image Icon;
        public Image Background;
        public Image Plate;
        public Color NormalColor;
        public System.Action Click;
        float _ignoreUntil;

        public void SetIcon(Sprite sprite)
        {
            if (Icon != null)
                Icon.sprite = sprite;
        }

        public void SetPressed(bool pressed)
        {
            if (Highlight != null)
                Highlight.SetActive(false);
            if (Background != null)
                Background.color = pressed ? new Color(1f, 0.78f, 0.12f, 1f) : NormalColor;
            if (Plate != null)
                Plate.color = pressed ? new Color(0.42f, 0.24f, 0.04f, 1f) : new Color(1f, 1f, 1f, 0.12f);
            if (Rect != null)
                Rect.localScale = pressed ? new Vector3(0.84f, 0.84f, 1f) : Vector3.one;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;
            if (!Contains(mouse.position.ReadValue()))
                return;
            Invoke();
        }

        bool Contains(Vector2 screen)
        {
            if (Rect == null)
                return false;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return RectTransformUtility.RectangleContainsScreenPoint(Rect, screen, null);

            Camera camera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (camera != null && RectTransformUtility.RectangleContainsScreenPoint(Rect, screen, camera))
                return true;
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screen, null);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Highlight != null)
                Highlight.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Highlight != null)
                Highlight.SetActive(false);
        }

        public void Invoke()
        {
            if (Time.unscaledTime < _ignoreUntil || Click == null)
                return;
            _ignoreUntil = Time.unscaledTime + 0.35f;
            Click();
        }
    }

    [HarmonyPatch(typeof(WoodThrustersUpdater), "Update")]
    static class InfiniteThrusters
    {
        static readonly Dictionary<ulong, InventorySlot> Slots = new Dictionary<ulong, InventorySlot>();
        static readonly Dictionary<ulong, float> Partial = new Dictionary<ulong, float>();

        static void Prefix(WoodThrustersUpdater __instance)
        {
            Slots.Clear();
            Partial.Clear();
            if (!CreativeSession.IsActive)
                return;

            IShipStateWrite ship = ShipOf(__instance);
            foreach (StatefulPart part in ship.GetAllStatefulParts<WoodThrusterState>())
            {
                if (!InfiniteFuel.IsOn(ship.Id, part.Id))
                    continue;
                WoodThrusterState state = (WoodThrusterState)part.State;
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                Slots[key] = state.ItemSlot;
                Partial[key] = state.PartialItemConsumptionLevel;
                ship.UpdatePartState(part.Id, state with
                {
                    ItemSlot = FuelSupply.Full(),
                    PartialItemConsumptionLevel = 1f
                });
            }
        }

        static void Postfix(WoodThrustersUpdater __instance)
        {
            if (!CreativeSession.IsActive || Slots.Count == 0)
                return;

            IShipStateWrite ship = ShipOf(__instance);
            foreach (StatefulPart part in ship.GetAllStatefulParts<WoodThrusterState>())
            {
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                if (!Slots.ContainsKey(key))
                    continue;

                WoodThrusterState state = (WoodThrusterState)part.State;
                ship.UpdatePartState(part.Id, state with
                {
                    ItemSlot = FuelSupply.Full(),
                    PartialItemConsumptionLevel = 0f
                });
            }
        }

        static IShipStateWrite ShipOf(WoodThrustersUpdater updater)
        {
            return Traverse.Create(updater).Field("_ship").GetValue<IShipStateWrite>();
        }
    }

    [HarmonyPatch(typeof(PowerGeneratorUpdaters), "WriteProductionCapacity")]
    static class InfiniteGeneratorCapacity
    {
        static readonly Dictionary<ulong, InventorySlot> Slots = new Dictionary<ulong, InventorySlot>();
        static readonly Dictionary<ulong, float> Partial = new Dictionary<ulong, float>();

        static void Prefix(PowerGeneratorUpdaters __instance)
        {
            Slots.Clear();
            Partial.Clear();
            PretendWood(ShipOf(__instance), Slots, Partial);
        }

        static void Postfix(PowerGeneratorUpdaters __instance)
        {
            RestoreWood(ShipOf(__instance), Slots, Partial);
        }

        static IShipStateWrite ShipOf(PowerGeneratorUpdaters updater)
        {
            return Traverse.Create(updater).Field("_ship").GetValue<IShipStateWrite>();
        }

        internal static void PretendWood(IShipStateWrite ship, Dictionary<ulong, InventorySlot> slots, Dictionary<ulong, float> partial)
        {
            if (!CreativeSession.IsActive || ship == null)
                return;

            foreach (StatefulPart part in ship.GetAllStatefulParts<WoodPowerGeneratorState>())
            {
                if (!InfiniteFuel.IsOn(ship.Id, part.Id))
                    continue;
                WoodPowerGeneratorState state = (WoodPowerGeneratorState)part.State;
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                slots[key] = state.ItemSlot;
                partial[key] = state.PartialItemConsumptionLevel;
                ship.UpdatePartState(part.Id, state with
                {
                    ItemSlot = FuelSupply.Full(),
                    PartialItemConsumptionLevel = 1f
                });
            }
        }

        internal static void RestoreWood(IShipStateWrite ship, Dictionary<ulong, InventorySlot> slots, Dictionary<ulong, float> partial)
        {
            if (!CreativeSession.IsActive || ship == null || slots.Count == 0)
                return;

            foreach (StatefulPart part in ship.GetAllStatefulParts<WoodPowerGeneratorState>())
            {
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                if (!slots.ContainsKey(key))
                    continue;
                WoodPowerGeneratorState state = (WoodPowerGeneratorState)part.State;
                ship.UpdatePartState(part.Id, state with
                {
                    ItemSlot = FuelSupply.Full(),
                    PartialItemConsumptionLevel = 0f
                });
            }
        }
    }

    [HarmonyPatch(typeof(PowerGeneratorUpdaters), "Update")]
    static class InfiniteGeneratorBurn
    {
        static readonly Dictionary<ulong, InventorySlot> Slots = new Dictionary<ulong, InventorySlot>();
        static readonly Dictionary<ulong, float> Partial = new Dictionary<ulong, float>();

        static void Prefix(PowerGeneratorUpdaters __instance)
        {
            Slots.Clear();
            Partial.Clear();
            InfiniteGeneratorCapacity.PretendWood(ShipOf(__instance), Slots, Partial);
        }

        static void Postfix(PowerGeneratorUpdaters __instance)
        {
            InfiniteGeneratorCapacity.RestoreWood(ShipOf(__instance), Slots, Partial);
        }

        static IShipStateWrite ShipOf(PowerGeneratorUpdaters updater)
        {
            return Traverse.Create(updater).Field("_ship").GetValue<IShipStateWrite>();
        }
    }

    [HarmonyPatch(typeof(EthanolDistillersUpdater), "GuessPowerConsumptionAndProductionRate")]
    static class InfiniteDistillerGuess
    {
        static readonly Dictionary<ulong, InventorySlot[]> Slots = new Dictionary<ulong, InventorySlot[]>();
        static readonly Dictionary<ulong, float> Partial = new Dictionary<ulong, float>();

        static void Prefix(EthanolDistillersUpdater __instance)
        {
            Slots.Clear();
            Partial.Clear();
            Pretend(ShipOf(__instance), Slots, Partial);
        }

        static void Postfix(EthanolDistillersUpdater __instance)
        {
            Restore(ShipOf(__instance), Slots, Partial);
        }

        static IShipStateWrite ShipOf(EthanolDistillersUpdater updater)
        {
            return Traverse.Create(updater).Field("_ship").GetValue<IShipStateWrite>();
        }

        internal static void Pretend(IShipStateWrite ship, Dictionary<ulong, InventorySlot[]> slots, Dictionary<ulong, float> partial)
        {
            if (!CreativeSession.IsActive || ship == null)
                return;

            foreach (StatefulPart part in ship.GetAllStatefulParts<EthanolDistillerState>())
            {
                if (!InfiniteFuel.IsOn(ship.Id, part.Id))
                    continue;
                EthanolDistillerState state = (EthanolDistillerState)part.State;
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                slots[key] = state.InventorySlots == null ? new InventorySlot[1] : (InventorySlot[])state.InventorySlots.Clone();
                partial[key] = state.PartialBiomass;
                InventorySlot[] full = new InventorySlot[Math.Max(1, state.SlotCount)];
                full[0] = FuelSupply.Full();
                ship.UpdatePartState(part.Id, state with
                {
                    InventorySlots = full,
                    PartialBiomass = 1f
                });
            }
        }

        internal static void Restore(IShipStateWrite ship, Dictionary<ulong, InventorySlot[]> slots, Dictionary<ulong, float> partial)
        {
            if (!CreativeSession.IsActive || ship == null || slots.Count == 0)
                return;

            foreach (StatefulPart part in ship.GetAllStatefulParts<EthanolDistillerState>())
            {
                ulong key = InfiniteFuel.Key(ship.Id, part.Id);
                if (!slots.ContainsKey(key))
                    continue;
                EthanolDistillerState state = (EthanolDistillerState)part.State;
                InventorySlot[] full = new InventorySlot[Math.Max(1, state.SlotCount)];
                full[0] = FuelSupply.Full();
                ship.UpdatePartState(part.Id, state with
                {
                    InventorySlots = full,
                    PartialBiomass = 0f
                });
            }
        }
    }

    [HarmonyPatch(typeof(EthanolDistillersUpdater), "Update")]
    static class InfiniteDistiller
    {
        static readonly Dictionary<ulong, InventorySlot[]> Slots = new Dictionary<ulong, InventorySlot[]>();
        static readonly Dictionary<ulong, float> Partial = new Dictionary<ulong, float>();

        static void Prefix(EthanolDistillersUpdater __instance)
        {
            Slots.Clear();
            Partial.Clear();
            InfiniteDistillerGuess.Pretend(ShipOf(__instance), Slots, Partial);
        }

        static void Postfix(EthanolDistillersUpdater __instance)
        {
            InfiniteDistillerGuess.Restore(ShipOf(__instance), Slots, Partial);
        }

        static IShipStateWrite ShipOf(EthanolDistillersUpdater updater)
        {
            return Traverse.Create(updater).Field("_ship").GetValue<IShipStateWrite>();
        }
    }
}
