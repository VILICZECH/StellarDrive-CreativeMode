using HarmonyLib;
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
using UnityEngine;
using UnityEngine.InputSystem;
using WorldTracking.Interface.Values;

namespace CreativeMode
{
    static class CreativeFlight
    {
        const float DoubleTapSeconds = 0.3f;
        const int SettleSteps = 8;

        static float _lastTap = -1f;
        static int _settle;
        static bool _safe;
        static uint _safeSpace;
        static Vector3 _safePos;
        static readonly Collider[] Hits = new Collider[16];

        public static void Tick()
        {
            if (!CreativeSession.IsActive)
            {
                _lastTap = -1f;
                return;
            }

            KeepInteractions();
            // A space typed into the chat or a name must not count as a tap.
            if (!StellarDriveMods.Shared.GameKeys.Free())
            {
                _lastTap = -1f;
                return;
            }
            Keyboard keyboard = Keyboard.current;
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
            state.Velocity = Vector3.zero;
            if (_safe && state.SpaceId == _safeSpace && Blocked(state.Position))
                state.Position = _safePos;
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
                return;
            EnableInteractions(activator.gameObject);
        }

        public static void EnableInteractions(GameObject player)
        {
            if (player == null)
                return;
            PlayerInteractionControl[] interact = player.GetComponentsInChildren<PlayerInteractionControl>(true);
            for (int i = 0; i < interact.Length; i++)
                interact[i].enabled = true;
            PlayerObjectInteractionDetector[] detectors = player.GetComponentsInChildren<PlayerObjectInteractionDetector>(true);
            for (int i = 0; i < detectors.Length; i++)
                detectors[i].enabled = true;
        }

        public static void EnableWalkingActions(PlayerInputSwitcher switcher)
        {
            if (switcher == null)
                return;
            InputActionMap walking = Traverse.Create(switcher).Field("_walkingActionMap").GetValue<InputActionMap>();
            if (walking != null)
                walking.Enable();
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
            if (!flying && CreativeFlight.Settling)
            {
                Rigidbody body = __instance.ServerRigidbody;
                if (body == null)
                    return;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
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
}
