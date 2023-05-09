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
    // Note 1: http://docs.unity3d.com/Manual/StyledText.html details the "richText" abilities
    public class JSILabel : InternalModule
    {
        [SerializeReference] ConfigNodeHolder moduleConfig;

        [KSPField]
        public string labelText = "uninitialized";
        [KSPField]
        public string transformName;
        [KSPField]
        public Vector2 transformOffset = Vector2.zero;
        [KSPField]
        public EmissiveMode emissive;
        float lastEmissiveValue = -1;
        public enum EmissiveMode
        {
            always,
            never,
            active,
            passive,
            flash
        };
        [KSPField]
        public float flashRate = 0.0f;

        [KSPField]
        public float fontSize = 8.0f;
        [KSPField]
        public float lineSpacing = 1.0f;
        [KSPField]
        public string fontName = "Arial";
        [KSPField]
        public TextAnchor anchor;
        [KSPField]
        public TextAlignment alignment;
        [KSPField]
        public int fontQuality = 32;

        [KSPField]
        public string switchTransform = string.Empty;
        [KSPField]
        public string switchSound = "Squad/Sounds/sound_click_flick";
        [KSPField]
        public float switchSoundVolume = 0.5f;

        [KSPField]
        public int refreshRate = 10;
        [KSPField]
        public bool oneshot;
        [KSPField]
        public string variableName = string.Empty;
        [KSPField]
        public string positiveColor = string.Empty;
        private Color positiveColorValue = XKCDColors.White;
        [KSPField]
        public string negativeColor = string.Empty;
        private Color negativeColorValue = XKCDColors.White;
        [KSPField]
        public string zeroColor = string.Empty;
        private Color zeroColorValue = XKCDColors.White;
        private bool variablePositive = false;
        private bool flashOn = true;

        [SerializeField] private JSITextMesh textObj;
        private readonly int emissiveFactorIndex = Shader.PropertyToID("_EmissiveFactor");

        private List<JSILabelSet> labels = new List<JSILabelSet>();
        private int activeLabel = 0;
        private FXGroup audioOutput;

        private int updateCountdown;
        private Action<float> del;
        
        RasterPropMonitorComputer rpmComp;
        private JSIFlashModule fm;

        public override void OnLoad(ConfigNode node)
        {
            moduleConfig = ScriptableObject.CreateInstance<ConfigNodeHolder>();
            moduleConfig.Node = node;

            Transform textObjTransform = JUtil.FindPropTransform(internalProp, transformName);
            Vector3 localScale = internalProp.transform.localScale;

            Transform offsetTransform = new GameObject().transform;
            offsetTransform.gameObject.name = "JSILabel-" + this.internalProp.propID + "-" + this.GetHashCode().ToString();
            offsetTransform.gameObject.layer = textObjTransform.gameObject.layer;
            offsetTransform.SetParent(textObjTransform, false);
            offsetTransform.Translate(transformOffset.x * localScale.x, transformOffset.y * localScale.y, 0.0f);

            textObj = offsetTransform.gameObject.AddComponent<JSITextMesh>();

            var font = JUtil.LoadFont(fontName, fontQuality);

            textObj.font = font;
            //textObj.fontSize = fontQuality; // This doesn't work with Unity-embedded fonts
            textObj.fontSize = font.fontSize;

            textObj.anchor = anchor;
            textObj.alignment = alignment;

            float sizeScalar = 32.0f / (float)font.fontSize;
            textObj.characterSize = fontSize * 0.00005f * sizeScalar;
            textObj.lineSpacing *= lineSpacing;
        }

        /// <summary>
        /// Start everything up and get it configured.
        /// </summary>
        public void Start()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            try
            {
                rpmComp = RasterPropMonitorComputer.FindFromProp(internalProp);

                // "Normal" mode
                if (string.IsNullOrEmpty(switchTransform))
                {
                    // Force oneshot if there's no variables:
                    oneshot |= !labelText.Contains("$&$");
                        string sourceString = labelText.UnMangleConfigText();

                        if (!string.IsNullOrEmpty(sourceString) && sourceString.Length > 1)
                        {
                            // Alow a " character to escape leading whitespace
                            if (sourceString[0] == '"')
                            {
                                sourceString = sourceString.Substring(1);
                            }
                        }
                        labels.Add(new JSILabelSet(sourceString, rpmComp, oneshot));

                    if (!oneshot)
                    {
                        rpmComp.UpdateDataRefreshRate(refreshRate);
                    }
                }
                else // Switchable mode
                {
                    SmarterButton.CreateButton(internalProp, switchTransform, Click);
                    audioOutput = JUtil.SetupIVASound(internalProp, switchSound, switchSoundVolume, false);

                    ConfigNode[] variableNodes = moduleConfig.Node.GetNodes("VARIABLESET");

                    for (int i = 0; i < variableNodes.Length; i++)
                    {
                        try
                        {
                            string lText = variableNodes[i].GetValue("labelText");
                            if (lText != null)
                            {
                                bool lOneshot = false;
                                variableNodes[i].TryGetValue("oneshot", ref lOneshot);

                                string sourceString = lText.UnMangleConfigText();
                                lOneshot |= !lText.Contains("$&$");
                                labels.Add(new JSILabelSet(sourceString, rpmComp, lOneshot));
                                if (!lOneshot)
                                {
                                    rpmComp.UpdateDataRefreshRate(refreshRate);
                                }
                            }
                        }
                        catch (ArgumentException e)
                        {
                            JUtil.LogErrorMessage(this, "Error in building prop number {1} - {0}", e.Message, internalProp.propID);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(zeroColor))
                {
                    zeroColorValue = JUtil.ParseColor32(zeroColor, rpmComp);
                    textObj.color = zeroColorValue;
                }

                if (!(string.IsNullOrEmpty(variableName) || string.IsNullOrEmpty(positiveColor) || string.IsNullOrEmpty(negativeColor) || string.IsNullOrEmpty(zeroColor)))
                {
                    positiveColorValue = JUtil.ParseColor32(positiveColor, rpmComp);
                    negativeColorValue = JUtil.ParseColor32(negativeColor, rpmComp);
                    del = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), this, "OnCallback");
                    rpmComp.RegisterVariableCallback(variableName, del);

                    emissive = EmissiveMode.active;

                    // Initialize the text color.  Actually, callback registration takes care of that.
                }

                if (emissive == EmissiveMode.flash)
                {
                    if (flashRate > 0.0f)
                    {
                        emissive = EmissiveMode.flash;
                        fm = JUtil.InstallFlashModule(part, flashRate);
                        if (fm != null)
                        {
                            fm.flashSubscribers += FlashToggle;
                        }
                    }
                    else
                    {
                        emissive = EmissiveMode.active;
                    }
                }

                UpdateShader();
            }
            catch (Exception e)
            {
                JUtil.LogErrorMessage(this, "Start failed in prop {1} ({2}) with exception {0}", e, internalProp.propID, internalProp.propName);
                labels.Add(new JSILabelSet("ERR", rpmComp, true));
            }
        }

        /// <summary>
        /// Callback to manage toggling the flash state, where applicable.
        /// </summary>
        /// <param name="newFlashState"></param>
        private void FlashToggle(bool newFlashState)
        {
            flashOn = newFlashState;
            UpdateShader();

            if(variablePositive)
            {
                textObj.color = (flashOn) ? positiveColorValue : negativeColorValue;
            }
        }

        /// <summary>
        /// Respond to a click event: update the text object
        /// </summary>
        public void Click()
        {
            activeLabel++;

            if (activeLabel == labels.Count)
            {
                activeLabel = 0;
            }

            textObj.text = StringProcessor.ProcessString(labels[activeLabel].spf, rpmComp);

            // do we need to activate the update loop?
            if (labels.Count > 1 && !labels[activeLabel].oneshot)
            {
                rpmComp.RestoreInternalModule(this);
            }

            // Force an update.
            updateCountdown = 0;

            if (audioOutput != null && (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA ||
                CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.Internal))
            {
                audioOutput.audio.Play();
            }
        }

        /// <summary>
        /// Update the emissive value in the shader.
        /// </summary>
        private void UpdateShader()
        {
            float emissiveValue;
            if (emissive == EmissiveMode.always)
            {
                emissiveValue = 1.0f;
            }
            else if (emissive == EmissiveMode.never)
            {
                emissiveValue = 0.0f;
            }
            else if (emissive == EmissiveMode.flash)
            {
                emissiveValue = (variablePositive && flashOn) ? 1.0f : 0.0f;
            }
            else if (variablePositive ^ (emissive == EmissiveMode.passive))
            {
                emissiveValue = 1.0f;
            }
            else
            {
                emissiveValue = 0.0f;
            }

            if (emissiveValue != lastEmissiveValue)
            {
                textObj.material.SetFloat(emissiveFactorIndex, emissiveValue);
                lastEmissiveValue = emissiveValue;
            }
        }

        /// <summary>
        /// Tear down
        /// </summary>
        public void OnDestroy()
        {
            if (fm != null)
            {
                fm.flashSubscribers -= FlashToggle;
            }

            //JUtil.LogMessage(this, "OnDestroy() for {0}", GetHashCode());
            if (del != null)
            {
                try
                {
                    rpmComp.UnregisterVariableCallback(variableName, del);
                }
                catch
                {
                    //JUtil.LogMessage(this, "Trapped exception unregistering JSIVariableLabel (you can ignore this)");
                }
            }
            Destroy(textObj);
            textObj = null;
        }

        /// <summary>
        /// Handle callbacks to update our color.
        /// </summary>
        /// <param name="value"></param>
        private void OnCallback(float value)
        {
            // Sanity checks:
            if (vessel == null)
            {
                // We're not attached to a ship?
                rpmComp.UnregisterVariableCallback(variableName, del);
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback()");
                return;
            }

            if (textObj == null)
            {
                // I don't know what is going on here.  This callback is
                // getting called when textObj is null - did the callback
                // fail to unregister on destruction?  It can't get called
                // before textObj is created.
                if (del != null && !string.IsNullOrEmpty(variableName))
                {
                    rpmComp.UnregisterVariableCallback(variableName, del);
                }
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback() when textObj was null");
                return;
            }

            if (value < 0.0f)
            {
                textObj.color = negativeColorValue;
                variablePositive = false;
            }
            else if (value > 0.0f)
            {
                textObj.color = (flashOn) ? positiveColorValue : negativeColorValue;
                variablePositive = true;
            }
            else
            {
                textObj.color = zeroColorValue;
                variablePositive = false;
            }

            UpdateShader();
        }

        /// <summary>
        /// Time to update?
        /// </summary>
        /// <returns></returns>
        private bool UpdateCheck()
        {
            if (updateCountdown <= 0)
            {
                updateCountdown = refreshRate;
                return true;
            }
            updateCountdown--;
            return false;
        }

        /// <summary>
        /// Do we need to update our text and shader?
        /// </summary>
        public override void OnUpdate()
        {
            if (textObj == null)
            {
                // Shouldn't happen ... but it does, thanks to the quirks of
                // docking and undocking.
                rpmComp.RemoveInternalModule(this);
                return;
            }

            if (labels[activeLabel].oneshotComplete && labels[activeLabel].oneshot)
            {
                rpmComp.RemoveInternalModule(this);
                return;
            }

            if (UpdateCheck() && JUtil.RasterPropMonitorShouldUpdate(part))
            {
                textObj.text = StringProcessor.ProcessString(labels[activeLabel].spf, rpmComp);
                labels[activeLabel].oneshotComplete = true;
            }
        }
    }

    internal class JSILabelSet
    {
        public readonly StringProcessorFormatter spf;
        public bool oneshotComplete;
        public readonly bool oneshot;

        internal JSILabelSet(string labelText, RasterPropMonitorComputer rpmComp, bool isOneshot)
        {
            oneshotComplete = false;
            spf = new StringProcessorFormatter(labelText, rpmComp);
            oneshot = isOneshot || spf.IsConstant;
        }
    }

}
