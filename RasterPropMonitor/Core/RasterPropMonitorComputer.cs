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
using System.Text.RegularExpressions;
using UnityEngine;

namespace JSI
{
    // MOARdV TODO:
    // Crew:
    // onCrewBoardVessel
    // onCrewOnEva
    // onCrewTransferred

    /// <summary>
    /// The computer for the pod. This class can be used for shared data across
    /// different screens in the same pod.
    /// </summary>
    public partial class RasterPropMonitorComputer : PartModule
    {
        // The only public configuration variable.
        [KSPField]
        public string storedStrings = string.Empty;

        // The OTHER public configuration variable.
        [KSPField]
        public string triggeredEvents = string.Empty;

        // Vessel description storage and related code.
        [KSPField(isPersistant = true)]
        public string vesselDescription = string.Empty;
        private string vesselDescriptionForDisplay = string.Empty;
        private static readonly string editorNewline = ((char)0x0a).ToString();
        private string lastVesselDescription = string.Empty;

        internal readonly string[] actionGroupMemo = {
            "AG0",
            "AG1",
            "AG2",
            "AG3",
            "AG4",
            "AG5",
            "AG6",
            "AG7",
            "AG8",
            "AG9"
        };

        internal List<string> storedStringsArray = new List<string>();

        // Local variables
        private ManeuverNode node;
        private bool orbitSensibility;

        private List<ProtoCrewMember> localCrew = new List<ProtoCrewMember>();
        private List<kerbalExpressionSystem> localCrewMedical = new List<kerbalExpressionSystem>();

        private readonly VariableCollection variableCollection = new VariableCollection();
        private readonly List<IJSIModule> installedModules = new List<IJSIModule>();
        private readonly HashSet<string> unrecognizedVariables = new HashSet<string>();
        private readonly Dictionary<string, IComplexVariable> customVariables = new Dictionary<string, IComplexVariable>();

        private class PeriodicRandomValue
        {
            internal readonly int period;
            internal int counter;
            internal float value;

            internal PeriodicRandomValue(int period_)
            {
                value = UnityEngine.Random.value;
                period = period_;
                counter = period;
            }
        }
        private readonly List<PeriodicRandomValue> periodicRandomVals = new List<PeriodicRandomValue>();

        // Data refresh
        private int dataUpdateCountdown;
        private int refreshDataRate = 60;

        // Diagnostics
        private int debug_fixedUpdates = 0;
        private DefaultableDictionary<string, int> debug_callCount = new DefaultableDictionary<string, int>(0);

        [KSPField(isPersistant = true)]
        public string RPMCid = string.Empty;
        private Guid id = Guid.Empty;
        /// <summary>
        /// The Guid of the vessel to which we belong.  We update this very
        /// obsessively to avoid it being out-of-sync with our craft.
        /// </summary>
        private Guid vid = Guid.Empty;

        private ExternalVariableHandlers plugins = null;
        internal Dictionary<string, Color32> overrideColors = new Dictionary<string, Color32>();
        private int selectedPatchIndex;

        static readonly Regex x_agmemoRegex = new Regex("^AG([0-9])\\s*=\\s*(.*)\\s*");

        public static RasterPropMonitorComputer FindFromProp(InternalProp prop)
        {
            var rpmc = prop.part.FindModuleImplementing<RasterPropMonitorComputer>();

            if (rpmc == null)
            {
                JUtil.LogErrorMessage(null, "No RasterPropMonitorComputer module found on part {0} for prop {1} in internal {2}", prop.part.partInfo.name, prop.propName, prop.internalModel.internalName);
            }

            return rpmc;
        }

        // Page handler interface for vessel description page.
        // Analysis disable UnusedParameter
        public string VesselDescriptionRaw(int screenWidth, int screenHeight)
        {
            // Analysis restore UnusedParameter
            return vesselDescriptionForDisplay.UnMangleConfigText();
        }

        // Analysis disable UnusedParameter
        public string VesselDescriptionWordwrapped(int screenWidth, int screenHeight)
        {
            // Analysis restore UnusedParameter
            return JUtil.WordWrap(vesselDescriptionForDisplay.UnMangleConfigText(), screenWidth);
        }

        /// <summary>
        /// Register a callback to receive notifications when a variable has changed.
        /// Used to prevent polling of low-frequency, high-utilization variables.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="cb"></param>
        public void RegisterVariableCallback(string variableName, Action<float> cb)
        {
            var vc = InstantiateVariableOrNumber(variableName);
            vc.onChangeCallbacks += cb;
            cb(vc.AsFloat());
        }

        /// <summary>
        /// Unregister a callback for receiving variable update notifications.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="cb"></param>
        public void UnregisterVariableCallback(string variableName, Action<float> cb)
        {
            var vc = variableCollection.GetVariable(variableName);
            if (vc != null)
            {
                vc.onChangeCallbacks -= cb;
            }
        }

        /// <summary>
        /// Register for a resource callback.  Resource callbacks provide a boolean that is
        /// updated when the named resource drops above or below 0.01f.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="cb"></param>
        public void RegisterResourceCallback(string variableName, Action<bool> cb)
        {
            var vc = InstantiateVariableOrNumber(variableName);
            vc.onResourceDepletedCallbacks += cb;
            cb(vc.AsDouble() < 0.01);
        }

        /// <summary>
        /// Remove a previously-registered resource change callback
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="cb"></param>
        public void UnregisterResourceCallback(string variableName, Action<bool> cb)
        {
            var vc = variableCollection?.GetVariable(variableName);
            if (vc != null)
            {
                vc.onResourceDepletedCallbacks -= cb;
            }
        }

        /// <summary>
        /// Instantiate a VariableOrNumber object attached to this computer, or
        /// return a reference to an existing one.
        /// </summary>
        /// <param name="variableName">Name of the variable</param>
        /// <returns>The VariableOrNumber</returns>
        public VariableOrNumber InstantiateVariableOrNumber(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName)) return null;

            variableName = variableName.Trim();
            var variable = variableCollection.GetVariable(variableName);
            if (variable == null)
            {
                variable = AddVariable(variableName);
            }

            return variable;
        }

        /// <summary>
        /// Add a variable to the VariableOrNumber
        /// </summary>
        /// <param name="variableName"></param>
        private VariableOrNumber AddVariable(string variableName)
        {
            RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
            VariableOrNumber vc;
            
            // try to find a numeric evaluator first
            var numericEvaluator = GetNumericEvaluator(variableName, out VariableUpdateType updateType);
            if (numericEvaluator != null)
            {
                vc = new VariableOrNumber(variableName, numericEvaluator, comp, updateType, updateType == VariableUpdateType.Volatile ? this : null);
            }
            else
            {
                // if that doesnt' work, look for a generic one
                var evaluator = GetEvaluator(variableName, out updateType);
                if (evaluator == null) updateType = VariableUpdateType.Constant;
                vc = new VariableOrNumber(variableName, evaluator, comp, updateType, updateType == VariableUpdateType.Volatile ? this : null);

                if (evaluator == null && !unrecognizedVariables.Contains(variableName))
                {
                    unrecognizedVariables.Add(variableName);
                    JUtil.LogErrorMessage(this, "Unrecognized variable {0}", variableName);
                }
            }

            variableCollection.AddVariable(vc);

            return vc;
        }

        public override void OnLoad(ConfigNode node)
        {
            m_persistentVariables.Load(node);

            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                foreach (var overrideColorSetup in node.GetNodes("RPM_COLOROVERRIDE"))
                {
                    foreach (var colorConfig in overrideColorSetup.GetNodes("COLORDEFINITION"))
                    {
                        string name = colorConfig.GetValue("name");
                        Color32 color = default(Color);

                        if (name != null && colorConfig.TryGetValue("color", ref color))
                        {
                            name = "COLOR_" + name.Trim();

                            overrideColors[name] = color;
                        }
                    }
                }
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                var modulePrefab = part.partInfo.partPrefab.FindModuleImplementing<RasterPropMonitorComputer>();
                if (modulePrefab != null)
                {
                    overrideColors = modulePrefab.overrideColors;
                }
            }
        }

        public override void OnSave(ConfigNode node)
        {
            m_persistentVariables.Save(node);
        }

        /// <summary>
        /// Set the refresh rate (number of Update() calls per triggered update).
        /// The lower of the current data rate and the new data rate is used.
        /// </summary>
        /// <param name="newDataRate">New data rate</param>
        internal void UpdateDataRefreshRate(int newDataRate)
        {
            refreshDataRate = Math.Max(RPMGlobals.minimumRefreshRate, Math.Min(newDataRate, refreshDataRate));

            RPMVesselComputer comp = null;
            if (RPMVesselComputer.TryGetInstance(vessel, ref comp))
            {
                comp.UpdateDataRefreshRate(newDataRate);
            }
        }

        /// <summary>
        /// Clear out variables to force them to be re-evaluated.  TODO: Do
        /// I clear out the VariableOrNumber?
        /// </summary>
        private void ClearVariables()
        {
            sideSlipEvaluator = null;
            angleOfAttackEvaluator = null;
        }

        // provide a way for internal modules to remove themselves temporarily from InternalProps
        // this allows modules to "go to sleep" so we don't spend time updating them

        private List<InternalModule> modulesToRemove = new List<InternalModule>();
        private List<InternalModule> modulesToRestore = new List<InternalModule>();

        public void RemoveInternalModule(InternalModule module)
        {
            modulesToRemove.Add(module);
        }

        public void RestoreInternalModule(InternalModule module)
        {
            modulesToRestore.Add(module);
        }

        /// <summary>
        /// Find the selected orbital patch. A patch is selected if we are
        /// looking at it.
        /// </summary>
        /// <returns>
        /// 1. The count of the patch. 0 for current orbit, 1 for next SOI, and
        ///     so on
        /// 2. The orbit object that represents the patch.
        /// </returns>
        internal (int, Orbit) GetSelectedPatch()
        {
            return EffectivePatch(selectedPatchIndex);
        }

        private Orbit GetSelectedPatchOrbit()
        {
            (int _, Orbit patch) = GetSelectedPatch();
            return patch;
        }

        internal (int, Orbit) GetLastPatch()
        {
            return EffectivePatch(1000);
        }

        internal void SelectNextPatch()
        {
            (int effectivePatchIndex, _) = GetSelectedPatch();
            SelectPatch(effectivePatchIndex + 1);
        }

        internal void SelectPreviousPatch()
        {
            (int effectivePatchIndex, _) = GetSelectedPatch();
            SelectPatch(effectivePatchIndex - 1);
        }

        private void SelectPatch(int patchIndex)
        {
            (int effectivePatchIndex, _) = EffectivePatch(patchIndex);
            selectedPatchIndex = effectivePatchIndex;
        }

        /// <summary>
        /// Returns the orbit (patch) and orbit index given a selection.
        /// </summary>
        /// <returns>true if it's time to update things</returns>
        private (int, Orbit) EffectivePatch(int patchIndex)
        {
            Orbit patch = vessel.orbit;
            int effectivePatchIndex = 0;
            while (effectivePatchIndex < patchIndex
                && patch.nextPatch != null
                && patch.nextPatch.activePatch
                && (patch.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER || patch.patchEndTransition == Orbit.PatchTransitionType.ESCAPE))
            {
                patch = patch.nextPatch;
                effectivePatchIndex++;
            }

            return (effectivePatchIndex, patch);
        }

        #region Monobehaviour
        /// <summary>
        /// Configure this computer for operation.
        /// </summary>
        public void Start()
        {
            if (!HighLogic.LoadedSceneIsEditor)
            {
                vid = vessel.id;
                refreshDataRate = RPMGlobals.defaultRefreshRate;

                GameEvents.onVesselWasModified.Add(onVesselWasModified);
                GameEvents.onVesselChange.Add(onVesselChange);
                GameEvents.onVesselCrewWasModified.Add(onVesselCrewWasModified);

                IJSIModule.CreateJSIModules(installedModules, vessel);

#if ENABLE_ENGINE_MONITOR
                installedModules.Add(new JSIEngine(vessel));
#endif

                if (string.IsNullOrEmpty(RPMCid))
                {
                    id = Guid.NewGuid();
                    RPMCid = id.ToString();
                    if (part.internalModel != null)
                    {
                        JUtil.LogMessage(this, "Start: Creating GUID {0} in {1}", id, part.internalModel.internalName);
                    }
                    else
                    {
                        JUtil.LogMessage(this, "Start: Creating GUID {0}", id);
                    }
                }
                else
                {
                    id = new Guid(RPMCid);
                    if (part.internalModel != null)
                    {
                        JUtil.LogMessage(this, "Start: Loading GUID string {0} in {1}", RPMCid, part.internalModel.internalName);
                    }
                    else
                    {
                        JUtil.LogMessage(this, "Start: Loading GUID {0}", id);
                    }
                }

                plugins = new ExternalVariableHandlers(part);

                // Make sure we have the description strings parsed.
                string[] descriptionStrings = vesselDescription.UnMangleConfigText().Split(JUtil.LineSeparator, StringSplitOptions.None);
                for (int i = 0; i < descriptionStrings.Length; i++)
                {
                    var match = x_agmemoRegex.Match(descriptionStrings[i]);
                    if (match.Success && match.Groups.Count == 3 && uint.TryParse(match.Groups[1].Value, out uint groupID) && groupID < actionGroupMemo.Length)
                    {
                        descriptionStrings[i] = string.Empty;
                        actionGroupMemo[groupID] = match.Groups[2].Value;
                    }
                }

                vesselDescriptionForDisplay = string.Join(Environment.NewLine, descriptionStrings).MangleConfigText();
                if (string.IsNullOrEmpty(vesselDescriptionForDisplay))
                {
                    vesselDescriptionForDisplay = " "; // Workaround for issue #466.
                }

                // Now let's parse our stored strings...
                if (!string.IsNullOrEmpty(storedStrings))
                {
                    var storedStringsSplit = storedStrings.Split('|');
                    for (int i = 0; i < storedStringsSplit.Length; ++i)
                    {
                        storedStringsArray.Add(storedStringsSplit[i]);
                    }
                }

                // TODO: If there are triggered events, register for an undock
                // callback so we can void and rebuild the callbacks after undocking.
                // Although it didn't work when I tried it...
                if (!string.IsNullOrEmpty(triggeredEvents))
                {
                    string[] varstring = triggeredEvents.Split('|');
                    for (int i = 0; i < varstring.Length; ++i)
                    {
                        AddTriggeredEvent(varstring[i].Trim());
                    }
                }

                UpdateLocalCrew();
                UpdateLocalVars();
            }
        }

        /// <summary>
        /// Update the variables tracking crew in this pod.
        /// </summary>
        private void UpdateLocalCrew()
        {
            // part.internalModel can be null if the craft is loaded, but isn't the active/IVA craft
            if (part.internalModel != null)
            {
                if (part.internalModel.seats.Count != localCrew.Count)
                {
                    // This can happen when the internalModel is loaded when
                    // it wasn't previously, which appears to occur on docking
                    // for instance.
                    localCrew.Clear();
                    localCrewMedical.Clear();

                    // Note that we set localCrewMedical to null because the
                    // crewMedical ends up being going null sometime between
                    // when the crew changed callback fires and when we start
                    // checking variables.  Thus, we still have to poll the
                    // crew medical.
                    for (int i = 0; i < part.internalModel.seats.Count; i++)
                    {
                        localCrew.Add(part.internalModel.seats[i].crew);
                        localCrewMedical.Add(null);
                    }
                }
                else
                {
                    for (int i = 0; i < part.internalModel.seats.Count; i++)
                    {
                        localCrew[i] = part.internalModel.seats[i].crew;
                        localCrewMedical[i] = null;
                    }
                }
            }
            else
            {
                JUtil.LogErrorMessage(this, "UpdateLocalCrew() - no internal model!");
            }
        }

        private void UpdateLocalVars()
        {
            if (vessel.patchedConicSolver != null)
            {
                node = vessel.patchedConicSolver.maneuverNodes.Count > 0 ? vessel.patchedConicSolver.maneuverNodes[0] : null;
            }
            else
            {
                node = null;
            }

            orbitSensibility = JUtil.OrbitMakesSense(vessel);

            if (part.internalModel != null && part.internalModel.seats.Count == localCrew.Count)
            {
                // For some reason, the localCrewMedical value seems to get nulled after
                // we update crew assignments, so we keep polling it here.
                for (int i = 0; i < part.internalModel.seats.Count; i++)
                {
                    if (localCrew[i] != null && localCrew[i].KerbalRef != null)
                    {
                        kerbalExpressionSystem kES = localCrewMedical[i];
                        localCrew[i].KerbalRef.GetComponentCached<kerbalExpressionSystem>(ref kES);
                        localCrewMedical[i] = kES;
                    }
                    else
                    {
                        localCrewMedical[i] = null;
                    }
                }
            }
        }

        void UpdateVariables()
        {
            UpdateLocalVars();

            RPMVesselComputer comp = RPMVesselComputer.Instance(vid);

            for (int i = 0; i < periodicRandomVals.Count; ++i)
            {
                periodicRandomVals[i].counter -= refreshDataRate;
                if (periodicRandomVals[i].counter <= 0)
                {
                    periodicRandomVals[i].counter = periodicRandomVals[i].period;
                    periodicRandomVals[i].value = UnityEngine.Random.value;
                }
            }

            variableCollection.Update(comp);

            ++debug_fixedUpdates;

            Vessel v = vessel;
            for (int i = 0; i < activeTriggeredEvents.Count; ++i)
            {
                activeTriggeredEvents[i].Update(v);
            }
        }

        /// <summary>
        /// Check if it's time to update.
        /// </summary>
        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                // well, it looks sometimes it might become null..
                string s = EditorLogic.fetch != null && EditorLogic.fetch.shipDescriptionField != null
                    ? EditorLogic.fetch.shipDescriptionField.text
                    : string.Empty;

                if (s != lastVesselDescription)
                {
                    lastVesselDescription = s;
                    // For some unclear reason, the newline in this case is always 0A, rather than Environment.NewLine.
                    vesselDescription = s.MangleConfigText();
                }
            }
            else
            {
                if (JUtil.RasterPropMonitorShouldUpdate(part))
                {
                    if (--dataUpdateCountdown < 0)
                    {
                        dataUpdateCountdown = refreshDataRate;
                        UpdateVariables();
                    }
                }

                // handle InternalModules that want to activate/deactivate

                foreach (var module in modulesToRemove)
                {
                    module.internalProp.internalModules.Remove(module);
                }
                modulesToRemove.Clear();

                foreach (var module in modulesToRestore)
                {
                    int insertIndex = 0;
                    for (; insertIndex < module.internalProp.internalModules.Count; ++insertIndex)
                    {
                        InternalModule otherModule = module.internalProp.internalModules[insertIndex];
                        if (module.moduleID < otherModule.moduleID)
                        {
                            break;
                        }
                        else if (module.moduleID == otherModule.moduleID)
                        {
                            if (module != otherModule)
                            {
                                JUtil.LogErrorMessage(this, "Tried to restore internalmodule {0} in prop {1} at index {2} but module {3} is already at that id", module.ClassName, module.internalProp.propName, insertIndex, otherModule.ClassName);
                            }
                            else
                            {
                                // tried to restore something that was already here
                                insertIndex = -1;
                            }

                            break;
                        }
                    }

                    if (insertIndex >= 0)
                    {
                        module.internalProp.internalModules.Insert(insertIndex, module);
                    }
                }
                modulesToRestore.Clear();
            }
        }

        /// <summary>
        /// Tear down this computer.
        /// </summary>
        public void OnDestroy()
        {
            if (!string.IsNullOrEmpty(RPMCid))
            {
                JUtil.LogMessage(this, "OnDestroy: GUID {0}", RPMCid);
            }

            GameEvents.onVesselWasModified.Remove(onVesselWasModified);
            GameEvents.onVesselChange.Remove(onVesselChange);
            GameEvents.onVesselCrewWasModified.Remove(onVesselCrewWasModified);

            if (RPMGlobals.debugShowVariableCallCount && !string.IsNullOrEmpty(RPMCid))
            {
                List<KeyValuePair<string, int>> l = new List<KeyValuePair<string, int>>();
                l.AddRange(debug_callCount);
                l.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    return a.Value - b.Value;
                });
                for (int i = 0; i < l.Count; ++i)
                {
                    JUtil.LogMessage(this, "{0} queried {1} times {2:0.0} calls/FixedUpdate", l[i].Key, l[i].Value, (float)(l[i].Value) / (float)(debug_fixedUpdates));
                }

                JUtil.LogMessage(this, "{0} total variables were instantiated in this part", variableCollection.Count);
                JUtil.LogMessage(this, "{0} variables were polled every {1} updates in the VariableOrNumber", variableCollection.UpdatableCount, refreshDataRate);
            }

            localCrew.Clear();
            localCrewMedical.Clear();

            installedModules.Clear();

            variableCollection.Clear();
            ClearVariables();
        }

        /// <summary>
        /// Callback to tell us our vessel was modified (and we thus need to
        /// refresh some values.
        /// </summary>
        /// <param name="who"></param>
        private void onVesselChange(Vessel who)
        {
            if (who.id == vessel.id)
            {
                vid = vessel.id;
                //JUtil.LogMessage(this, "onVesselChange(): RPMCid {0} / vessel {1}", RPMCid, vid);
                ClearVariables();
                UpdateLocalCrew();

                for (int i = 0; i < installedModules.Count; ++i)
                {
                    installedModules[i].vessel = vessel;
                }

                RPMVesselComputer comp = null;
                if (RPMVesselComputer.TryGetInstance(vessel, ref comp))
                {
                    comp.UpdateDataRefreshRate(refreshDataRate);
                }
            }
        }

        /// <summary>
        /// Callback to tell us the crew aboard was modifed in some way.
        /// </summary>
        /// <param name="who"></param>
        private void onVesselCrewWasModified(Vessel who)
        {
            if (who.id == vessel.id)
            {
                // Someone went on EVA, or came inside, or changed seats.
                UpdateLocalCrew();
            }
        }

        /// <summary>
        /// Callback to tell us our vessel was modified (and we thus need to
        /// re-examine some values.
        /// </summary>
        /// <param name="who"></param>
        private void onVesselWasModified(Vessel who)
        {
            if (who.id == vessel.id)
            {
                vid = vessel.id;
                //JUtil.LogMessage(this, "onVesselWasModified(): RPMCid {0} / vessel {1}", RPMCid, vid;)
                ClearVariables();
                UpdateLocalCrew();

                for (int i = 0; i < installedModules.Count; ++i)
                {
                    installedModules[i].vessel = vessel;
                }

                RPMVesselComputer comp = null;
                if (RPMVesselComputer.TryGetInstance(vessel, ref comp))
                {
                    comp.UpdateDataRefreshRate(refreshDataRate);
                }
            }
        }
        #endregion
    }
}
