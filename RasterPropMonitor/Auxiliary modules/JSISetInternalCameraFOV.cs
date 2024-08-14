/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/
using System;
using System.Collections.Generic;
using UniLinq;
using UnityEngine;

namespace JSI
{
    public class JSISetInternalCameraFOV : InternalModule
    {
        public enum HideKerbal
        {
            none,
            head,
            all
        };

        private int oldSeat = -1;

        private struct SeatCamera
        {
            public float fov;
            public float maxRot;
            public float maxPitch;
            public float minPitch;
            public HideKerbal hideKerbal;

            public SeatCamera(ConfigNode node)
            {
                fov = node.GetFloat(nameof(fov)) ?? defaultFov;
                maxRot = node.GetFloat(nameof(maxRot)) ?? defaultMaxRot;
                maxPitch = node.GetFloat(nameof(maxPitch)) ?? defaultMaxPitch;
                minPitch = node.GetFloat(nameof(minPitch)) ?? defaultMinPitch;
                hideKerbal = defaultHideKerbal;
                node.TryGetEnum(nameof(hideKerbal), ref hideKerbal, defaultHideKerbal);
            }
        }

        // per-internalmodel data that is shared between all instances of the model
        private class InternalModelSeatMetadata : ScriptableObject
        {
            public SeatCamera[] seats;
        }
        
        [SerializeReference]
        private InternalModelSeatMetadata seatMetadata;

        private const float defaultFov = 60f;
        private const float defaultMaxRot = 60f;
        private const float defaultMaxPitch = 60f;
        private const float defaultMinPitch = -50f;
        private const HideKerbal defaultHideKerbal = HideKerbal.none;

        #region Loading Code

        static Dictionary<string, ConfigNode> propDefinitionSeatModuleConfigs;

        private static ConfigNode GetInternalSeatConfigNode(ConfigNode propNode)
        {
            foreach (var childNode in propNode.nodes.nodes)
            {
                if (childNode.name == "MODULE" && childNode.GetValue("name") == nameof(InternalSeat))
                {
                    return childNode;
                }
            }
            return null;
        }

        private static ConfigNode GetInternalSeatConfigNodeFromProp(ConfigNode propNode)
        {
            var seatNode = GetInternalSeatConfigNode(propNode);
            if (seatNode != null) return seatNode;

            // on the first call, we need to go find all of the InternalSeat module config nodes in prop definitions
            if (propDefinitionSeatModuleConfigs == null)
            {
                propDefinitionSeatModuleConfigs = new Dictionary<string, ConfigNode>();

                foreach (var propDefinitionNode in GameDatabase.Instance.GetConfigNodes("PROP"))
                {
                    seatNode = GetInternalSeatConfigNode(propDefinitionNode);
                    if (seatNode != null)
                    {
                        propDefinitionSeatModuleConfigs.Add(propDefinitionNode.GetValue("name"), seatNode);
                    }
                }
            }

            propDefinitionSeatModuleConfigs.TryGetValue(propNode.GetValue("name"), out seatNode);
            return seatNode;
        }

        private void OnDisable()
        {
            // Dirty hack: this is called during loading after the prefab is completely set up.
            // This is how we can hook into final processing for all the seats.
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                seatMetadata = ScriptableObject.CreateInstance<InternalModelSeatMetadata>();
                var seats = seatMetadata.seats = new SeatCamera[internalModel.seats.Count];

                int currentSeatIndex = 0;

                // unfortunately props don't store a reference to their confignode, so we need to iterate over prop and module nodes that might have seats
                for (int nodeIndex = 0; nodeIndex < internalModel.internalConfig.nodes.nodes.Count; ++nodeIndex)
                {
                    var childNode = internalModel.internalConfig.nodes.nodes[nodeIndex];

                    // if this is a prop, it could contain an internalseat module directly, or in its prop definition
                    if (childNode.name == "PROP")
                    {
                        childNode = GetInternalSeatConfigNodeFromProp(childNode);
                        if (childNode == null) continue;
                    }

                    // does this thing represent a seat, one way or another?
                    if (childNode.name == "MODULE" && childNode.GetValue("name") == "InternalSeat")
                    {
                        if (currentSeatIndex < seats.Length)
                        {
                            var seatData = new SeatCamera(childNode);
                            seats[currentSeatIndex] = seatData;
                            
                            JUtil.LogMessage(this, "Setting per-seat camera parameters for seat {0}: fov {1}, maxRot {2}, maxPitch {3}, minPitch {4}, hideKerbal {5}",
                                currentSeatIndex, seatData.fov, seatData.maxRot, seatData.maxPitch, seatData.minPitch, seatData.hideKerbal.ToString());
                        }

                        ++currentSeatIndex;
                    }
                }

                if (currentSeatIndex != seats.Length)
                {
                    JUtil.LogErrorMessage(this, "Internal {0} has {1} seats but found configs for {2} seat modules", internalModel.internalName, seats.Length, currentSeatIndex);
                }
            }
        }

        #endregion

        public void Start()
        {
            GameEvents.OnCameraChange.Add(OnCameraChange);
            GameEvents.OnIVACameraKerbalChange.Add(OnIVACameraChange);
 
            // If (somehow) we start in IVA, make sure we initialize here.
            if (CameraManager.Instance.activeInternalPart == part)
            {
                Kerbal activeKerbal = CameraManager.Instance.IVACameraActiveKerbal;
                int seatID;
                if (activeKerbal == null)
                {
                    seatID = -1;
                }
                else
                {
                    seatID = activeKerbal.protoCrewMember.seatIdx;
                }

                UpdateCameras(seatID, activeKerbal);
            }
        }

        /// <summary>
        /// Unregister those callbacks
        /// </summary>
        public void OnDestroy()
        {
            GameEvents.OnIVACameraKerbalChange.Remove(OnIVACameraChange);
            GameEvents.OnCameraChange.Remove(OnCameraChange);
        }

        /// <summary>
        /// If the camera mode changes, we need to reset our local cache.
        /// </summary>
        /// <param name="newMode"></param>
        private void OnCameraChange(CameraManager.CameraMode newMode)
        {
            if (CameraManager.Instance.activeInternalPart == part)
            {
                Kerbal activeKerbal = CameraManager.Instance.IVACameraActiveKerbal;
                if (activeKerbal != null)
                {
                    int seatID = activeKerbal.protoCrewMember.seatIdx;
                    if (seatID != oldSeat)
                    {
                        UpdateCameras(seatID, activeKerbal);
                    }
                }
            }
            else
            {
                oldSeat = -1;
            }
        }

        /// <summary>
        /// Take care of updating everything.
        /// </summary>
        /// <param name="seatID"></param>
        /// <param name="activeKerbal"></param>
        private void UpdateCameras(int seatID, Kerbal activeKerbal)
        {
            var seatData = seatMetadata.seats[seatID];

            InternalCamera.Instance.SetFOV(seatData.fov);
            InternalCamera.Instance.maxRot = seatData.maxRot;
            InternalCamera.Instance.maxPitch = seatData.maxPitch;
            InternalCamera.Instance.minPitch = seatData.minPitch;

            RPMVesselComputer comp = null;
            if (RPMVesselComputer.TryGetInstance(vessel, ref comp))
            {
                comp.SetKerbalVisible(activeKerbal, seatData.hideKerbal);
            }

            oldSeat = seatID;
        }

        /// <summary>
        /// Callback when the player switches IVA camera.
        /// 
        /// BUG: The callback's parameter tells me who the
        /// previous Kerbal was, not who the new Kerbal is.
        /// </summary>
        /// <param name="newKerbal"></param>
        private void OnIVACameraChange(Kerbal newKerbal)
        {
            // Unfortunately, the callback is telling me who the previous Kerbal was,
            // not who the new Kerbal is.
            Kerbal activeKerbal = CameraManager.Instance.IVACameraActiveKerbal;
            if (activeKerbal != null && CameraManager.Instance.activeInternalPart == part)
            {
                int seatID = activeKerbal.protoCrewMember.seatIdx;
                if (seatID != oldSeat)
                {
                    UpdateCameras(seatID, activeKerbal);
                }
            }
        }
    }
}
