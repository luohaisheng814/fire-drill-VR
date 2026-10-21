using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace FireSafety
{
    /// <summary>
    /// 可选：把 XR Grab Interactable 的“选中/取消选中”自动接到 GrabSfx 上。
    /// 用 ToggleGrab 或手动事件的话，删掉本文件即可（不会影响编译）。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
    public class GrabSfxXRHook : MonoBehaviour
    {
        public GrabSfx grabSfx;

        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grab;

        private void Awake()
        {
            _grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (!grabSfx) grabSfx = GetComponent<GrabSfx>();
        }

        private void OnEnable()
        {
            if (!_grab) return;
            _grab.selectEntered.AddListener(OnGrab);
            _grab.selectExited.AddListener(OnRelease);
        }

        private void OnDisable()
        {
            if (!_grab) return;
            _grab.selectEntered.RemoveListener(OnGrab);
            _grab.selectExited.RemoveListener(OnRelease);
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            if (grabSfx) grabSfx.PlayPickup();
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            if (grabSfx) grabSfx.PlayDrop();
        }
    }
}
