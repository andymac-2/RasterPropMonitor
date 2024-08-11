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
using System.Collections.Generic;
using System;

namespace JSI
{
    public class ResourceDataStorage
    {
        private readonly ResourceData[] rs;
        private readonly Dictionary<string, ResourceData> nameResources = new Dictionary<string, ResourceData>();
        private readonly Dictionary<string, ResourceData> sysrResources = new Dictionary<string, ResourceData>();
        private readonly List<string> sortedResourceNames;
        private HashSet<Part> activeStageParts = new HashSet<Part>();
        private PartSet partSet = null;

        private class ResourceComparer : IComparer<ResourceData>
        {
            public int Compare(ResourceData a, ResourceData b)
            {
                return string.Compare(a.resourceDefinition.name, b.resourceDefinition.name);
            }
        }

        private static bool IsFreeFlow(ResourceFlowMode flowMode)
        {
            return (flowMode == ResourceFlowMode.ALL_VESSEL || flowMode == ResourceFlowMode.STAGE_PRIORITY_FLOW);
        }

        public ResourceDataStorage()
        {
            int resourceCount = PartResourceLibrary.Instance.resourceDefinitions.Count;

            rs = new ResourceData[resourceCount];
            sortedResourceNames = new List<string>(resourceCount);
            int index = 0;
            foreach (PartResourceDefinition thatResource in PartResourceLibrary.Instance.resourceDefinitions)
            {
                string nameSysr = thatResource.name.ToUpperInvariant().Replace(' ', '-').Replace('_', '-');

                rs[index] = new ResourceData(thatResource);
                
                nameResources.Add(thatResource.name, rs[index]);
                sysrResources.Add(nameSysr, rs[index]);
                ++index;
            }

            // Alphabetize our list
            Array.Sort(rs, new ResourceComparer());
        }

        private bool stagePartsChanged = true;
        public void ClearActiveStageParts()
        {
            activeStageParts.Clear();
            stagePartsChanged = true;
        }

        public void StartLoop(Vessel vessel)
        {
            for (int i = 0; i < rs.Length; ++i)
            {
                ResourceData rd = rs[i];

                rd.stage = 0.0f;
                rd.stagemax = 0.0f;
                rd.ispropellant = false;

                double amount, maxAmount;
                vessel.GetConnectedResourceTotals(rd.resourceDefinition.id, out amount, out maxAmount);

                rd.current = (float)amount;
                rd.max = (float)maxAmount;
                if (IsFreeFlow(rd.resourceDefinition.resourceFlowMode))
                {
                    rd.stage = (float)amount;
                    rd.stagemax = (float)maxAmount;
                }
            }
        }

        public void EndLoop(double dt)
        {
            sortedResourceNames.Clear();

            if (stagePartsChanged)
            {
                if (partSet == null)
                {
                    partSet = new PartSet(activeStageParts);
                }
                else
                {
                    partSet.RebuildParts(activeStageParts);
                }
                stagePartsChanged = false;
            }

            float invDeltaT = (float)(1.0 / dt);
            for (int i = 0; i < rs.Length; ++i)
            {
                ResourceData rd = rs[i];

                rd.delta = (rd.previous - rd.current) * invDeltaT;
                rd.previous = rd.current;

                if (rd.max > 0.0)
                {
                    sortedResourceNames.Add(rd.resourceDefinition.name);

                    // If the resource can flow anywhere, we already have the stage
                    // values listed here.
                    if (rd.stagemax == 0.0)
                    {
                        double amount, maxAmount;
                        partSet.GetConnectedResourceTotals(rd.resourceDefinition.id, out amount, out maxAmount, true);
                        rd.stagemax = (float)maxAmount;
                        rd.stage = (float)amount;
                    }
                }
            }
        }

        public string GetActiveResourceByIndex(int index)
        {
            return (index < sortedResourceNames.Count) ? sortedResourceNames[index] : string.Empty;
        }

        public void MarkActiveStage(PartSet ps)
        {
            var parts = ps.GetParts();
            activeStageParts.UnionWith(parts);
            stagePartsChanged = true;
        }

        public void MarkPropellant(Propellant propel)
        {
            ResourceData r = nameResources[propel.name];
            r.ispropellant = true;
        }


        public double PropellantMass(bool stage)
        {
            double mass = 0.0;
            for (int i = 0; i < rs.Length; ++i)
            {
                ResourceData rd = rs[i];

                if (rd.ispropellant)
                {
                    mass += rd.resourceDefinition.density * ((stage) ? rd.stage : rd.current);
                }
            }
            return mass;
        }

        public enum ResourceProperty
        {
            VAL,
            DENSITY,
            DELTA,
            DELTAINV,
            MAXMASS,
            MASS,
            MAX,
            PERCENT,
            DEPLETED
        }

        /// <summary>
        /// Given a string in the form XXXYYYZZZ where YYY may be "STAGE" or null, and ZZZ is one of the keywords above, sets the valueType to the keyword, sets the stage param to whether "STAGE" was present, returns XXX
        /// </summary>
        /// <param name="resourceQuery"></param>
        /// <param name="valueType"></param>
        /// <param name="stage"></param>
        public static string ParseResourceQuery(string resourceQuery, out ResourceProperty valueType, out bool stage)
        {
            valueType = ResourceProperty.VAL;
            stage = false;

            foreach (var propertyType in TEnum.GetValues<ResourceProperty>())
            {
                var propertyName = propertyType.ToString();
                if (resourceQuery.EndsWith(propertyName))
                {
                    valueType = propertyType;
                    resourceQuery = resourceQuery.Substring(0, resourceQuery.Length - propertyName.Length);
                    break;
                }
            }

            if (resourceQuery.EndsWith("STAGE"))
            {
                stage = true;
                resourceQuery = resourceQuery.Substring(0, resourceQuery.Length - "STAGE".Length);
            }

            return resourceQuery;
        }

        public double ListSYSElement(string resourceName, ResourceProperty valueType, bool stage)
        {
            if (sysrResources.TryGetValue(resourceName, out var resource))
            {
                return resource.GetProperty(valueType, stage);
            }
            else
            {
                JUtil.LogErrorMessage(this, "ListElement({0}) resource not found", resourceName);
                return double.NaN;
            }
        }

        public double ListElement(string resourceName, ResourceProperty valueType, bool stage)
        {
            if (nameResources.TryGetValue(resourceName, out var resource))
            {
                return resource.GetProperty(valueType, stage);
            }
            else
            {
                JUtil.LogErrorMessage(this, "Error finding {0}-{2}", resourceName, valueType);
                return 0;
            }
        }

        private ResourceData GetPropellantResource(uint propellantIndex)
        {
            uint currentPropellantIndex = 0;

            for (int resourceIndex = 0; resourceIndex < sortedResourceNames.Count; ++resourceIndex)
            {
                if (nameResources.TryGetValue(sortedResourceNames[resourceIndex], out var resource))
                {
                    if (resource.ispropellant)
                    {
                        if (currentPropellantIndex == propellantIndex)
                        {
                            return resource;
                        }
                        else
                        {
                            ++currentPropellantIndex;
                        }
                    }
                }
            }

            return null;
        }

        public double GetPropellantResourceValue(uint propellantIndex, ResourceProperty valueType, bool stage)
        {
            ResourceData rd = GetPropellantResource(propellantIndex);

            return rd == null ? 0 : rd.GetProperty(valueType, stage);
        }

        public string GetPropellantResourceName(uint propellantIndex)
        {
            ResourceData rd = GetPropellantResource(propellantIndex);
            return rd == null ? "" : rd.resourceDefinition.name;
        }

        private class ResourceData
        {
            public ResourceData(PartResourceDefinition resourceDefinition)
            {
                this.resourceDefinition = resourceDefinition;
            }

            public PartResourceDefinition resourceDefinition;

            public float current;
            public float max;
            public float previous;

            public float stage;
            public float stagemax;

            public float delta;

            public bool ispropellant;

            internal float GetProperty(ResourceProperty valueType, bool currentStage)
            {
                switch (valueType)
                {
                    case ResourceProperty.VAL:
                        return currentStage ? stage : current;
                    case ResourceProperty.DENSITY:
                        return resourceDefinition.density;
                    case ResourceProperty.DELTA:
                        return delta;
                    case ResourceProperty.DELTAINV:
                        return -delta;
                    case ResourceProperty.MASS:
                        return resourceDefinition.density * (currentStage ? stage : current);
                    case ResourceProperty.MAXMASS:
                        return resourceDefinition.density * (currentStage ? stagemax : max);
                    case ResourceProperty.MAX:
                        return currentStage ? stagemax : max;
                    case ResourceProperty.PERCENT:
                        if (currentStage)
                        {
                            return stagemax > 0 ? stage / stagemax : 0;
                        }
                        else
                        {
                            return max > 0 ? current / max : 0;
                        }
                }

                return 0;
            }
        }
    }
}
