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
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace JSI
{
    // Note 1: http://docs.unity3d.com/Manual/StyledText.html details the "richText" abilities
    public class JSILabel : InternalModule
    {
        internal class TextBatchInfo : ScriptableObject
        {
            public void OnLoad(ConfigNode node)
            {
                variableName = node.GetValue(nameof(variableName));
                node.TryGetValue(nameof(flashRate), ref flashRate);

                string fontName = "Arial";
                node.TryGetValue(nameof(fontName), ref fontName);
                int fontQuality = 32;
                node.TryGetValue(nameof(fontQuality), ref fontQuality);

                font = JUtil.LoadFont(fontName, fontQuality);
            }

            public void OnStart(ConfigNode node, RasterPropMonitorComputer rpmComp)
            {
                ReadColor(node, nameof(zeroColor), rpmComp, ref zeroColor);
                ReadColor(node, nameof(positiveColor), rpmComp, ref positiveColor);
                ReadColor(node, nameof(negativeColor), rpmComp, ref negativeColor);
            }

            void ReadColor(ConfigNode node, string key, RasterPropMonitorComputer rpmComp, ref Color32 color)
            {
                var colorString = node.GetValue(key);
                if (colorString != null)
                {
                    color = JUtil.ParseColor32(colorString, rpmComp);
                }
            }

            public override bool Equals(object obj)
            {
                return obj is TextBatchInfo info &&
                       variableName == info.variableName &&
                       font == info.font &&
                       EqualityComparer<Color32>.Default.Equals(zeroColor, info.zeroColor) &&
                       EqualityComparer<Color32>.Default.Equals(positiveColor, info.positiveColor) &&
                       EqualityComparer<Color32>.Default.Equals(negativeColor, info.negativeColor) &&
                       flashRate == info.flashRate;
            }

            public override int GetHashCode()
            {
                var hashCode = -1112470117;
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(variableName);
                hashCode = hashCode * -1521134295 + font.GetHashCode();
                hashCode = hashCode * -1521134295 + zeroColor.GetHashCode();
                hashCode = hashCode * -1521134295 + positiveColor.GetHashCode();
                hashCode = hashCode * -1521134295 + negativeColor.GetHashCode();
                hashCode = hashCode * -1521134295 + flashRate.GetHashCode();
                return hashCode;
            }

            public string variableName;
            public Font font;
            public Color32 zeroColor = XKCDColors.White;
            public Color32 positiveColor = XKCDColors.White;
            public Color32 negativeColor = XKCDColors.White;
            public float flashRate = 0.0f;
        }

        [SerializeReference] ConfigNodeHolder moduleConfig;
        [SerializeField] internal TextBatchInfo batchInfo;

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
        public float fontSize = 8.0f;
        [KSPField]
        public float lineSpacing = 1.0f;
        [KSPField]
        public TextAnchor anchor;
        [KSPField]
        public TextAlignment alignment;

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

        private bool variablePositive = false;
        private bool flashOn = true;

        [SerializeField] internal JSITextMesh textObj;
        public static readonly int emissiveFactorIndex = Shader.PropertyToID("_EmissiveFactor");

        private List<StringProcessorFormatter> labels = new List<StringProcessorFormatter>();
        private int activeLabel = 0;
        private FXGroup audioOutput;

        private int updateCountdown;
        private Action<float> del;
        
        internal RasterPropMonitorComputer rpmComp;
        private JSIFlashModule fm;

        public override void OnLoad(ConfigNode node)
        {
            moduleConfig = ScriptableObject.CreateInstance<ConfigNodeHolder>();
            moduleConfig.Node = node;

            batchInfo = ScriptableObject.CreateInstance<TextBatchInfo>();
            batchInfo.OnLoad(node);

            Transform textObjTransform = JUtil.FindPropTransform(internalProp, transformName);
            Vector3 localScale = internalProp.transform.localScale;

            Transform offsetTransform = new GameObject().transform;
            offsetTransform.gameObject.name = "JSILabel-" + this.internalProp.propID + "-" + this.GetHashCode().ToString();
            offsetTransform.gameObject.layer = textObjTransform.gameObject.layer;
            offsetTransform.SetParent(textObjTransform, false);
            offsetTransform.Translate(transformOffset.x * localScale.x, transformOffset.y * localScale.y, 0.0f);

            textObj = offsetTransform.gameObject.AddComponent<JSITextMesh>();

            textObj.font = batchInfo.font;
            //textObj.fontSize = fontQuality; // This doesn't work with Unity-embedded fonts
            textObj.fontSize = batchInfo.font.fontSize;

            textObj.anchor = anchor;
            textObj.alignment = alignment;

            float sizeScalar = 32.0f / (float)batchInfo.font.fontSize;
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

                batchInfo.OnStart(moduleConfig.Node, rpmComp);

                // "Normal" mode
                if (string.IsNullOrEmpty(switchTransform))
                {
                    string sourceString = labelText.UnMangleConfigText();

                    if (!string.IsNullOrEmpty(sourceString) && sourceString.Length > 1)
                    {
                        // Alow a " character to escape leading whitespace
                        if (sourceString[0] == '"')
                        {
                            sourceString = sourceString.Substring(1);
                        }
                    }
                    labels.Add(new StringProcessorFormatter(sourceString, rpmComp));
                    oneshot |= labels[0].IsConstant;

                    if (oneshot)
                    {
                        var propBatcher = internalModel.GetComponentInChildren<PropBatcher>();
                        if (propBatcher != null)
                        {
                            textObj.text = labels[0].Get();
                            propBatcher.AddStaticLabel(this);
                            return;
                        }
                    }
                    else
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
                                string sourceString = lText.UnMangleConfigText();
                                labels.Add(new StringProcessorFormatter(sourceString, rpmComp));
                                if (!labels.Last().IsConstant)
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

                textObj.color = batchInfo.zeroColor;

                if (!string.IsNullOrEmpty(batchInfo.variableName))
                {
                    del = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), this, "OnCallback");
                    rpmComp.RegisterVariableCallback(batchInfo.variableName, del);

                    emissive = EmissiveMode.active;

                    // Initialize the text color.  Actually, callback registration takes care of that.
                }

                if (emissive == EmissiveMode.flash)
                {
                    if (batchInfo.flashRate > 0.0f)
                    {
                        emissive = EmissiveMode.flash;
                        fm = JUtil.InstallFlashModule(part, batchInfo.flashRate);
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
                labels.Add(new StringProcessorFormatter("ERR", rpmComp));
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
                textObj.color = (flashOn) ? batchInfo.positiveColor : batchInfo.negativeColor;
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

            textObj.text = labels[activeLabel].Get();

            // do we need to activate the update loop?
            if (labels.Count > 1 && !labels[activeLabel].IsConstant)
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
                    rpmComp.UnregisterVariableCallback(batchInfo.variableName, del);
                }
                catch
                {
                    //JUtil.LogMessage(this, "Trapped exception unregistering JSIVariableLabel (you can ignore this)");
                }
            }
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
                rpmComp.UnregisterVariableCallback(batchInfo.variableName, del);
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback()");
                return;
            }

            if (textObj == null)
            {
                // I don't know what is going on here.  This callback is
                // getting called when textObj is null - did the callback
                // fail to unregister on destruction?  It can't get called
                // before textObj is created.
                if (del != null && !string.IsNullOrEmpty(batchInfo.variableName))
                {
                    rpmComp.UnregisterVariableCallback(batchInfo.variableName, del);
                }
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback() when textObj was null");
                return;
            }

            if (value < 0.0f)
            {
                textObj.color = batchInfo.negativeColor;
                variablePositive = false;
            }
            else if (value > 0.0f)
            {
                textObj.color = (flashOn) ? batchInfo.positiveColor : batchInfo.negativeColor;
                variablePositive = true;
            }
            else
            {
                textObj.color = batchInfo.zeroColor;
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
            if (textObj == null || labels[activeLabel].IsConstant)
            {
                // Shouldn't happen ... but it does, thanks to the quirks of
                // docking and undocking.
                rpmComp.RemoveInternalModule(this);
                return;
            }

            if (UpdateCheck() && JUtil.RasterPropMonitorShouldUpdate(part))
            {
                textObj.text = labels[activeLabel].Get();
            }
        }
    }
}
