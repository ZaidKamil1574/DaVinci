using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DataCenter.Teleop
{
    /// <summary>
    /// The grab handle for direct control. It sits at the <i>commanded</i> tool pose; grabbing it in
    /// VR, or dragging it with the mouse, moves that command, and the arm follows over the link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from the Da Vinci project's <c>DaVinciArmGrabHandle</c>, and for the same reason it
    /// derives from <see cref="XRBaseInteractable"/> rather than <c>XRGrabInteractable</c>: a grab
    /// interactable moves the object it is on, which would fight the IK for the arm. Here the hand
    /// only supplies a goal.
    /// </para>
    /// <para>
    /// One change: the Da Vinci handle was on the instrument itself. With a laggy link the arm can
    /// be hundreds of milliseconds behind the hand, so this handle lives at the commanded pose
    /// instead, and the gap between it and the gripper is the latency made visible.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public class TargetHandle : XRBaseInteractable
    {
        [SerializeField, Tooltip("Renderers tinted to show whether motion is enabled.")]
        Renderer[] m_Tinted;

        [SerializeField]
        Color m_LockedColor = new Color(0.45f, 0.5f, 0.55f);

        [SerializeField]
        Color m_ActiveColor = new Color(0.1f, 0.85f, 1f);

        [SerializeField]
        Color m_ClampedColor = new Color(1f, 0.6f, 0.1f);

        static MaterialPropertyBlock s_Block;
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        Pose m_GrabOffset;
        Pose m_GrabPose;
        bool m_HasGrabPose;
        int m_Tint = -1;

        /// <summary>Whether a VR hand is holding the handle.</summary>
        public bool isGrabbed => isSelected;

        /// <summary>The pose the holding hand is asking for, if grabbed.</summary>
        public bool TryGetGrabPose(out Pose pose)
        {
            pose = m_GrabPose;
            return isSelected && m_HasGrabPose;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);

            // Remember where on the handle the hand closed, so grabbing does not snap the target to the palm.
            var hand = args.interactorObject.GetAttachTransform(this);
            var inverse = Quaternion.Inverse(hand.rotation);
            m_GrabOffset = new Pose(inverse * (transform.position - hand.position), inverse * transform.rotation);
            m_GrabPose = new Pose(transform.position, transform.rotation);
            m_HasGrabPose = true;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            m_HasGrabPose = false;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);

            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || interactorsSelecting.Count == 0)
                return;

            var hand = interactorsSelecting[0].GetAttachTransform(this);
            if (hand == null)
                return;

            m_GrabPose = new Pose(hand.position + hand.rotation * m_GrabOffset.position, hand.rotation * m_GrabOffset.rotation);
        }

        /// <summary>Places the handle at the commanded pose. Called by the operator every frame.</summary>
        public void Show(Pose commanded, bool motionEnabled, bool clamped)
        {
            transform.SetPositionAndRotation(commanded.position, commanded.rotation);

            var tint = !motionEnabled ? 0 : clamped ? 2 : 1;
            if (tint == m_Tint || m_Tinted == null)
                return;

            m_Tint = tint;
            var color = tint == 0 ? m_LockedColor : tint == 2 ? m_ClampedColor : m_ActiveColor;
            s_Block ??= new MaterialPropertyBlock();
            for (var i = 0; i < m_Tinted.Length; i++)
            {
                if (m_Tinted[i] == null)
                    continue;

                m_Tinted[i].GetPropertyBlock(s_Block);
                s_Block.SetColor(k_BaseColor, color);
                m_Tinted[i].SetPropertyBlock(s_Block);
            }
        }

        /// <summary>Wires the handle from code. Used by the scene builder.</summary>
        public void Configure(Renderer[] tinted) => m_Tinted = tinted;
    }
}
