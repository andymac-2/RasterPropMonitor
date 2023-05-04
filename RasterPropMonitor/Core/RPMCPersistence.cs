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
using JSI.Core;
using UnityEngine;

namespace JSI
{
    public partial class RasterPropMonitorComputer : PartModule
    {
        internal readonly PersistentVariableCollection m_persistentVariables = new PersistentVariableCollection();

        PersistentVariableCollection GetVariableCollection(bool global)
        {
            return global ? RPMVesselComputer.Instance(vessel).PersistentVariables : m_persistentVariables;
        }

        internal double GetPersistentVariable(string name, double defaultValue, bool global)
        {
            return GetVariableCollection(global).GetPersistentVariable(name, defaultValue);
        }

        internal bool GetPersistentVariable(string name, bool defaultValue, bool global)
        {
            return GetVariableCollection(global).GetPersistentVariable(name, defaultValue);
        }

        internal bool HasPersistentVariable(string name, bool global)
        {
            return GetVariableCollection(global).HasPersistentVariable(name);
        }

        internal void SetPersistentVariable(string name, double value, bool global)
        {
            bool valueChanged = GetVariableCollection(global).SetPersistentVariable(name, value);

            if (!valueChanged) return;

            // we need to go update the variableCollections....

            RPMVesselComputer vesselComp = RPMVesselComputer.Instance(vessel);
            string varName = "PERSISTENT_" + name;

            if (global)
            {
                // TODO: might want to cache this list in the vesselmodule?
                foreach (var part in vessel.parts)
                {
                    var rpmc = part.FindModuleImplementing<RasterPropMonitorComputer>();
                    if (rpmc != null)
                    {
                        var vc = rpmc.variableCollection.GetVariable(varName);
                        vc.Update(vesselComp);
                    }
                }
            }
            else
            {
                var vc = variableCollection.GetVariable(varName);
                if (vc != null)
                {
                    vc.Update(vesselComp);
                }
            }
        }

        internal void SetPersistentVariable(string name, bool value, bool global)
        {
            SetPersistentVariable(name, value ? 1 : 0, global);
        }
    }
}
