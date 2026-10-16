using System.Collections.Generic;
using Core.Services;
using FishNet.Connection;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Interface.Services;
using Players;
using Players.Interface.Model;
using Players.LocalClient;
using Players.LocalClient.Controls;
using Players.LocalClient.Controls.Input;
using Players.LocalClient.Interaction;
using Players.Network;
using Players.Network.Model;
using Players.Physics;
using Players.Physics.Model;
using Players.Services;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WorldTracking.Interface.Values;

namespace CreativeMode
{
    static class CreativeFlight
    {
        const float DoubleTapSeconds = 0.3f;
        const int SettleSteps = 20;
        const float SettleJump = 6f;
        const float FlyReach = 4f;
        static readonly float[] GearScale = { 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f, 32f, 64f, 128f };
        static readonly string[] GearMark = { "¼×", "½×", "1×", "2×", "4×", "8×", "16×", "32×", "64×", "128×" };

        static float _lastTap = -1f;
        static int _gear = 2;
        static int _scrollFrame = -1;
        static float _hintUntil;
        static TMP_Text _hint;
        static int _settle;
        static int _plugFrame = -1;
        static readonly HashSet<int> FlewIntoUse = new HashSet<int>();
        static bool _deviceOpen;
        static bool _keepFlight;
        static bool _pendingFly;
        static uint _useShip;
        static ushort _usePart;
        static bool _safe;
        static uint _safeSpace;
        static Vector3 _safePos;
        static float _savedReach = -1f;
        static bool _reachWidened;
        static bool _heldKeys;
        static readonly Collider[] Hits = new Collider[16];

        public static void Tick()
        {
            if (!CreativeSession.IsActive)
            {
                _lastTap = -1f;
                RestoreReach();
                HideSpeed();
                return;
            }

            KeepInteractions();
            PaintSpeed();
            // A space typed into the chat or a name must not count as a tap.
            if (_deviceOpen && !StellarDriveMods.Shared.GameKeys.Typing())
                HoldDevice();
            if (!StellarDriveMods.Shared.GameKeys.Free())
            {
                _lastTap = -1f;
                _heldKeys = false;
                return;
            }
            if (!_deviceOpen)
                DriveHeldKeys();
            Keyboard keyboard = Keyboard.current;
            UseLookedAt(keyboard);
            PlugSnappedCable();
            if (keyboard == null || !keyboard.spaceKey.wasPressedThisFrame)
                return;

            float now = Time.unscaledTime;
            if (_lastTap < 0f || now - _lastTap > DoubleTapSeconds)
            {
                _lastTap = now;
                return;
            }

            _lastTap = -1f;
            Toggle();
        }

        static void Toggle()
        {
            PlayerCheatControls controls = Object.FindAnyObjectByType<PlayerCheatControls>();
            if (controls == null || !controls.isActiveAndEnabled)
                return;

            PlayerNetworkInputs inputs = Traverse.Create(controls).Field("_inputs").GetValue<PlayerNetworkInputs>();
            if (inputs == null)
                return;

            inputs.ToggleCheatFlyState_ServerRPC();
        }

        public static void Remember(PlayerPhysicsState state)
        {
            if (!Blocked(state.Position))
            {
                _safe = true;
                _safeSpace = state.SpaceId;
                _safePos = state.Position;
            }
        }

        public static void BeginSettle()
        {
            _settle = SettleSteps;
        }

        public static void Settle(ref PlayerPhysicsState state)
        {
            if (_settle <= 0)
                return;
            _settle--;
            if (!_safe || state.SpaceId != _safeSpace)
                return;
            Vector3 jump = state.Position - _safePos;
            if (jump.sqrMagnitude <= SettleJump * SettleJump)
                return;
            state.Position = _safePos;
            state.Velocity = Vector3.zero;
        }

        public static bool InsideGeometry(Vector3 position)
        {
            return Blocked(position);
        }

        public static bool Settling
        {
            get { return _settle > 0; }
        }

        static void KeepInteractions()
        {
            PlayerControlsActivator activator = Object.FindAnyObjectByType<PlayerControlsActivator>();
            if (activator == null)
                return;
            PlayerMainState main = Traverse.Create(activator).Field("_state").GetValue<PlayerMainState>();
            if (main == null)
                return;
            PlayerState probe = new PlayerState();
            probe.Type = main.CurrentStateType;
            if (!probe.IsCheatFlying)
            {
                RestoreReach();
                return;
            }
            EnableInteractions(activator.gameObject);
        }

        public static void EnableInteractions(GameObject player)
        {
            if (player == null)
                return;
            Enable<PlayerInteractionControl>(player);
            Enable<PlayerObjectInteractionDetector>(player);
            Enable<PlayerPrimaryActionControl>(player);
            Enable<PlayerPlugSocketDetector>(player);
            PlayerObjectInteractionDetector[] detectors = player.GetComponentsInChildren<PlayerObjectInteractionDetector>(true);
            for (int i = 0; i < detectors.Length; i++)
                WidenReach(detectors[i]);
            PlayerCheatFlyControls fly = player.GetComponentInChildren<PlayerCheatFlyControls>(true);
            if (fly != null)
                fly.currentSpeed = 1f;
            PlayerInput input = player.GetComponentInChildren<PlayerInput>(true);
            if (input == null || input.actions == null)
                return;
            InputActionMap actions = input.actions.FindActionMap("Player", false);
            if (actions != null)
                actions.Enable();
        }

        public static float SpeedScale()
        {
            float scale = GearScale[_gear];
            if (ShiftHeld())
                scale *= 2f;
            return scale;
        }

        public static bool TakeScroll()
        {
            return StellarDriveMods.Shared.GameKeys.Free() && !BuildLine.Locked && ShiftHeld();
        }

        public static void BumpScroll(InputAction.CallbackContext context)
        {
            if (_scrollFrame == Time.frameCount)
                return;
            float value = context.ReadValue<float>();
            if (Mathf.Abs(value) < 0.01f && Mouse.current != null)
                value = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(value) < 0.01f)
                return;
            _scrollFrame = Time.frameCount;
            int step = value > 0f ? 1 : -1;
            _gear = Mathf.Clamp(_gear + step, 0, GearScale.Length - 1);
            FlashSpeed();
        }

        static void DriveHeldKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                y += 1f;
            bool down = x * x + y * y > 0.01f;
            if (!down && !_heldKeys)
                return;
            _heldKeys = down;
            PlayerControlsActivator activator = Object.FindAnyObjectByType<PlayerControlsActivator>();
            if (activator == null)
                return;
            if (FlyingNow())
            {
                PlayerCheatFlyControls fly = activator.GetComponentInChildren<PlayerCheatFlyControls>();
                if (fly == null || !fly.isActiveAndEnabled)
                    return;
                fly.moveInput.x = x;
                fly.moveInput.z = y;
                return;
            }
            PlayerWalkingControls walking = activator.GetComponentInChildren<PlayerWalkingControls>();
            if (walking == null || !walking.isActiveAndEnabled)
                return;
            walking.moveInput = new Vector2(x, y);
        }

        static void HoldDevice()
        {
            PlayerControlsActivator activator = Object.FindAnyObjectByType<PlayerControlsActivator>();
            if (activator == null)
                return;
            bool alt = LeftAlt();
            PlayerStateType type;
            StellarDriveMods.Shared.GameKeys.TryGetState(out type);
            PlayerLeverControl lever = activator.GetComponentInChildren<PlayerLeverControl>(true);
            PlayerPilotingControls pilot = activator.GetComponentInChildren<PlayerPilotingControls>(true);
            if (lever != null)
            {
                bool on = !alt && type == PlayerStateType.InteractingWithLever;
                if (lever.enabled != on)
                    lever.enabled = on;
                if (alt)
                    AccessTools.Field(typeof(PlayerLeverControl), "<PushInput>k__BackingField")?.SetValue(lever, 0f);
            }
            if (pilot != null)
            {
                bool on = !alt && type == PlayerStateType.Piloting;
                if (pilot.enabled != on)
                    pilot.enabled = on;
                if (alt)
                {
                    AccessTools.Field(typeof(PlayerPilotingControls), "<MoveInput>k__BackingField")?.SetValue(pilot, Vector3.zero);
                    AccessTools.Field(typeof(PlayerPilotingControls), "<RollInput>k__BackingField")?.SetValue(pilot, 0f);
                }
            }
            if (!alt)
            {
                StopBody(activator);
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                y += 1f;
            PlayerWalkingControls walk = activator.GetComponentInChildren<PlayerWalkingControls>(true);
            if (walk == null)
                return;
            walk.moveInput = new Vector2(x, y);
            bool up = keyboard.spaceKey.isPressed;
            bool down = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            walk.jumpInput = up ? 1f : 0f;
            walk.crouchInput = down && !up ? 1f : 0f;
            PlayerCheatFlyControls fly = activator.GetComponentInChildren<PlayerCheatFlyControls>(true);
            if (fly == null)
                return;
            fly.moveInput.x = x;
            fly.moveInput.z = y;
            fly.moveInput.y = up ? 1f : (down ? -1f : 0f);
        }

        static void StopBody(PlayerControlsActivator activator)
        {
            PlayerWalkingControls walk = activator.GetComponentInChildren<PlayerWalkingControls>(true);
            if (walk != null)
            {
                walk.moveInput = Vector2.zero;
                walk.jumpInput = 0f;
                walk.crouchInput = 0f;
            }
            PlayerCheatFlyControls fly = activator.GetComponentInChildren<PlayerCheatFlyControls>(true);
            if (fly != null)
                fly.moveInput = Vector3.zero;
        }

        static bool LeftAlt()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.leftAltKey.isPressed;
        }

        static bool ShiftHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        public static void RememberUse(int clientId)
        {
            FlewIntoUse.Add(clientId);
            _pendingFly = true;
        }

        public static bool WasFlying(int clientId)
        {
            return FlewIntoUse.Contains(clientId);
        }

        public static bool ForgetUse(int clientId)
        {
            if (!FlewIntoUse.Remove(clientId))
                return false;
            CloseDevice();
            return true;
        }

        public static bool BridgingToDevice(PlayerStateType next)
        {
            return _pendingFly && next == PlayerStateType.Walking;
        }

        public static void OpenDevice(bool fromFly, PlayerState state)
        {
            _deviceOpen = true;
            _keepFlight = fromFly || _pendingFly;
            _pendingFly = false;
            _useShip = state.EntityId;
            _usePart = state.PartId;
        }

        public static void CloseDevice()
        {
            _deviceOpen = false;
            _keepFlight = false;
            _pendingFly = false;
        }

        public static bool IsDevice(PlayerStateType type)
        {
            return type == PlayerStateType.InteractingWithLever
                || type == PlayerStateType.InteractingWithChest
                || type == PlayerStateType.InteractingWithGenericPart
                || type == PlayerStateType.Reading
                || type == PlayerStateType.Writing
                || type == PlayerStateType.Piloting;
        }

        public static bool IsMoveMode(PlayerStateType type)
        {
            return type == PlayerStateType.Walking
                || type == PlayerStateType.WalkingHoldingCable
                || type == PlayerStateType.WalkingHoldingShipPart
                || type == PlayerStateType.CheatFlying
                || type == PlayerStateType.CheatFlyingHoldingCable
                || type == PlayerStateType.CheatFlyingHoldingShipPart;
        }

        public static bool IsFly(PlayerStateType type)
        {
            return type == PlayerStateType.CheatFlying
                || type == PlayerStateType.CheatFlyingHoldingCable
                || type == PlayerStateType.CheatFlyingHoldingShipPart;
        }

        public static void HoldFlight(PlayerState state)
        {
            OpenDevice(true, state);
        }

        public static bool KeepsFlight(PlayerState state)
        {
            if (!_deviceOpen || !_keepFlight || state.EntityId != _useShip || state.PartId != _usePart)
                return false;
            if (state.Type == PlayerStateType.Piloting)
                return LeftAlt();
            return IsDevice(state.Type);
        }

        public static bool AltDrives(PlayerState state)
        {
            return _deviceOpen && LeftAlt() && state.EntityId == _useShip && state.PartId == _usePart && IsDevice(state.Type);
        }

        static void FlashSpeed()
        {
            _hintUntil = Time.unscaledTime + 1.2f;
            EnsureSpeedLabel();
            if (_hint != null)
                _hint.text = GameText.FlySpeed(GearMark[_gear]);
            PaintSpeed();
        }

        static void PaintSpeed()
        {
            if (_hint == null)
                return;
            bool show = Time.unscaledTime < _hintUntil;
            if (_hint.gameObject.activeSelf != show)
                _hint.gameObject.SetActive(show);
        }

        static void HideSpeed()
        {
            _hintUntil = 0f;
            PaintSpeed();
        }

        static void EnsureSpeedLabel()
        {
            if (_hint != null)
                return;
            TMP_Text sample = Object.FindAnyObjectByType<TMP_Text>(FindObjectsInactive.Include);
            GameObject root = new GameObject("CreativeFlySpeed");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject label = new GameObject("Speed");
            label.transform.SetParent(root.transform, false);
            RectTransform rect = label.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(220f, 36f);
            rect.anchoredPosition = new Vector2(0f, 148f);
            TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
            if (sample != null)
                text.font = sample.font;
            text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.85f, 0.95f, 1f, 1f);
            text.raycastTarget = false;
            _hint = text;
        }

        public static bool FlyingNow()
        {
            PlayerStateType state;
            if (!StellarDriveMods.Shared.GameKeys.TryGetState(out state))
                return false;
            return state == PlayerStateType.CheatFlying
                || state == PlayerStateType.CheatFlyingHoldingCable
                || state == PlayerStateType.CheatFlyingHoldingShipPart;
        }

        public static bool HoldingCable()
        {
            PlayerStateType state;
            if (!StellarDriveMods.Shared.GameKeys.TryGetState(out state))
                return false;
            return state == PlayerStateType.WalkingHoldingCable
                || state == PlayerStateType.CheatFlyingHoldingCable;
        }

        public static bool ClaimPlug()
        {
            if (_plugFrame == Time.frameCount)
                return false;
            _plugFrame = Time.frameCount;
            return true;
        }

        public static void ReleaseStolenHover()
        {
            InteractableHoverTracker interactable = ServiceLocator.GetService<InteractableHoverTracker>();
            if (interactable != null)
                interactable.Clear();
            PlayerShipSocketHoverTracker socket = ServiceLocator.GetService<PlayerShipSocketHoverTracker>();
            if (socket != null)
                socket.Clear();
        }

        public static void EnableWalkingActions(PlayerInputSwitcher switcher)
        {
            if (switcher == null)
                return;
            InputActionMap walking = Traverse.Create(switcher).Field("_walkingActionMap").GetValue<InputActionMap>();
            if (walking != null)
                walking.Enable();
        }

        static void UseLookedAt(Keyboard keyboard)
        {
            if (keyboard == null || !keyboard.fKey.wasPressedThisFrame || !FlyingNow())
                return;
            InteractableHoverTracker tracker = ServiceLocator.GetService<InteractableHoverTracker>();
            IInteractableObject hover = tracker != null ? tracker.CurrentHoveredInteractable : null;
            if (hover != null)
                hover.Interact(InteractionType.Interact);
        }

        static void PlugSnappedCable()
        {
            if (!HoldingCable())
                return;
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;
            PlayerPlugSocketDetector detector = Object.FindAnyObjectByType<PlayerPlugSocketDetector>();
            if (detector == null || !detector.HasSocket || detector.CurrentSocket == null)
                return;
            if (!ClaimPlug())
                return;
            IShipInteractionNetworkService interaction = ServiceLocator.GetService<IShipInteractionNetworkService>();
            if (interaction == null)
                return;
            interaction.PlugInCable(new SocketNetworkRequest
            {
                EntityPartSocket = detector.CurrentSocket.EntityPartSocket
            });
        }

        static void Enable<T>(GameObject player) where T : Behaviour
        {
            T[] behaviours = player.GetComponentsInChildren<T>(true);
            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].enabled = true;
        }

        static void WidenReach(PlayerObjectInteractionDetector detector)
        {
            if (detector == null)
                return;
            if (_savedReach < 0f)
                _savedReach = detector.checkDistance;
            detector.checkDistance = Mathf.Max(_savedReach, FlyReach);
            _reachWidened = true;
        }

        static void RestoreReach()
        {
            if (!_reachWidened || _savedReach < 0f)
                return;
            _reachWidened = false;
            PlayerObjectInteractionDetector[] detectors = Object.FindObjectsByType<PlayerObjectInteractionDetector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < detectors.Length; i++)
            {
                if (detectors[i] != null)
                    detectors[i].checkDistance = _savedReach;
            }
        }

        static bool Blocked(Vector3 position)
        {
            if (Overlap(Physics.defaultPhysicsScene, position))
                return true;
            PhysicsScene scene = ClientScene.PhysicsScene;
            return scene.IsValid() && Overlap(scene, position);
        }

        static bool Overlap(PhysicsScene scene, Vector3 position)
        {
            int count = scene.OverlapSphere(position, 0.28f, Hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i];
                if (hit == null)
                    continue;
                if (hit.GetComponentInParent<TrackedPlayerServer>() != null)
                    continue;
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerCheatFlyPhysics), "ProcessPhysicsStep")]
    static class RememberOpenAir
    {
        static void Postfix(ref PlayerPhysicsStepResult __result)
        {
            if (CreativeSession.IsActive)
                CreativeFlight.Remember(__result.State);
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkInputs), "RpcLogic___ToggleCheatFlyState_ServerRPC___2166136261")]
    static class SettleAfterFlight
    {
        static void Postfix(PlayerNetworkInputs __instance)
        {
            if (!CreativeSession.IsActive)
                return;
            PlayerNetworkState state = Traverse.Create(__instance).Field("_networkState").GetValue<PlayerNetworkState>();
            if (state != null && !state.ServerState.IsCheatFlying)
                CreativeFlight.BeginSettle();
        }
    }

    [HarmonyPatch(typeof(PlayerWalkingPhysics), "ProcessPhysicsStep")]
    static class StopFlightShove
    {
        static void Prefix(ref PlayerPhysicsStepContext context)
        {
            if (!CreativeSession.IsActive)
                return;
            PlayerPhysicsState state = context.State;
            CreativeFlight.Settle(ref state);
            context.State = state;
        }
    }

    [HarmonyPatch(typeof(PlayerWalkingPhysics), "CorrectOverlap")]
    static class SkipFlightEject
    {
        static bool Prefix(ref PlayerPhysicsState state)
        {
            if (!CreativeSession.IsActive || !CreativeFlight.Settling)
                return true;
            if (!CreativeFlight.InsideGeometry(state.Position))
                return true;
            state.Velocity = Vector3.zero;
            return false;
        }
    }

    [HarmonyPatch(typeof(TrackedPlayerServer), "CalculateNewPhysicsState")]
    static class NoFlightCollision
    {
        static void Prefix(TrackedPlayerServer __instance)
        {
            SetCapsule(__instance, false);
        }

        static void Postfix(TrackedPlayerServer __instance)
        {
            bool flying = CreativeSession.IsActive && __instance.State.IsCheatFlying;
            SetCapsule(__instance, !flying);
        }

        static void SetCapsule(TrackedPlayerServer player, bool collisions)
        {
            if (!CreativeSession.IsActive || player == null)
                return;
            if (!player.State.IsCheatFlying && !collisions)
                return;
            Rigidbody body = player.ServerRigidbody;
            if (body == null)
                return;
            CapsuleCollider capsule = body.GetComponent<CapsuleCollider>();
            if (capsule != null)
                capsule.enabled = collisions;
            body.detectCollisions = collisions;
        }
    }

    [HarmonyPatch(typeof(PlayerControlsActivator), "ReloadEnabledScripts")]
    static class FlyCanInteract
    {
        static void Postfix(PlayerControlsActivator __instance)
        {
            if (!CreativeSession.IsActive || __instance == null)
                return;
            PlayerMainState main = Traverse.Create(__instance).Field("_state").GetValue<PlayerMainState>();
            if (main == null)
                return;
            PlayerState probe = new PlayerState();
            probe.Type = main.CurrentStateType;
            if (!probe.IsCheatFlying)
                return;
            CreativeFlight.EnableInteractions(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(PlayerInputSwitcher), "OnPlayerStateChange")]
    static class FlyKeepsUseKey
    {
        static void Postfix(PlayerInputSwitcher __instance, PlayerStateChange change)
        {
            if (!CreativeSession.IsActive || !change.NewState.IsCheatFlying)
                return;
            CreativeFlight.EnableWalkingActions(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerPrimaryActionControl), "OnPrimaryAction")]
    static class PlugInsteadOfNewCable
    {
        static bool Prefix(InputAction.CallbackContext context)
        {
            if (!CreativeSession.IsActive || !context.performed || !CreativeFlight.HoldingCable())
                return true;
            if (!CreativeFlight.ClaimPlug())
                return false;
            CreativeFlight.ReleaseStolenHover();
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerInteractionControl), "OnInteract")]
    static class FlyHandlesUseKey
    {
        static bool Prefix(InputAction.CallbackContext context)
        {
            if (!CreativeSession.IsActive || !context.started || !CreativeFlight.FlyingNow())
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerCheatFlyControls), "OnScroll")]
    static class FlyScrollSpeed
    {
        static bool Prefix(InputAction.CallbackContext context)
        {
            if (!CreativeSession.IsActive || !CreativeFlight.FlyingNow())
                return true;
            if (context.started && CreativeFlight.TakeScroll())
                CreativeFlight.BumpScroll(context);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerScrollControl), "OnScroll")]
    static class FlyScrollBeatsTool
    {
        static bool Prefix(InputAction.CallbackContext context)
        {
            if (!CreativeSession.IsActive || !context.started || !CreativeFlight.FlyingNow())
                return true;
            if (!CreativeFlight.TakeScroll())
                return true;
            CreativeFlight.BumpScroll(context);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerCheatControls), "ToggleFly")]
    static class IgnoreAccidentalFlyKey
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch("WorldTracking.Spaces.PlanetSurfaceSpacesServerUpdater", "ObjectLeavePlanet")]
    static class NoCreativeExitOrbit
    {
        static bool Prefix(object worldObject)
        {
            return LeaveAllowed(worldObject);
        }

        internal static bool LeaveAllowed(object worldObject)
        {
            if (!CreativeSession.IsActive || !CreativeFlight.Settling)
                return true;
            TrackedPlayerServer player = worldObject as TrackedPlayerServer;
            if (player == null || player.State.IsCheatFlying)
                return true;
            return false;
        }
    }

    [HarmonyPatch("WorldTracking.Spaces.SpacesServerUpdater", "ObjectLeaveRing")]
    static class NoCreativeExitRing
    {
        static bool Prefix(object worldObject)
        {
            return NoCreativeExitOrbit.LeaveAllowed(worldObject);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___StartInteract_ServerRPC___2228377996")]
    static class FlyCanUseParts
    {
        static void Prefix(object __instance, NetworkConnection __1)
        {
            TrackedPlayerServer player = FlyingUser(__instance, __1);
            if (player == null || player.State.Type != PlayerStateType.CheatFlying)
                return;
            CreativeFlight.RememberUse(__1.ClientId);
            player.SetState(new PlayerState { Type = PlayerStateType.Walking });
        }

        static void Postfix(object __instance, NetworkConnection __1)
        {
            if (__1 == null || !CreativeFlight.WasFlying(__1.ClientId))
                return;
            TrackedPlayerServer player = FlyingUser(__instance, __1);
            if (player == null)
                return;
            if (player.State.Type == PlayerStateType.Walking)
            {
                CreativeFlight.ForgetUse(__1.ClientId);
                player.SetState(new PlayerState { Type = PlayerStateType.CheatFlying });
                return;
            }
            CreativeFlight.HoldFlight(player.State);
        }

        internal static TrackedPlayerServer FlyingUser(object service, NetworkConnection connection)
        {
            if (!CreativeSession.IsActive || service == null || connection == null)
                return null;
            object tracker = Traverse.Create(service).Field("_players").GetValue();
            if (tracker == null)
                return null;
            return Traverse.Create(tracker).Method("GetTrackedPlayerFromConnectionId", connection.ClientId).GetValue<TrackedPlayerServer>();
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RpcLogic___StopInteract_ServerRPC___328543758")]
    static class FlyAfterPartUse
    {
        static void Postfix(object __instance, NetworkConnection __0)
        {
            if (__0 == null || !CreativeFlight.ForgetUse(__0.ClientId))
                return;
            TrackedPlayerServer player = FlyCanUseParts.FlyingUser(__instance, __0);
            if (player == null)
                return;
            player.SetState(new PlayerState { Type = PlayerStateType.CheatFlying });
        }
    }

    [HarmonyPatch(typeof(PlayerState), "get_IsCheatFlying")]
    static class StayFlyingWhileUsing
    {
        static void Postfix(ref PlayerState __instance, ref bool __result)
        {
            if (__result || !CreativeFlight.KeepsFlight(__instance))
                return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(PlayerState), "get_IsUsingPhysics")]
    static class AltMovesWhilePiloting
    {
        static void Postfix(ref PlayerState __instance, ref bool __result)
        {
            if (__result || !CreativeFlight.AltDrives(__instance))
                return;
            __result = true;
        }
    }

    [HarmonyPatch(typeof(TrackedPlayerServer), "SetState")]
    static class RememberDeviceMode
    {
        static void Prefix(TrackedPlayerServer __instance, PlayerState state)
        {
            if (!CreativeSession.IsActive || __instance == null)
                return;
            PlayerStateType oldType = __instance.State.Type;
            PlayerStateType next = state.Type;
            if (CreativeFlight.IsDevice(next))
                CreativeFlight.OpenDevice(CreativeFlight.IsFly(oldType), state);
            else if (CreativeFlight.IsMoveMode(next) && !CreativeFlight.BridgingToDevice(next))
                CreativeFlight.CloseDevice();
        }
    }
}
