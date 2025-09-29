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
using UnityEngine;

namespace JSI
{
    public class JSISwitchableVariableLabel : InternalModule
    {
        [SerializeReference] ConfigNodeHolder moduleConfig;

        [KSPField]
        public string labelTransform = string.Empty;
        [KSPField]
        public float fontSize = 0.008f;
        [KSPField]
        public int refreshRate = 10;
        [KSPField]
        public string switchTransform = string.Empty;
        [KSPField]
        public string decrementSwitchTransform = string.Empty;
        [KSPField]
        public string switchSound = "Squad/Sounds/sound_click_flick";
        [KSPField]
        public float switchSoundVolume = 0.5f;
        [KSPField]
        public string coloredObject = string.Empty;
        [KSPField]
        public string colorName = "_EmissiveColor";
        [KSPField]
        public string alignment = "TopLeft";
        [KSPField]
        public bool persistActiveLabel = false;

        private int colorNameId = -1;
        private readonly List<VariableLabelSet> labelsEx = new List<VariableLabelSet>();
        private int activeLabel = 0;
        private string persistentVariableName;
        private const string fontName = "Arial";
        private InternalText textObj;
        private Transform textObjTransform;
        private int updateCountdown;
        private Renderer colorShiftRenderer;
        private FXGroup audioOutput;
        private RasterPropMonitorComputer rpmComp;

        public override void OnLoad(ConfigNode node)
        {
            moduleConfig = ScriptableObject.CreateInstance<ConfigNodeHolder>();
            moduleConfig.Node = node;
        }

        public override void OnAwake()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            try
            {
                rpmComp = RasterPropMonitorComputer.FindFromProp(internalProp);

                textObjTransform = JUtil.FindPropTransform(internalProp, labelTransform);
                textObj = InternalComponents.Instance.CreateText(fontName, fontSize * 15.5f, textObjTransform, "", Color.green, false, alignment);
                
                if (persistActiveLabel)
                {
                    persistentVariableName = "switchableLabel_" + internalProp.propID + "_" + moduleID;
                    activeLabel = (int)rpmComp.GetPersistentVariable(persistentVariableName, activeLabel, false);
                }
                else
                {
                    activeLabel = 0;
                }

                SmarterButton.CreateButton(internalProp, switchTransform, Click);

                if (decrementSwitchTransform != string.Empty)
                {
                    SmarterButton.CreateButton(internalProp, decrementSwitchTransform, DecremenetClick);
                }

                ConfigNode[] variableNodes = moduleConfig.Node.GetNodes("VARIABLESET");

                for (int i = 0; i < variableNodes.Length; i++)
                {
                    try
                    {
                        labelsEx.Add(new VariableLabelSet(variableNodes[i], internalProp));
                    }
                    catch (ArgumentException e)
                    {
                        JUtil.LogErrorMessage(this, "Error in building prop number {1} - {0}", e.Message, internalProp.propID);
                    }
                }

                // Fallback: If there are no VARIABLESET blocks, we treat the module configuration itself as a variableset block.
                if (labelsEx.Count < 1)
                {
                    try
                    {
                        labelsEx.Add(new VariableLabelSet(moduleConfig.Node, internalProp));
                    }
                    catch (ArgumentException e)
                    {
                        JUtil.LogErrorMessage(this, "Error in building prop number {1} - {0}", e.Message, internalProp.propID);
                    }
                }

                if (labelsEx.Count == 0)
                {
                    JUtil.LogErrorMessage(this, "No labels defined.");
                    throw new ArgumentException("No labels defined");
                }

                activeLabel = Math.Max(0, Math.Min(activeLabel, labelsEx.Count - 1));

                colorShiftRenderer = internalProp.FindModelComponent<Renderer>(coloredObject);
                if (labelsEx[activeLabel].hasColor)
                {
                    colorNameId = Shader.PropertyToID(colorName);
                    colorShiftRenderer.material.SetColor(colorNameId, labelsEx[activeLabel].color);
                }
                if (labelsEx[activeLabel].hasText)
                {
                    if (labelsEx[activeLabel].oneShot)
                    {
                        textObj.text.text = labelsEx[activeLabel].label.cachedResult;
                    }
                    else
                    {
                        textObj.text.text = "";
                    }
                }

                audioOutput = JUtil.SetupIVASound(internalProp, switchSound, switchSoundVolume, false);
                JUtil.LogMessage(this, "Configuration complete in prop {1}, supporting {0} variable indicators.", labelsEx.Count, internalProp.propID);
            }
            catch
            {
                JUtil.AnnoyUser(this);
                enabled = false;
                throw;
            }
        }

        public void OnDestroy()
        {
            //JUtil.LogMessage(this, "OnDestroy()");
        }

        private bool UpdateCheck()
        {
            if (labelsEx.Count == 0)
            {
                rpmComp.RemoveInternalModule(this);
                return false;
            }

            if (updateCountdown <= 0)
            {
                updateCountdown = refreshRate;
                return true;
            }
            updateCountdown--;
            return false;
        }

        public override void OnUpdate()
        {
            if (UpdateCheck())
            {
                textObj.text.text = labelsEx[activeLabel].label.GetFormattedString();

                if (labelsEx[activeLabel].oneShot)
                {
                    rpmComp.RemoveInternalModule(this);
                }
            }
        }

        private void DecremenetClick()
        {
            UpdateActiveLabel(-1);
        }

        public void Click()
        {
            UpdateActiveLabel(+1);
        }

        void UpdateActiveLabel(int direction)
        {
            if (labelsEx.Count == 0) return;

            activeLabel = (activeLabel + direction + labelsEx.Count) % labelsEx.Count;

            if (persistActiveLabel)
            {
                rpmComp.SetPersistentVariable(persistentVariableName, activeLabel, false);
            }

            if (labelsEx.Count > 1 && !labelsEx[activeLabel].oneShot)
            {
                rpmComp.RestoreInternalModule(this);
            }

            if (labelsEx[activeLabel].hasColor)
            {
                colorShiftRenderer.material.SetColor(colorNameId, labelsEx[activeLabel].color);
            }

            if (labelsEx[activeLabel].hasText)
            {
                textObj.text.text = labelsEx[activeLabel].label.GetFormattedString();
            }

            // Force an update.
            updateCountdown = 0;

            if (audioOutput != null && (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA ||
                CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal))
            {
                audioOutput.audio.Play();
            }
        }
    }

    public class VariableLabelSet
    {
        public readonly StringProcessorFormatter label;
        public readonly bool hasText;
        public readonly bool oneShot;
        public readonly Color color;
        public readonly bool hasColor;

        public VariableLabelSet(ConfigNode node, InternalProp prop)
        {
            RasterPropMonitorComputer rpmComp = null;
            if (node.HasValue("labelText"))
            {
                string labelText = node.GetValue("labelText").Trim().UnMangleConfigText();
                hasText = true;
                rpmComp = RasterPropMonitorComputer.FindFromProp(prop);
                label = new StringProcessorFormatter(labelText, rpmComp);
                oneShot = label.IsConstant;
            }
            else
            {
                hasText = false;
                oneShot = true;
            }

            if (node.HasValue("color"))
            {
                color = JUtil.ParseColor32(node.GetValue("color").Trim(), rpmComp);
                hasColor = true;
            }
            else
            {
                hasColor = false;
            }
        }
    }
}
