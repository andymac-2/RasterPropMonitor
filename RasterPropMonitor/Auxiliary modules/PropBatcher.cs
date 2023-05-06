using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;

namespace JSI.Auxiliary_modules
{
    class BatchFilter
    {
        public BatchFilter(ConfigNode node, int batchID)
        {
            this.batchID = batchID;
            propName = node.GetValue(nameof(propName));
            transformName = node.GetValue(nameof(transformName));
            node.TryGetValue(nameof(keepProp), ref keepProp);
        }

        public readonly int batchID;
        public readonly string propName;
        public readonly string transformName;
        public readonly bool keepProp = true;
        
    }

    public class PropBatcher : InternalModule
    {
        static Dictionary<string, BatchFilter> x_propNameToFilter;
        
        public static void ModuleManagerPostLoad()
        {
            x_propNameToFilter = new Dictionary<string, BatchFilter>();

            var batchNodes = GameDatabase.Instance.GetConfigNodes("PROP_BATCH");
            int nextBatchID = 0;
            foreach (var batchNode in batchNodes)
            {
                foreach (var filterNode in batchNode.GetNodes("FILTER"))
                {
                    var batchFilter = new BatchFilter(filterNode, nextBatchID);
                    
                    if (batchFilter.propName != null)
                    {
                        if (x_propNameToFilter.ContainsKey(batchFilter.propName))
                        {
                            JUtil.LogErrorMessage(null, "PROP_BATCH: Tried to add prop {0} to multiple filters", batchFilter.propName);
                        }
                        else
                        {
                            x_propNameToFilter.Add(batchFilter.propName, batchFilter);
                        }
                    }
                }

                ++nextBatchID;
            }
        }

        static BatchFilter GetBatchFilterForProp(InternalProp prop)
        {
            if (!prop.hasModel) return null;

            // currently, we only support matching by propName but that will change eventually
            if (x_propNameToFilter.TryGetValue(prop.propName, out var batchFilter))
            {
                return batchFilter;
            }

            return null;
        }

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING) return;

            var newProps = new List<InternalProp>(internalModel.props.Count);
            var batchRoots = new Dictionary<int, Transform>();

            foreach(var oldProp in internalModel.props)
            {
                bool keepProp = true;
                var batchFilter = GetBatchFilterForProp(oldProp);
                if (batchFilter != null)
                {
                    var childTransform = JUtil.FindPropTransform(oldProp, batchFilter.transformName);
                    if (childTransform != null)
                    {
                        if (batchRoots.TryGetValue(batchFilter.batchID, out var batchRoot))
                        {
                            keepProp = batchFilter.keepProp;
                        }
                        else
                        {
                            childTransform.SetParent(null, true); // detach early so the transform change below doesn't move it
                            oldProp.transform.localPosition = Vector3.zero;
                            oldProp.transform.localRotation = Quaternion.identity;
                            oldProp.transform.localScale = Vector3.one;
                            batchRoot = new GameObject("batchRoot").transform;
                            batchRoot.SetParent(oldProp.transform, false);
                            batchRoots[batchFilter.batchID] = batchRoot;
                        }

                        childTransform.SetParent(batchRoot, true);
                    }
                    else
                    {
                        JUtil.LogErrorMessage(null, "PROP_BATCH: Could not find transform named {0} in prop {1}", batchFilter.transformName, oldProp.propName);
                    }
                }

                if (keepProp)
                {
                    oldProp.propID = newProps.Count;
                    newProps.Add(oldProp);
                }
                else
                {
                    GameObject.Destroy(oldProp.gameObject);
                }
            }

            if (batchRoots.Count > 0)
            {
                foreach (var batchRoot in batchRoots.Values)
                {
                    StaticBatchingUtility.Combine(batchRoot.gameObject);
                    JUtil.LogMessage(null, "PROP_BATCH: batching {0} transforms under {1}", batchRoot.childCount, batchRoot.parent.name);
                }

                JUtil.LogMessage(null, "PROP_BATCH: old prop count: {0}; new prop count: {1}; delta {2}", internalModel.props.Count, newProps.Count, internalModel.props.Count - newProps.Count);
                internalModel.props = newProps;
            }
        }
    }
}
