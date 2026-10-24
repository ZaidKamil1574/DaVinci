using System.Collections.Generic;
using UnityEngine;

namespace DataCenter.Robotics
{
    /// <summary>
    /// Cyclic Coordinate Descent solver constrained to one rotation axis per joint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ported from the Da Vinci project (<c>XRMultiplayer.DaVinci.CcdSolver</c>, unchanged there).
    /// The position solve is identical; <see cref="SolveOrientation"/> is new, for the wrist of a
    /// six-axis arm that has to present a connector square to a port.
    /// </para>
    /// <para>
    /// This exists because the stock <c>ChainIKConstraint</c> in com.unity.animation.rigging treats
    /// every joint as a ball joint. A surgical arm is a chain of single-axis revolute joints, so an
    /// unconstrained solver produces poses the real mechanism cannot reach. Here each correction is
    /// projected onto the plane perpendicular to the joint's own axis before it is applied, which
    /// makes an out-of-axis pose unrepresentable rather than merely penalised.
    /// </para>
    /// </remarks>
    public static class CcdSolver
    {
        /// <summary>Below this projected length the swing angle is numerically meaningless.</summary>
        const float k_MinProjectedLength = 1e-5f;

        /// <summary>
        /// Rotates <paramref name="joints"/> so that <paramref name="effector"/> moves toward
        /// <paramref name="targetPosition"/>, respecting each joint's axis and angle limits.
        /// </summary>
        /// <param name="joints">Chain ordered from the base outward. Solved from the tip inward each iteration.</param>
        /// <param name="effector">The transform being driven to the target. Usually the last joint's tip.</param>
        /// <param name="targetPosition">World-space goal for the effector.</param>
        /// <param name="iterations">Maximum full passes over the chain.</param>
        /// <param name="tolerance">Distance at which the solve is considered converged, in metres.</param>
        /// <returns>Final distance from the effector to the target, in metres.</returns>
        public static float Solve(
            IReadOnlyList<RevoluteJoint> joints,
            Transform effector,
            Vector3 targetPosition,
            int iterations,
            float tolerance)
        {
            if (joints == null || joints.Count == 0 || effector == null)
                return float.PositiveInfinity;

            var distance = Vector3.Distance(effector.position, targetPosition);

            for (var iteration = 0; iteration < iterations && distance > tolerance; iteration++)
            {
                // Tip inward. The distal joints are light and fast in the real mechanism, so letting
                // them absorb the correction first keeps the heavy setup joints comparatively still.
                for (var i = joints.Count - 1; i >= 0; i--)
                {
                    var joint = joints[i];
                    if (!joint.isValid || joint.weight <= 0f)
                        continue;

                    var pivot = joint.joint.position;
                    var axis = joint.worldAxis;

                    // Only the component of each vector in the joint's plane of rotation is reachable.
                    var toEffector = Vector3.ProjectOnPlane(effector.position - pivot, axis);
                    var toTarget = Vector3.ProjectOnPlane(targetPosition - pivot, axis);

                    if (toEffector.sqrMagnitude < k_MinProjectedLength || toTarget.sqrMagnitude < k_MinProjectedLength)
                        continue;

                    var swing = Vector3.SignedAngle(toEffector, toTarget, axis);
                    if (Mathf.Abs(swing) < Mathf.Epsilon)
                        continue;

                    joint.Rotate(swing * joint.weight);
                }

                var previous = distance;
                distance = Vector3.Distance(effector.position, targetPosition);

                // Joint limits can leave the chain unable to improve. Stop rather than spin.
                if (previous - distance < tolerance * 0.01f)
                    break;
            }

            return distance;
        }

        /// <summary>
        /// Rotates <paramref name="joints"/> so that <paramref name="effector"/>'s rotation moves
        /// toward <paramref name="targetRotation"/>, respecting each joint's axis and limits.
        /// </summary>
        /// <remarks>
        /// The orientation analogue of <see cref="Solve"/>. The remaining error is expressed as a
        /// rotation vector (axis times angle), and each joint takes only the component of it that
        /// lies along its own axis. A single pass under-corrects whenever axes are not orthogonal
        /// to the error, so it iterates, tip inward, until the error stops shrinking.
        /// </remarks>
        /// <returns>Final angle between the effector and target rotations, in degrees.</returns>
        public static float SolveOrientation(
            IReadOnlyList<RevoluteJoint> joints,
            Transform effector,
            Quaternion targetRotation,
            int iterations,
            float toleranceDegrees)
        {
            if (joints == null || joints.Count == 0 || effector == null)
                return 180f;

            var error = Quaternion.Angle(effector.rotation, targetRotation);

            for (var iteration = 0; iteration < iterations && error > toleranceDegrees; iteration++)
            {
                for (var i = joints.Count - 1; i >= 0; i--)
                {
                    var joint = joints[i];
                    if (!joint.isValid || joint.weight <= 0f)
                        continue;

                    var delta = targetRotation * Quaternion.Inverse(effector.rotation);
                    delta.ToAngleAxis(out var angle, out var axis);

                    // ToAngleAxis reports 0..360. Take the short way round.
                    if (angle > 180f)
                        angle -= 360f;

                    if (Mathf.Abs(angle) < Mathf.Epsilon || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
                        continue;

                    var twist = Vector3.Dot(axis * angle, joint.worldAxis);
                    if (Mathf.Abs(twist) < Mathf.Epsilon)
                        continue;

                    joint.Rotate(twist * joint.weight);
                }

                var previous = error;
                error = Quaternion.Angle(effector.rotation, targetRotation);

                if (previous - error < toleranceDegrees * 0.01f)
                    break;
            }

            return error;
        }
    }
}
