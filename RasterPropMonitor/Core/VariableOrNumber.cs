﻿/*****************************************************************************
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
using static JSI.RasterPropMonitorComputer;

namespace JSI
{
    enum VariableUpdateType
    {
        Volatile, // variable must be re-evaluated every time it's accessed
        PerFrame, // variable is re-evaluated once per frame and cached (this is default)
        Pushed,   // variable is re-evaluated on demand by an external system (e.g. persistent variables)
        Constant, // variable is evaluated once on first use
    }

    /// <summary>
    /// This is the class that individual modules use to access the variable
    /// It's owned by the VariableCache instances inside the RasterPropMonitorComputer
    /// This architecture is kind of strange; this could probably be unified with VariableCache
    /// </summary>
    public class VariableOrNumber
    {
        internal readonly string variableName;
        internal bool isNumeric;
        internal VariableUpdateType updateType;
        private readonly RasterPropMonitorComputer rpmComp;

        internal NumericVariableEvaluator numericEvaluator;
        internal VariableEvaluator evaluator;
        internal event Action<float> onChangeCallbacks;
        internal event Action<bool> onResourceDepletedCallbacks;

        public bool isConstant => updateType == VariableUpdateType.Constant;

        private double numericValue;
        private string stringValue;

        internal void FireCallbacks(float newValue)
        {
            if (onChangeCallbacks != null)
            {
                onChangeCallbacks(newValue);
            }

            if (onResourceDepletedCallbacks != null)
            {
                onResourceDepletedCallbacks.Invoke(newValue < 0.01f);
            }
        }

        /// <summary>
        /// Initialize a VariableOrNumber
        /// </summary>
        /// <param name="input">The name of the variable</param>
        /// <param name="cacheable">Whether the variable is cacheable</param>
        /// <param name="rpmComp">The RasterPropMonitorComputer that owns the variable</param>
        internal VariableOrNumber(string input, VariableEvaluator evaluator, RPMVesselComputer vesselComp, VariableUpdateType updateType, RasterPropMonitorComputer rpmComp_)
        {
            variableName = input;
            this.updateType = updateType;
            rpmComp = rpmComp_; // will be null if this variable is cacheable
            this.evaluator = evaluator;

            if (evaluator == null)
            {
                updateType = VariableUpdateType.Constant;
                stringValue = input;
                isNumeric = false;
                rpmComp = null;
            }
            else
            {
                object value = evaluator(vesselComp);

                if (value is string str)
                {
                    stringValue = str;
                    isNumeric = false;
                }
                else
                {
                    stringValue = value.ToString();
                    numericValue = value.MassageToDouble();
                    isNumeric = true;
                }
            }

            if (updateType != VariableUpdateType.Constant)
            {
                this.updateType = VariableUpdateType.Volatile;
            }
        }

        internal VariableOrNumber(string input, NumericVariableEvaluator evaluator, RPMVesselComputer vesselComp, VariableUpdateType updateType, RasterPropMonitorComputer rpmComp_)
        {
            variableName = input;
            this.updateType = updateType;
            rpmComp = rpmComp_; // will be null if this variable is cacheable
            this.numericEvaluator = evaluator;

            double value = evaluator(vesselComp);

            numericValue = value.MassageToDouble();
            isNumeric = true;

            if (updateType != VariableUpdateType.Constant)
            {
                this.updateType = VariableUpdateType.Volatile;
            }
        }

        void UpdateNumericValue(double newVal)
        {
            double oldVal = numericValue;
            isNumeric = true;
            numericValue = newVal;

            if (JUtil.ValueChanged(oldVal, newVal))
            {
                FireCallbacks((float)newVal);
            }
        }

        public void Update(RPMVesselComputer comp)
        {
            if (numericEvaluator != null)
            {
                double newVal = numericEvaluator(comp);
                UpdateNumericValue(newVal);
            }
            else
            {
                object evaluant = evaluator(comp);

                if (evaluant is string str)
                {
                    isNumeric = false;
                    stringValue = str;
                }
                else
                {
                    UpdateNumericValue(evaluant.MassageToDouble());
                }
            }
        }

        /// <summary>
        /// Return the value as a float.
        /// </summary>
        /// <returns></returns>
        public float AsFloat()
        {
            if (updateType == VariableUpdateType.Volatile)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(rpmComp.vessel);
                if (numericEvaluator != null)
                {
                    return (float)numericEvaluator(comp);
                }
                return evaluator(comp).MassageToFloat();
            }
            else
            {
                return (float)numericValue;
            }
        }

        /// <summary>
        /// Returns the value as a double.
        /// </summary>
        /// <returns></returns>
        public double AsDouble()
        {
            if (updateType == VariableUpdateType.Volatile)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(rpmComp.vessel);
                if (numericEvaluator != null)
                {
                    return numericEvaluator(comp);
                }
                return evaluator(comp).MassageToDouble();
            }
            else
            {
                return numericValue;
            }
        }

        /// <summary>
        /// Returns the value as an int.
        /// </summary>
        /// <returns></returns>
        public int AsInt()
        {
            if (updateType == VariableUpdateType.Volatile)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(rpmComp.vessel);
                if (numericEvaluator != null)
                {
                    return (int)numericEvaluator(comp);
                }
                return evaluator(comp).MassageToInt();
            }
            else
            {
                return (int)numericValue;
            }
        }

        /// <summary>
        /// Return the value boxed as an object
        /// </summary>
        /// <returns></returns>
        public object Get()
        {
            if (updateType == VariableUpdateType.Volatile)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(rpmComp.vessel);
                if (numericEvaluator != null)
                {
                    return numericEvaluator(comp);
                }
                return evaluator(comp);
            }
            else if (isNumeric)
            {
                return numericValue;
            }
            else
            {
                return stringValue;
            }
        }
    }

    /// <summary>
    /// Encapsulates the code and logic required to track a variable-or-number
    /// that is bounded with a range that is likewise defined as variable-or-
    /// number.
    /// </summary>
    public class VariableOrNumberRange
    {
        VariableOrNumber sourceValue;
        VariableOrNumber lowerBound;
        VariableOrNumber upperBound;
        VariableOrNumber modulo;

        public string variableName
        {
            get
            {
                return sourceValue.variableName;
            }
        }

        public double rawValue
        {
            get
            {
                return sourceValue.AsDouble();
            }
        }

        public VariableOrNumberRange(RasterPropMonitorComputer rpmComp, string sourceVariable, string range1, string range2, string moduloVariable = null)
        {
            sourceValue = rpmComp.InstantiateVariableOrNumber(sourceVariable);
            lowerBound = rpmComp.InstantiateVariableOrNumber(range1);
            upperBound = rpmComp.InstantiateVariableOrNumber(range2);
            if (!string.IsNullOrEmpty(moduloVariable))
            {
                modulo = rpmComp.InstantiateVariableOrNumber(moduloVariable);
            }
        }

        /// <summary>
        /// Return a value in the range of 0 to 1 representing where the current variable
        /// evaluates within its range.
        /// </summary>
        /// <returns></returns>
        public float InverseLerp()
        {
            float value = sourceValue.AsFloat();
            float low = lowerBound.AsFloat();
            float high = upperBound.AsFloat();

            if (modulo != null)
            {
                float mod = modulo.AsFloat();

                float scaledValue = Mathf.InverseLerp(low, high, value);
                float range = Mathf.Abs(high - low);
                if (range > 0.0f)
                {
                    float modDivRange = mod / range;
                    scaledValue = (scaledValue % (modDivRange)) / modDivRange;
                }

                return scaledValue;
            }
            else
            {
                return Mathf.InverseLerp(low, high, value);
            }
        }

        /// <summary>
        /// Return a value in the range of 0 to 1 representing where the current variable
        /// evaluates within its range.
        /// </summary>
        /// <param name="value">The new value (assumed to be from the right variable)</param>
        /// <returns>0-1</returns>
        public float InverseLerp(float value)
        {
            float low = lowerBound.AsFloat();
            float high = upperBound.AsFloat();

            if (modulo != null)
            {
                float mod = modulo.AsFloat();

                float scaledValue = Mathf.InverseLerp(low, high, value);
                float range = Mathf.Abs(high - low);
                if (range > 0.0f)
                {
                    float modDivRange = mod / range;
                    scaledValue = (scaledValue % (modDivRange)) / modDivRange;
                }

                return scaledValue;
            }
            else
            {
                return Mathf.InverseLerp(low, high, value);
            }
        }

        /// <summary>
        /// Provides a simple boolean true/false for whether the named
        /// variable is in range.
        /// </summary>
        /// <param name="comp"></param>
        /// <returns></returns>
        public bool IsInRange()
        {
            float value = sourceValue.AsFloat();
            float low = lowerBound.AsFloat();
            float high = upperBound.AsFloat();

            if (high < low)
            {
                return (value >= high && value <= low);
            }
            else
            {
                return (value >= low && value <= high);
            }
        }

        /// <summary>
        /// Provides a simple boolean true/false for whether the named
        /// variable is in range.
        /// </summary>
        /// <param name="comp"></param>
        /// <param name="value">The value to test (provided externally)</param>
        /// <returns></returns>
        public bool IsInRange(float value)
        {
            float low = lowerBound.AsFloat();
            float high = upperBound.AsFloat();

            if (high < low)
            {
                return (value >= high && value <= low);
            }
            else
            {
                return (value >= low && value <= high);
            }
        }
    }
}
