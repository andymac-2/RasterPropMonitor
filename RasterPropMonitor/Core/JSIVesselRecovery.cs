using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace JSI.Core
{
	// To avoid some weird bugs, OnVesselRecoveryRequested must be called from LateUpdate, not Update where most props are executed.
	// To avoid constantly checking whether we need to recover every LateUpdate, we just create this simple behavior class which
	// updates once and deletes itself.
	// to reproduce the original bug: activate an IVA, then ProbeControlRoom, then go back to the IVA (i.e. hit C 4 times) and then click a recovery prop
	// the debug log will be spammed with NREs, the game will become unsaveable which then locks you out from many buildings and even returning to the main menu.
	class JSIVesselRecovery : MonoBehaviour
	{
		public void LateUpdate()
		{

			JUtil.LogMessage(this, "Attempting vessel recovery");
			GameEvents.OnVesselRecoveryRequested.Fire(VesselToRecover);
			GameObject.Destroy(this.gameObject);
		}

		public global::Vessel VesselToRecover;

		public static void Recover(global::Vessel vessel)
		{
			var gameObject = GameObject.Instantiate(new GameObject("JSIVesselRecovery", typeof(JSIVesselRecovery)));
			gameObject.GetComponent<JSIVesselRecovery>().VesselToRecover = vessel;
		}
	}
}
