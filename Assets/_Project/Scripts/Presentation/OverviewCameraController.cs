using UnityEngine;
using UnityEngine.InputSystem;

namespace LivingEconomy.Presentation
{
    // The view retains serialized pose fields so script reload preserves the overview.
    internal sealed class OverviewCameraController
    {
        public void ResetCamera(Camera mapCamera, ref Vector3 cameraFocus, ref float cameraYaw, ref float cameraPitch, ref float cameraDistance)
        {
            cameraFocus = new Vector3(0, 0, 1);
            cameraYaw = -25; cameraPitch = 43; cameraDistance = 70;
            ApplyCameraPose(mapCamera, ref cameraFocus, cameraYaw, ref cameraPitch, ref cameraDistance);
        }

        public void ApplyCameraPose(Camera mapCamera, ref Vector3 cameraFocus, float cameraYaw, ref float cameraPitch, ref float cameraDistance)
        {
            if (mapCamera == null) return;
            cameraPitch = Mathf.Clamp(cameraPitch, 20, 75);
            cameraDistance = Mathf.Clamp(cameraDistance, 12, 100);
            cameraFocus.x = Mathf.Clamp(cameraFocus.x, -20, 20);
            cameraFocus.z = Mathf.Clamp(cameraFocus.z, -17, 19);
            cameraFocus.y = 0;
            var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0);
            mapCamera.transform.SetPositionAndRotation(cameraFocus + rotation * Vector3.back * cameraDistance, rotation);
        }

        public void UpdateCameraControls(Camera mapCamera, IslandPlayer player, ref Vector3 cameraFocus, ref float cameraYaw, ref float cameraPitch, ref float cameraDistance)
        {
            if (player != null && player.Exploring) return;
            var mouse = Mouse.current;
            if (mouse == null || !mapCamera.pixelRect.Contains(mouse.position.ReadValue())) return;
            var delta = mouse.delta.ReadValue();
            if (mouse.rightButton.isPressed)
            {
                cameraYaw = Mathf.Repeat(cameraYaw + delta.x * 0.2f, 360);
                cameraPitch -= delta.y * 0.15f;
            }
            if (mouse.middleButton.isPressed)
            {
                var right = Quaternion.Euler(0, cameraYaw, 0) * Vector3.right;
                var forward = Quaternion.Euler(0, cameraYaw, 0) * Vector3.forward;
                float unitsPerPixel = 2 * cameraDistance * Mathf.Tan(mapCamera.fieldOfView * Mathf.Deg2Rad / 2)
                    / Mathf.Max(1, mapCamera.pixelHeight);
                cameraFocus -= (right * delta.x + forward * delta.y) * unitsPerPixel;
            }
            cameraDistance = ZoomDistance(cameraDistance, mouse.scroll.ReadValue().y);
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetCamera(mapCamera, ref cameraFocus, ref cameraYaw, ref cameraPitch, ref cameraDistance);
            ApplyCameraPose(mapCamera, ref cameraFocus, cameraYaw, ref cameraPitch, ref cameraDistance);
        }

        public static float ZoomDistance(float distance, float scroll)
        {
            // Input System can expose either normalized notches or raw Windows wheel units.
            float notches = Mathf.Abs(scroll) >= 10 ? scroll / 120 : scroll;
            return Mathf.Clamp(distance * Mathf.Exp(-notches * 0.12f), 12, 100);
        }

    }
}
