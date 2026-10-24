using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace DataCenter.Teleop
{
    /// <summary>
    /// Answers "is a headset rendering right now?" the same way on Quest (OpenXR) and in the
    /// browser (WebXR), where the answer changes when the user enters or leaves immersive mode.
    /// </summary>
    public static class XRPlatform
    {
        static readonly List<XRDisplaySubsystem> s_Displays = new List<XRDisplaySubsystem>();
        static int s_CheckedFrame = -1;
        static bool s_Immersive;

        /// <summary>Whether any XR display subsystem is running. Evaluated at most once per frame.</summary>
        public static bool isImmersive
        {
            get
            {
                if (s_CheckedFrame == Time.frameCount)
                    return s_Immersive;

                s_CheckedFrame = Time.frameCount;
                s_Immersive = false;
                SubsystemManager.GetSubsystems(s_Displays);
                for (var i = 0; i < s_Displays.Count; i++)
                {
                    if (s_Displays[i].running)
                    {
                        s_Immersive = true;
                        break;
                    }
                }

                return s_Immersive;
            }
        }

        /// <summary>True on a standalone headset build, where there is never a mouse and keyboard.</summary>
        public static bool isStandaloneHeadset => Application.platform == RuntimePlatform.Android;
    }
}
