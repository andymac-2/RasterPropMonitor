using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSI
{
	class ElectricalSystem
	{
		public bool generatorsActive { get; private set; } // Returns true if at least one generator or fuel cell is active that can be otherwise switched off
		public bool solarPanelsDeployable { get; private set; }
		public bool solarPanelsRetractable { get; private set; }
		public bool solarPanelsState { get; private set; } // Returns false if the solar panels are extendable or are retracting
		public int solarPanelMovement { get; private set; }
		public float alternatorOutput { get; private set; }
		public float fuelcellOutput { get; private set; }
		public float generatorOutput { get; private set; }
		public float solarOutput { get; private set; }

		internal List<ModuleAlternator> availableAlternators = new List<ModuleAlternator>();
		internal List<ModuleResourceConverter> availableFuelCells = new List<ModuleResourceConverter>();
		internal List<float> availableFuelCellOutput = new List<float>();
		internal List<ModuleGenerator> availableGenerators = new List<ModuleGenerator>();
		internal List<float> availableGeneratorOutput = new List<float>();
		internal List<ModuleDeployableSolarPanel> availableSolarPanels = new List<ModuleDeployableSolarPanel>();

		private Vessel vessel;

		public ElectricalSystem(Vessel vessel)
		{
			this.vessel = vessel;
		}

		public virtual void Clear()
		{
			availableAlternators.Clear();
			availableFuelCells.Clear();
			availableFuelCellOutput.Clear();
			availableGenerators.Clear();
			availableGeneratorOutput.Clear();
			availableSolarPanels.Clear();
		}

		public virtual bool ConsiderModule(PartModule module)
		{
			if (module is ModuleAlternator)
			{
				ModuleAlternator alt = module as ModuleAlternator;
				for (int i = 0; i < alt.resHandler.outputResources.Count; ++i)
				{
					if (alt.resHandler.outputResources[i].name == "ElectricCharge")
					{
						availableAlternators.Add(alt);
						break;
					}
				}
			}
			else if (module is ModuleGenerator)
			{
				ModuleGenerator gen = module as ModuleGenerator;
				for (int i = 0; i < gen.resHandler.outputResources.Count; ++i)
				{
					if (gen.resHandler.outputResources[i].name == "ElectricCharge")
					{
						availableGenerators.Add(gen);
						availableGeneratorOutput.Add((float)gen.resHandler.outputResources[i].rate);
						break;
					}
				}
			}
			else if (module is ModuleResourceConverter)
			{
				ModuleResourceConverter gen = module as ModuleResourceConverter;
				ConversionRecipe recipe = gen.Recipe;
				for (int i = 0; i < recipe.Outputs.Count; ++i)
				{
					if (recipe.Outputs[i].ResourceName == "ElectricCharge")
					{
						availableFuelCells.Add(gen);
						availableFuelCellOutput.Add((float)recipe.Outputs[i].Ratio);
						break;
					}
				}
			}
			else if (module is ModuleDeployableSolarPanel)
			{
				ModuleDeployableSolarPanel sp = module as ModuleDeployableSolarPanel;

				if (sp.resourceName == "ElectricCharge")
				{
					availableSolarPanels.Add(sp);
				}
			}
			else
			{
				return false;
			}

			return true;
		}

		public virtual void Update()
		{
			if (availableAlternators.Count > 0)
			{
				alternatorOutput = 0.0f;
			}
			else
			{
				alternatorOutput = -1.0f;
			}
			if (availableFuelCells.Count > 0)
			{
				fuelcellOutput = 0.0f;
			}
			else
			{
				fuelcellOutput = -1.0f;
			}
			if (availableGenerators.Count > 0)
			{
				generatorOutput = 0.0f;
			}
			else
			{
				generatorOutput = -1.0f;
			}
			if (availableSolarPanels.Count > 0)
			{
				solarOutput = 0.0f;
			}
			else
			{
				solarOutput = -1.0f;
			}

			generatorsActive = false;
			solarPanelsDeployable = solarPanelsRetractable = solarPanelsState = false;
			solarPanelMovement = -1;

			for (int i = 0; i < availableGenerators.Count; ++i)
			{
				generatorsActive |= (availableGenerators[i].generatorIsActive && !availableGenerators[i].isAlwaysActive);

				if (availableGenerators[i].generatorIsActive)
				{
					float output = availableGenerators[i].efficiency * availableGeneratorOutput[i];
					if (availableGenerators[i].isThrottleControlled)
					{
						output *= availableGenerators[i].throttle;
					}
					generatorOutput += output;
				}
			}

			for (int i = 0; i < availableFuelCells.Count; ++i)
			{
				generatorsActive |= (availableFuelCells[i].IsActivated && !availableFuelCells[i].AlwaysActive);

				if (availableFuelCells[i].IsActivated)
				{
					fuelcellOutput += (float)availableFuelCells[i].lastTimeFactor * availableFuelCellOutput[i];
				}
			}

			for (int i = 0; i < availableAlternators.Count; ++i)
			{
				// I assume there's only one ElectricCharge output in a given ModuleAlternator
				alternatorOutput += availableAlternators[i].outputRate;
			}

			for (int i = 0; i < availableSolarPanels.Count; ++i)
			{
				solarOutput += availableSolarPanels[i].flowRate;
				solarPanelsRetractable |= (availableSolarPanels[i].useAnimation && availableSolarPanels[i].retractable && availableSolarPanels[i].deployState == ModuleDeployableSolarPanel.DeployState.EXTENDED);
				solarPanelsDeployable |= (availableSolarPanels[i].useAnimation && availableSolarPanels[i].deployState == ModuleDeployableSolarPanel.DeployState.RETRACTED);
				solarPanelsState |= (availableSolarPanels[i].useAnimation && (availableSolarPanels[i].deployState == ModuleDeployableSolarPanel.DeployState.EXTENDED || availableSolarPanels[i].deployState == ModuleDeployableSolarPanel.DeployState.EXTENDING));

				if ((solarPanelMovement == -1 || solarPanelMovement == (int)ModuleDeployableSolarPanel.DeployState.BROKEN) && availableSolarPanels[i].useAnimation)
				{
					solarPanelMovement = (int)availableSolarPanels[i].deployState;
				}
			}
		}

		/// <summary>
		/// Toggle the state of any generators or resource converters that can
		/// be toggled.
		/// </summary>
		/// <param name="state"></param>
		internal void SetEnableGenerators(bool state)
		{
			for (int i = 0; i < availableGenerators.Count; ++i)
			{
				if (!availableGenerators[i].isAlwaysActive)
				{
					if (state)
					{
						availableGenerators[i].Activate();
					}
					else
					{
						availableGenerators[i].Shutdown();
					}
				}
			}

			for (int i = 0; i < availableFuelCells.Count; ++i)
			{
				if (!availableFuelCells[i].AlwaysActive)
				{
					if (state)
					{
						availableFuelCells[i].StartResourceConverter();
					}
					else
					{
						availableFuelCells[i].StopResourceConverter();
					}
				}
			}
		}

		/// <summary>
		/// Deploy and retract (where applicable) deployable solar panels.
		/// </summary>
		/// <param name="state"></param>
		internal void SetDeploySolarPanels(bool state)
		{
			if (state)
			{
				for (int i = 0; i < availableSolarPanels.Count; ++i)
				{
					if (availableSolarPanels[i].useAnimation && availableSolarPanels[i].deployState == ModuleDeployablePart.DeployState.RETRACTED)
					{
						availableSolarPanels[i].Extend();
					}
				}
			}
			else
			{
				for (int i = 0; i < availableSolarPanels.Count; ++i)
				{
					if (availableSolarPanels[i].useAnimation && availableSolarPanels[i].retractable && availableSolarPanels[i].deployState == ModuleDeployablePart.DeployState.EXTENDED)
					{
						availableSolarPanels[i].Retract();
					}
				}
			}
		}
	}
}
