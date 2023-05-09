using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace JSI
{
    // since you can't serialize ConfigNodes, and most of RPM initializes itself in Start in the flight scene,
    // this class allows a module to store its ConfigNode in OnLoad and then consume it in Start
    public class ConfigNodeHolder : ScriptableObject
    {
        public ConfigNode Node;
    }
}
