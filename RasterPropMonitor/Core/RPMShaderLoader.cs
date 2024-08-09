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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JSI
{
    /// <summary>
    /// The RPMShaderLoader is a run-once class that is executed when KSP
    /// reaches the main menu.  Its purpose is to parse rasterpropmonitor.ksp
    /// and fetch the shaders embedded in there.  Those shaders are stored in
    /// a dictionary in JUtil.  In addition, other config assets are parsed
    /// and stored (primarily values found in the RPMVesselComputer).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class RPMShaderLoader : MonoBehaviour
    {
        private void LoadAssets()
        {
            String assetsPath = KSPUtil.ApplicationRootPath + "GameData/JSI/RasterPropMonitor/";
            String shaderAssetBundleName = "rasterpropmonitor-shaders.assetbundle";
            WWW www = new WWW("file://" + assetsPath + shaderAssetBundleName);

            if (!string.IsNullOrEmpty(www.error))
            {
                JUtil.LogErrorMessage(this, "Error loading AssetBundle: {0}", www.error);
                return;
            }
            else if (www.assetBundle == null)
            {
                JUtil.LogErrorMessage(this, "Unable to load AssetBundle {0}", www);
                return;
            }

            JUtil.parsedShaders.Clear();

            AssetBundle bundle = www.assetBundle;

            string[] assetNames = bundle.GetAllAssetNames();
            int len = assetNames.Length;

            Shader shader;
            for (int i = 0; i < len; i++)
            {
                if (assetNames[i].EndsWith(".shader"))
                {
                    shader = bundle.LoadAsset<Shader>(assetNames[i]);
                    if (!shader.isSupported)
                    {
                        JUtil.LogErrorMessage(this, "Shader {0} - unsupported in this configuration", shader.name);
                    }
                    JUtil.parsedShaders[shader.name] = shader;
                }
            }

            bundle.Unload(false);

            string fontAssetBundleName = "rasterpropmonitor-font.assetbundle";
            www = new WWW("file://" + assetsPath + fontAssetBundleName);

            if (!string.IsNullOrEmpty(www.error))
            {
                JUtil.LogErrorMessage(this, "Error loading AssetBundle: {0}", www.error);
                return;
            }
            else if (www.assetBundle == null)
            {
                JUtil.LogErrorMessage(this, "Unable to load AssetBundle {0}", www);
                return;
            }

            JUtil.loadedFonts.Clear();

            bundle = www.assetBundle;

            assetNames = bundle.GetAllAssetNames();
            len = assetNames.Length;

            Font font;
            for (int i = 0; i < len; i++)
            {
                if (assetNames[i].EndsWith(".ttf"))
                {
                    font = bundle.LoadAsset<Font>(assetNames[i]);
                    JUtil.LogInfo(this, "Adding RPM-included font {0} / {1}", font.name, font.fontSize);

                    JUtil.loadedFonts[font.name] = font;
                }
            }
            bundle.Unload(false);

            JUtil.LogInfo(this, "Found {0} RPM shaders and {1} fonts.", JUtil.parsedShaders.Count, JUtil.loadedFonts.Count);
        }

        public static void WaitCoroutine(IEnumerator func)
        {
            while (func.MoveNext())
            {
                if (func.Current != null)
                {
                    IEnumerator num;
                    try
                    {
                        num = (IEnumerator)func.Current;
                    }
                    catch (InvalidCastException)
                    {
                        if (func.Current.GetType() == typeof(WaitForSeconds))
                            Debug.LogWarning("Skipped call to WaitForSeconds. Use WaitForSecondsRealtime instead.");
                        return;  // Skip WaitForSeconds, WaitForEndOfFrame and WaitForFixedUpdate
                    }
                    WaitCoroutine(num);
                }
            }
        }

        /// <summary>
        /// Wake up and ask for all of the shaders in our asset bundle and kick off
        /// the coroutines that look for global RPM config data.
        /// </summary>
        private void Awake()
        {
#if ENABLE_PROFILER
            Profiler.enableAllocationCallstacks = true;
#endif

            DontDestroyOnLoad(this);

            // this should probably use the official async loading stuff
            LoadAssets();
        }

        /// <summary>
        /// Coroutine for loading the various custom variables used for variables.
        /// Yield-returns ever 32 or so variables so it's not as costly in a
        /// given frame.  Also loads all the other various values used by RPM.
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadRasterPropMonitorValues()
        {
            ConfigNode rpmSettings = GameDatabase.Instance.GetConfigNodes("RasterPropMonitorSettings").FirstOrDefault();

            if (rpmSettings != null)
            {
                if (rpmSettings.TryGetValue("DebugLogging", ref RPMGlobals.debugLoggingEnabled))
                {
                    JUtil.LogInfo(this, "Set debugLoggingEnabled to {0}", RPMGlobals.debugLoggingEnabled);
                }

                if (rpmSettings.TryGetValue("ShowCallCount", ref RPMGlobals.debugShowVariableCallCount))
                {
                    // call count doesn't write anything if enableLogging is false
                    RPMGlobals.debugShowVariableCallCount = RPMGlobals.debugShowVariableCallCount && RPMGlobals.debugLoggingEnabled;
                }

                if (rpmSettings.TryGetValue("DefaultRefreshRate", ref RPMGlobals.defaultRefreshRate))
                {
                    RPMGlobals.defaultRefreshRate = Math.Max(RPMGlobals.defaultRefreshRate, 1);
                }

                if (rpmSettings.TryGetValue("MinimumRefreshRate", ref RPMGlobals.minimumRefreshRate))
                {
                    RPMGlobals.minimumRefreshRate = Math.Max(RPMGlobals.minimumRefreshRate, 1);
                }

                rpmSettings.TryGetValue("UseNewVariableAnimator", ref RPMGlobals.useNewVariableAnimator);

                RPMGlobals.debugShowOnly.Clear();
                string showOnlyConcat = string.Empty;
                if (rpmSettings.TryGetValue("ShowOnly", ref showOnlyConcat) && !string.IsNullOrEmpty(showOnlyConcat))
                {
                    string[] showOnly = showOnlyConcat.Split('|');
                    for (int i = 0; i < showOnly.Length; ++i)
                    {
                        RPMGlobals.debugShowOnly.Add(showOnly[i].Trim());
                    }
                }
            }

            RPMGlobals.customVariables.Clear();

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("RPM_CUSTOM_VARIABLE");
            for (int i = 0; i < nodes.Length; ++i)
            {

                try
                {
                    string varName = nodes[i].GetValue("name");

                    if (!string.IsNullOrEmpty(varName))
                    {
                        string completeVarName = "CUSTOM_" + varName;
                        RPMGlobals.customVariables.Add(completeVarName, nodes[i]);
                        JUtil.LogMessage(this, "I know about {0}", completeVarName);
                    }
                }
                catch
                {

                }

                if ((i & 0x1f) == 0x1f)
                {
                    yield return null;
                }
            }

            // And parse known mapped variables
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_MAPPED_VARIABLE");
            for (int i = 0; i < nodes.Length; ++i)
            {
                try
                {
                    string varName = nodes[i].GetValue("mappedVariable");

                    if (!string.IsNullOrEmpty(varName))
                    {
                        string completeVarName = "MAPPED_" + varName;
                        RPMGlobals.customVariables.Add(completeVarName, nodes[i]);
                        JUtil.LogMessage(this, "I know about {0}", completeVarName);
                    }
                }
                catch
                {

                }
                if ((i & 0x1f) == 0x1f)
                {
                    yield return null;
                }
            }

            // And parse known math variables
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_MATH_VARIABLE");
            for (int i = 0; i < nodes.Length; ++i)
            {
                try
                {
                    string varName = nodes[i].GetValue("name");

                    if (!string.IsNullOrEmpty(varName))
                    {
                        string completeVarName = "MATH_" + varName;
                        RPMGlobals.customVariables.Add(completeVarName, nodes[i]);
                        JUtil.LogMessage(this, "I know about {0}", completeVarName);
                    }
                }
                catch
                {

                }
                if ((i & 0x1f) == 0x1f)
                {
                    yield return null;
                }
            }

            // And parse known select variables
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_SELECT_VARIABLE");
            for (int i = 0; i < nodes.Length; ++i)
            {
                try
                {
                    string varName = nodes[i].GetValue("name");

                    if (!string.IsNullOrEmpty(varName))
                    {
                        string completeVarName = "SELECT_" + varName;
                        RPMGlobals.customVariables.Add(completeVarName, nodes[i]);
                        JUtil.LogMessage(this, "I know about {0}", completeVarName);
                    }
                }
                catch
                {

                }
                if ((i & 0x1f) == 0x1f)
                {
                    yield return null;
                }
            }
            yield return null;

            JUtil.globalColors.Clear();
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_GLOBALCOLORSETUP");
            for (int idx = 0; idx < nodes.Length; ++idx)
            {
                ConfigNode[] colorConfig = nodes[idx].GetNodes("COLORDEFINITION");
                for (int defIdx = 0; defIdx < colorConfig.Length; ++defIdx)
                {
                    if (colorConfig[defIdx].HasValue("name") && colorConfig[defIdx].HasValue("color"))
                    {
                        string name = "COLOR_" + (colorConfig[defIdx].GetValue("name").Trim());
                        try
                        {
                            Color32 color = ConfigNode.ParseColor32(colorConfig[defIdx].GetValue("color").Trim());
                            if (JUtil.globalColors.ContainsKey(name))
                            {
                                JUtil.globalColors[name] = color;
                            }
                            else
                            {
                                JUtil.globalColors.Add(name, color);
                            }
                            JUtil.LogMessage(this, "I know {0} = {1}", name, color);
                        }
                        catch (Exception e)
                        {
                            JUtil.LogErrorMessage(this, "Error parsing color {0}: {1}", colorConfig[defIdx].GetValue("name").Trim(), e);
                        }
                    }
                }
            }

            RPMGlobals.triggeredEvents.Clear();
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_TRIGGERED_EVENT");
            for (int idx = 0; idx < nodes.Length; ++idx)
            {
                string eventName = nodes[idx].GetValue("eventName").Trim();

                try
                {
                    RasterPropMonitorComputer.TriggeredEventTemplate triggeredVar = new RasterPropMonitorComputer.TriggeredEventTemplate(nodes[idx]);

                    if (!string.IsNullOrEmpty(eventName) && triggeredVar != null)
                    {
                        RPMGlobals.triggeredEvents.Add(triggeredVar);
                        JUtil.LogMessage(this, "I know about event {0}", eventName);
                    }
                }
                catch (Exception e)
                {
                    JUtil.LogErrorMessage(this, "Error adding triggered event {0}: {1}", eventName, e);
                }
            }

            RPMGlobals.ignoreAllPartModules.Clear();
            RPMGlobals.ignorePartModules.Clear();
            nodes = GameDatabase.Instance.GetConfigNodes("RPM_IGNORE_MODULES");
            for (int idx = 0; idx < nodes.Length; ++idx)
            {
                foreach (string nameExpression in nodes[idx].GetValuesList("moduleName"))
                {
                    string[] splitted = nameExpression.Split(':'); //splitted[0] - part name, splitted[1] - module name
                    if (splitted.Length != 2 || splitted[0].Length == 0 || splitted[1].Length == 0)
                    {
                        continue; //just skipping in case of bad syntax
                    }
                    string partName = splitted[0].Replace('_', '.'); //KSP does it, so we do
                    if (splitted[1] == "*")
                    {
                        RPMGlobals.ignoreAllPartModules.Add(partName);
                    }
                    else
                    {
                        List<string> moduleNameList;
                        if (!RPMGlobals.ignorePartModules.TryGetValue(partName, out moduleNameList))
                        {
                            moduleNameList = new List<string>();
                            RPMGlobals.ignorePartModules.Add(partName, moduleNameList);
                        }
                        if (!moduleNameList.Contains(splitted[1]))
                        {
                            moduleNameList.Add(splitted[1]);
                        }
                    }
                }
            }

            RPMGlobals.knownLoadedAssemblies.Clear();
            for (int i = 0; i < AssemblyLoader.loadedAssemblies.Count; ++i)
            {
                string thatName = AssemblyLoader.loadedAssemblies[i].assembly.GetName().Name;
                RPMGlobals.knownLoadedAssemblies.Add(thatName.ToUpper());
                JUtil.LogMessage(this, "I know that {0} ISLOADED_{1}", thatName, thatName.ToUpper());
                if ((i & 0xf) == 0xf)
                {
                    yield return null;
                }
            }

            RPMGlobals.systemNamedResources.Clear();
            foreach (PartResourceDefinition thatResource in PartResourceLibrary.Instance.resourceDefinitions)
            {
                string varname = thatResource.name.ToUpperInvariant().Replace(' ', '-').Replace('_', '-');
                RPMGlobals.systemNamedResources.Add(varname, thatResource.name);
                JUtil.LogMessage(this, "Remembering system resource {1} as SYSR_{0}", varname, thatResource.name);
            }

            yield return null;
        }

        public void ModuleManagerPostLoad()
        {
            WaitCoroutine(LoadRasterPropMonitorValues());
        }
    }
}
