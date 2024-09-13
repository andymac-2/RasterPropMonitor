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
using UnityEngine;

namespace JSI
{
    /// <summary>
    /// JSICallbackAnimator is an alternative for JSIVariableAnimator that handles
    /// thresholded behavior (switching on/off, not interpolating between two
    /// states).
    /// </summary>
    public class JSICallbackAnimator : InternalModule, IPropBatchModuleHandler
    {
        [KSPField]
        public string variableName = string.Empty;
        [KSPField]
        public float flashRate = 0.0f;

        [SerializeField]
        private List<CallbackAnimationSet> variableSets = new List<CallbackAnimationSet>();
        private RasterPropMonitorComputer rpmComp;
        private JSIFlashModule fm;

        public override void OnLoad(ConfigNode node)
        {
            ConfigNode[] variableNodes = node.GetNodes("VARIABLESET");

            for (int i = 0; i < variableNodes.Length; i++)
            {
                var variableSet = gameObject.AddComponent<CallbackAnimationSet>();
                try
                {
                    variableSet.Load(variableNodes[i], internalProp);
                    variableSets.Add(variableSet);
                }
                catch (Exception e)
                {
                    JUtil.LogErrorMessage(this, "Error in building prop number {1} - {0}", e.Message, internalProp.propName);
                    Component.Destroy(variableSet);
                }
            }
        }

        /// <summary>
        /// Start and initialize all the things!
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

                foreach (var variableSet in variableSets)
                {
                    variableSet.OnStart(internalProp, variableName);
                }

                if (string.IsNullOrEmpty(variableName))
                {
                    JUtil.LogErrorMessage(this, "Configuration failed in prop {0} ({1}), no variableName.", internalProp.propID, internalProp.propName);
                    throw new ArgumentNullException();
                }

                if (flashRate > 0.0f)
                {
                    fm = JUtil.InstallFlashModule(part, flashRate);

                    if (fm != null)
                    {
                        fm.flashSubscribers += FlashToggle;
                    }
                }

                rpmComp.RegisterVariableCallback(variableName, OnCallback);
                rpmComp.RemoveInternalModule(this);
                JUtil.LogMessage(this, "Configuration complete in prop {1} ({2}), supporting {0} callback animators.", variableSets.Count, internalProp.propID, internalProp.propName);
            }
            catch (Exception ex)
            {
                JUtil.LogErrorMessage(this, $"{internalProp.propName} - {internalProp.propID} - {variableName} - {internalModel?.internalName} - {ex}");
                JUtil.AnnoyUser(this);
                enabled = false;
                throw;
            }
        }

        /// <summary>
        /// Callback to update flashing parts
        /// </summary>
        /// <param name="newState"></param>
        private void FlashToggle(bool newState)
        {
            for (int i = 0; i < variableSets.Count; ++i)
            {
                variableSets[i].FlashState(newState);
            }
        }

        /// <summary>
        /// Tear down the object.
        /// </summary>
        public void OnDestroy()
        {
            for (int i = 0; i < variableSets.Count; ++i)
            {
                variableSets[i].TearDown();
            }
            variableSets.Clear();

            try
            {
                rpmComp.UnregisterVariableCallback(variableName, OnCallback);
                if (fm != null)
                {
                    fm.flashSubscribers -= FlashToggle;
                }
            }
            catch
            {
                //JUtil.LogMessage(this, "Trapped exception unregistering JSICallback (you can ignore this)");
            }
        }

        /// <summary>
        /// Callback RasterPropMonitorComputer calls when the variable of interest
        /// changes.
        /// </summary>
        /// <param name="value"></param>
        void OnCallback(float value)
        {
            // Sanity checks:
            if (vessel == null)
            {
                // Stop getting callbacks if for some reason a different
                // computer is talking to us.
                //JUtil.LogMessage(this, "OnCallback - unregistering del {0}, vessel null is {1}, comp.id = {2}", del.GetHashCode(), (vessel == null), comp.id);
                rpmComp.UnregisterVariableCallback(variableName, OnCallback);
                JUtil.LogErrorMessage(this, "Received an unexpected OnCallback()");
            }
            else
            {
                for (int i = 0; i < variableSets.Count; ++i)
                {
                    variableSets[i].UpdateValue(value);
                }
            }
        }

        public bool NotifyTransformBatched(Transform transform)
        {
            for (int i = variableSets.Count-1 ; i >= 0; --i)
            {
                if (object.ReferenceEquals(transform, variableSets[i].Transform))
                {
                    variableSets.RemoveAt(i);
                }
            }

            return variableSets.Count == 0 && fm == null;
        }
    }

    /// <summary>
    /// CallbackAnimationSet tracks one particular animation (color change,
    /// rotation, transformation, texture coordinate change).  It has an
    /// independent range of enabling values, but it depends on the parent
    /// JSICallback class to control what variable is tracked.
    /// </summary>
    public class CallbackAnimationSet : MonoBehaviour
    {
        [SerializeField] private string scaleRangeMin, scaleRangeMax;
        [SerializeField] private bool reverse;
        [SerializeField] private bool animateExterior;
        [SerializeField] private string animationName;
        [SerializeField] private string stopAnimationName;
        [SerializeField] private float animationSpeed;
        [SerializeField] private string passiveColorName, activeColorName;
        [SerializeField] private Transform controlledTransform;
        [SerializeField] private Vector3 initialPosition, initialScale, vectorStart, vectorEnd;
        [SerializeField] private Quaternion initialRotation, rotationStart, rotationEnd;
        [SerializeField] private bool longPath;
        [SerializeField] private int colorName = -1;
        [SerializeField] private Vector2 textureShiftStart, textureShiftEnd, textureScaleStart, textureScaleEnd;
        [SerializeField] private Material affectedMaterial;
        [SerializeField] private List<string> textureLayer = new List<string>();
        [SerializeField] private Mode mode;
        [SerializeField] private bool looping;
        [SerializeField] private bool flash;
        [SerializeField] private string alarmSoundName;
        [SerializeField] private float alarmSoundVolume;
        [SerializeField] private bool alarmMustPlayOnce;
        [SerializeField] private bool alarmSoundLooping;

        // runtime values:
        private bool alarmActive; 
        private bool currentState;
        private bool inIVA = false;
        private VariableOrNumberRange variable;
        private FXGroup audioOutput;
        private Color passiveColor, activeColor;
        private Animation onAnim;
        private Animation offAnim;

        private enum Mode
        {
            Animation,
            Color,
            LoopingAnimation,
            Rotation,
            Translation,
            Scale,
            TextureShift,
            TextureScale,
        }

        /// <summary>
        /// Initialize and configure the callback handler.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="variableName"></param>
        /// <param name="thisProp"></param>
        public void Load(ConfigNode node, InternalProp thisProp)
        {
            currentState = false;

            if (!node.HasData)
            {
                throw new ArgumentException("No data?!");
            }

            string[] tokens = { };

            if (node.HasValue("scale"))
            {
                tokens = node.GetValue("scale").Split(',');
            }

            if (tokens.Length != 2)
            {
                throw new ArgumentException("Could not parse 'scale' parameter.");
            }

            scaleRangeMin = tokens[0];
            scaleRangeMax = tokens[1];

            // That takes care of the scale, now what to do about that scale:
            node.TryGetValue(nameof(reverse), ref reverse);
            node.TryGetValue(nameof(flash), ref flash);

            alarmSoundName = node.GetValue("alarmSound");

            if (alarmSoundName != null)
            {
                alarmSoundVolume = 0.5f;
                node.TryGetValue(nameof(alarmSoundVolume), ref alarmSoundVolume);
                node.TryGetValue(nameof(alarmMustPlayOnce), ref alarmMustPlayOnce);
                string alarmShutdownButton = node.GetValue(nameof(alarmShutdownButton));
                if (alarmShutdownButton != null)
                {
                    SmarterButton.CreateButton(thisProp, alarmShutdownButton, AlarmShutdown);
                }
                node.TryGetValue(nameof(alarmSoundLooping), ref alarmSoundLooping);
            }

            animationName = node.GetValue(nameof(animationName));
            string controlledTransformName = node.GetValue("KcontrolledTransform");

            if (animationName != null)
            {
                node.TryGetValue(nameof(animationSpeed), ref animationSpeed);
                node.TryGetValue(nameof(animateExterior), ref animateExterior);
                node.TryGetValue("loopingAnimation", ref looping);
                stopAnimationName = node.GetValue(nameof(stopAnimationName));
            }
            else if (node.HasValue("activeColor") && node.HasValue("passiveColor") && node.HasValue("coloredObject"))
            {
                string colorNameString = node.GetValue("colorName") ?? "_EmissiveColor";
                colorName = Shader.PropertyToID(colorNameString);

                passiveColorName = node.GetValue("passiveColor");
                activeColorName = node.GetValue("activeColor");

                string coloredObjectName = node.GetValue("coloredObject");
                controlledTransform = thisProp.FindModelComponent<Renderer>(coloredObjectName)?.transform;
                if (controlledTransform == null)
                {
                    throw new ArgumentException($"Could not find transform {coloredObjectName} in prop {thisProp.propName}");
                }
                mode = Mode.Color;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localRotationStart") && node.HasValue("localRotationEnd"))
            {
                controlledTransform = JUtil.FindPropTransform(thisProp, node.GetValue("controlledTransform").Trim());
                initialRotation = controlledTransform.localRotation;
                if (node.HasValue("longPath"))
                {
                    longPath = true;
                    vectorStart = ConfigNode.ParseVector3(node.GetValue("localRotationStart"));
                    vectorEnd = ConfigNode.ParseVector3(node.GetValue("localRotationEnd"));
                }
                else
                {
                    rotationStart = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationStart")));
                    rotationEnd = Quaternion.Euler(ConfigNode.ParseVector3(node.GetValue("localRotationEnd")));
                }
                mode = Mode.Rotation;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localTranslationStart") && node.HasValue("localTranslationEnd"))
            {
                controlledTransform = JUtil.FindPropTransform(thisProp, node.GetValue("controlledTransform").Trim());
                initialPosition = controlledTransform.localPosition;
                vectorStart = ConfigNode.ParseVector3(node.GetValue("localTranslationStart"));
                vectorEnd = ConfigNode.ParseVector3(node.GetValue("localTranslationEnd"));
                mode = Mode.Translation;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("localScaleStart") && node.HasValue("localScaleEnd"))
            {
                controlledTransform = JUtil.FindPropTransform(thisProp, node.GetValue("controlledTransform").Trim());
                initialScale = controlledTransform.localScale;
                vectorStart = ConfigNode.ParseVector3(node.GetValue("localScaleStart"));
                vectorEnd = ConfigNode.ParseVector3(node.GetValue("localScaleEnd"));
                mode = Mode.Scale;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("textureLayers") && node.HasValue("textureShiftStart") && node.HasValue("textureShiftEnd"))
            {
                controlledTransform = JUtil.FindPropTransform(thisProp, node.GetValue("controlledTransform").Trim());
                var textureLayers = node.GetValue("textureLayers").Split(',');
                for (int i = 0; i < textureLayers.Length; ++i)
                {
                    textureLayer.Add(textureLayers[i].Trim());
                }

                textureShiftStart = ConfigNode.ParseVector2(node.GetValue("textureShiftStart"));
                textureShiftEnd = ConfigNode.ParseVector2(node.GetValue("textureShiftEnd"));

                mode = Mode.TextureShift;
            }
            else if (node.HasValue("controlledTransform") && node.HasValue("textureLayers") && node.HasValue("textureScaleStart") && node.HasValue("textureScaleEnd"))
            {
                controlledTransform = JUtil.FindPropTransform(thisProp, node.GetValue("controlledTransform").Trim());
                var textureLayers = node.GetValue("textureLayers").Split(',');
                for (int i = 0; i < textureLayers.Length; ++i)
                {
                    textureLayer.Add(textureLayers[i].Trim());
                }

                textureScaleStart = ConfigNode.ParseVector2(node.GetValue("textureScaleStart"));
                textureScaleEnd = ConfigNode.ParseVector2(node.GetValue("textureScaleEnd"));

                mode = Mode.TextureScale;
            }
            else
            {
                throw new ArgumentException("Cannot initiate any of the possible action modes.");
            }

            if (reverse)
            {
                JUtil.Swap(ref activeColor, ref passiveColor);
                JUtil.Swap(ref vectorStart, ref vectorEnd);
                JUtil.Swap(ref rotationStart, ref rotationEnd);
                JUtil.Swap(ref textureShiftStart, ref textureShiftEnd);
                JUtil.Swap(ref textureScaleStart, ref textureScaleEnd);
                animationSpeed = -animationSpeed;
            }
        }

        public void OnStart(InternalProp thisProp, string variableName)
        {
            RasterPropMonitorComputer rpmComp = RasterPropMonitorComputer.FindFromProp(thisProp);
            variable = new VariableOrNumberRange(rpmComp, variableName, scaleRangeMin, scaleRangeMax);

            passiveColor = JUtil.ParseColor32(passiveColorName, rpmComp);
            activeColor = JUtil.ParseColor32(activeColorName, rpmComp);

            audioOutput = JUtil.SetupIVASound(thisProp, alarmSoundName, alarmSoundVolume, false);

            if (audioOutput != null)
            {
                audioOutput.audio.loop = alarmSoundLooping;
                inIVA = (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA);

                GameEvents.OnCameraChange.Add(CameraChangeCallback);
            }

            if (mode == Mode.Color || mode == Mode.TextureShift || mode == Mode.TextureScale)
            {
                // since materials get instanced by calling renderer.material; we can't cache this at load time because it will be referring to the material on the prefab
                var renderer = controlledTransform.GetComponent<Renderer>();
                affectedMaterial = renderer.isPartOfStaticBatch ? renderer.sharedMaterial : renderer.material;
                affectedMaterial.SetColor(colorName, passiveColor);
            }

            LoadAnims(thisProp);

            TurnOff();
        }

        private void LoadAnims(InternalProp thisProp)
        {
            if (!string.IsNullOrEmpty(animationName))
            {
                Animation[] anims = animateExterior ? thisProp.part.FindModelAnimators(animationName) : thisProp.FindModelAnimators(animationName);
                if (anims.Length > 0)
                {
                    onAnim = anims[0];
                    onAnim.enabled = true;
                    onAnim[animationName].speed = 0;
                    onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    if (looping)
                    {
                        onAnim[animationName].wrapMode = WrapMode.Loop;
                        onAnim[animationName].speed = animationSpeed;
                        mode = Mode.LoopingAnimation;
                    }
                    else
                    {
                        onAnim[animationName].wrapMode = WrapMode.Once;
                        mode = Mode.Animation;
                    }
                    //alwaysActive = node.HasValue("animateExterior");
                }
                else
                {
                    throw new ArgumentException("Animation " + animationName + " could not be found.");
                }

                if (!string.IsNullOrEmpty(stopAnimationName))
                {
                    anims = animateExterior ? thisProp.part.FindModelAnimators(stopAnimationName) : thisProp.FindModelAnimators(stopAnimationName);
                    if (anims.Length > 0)
                    {
                        offAnim = anims[0];
                        offAnim.enabled = true;
                        offAnim[stopAnimationName].speed = 0;
                        offAnim[stopAnimationName].normalizedTime = reverse ? 1f : 0f;
                        if (looping)
                        {
                            offAnim[stopAnimationName].wrapMode = WrapMode.Loop;
                            offAnim[stopAnimationName].speed = animationSpeed;
                            mode = Mode.LoopingAnimation;
                        }
                        else
                        {
                            offAnim[stopAnimationName].wrapMode = WrapMode.Once;
                            mode = Mode.Animation;
                        }
                    }
                }
            }
        }

        public Transform Transform => controlledTransform;

        /// <summary>
        /// Callback method to notify animators that flash that it's time to flash.
        /// </summary>
        /// <param name="toggleState"></param>
        internal void FlashState(bool toggleState)
        {
            if (flash && currentState)
            {
                if (toggleState)
                {
                    TurnOn();
                }
                else
                {
                    TurnOff();
                }
            }
        }

        /// <summary>
        /// Some things need to be explicitly destroyed due to Unity quirks. 
        /// </summary>
        internal void TearDown()
        {
            if (audioOutput != null)
            {
                GameEvents.OnCameraChange.Remove(CameraChangeCallback);
            }
        }

        /// <summary>
        /// Switch the animator to the ON state.
        /// </summary>
        private void TurnOn()
        {
            switch (mode)
            {
                case Mode.Color:
                    affectedMaterial.SetColor(colorName, activeColor);
                    break;
                case Mode.Animation:
                    onAnim[animationName].normalizedTime = reverse ? 0f : 1f;
                    onAnim.Play();
                    break;
                case Mode.LoopingAnimation:
                    onAnim[animationName].speed = animationSpeed;
                    if (!onAnim.IsPlaying(animationName))
                    {
                        onAnim.Play(animationName);
                    }
                    break;
                case Mode.Rotation:
                    controlledTransform.localRotation = initialRotation * (longPath ? Quaternion.Euler(vectorEnd) : rotationEnd);
                    break;
                case Mode.Translation:
                    controlledTransform.localPosition = initialPosition + vectorEnd;
                    break;
                case Mode.Scale:
                    controlledTransform.localScale = initialScale + vectorEnd;
                    break;
                case Mode.TextureShift:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureOffset(textureLayer[i], textureShiftEnd);
                    }
                    break;
                case Mode.TextureScale:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureScale(textureLayer[i], textureScaleEnd);
                    }
                    break;
            }
        }

        /// <summary>
        /// Switch the animator to the OFF state
        /// </summary>
        private void TurnOff()
        {
            switch (mode)
            {
                case Mode.Color:
                    affectedMaterial.SetColor(colorName, passiveColor);
                    break;
                case Mode.Animation:
                    onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    onAnim.Play();
                    break;
                case Mode.LoopingAnimation:
                    if (offAnim != null)
                    {
                        offAnim[stopAnimationName].speed = animationSpeed;
                        if (!offAnim.IsPlaying(stopAnimationName))
                        {
                            offAnim.Play(stopAnimationName);
                        }
                    }
                    else
                    {
                        onAnim[animationName].speed = 0.0f;
                        onAnim[animationName].normalizedTime = reverse ? 1f : 0f;
                    }
                    break;
                case Mode.Rotation:
                    controlledTransform.localRotation = initialRotation * (longPath ? Quaternion.Euler(vectorStart) : rotationStart);
                    break;
                case Mode.Translation:
                    controlledTransform.localPosition = initialPosition + vectorStart;
                    break;
                case Mode.Scale:
                    controlledTransform.localScale = initialScale + vectorStart;
                    break;
                case Mode.TextureShift:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureOffset(textureLayer[i], textureShiftStart);
                    }
                    break;
                case Mode.TextureScale:
                    for (int i = 0; i < textureLayer.Count; ++i)
                    {
                        affectedMaterial.SetTextureScale(textureLayer[i], textureScaleStart);
                    }
                    break;
            }
        }

        /// <summary>
        /// Receive an update on the value; test if it is in the range we care
        /// about, do what's appropriate if it is.
        /// </summary>
        /// <param name="value"></param>
        public void UpdateValue(float value)
        {
            bool newState = variable.IsInRange(value);

            if (newState ^ currentState)
            {
                // State has changed
                if (newState)
                {
                    TurnOn();

                    if (audioOutput != null && !alarmActive)
                    {
                        audioOutput.audio.volume = (inIVA) ? alarmSoundVolume * GameSettings.SHIP_VOLUME : 0.0f;
                        audioOutput.audio.Play();
                        alarmActive = true;
                    }
                }
                else
                {
                    TurnOff();

                    if (audioOutput != null && alarmActive)
                    {
                        if (!alarmMustPlayOnce)
                        {
                            audioOutput.audio.Stop();
                        }
                        alarmActive = false;
                    }
                }

                currentState = newState;
            }
        }

        /// <summary>
        /// Callback to handle when the camera is switched from IVA to flight
        /// </summary>
        /// <param name="newMode"></param>
        public void CameraChangeCallback(CameraManager.CameraMode newMode)
        {
            inIVA = (CameraManager.Instance.currentCameraMode == CameraManager.CameraMode.IVA);

            if(inIVA)
            {
                if (audioOutput != null && alarmActive)
                {
                    audioOutput.audio.volume = alarmSoundVolume * GameSettings.SHIP_VOLUME;
                }
            }
            else
            {
                if (audioOutput != null && alarmActive)
                {
                    audioOutput.audio.volume = 0.0f;
                }
            }
        }

        /// <summary>
        /// Callback to turn off an alarm in response to a button hit on the prop.
        /// </summary>
        public void AlarmShutdown()
        {
            if (audioOutput != null && alarmActive && audioOutput.audio.isPlaying)
            {
                audioOutput.audio.Stop();
            }
        }
    }
}

