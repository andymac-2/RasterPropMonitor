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
using UnityEngine.Profiling;

namespace JSI
{
    public class StringProcessorFormatter
    {
        internal static readonly SIFormatProvider fp = new SIFormatProvider();

        // The formatString or plain text (if usesComp is false).
        private readonly string formatString;
        // An array of source variables
        public readonly VariableOrNumber[] sourceVariables;
        // An array holding evaluants
        public readonly object[] sourceValues;

        public bool IsConstant => sourceValues == null;

        public string cachedResult;

        // TODO: Add support for multi-line processed support.
        public StringProcessorFormatter(string input, RasterPropMonitorComputer rpmComp, RasterPropMonitor rpm = null)
        {
            if(string.IsNullOrEmpty(input))
            {
                cachedResult = "";
            }
            else if (input.IndexOf(JUtil.VariableListSeparator[0], StringComparison.Ordinal) >= 0)
            {
                string[] tokens = input.Split(JUtil.VariableListSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length != 2)
                {
                    throw new ArgumentException(string.Format("Invalid format string: {0}", input));
                }
                else
                {
                    bool allVariablesConstant = true;

                    string[] sourceVarStrings = tokens[1].Split(JUtil.VariableSeparator, StringSplitOptions.RemoveEmptyEntries);
                    sourceVariables = new VariableOrNumber[sourceVarStrings.Length];
                    for (int i = 0; i < sourceVarStrings.Length; ++i )
                    {
                        var variableName = sourceVarStrings[i];
                        if (variableName.StartsWith("MONITOR_LOCAL_"))
                        {
                            sourceVariables[i] = rpm.GetVariable(variableName);
                        }
                        else
                        {
                            sourceVariables[i] = rpmComp.InstantiateVariableOrNumber(variableName);
                        }

                        allVariablesConstant = allVariablesConstant && sourceVariables[i].isConstant;
                    }
                    sourceValues = new object[sourceVariables.Length];
                    formatString = tokens[0].TrimEnd();

                    for (int i = 0; i < sourceVariables.Length; ++i)
                    {
                        sourceValues[i] = sourceVariables[i].Get();
                    }

                    cachedResult = string.Format(fp, formatString, sourceValues);

                    // if every variable is a constant, we can run the format once and cache the result
                    if (allVariablesConstant)
                    {
                        sourceVariables = null;
                        sourceValues = null;
                    }
                }
            }
            else
            {
                cachedResult = input.TrimEnd();
            }
        }

        public bool UpdateValues()
        {
            if (sourceValues == null) return false;

            bool anyChanged = false;
            for (int i = 0; i < sourceVariables.Length; ++i)
            {
                var sourceVariable = sourceVariables[i];
                if (!sourceVariable.isConstant)
                {
                    if (sourceVariable.isNumeric)
                    {
                        double newValue = (double)sourceVariable.Get();
                        if (JUtil.ValueChanged((double)sourceValues[i], newValue))
                        {
                            anyChanged = true;
                            sourceValues[i] = newValue;
                        }
                    }
                    else
                    {
                        string newValue = (string)sourceVariable.Get();
                        anyChanged = anyChanged || newValue != (string)sourceValues[i];
                        sourceValues[i] = newValue;
                    }
                }
            }

            return anyChanged;
        }

        public string GetFormattedString()
        {
            if (UpdateValues())
            {
                cachedResult = string.Format(fp, formatString, sourceValues);
            }

            return cachedResult;
        }
    }
}
