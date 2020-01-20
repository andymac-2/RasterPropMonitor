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
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Text;

namespace JSI
{
    public partial class RPMVesselComputer : VesselModule
    {
        /// This partial module is used to track the many per-module fields in
        /// a vessel.  The original implementation looped every FixedUpdate
        /// over every single part, and every single module in the part, to
        /// track certain values.  By registering for the OnVesselChanged,
        /// OnVesselDestroy, and OnVesselModified events, I can reduce the
        /// need to iterate over _everything_ per FixedUpdate.
        ///
        /// A secondary benefit is that much of the overhead in some of the
        /// JSIInternalButtons methods is outright eliminated.

        private bool listsInvalid = true;
        private readonly static List<string> emptyIgnoreList = new List<string>();

        //--- Docking Nodes
        internal ModuleDockingNode mainDockingNode;
        /// <summary>
        /// Contains the state of the mainDockingNode in a queriable numeric
        /// instead of the string in ModuleDockingNode.  If mainDockingNode is
        /// null, this state is UNKNOWN.
        /// </summary>
        internal DockingNodeState mainDockingNodeState;
        internal enum DockingNodeState
        {
            UNKNOWN,
            DOCKED,
            PREATTACHED,
            READY
        };

        //--- Engines
        internal List<JSIThrustReverser> availableThrustReverser = new List<JSIThrustReverser>();
        internal List<ModuleEngines> availableEngines = new List<ModuleEngines>();
        internal List<MultiModeEngine> availableMultiModeEngines = new List<MultiModeEngine>();
        internal float totalCurrentThrust;
        internal float totalLimitedMaximumThrust;
        internal float totalRawMaximumThrust;
        internal float maxEngineFuelFlow;
        internal float currentEngineFuelFlow;
        internal int currentEngineCount;
        internal int activeEngineCount;
        internal bool anyEnginesFlameout;
        internal bool anyEnginesOverheating;
        internal bool anyEnginesEnabled;
        internal bool anyMmePrimary;
        internal bool anyThrustReversersDeployed;

        //--- Gimbals
        internal List<ModuleGimbal> availableGimbals = new List<ModuleGimbal>();
        internal bool gimbalsLocked;

        //--- Heat shields
        internal List<ModuleAblator> availableAblators = new List<ModuleAblator>();
        internal float heatShieldTemperature;
        internal float heatShieldFlux;

        //--- Intake air
        internal List<ModuleResourceIntake> availableAirIntakes = new List<ModuleResourceIntake>();
        internal float currentAirFlow;
        private static float IntakeAir_U_to_grams;

        //--- Parachutes
        internal List<ModuleParachute> availableParachutes = new List<ModuleParachute>();
        internal List<PartModule> availableRealChutes = new List<PartModule>();
        internal bool anyParachutesDeployed;
        internal bool allParachutesSafe;

		//--- Power production
		internal ElectricalSystem electricalSystem;

        //--- Radar
        internal List<JSIRadar> availableRadars = new List<JSIRadar>();
        internal bool radarActive;

        //--- Wheels
        internal List<ModuleWheels.ModuleWheelDeployment> availableDeployableWheels = new List<ModuleWheels.ModuleWheelDeployment>();
        internal List<ModuleWheels.ModuleWheelBrakes> availableWheelBrakes = new List<ModuleWheels.ModuleWheelBrakes>();
        internal List<ModuleWheels.ModuleWheelDamage> availableWheelDamage = new List<ModuleWheels.ModuleWheelDamage>();
        internal bool wheelsDamaged;
        internal bool wheelsRepairable;
        internal float wheelBrakeSetting;
        internal float wheelStress;
        internal int gearState;
        internal float gearPosition;

        #region List Management
        /// <summary>
        /// Flag the lists as invalid due to craft changes / destruction.
        /// </summary>
        internal void InvalidateModuleLists()
        {
            listsInvalid = true;

            availableAblators.Clear();
            availableAirIntakes.Clear();
            availableDeployableWheels.Clear();
            availableEngines.Clear();
            availableGimbals.Clear();
            availableMultiModeEngines.Clear();
            availableParachutes.Clear();
            availableRadars.Clear();
            availableRealChutes.Clear();
            availableThrustReverser.Clear();
            availableWheelBrakes.Clear();
            availableWheelDamage.Clear();
			electricalSystem.Clear();

            mainDockingNode = null;
        }

        /// <summary>
        /// Iterate over all of the modules in all of the parts and filter then
        /// into the myriad lists we keep, so when we do the FixedUpdate refresh
        /// of values, we only iterate over the modules we know we care about,
        /// instead of every module on every part.
        /// </summary>
        internal void UpdateModuleLists()
        {
            if (listsInvalid && vessel != null)
            {
                resources.ClearActiveStageParts();
                var partsList = vessel.parts;
                for (int partsIdx = 0; partsIdx < partsList.Count; ++partsIdx)
                {
                    string partName = partsList[partsIdx].partInfo.name;
                    if (!RPMGlobals.ignoreAllPartModules.Contains(partName))
                    {
                        List<string> modulesToIgnore;
                        if (!RPMGlobals.ignorePartModules.TryGetValue(partName, out modulesToIgnore))
                        {
                            modulesToIgnore = emptyIgnoreList;
                        }
                        foreach (PartModule module in partsList[partsIdx].Modules)
                        {
                            if (module.isEnabled && !modulesToIgnore.Contains(module.moduleName))
                            {
                                if (module is ModuleEngines)
                                {
                                    availableEngines.Add(module as ModuleEngines);
                                }
                                else if (module is MultiModeEngine)
                                {
                                    availableMultiModeEngines.Add(module as MultiModeEngine);
                                }
                                else if (module is JSIThrustReverser)
                                {
                                    availableThrustReverser.Add(module as JSIThrustReverser);
                                }
                                else if (module is ModuleAblator)
                                {
                                    availableAblators.Add(module as ModuleAblator);
                                }
                                else if (module is ModuleResourceIntake)
                                {
                                    if ((module as ModuleResourceIntake).resourceName == "IntakeAir")
                                    {
                                        availableAirIntakes.Add(module as ModuleResourceIntake);
                                    }
                                    else
                                    {
                                        JUtil.LogMessage(this, "intake resource is {0}?", (module as ModuleResourceIntake).resourceName);
                                    }
                                }
								else if (electricalSystem.ConsiderModule(module))
								{
									// handled
								}
                                else if (module is ModuleGimbal)
                                {
                                    availableGimbals.Add(module as ModuleGimbal);
                                }
                                else if (module is JSIRadar)
                                {
                                    availableRadars.Add(module as JSIRadar);
                                }
                                else if (module is ModuleParachute)
                                {
                                    availableParachutes.Add(module as ModuleParachute);
                                }
                                else if (module is ModuleWheels.ModuleWheelDeployment)
                                {
                                    availableDeployableWheels.Add(module as ModuleWheels.ModuleWheelDeployment);
                                }
                                else if (module is ModuleWheels.ModuleWheelDamage)
                                {
                                    availableWheelDamage.Add(module as ModuleWheels.ModuleWheelDamage);
                                }
                                else if (module is ModuleWheels.ModuleWheelBrakes)
                                {
                                    availableWheelBrakes.Add(module as ModuleWheels.ModuleWheelBrakes);
                                }
                                else if (JSIParachute.rcFound && module.GetType() == JSIParachute.rcModuleRealChute)
                                {
                                    availableRealChutes.Add(module);
                                }
                            }
                        }
                    }

                    if (vessel.currentStage <= partsList[partsIdx].inverseStage)
                    {
                        JUtil.LogMessage(this, "+ stage = {0}, part invsStage = {1} for {2}", vessel.currentStage, partsList[partsIdx].inverseStage, partsList[partsIdx].partInfo.title);
                        resources.MarkActiveStage(partsList[partsIdx].crossfeedPartSet);
                    }
                    else
                    {
                        JUtil.LogMessage(this, "- stage = {0}, part invsStage = {1} for {2}", vessel.currentStage, partsList[partsIdx].inverseStage, partsList[partsIdx].partInfo.title);
                    }

                }

                listsInvalid = false;
            }
        }

        /// <summary>
        /// Refresh ablator-specific fields (hottest ablator and flux).
        /// </summary>
        private void FetchAblatorData()
        {
            heatShieldTemperature = heatShieldFlux = 0.0f;
            float hottestShield = float.MinValue;

            for (int i = 0; i < availableAblators.Count; ++i)
            {
                Part thatPart = availableAblators[i].part;
                // Even though the interior contains a lot of heat, I think ablation is based on skin temp.
                // Although it seems odd that the skin temp quickly cools off after re-entry, while the
                // interior temp doesn't move cool much (for instance, I saw a peak ablator skin temp
                // of 950K, while the interior eventually reached 345K after the ablator had cooled below
                // 390K.  By the time the capsule landed, skin temp matched exterior temp (304K) but the
                // interior still held 323K.
                if (thatPart.skinTemperature - availableAblators[i].ablationTempThresh > hottestShield)
                {
                    hottestShield = (float)(thatPart.skinTemperature - availableAblators[i].ablationTempThresh);
                    heatShieldTemperature = (float)(thatPart.skinTemperature);
                    heatShieldFlux = (float)(thatPart.thermalConvectionFlux + thatPart.thermalRadiationFlux);
                }

            }
        }

        /// <summary>
        /// Refresh airflow rate (g/s).
        /// </summary>
        private void FetchAirIntakeData()
        {
            currentAirFlow = 0.0f;

            for (int i = 0; i < availableAirIntakes.Count; ++i)
            {
                if (availableAirIntakes[i].enabled)
                {
                    currentAirFlow += availableAirIntakes[i].airFlow;
                }
            }

            // Convert airflow from U to g/s, same as fuel flow.
            currentAirFlow *= IntakeAir_U_to_grams;
        }

        /// <summary>
        /// Convert the textual docking node state into an enum, so we don't
        /// need to do string compares.
        /// </summary>
        /// <param name="whichNode"></param>
        /// <returns></returns>
        internal static DockingNodeState GetNodeState(ModuleDockingNode whichNode)
        {
            if (whichNode == null)
            {
                return DockingNodeState.UNKNOWN;
            }

            switch (whichNode.state)
            {
                case "PreAttached":
                    return DockingNodeState.PREATTACHED;
                case "Docked (docker)":
                    return DockingNodeState.DOCKED;
                case "Docked (dockee)":
                    return DockingNodeState.DOCKED;
                case "Ready":
                    return DockingNodeState.READY;
                default:
                    return DockingNodeState.UNKNOWN;
            }
        }

        /// <summary>
        /// Refresh docking node data, including selecting the "reference"
        /// docking node (for docking node control).
        /// </summary>
        private void FetchDockingNodeData()
        {
            mainDockingNode = null;
            mainDockingNodeState = DockingNodeState.UNKNOWN;

            Part referencePart = vessel.GetReferenceTransformPart();
            if (referencePart != null)
            {
                ModuleDockingNode node = referencePart.FindModuleImplementing<ModuleDockingNode>();
                if (node != null)
                {
                    // The current reference part is a docking node, so we
                    // choose it.
                    mainDockingNode = node;
                }
            }

            if (mainDockingNode == null)
            {
                uint launchId;
                Part currentPart = JUtil.DeduceCurrentPart(vessel);
                if (currentPart == null)
                {
                    launchId = 0u;
                }
                else
                {
                    launchId = currentPart.launchID;
                }

                for (int i = 0; i < vessel.parts.Count; ++i)
                {
                    if (vessel.parts[i].launchID == launchId)
                    {
                        ModuleDockingNode node = vessel.parts[i].FindModuleImplementing<ModuleDockingNode>();
                        if (node != null)
                        {
                            // We found a docking node that has the same launch
                            // ID as the current IVA part, so we consider it our
                            // main docking node.
                            mainDockingNode = node;
                            break;
                        }
                    }
                }
            }

            mainDockingNodeState = GetNodeState(mainDockingNode);
        }

        /// <summary>
        /// Refresh engine data: current thrust, max thrust, max raw
        /// thrust (without throttle limits), current and max fuel flow,
        /// hottest engine temperature and limit, current and max ISP,
        /// boolean flags of engine states.
        /// </summary>
        /// <returns>true if something changed that warrants a reset
        /// of tracked modules.</returns>
        private bool FetchEngineData()
        {
            if (availableMultiModeEngines.Count == 0)
            {
                anyMmePrimary = true;
            }
            else
            {
                anyMmePrimary = false;
                for (int i = 0; i < availableMultiModeEngines.Count; ++i)
                {
                    if (availableMultiModeEngines[i].runningPrimary)
                    {
                        anyMmePrimary = true;
                    }
                }
            }

            anyThrustReversersDeployed = false;
            for (int i = 0; i < availableThrustReverser.Count; ++i)
            {
                if (availableThrustReverser[i].thrustReverser != null)
                {
                    anyThrustReversersDeployed |= (availableThrustReverser[i].thrustReverser.Progress > 0.5f);
                }
            }

            // Per-engine values
            currentEngineCount = 0;
            totalCurrentThrust = totalLimitedMaximumThrust = totalRawMaximumThrust = 0.0f;
            maxEngineFuelFlow = currentEngineFuelFlow = 0.0f;
            float hottestEngine = float.MaxValue;
            hottestEngineTemperature = hottestEngineMaxTemperature = 0.0f;
            anyEnginesOverheating = anyEnginesFlameout = anyEnginesEnabled = false;

            float averageIspContribution = 0.0f;
            float maxIspContribution = 0.0f;
            List<Part> visitedParts = new List<Part>();

            bool requestReset = false;
            for (int i = 0; i < availableEngines.Count; ++i)
            {
                requestReset |= (!availableEngines[i].isEnabled);

                Part thatPart = availableEngines[i].part;
                if (thatPart.inverseStage == StageManager.CurrentStage)
                {
                    if (!visitedParts.Contains(thatPart))
                    {
                        currentEngineCount++;
                        if (availableEngines[i].getIgnitionState)
                        {
                            activeEngineCount++;
                        }
                        visitedParts.Add(thatPart);
                    }
                }

                anyEnginesOverheating |= (thatPart.skinTemperature / thatPart.skinMaxTemp > 0.9) || (thatPart.temperature / thatPart.maxTemp > 0.9);
                anyEnginesEnabled |= availableEngines[i].allowShutdown && availableEngines[i].getIgnitionState;
                anyEnginesFlameout |= (availableEngines[i].isActiveAndEnabled && availableEngines[i].flameout);

                float currentThrust = GetCurrentThrust(availableEngines[i]);
                totalCurrentThrust += currentThrust;
                float rawMaxThrust = GetMaximumThrust(availableEngines[i]);
                totalRawMaximumThrust += rawMaxThrust;
                float maxThrust = rawMaxThrust * availableEngines[i].thrustPercentage * 0.01f;
                totalLimitedMaximumThrust += maxThrust;
                float realIsp = GetRealIsp(availableEngines[i]);
                if (realIsp > 0.0f)
                {
                    averageIspContribution += maxThrust / realIsp;

                    // Compute specific fuel consumption and
                    // multiply by thrust to get grams/sec fuel flow
                    float specificFuelConsumption = 101972f / realIsp;
                    maxEngineFuelFlow += specificFuelConsumption * rawMaxThrust;
                    currentEngineFuelFlow += specificFuelConsumption * currentThrust;
                }

                foreach (Propellant thatResource in availableEngines[i].propellants)
                {
                    resources.MarkPropellant(thatResource);
                }

                float minIsp, maxIsp;
                availableEngines[i].atmosphereCurve.FindMinMaxValue(out minIsp, out maxIsp);
                if (maxIsp > 0.0f)
                {
                    maxIspContribution += maxThrust / maxIsp;
                }

                if (thatPart.skinMaxTemp - thatPart.skinTemperature < hottestEngine)
                {
                    hottestEngineTemperature = (float)thatPart.skinTemperature;
                    hottestEngineMaxTemperature = (float)thatPart.skinMaxTemp;
                    hottestEngine = hottestEngineMaxTemperature - hottestEngineTemperature;
                }
                if (thatPart.maxTemp - thatPart.temperature < hottestEngine)
                {
                    hottestEngineTemperature = (float)thatPart.temperature;
                    hottestEngineMaxTemperature = (float)thatPart.maxTemp;
                    hottestEngine = hottestEngineMaxTemperature - hottestEngineTemperature;
                }
            }

            if (averageIspContribution > 0.0f)
            {
                actualAverageIsp = totalLimitedMaximumThrust / averageIspContribution;
            }
            else
            {
                actualAverageIsp = 0.0f;
            }

            if (maxIspContribution > 0.0f)
            {
                actualMaxIsp = totalLimitedMaximumThrust / maxIspContribution;
            }
            else
            {
                actualMaxIsp = 0.0f;
            }

            resources.EndLoop(Planetarium.GetUniversalTime());

            return requestReset;
        }

        /// <summary>
        /// Refresh gimbal data: any gimbals locked.
        /// </summary>
        private void FetchGimbalData()
        {
            gimbalsLocked = false;

            for (int i = 0; i < availableGimbals.Count; ++i)
            {
                gimbalsLocked |= availableGimbals[i].gimbalLock;
            }
        }

        /// <summary>
        /// Refresh parachute data
        /// </summary>
        private void FetchParachuteData()
        {
            anyParachutesDeployed = false;
            allParachutesSafe = true;

            for (int i = 0; i < availableParachutes.Count; ++i)
            {
                if (availableParachutes[i].deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED || availableParachutes[i].deploymentState == ModuleParachute.deploymentStates.DEPLOYED)
                {
                    anyParachutesDeployed = true;
                }

                if (availableParachutes[i].deploySafe != "Safe")
                {
                    allParachutesSafe = false;
                }
            }
        }

        /// <summary>
        /// Refresh radar data: any radar active.
        /// </summary>
        private void FetchRadarData()
        {
            radarActive = false;

            for (int i = 0; i < availableRadars.Count; ++i)
            {
                radarActive |= availableRadars[i].radarEnabled;
            }
        }

        /// <summary>
        /// Refresh wheel data: current landing gear deployment state, wheel
        /// damage state, brake settings.
        /// </summary>
        private void FetchWheelData()
        {
            gearState = -1;
            gearPosition = 0.0f;

            for (int i = 0; i < availableDeployableWheels.Count; ++i)
            {
                if (gearState == -1 || gearState == 4)
                {
                    try
                    {
                        gearPosition = UnityEngine.Mathf.Lerp(availableDeployableWheels[i].retractedPosition, availableDeployableWheels[i].deployedPosition, availableDeployableWheels[i].position);
                        var state = availableDeployableWheels[i].fsm.CurrentState;
                        if (state == availableDeployableWheels[i].st_deployed)
                        {
                            gearState = 1;
                        }
                        else if (state == availableDeployableWheels[i].st_retracted)
                        {
                            gearState = 0;
                        }
                        else if (state == availableDeployableWheels[i].st_deploying)
                        {
                            gearState = 3;
                        }
                        else if (state == availableDeployableWheels[i].st_retracting)
                        {
                            gearState = 2;
                        }
                        else if (state == availableDeployableWheels[i].st_inoperable)
                        {
                            gearState = 4;
                        }
                    }
                    catch { }
                }
            }

            wheelsDamaged = wheelsRepairable = false;
            wheelStress = 0.0f;

            for (int i = 0; i < availableWheelDamage.Count; ++i)
            {
                wheelsDamaged |= availableWheelDamage[i].isDamaged;
                wheelsRepairable |= availableWheelDamage[i].isRepairable;
                wheelStress = Math.Max(wheelStress, availableWheelDamage[i].stressPercent);
            }

            wheelBrakeSetting = 0.0f;
            if (availableWheelBrakes.Count > 0)
            {
                for (int i = 0; i < availableWheelBrakes.Count; ++i)
                {
                    wheelBrakeSetting += availableWheelBrakes[i].brakeTweakable;
                }
                wheelBrakeSetting /= (float)availableWheelBrakes.Count;
            }
        }

        /// <summary>
        /// Master update method.
        /// </summary>
        internal void FetchPerModuleData()
        {
            if (vessel == null)
            {
                return;
            }

            UpdateModuleLists();

            bool requestReset = false;
            FetchAblatorData();
            FetchAirIntakeData();
            FetchDockingNodeData();
			electricalSystem.Update();
			requestReset |= FetchEngineData();
            FetchGimbalData();
            FetchParachuteData();
            FetchRadarData();
            FetchWheelData();

            if (requestReset)
            {
                InvalidateModuleLists();
            }
        }
        #endregion

        #region Interface

        /// <summary>
        /// Toggle the state of any engines that we can control (currently-staged
        /// engines, or engines that are on if we are turning them off).
        /// </summary>
        /// <param name="state"></param>
        internal void SetEnableEngines(bool state)
        {
            for (int i = 0; i < availableEngines.Count; ++i)
            {
                Part thatPart = availableEngines[i].part;

                // The first line allows to start engines of the first stage before the initial launch 
                if ((StageManager.CurrentStage == StageManager.StageCount && thatPart.inverseStage == StageManager.StageCount - 1) ||
                    thatPart.inverseStage == StageManager.CurrentStage || !state)
                {
                    if (availableEngines[i].EngineIgnited != state)
                    {
                        if (state && availableEngines[i].allowRestart)
                        {
                            availableEngines[i].Activate();
                        }
                        else if (availableEngines[i].allowShutdown)
                        {
                            availableEngines[i].Shutdown();
                        }
                    }
                }
            }
        }

        #endregion
    }
}
