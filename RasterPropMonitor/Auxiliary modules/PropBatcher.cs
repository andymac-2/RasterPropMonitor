using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;

namespace JSI.Auxiliary_modules
{
    public class PropBatcher : InternalModule
    {
        static Dictionary<string, BatchFilter> x_propNameToFilter = new Dictionary<string, BatchFilter>();
        static Dictionary<string, BatchFilter> x_modelNameToFilter = new Dictionary<string, BatchFilter>();

        class BatchFilter
        {
            public BatchFilter(ConfigNode node, int batchID)
            {
                this.batchID = batchID;
                transformName = node.GetValue(nameof(transformName));
                node.TryGetValue(nameof(keepProp), ref keepProp);

                string propName = node.GetValue(nameof(propName));
                if (propName != null && !TryAdd(x_propNameToFilter, propName, this))
                {
                    JUtil.LogErrorMessage(null, "PROP_BATCH: Tried to add prop {0} to multiple batches", propName);
                }

                // models added with a MODEL node get their full path plus "(Clone)" as a top-level child of the "model" child of the prop
                string modelPath = node.GetValue(nameof(modelPath));
                if (modelPath != null && !TryAdd(x_modelNameToFilter, modelPath + "(Clone)", this))
                {
                    JUtil.LogErrorMessage(null, "PROP_BATCH: Tried to add modelPath {0} to multiple batches", modelPath);
                }

                // models that are implicitly added to props by virtue of being in the same directory are just directly added as children of the "model" node
                string modelName = node.GetValue(nameof(modelName));
                if (modelName != null &&  !TryAdd(x_modelNameToFilter, modelName, this))
                {
                    JUtil.LogErrorMessage(null, "PROP_BATCH: Tried to add modelName {0} to multiple batches", modelName);
                }
            }

            public readonly int batchID;
            public readonly string transformName;
            public readonly bool keepProp = true;
        }

        static bool TryAdd(Dictionary<string, BatchFilter> dict, string key, BatchFilter value)
        {
            if (dict.ContainsKey(key))
            {
                return false;
            }
            dict.Add(key, value);
            return true;
        }

        public static void ModuleManagerPostLoad()
        {
            x_propNameToFilter.Clear();
            x_modelNameToFilter.Clear();

            var batchNodes = GameDatabase.Instance.GetConfigNodes("PROP_BATCH");
            int nextBatchID = 0;
            foreach (var batchNode in batchNodes)
            {
                foreach (var filterNode in batchNode.GetNodes("FILTER"))
                {
                    new BatchFilter(filterNode, nextBatchID);
                }

                ++nextBatchID;
            }
        }

        static IEnumerable<BatchFilter> GetBatchFilterForProp(InternalProp prop)
        {
            if (!prop.hasModel) yield break;

            if (x_propNameToFilter.TryGetValue(prop.propName, out var batchFilter))
            {
                yield return batchFilter;
            }

            var modelRoot = prop.transform.Find("model");
            for (int childIndex = 0; childIndex < modelRoot.childCount; ++childIndex)
            {
                var model = modelRoot.GetChild(childIndex);
                if (x_modelNameToFilter.TryGetValue(model.name, out batchFilter))
                {
                    yield return batchFilter;
                }
            }
        }

        void AttachChildrenRecursively(Transform newParent, Transform child)
        {
            child.SetParent(newParent, true);
            for (int i = 0; i < child.childCount; i++)
            {
                AttachChildrenRecursively(newParent, child.GetChild(i));
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING) return;

            var newProps = new List<InternalProp>(internalModel.props.Count);
            var batchLists = new Dictionary<int, List<GameObject>>();

            foreach(var oldProp in internalModel.props)
            {
                bool keepProp = true;
                foreach (var batchFilter in GetBatchFilterForProp(oldProp))
                {
                    var childTransform = JUtil.FindPropTransform(oldProp, batchFilter.transformName);
                    if (childTransform != null)
                    {
                        if (batchLists.TryGetValue(batchFilter.batchID, out var batchList))
                        {
                            keepProp = batchFilter.keepProp;

                            if (!keepProp)
                            {
                                childTransform.SetParent(batchList[0].transform.parent, true);
                            }
                        }
                        else
                        {
                            batchList = new List<GameObject>();
                            batchLists[batchFilter.batchID] = batchList;

                            // if we're not keeping the other props, we need to attach all of their models to the first one
                            if (!batchFilter.keepProp)
                            {
                                childTransform.SetParent(null, true); // detach early so the transform change below doesn't move it
                                oldProp.transform.localPosition = Vector3.zero;
                                oldProp.transform.localRotation = Quaternion.identity;
                                oldProp.transform.localScale = Vector3.one;
                                var batchRoot = new GameObject(oldProp.propName + " batchRoot").transform;
                                batchRoot.SetParent(oldProp.transform, false);
                                childTransform.SetParent(batchRoot.transform, true);
                            }
                        }

                        batchList.Add(childTransform.gameObject);
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

            if (batchLists.Count > 0)
            {
                int batchedTransforms = 0;
                foreach (var batchList in batchLists.Values)
                {
                    // make sure everything is using the same material
                    var sharedMaterial = batchList[0].GetComponent<MeshRenderer>().sharedMaterial;
                    foreach (var child in  batchList)
                    {
                        child.GetComponent<MeshRenderer>().material = sharedMaterial;
                    }

                    batchedTransforms += batchList.Count;
                    StaticBatchingUtility.Combine(batchList.ToArray(), batchList[0].transform.parent.gameObject);
                    JUtil.LogMessage(null, "PROP_BATCH: batching {0} transforms under {1}", batchList.Count, batchList[0].transform.parent.name);
                }

                JUtil.LogMessage(null, "PROP_BATCH: old prop count: {0}; new prop count: {1}; delta {2}; total batched transforms {3}", internalModel.props.Count, newProps.Count, internalModel.props.Count - newProps.Count, batchedTransforms);
                internalModel.props = newProps;
            }
        }
    }
}
