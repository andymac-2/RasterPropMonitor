using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Profiling;

namespace JSI
{
    internal class VariableCollection
    {
        private readonly Dictionary<string, VariableOrNumber> variableCache = new Dictionary<string, VariableOrNumber>();
        private readonly List<VariableOrNumber> updatableVariables = new List<VariableOrNumber>();

        public VariableOrNumber GetVariable(string variableName)
        {
            if (variableCache.TryGetValue(variableName, out var variable)) return variable;
            return null;
        }

        public void AddVariable(VariableOrNumber variable)
        {
            variableCache.Add(variable.variableName, variable);
            if (!variable.isConstant)
            {
                updatableVariables.Add(variable);
            }
        }

        public void Update(RPMVesselComputer comp)
        {
            foreach (var vc in updatableVariables)
            {
                Profiler.BeginSample(vc.variableName);
                vc.Update(comp);
                Profiler.EndSample();
            }
        }

        public void Clear()
        {
            variableCache.Clear();
            updatableVariables.Clear();
        }

        public int Count => variableCache.Count;
        public int UpdatableCount => updatableVariables.Count;
    }
}
