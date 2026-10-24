using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DataCenter.Teleop
{
    /// <summary>
    /// A knob turned by twisting the wrist while gripping it. Reports -1 to 1 and springs back to
    /// centre on release, so it works as a rate control: turn and hold to keep the wrist rotating.
    /// </summary>
    /// <remarks>
    /// Written in the same pattern as the Da Vinci project's <c>XRLever2D</c>: an
    /// <see cref="XRBaseInteractable"/> that reads the hand and writes only its own value, so it can
    /// be replicated or recorded without knowing what it drives. It reads hand twist about the
    /// knob's axis rather than hand position, which is what makes a small knob comfortable in VR.
    /// </remarks>
    public class XRTwistKnob : XRBaseInteractable
    {
        [SerializeField, Tooltip("Visual that turns with the value. Rotates about its local Y.")]
        Transform m_Handle;

        [SerializeField, Tooltip("Hand twist for full deflection, in degrees.")]
        float m_MaxTwist = 70f;

        [SerializeField, Range(0f, 0.5f), Tooltip("Fraction of travel around centre that reads as zero.")]
        float m_DeadZone = 0.1f;

        [SerializeField, Tooltip("Return to centre on release, in units per second.")]
        float m_CentringSpeed = 5f;

        IXRSelectInteractor m_Interactor;
        Quaternion m_GrabRotation;
        float m_Raw;

        /// <summary>Current deflection, -1 to 1, after the dead zone.</summary>
        public float value { get; private set; }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_Interactor = args.interactorObject;
            m_GrabRotation = m_Interactor.GetAttachTransform(this).rotation;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            m_Interactor = null;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
                return;

            if (m_Interactor != null)
            {
                // Twist of the hand about the knob axis since the grab.
                var hand = m_Interactor.GetAttachTransform(this).rotation;
                var delta = hand * Quaternion.Inverse(m_GrabRotation);
                delta.ToAngleAxis(out var angle, out var axis);
                if (angle > 180f)
                    angle -= 360f;
                var twist = float.IsNaN(axis.x) ? 0f : Vector3.Dot(axis, transform.up) * angle;
                m_Raw = Mathf.Clamp(-twist / m_MaxTwist, -1f, 1f);
            }
            else
            {
                m_Raw = Mathf.MoveTowards(m_Raw, 0f, m_CentringSpeed * Time.deltaTime);
            }

            var magnitude = Mathf.Abs(m_Raw);
            value = magnitude <= m_DeadZone ? 0f : Mathf.Sign(m_Raw) * Mathf.InverseLerp(m_DeadZone, 1f, magnitude);

            if (m_Handle != null)
                m_Handle.localRotation = Quaternion.Euler(0f, -m_Raw * m_MaxTwist, 0f);
        }

        /// <summary>Wires the knob from code. Used by the scene builder.</summary>
        public void Configure(Transform handle) => m_Handle = handle;
    }
}
