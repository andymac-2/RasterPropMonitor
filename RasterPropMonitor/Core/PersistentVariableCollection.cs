using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSI.Core
{
    internal class PersistentVariableCollection
    {
        Dictionary<string, double> persistentVars = new Dictionary<string, double>();

        public double GetPersistentVariable(string name, double defaultValue)
        {
            if (persistentVars.TryGetValue(name, out double val))
            {
                return val;
            }
            return defaultValue;
        }

        public bool GetPersistentVariable(string name, bool defaultValue)
        {
            double val = GetPersistentVariable(name, defaultValue ? 1 : 0);

            return val != 0;
        }

        public bool HasPersistentVariable(string name)
        {
            return persistentVars.ContainsKey(name);
        }

        // returns whether the value changed
        public bool SetPersistentVariable(string name, double value)
        {
            if (name.Trim().Length == 0)
            {
                JUtil.LogErrorMessage(this, "Trying to set an empty variable name!");
                return false;
            }

            bool valueChanged = true;

            if (persistentVars.TryGetValue(name, out double oldValue))
            {
                valueChanged = JUtil.ValueChanged(oldValue, value);
            }

            persistentVars[name] = value;
            return valueChanged;
        }

        // returns whether the value changed
        public bool SetPersistentVariable(string name, bool value)
        {
            return SetPersistentVariable(name, value ? 1 : 0);
        }

        public void Load(ConfigNode baseNode)
        {
            var varNode = baseNode.GetNode("PERSISTENT_VARS");
            if (varNode != null)
            {
                foreach (var value in varNode.values.values)
                {
                    if (double.TryParse(value.value, out double dblValue))
                    {
                        persistentVars.Add(value.name, dblValue);
                    }
                    else
                    {
                        JUtil.LogErrorMessage(null, "Failed to parse {0} = {1} as a double when loading persistent variables", value.name, value.value);
                    }
                }
            }
        }

        public void Save(ConfigNode baseNode)
        {
            var varNode = baseNode.AddNode("PERSISTENT_VARS");

            foreach (var variable in persistentVars)
            {
                varNode.AddValue(variable.Key, variable.Value);
            }
        }
    }
}
