using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;

namespace JSI
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
                                batchRoot.gameObject.layer = 20;
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
                    CombineInstance[] instances = new CombineInstance[batchList.Count];
                    var worldToLocal = batchList[0].transform.worldToLocalMatrix;
                    
                    for (int i = 0; i < batchList.Count; i++)
                    {
                        var instance = new CombineInstance();
                        instance.mesh = batchList[i].GetComponent<MeshFilter>().mesh;
                        instance.transform = worldToLocal * batchList[i].transform.localToWorldMatrix;
                        
                        if (i > 0)
                        {
                            Component.Destroy(batchList[i].GetComponent<MeshRenderer>());
                            Component.Destroy(batchList[i].GetComponent<MeshFilter>());
                        }

                        instances[i] = instance;
                    }

                    batchedTransforms += batchList.Count;
                    
                    Mesh mesh = new Mesh();
                    mesh.CombineMeshes(instances);
                    batchList[0].GetComponent<MeshFilter>().sharedMesh = mesh;

                    JUtil.LogMessage(null, "PROP_BATCH: batching {0} transforms under {1}", batchList.Count, batchList[0].transform.parent.name);
                }

                JUtil.LogMessage(null, "PROP_BATCH: old prop count: {0}; new prop count: {1}; delta {2}; total batched transforms {3}", internalModel.props.Count, newProps.Count, internalModel.props.Count - newProps.Count, batchedTransforms);
                internalModel.props = newProps;
            }
        }

        // Text label batching:
        // A large number of JSILabel modules are completely static - things like labels on switches, buttons, etc.
        // But these can still sometimes change color, especially when the backlight is turned on.
        // For all the JSILabels with the same controlling variable and color settings, we can render them all at once in a single mesh.

        class LabelBatch
        {
            public JSILabel firstLabel;
            public List<JSITextMesh> textMeshes = new List<JSITextMesh>();
            public bool needsUpdate = true;
        }

        Dictionary<JSILabel.TextBatchInfo, LabelBatch> labelBatches = new Dictionary<JSILabel.TextBatchInfo, LabelBatch>();

        public void AddStaticLabel(JSILabel label)
        {
            LabelBatch labelBatch;
            if (!labelBatches.TryGetValue(label.batchInfo, out labelBatch))
            {
                labelBatch = new LabelBatch();
                labelBatch.firstLabel = label;
                labelBatches.Add(label.batchInfo, labelBatch);

                var oldParent = label.textObj.transform.parent;
                label.textObj.transform.SetParent(null, true);
                label.transform.localPosition = Vector3.zero;
                label.transform.localRotation = Quaternion.identity;
                label.transform.localScale = Vector3.one;
                label.textObj.transform.SetParent(oldParent, true);
            }
            else
            {
                Component.Destroy(label.textObj.transform.GetComponent<MeshRenderer>());
                // TODO: destroy meshfilter?
            }

            labelBatch.textMeshes.Add(label.textObj);
            labelBatch.needsUpdate = true;
        }

        void LateUpdate()
        {
            foreach (var labelBatch in labelBatches.Values)
            {
                if (labelBatch.needsUpdate)
                {
                    CombineInstance[] instances = new CombineInstance[labelBatch.textMeshes.Count];
                    for (int i = 0; i < instances.Length; ++i)
                    {
                        instances[i] = new CombineInstance();
                        instances[i].mesh = labelBatch.textMeshes[i].mesh;
                        instances[i].transform = labelBatch.textMeshes[i].transform.localToWorldMatrix;
                    }

                    labelBatch.needsUpdate = false;
                }
            }
        }
    }
}
