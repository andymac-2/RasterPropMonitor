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
    public partial class RasterPropMonitorComputer : PartModule
    {
        /// <summary>
        /// Per-pod persistence.  This code was devolved from RPMVC due to
        /// difficulties handling docking and undocking.
        /// </summary>
        internal Dictionary<string, double> persistentVars = new Dictionary<string, double>();

        /// <summary>
        /// Returns the named persistent value, or the default provided if
        /// it's not set.  The persistent value is initialized to the default
        /// if the default is used.  If 'broadcast' is set, other RPMC on the
        /// same vessel are queried, as well.
        /// </summary>
        /// <param name="name">Name of the persistent</param>
        /// <param name="defaultValue">The default value</param>
        /// <param name="broadcast">Broadcast the request to other parts of the same craft?</param>
        /// <returns></returns>
        internal double GetPersistentVariable(string name, double defaultValue, bool broadcast)
        {
            double val;
            if (persistentVars.TryGetValue(name, out val))
            {
                
            }
            else if (broadcast)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                val = comp.GetPersistentVariable(name, defaultValue);
                persistentVars[name] = val;
            }
            else
            {
                val = defaultValue;
                persistentVars[name] = defaultValue;
            }

            return val;
        }

        internal bool GetPersistentVariable(string name, bool defaultValue, bool broadcast)
        {
            double val = GetPersistentVariable(name, defaultValue ? 1 : 0, broadcast);
            
            // HACK: if someone tried to access this persistent var, it will have defaulted to -1 which would be "true"
            if (val == -1.0)
            {
                SetPersistentVariable(name, defaultValue ? 1 : 0, broadcast);
                return defaultValue;
            }
            return val != 0;
        }

        /// <summary>
        /// Indicates whether the named persistent variable is present in the
        /// dictionary.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="broadcast">Broadcast the request to other parts of the same craft?</param>
        /// <returns></returns>
        internal bool HasPersistentVariable(string name, bool broadcast)
        {
            if(persistentVars.ContainsKey(name))
            {
                return true;
            }
            else if(broadcast)
            {
                RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);
                return comp.HasPersistentVariable(name);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Set the named persistent variable to the value provided.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="broadcast">Broadcast the request to other parts of the same craft?</param>
        internal void SetPersistentVariable(string name, double value, bool broadcast)
        {
            if (name.Trim().Length == 0)
            {
                JUtil.LogErrorMessage(this, "Trying to set an empty variable name!");
                return;
            }
            persistentVars[name] = value;

            RPMVesselComputer comp = RPMVesselComputer.Instance(vessel);

            // TEMP: how can we avoid this string concat?
            var vc = variableCollection.GetVariable("PERSISTENT_" + name);
            if (vc != null)
            {
                vc.Update(comp);
            }

            if(broadcast)
            {
                comp.SetPersistentVariable(name, value);
            }
        }

        internal void SetPersistentVariable(string name, bool value, bool broadcast)
        {
            SetPersistentVariable(name, value ? 1 : 0, broadcast);
        }
    }
}
