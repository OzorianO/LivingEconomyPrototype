using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // An explorer only: no wallet, employment or economic side effects.
    [DisallowMultipleComponent]
    public sealed class IslandPlayer : MonoBehaviour
    {
        private CharacterController controller;
        private MeshCollider ground;
        private Camera followCamera;
        private SettlementView settlement;
        private Transform leftLeg, rightLeg;
        private float yaw, pitch = 20, distance = 6, verticalSpeed, gait;
        public bool Exploring { get; private set; } = true;

        public void Initialize(SettlementView view, Camera camera, MeshCollider terrain, Transform legA, Transform legB)
        {
            settlement = view; followCamera = camera; ground = terrain;
            leftLeg = legA; rightLeg = legB;
            controller = gameObject.AddComponent<CharacterController>();
            controller.height = 1.8f; controller.radius = 0.32f;
            controller.center = Vector3.up * 0.9f;
            controller.stepOffset = 0.25f; controller.slopeLimit = 45;
            controller.skinWidth = 0.04f; controller.minMoveDistance = 0;
        }

        public void SetExploring(bool value)
        {
            Exploring = value;
            if (settlement != null) settlement.CloseInteraction();
            if (!value && settlement != null) settlement.RestoreOverview();
        }

        private void Update()
        {
            if (controller == null || ground == null || followCamera == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) SetExploring(!Exploring);
            if (!Exploring) return;
            var mouse = Mouse.current;
            bool overMap = mouse != null && followCamera.pixelRect.Contains(mouse.position.ReadValue()) && !settlement.InteractionOpen;
            if (overMap && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw = Mathf.Repeat(yaw + delta.x * 0.2f, 360);
                pitch = Mathf.Clamp(pitch - delta.y * 0.15f, 8, 65);
            }
            if (overMap)
            {
                float scroll = mouse.scroll.ReadValue().y;
                float notches = Mathf.Abs(scroll) >= 10 ? scroll / 120 : scroll;
                distance = Mathf.Clamp(distance * Mathf.Exp(-notches * 0.12f), 3, 10);
            }
            var input = Vector2.zero;
            if (keyboard != null && !settlement.InteractionOpen)
            {
                input.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                input.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            }
            Vector3 direction = Quaternion.Euler(0, yaw, 0) * new Vector3(input.x, 0, input.y);
            MoveExplorer(direction, keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed),
                keyboard != null && keyboard.spaceKey.wasPressedThisFrame, Mathf.Min(Time.deltaTime, 0.05f));
        }

        public bool IsDryGround(Vector3 position)
        {
            return ground != null && ground.Raycast(new Ray(new Vector3(position.x, 30, position.z), Vector3.down), out var hit, 40)
                && hit.point.y >= -0.3f;
        }

        // Separate from input polling so collision/movement can be verified in Unity.
        public void MoveExplorer(Vector3 direction, bool running, bool jump, float dt)
        {
            if (controller == null || !Exploring || dt <= 0) return;
            if (settlement != null && settlement.InteractionOpen) { direction = Vector3.zero; jump = false; }
            direction.y = 0; direction = Vector3.ClampMagnitude(direction, 1);
            var displacement = direction * (running ? 6 : 3.5f) * Mathf.Min(dt, 0.05f);
            if (!IsDryGround(transform.position + displacement + direction * controller.radius)) displacement = Vector3.zero;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            if (jump && controller.isGrounded) verticalSpeed = 6;
            verticalSpeed -= 18 * Mathf.Min(dt, 0.05f);
            var before = transform.position;
            controller.Move(displacement + Vector3.up * verticalSpeed * Mathf.Min(dt, 0.05f));
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
            if (transform.position.y < -3) TeleportToSpawn();
            var moved = transform.position - before; moved.y = 0;
            if (moved.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moved), 12 * dt);
            gait += moved.magnitude * 3;
            float swing = moved.sqrMagnitude > 0.000001f ? Mathf.Sin(gait) * 25 : 0;
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
        }

        public void TeleportToSpawn()
        {
            if (controller != null) controller.enabled = false;
            transform.position = new Vector3(0, 0.08f, -1.5f); verticalSpeed = 0;
            if (controller != null) controller.enabled = true;
        }

        private void LateUpdate()
        {
            if (!Exploring || followCamera == null || controller == null) return;
            var focus = transform.position + Vector3.up * 1.35f;
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var offset = rotation * Vector3.back * distance;
            float safeDistance = distance;
            foreach (var hit in Physics.SphereCastAll(focus, 0.15f, offset.normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform) && !(hit.collider is CapsuleCollider)
                    && hit.distance < safeDistance) safeDistance = Mathf.Max(0.4f, hit.distance - 0.15f);
            followCamera.transform.SetPositionAndRotation(focus + offset.normalized * safeDistance, rotation);
        }

        private void OnGUI()
        {
            if (followCamera == null) return;
            float x = followCamera.pixelRect.x + 10;
            float width = Mathf.Max(100, Mathf.Min(460, followCamera.pixelRect.width - 20));
            GUILayout.BeginArea(new Rect(x, 10, width, 100), GUI.skin.box);
            GUILayout.Label(Exploring ? "HERO: WASD walk | Shift run | Space jump | E interact" : "ISLAND OVERVIEW");
            GUILayout.Label("Right drag: camera | Wheel: zoom | Tab: change mode");
            if (GUILayout.Button(Exploring ? "Return to island overview (Tab)" : "Control hero (Tab)")) SetExploring(!Exploring);
            GUILayout.EndArea();
        }
    }
}
