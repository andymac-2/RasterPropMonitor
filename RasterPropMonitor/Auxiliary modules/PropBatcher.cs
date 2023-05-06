using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace JSI.Auxiliary_modules
{
    public class PropBatcher : InternalModule
    {
        [KSPField] public string propName = string.Empty; // probably should be a model path
        [KSPField] public string transformName = string.Empty;

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING) return;

            var newProps = new List<InternalProp>(internalModel.props.Count);

            InternalProp firstProp = null;
            Transform batchRoot = null;

            foreach(var oldProp in internalModel.props)
            {
                bool keepProp = true;
                if (oldProp.propName == propName)
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

                    var childTransform = JUtil.FindPropTransform(oldProp, transformName);
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
