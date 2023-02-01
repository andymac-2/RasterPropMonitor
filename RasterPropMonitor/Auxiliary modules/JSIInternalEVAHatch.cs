using KSP.Localization;
using UnityEngine;

namespace JSI
{
    public class JSIInternalEVAHatch : InternalModule
    {
        [KSPField]
        public string hatchTransform = string.Empty;
        [KSPField]
        public string internalAnimation = string.Empty;
        private Kerbal activeKerbal;
        private Animation intAnim;
        private bool intAnimStarted;

        public void Start()
        {
            if (string.IsNullOrEmpty(hatchTransform))
            {
                JUtil.LogMessage(this, "Where's my transform?");
                return;
            }
            Transform actualTransform;
            if (internalProp == null)
            {
                actualTransform = internalModel.FindModelTransform(hatchTransform);
                if (!string.IsNullOrEmpty(internalAnimation))
                {
                    intAnim = internalModel.FindModelAnimators(internalAnimation)[0];
                }
            }
            else
            {
                actualTransform = internalProp.FindModelTransform(hatchTransform);
                if (!string.IsNullOrEmpty(internalAnimation))
                {
                    intAnim = internalProp.FindModelAnimators(internalAnimation)[0];
                }
            }
            if (!string.IsNullOrEmpty(internalAnimation) && intAnim == null)
                JUtil.LogErrorMessage(this, "Animation name was not found.");
            // Switching to using the stock button class because right now SmarterButton can't correctly handle doubleclick.
            InternalButton.Create(actualTransform.gameObject).OnDoubleTap(new InternalButton.InternalButtonDelegate(EVAClick));

        }

        // Note: this function is from FreeIva
        KerbalEVA SpawnEVA(ProtoCrewMember pCrew, Part airlockPart, Transform fromAirlock)
        {
            var flightEVA = FlightEVA.fetch;

            Part crewPart = pCrew.KerbalRef.InPart;

            if (FlightEVA.HatchIsObstructed(part, fromAirlock)) // NOTE: stock code also checks hatchInsideFairing
            {
                ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_111978"), 5f, ScreenMessageStyle.UPPER_CENTER);
                return null;
            }
            flightEVA.overrideEVA = false;
            GameEvents.onAttemptEva.Fire(pCrew, crewPart, fromAirlock);
            if (flightEVA.overrideEVA)
            {
                return null;
            }

            // at this point we're *definitely* going EVA
            // manipulate the crew assignments to make this work.
            if (crewPart != airlockPart)
            {
                crewPart.RemoveCrewmember(pCrew);

                ++airlockPart.CrewCapacity;
                airlockPart.AddCrewmember(pCrew);
                pCrew.KerbalRef.InPart = airlockPart;
                --airlockPart.CrewCapacity;
            }

            flightEVA.pCrew = pCrew;
            flightEVA.fromPart = airlockPart;
            flightEVA.fromAirlock = fromAirlock;
            return flightEVA.onGoForEVA();
        }

        private void GoEva()
        {
            if (activeKerbal != null && part.airlock != null)
            {
                //FlightEVA.SpawnEVA(activeKerbal);
                SpawnEVA(activeKerbal.protoCrewMember, part, part.airlock);
                CameraManager.Instance.SetCameraFlight();
                activeKerbal = null;
            }
        }
        // ..I don't feel like using coroutines.
        public override void OnUpdate()
        {
            if (intAnimStarted)
            {
                if (!intAnim.isPlaying)
                {
                    // The animation completed, so we kick the kerbal out now.
                    intAnimStarted = false;
                    GoEva();
                    // And immediately reset the animation.
                    intAnim[internalAnimation].normalizedTime = 0;
                    intAnim.Stop();
                }
            }
        }

        public void EVAClick()
        {
            Kerbal thatKerbal = CameraManager.Instance.IVACameraActiveKerbal;

            float acLevel = ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex);
            bool evaUnlocked = GameVariables.Instance.UnlockedEVA(acLevel);
            bool evaPossible = GameVariables.Instance.EVAIsPossible(evaUnlocked, vessel);
            if (evaPossible && thatKerbal != null && HighLogic.CurrentGame.Parameters.Flight.CanEVA)
            {
                if (thatKerbal.protoCrewMember.type != ProtoCrewMember.KerbalType.Tourist)
                {
                    activeKerbal = thatKerbal;
                    if (intAnim != null)
                    {
                        intAnim.enabled = true;
                        intAnim[internalAnimation].speed = 1;
                        intAnim.Play();
                        intAnimStarted = true;
                    }
                    else
                    {
                        GoEva();
                    }
                    JUtil.LogMessage(this, "{0} has opened the internal EVA hatch.", thatKerbal.name);
                }
                else
                {
                    JUtil.LogMessage(this, "{0}, a tourist, tried to open the EVA hatch.", thatKerbal.name);
                }
            }
            else
            {
                if (evaPossible)
                {
                    JUtil.LogMessage(this, "Could not open the internal EVA hatch, not sure why.");
                }
                else
                {
                    JUtil.LogMessage(this, "EVA hatches can not be opened - is the astronaut complex upgraded yet?");
                }
            }
        }
    }
}

