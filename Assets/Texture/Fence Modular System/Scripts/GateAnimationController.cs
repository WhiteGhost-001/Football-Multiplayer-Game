using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WB3DAssets.FenceModularSystem
{
// Attach to any gate prefab root. Colliders can be on child objects.
// Click to open (plays forward), click again to close (plays reverse).
// Supports flipped gates (FlipVisuals180). Play Mode only.
// Compatible with both Legacy Input Manager and New Input System.
public class GateAnimationController : MonoBehaviour
{
    Animator animator;
    float len, time, dir;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (!animator || !animator.runtimeAnimatorController) return;
        len = animator.runtimeAnimatorController.animationClips[0].length;
        animator.speed = 0f;
        animator.Play(0, 0, 0f);
        animator.Update(0f);

        bool isFlipped = false;
        Transform snap = transform.Find("SnapPoint1");
        float pivotZ = snap ? transform.InverseTransformPoint(snap.position).z : 0f;

        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("SnapPoint")) continue;
            if (child.localScale.z < 0f) { isFlipped = true; break; }
        }

        if (!isFlipped) return;

        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("SnapPoint")) continue;
            var p = child.localPosition;
            p.z = 2f * pivotZ - p.z;
            child.localPosition = p;
            var s = child.localScale;
            s.z = Mathf.Abs(s.z);
            child.localScale = s;
        }

        var rs = transform.localScale;
        rs.z *= -1f;
        transform.localScale = rs;
    }

    void Update()
    {
        if (!animator) return;

        if (dir == 0f && GetMouseDown())
        {
            var ray = Camera.main.ScreenPointToRay(GetMousePosition());
            if (Physics.Raycast(ray, out var hit) && hit.transform.IsChildOf(transform))
                dir = time <= 0f ? 1f : -1f;
        }

        if (dir == 0f) return;
        time = Mathf.Clamp(time + Time.deltaTime * dir, 0f, len);
        animator.Play(0, 0, time / len);
        animator.Update(0f);
        if (time <= 0f || time >= len) dir = 0f;
    }

    static bool GetMouseDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    static Vector2 GetMousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }
}
} // namespace WB3DAssets.FenceModularSystem
