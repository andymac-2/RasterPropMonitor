using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace JSI.Auxiliary_modules
{
    class BatchDefinition
    {
        static List<BatchDefinition> x_batchDefinitions = null;

        static void LoadBatchDefinitions()
        {
            var batchNodes = GameDatabase.Instance.GetConfigNodes("PROP_BATCH");
            x_batchDefinitions = new List<BatchDefinition>(batchNodes.Length);
            foreach (var batchNode in batchNodes)
            {
                x_batchDefinitions.Add(new BatchDefinition(batchNode));
            }
        }

        public static BatchDefinition FindBatchDefinitionForProp(InternalProp prop)
        {
            if (x_batchDefinitions == null) LoadBatchDefinitions();

            foreach (var batchDefinition in x_batchDefinitions)
            {
                if (batchDefinition.propName == prop.propName)
                {
                    return batchDefinition;
                }
            }

            return null;
        }

        BatchDefinition(ConfigNode node)
        {
            propName = node.GetValue(nameof(propName));
            transformName = node.GetValue(nameof(transformName));
        }

        public string propName;
        public string transformName;
    }


    public class PropBatcher : InternalModule
    {
        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING) return;

            var newProps = new List<InternalProp>(internalModel.props.Count);

            InternalProp firstProp = null;
            Transform batchRoot = null;

            foreach(var oldProp in internalModel.props)
            {
                bool keepProp = true;
                var batchDefinition = BatchDefinition.FindBatchDefinitionForProp(oldProp);
                if (batchDefinition != null)
                {
                    if (firstProp == null)
                    {
                        firstProp = oldProp;
                        firstProp.transform.localPosition = Vector3.zero;
                        firstProp.transform.localRotation = Quaternion.identity;
                        firstProp.transform.localScale = Vector3.one;
                        batchRoot = new GameObject("batchRoot").transform;
                        batchRoot.SetParent(firstProp.transform, false);
                    }
                    else
                    {
                        keepProp = false;
                    }

                    var childTransform = JUtil.FindPropTransform(oldProp, batchDefinition.transformName);
                    if (childTransform != null)
                    {
                        childTransform.SetParent(batchRoot, true);
                    }
                }

                if (keepProp)
                {
                    oldProp.propID = newProps.Count;
                    newProps.Add(oldProp);
                }
                else
                {
                    oldProp.transform.SetParent(null, false);
                }
            }

            if (batchRoot != null)
            {
                StaticBatchingUtility.Combine(batchRoot.gameObject);
            }

            internalModel.props = newProps;
        }
    }
}
