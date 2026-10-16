using System;
using HarmonyLib;
using TMPro;
using UI.MainMenu;
using UI.MainMenu.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CreativeMode
{
    static class ModePicker
    {
        static CreateNewWorldHandler _handler;
        static GameObject _dialog;
        static CreateWorldModal _dialogModal;

        public static void Open(CreateNewWorldButton button)
        {
            _handler = button.GetComponentInParent<CreateNewWorldHandler>();
            if (_handler == null)
                return;

            GameObject template = Traverse.Create(_handler).Field("modal").GetValue<GameObject>();
            if (template == null)
                return;

            CloseDialog();
            _dialog = UnityEngine.Object.Instantiate(template, template.transform.parent);
            _dialog.name = "GameModeDialog";
            _dialogModal = _dialog.GetComponent<CreateWorldModal>();
            if (_dialogModal == null || _dialogModal.nameField == null || _dialogModal.createButton == null)
            {
                CloseDialog();
                _handler.ShowCreateWorldModal();
                return;
            }

            _dialogModal.enabled = false;
            _dialog.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Build(_dialogModal);
        }

        public static void OnModalDisabled()
        {
            if (!CreativeSession.Committing)
                CreativeSession.Pending = WorldKind.Unset;
        }

        static void Build(CreateWorldModal modal)
        {
            CreativeSession.Pending = WorldKind.Unset;
            StopForeignLayout(modal.gameObject);

            TMP_Text title = FindTitle(modal);
            SetTitle(title, GameText.GameMode);
            RectTransform cancel = FindCancel(modal);
            RewireCancel(cancel);

            GameObject survival = Spawn(modal.createButton, GameText.Survival, WorldKind.Survival);
            GameObject creative = Spawn(modal.createButton, GameText.Creative, WorldKind.Creative);
            modal.nameField.gameObject.SetActive(false);
            modal.createButton.gameObject.SetActive(false);
            HideLooseLabels(modal, title);

            RectTransform frame = FindFrame(modal.transform, title, cancel);
            GrowChrome(modal.transform, title, cancel, null);
            frame = FindFrame(modal.transform, title, cancel);
            Stack(frame, title, survival, creative, cancel);
            TrimBackgrounds(frame);

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(survival);
        }

        static void Choose(WorldKind kind)
        {
            CreativeSession.Pending = kind;
            CloseDialog();
            if (_handler != null)
                _handler.ShowCreateWorldModal();
        }

        static void CancelDialog()
        {
            CreativeSession.Pending = WorldKind.Unset;
            CloseDialog();
        }

        static void CloseDialog()
        {
            if (_dialog != null)
                UnityEngine.Object.Destroy(_dialog);
            _dialog = null;
            _dialogModal = null;
        }

        static void GrowChrome(Transform root, TMP_Text title, RectTransform cancel, RectTransform column)
        {
            Canvas.ForceUpdateCanvases();
            WorldRect cancelRect = cancel != null ? Measure(cancel) : Measure(title.rectTransform);
            float needed = cancelRect.Height * 8f;
            RectTransform frame = FindFrame(root, title, cancel);
            Transform cursor = frame;
            while (cursor != null && (cursor == _dialog.transform || cursor.IsChildOf(_dialog.transform)))
            {
                RectTransform panel = cursor as RectTransform;
                cursor = cursor.parent;
                if (panel == null)
                    continue;

                WorldRect current = Measure(panel);
                if (current.Height >= Screen.height * 0.85f)
                    break;

                float extra = needed - current.Height;
                if (extra <= 0f)
                    continue;

                float scaleY = Mathf.Abs(panel.lossyScale.y);
                if (scaleY < 0.0001f)
                    scaleY = 1f;
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, panel.sizeDelta.y + extra / scaleY);
            }

            Canvas.ForceUpdateCanvases();
        }

        static void CollectChrome(Transform root, TMP_Text title, RectTransform cancel, System.Collections.Generic.List<RectTransform> panels)
        {
            Consider(root as RectTransform);
            RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
                Consider(rects[i]);

            void Consider(RectTransform rect)
            {
                if (rect == null || rect.GetComponent<Image>() == null)
                    return;
                if (title != null && !Contains(rect, title.rectTransform.position))
                    return;
                if (cancel != null && !Contains(rect, cancel.position))
                    return;
                panels.Add(rect);
            }
        }

        static void Stack(RectTransform frame, TMP_Text title, GameObject survival, GameObject creative, RectTransform cancel)
        {
            if (frame == null || cancel == null)
                return;

            GameObject stackObject = new GameObject("ModeStack", typeof(RectTransform));
            RectTransform stack = stackObject.GetComponent<RectTransform>();
            stack.SetParent(frame, false);
            stack.anchorMin = Vector2.zero;
            stack.anchorMax = Vector2.one;
            stack.offsetMin = Vector2.zero;
            stack.offsetMax = Vector2.zero;
            stack.localScale = Vector3.one;
            stack.SetAsLastSibling();

            float scaleY = Mathf.Abs(stack.lossyScale.y);
            float scaleX = Mathf.Abs(stack.lossyScale.x);
            if (scaleY < 0.0001f)
                scaleY = 1f;
            if (scaleX < 0.0001f)
                scaleX = 1f;

            WorldRect button = Measure(cancel);
            float height = button.Height / scaleY;
            float width = button.Width / scaleX;
            float gap = height * 0.55f;
            float pad = height * 0.75f;

            VerticalLayoutGroup layout = stackObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(Mathf.RoundToInt(pad), Mathf.RoundToInt(pad), Mathf.RoundToInt(pad), Mathf.RoundToInt(pad));
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            if (title != null)
            {
                title.alignment = TextAlignmentOptions.Center;
                PlaceRow(title.rectTransform, stack, width * 2f + gap, height * 0.9f);
            }

            RectTransform pair = MakePair(survival, creative, width, height, gap);
            LayoutElement pairElement = pair.gameObject.AddComponent<LayoutElement>();
            pairElement.preferredWidth = width * 2f + gap;
            pairElement.preferredHeight = height;
            pair.SetParent(stack, false);

            GameObject spacerObject = new GameObject("ModeSpacer", typeof(RectTransform));
            LayoutElement spacer = spacerObject.AddComponent<LayoutElement>();
            spacer.flexibleHeight = 1f;
            spacerObject.transform.SetParent(stack, false);

            RectTransform cancelRow = MakeCancelRow(cancel, width, height);
            cancelRow.SetParent(stack, false);
        }

        static RectTransform MakePair(GameObject survival, GameObject creative, float width, float height, float gap)
        {
            GameObject rowObject = new GameObject("ModePair", typeof(RectTransform));
            RectTransform row = rowObject.GetComponent<RectTransform>();
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            PlaceRow(survival.GetComponent<RectTransform>(), row, width, height);
            PlaceRow(creative.GetComponent<RectTransform>(), row, width, height);
            return row;
        }

        static RectTransform MakeCancelRow(RectTransform cancel, float width, float height)
        {
            GameObject rowObject = new GameObject("CancelRow", typeof(RectTransform));
            RectTransform row = rowObject.GetComponent<RectTransform>();
            HorizontalLayoutGroup layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            LayoutElement element = rowObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.flexibleWidth = 1f;
            PlaceRow(cancel, row, width, height);
            return row;
        }

        static void TrimBackgrounds(RectTransform frame)
        {
            if (frame == null || _dialog == null)
                return;

            WorldRect target = Measure(frame);
            Image[] images = _dialog.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                RectTransform rect = images[i].rectTransform;
                if (rect == frame || images[i].GetComponentInParent<Button>() != null)
                    continue;

                WorldRect area = Measure(rect);
                if (area.Width < target.Width * 0.5f)
                    continue;
                bool hangs = area.Bottom < target.Bottom - 1f || area.Top > target.Top + 1f;
                if (!hangs)
                    continue;

                FitToFrame(rect, target);
            }
        }

        static void FitToFrame(RectTransform rect, WorldRect target)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float scaleX = Mathf.Abs(rect.lossyScale.x);
            float scaleY = Mathf.Abs(rect.lossyScale.y);
            if (scaleX < 0.0001f)
                scaleX = 1f;
            if (scaleY < 0.0001f)
                scaleY = 1f;
            rect.sizeDelta = new Vector2(target.Width / scaleX, target.Height / scaleY);
            rect.position = new Vector3((target.Left + target.Right) * 0.5f, (target.Bottom + target.Top) * 0.5f, rect.position.z);
        }

        static void PlaceRow(RectTransform rect, RectTransform stack, float width, float height)
        {
            rect.gameObject.SetActive(true);
            LayoutElement layout = rect.GetComponent<LayoutElement>();
            if (layout == null)
                layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = false;
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            ContentSizeFitter fitter = rect.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;
            rect.SetParent(stack, false);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
        }

        static RectTransform MakeColumn(GameObject survival, GameObject creative, RectTransform cancel)
        {
            WorldRect button = cancel != null ? Measure(cancel) : Measure(survival.GetComponent<RectTransform>());
            float gap = button.Height * 0.55f;
            GameObject columnObject = new GameObject("ModeColumn", typeof(RectTransform));
            RectTransform column = columnObject.GetComponent<RectTransform>();
            column.SetParent(_dialog.transform, false);
            column.anchorMin = new Vector2(0.5f, 0.5f);
            column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.localScale = Vector3.one;

            float scaleY = Mathf.Abs(column.lossyScale.y);
            float scaleX = Mathf.Abs(column.lossyScale.x);
            if (scaleY < 0.0001f)
                scaleY = 1f;
            if (scaleX < 0.0001f)
                scaleX = 1f;

            VerticalLayoutGroup stack = columnObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = gap / scaleY;
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = true;

            float width = button.Width / scaleX;
            float height = button.Height / scaleY;
            Adopt(survival, column, width, height);
            Adopt(creative, column, width, height);
            column.sizeDelta = new Vector2(width, height * 2f + stack.spacing);
            return column;
        }

        static void Adopt(GameObject go, RectTransform column, float width, float height)
        {
            go.SetActive(true);
            RectTransform rect = go.GetComponent<RectTransform>();
            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout == null)
                layout = go.AddComponent<LayoutElement>();
            layout.ignoreLayout = false;
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;
            rect.SetParent(column, false);
            rect.localScale = Vector3.one;
        }

        static void StopForeignLayout(GameObject dialog)
        {
            LayoutGroup[] groups = dialog.GetComponentsInChildren<LayoutGroup>(true);
            for (int i = 0; i < groups.Length; i++)
                groups[i].enabled = false;
            ContentSizeFitter[] fitters = dialog.GetComponentsInChildren<ContentSizeFitter>(true);
            for (int i = 0; i < fitters.Length; i++)
                fitters[i].enabled = false;
        }

        static RectTransform FindFrame(Transform root, TMP_Text title, RectTransform cancel)
        {
            RectTransform best = root as RectTransform;
            float bestArea = float.MaxValue;
            Consider(root as RectTransform);
            RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < rects.Length; i++)
                Consider(rects[i]);
            Transform cursor = root.parent;
            while (cursor != null)
            {
                Consider(cursor as RectTransform);
                cursor = cursor.parent;
            }

            return best;

            void Consider(RectTransform rect)
            {
                if (rect == null || rect.GetComponent<Image>() == null)
                    return;
                if (title != null && !Contains(rect, title.rectTransform.position))
                    return;
                if (cancel != null && !Contains(rect, cancel.position))
                    return;
                float area = Mathf.Abs(rect.rect.width * rect.rect.height);
                if (area <= 0f || area >= bestArea)
                    return;
                best = rect;
                bestArea = area;
            }
        }

        static bool Contains(RectTransform rect, Vector3 world)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return world.x >= corners[0].x && world.x <= corners[2].x
                && world.y >= corners[0].y && world.y <= corners[1].y;
        }

        static void RewireCancel(RectTransform cancel)
        {
            if (cancel == null)
                return;
            Button button = cancel.GetComponent<Button>();
            if (button == null)
                return;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            button.onClick.AddListener(CancelDialog);
        }

        static GameObject Spawn(Button source, string label, WorldKind kind)
        {
            GameObject go = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            go.name = kind == WorldKind.Creative ? "CreativeModeButton" : "SurvivalModeButton";
            Button button = go.GetComponent<Button>();
            button.interactable = true;
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == "Create")
                    button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            }

            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;
            button.onClick.AddListener(() => Choose(kind));
            SilenceLocalization(go);
            foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
                text.text = label;
            go.SetActive(true);
            return go;
        }

        static RectTransform FindCancel(CreateWorldModal modal)
        {
            foreach (Button button in modal.GetComponentsInChildren<Button>(true))
            {
                if (button == null || button == modal.createButton)
                    continue;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentMethodName(i) == "Cancel")
                        return button.GetComponent<RectTransform>();
                }
            }

            return null;
        }

        static void HideLooseLabels(CreateWorldModal modal, TMP_Text title)
        {
            foreach (TMP_Text text in modal.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!text.gameObject.activeSelf)
                    continue;
                if (text.GetComponentInParent<Button>() != null)
                    continue;
                if (text == title || (title != null && text.transform.IsChildOf(title.transform)))
                    continue;
                if (modal.nameField != null && text.transform.IsChildOf(modal.nameField.transform))
                    continue;
                text.gameObject.SetActive(false);
            }
        }

        static void SetTitle(TMP_Text title, string value)
        {
            if (title == null)
                return;
            Behaviour localizer = FindLocalizer(title.gameObject);
            if (localizer != null)
                localizer.enabled = false;
            title.text = value;
        }

        static TMP_Text FindTitle(CreateWorldModal modal)
        {
            TMP_Text fallback = null;
            foreach (TMP_Text text in modal.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.GetComponentInParent<Button>() != null)
                    continue;
                if (modal.nameField != null && text.transform.IsChildOf(modal.nameField.transform))
                    continue;

                string name = text.gameObject.name;
                if (name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0)
                    return text;
                if (fallback == null)
                    fallback = text;
            }

            return fallback;
        }

        static void SilenceLocalization(GameObject go)
        {
            foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                Behaviour localizer = FindLocalizer(text.gameObject);
                if (localizer != null)
                    localizer.enabled = false;
            }
        }

        static Behaviour FindLocalizer(GameObject go)
        {
            foreach (Behaviour behaviour in go.GetComponents<Behaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "LocalizeStringEvent")
                    return behaviour;
            }

            return null;
        }

        internal struct WorldRect
        {
            public float Left;
            public float Right;
            public float Bottom;
            public float Top;
            public float Width;
            public float Height;
        }

        internal static WorldRect Measure(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            WorldRect result = new WorldRect();
            result.Left = corners[0].x;
            result.Right = corners[3].x;
            result.Bottom = corners[0].y;
            result.Top = corners[1].y;
            result.Width = result.Right - result.Left;
            result.Height = result.Top - result.Bottom;
            return result;
        }
    }

    public class ModeDialogLayout : MonoBehaviour
    {
        public RectTransform Frame;
        public RectTransform Title;
        public RectTransform Cancel;
        public RectTransform Column;

        public void Apply()
        {
            if (Frame == null || Column == null)
                return;

            ModePicker.WorldRect frame = ModePicker.Measure(Frame);
            ModePicker.WorldRect column = ModePicker.Measure(Column);
            float buttonHeight = Cancel != null ? ModePicker.Measure(Cancel).Height : column.Height * 0.45f;
            float pad = buttonHeight * 0.7f;
            float centerX = (frame.Left + frame.Right) * 0.5f;
            float depth = Frame.position.z;

            if (Title != null)
            {
                ModePicker.WorldRect title = ModePicker.Measure(Title);
                Title.position = new Vector3(centerX, frame.Top - pad - title.Height * 0.5f, depth);
            }

            if (Cancel != null)
            {
                ModePicker.WorldRect cancel = ModePicker.Measure(Cancel);
                Cancel.position = new Vector3(frame.Left + pad + cancel.Width * 0.5f, frame.Bottom + pad + cancel.Height * 0.5f, depth);
            }

            float titleBottom = Title != null ? ModePicker.Measure(Title).Bottom : frame.Top - pad;
            float cancelTop = Cancel != null ? ModePicker.Measure(Cancel).Top : frame.Bottom + pad;
            float midY = (titleBottom + cancelTop) * 0.5f;
            Column.position = new Vector3(centerX, midY, depth);
        }

        void LateUpdate()
        {
            Apply();
        }
    }
}
